// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Atomics.Native;

internal static class AtomicBitMemoryCommon
{
    public const int WordBits = AtomicLane64.BitSize;
    public const int LaneBits = AtomicLane64.Dim;
    public const ulong LaneMask = WordBits - 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong WordsForBits(ulong bits) => bits == 0 ? 0 : ((bits - 1) >> LaneBits) + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong WordIndex(ulong index) => index >> LaneBits;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int BitIndex(ulong index) => (int)(index & LaneMask);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong BitMask(ulong index) => 1UL << (int)(index & LaneMask);
}
