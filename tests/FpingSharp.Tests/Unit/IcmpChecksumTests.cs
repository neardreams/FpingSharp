using FpingSharp.Internal;
using Xunit;

namespace FpingSharp.Tests.Unit;

public class IcmpChecksumTests
{
    [Fact]
    public void Test_Compute_AllZeros()
    {
        byte[] buffer = new byte[8];

        ushort checksum = IcmpChecksum.Compute(buffer);

        Assert.Equal(0xFFFF, checksum);
    }

    [Fact]
    public void Test_Compute_KnownVector_RFC1071()
    {
        // RFC 1071 test vector
        byte[] buffer = { 0x00, 0x01, 0xf2, 0x03, 0xf4, 0xf5, 0xf6, 0xf7 };

        ushort checksum = IcmpChecksum.Compute(buffer);

        Assert.Equal(0x220D, checksum);
    }

    [Fact]
    public void Test_Verify_ValidPacket()
    {
        // Build a packet, compute the checksum, insert it, then verify
        byte[] packet = { 0x08, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01 };

        // Compute checksum over the packet (with checksum field as zeros)
        ushort checksum = IcmpChecksum.Compute(packet);

        // Insert checksum at bytes 2-3 (big-endian)
        packet[2] = (byte)(checksum >> 8);
        packet[3] = (byte)(checksum & 0xFF);

        // Verify should return true
        Assert.True(IcmpChecksum.Verify(packet, 0, packet.Length));
    }

    [Fact]
    public void Test_Verify_CorruptedPacket()
    {
        // Build a valid packet first
        byte[] packet = { 0x08, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01 };
        ushort checksum = IcmpChecksum.Compute(packet);
        packet[2] = (byte)(checksum >> 8);
        packet[3] = (byte)(checksum & 0xFF);

        // Corrupt a byte
        packet[4] ^= 0xFF;

        // Verify should return false
        Assert.False(IcmpChecksum.Verify(packet, 0, packet.Length));
    }

    [Fact]
    public void Test_Compute_OddLength()
    {
        // Odd-length buffer: the last byte should be padded with zero internally
        byte[] buffer = { 0x01, 0x02, 0x03 };

        ushort checksum = IcmpChecksum.Compute(buffer);

        // Manual calculation:
        // word1 = 0x0102 = 258
        // odd byte: 0x03 << 8 = 0x0300 = 768
        // sum = 258 + 768 = 1026 = 0x0402
        // ~0x0402 = 0xFBFD
        Assert.Equal(0xFBFD, checksum);
    }

    [Fact]
    public void Test_Compute_WithOffset()
    {
        // Full buffer, but only checksum a sub-region
        byte[] buffer = { 0xFF, 0xFF, 0x00, 0x01, 0xf2, 0x03, 0xf4, 0xf5, 0xf6, 0xf7, 0xFF, 0xFF };

        // Compute checksum over bytes [2..9] (same as RFC 1071 vector)
        ushort checksum = IcmpChecksum.Compute(buffer, offset: 2, length: 8);

        Assert.Equal(0x220D, checksum);
    }
}
