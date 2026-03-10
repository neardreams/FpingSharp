using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Threading;
using FpingSharp;
using FpingSharp.Exceptions;

namespace FpingSharp.Benchmark
{
    internal static class ScenarioBenchmark
    {
        private sealed class ScenarioConfig
        {
            public string Name { get; init; } = "";
            public string Description { get; init; } = "";
            public List<string> Targets { get; init; } = new();
            public int Count { get; init; } = 1;
            public int TimeoutMs { get; init; } = 500;
            public int Retry { get; init; } = 0;
            public double Backoff { get; init; } = 1.5;
            public int IntervalMs { get; init; } = 10;
            public int PerHostIntervalMs { get; init; } = 1000;
            public string FpingArgs { get; init; } = "";
            public int Parallelism { get; init; } = 256;
        }

        private sealed class ScenarioResult
        {
            public string RunnerName { get; init; } = "";
            public double? Seconds { get; set; }
            public int Alive { get; set; }
            public int Unreachable { get; set; }
            public bool Skipped { get; set; }
            public string SkipReason { get; set; } = "";
        }

        public static async Task RunAllScenarios()
        {
            Console.WriteLine("=== FpingSharp Multi-Scenario Benchmark ===");
            Console.WriteLine();

            var scenarios = BuildScenarios();

            foreach (var scenario in scenarios)
            {
                await RunScenario(scenario);
            }

            Console.WriteLine("=== All scenarios complete ===");
        }

        private static List<ScenarioConfig> BuildScenarios()
        {
            var scenarios = new List<ScenarioConfig>();

            // Scenario 1: Single host, multiple pings (localhost)
            scenarios.Add(new ScenarioConfig
            {
                Name = "Scenario 1: Single host, multiple pings",
                Description = "Target: 127.0.0.1, Count=10, tests throughput for single-host repeated pings",
                Targets = new List<string> { "127.0.0.1" },
                Count = 10,
                TimeoutMs = 1000,
                Retry = 0,
                IntervalMs = 1,
                PerHostIntervalMs = 100,
                FpingArgs = "-C 10 -q -i 1 -p 100 -t 1000 127.0.0.1",
                Parallelism = 1,
            });

            // Scenario 2: Small batch, mixed reachable/unreachable
            var scenario2Targets = new List<string> { "127.0.0.1", "192.168.1.1" };
            for (int i = 1; i <= 8; i++)
            {
                scenario2Targets.Add($"192.0.2.{i}");
            }
            scenarios.Add(new ScenarioConfig
            {
                Name = "Scenario 2: Small batch, mixed reachable/unreachable",
                Description = "Targets: 127.0.0.1 + gateway + 8 TEST-NET IPs, Count=1, tests mixed results behavior",
                Targets = scenario2Targets,
                Count = 1,
                TimeoutMs = 500,
                Retry = 0,
                IntervalMs = 10,
                PerHostIntervalMs = 1000,
                FpingArgs = "",  // built dynamically
                Parallelism = 10,
            });

            // Scenario 3: Medium batch, with retries
            var scenario3Targets = GenerateSubnetTargets(500);
            scenarios.Add(new ScenarioConfig
            {
                Name = "Scenario 3: Medium batch, with retries",
                Description = "Targets: 500 hosts from 192.168.0.0/16, Count=1, Retry=2, Backoff=1.5, tests retry/backoff overhead",
                Targets = scenario3Targets,
                Count = 1,
                TimeoutMs = 500,
                Retry = 2,
                Backoff = 1.5,
                IntervalMs = 1,
                PerHostIntervalMs = 1000,
                FpingArgs = "",  // built dynamically
                Parallelism = 256,
            });

            // Scenario 4: Large batch, multiple pings per host
            var scenario4Targets = GenerateSubnetTargets(1000);
            scenarios.Add(new ScenarioConfig
            {
                Name = "Scenario 4: Large batch, multiple pings per host",
                Description = "Targets: 1000 hosts from 192.168.0.0/16, Count=3, tests multi-ping throughput",
                Targets = scenario4Targets,
                Count = 3,
                TimeoutMs = 500,
                Retry = 0,
                IntervalMs = 1,
                PerHostIntervalMs = 100,
                FpingArgs = "",  // built dynamically
                Parallelism = 256,
            });

            // Scenario 5: Aggressive scan (minimum intervals)
            var scenario5Targets = GenerateSubnetTargets(5000);
            scenarios.Add(new ScenarioConfig
            {
                Name = "Scenario 5: Aggressive scan (minimum intervals)",
                Description = "Targets: 5000 hosts from 192.168.0.0/16, Count=1, TimeoutMs=200, tests maximum throughput",
                Targets = scenario5Targets,
                Count = 1,
                TimeoutMs = 200,
                Retry = 0,
                IntervalMs = 1,
                PerHostIntervalMs = 1000,
                FpingArgs = "",  // built dynamically
                Parallelism = 256,
            });

            return scenarios;
        }

        private static List<string> GenerateSubnetTargets(int count)
        {
            var targets = new List<string>(count);
            int generated = 0;
            for (int hi = 0; hi < 256 && generated < count; hi++)
            {
                for (int lo = 0; lo < 256 && generated < count; lo++)
                {
                    targets.Add($"192.168.{hi}.{lo}");
                    generated++;
                }
            }
            return targets;
        }

        private static string BuildFpingArgs(ScenarioConfig config)
        {
            if (!string.IsNullOrEmpty(config.FpingArgs))
                return config.FpingArgs;

            // Build fping args from config
            // fping -C <count> -q -i <interval> -p <perHostInterval> -t <timeout> -r <retry> -B <backoff>
            var args = $"-C {config.Count} -q -i {config.IntervalMs} -p {config.PerHostIntervalMs} -t {config.TimeoutMs} -r {config.Retry}";
            if (config.Backoff != 1.5)
                args += $" -B {config.Backoff}";
            return args;
        }

        private static async Task RunScenario(ScenarioConfig config)
        {
            Console.WriteLine($"{'=',-60}".Replace(' ', '='));
            Console.WriteLine($"  {config.Name}");
            Console.WriteLine($"{'=',-60}".Replace(' ', '='));
            Console.WriteLine($"  {config.Description}");
            Console.WriteLine($"  Hosts: {config.Targets.Count:N0}  Count: {config.Count}  Timeout: {config.TimeoutMs}ms  Retry: {config.Retry}  Backoff: {config.Backoff}  Interval: {config.IntervalMs}ms  PerHost: {config.PerHostIntervalMs}ms");
            Console.WriteLine();

            var results = new List<ScenarioResult>();

            // --- fping ---
            var fpingResult = await RunFping(config);
            results.Add(fpingResult);

            // --- FpingSharp ---
            var fpingSharpResult = RunFpingSharp(config);
            results.Add(fpingSharpResult);

            // --- C# Parallel Ping ---
            var parallelResult = await RunParallelPing(config);
            results.Add(parallelResult);

            // Print comparison table
            PrintComparisonTable(config, results);
            Console.WriteLine();
        }

        private static async Task<ScenarioResult> RunFping(ScenarioConfig config)
        {
            var result = new ScenarioResult { RunnerName = "fping (native)" };

            try
            {
                var whichProc = Process.Start(new ProcessStartInfo
                {
                    FileName = "which",
                    Arguments = "fping",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                });
                whichProc?.WaitForExit();

                if (whichProc is null || whichProc.ExitCode != 0)
                {
                    result.Skipped = true;
                    result.SkipReason = "fping not found in PATH";
                    return result;
                }

                string fpingArgs = BuildFpingArgs(config);

                // For scenarios with many targets, use a temp file
                string? tempFile = null;
                try
                {
                    if (config.Targets.Count > 1)
                    {
                        tempFile = Path.GetTempFileName();
                        await File.WriteAllLinesAsync(tempFile, config.Targets);
                        fpingArgs += $" -f {tempFile}";
                    }
                    else
                    {
                        // Single target: just pass on command line
                        // Remove the -f flag usage, fping args already has the target for scenario 1
                    }

                    Console.Error.WriteLine($"  [fping] Starting with {config.Targets.Count:N0} targets...");
                    var sw = Stopwatch.StartNew();

                    using var fpingProc = new Process();
                    fpingProc.StartInfo = new ProcessStartInfo
                    {
                        FileName = "fping",
                        Arguments = fpingArgs,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                    };

                    fpingProc.Start();

                    var stderrTask = fpingProc.StandardError.ReadToEndAsync();
                    var stdoutTask = fpingProc.StandardOutput.ReadToEndAsync();

                    await fpingProc.WaitForExitAsync();
                    sw.Stop();

                    var stderr = await stderrTask;
                    _ = await stdoutTask;

                    result.Seconds = sw.Elapsed.TotalSeconds;

                    foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var colonIdx = line.LastIndexOf(':');
                        if (colonIdx < 0) continue;

                        var rttPart = line[(colonIdx + 1)..].Trim();
                        // For multi-count, rttPart may contain space-separated values
                        var rttValues = rttPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        bool anySuccess = false;
                        foreach (var val in rttValues)
                        {
                            if (val != "-")
                            {
                                anySuccess = true;
                                break;
                            }
                        }
                        if (anySuccess)
                            result.Alive++;
                        else
                            result.Unreachable++;
                    }

                    Console.Error.WriteLine("  [fping] Complete.");
                }
                finally
                {
                    if (tempFile != null)
                    {
                        try { File.Delete(tempFile); } catch { /* best effort */ }
                    }
                }
            }
            catch (Exception ex)
            {
                result.Skipped = true;
                result.SkipReason = $"Error: {ex.Message}";
            }

            return result;
        }

        private static ScenarioResult RunFpingSharp(ScenarioConfig config)
        {
            var result = new ScenarioResult { RunnerName = "FpingSharp" };

            try
            {
                var options = new FpingOptions
                {
                    Count = config.Count,
                    TimeoutMs = config.TimeoutMs,
                    Retry = config.Retry,
                    Backoff = config.Backoff,
                    IntervalMs = config.IntervalMs,
                    PerHostIntervalMs = config.PerHostIntervalMs,
                };

                using var client = new FpingClient(options);

                Console.Error.WriteLine($"  [FpingSharp] Starting with {config.Targets.Count:N0} targets...");
                var sw = Stopwatch.StartNew();

                var pingResult = client.Run(config.Targets);

                sw.Stop();
                result.Seconds = sw.Elapsed.TotalSeconds;
                result.Alive = pingResult.AliveCount;
                result.Unreachable = pingResult.UnreachableCount;

                Console.Error.WriteLine("  [FpingSharp] Complete.");
            }
            catch (SocketPermissionException ex)
            {
                result.Skipped = true;
                result.SkipReason = $"Permission denied: {ex.Message}";
            }
            catch (Exception ex)
            {
                result.Skipped = true;
                result.SkipReason = $"Error: {ex.Message}";
            }

            return result;
        }

        private static async Task<ScenarioResult> RunParallelPing(ScenarioConfig config)
        {
            var result = new ScenarioResult { RunnerName = $"C# Parallel Ping ({config.Parallelism} concurrent)" };

            try
            {
                Console.Error.WriteLine($"  [Parallel Ping] Starting with {config.Targets.Count:N0} targets, count={config.Count}...");
                int alive = 0;
                int unreachable = 0;

                var sw = Stopwatch.StartNew();

                await Parallel.ForEachAsync(
                    config.Targets,
                    new ParallelOptions { MaxDegreeOfParallelism = config.Parallelism },
                    async (target, ct) =>
                    {
                        bool anySuccess = false;
                        for (int c = 0; c < config.Count; c++)
                        {
                            using var pingSender = new Ping();
                            try
                            {
                                var reply = await pingSender.SendPingAsync(target, config.TimeoutMs);
                                if (reply.Status == IPStatus.Success)
                                    anySuccess = true;
                            }
                            catch
                            {
                                // treat as failure
                            }
                        }

                        if (anySuccess)
                            Interlocked.Increment(ref alive);
                        else
                            Interlocked.Increment(ref unreachable);
                    });

                sw.Stop();
                result.Seconds = sw.Elapsed.TotalSeconds;
                result.Alive = alive;
                result.Unreachable = unreachable;

                Console.Error.WriteLine("  [Parallel Ping] Complete.");
            }
            catch (Exception ex)
            {
                result.Skipped = true;
                result.SkipReason = $"Error: {ex.Message}";
            }

            return result;
        }

        private static void PrintComparisonTable(ScenarioConfig config, List<ScenarioResult> results)
        {
            int totalHosts = config.Targets.Count;

            // Header
            Console.WriteLine($"  {"Runner",-40} {"Elapsed",10} {"Alive",8} {"Unreach",8} {"Rate",14}");
            Console.WriteLine($"  {new string('-', 40)} {new string('-', 10)} {new string('-', 8)} {new string('-', 8)} {new string('-', 14)}");

            foreach (var r in results)
            {
                if (r.Skipped)
                {
                    Console.WriteLine($"  {r.RunnerName,-40} {"SKIPPED",10} {"",8} {"",8} {r.SkipReason,14}");
                }
                else if (r.Seconds.HasValue)
                {
                    double rate = totalHosts / r.Seconds.Value;
                    Console.WriteLine($"  {r.RunnerName,-40} {r.Seconds.Value,9:F2}s {r.Alive,8:N0} {r.Unreachable,8:N0} {rate,10:N0} h/s");
                }
                else
                {
                    Console.WriteLine($"  {r.RunnerName,-40} {"N/A",10}");
                }
            }

            Console.WriteLine();

            // Pairwise comparisons
            var fpingResult = results.Find(r => r.RunnerName.Contains("fping (native)"));
            var sharpResult = results.Find(r => r.RunnerName.Contains("FpingSharp"));
            var parallelResult = results.Find(r => r.RunnerName.Contains("Parallel Ping"));

            if (sharpResult?.Seconds != null && fpingResult?.Seconds != null)
            {
                double ratio = sharpResult.Seconds.Value / fpingResult.Seconds.Value;
                string label = ratio >= 1.0 ? "slower" : "faster";
                double displayRatio = ratio >= 1.0 ? ratio : 1.0 / ratio;
                Console.WriteLine($"  FpingSharp vs fping:          {displayRatio:F2}x {label}");
            }
            else
            {
                Console.WriteLine($"  FpingSharp vs fping:          N/A (one or both skipped)");
            }

            if (sharpResult?.Seconds != null && parallelResult?.Seconds != null)
            {
                double ratio = parallelResult.Seconds.Value / sharpResult.Seconds.Value;
                string label = ratio >= 1.0 ? "faster" : "slower";
                double displayRatio = ratio >= 1.0 ? ratio : 1.0 / ratio;
                Console.WriteLine($"  FpingSharp vs C# Parallel:    {displayRatio:F2}x {label}");
            }
            else
            {
                Console.WriteLine($"  FpingSharp vs C# Parallel:    N/A (one or both skipped)");
            }
        }
    }
}
