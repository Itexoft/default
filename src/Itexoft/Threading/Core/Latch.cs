// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Threading;

namespace Itexoft.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly record struct Latch()
{
    public Latch(bool state) : this()
    {
        if (state)
            Unsafe.As<Latch, byte>(ref this) = 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe implicit operator bool(in Latch latch)
    {
        fixed (Latch* l = &latch)
            return Atomic.Read(ref ((byte*)l)[0]) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override string ToString() => ((bool)this).ToString();
}

public static class LatchExtensions
{
    extension(ref Latch latch)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Try() => Interlocked.Exchange(ref Unsafe.As<Latch, byte>(ref latch), 1) == 0;
        
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Reset() => Interlocked.Exchange(ref Unsafe.As<Latch, byte>(ref latch), 0) > 0;

        public void Wait()
        {
            for(var i = 0; !latch;)
                Spin.Wait(ref i);
        }
    }
}
