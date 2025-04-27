// Copyright Aspose (c) Denis Kudelin

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Threading.Atomics;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct AtomicLane32() : IAtomicLane<uint>
{
    public const int Dim = 5;
    public const int BitSize = 1 << Dim;
    public const int Size = BitSize / 8;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Mask(in byte index) => 1U << index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetBit(in byte index)
    {
        var bit = Mask(index);

        return (Interlocked.Or(ref Unsafe.As<AtomicLane32, uint>(ref this), bit) & bit) == 0;
    }

    public byte Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (byte)BitOperations.PopCount(Volatile.Read(ref Unsafe.As<AtomicLane32, uint>(ref this)));
    }

    public bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref Unsafe.As<AtomicLane32, uint>(ref this)) == 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetAllBits() => Interlocked.Exchange(ref Unsafe.As<AtomicLane32, uint>(ref this), uint.MaxValue);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearAllBits() => Interlocked.Exchange(ref Unsafe.As<AtomicLane32, uint>(ref this), 0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearAllBits() => Interlocked.CompareExchange(ref Unsafe.As<AtomicLane32, uint>(ref this), 0, uint.MaxValue) == uint.MaxValue;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetAllBits() => Interlocked.CompareExchange(ref Unsafe.As<AtomicLane32, uint>(ref this), uint.MaxValue, 0) == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearBit(in byte index)
    {
        var bit = Mask(index);

        return (Interlocked.And(ref Unsafe.As<AtomicLane32, uint>(ref this), ~bit) & bit) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetFlag<T>(T @enum) where T : unmanaged, Enum => this.TrySetMask(in Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearFlag<T>(T @enum) where T : unmanaged, Enum => this.TryClearMask(in Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetMask<T>(T @enum) where T : unmanaged, Enum => this.TrySetMask(in Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearMask<T>(T @enum) where T : unmanaged, Enum => this.TryClearMask(in Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquireMask<T>(ref T @enum) where T : unmanaged, Enum => this.TryAcquireMask(ref Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReleaseMask<T>(ref T @enum) where T : unmanaged, Enum => this.TryReleaseMask(ref Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquireMask<T>(T @enum) where T : unmanaged, Enum => this.TryAcquireMask(ref Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReleaseMask<T>(T @enum) where T : unmanaged, Enum => this.TryReleaseMask(ref Unsafe.As<T, uint>(ref @enum));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasFlags<T>(T @enum) where T : unmanaged, Enum
    {
        var value = Unsafe.As<T, uint>(ref @enum);

        return (Volatile.Read(ref Unsafe.As<AtomicLane32, uint>(ref this)) & value) == value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WaitFlags<T>(T @enum) where T : unmanaged, Enum
    {
        var value = Unsafe.As<T, uint>(ref @enum);

        for (var i = 0; (Volatile.Read(ref Unsafe.As<AtomicLane32, uint>(ref this)) & value) != value;)
            Spin.Wait(ref i);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetFlags<T>() where T : unmanaged, Enum
    {
        var value = Volatile.Read(ref Unsafe.As<AtomicLane32, uint>(ref this));

        return Unsafe.As<uint, T>(ref value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBitSet(in byte index) => (Volatile.Read(ref Unsafe.As<AtomicLane32, uint>(ref this)) & Mask(index)) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetMask(in uint mask) => (Interlocked.Or(ref Unsafe.As<AtomicLane32, uint>(ref this), mask) & mask) != mask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearMask(in uint mask) => (Interlocked.And(ref Unsafe.As<AtomicLane32, uint>(ref this), ~mask) & mask) != 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryAcquireMask(ref uint mask)
    {
        ref var location = ref Unsafe.As<AtomicLane32, uint>(ref this);
        var original = Volatile.Read(ref location);

        if ((original & mask) != 0)
            return false;

        return Interlocked.CompareExchange(ref location, mask = original | mask, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryReleaseMask(ref uint mask)
    {
        ref var location = ref Unsafe.As<AtomicLane32, uint>(ref this);
        var original = Volatile.Read(ref location);

        if ((original & mask) != mask)
            return false;

        return Interlocked.CompareExchange(ref location, mask = original & ~mask, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryUpdateFlags(in uint setMask, in uint clearMask, in uint requireSet = 0, in uint requireClear = 0)
    {
        ref var location = ref Unsafe.As<AtomicLane32, uint>(ref this);
        var original = Volatile.Read(ref location);

        if ((original & requireSet) != requireSet || (original & requireClear) != 0)
            return false;

        return Interlocked.CompareExchange(ref location, (original | setMask) & ~clearMask, original) == original;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryUpdateFlags<T>(T setMask, T clearMask, T requireSet = default, T requireClear = default) where T : unmanaged, Enum
    {
        return this.TryUpdateFlags(
            Unsafe.As<T, uint>(ref setMask),
            Unsafe.As<T, uint>(ref clearMask),
            Unsafe.As<T, uint>(ref requireSet),
            Unsafe.As<T, uint>(ref requireClear));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Arrive(in byte index, in uint fullMask)
    {
        var bit = Mask(index);

        return (Interlocked.Or(ref Unsafe.As<AtomicLane32, uint>(ref this), bit) | bit) == fullMask;
    }
}
