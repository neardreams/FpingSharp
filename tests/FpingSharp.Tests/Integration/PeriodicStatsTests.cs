using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FpingSharp.Internal;
using Xunit;

namespace FpingSharp.Tests.Integration
{
    public class PeriodicStatsTests
    {
        private static FpingOptions CreateOptions(int? statsIntervalMs, bool loop = true,
            int timeoutMs = 2000, int intervalMs = 10, int perHostIntervalMs = 50)
        {
            return new FpingOptions
            {
                Loop = loop,
                Count = 1,
                TimeoutMs = timeoutMs,
                Retry = 0,
                IntervalMs = intervalMs,
                PerHostIntervalMs = perHostIntervalMs,
                PacketSize = 56,
                Backoff = 1.0,
                StatsIntervalMs = statsIntervalMs,
            };
        }

        private static List<HostEntry> CreateHosts(int count, FpingOptions options)
        {
            var hosts = new List<HostEntry>();
            int maxEvents = options.Loop ? 1 : Math.Max(1, options.Count);
            for (int i = 0; i < count; i++)
            {
                var ip = IPAddress.Parse($"10.0.0.{i + 1}");
                var entry = new HostEntry(i, ip.ToString(), maxEvents)
                {
                    Address = ip,
                    AddressFamily = AddressFamily.InterNetwork,
                    EndPoint = new IPEndPoint(ip, 0),
                    DnsResolved = true,
                };
                hosts.Add(entry);
            }
            return hosts;
        }

        [Fact]
        public void Test_PeriodicStats_FiresMultipleTimes()
        {
            var options = CreateOptions(statsIntervalMs: 100);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(2, options);

            var engine = new PingEngine(options, socket, hosts);
            var statsEvents = new List<IntervalStats>();
            engine.OnIntervalStats += (_, e) => statsEvents.Add(e);

            engine.Initialize();

            // Run for ~350ms then cancel
            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            engine.Run(cts.Token);

            // With 100ms interval and 350ms run, expect 2-3 callbacks
            Assert.True(statsEvents.Count >= 2,
                $"Expected at least 2 interval stats callbacks, got {statsEvents.Count}");
            Assert.True(statsEvents.Count <= 4,
                $"Expected at most 4 interval stats callbacks, got {statsEvents.Count}");
        }

        [Fact]
        public void Test_PeriodicStats_IntervalValuesAreResetBetweenCallbacks()
        {
            var options = CreateOptions(statsIntervalMs: 100, perHostIntervalMs: 20);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);
            var statsEvents = new List<IntervalStats>();
            engine.OnIntervalStats += (_, e) => statsEvents.Add(e);

            engine.Initialize();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(350));
            engine.Run(cts.Token);

            Assert.True(statsEvents.Count >= 2,
                $"Expected at least 2 interval stats callbacks, got {statsEvents.Count}");

            // Each callback should have interval-only values, not cumulative
            foreach (var stats in statsEvents)
            {
                Assert.Single(stats.Hosts);
                var hostStat = stats.Hosts[0];
                // In 100ms with 20ms per-host interval, we expect roughly 5 pings per interval
                // The key assertion: each interval's Sent count should be small (interval, not cumulative)
                Assert.True(hostStat.Sent > 0, "Each interval should have at least 1 sent");
                Assert.True(hostStat.Sent <= 15,
                    $"Interval sent count ({hostStat.Sent}) seems too high for a 100ms interval — might be cumulative");
            }
        }

        [Fact]
        public void Test_PeriodicStats_NullInterval_DoesNotFire()
        {
            var options = CreateOptions(statsIntervalMs: null);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);
            var statsEvents = new List<IntervalStats>();
            engine.OnIntervalStats += (_, e) => statsEvents.Add(e);

            engine.Initialize();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            engine.Run(cts.Token);

            Assert.Empty(statsEvents);
        }

        [Fact]
        public void Test_PeriodicStats_ZeroInterval_DoesNotFire()
        {
            var options = CreateOptions(statsIntervalMs: 0);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);
            var statsEvents = new List<IntervalStats>();
            engine.OnIntervalStats += (_, e) => statsEvents.Add(e);

            engine.Initialize();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            engine.Run(cts.Token);

            Assert.Empty(statsEvents);
        }

        [Fact]
        public void Test_PeriodicStats_ElapsedIncreases()
        {
            var options = CreateOptions(statsIntervalMs: 80);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);
            var statsEvents = new List<IntervalStats>();
            engine.OnIntervalStats += (_, e) => statsEvents.Add(e);

            engine.Initialize();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            engine.Run(cts.Token);

            Assert.True(statsEvents.Count >= 2,
                $"Expected at least 2 callbacks, got {statsEvents.Count}");

            // Elapsed should increase with each callback
            for (int i = 1; i < statsEvents.Count; i++)
            {
                Assert.True(statsEvents[i].Elapsed > statsEvents[i - 1].Elapsed,
                    $"Elapsed should increase: [{i - 1}]={statsEvents[i - 1].Elapsed}, [{i}]={statsEvents[i].Elapsed}");
            }
        }
    }
}
