using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;

namespace FpingSharp.Internal
{
    /// <summary>
    /// Core event-driven ping engine, faithfully porting fping's main_loop.
    /// Single-threaded, non-blocking, poll-based.
    /// </summary>
    internal sealed class PingEngine
    {
        private readonly FpingOptions _options;
        private readonly IcmpSocket _socket;
        private readonly List<HostEntry> _hosts;
        private readonly SequenceMap _seqMap;
        private readonly StatsAccumulator _stats;
        private readonly EventQueue _pingQueue;
        private readonly EventQueue _timeoutQueue;
        private readonly byte[] _sendBuffer;
        private readonly byte[] _recvBuffer;
        private long _lastSendTimeNs; // global rate limiting
        private long _intervalNs;     // minimum ns between any two sends
        private long _perHostIntervalNs;
        private long _timeoutNs;
        private volatile bool _stopRequested;

        // Interval stats fields
        private readonly long _statsIntervalNs;
        private long _lastStatsTimeNs;
        private long _engineStartTimeNs;

        // Events for notifying caller
        public event EventHandler<PingReply>? OnReply;
        public event EventHandler<PingReply>? OnTimeout;
        public event EventHandler<IntervalStats>? OnIntervalStats;

        // Diagnostic counters (internal, for benchmarking)
        internal long DiagSendCount;
        internal long DiagTimeoutCount;
        internal long DiagPollCount;
        internal long DiagDrainCount;
        internal long DiagLoopCount;
        internal double DiagSendTotalMs;
        internal double DiagPollTotalMs;
        internal double DiagDrainTotalMs;

        public PingEngine(FpingOptions options, IcmpSocket socket, List<HostEntry> hosts)
        {
            _options = options;
            _socket = socket;
            _hosts = hosts;
            _seqMap = new SequenceMap();
            _stats = new StatsAccumulator(hosts);
            _pingQueue = new EventQueue();
            _timeoutQueue = new EventQueue();
            _sendBuffer = new byte[IcmpPacket.HeaderSize + options.PacketSize];
            _recvBuffer = new byte[4096];
            _intervalNs = HighResolutionClock.MsToNs(options.IntervalMs);
            _perHostIntervalNs = HighResolutionClock.MsToNs(options.PerHostIntervalMs);
            _timeoutNs = HighResolutionClock.MsToNs(options.TimeoutMs);

            _statsIntervalNs = options.StatsIntervalMs.HasValue && options.StatsIntervalMs.Value > 0
                ? HighResolutionClock.MsToNs(options.StatsIntervalMs.Value)
                : 0;

            // Initialize timeout per host
            foreach (var host in hosts)
            {
                host.TimeoutNs = _timeoutNs;
            }
        }

        public void RequestStop() => _stopRequested = true;

        public StatsAccumulator Stats => _stats;

        /// <summary>
        /// Initialize the ping queue by scheduling the first ping for each host.
        /// Hosts are spaced apart by intervalNs to avoid burst.
        /// </summary>
        public void Initialize()
        {
            long nowNs = HighResolutionClock.NowNs();
            _engineStartTimeNs = nowNs;
            _lastStatsTimeNs = nowNs;
            long scheduleTime = nowNs;

            foreach (var host in _hosts)
            {
                if (host.DnsFailed) continue;

                // Allocate RespTimes array for count mode
                if (!_options.Loop && _options.Count > 0)
                {
                    host.RespTimes = new long?[_options.Count];
                }

                var ev = host.CurrentPingEvent;
                ev.EvTime = scheduleTime;
                _pingQueue.Enqueue(ev);
                scheduleTime += _intervalNs;
            }
        }

        /// <summary>
        /// Run the main event loop. This is the core of fping's main_loop ported to C#.
        /// Returns when all pings are complete or cancellation is requested.
        /// </summary>
        public void Run(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !_stopRequested)
            {
                DiagLoopCount++;
                long nowNs = HighResolutionClock.NowNs();

                // Step 1: Process expired timeouts
                ProcessTimeouts(nowNs);

                // Step 1.5: Check if interval stats are due
                if (_statsIntervalNs > 0 && nowNs >= _lastStatsTimeNs + _statsIntervalNs)
                {
                    FireIntervalStats(nowNs);
                }

                if (_pingQueue.IsEmpty && _timeoutQueue.IsEmpty)
                    break;

                nowNs = HighResolutionClock.NowNs();

                // Step 2: Process ready ping events (respect global interval rate limiting)
                ProcessPings(nowNs);

                // Step 3: Calculate wait time
                nowNs = HighResolutionClock.NowNs();
                long waitNs = CalculateWaitTime(nowNs);

                if (waitNs < 0) waitNs = 0;

                // Step 4: Poll socket for replies
                if (waitNs > 0 || !_pingQueue.IsEmpty || !_timeoutQueue.IsEmpty)
                {
                    int waitUs = (int)(waitNs / 1000);
                    if (waitUs < 0) waitUs = 0;
                    if (waitUs > 100_000) waitUs = 100_000; // cap at 100ms for cancellation responsiveness

                    long pt0 = HighResolutionClock.NowNs();
                    bool hasData = _socket.Poll(waitUs);
                    DiagPollTotalMs += HighResolutionClock.NsToMs(HighResolutionClock.NowNs() - pt0);
                    DiagPollCount++;

                    if (hasData)
                    {
                        long dt0 = HighResolutionClock.NowNs();
                        DrainReplies();
                        DiagDrainTotalMs += HighResolutionClock.NsToMs(HighResolutionClock.NowNs() - dt0);
                        DiagDrainCount++;
                    }
                }

                // Check if we're done
                if (_pingQueue.IsEmpty && _timeoutQueue.IsEmpty)
                    break;
            }
        }

        private void ProcessTimeouts(long nowNs)
        {
            while (!_timeoutQueue.IsEmpty)
            {
                var ev = _timeoutQueue.PeekHead();
                if (ev == null || ev.EvTime > nowNs) break;

                _timeoutQueue.Dequeue();
                var host = ev.Host;
                if (host == null) continue;

                host.WaitingForReply = false;
                DiagTimeoutCount++;

                // Record timeout
                _stats.AddTimeout(host, host.CurrentPingCount);

                // Fire timeout event
                OnTimeout?.Invoke(this, new PingReply(
                    host.Name, host.Address, false, null,
                    0, host.CurrentPingCount));

                // Retry with exponential backoff?
                if (host.CurrentRetry < _options.Retry)
                {
                    host.CurrentRetry++;
                    host.TimeoutNs = (long)(host.TimeoutNs * _options.Backoff);

                    // Schedule retry ping immediately
                    var pingEv = host.CurrentPingEvent;
                    pingEv.EvTime = nowNs;
                    _pingQueue.Enqueue(pingEv);
                }
                else
                {
                    // No more retries for this ping count, advance to next
                    host.CurrentRetry = 0;
                    host.TimeoutNs = _timeoutNs; // reset timeout
                    AdvanceHost(host, nowNs);
                }
            }
        }

        private void ProcessPings(long nowNs)
        {
            while (!_pingQueue.IsEmpty)
            {
                var ev = _pingQueue.PeekHead();
                if (ev == null || ev.EvTime > nowNs) break;

                // Global rate limiting: ensure minimum interval between sends
                long earliestSend = _lastSendTimeNs + _intervalNs;
                if (nowNs < earliestSend)
                    break; // too soon, wait

                _pingQueue.Dequeue();
                var host = ev.Host;
                if (host == null) continue;

                SendPing(host, nowNs);
                nowNs = HighResolutionClock.NowNs(); // refresh after send
            }
        }

        private void SendPing(HostEntry host, long nowNs)
        {
            if (host.EndPoint == null) return;

            bool isIpv6 = host.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6;

            // Register in sequence map
            ushort seq = _seqMap.Add(host.Index, host.CurrentPingCount, nowNs);

            // Build ICMP echo request
            int len = IcmpPacket.BuildEchoRequest(_sendBuffer, _socket.Id, seq, _options.PacketSize, isIpv6);

            try
            {
                long t0 = HighResolutionClock.NowNs();
                _socket.SendTo(_sendBuffer, len, host.EndPoint);
                DiagSendTotalMs += HighResolutionClock.NsToMs(HighResolutionClock.NowNs() - t0);
                DiagSendCount++;
            }
            catch (System.Net.Sockets.SocketException)
            {
                // Send failed (ARP not ready on DGRAM, buffer full, etc.)
                // Still count as sent — host will timeout naturally
            }

            host.NumSent++;
            host.NumSentI++;
            host.LastSendTimeNs = nowNs;
            host.WaitingForReply = true;
            _lastSendTimeNs = nowNs;

            // Schedule timeout event
            var timeoutEv = host.CurrentTimeoutEvent;
            if (timeoutEv.IsLinked)
                _timeoutQueue.Remove(timeoutEv);
            timeoutEv.EvTime = nowNs + host.TimeoutNs;
            _timeoutQueue.Enqueue(timeoutEv);
        }

        private void DrainReplies()
        {
            EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);

            while (true)
            {
                // Check if more data available
                if (!_socket.Poll(0))
                    break;

                int received;
                try
                {
                    received = _socket.ReceiveFrom(_recvBuffer, ref remoteEP);
                }
                catch (System.Net.Sockets.SocketException)
                {
                    break;
                }

                if (received <= 0) break;

                long nowNs = HighResolutionClock.NowNs();
                ProcessReply(received, nowNs);
            }
        }

        private void ProcessReply(int length, long nowNs)
        {
            bool isIpv6 = _options.AddressFamily == FpingAddressFamily.IPv6;

            IcmpPacket.ParseResult? result;
            if (isIpv6)
            {
                result = IcmpPacket.ParseV6(_recvBuffer, length, _socket.Id);
            }
            else if (_socket.IsRaw)
            {
                result = IcmpPacket.ParseRawV4(_recvBuffer, length, _socket.Id);
            }
            else
            {
                result = IcmpPacket.ParseDgramV4(_recvBuffer, length, _socket.Id);
            }

            if (result == null) return;

            var parsed = result.Value;

            // Look up the sequence number
            var entry = _seqMap.Fetch(parsed.Seq, nowNs);
            if (entry == null) return; // expired or unknown

            var (hostIndex, pingCount, sendTimeNs) = entry.Value;
            if (hostIndex < 0 || hostIndex >= _hosts.Count) return;

            var host = _hosts[hostIndex];

            if (parsed.IsEchoReply)
            {
                long rttNs = nowNs - sendTimeNs;

                // Remove pending timeout
                var timeoutEv = host.CurrentTimeoutEvent;
                if (timeoutEv.IsLinked)
                    _timeoutQueue.Remove(timeoutEv);

                host.WaitingForReply = false;
                host.CurrentRetry = 0;
                host.TimeoutNs = _timeoutNs; // reset timeout

                // Record success
                _stats.AddSuccess(host, pingCount, rttNs);

                // Fire reply event
                OnReply?.Invoke(this, new PingReply(
                    host.Name, host.Address, true,
                    TimeSpan.FromTicks(rttNs / 100), // ns to TimeSpan ticks (100ns each)
                    parsed.Seq, pingCount,
                    parsed.Type, parsed.Code));

                // Schedule next ping for this host
                AdvanceHost(host, nowNs);
            }
            else if (parsed.IsError)
            {
                // ICMP error - treat similar to timeout but report immediately
                var timeoutEv = host.CurrentTimeoutEvent;
                if (timeoutEv.IsLinked)
                    _timeoutQueue.Remove(timeoutEv);

                host.WaitingForReply = false;

                // Fire as timeout with ICMP type/code info
                OnTimeout?.Invoke(this, new PingReply(
                    host.Name, host.Address, false, null,
                    parsed.Seq, pingCount,
                    parsed.Type, parsed.Code));

                // Don't retry on ICMP errors - advance
                host.CurrentRetry = 0;
                host.TimeoutNs = _timeoutNs;
                AdvanceHost(host, nowNs);
            }
        }

        private void AdvanceHost(HostEntry host, long nowNs)
        {
            host.CurrentPingCount++;

            // Check if we need more pings
            if (_options.Loop || host.CurrentPingCount < _options.Count)
            {
                // Schedule next ping after perHostInterval
                var nextEv = host.CurrentPingEvent;
                if (nextEv.IsLinked)
                    _pingQueue.Remove(nextEv);
                nextEv.EvTime = nowNs + _perHostIntervalNs;
                _pingQueue.Enqueue(nextEv);
            }
            // else: this host is done, no more events scheduled
        }

        private long CalculateWaitTime(long nowNs)
        {
            long nextEvent = long.MaxValue;

            var nextPing = _pingQueue.PeekHead();
            if (nextPing != null)
            {
                // Also account for global rate limit
                long earliestSend = Math.Max(nextPing.EvTime, _lastSendTimeNs + _intervalNs);
                if (earliestSend < nextEvent)
                    nextEvent = earliestSend;
            }

            var nextTimeout = _timeoutQueue.PeekHead();
            if (nextTimeout != null && nextTimeout.EvTime < nextEvent)
            {
                nextEvent = nextTimeout.EvTime;
            }

            // Include next stats time so we don't sleep through it
            if (_statsIntervalNs > 0)
            {
                long nextStatsTime = _lastStatsTimeNs + _statsIntervalNs;
                if (nextStatsTime < nextEvent)
                    nextEvent = nextStatsTime;
            }

            if (nextEvent == long.MaxValue) return 0;

            return nextEvent - nowNs;
        }

        private void FireIntervalStats(long nowNs)
        {
            var elapsed = TimeSpan.FromTicks((nowNs - _engineStartTimeNs) / 100);
            var hostStats = new List<HostIntervalStats>(_hosts.Count);

            foreach (var host in _hosts)
            {
                double lossPct = host.NumSentI > 0
                    ? (1.0 - (double)host.NumRecvI / host.NumSentI) * 100.0
                    : 0.0;

                TimeSpan minRtt = host.NumRecvI > 0 && host.MinReplyI != long.MaxValue
                    ? TimeSpan.FromTicks(host.MinReplyI / 100)
                    : TimeSpan.Zero;
                TimeSpan maxRtt = host.NumRecvI > 0
                    ? TimeSpan.FromTicks(host.MaxReplyI / 100)
                    : TimeSpan.Zero;
                TimeSpan avgRtt = host.NumRecvI > 0
                    ? TimeSpan.FromTicks(host.TotalTimeI / host.NumRecvI / 100)
                    : TimeSpan.Zero;

                hostStats.Add(new HostIntervalStats(
                    host.Name, host.NumSentI, host.NumRecvI,
                    lossPct, minRtt, maxRtt, avgRtt));

                host.ResetIntervalStats();
            }

            _lastStatsTimeNs = nowNs;

            OnIntervalStats?.Invoke(this, new IntervalStats(hostStats, elapsed));
        }
    }
}
