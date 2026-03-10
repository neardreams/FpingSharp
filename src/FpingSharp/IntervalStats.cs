using System;
using System.Collections.Generic;

namespace FpingSharp
{
    /// <summary>
    /// Per-host statistics for a single reporting interval.
    /// </summary>
    public sealed class HostIntervalStats
    {
        public string Name { get; }
        public int Sent { get; }
        public int Received { get; }
        public double LossPercent { get; }
        public TimeSpan MinRtt { get; }
        public TimeSpan MaxRtt { get; }
        public TimeSpan AvgRtt { get; }

        public HostIntervalStats(string name, int sent, int received,
            double lossPercent, TimeSpan minRtt, TimeSpan maxRtt, TimeSpan avgRtt)
        {
            Name = name;
            Sent = sent;
            Received = received;
            LossPercent = lossPercent;
            MinRtt = minRtt;
            MaxRtt = maxRtt;
            AvgRtt = avgRtt;
        }
    }

    /// <summary>
    /// Periodic interval statistics event data (-Q flag).
    /// </summary>
    public sealed class IntervalStats : EventArgs
    {
        public IReadOnlyList<HostIntervalStats> Hosts { get; }
        public TimeSpan Elapsed { get; }

        public IntervalStats(IReadOnlyList<HostIntervalStats> hosts, TimeSpan elapsed)
        {
            Hosts = hosts;
            Elapsed = elapsed;
        }
    }
}
