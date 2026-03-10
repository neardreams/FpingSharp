using System.Diagnostics;
using System.Net.NetworkInformation;
using FpingSharp;
using FpingSharp.Benchmark;
using FpingSharp.Exceptions;

const int DefaultHostCount = 65536;
const int DefaultParallelism = 256;
const int PingTimeoutMs = 500;

// Parse command-line arguments
int hostCount = DefaultHostCount;
int parallelism = DefaultParallelism;
bool dryRun = false;
bool runScenarios = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--scenarios")
    {
        runScenarios = true;
    }
    else if (args[i] == "--dry-run")
    {
        dryRun = true;
    }
    else if (args[i] == "--parallel" && i + 1 < args.Length)
    {
        if (int.TryParse(args[i + 1], out int p) && p > 0)
        {
            parallelism = p;
            i++;
        }
        else
        {
            Console.Error.WriteLine($"Invalid --parallel value: {args[i + 1]}");
            return 1;
        }
    }
    else if (int.TryParse(args[i], out int count) && count > 0)
    {
        hostCount = Math.Min(count, DefaultHostCount);
    }
    else if (args[i] == "--help" || args[i] == "-h")
    {
        Console.WriteLine("Usage: FpingSharp.Benchmark [host-count] [--parallel <N>] [--dry-run] [--scenarios]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine($"  host-count        Number of IPs to test from 192.168.0.0/16 (default: {DefaultHostCount})");
        Console.WriteLine($"  --parallel <N>    Degree of parallelism for C# Parallel Ping (default: {DefaultParallelism})");
        Console.WriteLine("  --dry-run         Generate IPs and print config without actually pinging");
        Console.WriteLine("  --scenarios       Run multi-scenario benchmarks instead of single /16 benchmark");
        Console.WriteLine("  --help, -h        Show this help message");
        return 0;
    }
}

if (runScenarios)
{
    await ScenarioBenchmark.RunAllScenarios();
    return 0;
}

// Generate target IPs: 192.168.0.0 through 192.168.255.255
Console.Error.WriteLine("Generating target IP addresses...");
var allTargets = new List<string>(hostCount);
int generated = 0;
for (int hi = 0; hi < 256 && generated < hostCount; hi++)
{
    for (int lo = 0; lo < 256 && generated < hostCount; lo++)
    {
        allTargets.Add($"192.168.{hi}.{lo}");
        generated++;
    }
}

Console.WriteLine("=== FpingSharp Batch Ping Benchmark ===");
Console.WriteLine($"Target range: 192.168.0.0/16 ({hostCount:N0} hosts)");
Console.WriteLine($"Parallel ping concurrency: {parallelism}");
Console.WriteLine($"Ping timeout: {PingTimeoutMs}ms");
Console.WriteLine();

if (dryRun)
{
    Console.WriteLine("[dry-run] Configuration verified. No pings will be sent.");
    Console.WriteLine($"[dry-run] First target: {allTargets[0]}");
    Console.WriteLine($"[dry-run] Last target:  {allTargets[^1]}");
    Console.WriteLine($"[dry-run] Total targets: {allTargets.Count:N0}");
    return 0;
}

// Results storage
double? fpingSeconds = null;
int fpingAlive = 0, fpingUnreachable = 0;

double? fpingSharpSeconds = null;
int fpingSharpAlive = 0, fpingSharpUnreachable = 0;

double? parallelPingSeconds = null;
int parallelPingAlive = 0, parallelPingUnreachable = 0;

// ---------------------------------------------------------------
// Benchmark 1: fping (native)
// ---------------------------------------------------------------
Console.Error.WriteLine("--- Running: fping (native) ---");
try
{
    // Check if fping is available
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
        Console.Error.WriteLine("  fping not found in PATH. Skipping this benchmark.");
        Console.Error.WriteLine("  Install with: sudo apt install fping");
    }
    else
    {
        // Write targets to temp file
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllLinesAsync(tempFile, allTargets);

            Console.Error.WriteLine($"  Starting fping with {hostCount:N0} targets...");
            var sw = Stopwatch.StartNew();

            using var fpingProc = new Process();
            fpingProc.StartInfo = new ProcessStartInfo
            {
                FileName = "fping",
                Arguments = $"-C 1 -q -i 1 -t {PingTimeoutMs} -r 0 -f {tempFile}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            fpingProc.Start();

            // fping outputs results to stderr in format:
            // 192.168.0.1 : 1.23
            // 192.168.0.2 : -
            var stderrTask = fpingProc.StandardError.ReadToEndAsync();
            var stdoutTask = fpingProc.StandardOutput.ReadToEndAsync();

            await fpingProc.WaitForExitAsync();
            sw.Stop();

            var stderr = await stderrTask;
            _ = await stdoutTask; // consume stdout to avoid deadlocks

            fpingSeconds = sw.Elapsed.TotalSeconds;

            // Parse fping stderr output
            foreach (var line in stderr.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                // Format: "hostname : rtt" or "hostname : -"
                var colonIdx = line.LastIndexOf(':');
                if (colonIdx < 0) continue;

                var rttPart = line[(colonIdx + 1)..].Trim();
                if (rttPart == "-")
                    fpingUnreachable++;
                else
                    fpingAlive++;
            }

            Console.Error.WriteLine("  fping complete.");
        }
        finally
        {
            try { File.Delete(tempFile); } catch { /* best effort */ }
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"  fping benchmark failed: {ex.Message}");
}

// ---------------------------------------------------------------
// Benchmark 2: FpingSharp
// ---------------------------------------------------------------
Console.Error.WriteLine("--- Running: FpingSharp ---");
try
{
    var options = new FpingOptions
    {
        Count = 1,
        TimeoutMs = PingTimeoutMs,
        Retry = 0,
        IntervalMs = 1,
    };

    using var client = new FpingClient(options);

    Console.Error.WriteLine($"  Starting FpingSharp with {hostCount:N0} targets...");
    var sw = Stopwatch.StartNew();

    var result = client.Run(allTargets);

    sw.Stop();
    fpingSharpSeconds = sw.Elapsed.TotalSeconds;
    fpingSharpAlive = result.AliveCount;
    fpingSharpUnreachable = result.UnreachableCount;

    Console.Error.WriteLine("  FpingSharp complete.");
}
catch (SocketPermissionException ex)
{
    Console.Error.WriteLine($"  FpingSharp benchmark skipped: {ex.Message}");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"  FpingSharp benchmark failed: {ex.Message}");
}

// ---------------------------------------------------------------
// Benchmark 3: C# Parallel Ping
// ---------------------------------------------------------------
Console.Error.WriteLine($"--- Running: C# Parallel Ping ({parallelism} concurrent) ---");
try
{
    Console.Error.WriteLine($"  Starting parallel ping with {hostCount:N0} targets...");
    int alive = 0;
    int unreachable = 0;

    var sw = Stopwatch.StartNew();

    await Parallel.ForEachAsync(
        allTargets,
        new ParallelOptions { MaxDegreeOfParallelism = parallelism },
        async (target, ct) =>
        {
            using var pingSender = new Ping();
            try
            {
                var reply = await pingSender.SendPingAsync(target, PingTimeoutMs);
                if (reply.Status == IPStatus.Success)
                    Interlocked.Increment(ref alive);
                else
                    Interlocked.Increment(ref unreachable);
            }
            catch
            {
                Interlocked.Increment(ref unreachable);
            }
        });

    sw.Stop();
    parallelPingSeconds = sw.Elapsed.TotalSeconds;
    parallelPingAlive = alive;
    parallelPingUnreachable = unreachable;

    Console.Error.WriteLine("  Parallel ping complete.");
}
catch (Exception ex)
{
    Console.Error.WriteLine($"  Parallel ping benchmark failed: {ex.Message}");
}

// ---------------------------------------------------------------
// Results
// ---------------------------------------------------------------
Console.WriteLine();

PrintResult("fping (native)", fpingSeconds, fpingAlive, fpingUnreachable, hostCount);
PrintResult("FpingSharp", fpingSharpSeconds, fpingSharpAlive, fpingSharpUnreachable, hostCount);
PrintResult($"C# Parallel Ping ({parallelism} concurrent)", parallelPingSeconds, parallelPingAlive, parallelPingUnreachable, hostCount);

// Comparison
Console.WriteLine("=== Comparison ===");

if (fpingSharpSeconds.HasValue && fpingSeconds.HasValue)
{
    double ratio = fpingSharpSeconds.Value / fpingSeconds.Value;
    string label = ratio >= 1.0 ? "slower" : "faster";
    double displayRatio = ratio >= 1.0 ? ratio : 1.0 / ratio;
    Console.WriteLine($"  FpingSharp vs fping:          {displayRatio:F2}x {label}");
}
else
{
    Console.WriteLine("  FpingSharp vs fping:          N/A (one or both skipped)");
}

if (fpingSharpSeconds.HasValue && parallelPingSeconds.HasValue)
{
    double ratio = parallelPingSeconds.Value / fpingSharpSeconds.Value;
    string label = ratio >= 1.0 ? "faster" : "slower";
    double displayRatio = ratio >= 1.0 ? ratio : 1.0 / ratio;
    Console.WriteLine($"  FpingSharp vs C# Parallel:   {displayRatio:F2}x {label}");
}
else
{
    Console.WriteLine("  FpingSharp vs C# Parallel:   N/A (one or both skipped)");
}

Console.WriteLine();
return 0;

// ---------------------------------------------------------------
// Helper
// ---------------------------------------------------------------
static void PrintResult(string name, double? seconds, int alive, int unreachable, int totalHosts)
{
    Console.WriteLine($"--- {name} ---");
    if (seconds.HasValue)
    {
        double rate = totalHosts / seconds.Value;
        Console.WriteLine($"  Elapsed: {seconds.Value:F2}s");
        Console.WriteLine($"  Alive: {alive:N0} / Unreachable: {unreachable:N0}");
        Console.WriteLine($"  Rate: {rate:N0} hosts/sec");
    }
    else
    {
        Console.WriteLine("  Skipped");
    }
    Console.WriteLine();
}
