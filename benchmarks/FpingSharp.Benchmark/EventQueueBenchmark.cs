using BenchmarkDotNet.Attributes;
using FpingSharp.Internal;

namespace FpingSharp.Benchmark;

[MemoryDiagnoser]
public class EventQueueBenchmark
{
    [Params(100, 1000, 10000)]
    public int Count;

    private PingEvent[] _events = null!;

    [IterationSetup]
    public void Setup()
    {
        _events = new PingEvent[Count];
        for (int i = 0; i < Count; i++)
            _events[i] = new PingEvent { EvTime = i * 1000L };
    }

    [Benchmark(Description = "Enqueue (sorted insert)")]
    public void EnqueueAll()
    {
        var queue = new EventQueue();
        for (int i = 0; i < Count; i++)
            queue.Enqueue(_events[i]);
    }

    [Benchmark(Description = "Enqueue + Dequeue all")]
    public void EnqueueDequeueAll()
    {
        var queue = new EventQueue();
        for (int i = 0; i < Count; i++)
            queue.Enqueue(_events[i]);
        while (!queue.IsEmpty)
            queue.Dequeue();
    }
}
