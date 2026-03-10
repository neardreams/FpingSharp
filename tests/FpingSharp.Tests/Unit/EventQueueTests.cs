using FpingSharp.Internal;
using Xunit;

namespace FpingSharp.Tests.Unit;

public class EventQueueTests
{
    private static PingEvent MakeEvent(long evTime)
    {
        return new PingEvent { EvTime = evTime };
    }

    [Fact]
    public void Test_Enqueue_SingleEvent_CanDequeue()
    {
        var queue = new EventQueue();
        var ev = MakeEvent(100);

        queue.Enqueue(ev);

        Assert.Same(ev, queue.PeekHead());
        var dequeued = queue.Dequeue();
        Assert.Same(ev, dequeued);
    }

    [Fact]
    public void Test_Enqueue_MultipleEvents_SortedByTime()
    {
        var queue = new EventQueue();
        var ev300 = MakeEvent(300);
        var ev100 = MakeEvent(100);
        var ev200 = MakeEvent(200);

        queue.Enqueue(ev300);
        queue.Enqueue(ev100);
        queue.Enqueue(ev200);

        var first = queue.Dequeue();
        var second = queue.Dequeue();
        var third = queue.Dequeue();

        Assert.Equal(100, first!.EvTime);
        Assert.Equal(200, second!.EvTime);
        Assert.Equal(300, third!.EvTime);
    }

    [Fact]
    public void Test_Dequeue_EmptyQueue_ReturnsNull()
    {
        var queue = new EventQueue();

        var result = queue.Dequeue();

        Assert.Null(result);
    }

    [Fact]
    public void Test_Remove_MiddleEvent()
    {
        var queue = new EventQueue();
        var ev1 = MakeEvent(100);
        var ev2 = MakeEvent(200);
        var ev3 = MakeEvent(300);

        queue.Enqueue(ev1);
        queue.Enqueue(ev2);
        queue.Enqueue(ev3);

        queue.Remove(ev2);

        var first = queue.Dequeue();
        var second = queue.Dequeue();
        var third = queue.Dequeue();

        Assert.Equal(100, first!.EvTime);
        Assert.Equal(300, second!.EvTime);
        Assert.Null(third);
    }

    [Fact]
    public void Test_IsEmpty_AfterClear()
    {
        var queue = new EventQueue();
        queue.Enqueue(MakeEvent(100));
        queue.Enqueue(MakeEvent(200));

        queue.Dequeue();
        queue.Dequeue();

        Assert.True(queue.IsEmpty);
    }

    [Fact]
    public void Test_Count_TracksCorrectly()
    {
        var queue = new EventQueue();
        Assert.Equal(0, queue.Count);

        queue.Enqueue(MakeEvent(100));
        Assert.Equal(1, queue.Count);

        queue.Enqueue(MakeEvent(200));
        Assert.Equal(2, queue.Count);

        queue.Dequeue();
        Assert.Equal(1, queue.Count);

        queue.Dequeue();
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void Test_Enqueue_EventAtEnd_IsEfficient()
    {
        var queue = new EventQueue();
        var ev1 = MakeEvent(100);
        var ev2 = MakeEvent(200);
        var ev3 = MakeEvent(300);

        // Enqueue in ascending order (common case, should be O(1) per insertion)
        queue.Enqueue(ev1);
        queue.Enqueue(ev2);
        queue.Enqueue(ev3);

        Assert.Equal(100, queue.Dequeue()!.EvTime);
        Assert.Equal(200, queue.Dequeue()!.EvTime);
        Assert.Equal(300, queue.Dequeue()!.EvTime);
    }

    [Fact]
    public void Test_Remove_SetIsLinkedFalse()
    {
        var queue = new EventQueue();
        var ev = MakeEvent(100);

        queue.Enqueue(ev);
        Assert.True(ev.IsLinked);

        queue.Remove(ev);
        Assert.False(ev.IsLinked);
    }
}
