// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.UI.Web.EmbeddedWeb;

public sealed class EmbeddedWebHandle : IDisposable
{
    private readonly CancelToken cancelToken;
    private readonly NetHttpServer server;
    private readonly List<string> urls;
    private Disposed disposed = new();

    internal EmbeddedWebHandle(string bundleId, NetHttpServer server, CancelToken cancelToken, Promise completion)
    {
        this.BundleId = bundleId;
        this.server = server ?? throw new ArgumentNullException(nameof(server));
        this.cancelToken = cancelToken;
        this.Completion = completion;
        this.urls = [BuildUrl(server.Endpoint)];
    }

    public string BundleId { get; }

    public Promise Completion { get; }

    public ICollection<string> Urls => this.urls;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.cancelToken.Cancel();
        this.server.Dispose();
        this.Completion.GetAwaiter().GetResult();
    }

    public void WaitCompletion() => this.Completion.Wait();

    public void Stop(CancelToken cancelToken = default)
    {
        if (this.disposed)
            return;

        cancelToken.ThrowIf();
        this.cancelToken.Cancel();
        this.server.Dispose();
        cancelToken.ThrowIf();
    }

    private static string BuildUrl(NetIpEndpoint endpoint)
    {
        var host = endpoint.IpAddress.ToString();

        if (endpoint.IpAddress.AddressFamily == NetAddressFamily.InterNetworkV6)
            host = $"[{host}]";

        return $"http://{host}:{(int)endpoint.Port}";
    }
}
