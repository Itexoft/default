// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.Net.Core;

public sealed class NetLazyConnector(Func<CancelToken, INetStream> connector)
{
    private readonly Func<CancelToken, INetStream> connector = connector.Required();
    private AtomicLock atomicLock = new();
    private Disposed disposed = new();
    private INetStream? inner;

    public INetStream Connect(CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();

        using (this.atomicLock.Enter())
        {
            if (this.inner is INetStream stream)
                return stream;

            stream = this.connector(cancelToken.ThrowIf());
            this.inner = stream;

            return stream;
        }
    }

    public void Disconnect()
    {
        this.disposed.ThrowIf();

        using (this.atomicLock.Enter())
        {
            var stream = this.inner;
            this.inner = null;

            if (stream is not null)
                stream.Dispose();
        }
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        var stream = this.inner;
        this.inner = null;

        if (stream is not null)
            stream.Dispose();
    }
}
