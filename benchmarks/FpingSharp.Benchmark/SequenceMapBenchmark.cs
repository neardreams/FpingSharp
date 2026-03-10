using BenchmarkDotNet.Attributes;
using FpingSharp.Internal;

namespace FpingSharp.Benchmark;

[MemoryDiagnoser]
public class SequenceMapBenchmark
{
    private SequenceMap _map = null!;
    private ushort[] _seqs = null!;

    [Params(1000, 10000)]
    public int Count;

    [IterationSetup]
    public void Setup()
    {
        _map = new SequenceMap();
        _seqs = new ushort[Count];
        for (int i = 0; i < Count; i++)
            _seqs[i] = _map.Add(i, 1, (long)i * 1_000_000);
    }

    [Benchmark(Description = "Add")]
    public void AddEntries()
    {
        var map = new SequenceMap();
        for (int i = 0; i < Count; i++)
            map.Add(i, 1, (long)i * 1_000_000);
    }

    [Benchmark(Description = "Fetch (hit)")]
    public void FetchAll()
    {
        // Re-populate since Fetch invalidates entries
        var map = new SequenceMap();
        var seqs = new ushort[Count];
        for (int i = 0; i < Count; i++)
            seqs[i] = map.Add(i, 1, (long)i * 1_000_000);

        long nowNs = (long)Count * 1_000_000;
        for (int i = 0; i < Count; i++)
            map.Fetch(seqs[i], nowNs);
    }
}
