// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.IO;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Http.Internal;

internal sealed class NetHttpStreamAdapter(Stream inner, bool leaveOpen) : StreamBase, IStreamRa
{
    private readonly Stream inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public async StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            return await this.inner.ReadAsync(buffer, token);
    }

    protected async override StackTask DisposeAny()
    {
        if (leaveOpen)
            return;

        if (this.inner is ITaskDisposable asyncDisposable)
            await asyncDisposable.DisposeAsync();
        else
            await this.inner.DisposeAsync();
    }
}
