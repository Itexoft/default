// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Atomics.Bits;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct AtomicBits(int count)
{
    private readonly AtomicLane64[] segments = new AtomicLane64[count];
    private readonly int count = count;

    public readonly int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.count;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySetBit(int index)
    {
        this.RequireBitIndex(index);

        return this.segments[index >> AtomicLane64.Dim].TrySetBit((byte)(index & (AtomicLane64.BitSize - 1)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClearBit(int index)
    {
        this.RequireBitIndex(index);

        return this.segments[index >> AtomicLane64.Dim].TryClearBit((byte)(index & (AtomicLane64.BitSize - 1)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsBitSet(int index)
    {
        this.RequireBitIndex(index);

        return this.segments[index >> AtomicLane64.Dim].IsBitSet((byte)(index & (AtomicLane64.BitSize - 1)));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void RequireBitIndex(int index)
    {
        if (index < 0 || (uint)index >= (uint)(this.count << AtomicLane64.Dim))
            throw new ArgumentOutOfRangeException(nameof(index), index, "Value is out of range.");
    }
}
