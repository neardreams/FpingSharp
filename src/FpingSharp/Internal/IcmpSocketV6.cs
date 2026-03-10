using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using FpingSharp.Exceptions;

namespace FpingSharp.Internal
{
    internal sealed class IcmpSocketV6 : IcmpSocket
    {
        public IcmpSocketV6()
        {
            CreateSocket();
        }

        protected override void CreateSocket()
        {
            // Try RAW socket first
            try
            {
                _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Raw, ProtocolType.IcmpV6);
                _isRaw = true;
                return;
            }
            catch (SocketException)
            {
                // RAW failed, try DGRAM
            }

            // Fallback to DGRAM
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new SocketPermissionException();
            }

            try
            {
                _socket = new Socket(AddressFamily.InterNetworkV6, SocketType.Dgram, ProtocolType.IcmpV6);
                _isRaw = false;
            }
            catch (SocketException ex)
            {
                throw new SocketPermissionException(ex);
            }
        }
    }
}
