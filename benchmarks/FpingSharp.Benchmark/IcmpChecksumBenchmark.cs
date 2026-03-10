using BenchmarkDotNet.Attributes;
using FpingSharp.Internal;

namespace FpingSharp.Benchmark;

[MemoryDiagnoser]
public class IcmpChecksumBenchmark
{
    private byte[] _small = null!;
    private byte[] _medium = null!;
    private byte[] _large = null!;

    [GlobalSetup]
    public void Setup()
    {
        var rng = new Random(42);
        _small = new byte[64];    // typical ICMP packet
        _medium = new byte[512];
        _large = new byte[65535]; // max IP packet
        rng.NextBytes(_small);
        rng.NextBytes(_medium);
        rng.NextBytes(_large);
    }

    [Benchmark(Description = "64 bytes")]
    public ushort Checksum_64B() => IcmpChecksum.Compute(_small);

    [Benchmark(Description = "512 bytes")]
    public ushort Checksum_512B() => IcmpChecksum.Compute(_medium);

    [Benchmark(Description = "65535 bytes")]
    public ushort Checksum_64KB() => IcmpChecksum.Compute(_large);
}
