// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Sockets;
using Itexoft.Extensions;
using Itexoft.Net.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Proxies;

public interface INetProxy
{
    NetEndpoint Endpoint { get; }
    NetCredential Credential { get; }

    StackTask<TStream> ConnectAsync<TStream>(NetDnsHost host, NetPort port, TStream stream, CancelToken cancelToken = default)
        where TStream : class, INetStream =>
        this.ConnectAsync(new(host, port), stream, cancelToken);

    StackTask<TStream> ConnectAsync<TStream>(NetEndpoint endpoint, TStream stream, CancelToken cancelToken = default)
        where TStream : class, INetStream;

    async StackTask<INetStream> ConnectAsync(INetConnector connector, NetEndpoint endpoint, CancelToken cancelToken = default)
    {
        var proxyEndpoint = await this.Endpoint.ResolveAsync(cancelToken);
        var handle = await connector.Required().ConnectAsync(proxyEndpoint, static () => default, cancelToken);

        if (handle is null)
            throw new SocketException((int)SocketError.NetworkUnreachable);

        try
        {
            return await this.ConnectAsync(endpoint, handle.Stream, cancelToken);
        }
        catch
        {
            await handle.DisposeAsync();

            throw;
        }
    }
}
