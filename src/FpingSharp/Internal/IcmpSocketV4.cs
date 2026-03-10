using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FpingSharp.Exceptions;

namespace FpingSharp.Internal
{
    internal sealed class IcmpSocketV4 : IcmpSocket
    {
        public IcmpSocketV4()
        {
            CreateSocket();
        }

        protected override void CreateSocket()
        {
            // Try RAW socket first (needs root/admin)
            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Raw, ProtocolType.Icmp);
                _isRaw = true;
                // Enable IP header inclusion is NOT needed for ICMP RAW sockets on modern OS
                // The kernel handles IP header for us
                return;
            }
            catch (SocketException ex)
            {
                Console.Error.WriteLine($"  [IcmpSocketV4] RAW socket failed: {ex.SocketErrorCode} - {ex.Message}");
                // RAW failed, try DGRAM
            }

            // Fallback to DGRAM socket (Linux with ping_group_range)
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // Windows doesn't support DGRAM ICMP
                throw new SocketPermissionException();
            }

            try
            {
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Icmp);
                _isRaw = false;
            }
            catch (SocketException ex)
            {
                throw new SocketPermissionException(ex);
            }
        }
    }
}
