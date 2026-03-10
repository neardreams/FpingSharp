using FpingSharp.Internal;
using Xunit;

namespace FpingSharp.Tests.Unit;

public class SequenceMapTests
{
    [Fact]
    public void Test_Add_ReturnsIncrementingSeq()
    {
        var map = new SequenceMap();

        ushort seq0 = map.Add(0, 0, 1000);
        ushort seq1 = map.Add(1, 1, 2000);
        ushort seq2 = map.Add(2, 2, 3000);

        Assert.Equal(0, seq0);
        Assert.Equal(1, seq1);
        Assert.Equal(2, seq2);
    }

    [Fact]
    public void Test_Fetch_ValidEntry_ReturnsData()
    {
        var map = new SequenceMap();

        ushort seq = map.Add(hostIndex: 42, pingCount: 7, timestampNs: 5000);

        var result = map.Fetch(seq, nowNs: 5000);

        Assert.NotNull(result);
        Assert.Equal(42, result!.Value.HostIndex);
        Assert.Equal(7, result.Value.PingCount);
        Assert.Equal(5000, result.Value.TimestampNs);
    }

    [Fact]
    public void Test_Fetch_InvalidSeq_ReturnsNull()
    {
        var map = new SequenceMap();

        var result = map.Fetch(seq: 999, nowNs: 1000);

        Assert.Null(result);
    }

    [Fact]
    public void Test_Fetch_Expired_ReturnsNull()
    {
        var map = new SequenceMap();

        // Add with timestamp 0, so ExpiresNs = 0 + 10_000_000_000 = 10_000_000_000
        ushort seq = map.Add(hostIndex: 1, pingCount: 0, timestampNs: 0);

        // Fetch with nowNs > ExpiresNs
        var result = map.Fetch(seq, nowNs: 11_000_000_000);

        Assert.Null(result);
    }

    [Fact]
    public void Test_Fetch_ConsumesEntry()
    {
        var map = new SequenceMap();

        ushort seq = map.Add(hostIndex: 1, pingCount: 0, timestampNs: 1000);

        var first = map.Fetch(seq, nowNs: 1000);
        var second = map.Fetch(seq, nowNs: 1000);

        Assert.NotNull(first);
        Assert.Null(second);
    }

    [Fact]
    public void Test_Wrapping_At65535()
    {
        var map = new SequenceMap();

        // Fill all 65535 slots (indices 0..65534)
        for (int i = 0; i < 65535; i++)
        {
            ushort seq = map.Add(hostIndex: i, pingCount: 0, timestampNs: 1000);
            Assert.Equal((ushort)i, seq);
        }

        // The next add should wrap back to 0
        ushort wrappedSeq = map.Add(hostIndex: 99, pingCount: 1, timestampNs: 2000);
        Assert.Equal(0, wrappedSeq);

        // Verify the wrapped entry is fetchable
        var result = map.Fetch(wrappedSeq, nowNs: 2000);
        Assert.NotNull(result);
        Assert.Equal(99, result!.Value.HostIndex);
        Assert.Equal(1, result.Value.PingCount);
    }

    [Fact]
    public void Test_Clear_ResetsAll()
    {
        var map = new SequenceMap();

        ushort seq0 = map.Add(hostIndex: 1, pingCount: 0, timestampNs: 1000);
        ushort seq1 = map.Add(hostIndex: 2, pingCount: 1, timestampNs: 2000);

        map.Clear();

        Assert.Null(map.Fetch(seq0, nowNs: 1000));
        Assert.Null(map.Fetch(seq1, nowNs: 2000));

        // After clear, sequence numbers should restart from 0
        ushort newSeq = map.Add(hostIndex: 3, pingCount: 0, timestampNs: 3000);
        Assert.Equal(0, newSeq);
    }
}
