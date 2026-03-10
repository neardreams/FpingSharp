using System.Buffers.Binary;
using FpingSharp.Internal;
using Xunit;

namespace FpingSharp.Tests.Unit;

public class IcmpPacketTests
{
    [Fact]
    public void Test_BuildEchoRequest_V4_CorrectTypeAndChecksum()
    {
        ushort id = 0x1234;
        ushort seq = 0x0001;
        int payloadSize = 56;
        var buffer = new byte[IcmpPacket.HeaderSize + payloadSize];

        int totalLen = IcmpPacket.BuildEchoRequest(buffer, id, seq, payloadSize, isIpv6: false);

        Assert.Equal(IcmpPacket.HeaderSize + payloadSize, totalLen);
        // Type = 8 (Echo Request)
        Assert.Equal(IcmpPacket.EchoRequest, buffer[0]);
        // Code = 0
        Assert.Equal(0, buffer[1]);
        // Id and Seq stored in big-endian
        Assert.Equal(id, BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(4)));
        Assert.Equal(seq, BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(6)));
        // Checksum should be valid (verify returns true when computed over whole packet including checksum)
        Assert.True(IcmpChecksum.Verify(buffer, 0, totalLen));
    }

    [Fact]
    public void Test_BuildEchoRequest_V6_CorrectType_ZeroChecksum()
    {
        ushort id = 0xABCD;
        ushort seq = 0x0005;
        int payloadSize = 32;
        var buffer = new byte[IcmpPacket.HeaderSize + payloadSize];

        int totalLen = IcmpPacket.BuildEchoRequest(buffer, id, seq, payloadSize, isIpv6: true);

        Assert.Equal(IcmpPacket.HeaderSize + payloadSize, totalLen);
        // Type = 128 (ICMPv6 Echo Request)
        Assert.Equal(IcmpPacket.Icmpv6EchoRequest, buffer[0]);
        // Code = 0
        Assert.Equal(0, buffer[1]);
        // Checksum bytes should be zero (kernel computes for IPv6)
        Assert.Equal(0, buffer[2]);
        Assert.Equal(0, buffer[3]);
    }

    [Fact]
    public void Test_BuildEchoRequest_PayloadPattern()
    {
        ushort id = 1;
        ushort seq = 1;
        int payloadSize = 300; // large enough to wrap around 0xFF
        var buffer = new byte[IcmpPacket.HeaderSize + payloadSize];

        IcmpPacket.BuildEchoRequest(buffer, id, seq, payloadSize, isIpv6: true);

        for (int i = 0; i < payloadSize; i++)
        {
            Assert.Equal((byte)(i & 0xFF), buffer[IcmpPacket.HeaderSize + i]);
        }
    }

    [Fact]
    public void Test_ParseRawV4_EchoReply()
    {
        ushort expectedId = 0x1234;
        ushort seq = 0x0007;

        // Build a fake raw IPv4 packet: 20-byte IP header + 8-byte ICMP header
        var buffer = new byte[20 + IcmpPacket.HeaderSize];
        // IP header: version=4, IHL=5 (20 bytes)
        buffer[0] = 0x45;
        // ICMP Echo Reply: type=0, code=0
        int icmpOffset = 20;
        buffer[icmpOffset] = IcmpPacket.EchoReply;
        buffer[icmpOffset + 1] = 0; // code
        // checksum (not validated by parser, set to 0)
        buffer[icmpOffset + 2] = 0;
        buffer[icmpOffset + 3] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(icmpOffset + 4), expectedId);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(icmpOffset + 6), seq);

        var result = IcmpPacket.ParseRawV4(buffer, buffer.Length, expectedId);

        Assert.NotNull(result);
        Assert.Equal(IcmpPacket.EchoReply, result.Value.Type);
        Assert.Equal(0, result.Value.Code);
        Assert.Equal(expectedId, result.Value.Id);
        Assert.Equal(seq, result.Value.Seq);
        Assert.False(result.Value.IsEmbedded);
        Assert.True(result.Value.IsEchoReply);
    }

    [Fact]
    public void Test_ParseRawV4_WrongId_ReturnsNull()
    {
        ushort expectedId = 0x1234;
        ushort wrongId = 0x5678;
        ushort seq = 0x0007;

        var buffer = new byte[20 + IcmpPacket.HeaderSize];
        buffer[0] = 0x45;
        int icmpOffset = 20;
        buffer[icmpOffset] = IcmpPacket.EchoReply;
        buffer[icmpOffset + 1] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(icmpOffset + 4), wrongId);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(icmpOffset + 6), seq);

        var result = IcmpPacket.ParseRawV4(buffer, buffer.Length, expectedId);

        Assert.Null(result);
    }

    [Fact]
    public void Test_ParseDgramV4_EchoReply()
    {
        ushort expectedId = 0x9999;
        ushort seq = 0x0003;

        // DGRAM socket: no IP header, just ICMP header
        var buffer = new byte[IcmpPacket.HeaderSize];
        buffer[0] = IcmpPacket.EchoReply;
        buffer[1] = 0; // code
        buffer[2] = 0; // checksum
        buffer[3] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), expectedId);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6), seq);

        var result = IcmpPacket.ParseDgramV4(buffer, buffer.Length, expectedId);

        Assert.NotNull(result);
        Assert.Equal(IcmpPacket.EchoReply, result.Value.Type);
        Assert.Equal(expectedId, result.Value.Id);
        Assert.Equal(seq, result.Value.Seq);
        Assert.False(result.Value.IsEmbedded);
        Assert.True(result.Value.IsEchoReply);
    }

    [Fact]
    public void Test_ParseV6_EchoReply()
    {
        ushort expectedId = 0x4321;
        ushort seq = 0x000A;

        // ICMPv6: no IP header
        var buffer = new byte[IcmpPacket.HeaderSize];
        buffer[0] = IcmpPacket.Icmpv6EchoReply; // type=129
        buffer[1] = 0; // code
        buffer[2] = 0;
        buffer[3] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4), expectedId);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(6), seq);

        var result = IcmpPacket.ParseV6(buffer, buffer.Length, expectedId);

        Assert.NotNull(result);
        Assert.Equal(IcmpPacket.Icmpv6EchoReply, result.Value.Type);
        Assert.Equal(expectedId, result.Value.Id);
        Assert.Equal(seq, result.Value.Seq);
        Assert.False(result.Value.IsEmbedded);
        Assert.True(result.Value.IsEchoReply);
    }

    [Fact]
    public void Test_ParseRawV4_DestUnreachable_ExtractsEmbeddedSeq()
    {
        ushort expectedId = 0x1234;
        ushort embeddedSeq = 0x0042;

        // Raw IPv4 packet layout:
        // [0..19]  outer IP header (IHL=5)
        // [20..27] ICMP Dest Unreachable header (type=3, code=1, checksum, unused)
        // [28..47] embedded IP header (IHL=5)
        // [48..55] embedded ICMP Echo Request header
        var buffer = new byte[20 + IcmpPacket.HeaderSize + 20 + IcmpPacket.HeaderSize];

        // Outer IP header
        buffer[0] = 0x45; // version=4, IHL=5

        // ICMP Destination Unreachable
        int icmpOffset = 20;
        buffer[icmpOffset] = IcmpPacket.DestinationUnreachable; // type=3
        buffer[icmpOffset + 1] = 1; // code=1 (host unreachable)
        // id/seq in error message header are unused (zeroed)

        // Embedded IP header
        int embIpOffset = icmpOffset + IcmpPacket.HeaderSize; // 28
        buffer[embIpOffset] = 0x45; // version=4, IHL=5

        // Embedded ICMP Echo Request
        int embIcmpOffset = embIpOffset + 20; // 48
        buffer[embIcmpOffset] = IcmpPacket.EchoRequest; // type=8
        buffer[embIcmpOffset + 1] = 0;
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(embIcmpOffset + 4), expectedId);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(embIcmpOffset + 6), embeddedSeq);

        var result = IcmpPacket.ParseRawV4(buffer, buffer.Length, expectedId);

        Assert.NotNull(result);
        Assert.Equal(IcmpPacket.DestinationUnreachable, result.Value.Type);
        Assert.Equal(1, result.Value.Code);
        Assert.Equal(expectedId, result.Value.Id);
        Assert.Equal(embeddedSeq, result.Value.Seq);
        Assert.True(result.Value.IsEmbedded);
        Assert.True(result.Value.IsError);
        Assert.False(result.Value.IsEchoReply);
    }
}
