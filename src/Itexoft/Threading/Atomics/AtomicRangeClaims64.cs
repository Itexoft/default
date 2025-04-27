// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Threading.Atomics;

public struct AtomicRangeClaims64
{
    public const byte NoSlot = AtomicLane64.BitSize;
    private AtomicLane64 inUse;
    private AtomicLane64 active;
    private AtomicLane64 writers;
    private Starts starts;
    private Ends ends;

    public byte EnterShared(long start, long length)
    {
        for (var spin = 0;; Spin.Wait(ref spin))
        {
            if (this.TryEnter(start, length, false, out var slot))
                return slot;
        }
    }

    public byte EnterExclusive(long start, long length)
    {
        for (var spin = 0;; Spin.Wait(ref spin))
        {
            if (this.TryEnter(start, length, true, out var slot))
                return slot;
        }
    }

    private bool TryEnter(long start, long length, bool exclusive, out byte slot)
    {
        if (start < 0)
            throw new ArgumentOutOfRangeException(nameof(start));

        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length == 0)
        {
            slot = NoSlot;

            return true;
        }

        if (!this.TryAcquireSlot(out slot))
            return false;

        var end = checked(start + length);
        Volatile.Write(ref this.starts[slot], start);
        Volatile.Write(ref this.ends[slot], end);

        if (exclusive && !this.writers.TrySetBit(slot))
            throw new InvalidOperationException("Range writer claim slot publication failed.");

        if (!this.active.TrySetBit(slot))
            throw new InvalidOperationException("Range claim slot publication failed.");

        var activeMask = Volatile.Read(ref Unsafe.As<AtomicLane64, ulong>(ref this.active)) & ~(1UL << slot);
        var conflictMask = exclusive ? activeMask : activeMask & Volatile.Read(ref Unsafe.As<AtomicLane64, ulong>(ref this.writers));

        if (!this.HasOverlap(conflictMask, start, end))
            return true;

        _ = this.active.TryClearBit(slot);

        if (exclusive)
            _ = this.writers.TryClearBit(slot);

        _ = this.inUse.TryClearBit(slot);

        return false;
    }

    public void Exit(byte slot)
    {
        if (slot == NoSlot)
            return;

        _ = this.active.TryClearBit(slot);
        _ = this.writers.TryClearBit(slot);
        _ = this.inUse.TryClearBit(slot);
    }

    private bool TryAcquireSlot(out byte slot)
    {
        for (byte index = 0; index < AtomicLane64.BitSize; index++)
        {
            if (!this.inUse.TrySetBit(index))
                continue;

            slot = index;

            return true;
        }

        slot = 0;

        return false;
    }

    private bool HasOverlap(ulong mask, long start, long end)
    {
        while (mask != 0)
        {
            var index = BitOperations.TrailingZeroCount(mask);
            mask &= mask - 1;

            var otherStart = Volatile.Read(ref this.starts[(byte)index]);
            var otherEnd = Volatile.Read(ref this.ends[(byte)index]);

            if (start < otherEnd && otherStart < end)
                return true;
        }

        return false;
    }

    [InlineArray(AtomicLane64.BitSize), StructLayout(LayoutKind.Sequential)]
    private struct Starts
    {
        private long value0;
    }

    [InlineArray(AtomicLane64.BitSize), StructLayout(LayoutKind.Sequential)]
    private struct Ends
    {
        private long value0;
    }
}
