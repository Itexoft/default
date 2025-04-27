// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Proxies;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Core;

public abstract class NetConnector(LString kind, params INetProxy[] proxies) : INetConnector
{
    public FileAccess Access { get; init; } = FileAccess.ReadWrite;
    public LString Kind { get; } = kind;

    public virtual async StackTask<INetConnectionHandle> ConnectAsync(NetEndpoint endpoint, Func<StackTask> dispose, CancelToken cancelToken)
    {
        if (proxies.Length == 0)
        {
            var ipEndpoint = await endpoint.ResolveAsync(cancelToken);
            var handle = await this.ConnectAsync(ipEndpoint, dispose, cancelToken);

            return new NetConnectionHandle(endpoint, handle.Stream);
        }

        var proxyEndpoint = await proxies[0].Endpoint.ResolveAsync(cancelToken);
        var proxyHandle = await this.ConnectAsync(proxyEndpoint, dispose, cancelToken);

        try
        {
            var stream = proxyHandle.Stream;

            for (var i = 0; i < proxies.Length; i++)
            {
                var target = i + 1 < proxies.Length ? proxies[i + 1].Endpoint : endpoint;
                stream = await proxies[i].ConnectAsync(target, stream, cancelToken);
            }

            return new NetConnectionHandle(endpoint, stream);
        }
        catch
        {
            await proxyHandle.DisposeAsync();

            throw;
        }
    }

    public abstract StackTask<INetConnectionHandle> ConnectAsync(NetIpEndpoint endpoint, Func<StackTask> dispose, CancelToken cancelToken);

    public StackTask<INetConnectionHandle> ConnectAsync(NetEndpoint endpoint, CancelToken cancelToken) =>
        this.ConnectAsync(endpoint, static () => default, cancelToken);
}
