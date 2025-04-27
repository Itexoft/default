// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.IO;

public abstract partial class StreamWrapper : IStream
{
    private Disposed disposed;
    private Deferred<Stream> wrapper;

    private protected StreamWrapper(Stream stream) => this.wrapper = new(() => stream);

    private protected Stream BclStream => this.wrapper.Value;

    public async StackTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        GC.SuppressFinalize(this);

        if (this.wrapper.TryGetValueIfCreated(out var stream) && stream is not null)
            await stream.DisposeAsync();

        await this.DisposeAny();
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        GC.SuppressFinalize(this);

        var task = this.DisposeAny();

        if (this.wrapper.TryGetValueIfCreated(out var stream) && stream is not null)
            stream.Dispose();

        if (!task.IsCompletedSuccessfully)
            task.GetAwaiter().GetResult();
    }

    protected void ThrowIfDisposed() => this.disposed.ThrowIf();

    protected abstract StackTask DisposeAny();

    public static implicit operator Stream(StreamWrapper stream) => stream.wrapper.Value;
}
