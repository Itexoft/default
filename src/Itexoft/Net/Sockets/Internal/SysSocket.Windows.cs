// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;

namespace Itexoft.Net.Sockets.Internal;

internal static partial class SysSocketWindows
{
    private const int invalidSocket = -1;
    private const int socketError = -1;
    private const int fionbio = unchecked((int)0x8004667E);
    private const int fionread = unchecked((int)0x4004667F);
    private const int wsaFlagNoHandleInherit = 0x80;

    private static int winsockInitialized;

    public static bool IsWindows { get; } = Environment.OSVersion.Platform == PlatformID.Win32NT
                                            || Environment.OSVersion.Platform == PlatformID.Win32Windows
                                            || Environment.OSVersion.Platform == PlatformID.Win32S
                                            || Environment.OSVersion.Platform == PlatformID.WinCE;

    public static SysSocketHandle CreateSocketHandle(NetAddressFamily addressFamily, NetSocketType socketType, NetSocketProtocol socketProtocol)
    {
        EnsureInitialized();

        var handle = WsaSocketW(GetNativeAddressFamily(addressFamily), (int)socketType, (int)socketProtocol, nint.Zero, 0, wsaFlagNoHandleInherit);

        if (handle == new nint(invalidSocket))
        {
            var error = WSAGetLastError();

            throw new NetSocketException(MapError(error), error);
        }

        return new(handle);
    }

    public static void ConfigureNewSocket(ref SysSocketHandle handle, NetAddressFamily addressFamily, bool dualMode)
    {
        ConfigureSocket(handle);

        if (addressFamily == NetAddressFamily.InterNetworkV6)
            ThrowIfOptionFailure(ref handle, NetSocketOptionLevel.Ipv6, NetSocketOptionName.Ipv6Only, dualMode ? 0 : 1);
    }

    public static void ConfigureAcceptedSocket(ref SysSocketHandle handle, NetAddressFamily addressFamily, bool dualMode) =>
        ConfigureSocket(handle);

    public static NetSocketError GetAvailable(ref SysSocketHandle handle, out int available)
    {
        uint value = 0;

        if (Ioctlsocket(handle, fionread, ref value) == socketError)
        {
            available = 0;

            return MapError(WSAGetLastError());
        }

        available = (int)value;

        return NetSocketError.Success;
    }

    public static NetSocketError GetSockName(ref SysSocketHandle handle, Span<byte> socketAddress, out int socketAddressLength)
    {
        var length = socketAddress.Length;

        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                if (Getsockname(handle, buffer, ref length) == socketError)
                {
                    socketAddressLength = 0;

                    return MapError(WSAGetLastError());
                }
            }
        }

        socketAddressLength = length;

        return NetSocketError.Success;
    }

    public static NetSocketError GetPeerName(ref SysSocketHandle handle, Span<byte> socketAddress, out int socketAddressLength)
    {
        var length = socketAddress.Length;

        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                if (Getpeername(handle, buffer, ref length) == socketError)
                {
                    socketAddressLength = 0;

                    return MapError(WSAGetLastError());
                }
            }
        }

        socketAddressLength = length;

        return NetSocketError.Success;
    }

    public static NetSocketError Bind(ref SysSocketHandle handle, ReadOnlySpan<byte> socketAddress)
    {
        unsafe
        {
            fixed (byte* buffer = socketAddress)
                return Bind(handle, buffer, socketAddress.Length) == socketError ? MapError(WSAGetLastError()) : NetSocketError.Success;
        }
    }

    public static NetSocketError Listen(ref SysSocketHandle handle, int backlog) =>
        Listen(handle, backlog) == socketError ? MapError(WSAGetLastError()) : NetSocketError.Success;

    public static NetSocketError Accept(
        ref SysSocketHandle handle,
        Span<byte> socketAddress,
        out int socketAddressLength,
        out SysSocketHandle acceptedHandle)
    {
        var length = socketAddress.Length;

        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                var accepted = Accept(handle, buffer, ref length);

                if (accepted == new nint(invalidSocket))
                {
                    socketAddressLength = 0;
                    acceptedHandle = new();

                    return MapError(WSAGetLastError());
                }

                socketAddressLength = length;
                acceptedHandle = new(accepted);

                return NetSocketError.Success;
            }
        }
    }

    public static NetSocketError Connect(ref SysSocketHandle handle, ReadOnlySpan<byte> socketAddress)
    {
        unsafe
        {
            fixed (byte* buffer = socketAddress)
                return Connect(handle, buffer, socketAddress.Length) == socketError ? MapError(WSAGetLastError()) : NetSocketError.Success;
        }
    }

    public static NetSocketError GetPendingConnectError(ref SysSocketHandle handle, out NetSocketError error)
    {
        var optionValue = 0;
        var optionLength = sizeof(int);

        unsafe
        {
            if (Getsockopt(handle, 0xffff, 0x1007, &optionValue, ref optionLength) == socketError)
            {
                error = MapError(WSAGetLastError());

                return error;
            }
        }

        error = optionValue == 0 ? NetSocketError.Success : MapError(optionValue);

        return NetSocketError.Success;
    }

    public static NetSocketError Send(ref SysSocketHandle handle, ReadOnlySpan<byte> buffer, NetSocketFlags flags, out int bytesTransferred)
    {
        unsafe
        {
            fixed (byte* data = buffer)
            {
                var sent = Send(handle, data, buffer.Length, (int)flags);

                if (sent == socketError)
                {
                    bytesTransferred = 0;

                    return MapError(WSAGetLastError());
                }

                bytesTransferred = sent;

                return NetSocketError.Success;
            }
        }
    }

    public static NetSocketError Receive(ref SysSocketHandle handle, Span<byte> buffer, NetSocketFlags flags, out int bytesTransferred)
    {
        unsafe
        {
            fixed (byte* data = buffer)
            {
                var received = Recv(handle, data, buffer.Length, (int)flags);

                if (received == socketError)
                {
                    bytesTransferred = 0;

                    return MapError(WSAGetLastError());
                }

                bytesTransferred = received;

                return NetSocketError.Success;
            }
        }
    }

    public static NetSocketError Shutdown(ref SysSocketHandle handle, NetSocketShutdown how) =>
        Shutdown(handle, (int)how) == socketError ? MapError(WSAGetLastError()) : NetSocketError.Success;

    public static NetSocketError Wait(ref SysSocketHandle handle, SocketWaitMode mode, int timeoutMilliseconds, out bool ready)
    {
        var fdSet = new FdSet
        {
            Count = 1,
            Socket0 = handle,
        };

        var exceptionSet = new FdSet
        {
            Count = 1,
            Socket0 = handle,
        };

        var timeout = new TimeValue
        {
            Seconds = timeoutMilliseconds / 1000,
            Microseconds = timeoutMilliseconds % 1000 * 1000,
        };

        unsafe
        {
            var read = mode == SocketWaitMode.Read ? &fdSet : null;
            var write = mode == SocketWaitMode.Write ? &fdSet : null;
            var except = mode == SocketWaitMode.Write ? &exceptionSet : null;
            var time = timeoutMilliseconds < 0 ? null : &timeout;
            var selected = Select(0, read, write, except, time);

            if (selected == socketError)
            {
                ready = false;

                return MapError(WSAGetLastError());
            }

            ready = mode == SocketWaitMode.Read ? fdSet.Count != 0 : fdSet.Count != 0 || exceptionSet.Count != 0;

            return NetSocketError.Success;
        }
    }

    public static NetSocketError SetIntSocketOption(ref SysSocketHandle handle, NetSocketOptionLevel level, NetSocketOptionName name, int value) =>
        SetIntSocketOption(handle, level, name, value);

    public static NetSocketError GetIntSocketOption(ref SysSocketHandle handle, NetSocketOptionLevel level, NetSocketOptionName name, out int value)
    {
        var (nativeLevel, nativeName) = GetSocketOption(level, name);
        var optionLength = sizeof(int);

        unsafe
        {
            var optionValue = 0;

            var error = Getsockopt(handle, nativeLevel, nativeName, &optionValue, ref optionLength) == socketError
                ? MapError(WSAGetLastError())
                : NetSocketError.Success;

            value = optionValue;

            return error;
        }
    }

    public static NetSocketError SetLingerState(ref SysSocketHandle handle, NetLingerOption value)
    {
        var optionValue = new LingerOption
        {
            OnOff = (ushort)(value.Enabled ? 1 : 0),
            Linger = (ushort)value.LingerTime,
        };

        unsafe
        {
            return Setsockopt(handle, 0xffff, 0x0080, &optionValue, sizeof(LingerOption)) == socketError
                ? MapError(WSAGetLastError())
                : NetSocketError.Success;
        }
    }

    public static NetSocketError GetLingerState(ref SysSocketHandle handle, out NetLingerOption value)
    {
        var optionLength = Marshal.SizeOf<LingerOption>();
        var optionValue = default(LingerOption);

        unsafe
        {
            if (Getsockopt(handle, 0xffff, 0x0080, &optionValue, ref optionLength) == socketError)
            {
                value = default;

                return MapError(WSAGetLastError());
            }
        }

        value = new(optionValue.OnOff != 0, optionValue.Linger);

        return NetSocketError.Success;
    }

    private static void ConfigureSocket(nint socket)
    {
        uint enabled = 1;

        if (Ioctlsocket(socket, fionbio, ref enabled) == socketError)
        {
            var error = WSAGetLastError();

            throw new NetSocketException(MapError(error), error);
        }
    }

    private static void ThrowIfOptionFailure(ref SysSocketHandle handle, NetSocketOptionLevel level, NetSocketOptionName name, int value)
    {
        var error = SetIntSocketOption(handle, level, name, value);

        if (error != NetSocketError.Success)
            throw new NetSocketException(error);
    }

    public static int GetNativeAddressFamily(NetAddressFamily addressFamily) => addressFamily switch
    {
        NetAddressFamily.InterNetwork => 2,
        NetAddressFamily.InterNetworkV6 => 23,
        _ => 0,
    };

    public static NetAddressFamily FromNativeAddressFamily(ushort addressFamily) => addressFamily switch
    {
        2 => NetAddressFamily.InterNetwork,
        23 => NetAddressFamily.InterNetworkV6,
        _ => NetAddressFamily.Unspecified,
    };

    private static NetSocketError SetIntSocketOption(nint socket, NetSocketOptionLevel level, NetSocketOptionName name, int value)
    {
        var (nativeLevel, nativeName) = GetSocketOption(level, name);

        unsafe
        {
            return Setsockopt(socket, nativeLevel, nativeName, &value, sizeof(int)) == socketError
                ? MapError(WSAGetLastError())
                : NetSocketError.Success;
        }
    }

    private static (int level, int name) GetSocketOption(NetSocketOptionLevel level, NetSocketOptionName name) => (level, name) switch
    {
        (NetSocketOptionLevel.Socket, NetSocketOptionName.ReuseAddress) => (0xffff, 0x0004),
        (NetSocketOptionLevel.Socket, NetSocketOptionName.SendBufferSize) => (0xffff, 0x1001),
        (NetSocketOptionLevel.Socket, NetSocketOptionName.ReceiveBufferSize) => (0xffff, 0x1002),
        (NetSocketOptionLevel.Tcp, NetSocketOptionName.NoDelay) => (6, 0x0001),
        (NetSocketOptionLevel.Ipv6, NetSocketOptionName.Ipv6Only) => (41, 27),
        _ => throw new NetSocketException(NetSocketError.ProtocolNotSupported),
    };

    private static NetSocketError MapError(int error) => error switch
    {
        995 => NetSocketError.OperationAborted,
        10004 => NetSocketError.Interrupted,
        10013 => NetSocketError.AccessDenied,
        10022 => NetSocketError.InvalidArgument,
        10035 => NetSocketError.WouldBlock,
        10036 => NetSocketError.InProgress,
        10037 => NetSocketError.AlreadyInProgress,
        10040 => NetSocketError.MessageSize,
        10043 => NetSocketError.ProtocolNotSupported,
        10047 => NetSocketError.AddressFamilyNotSupported,
        10048 => NetSocketError.AddressAlreadyInUse,
        10049 => NetSocketError.AddressNotAvailable,
        10050 => NetSocketError.NetworkDown,
        10051 => NetSocketError.NetworkUnreachable,
        10053 => NetSocketError.ConnectionAborted,
        10054 => NetSocketError.ConnectionReset,
        10055 => NetSocketError.NoBufferSpaceAvailable,
        10056 => NetSocketError.IsConnected,
        10057 => NetSocketError.NotConnected,
        10058 => NetSocketError.Shutdown,
        10060 => NetSocketError.TimedOut,
        10061 => NetSocketError.ConnectionRefused,
        10065 => NetSocketError.HostUnreachable,
        10101 => NetSocketError.Disconnecting,
        11001 => NetSocketError.HostNotFound,
        11002 => NetSocketError.TryAgain,
        _ => NetSocketError.SocketError,
    };

    private static void EnsureInitialized()
    {
        if (winsockInitialized != 0)
            return;

        var data = default(WsaData);
        var error = WsaStartup(0x202, ref data);

        if (error != 0)
            throw new NetSocketException(MapError(error), error);

        if (Interlocked.CompareExchange(ref winsockInitialized, 1, 0) != 0)
            _ = WsaCleanup();
    }

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "WSAStartup")]
    private static partial int WsaStartup(ushort versionRequested, ref WsaData data);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "WSACleanup")]
    private static partial int WsaCleanup();

    [LibraryImport("ws2_32.dll", EntryPoint = "WSAGetLastError")]
    private static partial int WSAGetLastError();

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "WSASocketW")]
    private static partial nint WsaSocketW(int addressFamily, int socketType, int protocolType, nint protocolInfo, uint group, int flags);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "ioctlsocket")]
    private static partial int Ioctlsocket(nint socket, int command, ref uint value);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "bind")]
    private static unsafe partial int Bind(nint socket, byte* address, int addressLength);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "listen")]
    private static partial int Listen(nint socket, int backlog);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "accept")]
    private static unsafe partial nint Accept(nint socket, byte* address, ref int addressLength);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "connect")]
    private static unsafe partial int Connect(nint socket, byte* address, int addressLength);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "send")]
    private static unsafe partial int Send(nint socket, byte* buffer, int length, int flags);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "recv")]
    private static unsafe partial int Recv(nint socket, byte* buffer, int length, int flags);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "shutdown")]
    private static partial int Shutdown(nint socket, int how);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "getsockname")]
    private static unsafe partial int Getsockname(nint socket, byte* address, ref int addressLength);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "getpeername")]
    private static unsafe partial int Getpeername(nint socket, byte* address, ref int addressLength);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "setsockopt")]
    private static unsafe partial int Setsockopt(nint socket, int level, int name, void* value, int valueLength);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "getsockopt")]
    private static unsafe partial int Getsockopt(nint socket, int level, int name, void* value, ref int valueLength);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "select")]
    private static unsafe partial int Select(int ignored, FdSet* readSet, FdSet* writeSet, FdSet* exceptSet, TimeValue* timeout);

    [LibraryImport("ws2_32.dll", SetLastError = true, EntryPoint = "closesocket")]
    public static partial int Close(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WsaData
    {
        public ushort Version;
        public ushort HighVersion;
        public fixed byte Description[257];
        public fixed byte SystemStatus[129];
        public ushort MaxSockets;
        public ushort MaxUdpDatagram;
        public IntPtr VendorInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TimeValue
    {
        public int Seconds;
        public int Microseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FdSet
    {
        public uint Count;
        public IntPtr Socket0;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LingerOption
    {
        public ushort OnOff;
        public ushort Linger;
    }
}
