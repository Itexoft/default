// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Runtime.CompilerServices;
using Itexoft.Extensions;

namespace Itexoft.Threading.Atomics.Arrays;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct AtomicArray64<T>() : IAtomicArray<T>
{
    private InternalArray array = new();
    private AtomicLane64 atomicLane64 = new();
    private AtomicLane64 isSet64 = new();

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.isSet64.Count;
    }

    public ulong Mask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref Unsafe.As<AtomicLane64, ulong>(ref this.isSet64));
    }

    private ulong Locks
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref Unsafe.As<AtomicLane64, ulong>(ref this.atomicLane64));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong LockMask(in byte index) => 1UL << index.RequiredAsLength(AtomicLane64.BitSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSet(in byte index) => this.isSet64.IsBitSet(index.RequiredAsLength(AtomicLane64.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySet(in byte index) => this.isSet64.TrySetBit(index.RequiredAsLength(AtomicLane64.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAll() => this.isSet64.SetAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearAll() => this.isSet64.ClearAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClear(in byte index) => this.isSet64.TryClearBit(index.RequiredAsLength(AtomicLane64.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnter(in byte index) => this.atomicLane64.TrySetBit(index.RequiredAsLength(AtomicLane64.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ulong TryEnter(in ulong mask)
    {
        if (mask == 0)
            return 0;

        ref var location = ref Unsafe.As<AtomicLane64, ulong>(ref this.atomicLane64);

        while (true)
        {
            var original = Volatile.Read(ref location);
            var free = mask & ~original;

            if (free == 0)
                return 0;

            if (Interlocked.CompareExchange(ref location, original | free, original) == original)
                return free;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit(in byte index) => this.atomicLane64.TryClearBit(index.RequiredAsLength(AtomicLane64.BitSize));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit(in ulong mask)
    {
        if (mask != 0)
            _ = this.atomicLane64.TryClearMask(mask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter(in byte index)
    {
        index.RequiredAsLength(AtomicLane64.BitSize);

        if (this.atomicLane64.TrySetBit(index))
            return;

        var bit = LockMask(index);

        for (var i = 0;;)
        {
            while ((this.Locks & bit) != 0)
                Spin.Wait(ref i);

            if (this.atomicLane64.TrySetBit(index))
                return;
        }
    }

    public void EnterAll()
    {
        if (this.atomicLane64.TrySetAllBits())
            return;

        for (var i = 0;;)
        {
            while (!this.atomicLane64.IsEmpty)
                Spin.Wait(ref i);

            if (this.atomicLane64.TrySetAllBits())
                return;
        }
    }

    public void ExitAll() => this.atomicLane64.ClearAllBits();

    [InlineArray(AtomicLane64.BitSize)]
    private struct InternalArray
    {
        private T value0;
    }

    public static ref T Ref<TAtomicArray>(ref TAtomicArray array, byte index) where TAtomicArray : struct, IAtomicArray<T>, allows ref struct =>
        ref Unsafe.As<TAtomicArray, AtomicArray64<T>>(ref array).array[index];

    public IEnumerator<T> GetEnumerator()
    {
        this.EnterAll();

        try
        {
            for (byte i = 0; i < AtomicLane64.BitSize; i++)
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
