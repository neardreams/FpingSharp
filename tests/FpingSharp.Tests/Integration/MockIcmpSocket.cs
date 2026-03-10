using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using FpingSharp.Internal;

namespace FpingSharp.Tests.Integration
{
    /// <summary>
    /// Mock ICMP socket that simulates replies for testing PingEngine without network access.
    /// </summary>
    internal class MockIcmpSocket : IcmpSocket
    {
        private readonly Queue<(byte[] Data, int Length)> _pendingReplies = new();
        private readonly List<(byte[] Data, int Length, EndPoint Dest)> _sentPackets = new();

        // Configuration
        public TimeSpan SimulatedLatency { get; set; } = TimeSpan.FromMilliseconds(1);
        public bool SimulateTimeout { get; set; }
        public HashSet<int> TimeoutSequences { get; } = new(); // specific seqs to timeout
        public int ReplyDelayCallCount { get; set; } // after how many Poll calls replies appear

        private int _pollCount;

        public IReadOnlyList<(byte[] Data, int Length, EndPoint Dest)> SentPackets => _sentPackets;

        public MockIcmpSocket(bool isRaw = false)
        {
            _isRaw = isRaw;
            // Don't call CreateSocket - we're mocking
        }

        protected override void CreateSocket()
        {
            // No-op for mock
        }

        // Override SendTo to capture packets and auto-generate replies
        public override int SendTo(byte[] buffer, int length, EndPoint remoteEP)
        {
            var copy = new byte[length];
            Buffer.BlockCopy(buffer, 0, copy, 0, length);
            _sentPackets.Add((copy, length, remoteEP));

            if (!SimulateTimeout)
            {
                // Parse the echo request to get id and seq
                // For DGRAM (non-raw), packet starts at offset 0
                if (length >= IcmpPacket.HeaderSize)
                {
                    byte type = copy[0];
                    ushort id = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(4));
                    ushort seq = BinaryPrimitives.ReadUInt16BigEndian(copy.AsSpan(6));

                    if (!TimeoutSequences.Contains(seq))
                    {
                        // Build echo reply
                        var reply = new byte[length];
                        Buffer.BlockCopy(copy, 0, reply, 0, length);
                        reply[0] = IcmpPacket.EchoReply; // type = Echo Reply
                        reply[1] = 0; // code
                        // Recalculate checksum
                        reply[2] = 0;
                        reply[3] = 0;
                        ushort checksum = IcmpChecksum.Compute(reply, 0, length);
                        reply[2] = (byte)(checksum >> 8);
                        reply[3] = (byte)(checksum & 0xFF);

                        _pendingReplies.Enqueue((reply, length));
                    }
                }
            }

            return length;
        }

        // Override Poll to check pending replies
        public override bool Poll(int microSeconds)
        {
            _pollCount++;
            if (_pollCount <= ReplyDelayCallCount) return false;
            return _pendingReplies.Count > 0;
        }

        // Override ReceiveFrom to return mock reply
        public override int ReceiveFrom(byte[] buffer, ref EndPoint remoteEP)
        {
            if (_pendingReplies.Count == 0)
                throw new SocketException((int)SocketError.WouldBlock);

            var (data, length) = _pendingReplies.Dequeue();
            Buffer.BlockCopy(data, 0, buffer, 0, length);
            remoteEP = new IPEndPoint(IPAddress.Loopback, 0);
            return length;
        }

        public void Reset()
        {
            _pendingReplies.Clear();
            _sentPackets.Clear();
            _pollCount = 0;
        }
    }
}
