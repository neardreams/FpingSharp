using System;
using System.Net;
using System.Net.Sockets;
using FpingSharp.Exceptions;

namespace FpingSharp.Internal
{
    internal abstract class IcmpSocket : IDisposable
    {
        protected Socket? _socket;
        protected bool _isRaw; // true = RAW socket, false = DGRAM
        private bool _disposed;

        public bool IsRaw => _isRaw;

        public ushort Id { get; }

        protected IcmpSocket()
        {
            // Generate a unique ID based on process ID + timestamp, truncated to ushort
            Id = (ushort)(System.Diagnostics.Process.GetCurrentProcess().Id & 0xFFFF);
        }

        /// Send ICMP packet to the given endpoint
        public virtual int SendTo(byte[] buffer, int length, EndPoint remoteEP)
        {
            if (_socket == null) throw new ObjectDisposedException(nameof(IcmpSocket));
            return _socket.SendTo(buffer, 0, length, SocketFlags.None, remoteEP);
        }

        /// Receive ICMP packet. Returns bytes received, fills buffer and sets remoteEP.
        public virtual int ReceiveFrom(byte[] buffer, ref EndPoint remoteEP)
        {
            if (_socket == null) throw new ObjectDisposedException(nameof(IcmpSocket));
            return _socket.ReceiveFrom(buffer, ref remoteEP);
        }

        /// Check if data is available to read within the given timeout (microseconds).
        /// Returns true if data is available.
        public virtual bool Poll(int microSeconds)
        {
            if (_socket == null) throw new ObjectDisposedException(nameof(IcmpSocket));
            return _socket.Poll(microSeconds, SelectMode.SelectRead);
        }

        /// Set socket options like TTL, TOS, DontFragment
        public void Configure(FpingOptions options)
        {
            if (_socket == null) return;

            if (options.Ttl.HasValue)
            {
                _socket.Ttl = (short)options.Ttl.Value;
            }

            // TOS and DontFragment are IPv4 only
            if (_socket.AddressFamily == AddressFamily.InterNetwork)
            {
                if (options.Tos.HasValue)
                {
                    _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.TypeOfService, options.Tos.Value);
                }
                if (options.DontFragment)
                {
                    _socket.DontFragment = true;
                }
            }
        }

        /// Set receive buffer size
        public void SetReceiveBufferSize(int size)
        {
            _socket?.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveBuffer, size);
        }

        /// Set socket to non-blocking mode
        public void SetNonBlocking()
        {
            if (_socket != null)
                _socket.Blocking = false;
        }

        /// Set send timeout in milliseconds
        public void SetSendTimeout(int ms)
        {
            if (_socket != null)
                _socket.SendTimeout = ms;
        }

        /// <summary>Bind the socket to a specific source address (-S flag).</summary>
        public void BindToAddress(IPAddress addr)
        {
            if (_socket == null) throw new ObjectDisposedException(nameof(IcmpSocket));
            _socket.Bind(new IPEndPoint(addr, 0));
        }

        /// <summary>Bind the socket to a specific network interface (-I flag). Linux only.</summary>
        public void BindToInterface(string iface)
        {
            if (_socket == null) throw new ObjectDisposedException(nameof(IcmpSocket));
            if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(
                    System.Runtime.InteropServices.OSPlatform.Linux))
            {
                throw new PlatformNotSupportedException("SO_BINDTODEVICE is only supported on Linux.");
            }
            _socket.SetSocketOption(SocketOptionLevel.Socket, (SocketOptionName)25 /* SO_BINDTODEVICE */,
                System.Text.Encoding.ASCII.GetBytes(iface + '\0'));
        }

        protected abstract void CreateSocket();

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                _socket?.Dispose();
                _socket = null;
            }
        }
    }
}
