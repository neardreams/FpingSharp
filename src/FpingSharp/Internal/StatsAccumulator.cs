using System;
using System.Collections.Generic;

namespace FpingSharp.Internal
{
    internal sealed class StatsAccumulator
    {
        private readonly List<HostEntry> _hosts;

        public StatsAccumulator(List<HostEntry> hosts)
        {
            _hosts = hosts;
        }

        /// <summary>Record a successful ping reply.</summary>
        public void AddSuccess(HostEntry host, int pingIndex, long rttNs)
        {
            host.NumRecv++;
            host.NumRecvTotal++;
            host.NumRecvI++;
            host.TotalTimeNs += rttNs;
            host.TotalTimeI += rttNs;

            if (rttNs > host.MaxReplyNs) host.MaxReplyNs = rttNs;
            if (rttNs < host.MinReplyNs) host.MinReplyNs = rttNs;
            if (rttNs > host.MaxReplyI) host.MaxReplyI = rttNs;
            if (rttNs < host.MinReplyI) host.MinReplyI = rttNs;

            if (host.RespTimes != null && pingIndex < host.RespTimes.Length)
            {
                host.RespTimes[pingIndex] = rttNs;
            }
        }

        /// <summary>Record a timeout (no reply received).</summary>
        public void AddTimeout(HostEntry host, int pingIndex)
        {
            // RespTimes entry remains null (timeout)
            // NumSent was already incremented when the ping was sent
        }

        /// <summary>Build summary results for all hosts.</summary>
        public (int totalSent, int totalRecv, int aliveCount, int unreachableCount,
                long globalMinNs, long globalMaxNs, long globalTotalNs) GetGlobalStats()
        {
            int totalSent = 0, totalRecv = 0, aliveCount = 0, unreachableCount = 0;
            long globalMinNs = long.MaxValue, globalMaxNs = 0, globalTotalNs = 0;

            foreach (var host in _hosts)
            {
                totalSent += host.NumSent;
                totalRecv += host.NumRecv;

                if (host.NumRecv > 0)
                {
                    aliveCount++;
                    if (host.MinReplyNs < globalMinNs) globalMinNs = host.MinReplyNs;
                    if (host.MaxReplyNs > globalMaxNs) globalMaxNs = host.MaxReplyNs;
                    globalTotalNs += host.TotalTimeNs;
                }
                else
                {
                    unreachableCount++;
                }
            }

            if (globalMinNs == long.MaxValue) globalMinNs = 0;

            return (totalSent, totalRecv, aliveCount, unreachableCount, globalMinNs, globalMaxNs, globalTotalNs);
        }
    }
}
