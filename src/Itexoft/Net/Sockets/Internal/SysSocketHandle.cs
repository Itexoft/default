// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Net.Sockets.Internal;

internal struct SysSocketHandle(nint value)
{
    private readonly nint value = value;

    public SysSocketHandle() : this(-1) { }

    public bool IsInvalid => this.value == -1;

    public bool Close()
    {
        if (this.IsInvalid)
            return false;

        if (SysSocketWindows.IsWindows)
            return SysSocketWindows.Close(this.value) == 0;

        if (SysSocketUnix.IsUnix)
            return SysSocketUnix.Close(this.value) == 0;

        throw new NotSupportedException("Unsupported platform.");
    }

    internal static SysSocketHandle Create(NetAddressFamily addressFamily, NetSocketType socketType, NetSocketProtocol socketProtocol)
    {
        if (SysSocketWindows.IsWindows)
            return SysSocketWindows.CreateSocketHandle(addressFamily, socketType, socketProtocol);

        if (SysSocketUnix.IsUnix)
            return SysSocketUnix.CreateSocketHandle(addressFamily, socketType, socketProtocol);

        throw new NotSupportedException("Unsupported platform.");
    }

    public static implicit operator nint(SysSocketHandle handle) => handle.value;

    public int ToInt32() => this.value.ToInt32();
}

internal enum SocketWaitMode
{
    Read,
    Write,
}

internal static partial class SysSocketHandleExtensions
{
    extension(ref SysSocketHandle handle)
    {
        public void ConfigureNewSocket(NetAddressFamily addressFamily, bool dualMode)
        {
            if (SysSocketWindows.IsWindows)
                SysSocketWindows.ConfigureNewSocket(ref handle, addressFamily, dualMode);
            else
                SysSocketUnix.ConfigureNewSocket(ref handle, addressFamily, dualMode);
        }

        public void ConfigureAcceptedSocket(NetAddressFamily addressFamily, bool dualMode)
        {
            if (SysSocketWindows.IsWindows)
                SysSocketWindows.ConfigureAcceptedSocket(ref handle, addressFamily, dualMode);
            else
                SysSocketUnix.ConfigureAcceptedSocket(ref handle, addressFamily, dualMode);
        }

        public NetSocketError GetAvailableCore(out int available) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.GetAvailable(ref handle, out available)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.GetAvailable(ref handle, out available)
                : throw new PlatformNotSupportedException();

        public NetSocketError GetSockNameCore(Span<byte> socketAddress, out int socketAddressLength) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.GetSockName(ref handle, socketAddress, out socketAddressLength)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.GetSockName(ref handle, socketAddress, out socketAddressLength)
                : throw new PlatformNotSupportedException();

        public NetSocketError GetPeerNameCore(Span<byte> socketAddress, out int socketAddressLength) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.GetPeerName(ref handle, socketAddress, out socketAddressLength)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.GetPeerName(ref handle, socketAddress, out socketAddressLength)
                : throw new PlatformNotSupportedException();

        public NetSocketError BindCore(ReadOnlySpan<byte> socketAddress) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.Bind(ref handle, socketAddress)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.Bind(ref handle, socketAddress)
                : throw new PlatformNotSupportedException();

        public NetSocketError ListenCore(int backlog) => SysSocketWindows.IsWindows ? SysSocketWindows.Listen(ref handle, backlog) :
            SysSocketUnix.IsUnix ? SysSocketUnix.Listen(ref handle, backlog) : throw new PlatformNotSupportedException();

        public NetSocketError AcceptCore(Span<byte> socketAddress, out int socketAddressLength, out SysSocketHandle acceptedHandle) =>
            SysSocketWindows.IsWindows ? SysSocketWindows.Accept(ref handle, socketAddress, out socketAddressLength, out acceptedHandle) :
            SysSocketUnix.IsUnix ? SysSocketUnix.Accept(ref handle, socketAddress, out socketAddressLength, out acceptedHandle) :
            throw new PlatformNotSupportedException();

        public NetSocketError ConnectCore(ReadOnlySpan<byte> socketAddress) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.Connect(ref handle, socketAddress)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.Connect(ref handle, socketAddress)
                : throw new PlatformNotSupportedException();

        public NetSocketError GetPendingConnectErrorCore(out NetSocketError error) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.GetPendingConnectError(ref handle, out error)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.GetPendingConnectError(ref handle, out error)
                : throw new PlatformNotSupportedException();

        public NetSocketError SendCore(ReadOnlySpan<byte> buffer, NetSocketFlags flags, out int bytesTransferred) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.Send(ref handle, buffer, flags, out bytesTransferred)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.Send(ref handle, buffer, flags, out bytesTransferred)
                : throw new PlatformNotSupportedException();

        public NetSocketError ReceiveCore(Span<byte> buffer, NetSocketFlags flags, out int bytesTransferred) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.Receive(ref handle, buffer, flags, out bytesTransferred)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.Receive(ref handle, buffer, flags, out bytesTransferred)
                : throw new PlatformNotSupportedException();

        public NetSocketError ShutdownCore(NetSocketShutdown how) => SysSocketWindows.IsWindows ? SysSocketWindows.Shutdown(ref handle, how) :
            SysSocketUnix.IsUnix ? SysSocketUnix.Shutdown(ref handle, how) : throw new PlatformNotSupportedException();

        public NetSocketError WaitCore(SocketWaitMode mode, int timeoutMilliseconds, out bool ready) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.Wait(ref handle, mode, timeoutMilliseconds, out ready)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.Wait(ref handle, mode, timeoutMilliseconds, out ready)
                : throw new PlatformNotSupportedException();

        public NetSocketError SetIntSocketOptionCore(NetSocketOptionLevel level, NetSocketOptionName name, int value) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.SetIntSocketOption(ref handle, level, name, value)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.SetIntSocketOption(ref handle, level, name, value)
                : throw new PlatformNotSupportedException();

        public NetSocketError GetIntSocketOptionCore(NetSocketOptionLevel level, NetSocketOptionName name, out int value) =>
            SysSocketWindows.IsWindows ? SysSocketWindows.GetIntSocketOption(ref handle, level, name, out value) :
            SysSocketUnix.IsUnix ? SysSocketUnix.GetIntSocketOption(ref handle, level, name, out value) : throw new PlatformNotSupportedException();

        public NetSocketError SetLingerStateCore(NetLingerOption value) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.SetLingerState(ref handle, value)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.SetLingerState(ref handle, value)
                : throw new PlatformNotSupportedException();

        public NetSocketError GetLingerStateCore(out NetLingerOption value) => SysSocketWindows.IsWindows
            ?
            SysSocketWindows.GetLingerState(ref handle, out value)
            : SysSocketUnix.IsUnix
                ? SysSocketUnix.GetLingerState(ref handle, out value)
                : throw new PlatformNotSupportedException();
    }
}
