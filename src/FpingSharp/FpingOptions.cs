using System;
using System.Net;

namespace FpingSharp
{
    public sealed class FpingOptions
    {
        /// <summary>Number of pings per host (-c flag).</summary>
        public int Count { get; set; } = 1;

        /// <summary>Continuous ping mode (-l flag).</summary>
        public bool Loop { get; set; } = false;

        /// <summary>Minimum interval between sending any ping in milliseconds (-i flag).</summary>
        public int IntervalMs { get; set; } = 10;

        /// <summary>Interval between pings to the same host in milliseconds (-p flag).</summary>
        public int PerHostIntervalMs { get; set; } = 1000;

        /// <summary>Timeout for an individual ping in milliseconds (-t flag).</summary>
        public int TimeoutMs { get; set; } = 500;

        /// <summary>Number of retries (-r flag).</summary>
        public int Retry { get; set; } = 3;

        /// <summary>Backoff factor for retries (-B flag).</summary>
        public double Backoff { get; set; } = 1.5;

        /// <summary>ICMP payload size in bytes (-b flag).</summary>
        public int PacketSize { get; set; } = 56;

        /// <summary>IP Time-To-Live. Null means use OS default.</summary>
        public int? Ttl { get; set; } = null;

        /// <summary>IP Type-Of-Service. Null means use OS default.</summary>
        public int? Tos { get; set; } = null;

        /// <summary>Set the Don't Fragment bit.</summary>
        public bool DontFragment { get; set; } = false;

        /// <summary>Address family to use for pinging.</summary>
        public FpingAddressFamily AddressFamily { get; set; } = FpingAddressFamily.IPv4;

        /// <summary>Source address to bind to (-S flag). Null means use OS default.</summary>
        public string? SourceAddress { get; set; }

        /// <summary>Network interface to bind to (-I flag). Linux only. Null means use OS default.</summary>
        public string? InterfaceName { get; set; }

        /// <summary>Interval in milliseconds for periodic statistics callbacks (-Q flag). Null or 0 disables.</summary>
        public int? StatsIntervalMs { get; set; }

        /// <summary>
        /// Validates all option values and throws <see cref="ArgumentOutOfRangeException"/>
        /// if any value is invalid.
        /// </summary>
        public void Validate()
        {
            if (!Loop && Count < 1)
                throw new ArgumentOutOfRangeException(nameof(Count), Count, "Count must be >= 1 when Loop is false.");

            if (IntervalMs < 1)
                throw new ArgumentOutOfRangeException(nameof(IntervalMs), IntervalMs, "IntervalMs must be >= 1.");

            if (PerHostIntervalMs < 10)
                throw new ArgumentOutOfRangeException(nameof(PerHostIntervalMs), PerHostIntervalMs, "PerHostIntervalMs must be >= 10.");

            if (TimeoutMs < 50)
                throw new ArgumentOutOfRangeException(nameof(TimeoutMs), TimeoutMs, "TimeoutMs must be >= 50.");

            if (Retry < 0)
                throw new ArgumentOutOfRangeException(nameof(Retry), Retry, "Retry must be >= 0.");

            if (Backoff < 1.0)
                throw new ArgumentOutOfRangeException(nameof(Backoff), Backoff, "Backoff must be >= 1.0.");

            if (PacketSize < 0 || PacketSize > 65500)
                throw new ArgumentOutOfRangeException(nameof(PacketSize), PacketSize, "PacketSize must be between 0 and 65500.");

            if (Ttl.HasValue && (Ttl.Value < 1 || Ttl.Value > 255))
                throw new ArgumentOutOfRangeException(nameof(Ttl), Ttl.Value, "Ttl must be between 1 and 255.");

            if (Tos.HasValue && (Tos.Value < 0 || Tos.Value > 255))
                throw new ArgumentOutOfRangeException(nameof(Tos), Tos.Value, "Tos must be between 0 and 255.");

            if (SourceAddress != null && !IPAddress.TryParse(SourceAddress, out _))
                throw new ArgumentException("SourceAddress is not a valid IP address.", nameof(SourceAddress));

            if (InterfaceName != null && string.IsNullOrWhiteSpace(InterfaceName))
                throw new ArgumentException("InterfaceName must not be empty or whitespace.", nameof(InterfaceName));

            if (StatsIntervalMs.HasValue && StatsIntervalMs.Value < 0)
                throw new ArgumentOutOfRangeException(nameof(StatsIntervalMs), StatsIntervalMs.Value, "StatsIntervalMs must be >= 0.");
        }
    }
}
