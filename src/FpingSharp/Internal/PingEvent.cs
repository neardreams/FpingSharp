namespace FpingSharp.Internal
{
    internal sealed class PingEvent
    {
        /// <summary>Nanosecond timestamp when this event fires.</summary>
        public long EvTime;

        /// <summary>Reference to the host this event belongs to.</summary>
        public HostEntry? Host;

        /// <summary>Index into the host's event storage array.</summary>
        public int EventIndex;

        /// <summary>Previous node in the doubly-linked list.</summary>
        public PingEvent? Prev;

        /// <summary>Next node in the doubly-linked list.</summary>
        public PingEvent? Next;

        /// <summary>True when this event is currently in a queue.</summary>
        public bool IsLinked;

        /// <summary>Removes this node from the doubly-linked list by updating Prev/Next pointers.</summary>
        public void Unlink()
        {
            if (Prev != null)
                Prev.Next = Next;

            if (Next != null)
                Next.Prev = Prev;

            Prev = null;
            Next = null;
            IsLinked = false;
        }
    }
}
