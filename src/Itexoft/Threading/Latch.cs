// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly record struct Latch()
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe implicit operator bool(in Latch latch)
    {
        fixed (Latch* l = &latch)
            return Volatile.Read(ref ((byte*)l)[0]) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => ((bool)this).ToString();
}

public static class LatchExtensions
{
    extension(ref Latch latch)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool Try()
        {
            fixed (Latch* l = &latch)
                return Interlocked.Exchange(ref ((byte*)l)[0], 1) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool Reset()
        {
            fixed (Latch* l = &latch)
                return Interlocked.Exchange(ref ((byte*)l)[0], 0) > 0;
        }
    }
}
