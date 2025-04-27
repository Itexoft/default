// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Core;

public static class Maths
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DivCeil(ulong a, ulong b)
    {
        var q = a / b;
        var r = a - q * b;

        return q + ((r | (0ul - r)) >> 63);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static uint DivCeil(uint a, uint b)
    {
        var q = a / b;
        var r = a - q * b;

        return q + ((r | (0u - r)) >> 31);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DivCeil(int a, int b)
    {
        var ua = (uint)a;
        var ub = (uint)b;
        var q = ua / ub;
        var r = ua - q * ub;

        return (int)(q + ((r | (0u - r)) >> 31));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static long DivCeil(long a, long b)
    {
        var ua = (ulong)a;
        var ub = (ulong)b;
        var q = ua / ub;
        var r = ua - q * ub;

        return (long)(q + ((r | (0ul - r)) >> 63));
    }
}
