using System;
using System.Net;

namespace FpingSharp
{
    /// <summary>
    /// Data for a single ping reply or timeout event.
    /// </summary>
    public sealed class PingReply : EventArgs
    {
        public string HostName { get; }
        public IPAddress? Address { get; }
        public bool Success { get; }
        public TimeSpan? RoundTripTime { get; }
        public int SequenceNumber { get; }
        public int PingCount { get; }
        public byte IcmpType { get; }
        public byte IcmpCode { get; }

        public PingReply(string hostName, IPAddress? address, bool success, TimeSpan? rtt,
                         int sequenceNumber, int pingCount, byte icmpType = 0, byte icmpCode = 0)
        {
            HostName = hostName;
            Address = address;
            Success = success;
            RoundTripTime = rtt;
            SequenceNumber = sequenceNumber;
            PingCount = pingCount;
            IcmpType = icmpType;
            IcmpCode = icmpCode;
        }
    }
}
