// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Sockets;
using Itexoft.Extensions;
using Itexoft.Net.Proxies;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Core;

public sealed class NetTcpConnector(params INetProxy[] proxies) : NetConnector(NetTransports.Tcp, proxies)
{
    public bool NoDelay { get; init; } = true;
    public int SendBufferSize { get; init; } = 8192;
    public int ReceiveBufferSize { get; init; } = 8192;
    public bool KeepAliveEnabled { get; init; }
    public TimeSpan? KeepAliveTime { get; init; }
    public TimeSpan? KeepAliveInterval { get; init; }
    public int? KeepAliveRetryCount { get; init; }

    public override StackTask<INetConnectionHandle> ConnectAsync(NetIpEndpoint endpoint, Func<StackTask> dispose, CancelToken cancelToken)
    {
        return new StackTask<INetConnectionHandle>(new NetConnectionHandle(endpoint, new NetLazyStream(getStream)));

        async StackTask<INetStream> getStream()
        {
            var socket = this.CreateSocket(dispose.Required());
            await socket.ConnectAsync(endpoint, cancelToken);

            return new NetStream(socket, this.Access, true);
        }
    }

    private NetSocket CreateSocket(Func<StackTask> dispose)
    {
        var socket = new NetSocket(SocketType.Stream, ProtocolType.Tcp, dispose);
        socket.NoDelay = this.NoDelay;
        socket.SendBufferSize = this.SendBufferSize;
        socket.ReceiveBufferSize = this.ReceiveBufferSize;
        socket.KeepAliveEnabled = this.KeepAliveEnabled;
        socket.KeepAliveInterval = this.KeepAliveInterval;
        socket.KeepAliveRetryCount = this.KeepAliveRetryCount;
        socket.KeepAliveTime = this.KeepAliveTime;

        return socket;
    }
}
