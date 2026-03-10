using System;
using System.Collections.Generic;

namespace FpingSharp
{
    /// <summary>
    /// Aggregated result of an entire fping run.
    /// </summary>
    public sealed class FpingResult
    {
        public IReadOnlyList<HostResult> Hosts { get; }
        public int TotalSent { get; }
        public int TotalReceived { get; }
        public int AliveCount { get; }
        public int UnreachableCount { get; }
        public TimeSpan GlobalMinRtt { get; }
        public TimeSpan GlobalMaxRtt { get; }
        public TimeSpan GlobalAvgRtt { get; }
        public TimeSpan Elapsed { get; }

        public FpingResult(IReadOnlyList<HostResult> hosts,
                           int totalSent, int totalReceived,
                           int aliveCount, int unreachableCount,
                           TimeSpan globalMinRtt, TimeSpan globalMaxRtt, TimeSpan globalAvgRtt,
                           TimeSpan elapsed)
        {
            Hosts = hosts;
            TotalSent = totalSent;
            TotalReceived = totalReceived;
            AliveCount = aliveCount;
            UnreachableCount = unreachableCount;
            GlobalMinRtt = globalMinRtt;
            GlobalMaxRtt = globalMaxRtt;
            GlobalAvgRtt = globalAvgRtt;
            Elapsed = elapsed;
        }
    }
}
