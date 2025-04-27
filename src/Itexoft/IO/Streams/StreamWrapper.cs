// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;

namespace Itexoft.IO;

public abstract partial class StreamWrapper : IStream<byte>
{
    private readonly bool ownStream;
    private Disposed disposed;
    private Deferred<System.IO.Stream> wrapper;

    private protected StreamWrapper(System.IO.Stream stream, bool ownStream)
    {
        this.ownStream = ownStream;
        this.wrapper = new(() => stream);
    }

    private protected System.IO.Stream BclStream => this.wrapper.Value;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        if (this.ownStream && this.wrapper.TryGetValueIfCreated(out var stream) && stream is not null)
            stream.Dispose();
    }

    protected void ThrowIfDisposed() => this.disposed.ThrowIf();

    public static implicit operator System.IO.Stream(StreamWrapper stream) => stream.wrapper.Value;
}
