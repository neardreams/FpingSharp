using System;
using System.Collections.Generic;
using System.Net;

namespace FpingSharp
{
    /// <summary>
    /// Aggregated result for a single host after a ping run completes.
    /// </summary>
    public sealed class HostResult
    {
        public string Name { get; }
        public IPAddress? Address { get; }
        public bool IsAlive { get; }
        public int Sent { get; }
        public int Received { get; }

        public double LossPercent => Sent > 0 ? (double)(Sent - Received) / Sent * 100.0 : 100.0;

        public TimeSpan MinRtt { get; }
        public TimeSpan MaxRtt { get; }
        public TimeSpan AvgRtt { get; }

        public IReadOnlyList<TimeSpan?> ResponseTimes { get; }

        public HostResult(string name, IPAddress? address, bool isAlive,
                          int sent, int received,
                          TimeSpan minRtt, TimeSpan maxRtt, TimeSpan avgRtt,
                          IReadOnlyList<TimeSpan?> responseTimes)
        {
            Name = name;
            Address = address;
            IsAlive = isAlive;
            Sent = sent;
            Received = received;
            MinRtt = minRtt;
            MaxRtt = maxRtt;
            AvgRtt = avgRtt;
            ResponseTimes = responseTimes;
        }
    }
}
