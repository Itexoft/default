// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Net.Sockets.Internal;
using Itexoft.Threading;

namespace Itexoft.Net.Sockets;

public sealed class NetSocket : IDisposable
{
    private const int waitQuantumMilliseconds = 50;
    private const int sockaddrMaxSize = 28;
    private const int parameterlessListenBacklog = int.MaxValue;

    private const byte sockaddrInSize = 16;
    private const byte sockaddrIn6Size = 28;
    private readonly NetAddressFamily addressFamily;
    
    private readonly NetProtocol protocol;
    private readonly NetSocketType socketType;

    private Disposed disposed = new();
    private bool dualMode;

    private SysSocketHandle handle;
    private int receiveTimeoutMilliseconds = Timeout.Infinite;
    private int sendTimeoutMilliseconds = Timeout.Infinite;

    public NetSocket(NetAddressFamily addressFamily, NetSocketType socketType, NetProtocol protocol)
    {
        this.addressFamily = addressFamily;
        this.socketType = socketType;
        this.protocol = protocol;
        
        var socketProtocol = this.protocol switch
        {
            NetProtocol.Tcp => NetSocketProtocol.Tcp,
            NetProtocol.Udp => NetSocketProtocol.Udp,
            _ => throw new ArgumentOutOfRangeException(nameof(protocol))
        };
            
        this.handle = SysSocketHandle.Create(addressFamily, socketType, socketProtocol);

        this.handle.ConfigureNewSocket(addressFamily, false);
    }
    

    private NetSocket(
        SysSocketHandle handle,
        NetAddressFamily addressFamily,
        NetSocketType socketType,
        NetProtocol socketProtocol,
        bool dualMode,
        int sendTimeoutMilliseconds,
        int receiveTimeoutMilliseconds)
    {
        this.handle = handle;
        this.addressFamily = addressFamily;
        this.socketType = socketType;
        this.protocol = socketProtocol;
        this.dualMode = dualMode;
        this.sendTimeoutMilliseconds = sendTimeoutMilliseconds;
        this.receiveTimeoutMilliseconds = receiveTimeoutMilliseconds;

        this.handle.ConfigureAcceptedSocket(addressFamily, dualMode);
    }

    public static TimeSpan DefaultReceiveTimeout { get; } = Timeout.InfiniteTimeSpan;
    public static TimeSpan DefaultSendTimeout { get; } = Timeout.InfiniteTimeSpan;

    public static bool OsSupportsIPv4 { get; } = ProbeSupport(NetAddressFamily.InterNetwork);
    public static bool OsSupportsIPv6 { get; } = ProbeSupport(NetAddressFamily.InterNetworkV6);

    public static bool Probe(NetIpEndpoint endpoint, CancelToken cancelToken = default)
    {
        var socketType = endpoint.Protocol switch
        {
            NetProtocol.Tcp => NetSocketType.Stream,
            NetProtocol.Udp => NetSocketType.Dgram,
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint.Protocol)),
        };

        try
        {
            using var socket = new NetSocket(endpoint.AddressFamily, socketType, endpoint.Protocol);
            socket.Connect(endpoint, cancelToken);

            return true;
        }
        catch (NetSocketException ex) when (IsProbeUnavailable(ex.SocketErrorCode))
        {
            return false;
        }
    }

    public bool DualMode
    {
        get => this.dualMode;
        set
        {
            this.ThrowIfDisposed();

            if (this.addressFamily != NetAddressFamily.InterNetworkV6)
            {
                if (value)
                    throw new NetSocketException(NetSocketError.ProtocolNotSupported);

                return;
            }

            if (this.dualMode == value)
                return;

            ThrowIfError(this.handle.SetIntSocketOptionCore(NetSocketOptionLevel.Ipv6, NetSocketOptionName.Ipv6Only, value ? 0 : 1));
            this.dualMode = value;
        }
    }

    public bool NoDelay
    {
        get => this.GetBooleanSocketOption(NetSocketOptionLevel.Tcp, NetSocketOptionName.NoDelay);
        set => this.SetBooleanSocketOption(NetSocketOptionLevel.Tcp, NetSocketOptionName.NoDelay, value);
    }

    public int SendBufferSize
    {
        get => this.GetIntSocketOption(NetSocketOptionLevel.Socket, NetSocketOptionName.SendBufferSize);
        set => this.SetSocketOption(NetSocketOptionLevel.Socket, NetSocketOptionName.SendBufferSize, value);
    }

    public int ReceiveBufferSize
    {
        get => this.GetIntSocketOption(NetSocketOptionLevel.Socket, NetSocketOptionName.ReceiveBufferSize);
        set => this.SetSocketOption(NetSocketOptionLevel.Socket, NetSocketOptionName.ReceiveBufferSize, value);
    }

    public int Available
    {
        get
        {
            this.ThrowIfDisposed();
            ThrowIfError(this.handle.GetAvailableCore(out var available));

            return available;
        }
    }

    public TimeSpan ReceiveTimeout
    {
        get => TimeSpan.FromMilliseconds(this.receiveTimeoutMilliseconds);
        set => this.receiveTimeoutMilliseconds = NormalizeTimeoutMilliseconds(value.TimeoutMilliseconds);
    }

    public TimeSpan SendTimeout
    {
        get => TimeSpan.FromMilliseconds(this.sendTimeoutMilliseconds);
        set => this.sendTimeoutMilliseconds = NormalizeTimeoutMilliseconds(value.TimeoutMilliseconds);
    }

    public NetLingerOption? LingerState
    {
        get
        {
            this.ThrowIfDisposed();
            ThrowIfError(this.handle.GetLingerStateCore(out var value));

            return value;
        }
        set
        {
            this.ThrowIfDisposed();
            ThrowIfError(this.handle.SetLingerStateCore(value ?? default));
        }
    }

    public NetEndpoint LocalEndPoint => this.TryGetLocalEndPoint(out var endpoint) ? endpoint : default;

    public NetEndpoint RemoteEndPoint => this.TryGetRemoteEndPoint(out var endpoint) ? endpoint : default;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        try
        {
            this.handle.ShutdownCore(NetSocketShutdown.Both);
        }
        catch
        {
            // ignored
        }
        finally
        {
            this.handle.Close();
        }
    }

    public int Send(ReadOnlySpan<byte> span, NetSocketFlags flags, CancelToken cancelToken)
    {
        if (span.IsEmpty)
            return 0;

        while (true)
        {
            this.ThrowIfDisposed();
            cancelToken.ThrowIf();

            var error = this.handle.SendCore(span, flags, out var bytesSent);

            switch (error)
            {
                case NetSocketError.Success:
                    return bytesSent;
                case NetSocketError.WouldBlock:
                case NetSocketError.InProgress:
                case NetSocketError.AlreadyInProgress:
                    this.WaitUntilReady(SocketWaitMode.Write, this.sendTimeoutMilliseconds, cancelToken);

                    continue;
                case NetSocketError.Interrupted:
                    continue;
                default:
                    ThrowIfError(error);

                    return 0;
            }
        }
    }

    public int Receive(Span<byte> span, NetSocketFlags flags, CancelToken cancelToken)
    {
        if (span.IsEmpty)
            return 0;

        while (true)
        {
            this.ThrowIfDisposed();
            cancelToken.ThrowIf();

            var error = this.handle.ReceiveCore(span, flags, out var bytesReceived);

            switch (error)
            {
                case NetSocketError.Success:
                    return bytesReceived;
                case NetSocketError.WouldBlock:
                case NetSocketError.InProgress:
                case NetSocketError.AlreadyInProgress:
                    this.WaitUntilReady(SocketWaitMode.Read, this.receiveTimeoutMilliseconds, cancelToken);

                    continue;
                case NetSocketError.Interrupted:
                    continue;
                default:
                    ThrowIfError(error);

                    return 0;
            }
        }
    }

    public void Connect(NetIpEndpoint endPoint, CancelToken cancelToken)
    {
        Span<byte> socketAddress = stackalloc byte[sockaddrMaxSize];

        while (true)
        {
            this.ThrowIfDisposed();
            cancelToken.ThrowIf();

            var socketAddressLength = EncodeSocketAddress(endPoint, this.addressFamily, this.dualMode, socketAddress);
            var error = this.handle.ConnectCore(socketAddress[..socketAddressLength]);

            switch (error)
            {
                case NetSocketError.Success:
                    return;
                case NetSocketError.WouldBlock:
                case NetSocketError.InProgress:
                case NetSocketError.AlreadyInProgress:
                    this.WaitUntilReady(SocketWaitMode.Write, Timeout.Infinite, cancelToken);
                    ThrowIfError(this.handle.GetPendingConnectErrorCore(out error));

                    if (error == NetSocketError.Success)
                        return;

                    if (error is NetSocketError.WouldBlock
                        or NetSocketError.InProgress
                        or NetSocketError.AlreadyInProgress
                        or NetSocketError.Interrupted)
                        continue;

                    goto default;
                case NetSocketError.Interrupted:
                    continue;
                default:
                    ThrowIfError(error);

                    return;
            }
        }
    }

    public void Shutdown(NetSocketShutdown how)
    {
        this.ThrowIfDisposed();
        ThrowIfError(this.handle.ShutdownCore(how));
    }

    public void SetSocketOption(NetSocketOptionLevel socketOptionLevel, NetSocketOptionName socketOptionName, bool optionValue) =>
        this.SetSocketOption(socketOptionLevel, socketOptionName, optionValue ? 1 : 0);

    public void SetSocketOption(NetSocketOptionLevel socketOptionLevel, NetSocketOptionName socketOptionName, int optionValue)
    {
        this.ThrowIfDisposed();

        switch (socketOptionLevel, socketOptionName)
        {
            case (NetSocketOptionLevel.Socket, NetSocketOptionName.ReuseAddress):
            case (NetSocketOptionLevel.Socket, NetSocketOptionName.SendBufferSize):
            case (NetSocketOptionLevel.Socket, NetSocketOptionName.ReceiveBufferSize):
            case (NetSocketOptionLevel.Tcp, NetSocketOptionName.NoDelay):
                ThrowIfError(this.handle.SetIntSocketOptionCore(socketOptionLevel, socketOptionName, optionValue));

                return;

            case (NetSocketOptionLevel.Socket, NetSocketOptionName.SendTimeout):
                this.sendTimeoutMilliseconds = NormalizeTimeoutMilliseconds(optionValue);

                return;

            case (NetSocketOptionLevel.Socket, NetSocketOptionName.ReceiveTimeout):
                this.receiveTimeoutMilliseconds = NormalizeTimeoutMilliseconds(optionValue);

                return;

            case (NetSocketOptionLevel.Ipv6, NetSocketOptionName.Ipv6Only):
                if (this.addressFamily != NetAddressFamily.InterNetworkV6)
                    throw new NetSocketException(NetSocketError.ProtocolNotSupported);

                ThrowIfError(this.handle.SetIntSocketOptionCore(socketOptionLevel, socketOptionName, optionValue));
                this.dualMode = optionValue == 0;

                return;
        }

        throw new NetSocketException(NetSocketError.ProtocolNotSupported);
    }

    public void Listen(int backlog)
    {
        this.ThrowIfDisposed();
        ThrowIfError(this.handle.ListenCore(backlog));
    }

    public void Listen() => this.Listen(parameterlessListenBacklog);

    public void Bind(NetIpEndpoint endPoint)
    {
        this.ThrowIfDisposed();
        Span<byte> socketAddress = stackalloc byte[sockaddrMaxSize];
        var socketAddressLength = EncodeSocketAddress(endPoint, this.addressFamily, this.dualMode, socketAddress);
        ThrowIfError(this.handle.BindCore(socketAddress[..socketAddressLength]));
    }

    public NetSocket Accept(CancelToken cancelToken)
    {
        Span<byte> socketAddress = stackalloc byte[sockaddrMaxSize];

        while (true)
        {
            this.ThrowIfDisposed();
            cancelToken.ThrowIf();

            var error = this.handle.AcceptCore(socketAddress, out _, out var acceptedHandle);

            switch (error)
            {
                case NetSocketError.Success:
                    return new NetSocket(
                        acceptedHandle,
                        this.addressFamily,
                        this.socketType,
                        this.protocol,
                        this.dualMode,
                        this.sendTimeoutMilliseconds,
                        this.receiveTimeoutMilliseconds);
                case NetSocketError.WouldBlock:
                case NetSocketError.InProgress:
                case NetSocketError.AlreadyInProgress:
                    this.WaitUntilReady(SocketWaitMode.Read, Timeout.Infinite, cancelToken);

                    continue;
                case NetSocketError.Interrupted:
                case NetSocketError.ConnectionAborted:
                case NetSocketError.NetworkDown:
                case NetSocketError.NetworkUnreachable:
                case NetSocketError.HostUnreachable:
                    continue;
                default:
                    ThrowIfError(error);

                    return null!;
            }
        }
    }

    private int GetIntSocketOption(NetSocketOptionLevel level, NetSocketOptionName name)
    {
        this.ThrowIfDisposed();
        ThrowIfError(this.handle.GetIntSocketOptionCore(level, name, out var value));

        return value;
    }

    private bool GetBooleanSocketOption(NetSocketOptionLevel level, NetSocketOptionName name) => this.GetIntSocketOption(level, name) != 0;

    private void SetBooleanSocketOption(NetSocketOptionLevel level, NetSocketOptionName name, bool value) =>
        this.SetSocketOption(level, name, value ? 1 : 0);

    private static int NormalizeTimeoutMilliseconds(int timeoutMilliseconds)
    {
        if (timeoutMilliseconds < Timeout.Infinite)
            throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));

        return timeoutMilliseconds <= 0 ? Timeout.Infinite : timeoutMilliseconds;
    }

    private bool TryGetLocalEndPoint(out NetIpEndpoint endpoint)
    {
        endpoint = default;

        if (this.disposed)
            return false;

        Span<byte> socketAddress = stackalloc byte[sockaddrMaxSize];
        var error = this.handle.GetSockNameCore(socketAddress, out var socketAddressLength);

        if (error != NetSocketError.Success)
            return false;

        endpoint = DecodeSocketAddress(socketAddress[..socketAddressLength]);

        return true;
    }

    private bool TryGetRemoteEndPoint(out NetIpEndpoint endpoint)
    {
        endpoint = default;

        if (this.disposed)
            return false;

        Span<byte> socketAddress = stackalloc byte[sockaddrMaxSize];
        var error = this.handle.GetPeerNameCore(socketAddress, out var socketAddressLength);

        if (error is NetSocketError.NotConnected or NetSocketError.InvalidArgument or NetSocketError.SocketError)
            return false;

        ThrowIfError(error);

        endpoint = DecodeSocketAddress(socketAddress[..socketAddressLength]);

        return true;
    }

    private void WaitUntilReady(SocketWaitMode mode, int timeoutMilliseconds, CancelToken cancelToken) =>
        this.WaitUntilReady(mode, timeoutMilliseconds, cancelToken, out _);

    private bool WaitUntilReady(SocketWaitMode mode, int timeoutMilliseconds, CancelToken cancelToken, out bool ready)
    {
        ready = false;

        var start = timeoutMilliseconds == Timeout.Infinite ? 0L : Environment.TickCount64;

        while (true)
        {
            cancelToken.ThrowIf();

            if (this.disposed)
                throw new NetSocketException(NetSocketError.OperationAborted);

            var wait = timeoutMilliseconds == Timeout.Infinite
                ? waitQuantumMilliseconds
                : Math.Min(waitQuantumMilliseconds, Math.Max(0, timeoutMilliseconds - (int)(Environment.TickCount64 - start)));

            var error = this.handle.WaitCore(mode, wait, out ready);

            if (error == NetSocketError.Interrupted)
                continue;

            ThrowIfError(error);

            if (ready)
                return true;

            if (timeoutMilliseconds != Timeout.Infinite && Environment.TickCount64 - start >= timeoutMilliseconds)
                throw new NetSocketException(NetSocketError.TimedOut);

            if (timeoutMilliseconds == 0)
                return false;
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
            throw new NetSocketException(NetSocketError.OperationAborted);
    }

    private static void ThrowIfError(NetSocketError error)
    {
        if (error != NetSocketError.Success)
            throw new NetSocketException(error);
    }

    private static bool ProbeSupport(NetAddressFamily family)
    {
        SysSocketHandle handle = default;

        try
        {
            handle = SysSocketHandle.Create(family, NetSocketType.Stream, NetSocketProtocol.Tcp);

            return !handle.IsInvalid;
        }
        catch (NetSocketException ex) when (ex.SocketErrorCode is NetSocketError.AddressFamilyNotSupported or NetSocketError.ProtocolNotSupported)
        {
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            handle.Close();
        }
    }

    private static bool IsProbeUnavailable(NetSocketError error) => error is NetSocketError.ConnectionRefused
        or NetSocketError.TimedOut
        or NetSocketError.NetworkUnreachable
        or NetSocketError.HostUnreachable
        or NetSocketError.AddressFamilyNotSupported
        or NetSocketError.AddressNotAvailable;

    private static bool IsDisconnected(NetSocketError error) => error is NetSocketError.ConnectionReset
        or NetSocketError.ConnectionAborted
        or NetSocketError.Shutdown
        or NetSocketError.NotConnected
        or NetSocketError.Disconnecting;

    private static int EncodeSocketAddress(NetIpEndpoint endpoint, NetAddressFamily socketAddressFamily, bool dualMode, Span<byte> destination)
    {
        destination.Clear();
        var nativeSocketAddressFamily = GetNativeAddressFamily(socketAddressFamily);

        if (endpoint.AddressFamily == NetAddressFamily.InterNetwork)
        {
            if (socketAddressFamily == NetAddressFamily.InterNetworkV6 && dualMode)
            {
                WriteNativeAddressFamily(destination, nativeSocketAddressFamily, sockaddrIn6Size);
                BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(int)endpoint.Port);
                destination[18] = 0xff;
                destination[19] = 0xff;
                endpoint.IpAddress.WriteBytes(destination[20..24]);

                return sockaddrIn6Size;
            }

            if (socketAddressFamily != NetAddressFamily.InterNetwork)
                throw new NetSocketException(NetSocketError.AddressFamilyNotSupported);

            WriteNativeAddressFamily(destination, nativeSocketAddressFamily, sockaddrInSize);
            BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(int)endpoint.Port);
            endpoint.IpAddress.WriteBytes(destination[4..8]);

            return sockaddrInSize;
        }

        if (endpoint.AddressFamily != NetAddressFamily.InterNetworkV6 || socketAddressFamily != NetAddressFamily.InterNetworkV6)
            throw new NetSocketException(NetSocketError.AddressFamilyNotSupported);

        WriteNativeAddressFamily(destination, nativeSocketAddressFamily, sockaddrIn6Size);
        BinaryPrimitives.WriteUInt16BigEndian(destination[2..], (ushort)(int)endpoint.Port);
        WriteUInt32NativeEndian(destination[24..], endpoint.IpAddress.ScopeId);
        endpoint.IpAddress.WriteBytes(destination[8..24]);

        return sockaddrIn6Size;
    }

    private static NetIpEndpoint DecodeSocketAddress(ReadOnlySpan<byte> socketAddress)
    {
        var family = ReadNativeAddressFamily(socketAddress);

        switch (family)
        {
            case NetAddressFamily.InterNetwork:
                return new(new NetIpAddress(socketAddress[4..8]), BinaryPrimitives.ReadUInt16BigEndian(socketAddress[2..4]));

            case NetAddressFamily.InterNetworkV6:
            {
                var bytes = socketAddress[8..24];

                if (IsMappedIpv4(bytes))
                    return new(new NetIpAddress(bytes[12..16]), BinaryPrimitives.ReadUInt16BigEndian(socketAddress[2..4]));

                return new(
                    new NetIpAddress(bytes, ReadUInt32NativeEndian(socketAddress[24..28])),
                    BinaryPrimitives.ReadUInt16BigEndian(socketAddress[2..4]));
            }

            default:
                throw new NetSocketException(NetSocketError.AddressFamilyNotSupported);
        }
    }

    private static bool IsMappedIpv4(ReadOnlySpan<byte> bytes) =>
        bytes.Length == 16 && bytes[..10].SequenceEqual(stackalloc byte[10]) && bytes[10] == 0xff && bytes[11] == 0xff;

    private static int GetNativeAddressFamily(NetAddressFamily family) =>
        OperatingSystem.IsWindows() ? SysSocketWindows.GetNativeAddressFamily(family) : SysSocketUnix.GetNativeAddressFamily(family);

    private static NetAddressFamily FromNativeAddressFamily(ushort family) =>
        OperatingSystem.IsWindows() ? SysSocketWindows.FromNativeAddressFamily(family) : SysSocketUnix.FromNativeAddressFamily(family);

    private static NetAddressFamily ReadNativeAddressFamily(ReadOnlySpan<byte> socketAddress) =>
        OperatingSystem.IsMacOS()
            ? FromNativeAddressFamily(socketAddress.Length > 1 ? socketAddress[1] : (ushort)0)
            : FromNativeAddressFamily(ReadUInt16NativeEndian(socketAddress));

    private static void WriteNativeAddressFamily(Span<byte> destination, int family, byte length)
    {
        if (OperatingSystem.IsMacOS())
        {
            destination[0] = length;
            destination[1] = (byte)family;

            return;
        }

        WriteUInt16NativeEndian(destination, (ushort)family);
    }

    private static ushort ReadUInt16NativeEndian(ReadOnlySpan<byte> source) =>
        BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(source) : BinaryPrimitives.ReadUInt16BigEndian(source);

    private static void WriteUInt16NativeEndian(Span<byte> destination, ushort value)
    {
        if (BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt16BigEndian(destination, value);
    }

    private static uint ReadUInt32NativeEndian(ReadOnlySpan<byte> source) =>
        BitConverter.IsLittleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(source) : BinaryPrimitives.ReadUInt32BigEndian(source);

    private static void WriteUInt32NativeEndian(Span<byte> destination, uint value)
    {
        if (BitConverter.IsLittleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(destination, value);
        else
            BinaryPrimitives.WriteUInt32BigEndian(destination, value);
    }
}
