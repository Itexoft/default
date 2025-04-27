// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Atomics;

public interface IAtomicLane<T> where T : unmanaged
{
    byte Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    bool IsEmpty
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TrySetBit(in byte index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void SetAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ClearAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryClearAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TrySetAllBits();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryClearBit(in byte index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool IsBitSet(in byte index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TrySetMask(in T mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryClearMask(in T mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryAcquireMask(ref T mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TryReleaseMask(ref T mask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool Arrive(in byte index, in T fullMask);
}
