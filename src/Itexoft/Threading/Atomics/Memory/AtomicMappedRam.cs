// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;

namespace Itexoft.Threading.Atomics.Memory;

public class AtomicMappedRam<T> : AtomicRam<T>
{
    public int AllocatedCount
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.CountDense();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Ref(nuint ptr) => ref this.RefDense(ptr);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRef(nuint ptr, out Ref<T> @ref) => this.TryRefDense(ptr, out @ref);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Alloc(nuint pointer) => this.AllocExact(pointer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Alloc(nuint pointer, in T value) => this.AllocExact(pointer, in value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Free(nuint ptr) => this.FreeDense(ptr);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Free(nuint ptr, out T value) => this.FreeDense(ptr, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal int Sum(RefCounter counter) => this.SumDense(counter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected override AtomicRam<T> CreateNext() => new AtomicMappedRam<T>();
}
