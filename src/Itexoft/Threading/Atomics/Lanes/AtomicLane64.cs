// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Threading.Atomics;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct AtomicLane64() : IAtomicLane<ulong>
{
    public const int Dim = 6;
    public const int BitSize = 1 << Dim;
    public const int Size = BitSize / 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Mask(in byte index) => 1UL << index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetBit(in byte index)
    {
        var bit = Mask(index);

        return (Interlocked.Or(ref Unsafe.As<AtomicLane64, ulong>(ref this), bit) & bit) == 0;
    }

    public byte Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)BitOperations.PopCount(Volatile.Read(ref Unsafe.As<AtomicLane64, ulong>(ref this)));
    }

    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref Unsafe.As<AtomicLane64, ulong>(ref this)) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAllBits() => Interlocked.Exchange(ref Unsafe.As<AtomicLane64, ulong>(ref this), ulong.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearAllBits() => Interlocked.Exchange(ref Unsafe.As<AtomicLane64, ulong>(ref this), 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearAllBits() => Interlocked.CompareExchange(ref Unsafe.As<AtomicLane64, ulong>(ref this), 0, ulong.MaxValue) == ulong.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetAllBits() => Interlocked.CompareExchange(ref Unsafe.As<AtomicLane64, ulong>(ref this), ulong.MaxValue, 0) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearBit(in byte index)
    {
        var bit = Mask(index);

        return (Interlocked.And(ref Unsafe.As<AtomicLane64, ulong>(ref this), ~bit) & bit) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetMask<T>(T @enum) where T : unmanaged, Enum => this.TrySetMask(Unsafe.As<T, ulong>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearMask<T>(T @enum) where T : unmanaged, Enum => this.TryClearMask(Unsafe.As<T, ulong>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquireMask<T>(T @enum) where T : unmanaged, Enum => this.TryAcquireMask(ref Unsafe.As<T, ulong>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReleaseMask<T>(T @enum) where T : unmanaged, Enum => this.TryReleaseMask(ref Unsafe.As<T, ulong>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBitSet(in byte index) => (Volatile.Read(ref Unsafe.As<AtomicLane64, ulong>(ref this)) & Mask(index)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetMask(in ulong mask) => (Interlocked.Or(ref Unsafe.As<AtomicLane64, ulong>(ref this), mask) & mask) != mask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearMask(in ulong mask) => (Interlocked.And(ref Unsafe.As<AtomicLane64, ulong>(ref this), ~mask) & mask) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquireMask(ref ulong mask)
    {
        ref var location = ref Unsafe.As<AtomicLane64, ulong>(ref this);
        var original = Volatile.Read(ref location);

        if ((original & mask) != 0)
            return false;

        return Interlocked.CompareExchange(ref location, mask = original | mask, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReleaseMask(ref ulong mask)
    {
        ref var location = ref Unsafe.As<AtomicLane64, ulong>(ref this);
        var original = Volatile.Read(ref location);

        if ((original & mask) != mask)
            return false;

        return Interlocked.CompareExchange(ref location, mask = original & ~mask, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryUpdateFlags(in ulong setMask, in ulong clearMask, in ulong requireSet = 0, in ulong requireClear = 0)
    {
        ref var location = ref Unsafe.As<AtomicLane64, ulong>(ref this);
        var original = Volatile.Read(ref location);

        if ((original & requireSet) != requireSet || (original & requireClear) != 0)
            return false;

        return Interlocked.CompareExchange(ref location, (original | setMask) & ~clearMask, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryUpdateFlags<T>(T setMask, T clearMask, T requireSet = default, T requireClear = default) where T : unmanaged, Enum =>
        this.TryUpdateFlags(
            Unsafe.As<T, ulong>(ref setMask),
            Unsafe.As<T, ulong>(ref clearMask),
            Unsafe.As<T, ulong>(ref requireSet),
            Unsafe.As<T, ulong>(ref requireClear));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Arrive(in byte index, in ulong fullMask)
    {
        var bit = Mask(index);

        return (Interlocked.Or(ref Unsafe.As<AtomicLane64, ulong>(ref this), bit) | bit) == fullMask;
    }
}
