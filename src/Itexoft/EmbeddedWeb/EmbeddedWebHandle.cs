// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;
using Microsoft.AspNetCore.Builder;

namespace Itexoft.EmbeddedWeb;

public sealed class EmbeddedWebHandle : ITaskDisposable, IDisposable
{
    private readonly WebApplication app;
    private Disposed disposed;

    internal EmbeddedWebHandle(string bundleId, WebApplication app, HeapTask completion)
    {
        this.BundleId = bundleId;
        this.app = app;
        this.Completion = completion;
    }

    public string BundleId { get; }

    public HeapTask Completion { get; }

    public ICollection<string> Urls => this.app.Urls;

    void IDisposable.Dispose() => this.DisposeAsync().GetAwaiter().GetResult();

    public async StackTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        try
        {
            await this.app.StopAsync().ConfigureAwait(false);
        }
        finally
        {
            await this.Completion;
            await this.app.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async StackTask StopAsync(CancelToken cancelToken = default)
    {
        if (this.disposed)
            return;

        using (cancelToken.Bridge(out var token))
            await this.app.StopAsync(token).ConfigureAwait(false);
    }
}
