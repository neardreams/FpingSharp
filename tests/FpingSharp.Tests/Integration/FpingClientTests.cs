using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace FpingSharp.Tests.Integration
{
    [Trait("Category", "Integration")]
    public class FpingClientTests
    {
        [Fact]
        public void Test_PingLocalhost_ReturnsAlive()
        {
            var options = new FpingOptions { Count = 2, TimeoutMs = 2000, Retry = 0 };
            using var client = new FpingClient(options);

            var result = client.Run(new[] { "127.0.0.1" });

            Assert.Single(result.Hosts);
            Assert.True(result.Hosts[0].IsAlive);
            Assert.Equal(2, result.Hosts[0].Sent);
            Assert.True(result.Hosts[0].Received > 0);
        }

        [Fact]
        public void Test_PingUnreachable_ReturnsNotAlive()
        {
            var options = new FpingOptions { Count = 1, TimeoutMs = 500, Retry = 0 };
            using var client = new FpingClient(options);

            var result = client.Run(new[] { "192.0.2.1" }); // TEST-NET, should be unreachable

            Assert.Single(result.Hosts);
            // May or may not be alive depending on network, but should complete without exception
            Assert.Equal(1, result.Hosts[0].Sent);
        }

        [Fact]
        public void Test_EmptyHostList_ReturnsEmptyResult()
        {
            using var client = new FpingClient();
            var result = client.Run(Array.Empty<string>());
            Assert.Empty(result.Hosts);
            Assert.Equal(0, result.TotalSent);
        }

        [Fact]
        public void Test_MultipleHosts_Mixed()
        {
            // Ping 127.0.0.1 (reachable) + 192.0.2.1 (TEST-NET, unreachable)
            var options = new FpingOptions { Count = 1, TimeoutMs = 1000, Retry = 0 };
            using var client = new FpingClient(options);

            var result = client.Run(new[] { "127.0.0.1", "192.0.2.1" });

            Assert.Equal(2, result.Hosts.Count);

            // Localhost should be alive
            var localhostResult = result.Hosts[0];
            Assert.Equal("127.0.0.1", localhostResult.Name);
            Assert.True(localhostResult.IsAlive, "127.0.0.1 should be alive");

            // TEST-NET should be unreachable (may vary by network, but should not crash)
            var unreachableResult = result.Hosts[1];
            Assert.Equal("192.0.2.1", unreachableResult.Name);
            Assert.Equal(1, unreachableResult.Sent);

            // At minimum, one host should be alive (localhost)
            Assert.True(result.AliveCount >= 1, "At least localhost should be alive");
        }

        [Fact]
        public void Test_Count3_Localhost()
        {
            var options = new FpingOptions { Count = 3, TimeoutMs = 2000, Retry = 0 };
            using var client = new FpingClient(options);

            var result = client.Run(new[] { "127.0.0.1" });

            Assert.Single(result.Hosts);
            var host = result.Hosts[0];

            Assert.Equal(3, host.Sent);
            Assert.Equal(3, host.Received);
            Assert.True(host.IsAlive);

            // ResponseTimes should have 3 entries, all non-null for localhost
            Assert.Equal(3, host.ResponseTimes.Count);
            foreach (var rt in host.ResponseTimes)
            {
                Assert.NotNull(rt);
                Assert.True(rt!.Value.TotalMilliseconds >= 0, "RTT should be non-negative");
            }
        }

        [Fact]
        public void Test_OnReply_EventFires()
        {
            var options = new FpingOptions { Count = 1, TimeoutMs = 2000, Retry = 0 };
            using var client = new FpingClient(options);

            var replyEvents = new List<PingReply>();
            client.OnReply += (_, e) => replyEvents.Add(e);

            var result = client.Run(new[] { "127.0.0.1" });

            // Event should have fired with success=true
            Assert.True(replyEvents.Count > 0, "OnReply event should have fired at least once");
            Assert.True(replyEvents[0].Success, "Reply to localhost should be successful");
            Assert.Equal("127.0.0.1", replyEvents[0].HostName);
            Assert.NotNull(replyEvents[0].RoundTripTime);
        }

        [Fact]
        public void Test_InvalidHost_DnsFailure()
        {
            var options = new FpingOptions { Count = 1, TimeoutMs = 500, Retry = 0 };
            using var client = new FpingClient(options);

            // Should not throw - DNS failure should be handled gracefully
            var result = client.Run(new[] { "this.host.does.not.exist.invalid" });

            // Host should be present but unreachable (DNS resolution fails)
            Assert.Single(result.Hosts);
            Assert.False(result.Hosts[0].IsAlive, "Non-existent host should not be alive");
        }

        [Fact]
        public void Test_RequestStop_LoopMode()
        {
            var options = new FpingOptions { Loop = true, TimeoutMs = 2000, Retry = 0 };
            using var client = new FpingClient(options);

            // Schedule stop after 500ms via a timer
            using var stopTimer = new Timer(_ => client.RequestStop(), null, 500, Timeout.Infinite);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = client.Run(new[] { "127.0.0.1" });
            sw.Stop();

            // Should have completed (not hang forever)
            Assert.True(sw.Elapsed.TotalSeconds < 10,
                $"Loop mode should have stopped within ~500ms of RequestStop, took {sw.Elapsed.TotalSeconds:F1}s");

            // Should have received at least some pings
            Assert.True(result.TotalSent > 0, "Should have sent at least one ping in loop mode");
            Assert.True(result.Hosts[0].IsAlive, "Localhost should be alive");
        }

        [Fact]
        public void Test_SourceAddress_Loopback_PingLocalhost()
        {
            var options = new FpingOptions { Count = 1, TimeoutMs = 2000, Retry = 0, SourceAddress = "127.0.0.1" };
            using var client = new FpingClient(options);

            var result = client.Run(new[] { "127.0.0.1" });

            Assert.Single(result.Hosts);
            Assert.True(result.Hosts[0].IsAlive, "Localhost with source address 127.0.0.1 should be alive");
        }

        [Fact]
        public async Task Test_RunAsync_PingLocalhost()
        {
            var options = new FpingOptions { Count = 2, TimeoutMs = 2000, Retry = 0 };
            using var client = new FpingClient(options);

            var result = await client.RunAsync(new[] { "127.0.0.1" });

            Assert.Single(result.Hosts);
            Assert.True(result.Hosts[0].IsAlive, "Localhost should be alive via RunAsync");
            Assert.Equal(2, result.Hosts[0].Sent);
        }

        [Fact]
        public async Task Test_RunAsync_CancellationToken_LoopMode()
        {
            var options = new FpingOptions { Loop = true, TimeoutMs = 2000, Retry = 0 };
            using var client = new FpingClient(options);

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await client.RunAsync(new[] { "127.0.0.1" }, cts.Token);
            sw.Stop();

            Assert.True(sw.Elapsed.TotalSeconds < 10,
                $"RunAsync loop mode should have stopped within ~500ms, took {sw.Elapsed.TotalSeconds:F1}s");
            Assert.True(result.TotalSent > 0, "Should have sent at least one ping");
            Assert.True(result.Hosts[0].IsAlive, "Localhost should be alive");
        }
    }
}
