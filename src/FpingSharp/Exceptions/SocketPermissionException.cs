using System;
using System.Runtime.InteropServices;

namespace FpingSharp.Exceptions
{
    public class SocketPermissionException : FpingException
    {
        public SocketPermissionException()
            : base(GetPlatformMessage()) { }

        public SocketPermissionException(Exception innerException)
            : base(GetPlatformMessage(), innerException) { }

        private static string GetPlatformMessage()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return "Insufficient permissions to create ICMP socket. " +
                       "Run as root, use 'sudo setcap cap_net_raw+ep <binary>', " +
                       "or ensure your user group is in /proc/sys/net/ipv4/ping_group_range.";
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return "Insufficient permissions to create ICMP socket. " +
                       "Run the application as Administrator.";
            }
            return "Insufficient permissions to create ICMP socket.";
        }
    }
}
