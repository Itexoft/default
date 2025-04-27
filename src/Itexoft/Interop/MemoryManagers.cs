// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Interop;

public static unsafe class MemoryManagerExtensions
{
    extension<T>(ReadOnlySpan<T> buffer) where T : unmanaged
    {
        public Memory<T> AsMemory() => new MemoryConverter<T>(buffer).Memory;
    }

    private sealed class MemoryConverter<T>(ReadOnlySpan<T> span) : MemoryManager<T> where T : unmanaged
    {
        private readonly int length = span.Length;
        private readonly T* ptr = (T*)Unsafe.AsPointer(ref MemoryMarshal.GetReference(span));

        protected override void Dispose(bool disposing) { }
        public override Span<T> GetSpan() => new(this.ptr, this.length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(this.ptr + elementIndex);

        public override void Unpin() { }
    }
}
