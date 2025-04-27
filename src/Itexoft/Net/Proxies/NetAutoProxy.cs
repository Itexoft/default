// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Net.Core;
using Itexoft.Net.Sockets;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public sealed class NetAutoProxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    private static readonly TimeSpan probeTimeout = TimeSpan.FromSeconds(1);

    public NetAutoProxy(NetEndpoint endpoint, string? username = null, string? password = null) : this(endpoint, new(username, password)) { }

    public NetAutoProxy(NetHost host, NetPort port, NetCredential credentials = default) : this(new(host, port, NetProtocol.Tcp), credentials) { }

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public INetStream Connect(INetConnector connector, NetEndpoint endpoint, CancelToken cancelToken = default)
    {
        var stream = connector.Connect(this.Endpoint, cancelToken);

        try
        {
            var proxy = new NetSocks5Proxy(this.Endpoint, this.Credential);

            return proxy.Connect(stream, endpoint, cancelToken.Branch(probeTimeout));
        }
        catch
        {
            stream.Dispose();
            stream = connector.Connect(this.Endpoint, cancelToken);
        }

        try
        {
            var proxy = new NetHttpsProxy(this.Endpoint, this.Credential);

            return proxy.Connect(stream, endpoint, cancelToken.Branch(probeTimeout));
        }
        catch
        {
            stream.Dispose();
            stream = connector.Connect(this.Endpoint, cancelToken);
        }

        try
        {
            var proxy = new NetHttpsProxy(this.Endpoint, this.Credential);

            return proxy.Connect(stream, endpoint, cancelToken.Branch(probeTimeout));
        }
        catch
        {
            stream.Dispose();
            stream = connector.Connect(this.Endpoint, cancelToken);
        }

        try
        {
            var proxy = new NetHttpProxy(this.Endpoint, this.Credential);

            return proxy.Connect(stream, endpoint, cancelToken.Branch(probeTimeout));
        }
        catch
        {
            stream.Dispose();
            stream = connector.Connect(this.Endpoint, cancelToken);
        }

        try
        {
            var proxy = new NetSocks4Proxy(this.Endpoint, this.Credential);

            return proxy.Connect(stream, endpoint, cancelToken.Branch(probeTimeout));
        }
        catch
        {
            stream.Dispose();

            throw new NetSocketException(NetSocketError.ProtocolNotSupported);
        }
    }
}
