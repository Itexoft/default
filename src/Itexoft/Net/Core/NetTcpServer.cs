// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.ExceptionServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Net.Sockets;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Core;

public delegate void NetTcpConnectionHandler(INetStream stream, CancelToken cancelToken);

public sealed class NetTcpServer(NetIpEndpoint endpoint) : IDisposable
{
    private Disposed disposed = new();
    private NetSocket? listener;
    private AtomicLock listenerLock = new();

    public NetIpEndpoint Endpoint { get; } = endpoint;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        using (this.listenerLock.Enter())
        {
            this.listener?.Dispose();
            this.listener = null;
        }
    }

    public INetStream Accept(CancelToken cancelToken = default)
    {
        var socket = this.GetListener(cancelToken).Accept(cancelToken);

        try
        {
            this.disposed.ThrowIf(cancelToken);

            return new NetStream(socket, true);
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }

    public void Run(NetTcpConnectionHandler handler, CancelToken cancelToken = default)
    {
        handler.Required();

        ExceptionDispatchInfo? failure = null;

        try
        {
            while (true)
            {
                cancelToken.ThrowIf();
                var stream = this.Accept(cancelToken);

                try
                {
                    _ = Promise.Run(() => handleConnection(stream), false, cancelToken);
                }
                catch
                {
                    stream.Dispose();

                    throw;
                }
            }
        }
        catch (ObjectDisposedException) when (this.disposed) { }
        catch (NetSocketException exception) when (this.disposed || cancelToken.IsRequested || IsClientConnectionAbort(exception)) { }
        catch (OperationCanceledException) when (cancelToken.IsRequested) { }

        failure?.Throw();

        return;

        void handleConnection(INetStream stream)
        {
            try
            {
                using (stream)
                    handler(stream, cancelToken);
            }
            catch (Exception exception)
            {
                if (this.disposed || cancelToken.IsRequested || IsClientConnectionAbort(exception))
                    return;

                var edi = ExceptionDispatchInfo.Capture(exception.GetBaseException());

                if (Interlocked.CompareExchange(ref failure, edi, null) is null)
                    this.Dispose();
            }
        }
    }

    private static NetSocket CreateListener(NetIpEndpoint endpoint)
    {
        var socket = new NetSocket(endpoint.AddressFamily, NetSocketType.Stream, NetProtocol.Tcp);

        try
        {
            socket.SetSocketOption(NetSocketOptionLevel.Socket, NetSocketOptionName.ReuseAddress, true);

            if (OperatingSystem.IsMacOS())
                socket.SetSocketOption(NetSocketOptionLevel.Socket, NetSocketOptionName.ReusePort, true);

            socket.Bind(endpoint);
            socket.Listen();

            return socket;
        }
        catch
        {
            socket.Dispose();

            throw;
        }
    }

    private NetSocket GetListener(CancelToken cancelToken)
    {
        this.disposed.ThrowIf(cancelToken);

        if (this.listener is NetSocket listener)
            return listener;

        using (this.listenerLock.Enter())
        {
            this.disposed.ThrowIf(cancelToken);

            if (this.listener is not null)
                return this.listener;

            return this.listener = CreateListener(this.Endpoint);
        }
    }

    private static bool IsClientConnectionAbort(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NetSocketException socketException && IsClientConnectionAbort(socketException.SocketErrorCode))
                return true;
        }

        return false;
    }

    private static bool IsClientConnectionAbort(NetSocketError socketError) => socketError is NetSocketError.ConnectionReset
        or NetSocketError.ConnectionAborted
        or NetSocketError.Shutdown
        or NetSocketError.OperationAborted
        or NetSocketError.NotConnected
        or NetSocketError.Disconnecting;
}
