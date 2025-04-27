// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Threading.Atomics;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Explicit, Size = Size)]
public struct AtomicRoll64()
{
    public const int Dim = 6;
    public const int Max = (1 << Dim) - Dim;
    public const int MetaMax = Max + 1;
    public const int Size = (Dim + Max) / sizeof(byte);
    public const int BitSize = Dim + Max;
    private const ulong step = 1UL << Max;
    private const ulong stepn = unchecked(0UL - step);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Convert(in ulong code) =>
        (byte)((code >> Max) ^ (((code >> Max) ^ (ulong)Max) & (ulong)-(long)(((code >> Max) + (ulong)Dim) >> Dim)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte Convert(in ulong code, out byte p)
    {
        var x = code >> Max;
        var q = (x + (ulong)Dim) >> Dim;
        var m = (ulong)-(long)q;
        p = (byte)((x - (ulong)Max) & m);

        return (byte)(x ^ ((x ^ (ulong)Max) & m));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Increment() => Convert((ulong)Interlocked.Add(ref Unsafe.As<AtomicRoll64, long>(ref this), unchecked((long)step)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Increment(out byte p) => Convert((ulong)Interlocked.Add(ref Unsafe.As<AtomicRoll64, long>(ref this), unchecked((long)step)), out p);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Decrement() => Convert((ulong)Interlocked.Add(ref Unsafe.As<AtomicRoll64, long>(ref this), unchecked((long)stepn)));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte Decrement(out byte p) => Convert((ulong)Interlocked.Add(ref Unsafe.As<AtomicRoll64, long>(ref this), unchecked((long)stepn)), out p);
}
