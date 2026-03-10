namespace FpingSharp.Internal
{
    internal sealed class SequenceMap
    {
        private const int Capacity = 65535;
        private const long ExpiryDurationNs = 10_000_000_000L; // 10 seconds

        private struct Entry
        {
            public int HostIndex;
            public int PingCount;
            public long TimestampNs;
            public long ExpiresNs;
            public bool Valid;
        }

        private readonly Entry[] _entries = new Entry[Capacity];
        private ushort _nextSeq = 0;

        /// <summary>
        /// Stores an entry at the next sequence number slot and returns the sequence number used.
        /// </summary>
        public ushort Add(int hostIndex, int pingCount, long timestampNs)
        {
            ushort seq = _nextSeq;
            _entries[seq] = new Entry
            {
                HostIndex = hostIndex,
                PingCount = pingCount,
                TimestampNs = timestampNs,
                ExpiresNs = timestampNs + ExpiryDurationNs,
                Valid = true
            };

            // Increment and wrap within [0, Capacity)
            _nextSeq = (ushort)((_nextSeq + 1) % Capacity);

            return seq;
        }

        /// <summary>
        /// Returns entry data if valid and not expired, then marks the slot invalid.
        /// Returns null otherwise.
        /// </summary>
        public (int HostIndex, int PingCount, long TimestampNs)? Fetch(ushort seq, long nowNs)
        {
            if (seq >= Capacity)
                return null;

            ref Entry entry = ref _entries[seq];
            if (!entry.Valid)
                return null;

            if (nowNs > entry.ExpiresNs)
            {
                entry.Valid = false;
                return null;
            }

            var result = (entry.HostIndex, entry.PingCount, entry.TimestampNs);
            entry.Valid = false;
            return result;
        }

        /// <summary>Resets all entries to invalid.</summary>
        public void Clear()
        {
            for (int i = 0; i < Capacity; i++)
            {
                _entries[i] = default;
            }
            _nextSeq = 0;
        }
    }
}
