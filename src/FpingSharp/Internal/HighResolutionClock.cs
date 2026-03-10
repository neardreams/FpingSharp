using System.Diagnostics;

namespace FpingSharp.Internal
{
    internal static class HighResolutionClock
    {
        private static readonly double s_tickFrequency = (double)1_000_000_000L / Stopwatch.Frequency;

        /// <summary>Returns the current time in nanoseconds using <see cref="Stopwatch"/>.</summary>
        internal static long NowNs()
        {
            return (long)(Stopwatch.GetTimestamp() * s_tickFrequency);
        }

        internal static long MsToNs(int ms) => (long)ms * 1_000_000L;

        internal static long MsToNs(double ms) => (long)(ms * 1_000_000.0);

        internal static double NsToMs(long ns) => ns / 1_000_000.0;
    }
}
