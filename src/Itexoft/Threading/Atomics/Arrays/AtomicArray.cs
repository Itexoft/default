// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading.Atomics.Bits;

namespace Itexoft.Threading.Atomics.Arrays;

public struct AtomicArray<T>
{
    private readonly T[] array;
    private AtomicBits atomic64;
    private readonly int length;

    public AtomicArray(int capacity)
    {
        this.length = capacity;
        this.array = new T[capacity];
        this.atomic64 = new AtomicBits(Maths.DivCeil(capacity, AtomicLane64.BitSize));
    }

    public int Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.length;
    }

    public AtomicArray() => this.array = null!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T Enter(int index)
    {
        for (var i = 1; !this.atomic64.TrySetBit(index);)
            Spin.Wait(ref i);

        return ref this.array[index];
    }

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref this.array[index];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnter(int index) => this.atomic64.TrySetBit(index.RequiredAsLength(AtomicLane64.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit(byte index) => this.atomic64.TrySetBit(index.RequiredAsLength(AtomicLane64.BitSize));
}
