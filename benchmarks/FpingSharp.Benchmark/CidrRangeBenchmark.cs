using BenchmarkDotNet.Attributes;

namespace FpingSharp.Benchmark;

[MemoryDiagnoser]
public class CidrRangeBenchmark
{
    [Benchmark(Description = "/24 (254 hosts)")]
    public int Expand24()
    {
        int count = 0;
        foreach (var _ in CidrRange.Expand("10.0.0.0/24"))
            count++;
        return count;
    }

    [Benchmark(Description = "/16 (65534 hosts)")]
    public int Expand16()
    {
        int count = 0;
        foreach (var _ in CidrRange.Expand("10.0.0.0/16"))
            count++;
        return count;
    }

    [Benchmark(Description = "Range 10.0.0.1 - 10.0.3.254")]
    public int ExpandRange()
    {
        int count = 0;
        foreach (var _ in CidrRange.Expand("10.0.0.1", "10.0.3.254"))
            count++;
        return count;
    }
}
