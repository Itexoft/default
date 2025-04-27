// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;

namespace Itexoft.Net.Sockets.Internal;

internal static partial class SysSocketUnix
{
    private const int afInet = 2;
    private const int afInet6 = 10;
    private const int fGetFl = 3;
    private const int fSetFl = 4;
    private const int msgNoSignal = 0x4000;
    private const short pollIn = 0x001;
    private const short pollOut = 0x004;
    private const short pollErr = 0x008;
    private const short pollHup = 0x010;
    private const short pollNval = 0x020;
    public static bool IsUnix { get; } = Environment.OSVersion.Platform == PlatformID.Unix;

    public static SysSocketHandle CreateSocketHandle(NetAddressFamily addressFamily, NetSocketType socketType, NetSocketProtocol socketProtocol)
    {
        var handle = Socket(GetNativeAddressFamily(addressFamily), (int)socketType, (int)socketProtocol);

        if (handle == new nint(-1))
            throw new NetSocketException(MapError(Marshal.GetLastPInvokeError()), Marshal.GetLastPInvokeError());

        return new(handle);
    }

    public static void ConfigureNewSocket(ref SysSocketHandle handle, NetAddressFamily addressFamily, bool dualMode)
    {
        ConfigureSocket(handle.ToInt32());

        if (addressFamily == NetAddressFamily.InterNetworkV6)
            ThrowIfOptionFailure(ref handle, NetSocketOptionLevel.Ipv6, NetSocketOptionName.Ipv6Only, dualMode ? 0 : 1);
    }

    public static void ConfigureAcceptedSocket(ref SysSocketHandle handle, NetAddressFamily addressFamily, bool dualMode) =>
        ConfigureSocket(handle.ToInt32());

    public static NetSocketError GetAvailable(ref SysSocketHandle handle, out int available)
    {
        available = 0;

        return Ioctl(handle.ToInt32(), GetFionRead(), ref available) == -1 ? MapError(Marshal.GetLastPInvokeError()) : NetSocketError.Success;
    }

    public static NetSocketError GetSockName(ref SysSocketHandle handle, Span<byte> socketAddress, out int socketAddressLength)
    {
        var length = (uint)socketAddress.Length;

        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                if (Getsockname(handle.ToInt32(), buffer, ref length) == -1)
                {
                    socketAddressLength = 0;

                    return MapError(Marshal.GetLastPInvokeError());
                }
            }
        }

        socketAddressLength = (int)length;

        return NetSocketError.Success;
    }

    public static NetSocketError GetPeerName(ref SysSocketHandle handle, Span<byte> socketAddress, out int socketAddressLength)
    {
        var length = (uint)socketAddress.Length;

        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                if (Getpeername(handle.ToInt32(), buffer, ref length) == -1)
                {
                    socketAddressLength = 0;

                    return MapError(Marshal.GetLastPInvokeError());
                }
            }
        }

        socketAddressLength = (int)length;

        return NetSocketError.Success;
    }

    public static NetSocketError Bind(ref SysSocketHandle handle, ReadOnlySpan<byte> socketAddress)
    {
        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                return Bind(handle.ToInt32(), buffer, (uint)socketAddress.Length) == -1
                    ? MapError(Marshal.GetLastPInvokeError())
                    : NetSocketError.Success;
            }
        }
    }

    public static NetSocketError Listen(ref SysSocketHandle handle, int backlog) =>
        Listen(handle.ToInt32(), backlog) == -1 ? MapError(Marshal.GetLastPInvokeError()) : NetSocketError.Success;

    public static NetSocketError Accept(
        ref SysSocketHandle handle,
        Span<byte> socketAddress,
        out int socketAddressLength,
        out SysSocketHandle acceptedHandle)
    {
        var length = (uint)socketAddress.Length;

        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                var accepted = Accept(handle.ToInt32(), buffer, ref length);

                if (accepted == -1)
                {
                    socketAddressLength = 0;
                    acceptedHandle = new();

                    return MapError(Marshal.GetLastPInvokeError());
                }

                socketAddressLength = (int)length;
                acceptedHandle = new((nint)accepted);

                return NetSocketError.Success;
            }
        }
    }

    public static NetSocketError Connect(ref SysSocketHandle handle, ReadOnlySpan<byte> socketAddress)
    {
        unsafe
        {
            fixed (byte* buffer = socketAddress)
            {
                return Connect(handle.ToInt32(), buffer, (uint)socketAddress.Length) == -1
                    ? MapError(Marshal.GetLastPInvokeError())
                    : NetSocketError.Success;
            }
        }
    }

    public static NetSocketError GetPendingConnectError(ref SysSocketHandle handle, out NetSocketError error)
    {
        var optionValue = 0;
        var optionLength = sizeof(int);

        unsafe
        {
            if (Getsockopt(handle.ToInt32(), GetSocketLevel(), GetSocketErrorName(), &optionValue, ref optionLength) == -1)
            {
                error = MapError(Marshal.GetLastPInvokeError());

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
                var sent = Send(handle.ToInt32(), data, (nuint)buffer.Length, GetNativeSendFlags(flags));

                if (sent < 0)
                {
                    bytesTransferred = 0;

                    return MapError(Marshal.GetLastPInvokeError());
                }

                bytesTransferred = (int)sent;

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
                var received = Recv(handle.ToInt32(), data, (nuint)buffer.Length, (int)flags);

                if (received < 0)
                {
                    bytesTransferred = 0;

                    return MapError(Marshal.GetLastPInvokeError());
                }

                bytesTransferred = (int)received;

                return NetSocketError.Success;
            }
        }
    }

    public static NetSocketError Shutdown(ref SysSocketHandle handle, NetSocketShutdown how) =>
        Shutdown(handle.ToInt32(), (int)how) == -1 ? MapError(Marshal.GetLastPInvokeError()) : NetSocketError.Success;

    public static NetSocketError Wait(ref SysSocketHandle handle, SocketWaitMode mode, int timeoutMilliseconds, out bool ready)
    {
        var pollfd = new PollFd
        {
            FileDescriptor = handle.ToInt32(),
            Events = mode == SocketWaitMode.Read ? pollIn : pollOut,
        };

        ready = false;

        var result = Poll(ref pollfd, 1u, timeoutMilliseconds);

        if (result == -1)
            return MapError(Marshal.GetLastPInvokeError());

        ready = result > 0 && (pollfd.Revents & (short)(pollfd.Events | pollErr | pollHup | pollNval)) != 0;

        return NetSocketError.Success;
    }

    public static NetSocketError SetIntSocketOption(ref SysSocketHandle handle, NetSocketOptionLevel level, NetSocketOptionName name, int value) =>
        SetIntSocketOption(handle.ToInt32(), level, name, value);

    public static NetSocketError GetIntSocketOption(ref SysSocketHandle handle, NetSocketOptionLevel level, NetSocketOptionName name, out int value)
    {
        var (nativeLevel, nativeName) = GetSocketOption(level, name);
        var optionLength = sizeof(int);

        unsafe
        {
            var optionValue = 0;

            var error = Getsockopt(handle.ToInt32(), nativeLevel, nativeName, &optionValue, ref optionLength) == -1
                ? MapError(Marshal.GetLastPInvokeError())
                : NetSocketError.Success;

            value = optionValue;

            return error;
        }
    }

    public static NetSocketError SetLingerState(ref SysSocketHandle handle, NetLingerOption value)
    {
        var optionValue = new LingerOption
        {
            OnOff = value.Enabled ? 1 : 0,
            Seconds = value.LingerTime,
        };

        unsafe
        {
            return Setsockopt(handle.ToInt32(), GetSocketLevel(), GetLingerName(), &optionValue, sizeof(LingerOption)) == -1
                ? MapError(Marshal.GetLastPInvokeError())
                : NetSocketError.Success;
        }
    }

    public static NetSocketError GetLingerState(ref SysSocketHandle handle, out NetLingerOption value)
    {
        var optionLength = Marshal.SizeOf<LingerOption>();
        var optionValue = default(LingerOption);

        unsafe
        {
            if (Getsockopt(handle.ToInt32(), GetSocketLevel(), GetLingerName(), &optionValue, ref optionLength) == -1)
            {
                value = default;

                return MapError(Marshal.GetLastPInvokeError());
            }
        }

        value = new(optionValue.OnOff != 0, optionValue.Seconds);

        return NetSocketError.Success;
    }

    private static void ThrowIfOptionFailure(ref SysSocketHandle handle, NetSocketOptionLevel level, NetSocketOptionName name, int value)
    {
        var error = SetIntSocketOption(handle.ToInt32(), level, name, value);

        if (error != NetSocketError.Success)
            throw new NetSocketException(error);
    }

    public static int GetNativeAddressFamily(NetAddressFamily addressFamily) => addressFamily switch
    {
        NetAddressFamily.InterNetwork => afInet,
        NetAddressFamily.InterNetworkV6 => OperatingSystem.IsMacOS() ? 30 : afInet6,
        _ => 0,
    };

    public static NetAddressFamily FromNativeAddressFamily(ushort addressFamily) => addressFamily switch
    {
        afInet => NetAddressFamily.InterNetwork,
        10 or 30 => NetAddressFamily.InterNetworkV6,
        _ => NetAddressFamily.Unspecified,
    };

    private static void ConfigureSocket(int socket)
    {
        var currentFlags = Fcntl(socket, fGetFl, 0);

        if (currentFlags == -1 || Fcntl(socket, fSetFl, currentFlags | GetNonBlockingFlag()) == -1)
            throw new NetSocketException(MapError(Marshal.GetLastPInvokeError()), Marshal.GetLastPInvokeError());

        if (OperatingSystem.IsMacOS())
            EnableNoSigPipe(socket);
    }

    private static int GetNativeSendFlags(NetSocketFlags flags)
    {
        var nativeFlags = (int)flags;

        return OperatingSystem.IsLinux() ? nativeFlags | msgNoSignal : nativeFlags;
    }

    private static NetSocketError SetIntSocketOption(int socket, NetSocketOptionLevel level, NetSocketOptionName name, int value)
    {
        var (nativeLevel, nativeName) = GetSocketOption(level, name);

        unsafe
        {
            return Setsockopt(socket, nativeLevel, nativeName, &value, sizeof(int)) == -1
                ? MapError(Marshal.GetLastPInvokeError())
                : NetSocketError.Success;
        }
    }

    private static void EnableNoSigPipe(int socket)
    {
        var value = 1;

        unsafe
        {
            if (Setsockopt(socket, GetSocketLevel(), GetNoSigPipeName(), &value, sizeof(int)) == -1)
                throw new NetSocketException(MapError(Marshal.GetLastPInvokeError()), Marshal.GetLastPInvokeError());
        }
    }

    private static (int level, int name) GetSocketOption(NetSocketOptionLevel level, NetSocketOptionName name) => (level, name) switch
    {
        (NetSocketOptionLevel.Socket, NetSocketOptionName.ReuseAddress) => (GetSocketLevel(), GetReuseAddressName()),
        (NetSocketOptionLevel.Socket, NetSocketOptionName.SendBufferSize) => (GetSocketLevel(), GetSendBufferName()),
        (NetSocketOptionLevel.Socket, NetSocketOptionName.ReceiveBufferSize) => (GetSocketLevel(), GetReceiveBufferName()),
        (NetSocketOptionLevel.Tcp, NetSocketOptionName.NoDelay) => (6, 1),
        (NetSocketOptionLevel.Ipv6, NetSocketOptionName.Ipv6Only) => (41, GetIpv6OnlyName()),
        _ => throw new NetSocketException(NetSocketError.ProtocolNotSupported),
    };

    private static NetSocketError MapError(int error) => error switch
    {
        4 => NetSocketError.Interrupted,
        11 or 35 => NetSocketError.WouldBlock,
        13 => NetSocketError.AccessDenied,
        22 => NetSocketError.InvalidArgument,
        32 => NetSocketError.Shutdown,
        36 or 115 => NetSocketError.InProgress,
        37 or 114 => NetSocketError.AlreadyInProgress,
        40 or 90 => NetSocketError.MessageSize,
        43 or 93 => NetSocketError.ProtocolNotSupported,
        47 or 97 => NetSocketError.AddressFamilyNotSupported,
        48 or 98 => NetSocketError.AddressAlreadyInUse,
        49 or 99 => NetSocketError.AddressNotAvailable,
        50 or 100 => NetSocketError.NetworkDown,
        51 or 101 => NetSocketError.NetworkUnreachable,
        53 or 103 => NetSocketError.ConnectionAborted,
        54 or 104 => NetSocketError.ConnectionReset,
        55 or 105 => NetSocketError.NoBufferSpaceAvailable,
        57 or 107 => NetSocketError.NotConnected,
        58 or 108 => NetSocketError.Shutdown,
        60 or 110 => NetSocketError.TimedOut,
        61 or 111 => NetSocketError.ConnectionRefused,
        65 or 113 => NetSocketError.HostUnreachable,
        _ => NetSocketError.SocketError,
    };

    private static int GetSocketLevel() => OperatingSystem.IsMacOS() ? unchecked((int)0xffff) : 1;
    private static int GetReuseAddressName() => OperatingSystem.IsMacOS() ? 0x0004 : 2;
    private static int GetSendBufferName() => OperatingSystem.IsMacOS() ? 0x1001 : 7;
    private static int GetReceiveBufferName() => OperatingSystem.IsMacOS() ? 0x1002 : 8;
    private static int GetIpv6OnlyName() => OperatingSystem.IsMacOS() ? 27 : 26;
    private static int GetSocketErrorName() => OperatingSystem.IsMacOS() ? 0x1007 : 4;
    private static int GetLingerName() => OperatingSystem.IsMacOS() ? 0x0080 : 13;
    private static int GetNoSigPipeName() => OperatingSystem.IsMacOS() ? 0x1022 : 0;
    private static int GetNonBlockingFlag() => OperatingSystem.IsMacOS() ? 0x4 : 0x800;
    private static int GetFionRead() => OperatingSystem.IsMacOS() ? unchecked((int)0x4004667F) : 0x541B;

    [LibraryImport("libc", SetLastError = true, EntryPoint = "socket")]
    private static partial nint Socket(int domain, int type, int protocol);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "bind")]
    private static unsafe partial int Bind(int socket, byte* address, uint addressLength);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "listen")]
    private static partial int Listen(int socket, int backlog);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "accept")]
    private static unsafe partial int Accept(int socket, byte* address, ref uint addressLength);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "connect")]
    private static unsafe partial int Connect(int socket, byte* address, uint addressLength);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "send")]
    private static unsafe partial nint Send(int socket, byte* buffer, nuint length, int flags);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "recv")]
    private static unsafe partial nint Recv(int socket, byte* buffer, nuint length, int flags);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "shutdown")]
    private static partial int Shutdown(int socket, int how);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "poll")]
    private static partial int Poll(ref PollFd fds, uint count, int timeout);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "ioctl")]
    private static partial int Ioctl(int fd, int request, ref int value);

    private static int Fcntl(int fd, int command, nint value) => OperatingSystem.IsMacOS()
        ? FcntlMac(fd, command, value)
        : FcntlUnix(fd, command, value);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "__fcntl")]
    private static partial int FcntlMac(int fd, int command, nint value);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "fcntl")]
    private static partial int FcntlUnix(int fd, int command, nint value);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "getsockname")]
    private static unsafe partial int Getsockname(int socket, byte* address, ref uint addressLength);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "getpeername")]
    private static unsafe partial int Getpeername(int socket, byte* address, ref uint addressLength);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "setsockopt")]
    private static unsafe partial int Setsockopt(int socket, int level, int name, void* value, int valueLength);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "getsockopt")]
    private static unsafe partial int Getsockopt(int socket, int level, int name, void* value, ref int valueLength);

    [LibraryImport("libc", SetLastError = true, EntryPoint = "close")]
    public static partial int Close(nint handle);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int FileDescriptor;
        public short Events;
        public short Revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LingerOption
    {
        public int OnOff;
        public int Seconds;
    }
}
