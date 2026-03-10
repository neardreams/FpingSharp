using FpingSharp.Internal;
using Xunit;

namespace FpingSharp.Tests.Unit;

public class StatsAccumulatorTests
{
    private static HostEntry CreateHost(int index = 0, string name = "test-host", int maxEvents = 1)
    {
        return new HostEntry(index, name, maxEvents);
    }

    [Fact]
    public void Test_AddSuccess_UpdatesHostStats()
    {
        var host = CreateHost();
        var accumulator = new StatsAccumulator(new List<HostEntry> { host });
        long rttNs = 5_000_000; // 5ms

        accumulator.AddSuccess(host, 0, rttNs);

        Assert.Equal(1, host.NumRecv);
        Assert.Equal(1, host.NumRecvTotal);
        Assert.Equal(rttNs, host.MinReplyNs);
        Assert.Equal(rttNs, host.MaxReplyNs);
        Assert.Equal(rttNs, host.TotalTimeNs);
    }

    [Fact]
    public void Test_AddSuccess_Multiple_TracksMinMax()
    {
        var host = CreateHost();
        var accumulator = new StatsAccumulator(new List<HostEntry> { host });

        accumulator.AddSuccess(host, 0, 100);
        accumulator.AddSuccess(host, 1, 50);
        accumulator.AddSuccess(host, 2, 200);

        Assert.Equal(3, host.NumRecv);
        Assert.Equal(50, host.MinReplyNs);
        Assert.Equal(200, host.MaxReplyNs);
        Assert.Equal(350, host.TotalTimeNs);
    }

    [Fact]
    public void Test_AddSuccess_UpdatesRespTimes()
    {
        var host = CreateHost();
        host.RespTimes = new long?[5];
        var accumulator = new StatsAccumulator(new List<HostEntry> { host });

        accumulator.AddSuccess(host, 0, 1000);
        accumulator.AddSuccess(host, 3, 2000);

        Assert.Equal(1000L, host.RespTimes[0]);
        Assert.Null(host.RespTimes[1]);
        Assert.Null(host.RespTimes[2]);
        Assert.Equal(2000L, host.RespTimes[3]);
        Assert.Null(host.RespTimes[4]);
    }

    [Fact]
    public void Test_AddSuccess_UpdatesIntervalStats()
    {
        var host = CreateHost();
        var accumulator = new StatsAccumulator(new List<HostEntry> { host });

        accumulator.AddSuccess(host, 0, 100);
        accumulator.AddSuccess(host, 1, 200);

        Assert.Equal(2, host.NumRecvI);
        Assert.Equal(100, host.MinReplyI);
        Assert.Equal(200, host.MaxReplyI);
        Assert.Equal(300, host.TotalTimeI);
    }

    [Fact]
    public void Test_GetGlobalStats_MultipleHosts()
    {
        var host1 = CreateHost(0, "host1");
        host1.NumSent = 5;
        var host2 = CreateHost(1, "host2");
        host2.NumSent = 5;
        var host3 = CreateHost(2, "host3");
        host3.NumSent = 5;

        var hosts = new List<HostEntry> { host1, host2, host3 };
        var accumulator = new StatsAccumulator(hosts);

        // host1: receives 3 replies
        accumulator.AddSuccess(host1, 0, 100);
        accumulator.AddSuccess(host1, 1, 50);
        accumulator.AddSuccess(host1, 2, 200);

        // host2: receives 1 reply
        accumulator.AddSuccess(host2, 0, 75);

        // host3: receives nothing (all timeouts)

        var (totalSent, totalRecv, aliveCount, unreachableCount,
             globalMinNs, globalMaxNs, globalTotalNs) = accumulator.GetGlobalStats();

        Assert.Equal(15, totalSent);       // 5+5+5
        Assert.Equal(4, totalRecv);        // 3+1+0
        Assert.Equal(2, aliveCount);       // host1, host2
        Assert.Equal(1, unreachableCount); // host3
        Assert.Equal(50, globalMinNs);     // min across host1(50), host2(75)
        Assert.Equal(200, globalMaxNs);    // max across host1(200), host2(75)
        Assert.Equal(425, globalTotalNs);  // (100+50+200) + 75
    }

    [Fact]
    public void Test_GetGlobalStats_NoReplies()
    {
        var host1 = CreateHost(0, "host1");
        host1.NumSent = 3;
        var host2 = CreateHost(1, "host2");
        host2.NumSent = 3;

        var hosts = new List<HostEntry> { host1, host2 };
        var accumulator = new StatsAccumulator(hosts);

        // No AddSuccess calls - all timeouts

        var (totalSent, totalRecv, aliveCount, unreachableCount,
             globalMinNs, globalMaxNs, globalTotalNs) = accumulator.GetGlobalStats();

        Assert.Equal(6, totalSent);
        Assert.Equal(0, totalRecv);
        Assert.Equal(0, aliveCount);
        Assert.Equal(2, unreachableCount);
        Assert.Equal(0, globalMinNs);   // should be 0 when no replies (not long.MaxValue)
        Assert.Equal(0, globalMaxNs);
        Assert.Equal(0, globalTotalNs);
    }
}
