// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Threading.Atomics.Arrays;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct AtomicArray6<T>() : IAtomicArray<T>
{
    public const int BitSize = 6;
    private const uint lockMask = (1U << BitSize) - 1;
    private const uint valueMask = lockMask << BitSize;
    private InternalArray array = new();
    private AtomicLane32 state = new();

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => BitOperations.PopCount((this.State >> BitSize) & lockMask);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint LockMask(in byte index) => 1U << index.RequiredAsLength(BitSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint ValueMask(in byte index) => 1U << (index.RequiredAsLength(BitSize) + BitSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte LockBit(in byte index) => index.RequiredAsLength(BitSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte ValueBit(in byte index) => (byte)(index.RequiredAsLength(BitSize) + BitSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint SlotMask(in byte index) => LockMask(index) | ValueMask(index);

    private uint State
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref Unsafe.As<AtomicLane32, uint>(ref this.state));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryUpdateSlot(in byte index, in uint from, in uint to)
    {
        var slot = SlotMask(index);
        ref var state = ref Unsafe.As<AtomicLane32, uint>(ref this.state);

        for (;;)
        {
            var original = Volatile.Read(ref state);

            if ((original & slot) != from)
                return false;

            if (Interlocked.CompareExchange(ref state, (original & ~slot) | to, original) == original)
                return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSet(in byte index) => (this.State & ValueMask(index)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool IAtomicArray.TrySet(in byte index) => this.state.TrySetBit(ValueBit(index));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IAtomicArray.SetAll() => _ = this.state.TrySetMask(valueMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void IAtomicArray.ClearAll() => _ = this.state.TryClearMask(valueMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool IAtomicArray.TryClear(in byte index) => this.state.TryClearBit(ValueBit(index));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnter(in byte index) => this.state.TrySetBit(LockBit(index));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit(in byte index) => this.state.TryClearBit(LockBit(index));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter(in byte index)
    {
        if (this.state.TrySetBit(LockBit(index)))
            return;

        var bit = LockMask(index);

        for (var i = 0;;)
        {
            while ((this.State & bit) != 0)
                Spin.Wait(ref i);

            if (this.state.TrySetBit(LockBit(index)))
                return;
        }
    }

    public void EnterAll()
    {
        if (this.state.TryUpdateFlags(lockMask, 0U, 0U, lockMask))
            return;

        for (var i = 0;;)
        {
            while ((this.State & lockMask) != 0)
                Spin.Wait(ref i);

            if (this.state.TryUpdateFlags(lockMask, 0U, 0U, lockMask))
                return;
        }
    }

    public void ExitAll() => _ = this.state.TryClearMask(lockMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetPublishedValue(in byte index, out T value)
    {
        var published = ValueMask(index);
        var slot = SlotMask(index);
        ref var state = ref Unsafe.As<AtomicLane32, uint>(ref this.state);
        var snapshot = Volatile.Read(ref state) & slot;

        if (snapshot == 0)
        {
            value = default!;

            return false;
        }

        if (snapshot == published)
        {
            var current = this.array[index];

            if ((Volatile.Read(ref state) & slot) == published)
            {
                var stable = this.array[index];

                if (stable is not null && ReferenceEquals(current, stable))
                {
                    value = stable;

                    return true;
                }
            }
        }

        for (var i = 0;;)
        {
            if (this.TryEnterPublished(index))
                break;

            snapshot = Volatile.Read(ref state) & slot;

            if (snapshot == 0)
            {
                value = default!;

                return false;
            }

            Spin.Wait(ref i);
        }

        try
        {
            value = this.array[index];

            if (value is not null)
                return true;
        }
        finally
        {
            this.RestorePublished(index);
        }

        throw new InvalidOperationException("AtomicArray6 published read invariant violated.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnterEmpty(in byte index) => this.TryUpdateSlot(index, 0U, LockMask(index));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnterPublished(in byte index) => this.TryUpdateSlot(index, ValueMask(index), SlotMask(index));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void RestorePublished(in byte index)
    {
        if (this.TryUpdateSlot(index, SlotMask(index), ValueMask(index)))
            return;

        throw new InvalidOperationException("AtomicArray6 restore invariant violated.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Unpublish(in byte index)
    {
        if (this.TryUpdateSlot(index, SlotMask(index), LockMask(index)))
            return;

        throw new InvalidOperationException("AtomicArray6 unpublish invariant violated.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void PublishAndExit(in byte index, in T value)
    {
        this.array[index] = value;

        if (this.TryUpdateSlot(index, LockMask(index), ValueMask(index)))
            return;

        this.array[index] = default!;
        this.Exit(index);
        throw new InvalidOperationException("AtomicArray6 publication invariant violated.");
    }

    [InlineArray(BitSize)]
    private struct InternalArray
    {
        private T value0;
    }

    public static ref T Ref<TAtomicArray>(ref TAtomicArray array, byte index) where TAtomicArray : struct, IAtomicArray<T>, allows ref struct =>
        ref Unsafe.As<TAtomicArray, AtomicArray6<T>>(ref array).array[index];

    public IEnumerator<T> GetEnumerator()
    {
        this.EnterAll();

        try
        {
            for (byte i = 0; i < BitSize; i++)
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
