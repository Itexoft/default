// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Sockets;
using Itexoft.Net.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Proxies;

public sealed class NetAutoProxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    private static readonly TimeSpan probeTimeout = TimeSpan.FromSeconds(1);

    public NetAutoProxy(NetEndpoint endpoint, string? username = null, string? password = null) : this(endpoint, new(username, password)) { }

    public NetAutoProxy(NetDnsHost host, NetPort port, NetCredential credentials = default) : this(new(host, port), credentials) { }
    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public async StackTask<TStream> ConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken = default)
        where TStream : class, INetStream
    {
        var socks5 = new NetSocks5Proxy(this.Endpoint, this.Credential);

        if (await tryProbeAsync(socks5, endpoint, stream, cancelToken) is { } socks5Stream)
            return socks5Stream;

        var https = new NetHttpsProxy(this.Endpoint, this.Credential);

        if (await tryProbeAsync(https, endpoint, stream, cancelToken) is { } httpsStream)
            return httpsStream;

        var http = new NetHttpProxy(this.Endpoint, this.Credential);

        if (await tryProbeAsync(http, endpoint, stream, cancelToken) is { } httpStream)
            return httpStream;

        var socks4 = new NetSocks4Proxy(this.Endpoint, this.Credential);

        if (await tryProbeAsync(socks4, endpoint, stream, cancelToken) is { } socks4Stream)
            return socks4Stream;

        Exception? last = null;

        if (last is not null)
            throw last;

        throw new SocketException((int)SocketError.ProtocolNotSupported);

        async StackTask<TStream?> tryProbeAsync<TProxy>(TProxy proxy, NetEndpoint target, TStream netStream, CancelToken token)
            where TProxy : INetProxy
        {
            token.ThrowIf();
            var probeToken = token.Branch(probeTimeout);

            try
            {
                var result = await proxy.ConnectAsync(target, netStream, probeToken);

                return result;
            }
            catch (Exception ex)
            {
                last = ex;

                if (stream is INetLazyStream lazyStream)
                    await lazyStream.DisconnectAsync(token);

                token.ThrowIf();

                return null;
            }
        }
    }
}
