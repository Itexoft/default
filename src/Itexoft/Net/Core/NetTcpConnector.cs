// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Net.Sockets;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public sealed class NetTcpConnector() : NetConnector(NetTransports.Tcp)
{
    public bool NoDelay { get; init; } = true;
    public int SendBufferSize { get; init; } = 8192;
    public int ReceiveBufferSize { get; init; } = 8192;
    public TimeSpan ReceiveTimeout { get; init; } = NetSocket.DefaultReceiveTimeout;
    public TimeSpan SendTimeout { get; init; } = NetSocket.DefaultSendTimeout;

    public override INetStream Connect(NetIpEndpoint endpoint, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        var socket = this.CreateSocket(endpoint.AddressFamily);
        socket.Connect(endpoint, cancelToken);
        var stream = new NetStream(socket, true);

        return stream;
    }

    private NetSocket CreateSocket(NetAddressFamily addressFamily)
    {
        var socketAddressFamily = addressFamily == NetAddressFamily.InterNetwork && NetSocket.OsSupportsIPv4 && NetSocket.OsSupportsIPv6
            ? NetAddressFamily.InterNetworkV6
            : addressFamily;

        var socket = new NetSocket(socketAddressFamily, NetSocketType.Stream, NetProtocol.Tcp);

        if (socketAddressFamily == NetAddressFamily.InterNetworkV6 && NetSocket.OsSupportsIPv4 && NetSocket.OsSupportsIPv6)
            socket.DualMode = true;

        socket.NoDelay = this.NoDelay;
        socket.SendBufferSize = this.SendBufferSize;
        socket.ReceiveBufferSize = this.ReceiveBufferSize;
        socket.ReceiveTimeout = this.ReceiveTimeout;
        socket.SendTimeout = this.SendTimeout;

        return socket;
    }
}
