using System;
using System.Collections.Generic;
using System.Net;

namespace FpingSharp
{
    /// <summary>
    /// Generates IP address ranges from CIDR notation or start/end addresses (-g flag).
    /// IPv4 only, consistent with fping -g behavior.
    /// </summary>
    public static class CidrRange
    {
        /// <summary>
        /// Expand a CIDR notation string (e.g. "192.168.1.0/24") into individual IP addresses.
        /// For /32, returns the single address. For /31, returns both addresses (RFC 3021).
        /// For all other prefixes, excludes network and broadcast addresses.
        /// </summary>
        public static IEnumerable<string> Expand(string cidr)
        {
            if (cidr == null)
                throw new ArgumentNullException(nameof(cidr));

            int slashIndex = cidr.IndexOf('/');
            if (slashIndex < 0)
                throw new ArgumentException("CIDR notation must contain a '/' prefix length.", nameof(cidr));

            string ipPart = cidr.Substring(0, slashIndex);
            string prefixPart = cidr.Substring(slashIndex + 1);

            if (!IPAddress.TryParse(ipPart, out var ip))
                throw new ArgumentException($"Invalid IP address: '{ipPart}'.", nameof(cidr));

            if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Only IPv4 CIDR is supported.", nameof(cidr));

            if (!int.TryParse(prefixPart, out int prefix) || prefix < 0 || prefix > 32)
                throw new ArgumentException($"Invalid prefix length: '{prefixPart}'. Must be 0-32.", nameof(cidr));

            return ExpandCidrCore(ip, prefix);
        }

        private static IEnumerable<string> ExpandCidrCore(IPAddress ip, int prefix)
        {
            uint ipUint = IpToUint(ip);

            if (prefix == 32)
            {
                yield return UintToIp(ipUint);
                yield break;
            }

            uint mask = prefix == 0 ? 0 : 0xFFFFFFFF << (32 - prefix);
            uint network = ipUint & mask;
            uint broadcast = network | ~mask;

            if (prefix == 31)
            {
                // RFC 3021: point-to-point links, both addresses are usable
                yield return UintToIp(network);
                yield return UintToIp(broadcast);
                yield break;
            }

            // Exclude network address and broadcast address
            for (uint addr = network + 1; addr < broadcast; addr++)
            {
                yield return UintToIp(addr);
            }
        }

        /// <summary>
        /// Expand a range of IP addresses from startIp to endIp (inclusive).
        /// </summary>
        public static IEnumerable<string> Expand(string startIp, string endIp)
        {
            if (startIp == null) throw new ArgumentNullException(nameof(startIp));
            if (endIp == null) throw new ArgumentNullException(nameof(endIp));

            if (!IPAddress.TryParse(startIp, out var start))
                throw new ArgumentException($"Invalid start IP address: '{startIp}'.", nameof(startIp));
            if (!IPAddress.TryParse(endIp, out var end))
                throw new ArgumentException($"Invalid end IP address: '{endIp}'.", nameof(endIp));

            if (start.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Only IPv4 addresses are supported.", nameof(startIp));
            if (end.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                throw new ArgumentException("Only IPv4 addresses are supported.", nameof(endIp));

            uint startUint = IpToUint(start);
            uint endUint = IpToUint(end);

            if (startUint > endUint)
                throw new ArgumentException("Start IP must be less than or equal to end IP.");

            return ExpandRangeCore(startUint, endUint);
        }

        private static IEnumerable<string> ExpandRangeCore(uint start, uint end)
        {
            for (uint addr = start; addr <= end; addr++)
            {
                yield return UintToIp(addr);
            }
        }

        private static uint IpToUint(IPAddress ip)
        {
            byte[] bytes = ip.GetAddressBytes();
            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) |
                   ((uint)bytes[2] << 8) | bytes[3];
        }

        private static string UintToIp(uint ip)
        {
            return $"{(ip >> 24) & 0xFF}.{(ip >> 16) & 0xFF}.{(ip >> 8) & 0xFF}.{ip & 0xFF}";
        }
    }
}
