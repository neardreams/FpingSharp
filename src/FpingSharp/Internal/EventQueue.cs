namespace FpingSharp.Internal
{
    internal sealed class EventQueue
    {
        private readonly PingEvent _head;
        private readonly PingEvent _tail;
        private int _count;

        public EventQueue()
        {
            _head = new PingEvent { EvTime = long.MinValue };
            _tail = new PingEvent { EvTime = long.MaxValue };
            _head.Next = _tail;
            _tail.Prev = _head;
        }

        /// <summary>
        /// Inserts an event in sorted order by EvTime, scanning from the tail.
        /// O(1) amortized since events are usually appended near the end.
        /// </summary>
        public void Enqueue(PingEvent ev)
        {
            // Scan backwards from the tail sentinel to find insertion point
            var current = _tail.Prev!;
            while (current != _head && current.EvTime > ev.EvTime)
            {
                current = current.Prev!;
            }

            // Insert ev after current
            ev.Prev = current;
            ev.Next = current.Next;
            current.Next!.Prev = ev;
            current.Next = ev;
            ev.IsLinked = true;
            _count++;
        }

        /// <summary>Returns the first real event (after head sentinel) or null if empty.</summary>
        public PingEvent? PeekHead()
        {
            var first = _head.Next!;
            return first == _tail ? null : first;
        }

        /// <summary>Removes and returns the head event, or null if empty.</summary>
        public PingEvent? Dequeue()
        {
            var first = _head.Next!;
            if (first == _tail)
                return null;

            first.Unlink();
            // Unlink sets IsLinked = false, and reconnects surrounding nodes.
            // But we need to ensure head/tail stay connected if queue is now empty.
            // Unlink already handled Prev.Next = Next and Next.Prev = Prev, so this is fine.
            _count--;
            return first;
        }

        /// <summary>Removes a specific event from the queue.</summary>
        public void Remove(PingEvent ev)
        {
            if (!ev.IsLinked)
                return;

            ev.Unlink();
            _count--;
        }

        /// <summary>Returns true if there are no real events between the sentinels.</summary>
        public bool IsEmpty => _head.Next == _tail;

        /// <summary>Number of events in the queue.</summary>
        public int Count => _count;
    }
}
