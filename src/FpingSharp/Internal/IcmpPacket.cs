using System;
using System.Buffers.Binary;

namespace FpingSharp.Internal
{
    internal static class IcmpPacket
    {
        // ICMP types
        public const byte EchoReply = 0;
        public const byte DestinationUnreachable = 3;
        public const byte EchoRequest = 8;
        public const byte TimeExceeded = 11;

        // ICMPv6 types
        public const byte Icmpv6EchoRequest = 128;
        public const byte Icmpv6EchoReply = 129;
        public const byte Icmpv6DestUnreachable = 1;
        public const byte Icmpv6TimeExceeded = 3;

        // ICMP header is 8 bytes: type(1) + code(1) + checksum(2) + id(2) + seq(2)
        public const int HeaderSize = 8;

        /// <summary>
        /// Build an ICMP Echo Request packet.
        /// buffer must be at least HeaderSize + payloadSize bytes.
        /// Returns total packet length.
        /// </summary>
        public static int BuildEchoRequest(byte[] buffer, ushort id, ushort seq, int payloadSize, bool isIpv6)
        {
            int totalLen = HeaderSize + payloadSize;

            buffer[0] = isIpv6 ? Icmpv6EchoRequest : EchoRequest; // type
            buffer[1] = 0; // code
            buffer[2] = 0; // checksum (zero for calculation)
            buffer[3] = 0;
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), id);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6), seq);

            // Fill payload with index pattern (like fping does)
            for (int i = 0; i < payloadSize; i++)
            {
                buffer[HeaderSize + i] = (byte)(i & 0xFF);
            }

            // IPv4: compute checksum. IPv6: kernel computes it
            if (!isIpv6)
            {
                ushort checksum = IcmpChecksum.Compute(buffer, 0, totalLen);
                // Store checksum in network byte order
                buffer[2] = (byte)(checksum >> 8);
                buffer[3] = (byte)(checksum & 0xFF);
            }

            return totalLen;
        }

        /// <summary>
        /// Result of parsing an ICMP packet.
        /// </summary>
        internal readonly struct ParseResult
        {
            public readonly byte Type;
            public readonly byte Code;
            public readonly ushort Id;
            public readonly ushort Seq;
            public readonly bool IsEmbedded; // true if this was extracted from an error message

            public ParseResult(byte type, byte code, ushort id, ushort seq, bool isEmbedded)
            {
                Type = type;
                Code = code;
                Id = id;
                Seq = seq;
                IsEmbedded = isEmbedded;
            }

            public bool IsEchoReply => Type == EchoReply || Type == Icmpv6EchoReply;
            public bool IsError => Type == DestinationUnreachable || Type == TimeExceeded ||
                                   Type == Icmpv6DestUnreachable || Type == Icmpv6TimeExceeded;
        }

        /// <summary>
        /// Parse an ICMP packet received from a RAW socket (IPv4).
        /// Buffer includes the IP header which must be skipped.
        /// Returns null if packet is too short or not relevant.
        /// </summary>
        public static ParseResult? ParseRawV4(byte[] buffer, int length, ushort expectedId)
        {
            if (length < 20) return null; // minimum IP header

            // IP header length from IHL field
            int ipHeaderLen = (buffer[0] & 0x0F) * 4;
            if (length < ipHeaderLen + HeaderSize) return null;

            int icmpOffset = ipHeaderLen;
            byte type = buffer[icmpOffset];
            byte code = buffer[icmpOffset + 1];
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(icmpOffset + 4));
            ushort seq = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(icmpOffset + 6));

            if (type == EchoReply)
            {
                if (id != expectedId) return null;
                return new ParseResult(type, code, id, seq, false);
            }

            if (type == DestinationUnreachable || type == TimeExceeded)
            {
                // Error messages embed the original IP+ICMP header after the 8-byte ICMP header
                int embeddedOffset = icmpOffset + HeaderSize;
                if (length < embeddedOffset + 20 + HeaderSize) return null; // need embedded IP + ICMP header

                int embeddedIpHeaderLen = (buffer[embeddedOffset] & 0x0F) * 4;
                int embeddedIcmpOffset = embeddedOffset + embeddedIpHeaderLen;
                if (length < embeddedIcmpOffset + HeaderSize) return null;

                byte embType = buffer[embeddedIcmpOffset];
                if (embType != EchoRequest) return null;

                ushort embId = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(embeddedIcmpOffset + 4));
                ushort embSeq = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(embeddedIcmpOffset + 6));

                if (embId != expectedId) return null;
                return new ParseResult(type, code, embId, embSeq, true);
            }

            return null;
        }

        /// <summary>
        /// Parse an ICMP packet received from a DGRAM socket (no IP header).
        /// </summary>
        /// <summary>
        /// Parse an ICMP packet received from a DGRAM socket (no IP header).
        /// On Linux DGRAM ICMP sockets, the kernel manages the ICMP ID field and
        /// only delivers replies matching the socket's internal ident, so we skip
        /// the ID check entirely — the kernel already filtered for us.
        /// </summary>
        public static ParseResult? ParseDgramV4(byte[] buffer, int length, ushort expectedId)
        {
            if (length < HeaderSize) return null;

            byte type = buffer[0];
            byte code = buffer[1];
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4));
            ushort seq = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6));

            if (type == EchoReply)
            {
                // No ID check for DGRAM — kernel already filtered by socket ident
                return new ParseResult(type, code, id, seq, false);
            }

            if (type == DestinationUnreachable || type == TimeExceeded)
            {
                // DGRAM: embedded packet after the 8-byte ICMP error header
                // The embedded packet starts with IP header
                int embeddedOffset = HeaderSize;
                if (length < embeddedOffset + 20 + HeaderSize) return null;

                int embeddedIpHeaderLen = (buffer[embeddedOffset] & 0x0F) * 4;
                int embeddedIcmpOffset = embeddedOffset + embeddedIpHeaderLen;
                if (length < embeddedIcmpOffset + HeaderSize) return null;

                byte embType = buffer[embeddedIcmpOffset];
                if (embType != EchoRequest) return null;

                ushort embSeq = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(embeddedIcmpOffset + 6));

                // No ID check for DGRAM
                return new ParseResult(type, code, id, embSeq, true);
            }

            return null;
        }

        /// <summary>
        /// Parse an ICMPv6 packet (no IP header in both RAW and DGRAM for IPv6).
        /// </summary>
        public static ParseResult? ParseV6(byte[] buffer, int length, ushort expectedId)
        {
            if (length < HeaderSize) return null;

            byte type = buffer[0];
            byte code = buffer[1];
            ushort id = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4));
            ushort seq = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6));

            if (type == Icmpv6EchoReply)
            {
                if (id != expectedId) return null;
                return new ParseResult(type, code, id, seq, false);
            }

            if (type == Icmpv6DestUnreachable || type == Icmpv6TimeExceeded)
            {
                // Embedded ICMPv6 starts after 8-byte error header + 40-byte IPv6 header
                int embeddedOffset = HeaderSize + 40;
                if (length < embeddedOffset + HeaderSize) return null;

                byte embType = buffer[embeddedOffset];
                if (embType != Icmpv6EchoRequest) return null;

                ushort embId = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(embeddedOffset + 4));
                ushort embSeq = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(embeddedOffset + 6));

                if (embId != expectedId) return null;
                return new ParseResult(type, code, embId, embSeq, true);
            }

            return null;
        }
    }
}
