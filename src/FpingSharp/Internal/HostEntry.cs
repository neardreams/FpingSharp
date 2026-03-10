using System;
using System.Net;

namespace FpingSharp.Internal
{
    internal sealed class HostEntry
    {
        // Identity
        public int Index { get; }
        public string Name { get; }
        public string? ResolvedName { get; set; }
        public IPAddress? Address { get; set; }
        public EndPoint? EndPoint { get; set; }
        public System.Net.Sockets.AddressFamily AddressFamily { get; set; }
        public bool DnsResolved { get; set; }
        public bool DnsFailed { get; set; }

        // Timeout for this host (nanoseconds) - starts from options, grows with backoff on retry
        public long TimeoutNs { get; set; }
        public long LastSendTimeNs { get; set; }

        // Overall statistics
        public int NumSent { get; set; }
        public int NumRecv { get; set; }
        public int NumRecvTotal { get; set; }
        public long MaxReplyNs { get; set; }
        public long MinReplyNs { get; set; } = long.MaxValue;
        public long TotalTimeNs { get; set; }

        // Interval statistics (for periodic reporting in loop mode)
        public int NumSentI { get; set; }
        public int NumRecvI { get; set; }
        public long MaxReplyI { get; set; }
        public long MinReplyI { get; set; } = long.MaxValue;
        public long TotalTimeI { get; set; }

        // Per-ping response times for count mode (null = timeout)
        public long?[]? RespTimes { get; set; }

        // Pre-allocated event storage to avoid GC pressure
        // Each host needs events for ping scheduling and timeout tracking
        public PingEvent[] EventStoragePing { get; }
        public PingEvent[] EventStorageTimeout { get; }

        // Retry tracking
        public int CurrentRetry { get; set; }
        public int CurrentPingCount { get; set; }
        public bool WaitingForReply { get; set; }
        public int SendRetryCount { get; set; }

        public HostEntry(int index, string name, int maxEvents)
        {
            Index = index;
            Name = name;

            // Pre-allocate event nodes
            EventStoragePing = new PingEvent[maxEvents];
            EventStorageTimeout = new PingEvent[maxEvents];
            for (int i = 0; i < maxEvents; i++)
            {
                EventStoragePing[i] = new PingEvent { Host = this, EventIndex = i };
                EventStorageTimeout[i] = new PingEvent { Host = this, EventIndex = i };
            }
        }

        // Get the current ping event (for scheduling)
        public PingEvent CurrentPingEvent => EventStoragePing[CurrentPingCount % EventStoragePing.Length];
        public PingEvent CurrentTimeoutEvent => EventStorageTimeout[CurrentPingCount % EventStorageTimeout.Length];

        public bool IsAlive => NumRecv > 0;

        public void ResetIntervalStats()
        {
            NumSentI = 0;
            NumRecvI = 0;
            MaxReplyI = 0;
            MinReplyI = long.MaxValue;
            TotalTimeI = 0;
        }
    }
}
