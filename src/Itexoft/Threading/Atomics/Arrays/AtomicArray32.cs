// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Runtime.CompilerServices;
using Itexoft.Extensions;

namespace Itexoft.Threading.Atomics.Arrays;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct AtomicArray32<T>() : IAtomicArray<T>
{
    private InternalArray array = new();
    private AtomicLane32 atomic = new();
    private AtomicLane32 isSet = new();

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.isSet.Count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSet(in byte index) => this.isSet.IsBitSet(index.RequiredAsLength(AtomicLane32.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySet(in byte index) => this.isSet.TrySetBit(index.RequiredAsLength(AtomicLane32.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAll() => this.isSet.SetAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearAll() => this.isSet.ClearAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClear(in byte index) => this.isSet.TryClearBit(index.RequiredAsLength(AtomicLane32.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit(in byte index) => this.atomic.TryClearBit(index.RequiredAsLength(AtomicLane32.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter(in byte index)
    {
        index.RequiredAsLength(AtomicLane32.BitSize);

        for (var i = 1; !this.atomic.TrySetBit(index);)
            Spin.Wait(ref i);
    }

    public void EnterAll()
    {
        for (var i = 1; !this.atomic.TrySetAllBits();)
            Spin.Wait(ref i);
    }

    public void ExitAll() => this.atomic.ClearAllBits();

    [InlineArray(AtomicLane32.BitSize)]
    private struct InternalArray
    {
        private T value0;
    }

    static ref T IAtomicArray<T>.Ref<TAtomicArray>(ref TAtomicArray array, byte index) =>
        ref Unsafe.As<TAtomicArray, AtomicArray32<T>>(ref array).array[index];

    public IEnumerator<T> GetEnumerator()
    {
        this.EnterAll();

        try
        {
            for (byte i = 0; i < AtomicLane32.BitSize; i++)
            {
                if (!this.IsSet(i))
                    continue;

                yield return this.array[i];
            }
        }
        finally
        {
            this.ExitAll();
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
