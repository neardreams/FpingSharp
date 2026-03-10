using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FpingSharp.Exceptions;
using FpingSharp.Internal;

namespace FpingSharp
{
    /// <summary>
    /// High-performance parallel ping client, ported from fping v5.5.
    /// </summary>
    public sealed class FpingClient : IDisposable
    {
        private readonly FpingOptions _options;
        private volatile bool _stopRequested;
        private bool _disposed;

        /// <summary>
        /// Fired for each successful ping reply.
        /// </summary>
        public event EventHandler<PingReply>? OnReply;

        /// <summary>
        /// Fired for each ping timeout.
        /// </summary>
        public event EventHandler<PingReply>? OnTimeout;

        /// <summary>
        /// Fired at each statistics interval when StatsIntervalMs is configured (-Q flag).
        /// </summary>
        public event EventHandler<IntervalStats>? OnIntervalStats;

        public FpingClient(FpingOptions? options = null)
        {
            _options = options ?? new FpingOptions();
            _options.Validate();
        }

        /// <summary>
        /// Run fping against the given hosts. Blocks until all pings complete or cancellation.
        /// </summary>
        public FpingResult Run(IEnumerable<string> hosts, CancellationToken ct = default)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(FpingClient));
            _stopRequested = false;

            var sw = Stopwatch.StartNew();

            // Resolve hosts
            var hostList = ResolveHosts(hosts);
            if (hostList.Count == 0)
            {
                return new FpingResult(
                    Array.Empty<HostResult>(), 0, 0, 0, 0,
                    TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero);
            }

            // Create socket
            using var socket = CreateSocket();
            Console.Error.WriteLine($"  [FpingSharp] Socket type: {(socket.IsRaw ? "RAW" : "DGRAM")}");
            socket.Configure(_options);
            socket.SetReceiveBufferSize(4 * 1024 * 1024);

            // Bind to source address if specified (-S)
            if (_options.SourceAddress != null)
            {
                socket.BindToAddress(IPAddress.Parse(_options.SourceAddress));
            }

            // Bind to network interface if specified (-I)
            if (_options.InterfaceName != null)
            {
                socket.BindToInterface(_options.InterfaceName);
            }

            if (!socket.IsRaw)
            {
                // DGRAM sockets block on ARP resolution for local subnet hosts.
                // Use non-blocking mode to avoid this; failed sends will be retried
                // automatically by the PingEngine.
                socket.SetNonBlocking();
            }

            // Create and run engine
            var engine = new PingEngine(_options, socket, hostList);
            engine.OnReply += (s, e) => OnReply?.Invoke(this, e);
            engine.OnTimeout += (s, e) => OnTimeout?.Invoke(this, e);
            engine.OnIntervalStats += (s, e) => OnIntervalStats?.Invoke(this, e);
            engine.Initialize();

            // Use a linked CancellationToken for RequestStop
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            // Monitor _stopRequested in a simple way
            if (_stopRequested) linkedCts.Cancel();

            engine.Run(linkedCts.Token);

            sw.Stop();

            // Build results
            return BuildResult(hostList, engine.Stats, sw.Elapsed);
        }

        /// <summary>
        /// Run fping asynchronously against the given hosts.
        /// </summary>
        public Task<FpingResult> RunAsync(IEnumerable<string> hosts, CancellationToken ct = default)
        {
            return Task.Run(() => Run(hosts, ct), ct);
        }

        /// <summary>
        /// Request the engine to stop (for loop mode).
        /// </summary>
        public void RequestStop()
        {
            _stopRequested = true;
        }

        private List<HostEntry> ResolveHosts(IEnumerable<string> hosts)
        {
            var result = new List<HostEntry>();
            int index = 0;
            int maxEvents = _options.Loop ? 1 : Math.Max(1, _options.Count);

            foreach (var hostStr in hosts)
            {
                var entry = new HostEntry(index, hostStr, maxEvents);

                try
                {
                    // Try parsing as IP address first
                    if (IPAddress.TryParse(hostStr, out var ip))
                    {
                        entry.Address = ip;
                        entry.AddressFamily = ip.AddressFamily;
                        entry.EndPoint = new IPEndPoint(ip, 0);
                        entry.DnsResolved = true;
                    }
                    else
                    {
                        // DNS resolution
                        var addresses = Dns.GetHostAddresses(hostStr);
                        var targetFamily = _options.AddressFamily == FpingAddressFamily.IPv6
                            ? AddressFamily.InterNetworkV6
                            : AddressFamily.InterNetwork;

                        IPAddress? selected = null;
                        foreach (var addr in addresses)
                        {
                            if (_options.AddressFamily == FpingAddressFamily.Both || addr.AddressFamily == targetFamily)
                            {
                                selected = addr;
                                break;
                            }
                        }

                        if (selected == null && addresses.Length > 0)
                        {
                            selected = addresses[0]; // fallback to first available
                        }

                        if (selected != null)
                        {
                            entry.Address = selected;
                            entry.AddressFamily = selected.AddressFamily;
                            entry.EndPoint = new IPEndPoint(selected, 0);
                            entry.ResolvedName = hostStr;
                            entry.DnsResolved = true;
                        }
                        else
                        {
                            entry.DnsFailed = true;
                        }
                    }
                }
                catch (SocketException)
                {
                    entry.DnsFailed = true;
                }

                result.Add(entry);
                index++;
            }

            return result;
        }

        private IcmpSocket CreateSocket()
        {
            return _options.AddressFamily == FpingAddressFamily.IPv6
                ? (IcmpSocket)new IcmpSocketV6()
                : new IcmpSocketV4();
        }

        private FpingResult BuildResult(List<HostEntry> hosts, StatsAccumulator stats, TimeSpan elapsed)
        {
            var hostResults = new List<HostResult>(hosts.Count);

            foreach (var host in hosts)
            {
                var responseTimes = new List<TimeSpan?>();
                if (host.RespTimes != null)
                {
                    foreach (var rt in host.RespTimes)
                    {
                        responseTimes.Add(rt.HasValue ? TimeSpan.FromTicks(rt.Value / 100) : (TimeSpan?)null);
                    }
                }

                TimeSpan minRtt = host.NumRecv > 0 ? TimeSpan.FromTicks(host.MinReplyNs / 100) : TimeSpan.Zero;
                TimeSpan maxRtt = host.NumRecv > 0 ? TimeSpan.FromTicks(host.MaxReplyNs / 100) : TimeSpan.Zero;
                TimeSpan avgRtt = host.NumRecv > 0 ? TimeSpan.FromTicks(host.TotalTimeNs / host.NumRecv / 100) : TimeSpan.Zero;

                hostResults.Add(new HostResult(
                    host.Name, host.Address, host.IsAlive,
                    host.NumSent, host.NumRecv,
                    minRtt, maxRtt, avgRtt,
                    responseTimes));
            }

            var (totalSent, totalRecv, aliveCount, unreachableCount, globalMinNs, globalMaxNs, globalTotalNs) = stats.GetGlobalStats();

            TimeSpan globalMin = totalRecv > 0 ? TimeSpan.FromTicks(globalMinNs / 100) : TimeSpan.Zero;
            TimeSpan globalMax = totalRecv > 0 ? TimeSpan.FromTicks(globalMaxNs / 100) : TimeSpan.Zero;
            TimeSpan globalAvg = totalRecv > 0 ? TimeSpan.FromTicks(globalTotalNs / totalRecv / 100) : TimeSpan.Zero;

            return new FpingResult(hostResults, totalSent, totalRecv, aliveCount, unreachableCount,
                                  globalMin, globalMax, globalAvg, elapsed);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
            }
        }
    }
}
