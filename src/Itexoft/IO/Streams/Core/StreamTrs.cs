// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.IO;

public struct StreamTrs<T>(ReadOnlyMemory<T> value) : IStreamRs<T> where T : unmanaged
{
    private ReadOnlyMemory<T> memory = value;
    private int position;

    public void Dispose() { }

    public readonly long Length => this.memory.Length;

    public long Position
    {
        readonly get => this.position;
        set => this.position = unchecked((int)value.RequiredInRange(0, this.memory.Length - 1));
    }

    public int Read(Span<T> buffer, CancelToken cancelToken = default)
    {
        if (this.position >= this.memory.Length || buffer.IsEmpty)
            return 0;

        var maxLength = Math.Min(this.memory.Length - this.position, buffer.Length);
        this.memory.Span.Slice(this.position, maxLength).CopyTo(buffer);
        this.position += maxLength;

        return maxLength;
    }

    public static implicit operator StreamTrs<T>(ReadOnlyMemory<T> value) => new(value);
    public static implicit operator StreamTrs<T>(ReadOnlySpan<T> value) => new(value.ToArray());
}
