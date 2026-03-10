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
    public class PingEngineTests
    {
        private static FpingOptions CreateOptions(int count = 1, int timeoutMs = 500, int retry = 0,
            int intervalMs = 10, int perHostIntervalMs = 1000, double backoff = 1.5, bool loop = false)
        {
            return new FpingOptions
            {
                Count = count,
                TimeoutMs = timeoutMs,
                Retry = retry,
                IntervalMs = intervalMs,
                PerHostIntervalMs = perHostIntervalMs,
                PacketSize = 56,
                Backoff = backoff,
                Loop = loop,
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
        public void Test_SingleHost_SinglePing_Success()
        {
            var options = CreateOptions(count: 1, timeoutMs: 2000, retry: 0);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            var timeouts = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);
            engine.OnTimeout += (_, e) => timeouts.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // Verify reply fired
            Assert.Single(replies);
            Assert.True(replies[0].Success);
            Assert.Empty(timeouts);

            // Verify stats
            var (totalSent, totalRecv, aliveCount, unreachableCount, _, _, _) = engine.Stats.GetGlobalStats();
            Assert.Equal(1, totalSent);
            Assert.Equal(1, totalRecv);
            Assert.Equal(1, aliveCount);
            Assert.Equal(0, unreachableCount);

            // Verify host state
            Assert.True(hosts[0].IsAlive);
            Assert.Equal(1, hosts[0].NumSent);
            Assert.Equal(1, hosts[0].NumRecv);
        }

        [Fact]
        public void Test_SingleHost_Timeout()
        {
            var options = CreateOptions(count: 1, timeoutMs: 200, retry: 0);
            using var socket = new MockIcmpSocket(isRaw: false)
            {
                SimulateTimeout = true
            };
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            var timeouts = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);
            engine.OnTimeout += (_, e) => timeouts.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // Verify timeout fired
            Assert.Empty(replies);
            Assert.Single(timeouts);
            Assert.False(timeouts[0].Success);

            // Verify stats
            var (totalSent, totalRecv, _, unreachableCount, _, _, _) = engine.Stats.GetGlobalStats();
            Assert.Equal(1, totalSent);
            Assert.Equal(0, totalRecv);
            Assert.Equal(1, unreachableCount);

            // Verify host state
            Assert.False(hosts[0].IsAlive);
        }

        [Fact]
        public void Test_SingleHost_Retry_ThenTimeout()
        {
            var options = CreateOptions(count: 1, timeoutMs: 200, retry: 2);
            using var socket = new MockIcmpSocket(isRaw: false)
            {
                SimulateTimeout = true
            };
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            var timeouts = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);
            engine.OnTimeout += (_, e) => timeouts.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // Verify all attempts timed out: 1 original + 2 retries = 3 timeout events
            Assert.Empty(replies);
            Assert.Equal(3, timeouts.Count);
            foreach (var t in timeouts)
            {
                Assert.False(t.Success);
            }

            // Verify total sends: 1 original + 2 retries = 3
            Assert.Equal(3, hosts[0].NumSent);
            Assert.Equal(3, socket.SentPackets.Count);
        }

        [Fact]
        public void Test_MultipleHosts_AllReply()
        {
            var options = CreateOptions(count: 1, timeoutMs: 2000, retry: 0);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(3, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            var timeouts = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);
            engine.OnTimeout += (_, e) => timeouts.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // All 3 hosts should reply
            Assert.Equal(3, replies.Count);
            Assert.Empty(timeouts);

            var (totalSent, totalRecv, aliveCount, unreachableCount, _, _, _) = engine.Stats.GetGlobalStats();
            Assert.Equal(3, totalSent);
            Assert.Equal(3, totalRecv);
            Assert.Equal(3, aliveCount);
            Assert.Equal(0, unreachableCount);

            // All hosts should be alive
            foreach (var host in hosts)
            {
                Assert.True(host.IsAlive);
                Assert.Equal(1, host.NumSent);
                Assert.Equal(1, host.NumRecv);
            }
        }

        [Fact]
        public void Test_MultipleHosts_MixedResults()
        {
            var options = CreateOptions(count: 1, timeoutMs: 200, retry: 0);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(3, options);

            // The sequence numbers are assigned incrementally by SequenceMap.
            // Host 0 gets seq 0, Host 1 gets seq 1, Host 2 gets seq 2.
            // Set seq 1 to timeout (Host 1).
            socket.TimeoutSequences.Add(1);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            var timeouts = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);
            engine.OnTimeout += (_, e) => timeouts.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // 2 hosts should reply, 1 should timeout
            Assert.Equal(2, replies.Count);
            Assert.Single(timeouts);

            var (totalSent, totalRecv, aliveCount, unreachableCount, _, _, _) = engine.Stats.GetGlobalStats();
            Assert.Equal(3, totalSent);
            Assert.Equal(2, totalRecv);
            Assert.Equal(2, aliveCount);
            Assert.Equal(1, unreachableCount);

            // Host 0 and Host 2 alive, Host 1 not
            Assert.True(hosts[0].IsAlive);
            Assert.False(hosts[1].IsAlive);
            Assert.True(hosts[2].IsAlive);
        }

        [Fact]
        public void Test_SingleHost_MultiplePings_Count3()
        {
            var options = CreateOptions(count: 3, timeoutMs: 2000, retry: 0, perHostIntervalMs: 10);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            var timeouts = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);
            engine.OnTimeout += (_, e) => timeouts.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // Verify 3 sends, 3 replies
            Assert.Equal(3, replies.Count);
            Assert.Empty(timeouts);
            Assert.True(replies.All(r => r.Success));

            // Verify 3 packets sent
            Assert.Equal(3, socket.SentPackets.Count);

            // Verify stats show 3/3
            var (totalSent, totalRecv, aliveCount, unreachableCount, _, _, _) = engine.Stats.GetGlobalStats();
            Assert.Equal(3, totalSent);
            Assert.Equal(3, totalRecv);
            Assert.Equal(1, aliveCount);
            Assert.Equal(0, unreachableCount);

            // Verify host-level stats
            Assert.Equal(3, hosts[0].NumSent);
            Assert.Equal(3, hosts[0].NumRecv);
            Assert.True(hosts[0].IsAlive);
        }

        [Fact]
        public void Test_Retry_WithBackoff()
        {
            // Count=1, Retry=2, Backoff=1.5, TimeoutMs=100
            // First ping times out, retry 1 times out, retry 2 times out.
            // Timeout sequence should be: 100ms, 150ms (100*1.5), 225ms (150*1.5)
            var options = CreateOptions(count: 1, timeoutMs: 100, retry: 2, backoff: 1.5);
            using var socket = new MockIcmpSocket(isRaw: false)
            {
                SimulateTimeout = true
            };
            var hosts = CreateHosts(1, options);

            var engine = new PingEngine(options, socket, hosts);

            var timeoutEvents = new List<PingReply>();
            engine.OnTimeout += (_, e) => timeoutEvents.Add(e);

            engine.Initialize();

            var sw = Stopwatch.StartNew();
            engine.Run(CancellationToken.None);
            sw.Stop();

            // All 3 attempts should time out: 1 original + 2 retries = 3
            Assert.Equal(3, timeoutEvents.Count);
            Assert.Equal(3, hosts[0].NumSent);

            // The total elapsed time should reflect escalating timeouts:
            // 100ms + 150ms + 225ms = 475ms minimum
            // Allow generous tolerance since mock timing is not exact
            Assert.True(sw.Elapsed.TotalMilliseconds >= 350,
                $"Expected total time >= 350ms (due to backoff escalation), got {sw.Elapsed.TotalMilliseconds:F0}ms");
        }

        [Fact]
        public void Test_MultipleHosts_RoundRobin_Order()
        {
            // 5 hosts, Count=2, verify pings are sent in round-robin order:
            // First round: host 0, 1, 2, 3, 4
            // Second round: host 0, 1, 2, 3, 4
            var options = CreateOptions(count: 2, timeoutMs: 2000, retry: 0, intervalMs: 1, perHostIntervalMs: 10);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(5, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // All 10 pings should succeed (5 hosts * 2 count)
            Assert.Equal(10, replies.Count);
            Assert.Equal(10, socket.SentPackets.Count);

            // Verify that in the first 5 sends, each host appears once (round-robin).
            // The sent packets correspond to sequence numbers that map to host indices.
            // Since hosts are scheduled in order with staggered times, first 5 sends
            // should cover all 5 hosts.
            var firstRoundDestinations = new HashSet<EndPoint>();
            for (int i = 0; i < 5; i++)
            {
                firstRoundDestinations.Add(socket.SentPackets[i].Dest);
            }
            Assert.Equal(5, firstRoundDestinations.Count);

            // Verify all hosts received 2 pings
            foreach (var host in hosts)
            {
                Assert.Equal(2, host.NumSent);
                Assert.Equal(2, host.NumRecv);
            }
        }

        [Fact]
        public void Test_GlobalIntervalRateLimit()
        {
            // 3 hosts with IntervalMs=10, verify sends are spaced at least ~10ms apart
            var options = CreateOptions(count: 1, timeoutMs: 2000, retry: 0, intervalMs: 10);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(3, options);

            var engine = new PingEngine(options, socket, hosts);

            // Track send timestamps
            var sendTimes = new List<long>();
            var originalSendTo = socket;
            // We capture timing via the SentPackets list and engine diagnostics

            engine.Initialize();
            var sw = Stopwatch.StartNew();
            engine.Run(CancellationToken.None);
            sw.Stop();

            // 3 hosts at 10ms interval = minimum 20ms for all 3 sends (first immediate, then 10ms each)
            // The total wall time should be >= 20ms (tolerant to avoid flakiness)
            Assert.Equal(3, socket.SentPackets.Count);
            // With 10ms interval between 3 sends, minimum elapsed is 20ms
            // Use a lower bound with tolerance for timing jitter
            Assert.True(sw.Elapsed.TotalMilliseconds >= 15,
                $"Expected >= 15ms for 3 sends at 10ms interval, got {sw.Elapsed.TotalMilliseconds:F1}ms");
        }

        [Fact]
        public void Test_PerHostInterval_Count2()
        {
            // 2 hosts, Count=2, PerHostIntervalMs=100
            // Verify second ping to same host is delayed by ~100ms from the first reply
            var options = CreateOptions(count: 2, timeoutMs: 2000, retry: 0, intervalMs: 1, perHostIntervalMs: 100);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(2, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);

            engine.Initialize();
            var sw = Stopwatch.StartNew();
            engine.Run(CancellationToken.None);
            sw.Stop();

            // All 4 pings should succeed (2 hosts * 2 count)
            Assert.Equal(4, replies.Count);
            Assert.Equal(4, socket.SentPackets.Count);

            // The per-host interval should enforce ~100ms between first and second ping per host.
            // Total elapsed should be >= ~100ms (the per-host interval gap)
            Assert.True(sw.Elapsed.TotalMilliseconds >= 80,
                $"Expected >= 80ms due to 100ms per-host interval, got {sw.Elapsed.TotalMilliseconds:F1}ms");
        }

        [Fact]
        public void Test_CancellationToken_StopsLoop()
        {
            // Loop=true, cancel after 200ms, verify engine stops and returns partial results
            var options = CreateOptions(count: 1, timeoutMs: 2000, retry: 0, intervalMs: 1, perHostIntervalMs: 10, loop: true);
            using var socket = new MockIcmpSocket(isRaw: false);
            var hosts = CreateHosts(2, options);

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);

            engine.Initialize();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            var sw = Stopwatch.StartNew();
            engine.Run(cts.Token);
            sw.Stop();

            // Engine should have stopped due to cancellation
            Assert.True(sw.Elapsed.TotalMilliseconds < 2000,
                $"Engine should have stopped within ~200ms, took {sw.Elapsed.TotalMilliseconds:F0}ms");

            // Should have some replies (loop mode sends continuously)
            Assert.True(replies.Count > 0, "Expected at least some replies before cancellation");

            // Both hosts should have been pinged at least once
            Assert.True(hosts[0].NumSent >= 1, "Host 0 should have been pinged at least once");
            Assert.True(hosts[1].NumSent >= 1, "Host 1 should have been pinged at least once");
        }

        [Fact]
        public void Test_LargeHostCount_100()
        {
            // 100 hosts, Count=1, all reply, verify all 100 marked alive
            var options = CreateOptions(count: 1, timeoutMs: 2000, retry: 0, intervalMs: 1);
            using var socket = new MockIcmpSocket(isRaw: false);

            // Create 100 hosts using a wider IP range
            var hosts = new List<HostEntry>();
            int maxEvents = Math.Max(1, options.Count);
            for (int i = 0; i < 100; i++)
            {
                var ip = IPAddress.Parse($"10.0.{i / 256}.{(i % 256) + 1}");
                var entry = new HostEntry(i, ip.ToString(), maxEvents)
                {
                    Address = ip,
                    AddressFamily = AddressFamily.InterNetwork,
                    EndPoint = new IPEndPoint(ip, 0),
                    DnsResolved = true,
                };
                hosts.Add(entry);
            }

            var engine = new PingEngine(options, socket, hosts);

            var replies = new List<PingReply>();
            var timeouts = new List<PingReply>();
            engine.OnReply += (_, e) => replies.Add(e);
            engine.OnTimeout += (_, e) => timeouts.Add(e);

            engine.Initialize();
            engine.Run(CancellationToken.None);

            // All 100 should reply
            Assert.Equal(100, replies.Count);
            Assert.Empty(timeouts);

            // Verify stats
            var (totalSent, totalRecv, aliveCount, unreachableCount, _, _, _) = engine.Stats.GetGlobalStats();
            Assert.Equal(100, totalSent);
            Assert.Equal(100, totalRecv);
            Assert.Equal(100, aliveCount);
            Assert.Equal(0, unreachableCount);

            // All hosts should be alive
            foreach (var host in hosts)
            {
                Assert.True(host.IsAlive, $"Host {host.Name} should be alive");
                Assert.Equal(1, host.NumSent);
                Assert.Equal(1, host.NumRecv);
            }
        }
    }
}
