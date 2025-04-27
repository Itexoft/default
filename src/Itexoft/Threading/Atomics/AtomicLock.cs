// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Core;

namespace Itexoft.Threading.Atomics;

[StructLayout(LayoutKind.Explicit, Size = 1, Pack = 0)]
public struct AtomicLock : IDisposable
{
    public AtomicLock(bool isLocked)
    {
        if (isLocked)
            this.Enter();
    }

    public void Dispose() => this.Exit();

    public override string ToString() => (Unsafe.As<AtomicLock, byte>(ref this) != 0).ToString();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe void WaitZeroLock<T>(ref T state) where T : unmanaged
    {
        ref var firstByte = ref Unsafe.AsRef<byte>(Unsafe.AsPointer(ref state));

        for (var i = 1;;)
        {
            while (firstByte != 0)
                Spin.Wait(ref i);

            if (Interlocked.CompareExchange(ref firstByte, 1, 0) == 0)
                return;

            Spin.Wait(ref i);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WaitMaskZeroLock(ref int state, in int mask)
    {
        for (var i = 1;;)
        {
            while (Mask(ref state, mask, out _) != 0)
                Spin.Wait(ref i);

            if (Interlocked.CompareExchange(ref state, Mask(ref state, in mask, out var oldState), oldState) == oldState)
                return;

            Spin.Wait(ref i);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Mask(ref int value, in int mask, out int oldState)
    {
        oldState = value;

        return (value | mask) & mask;
    }
}

public static class AtomicLockExtensions
{
    extension(ref AtomicLock lockObj)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RefDispose<AtomicLock> Enter()
        {
            AtomicLock.WaitZeroLock(ref lockObj);

            return new RefDispose<AtomicLock>(ref lockObj);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryEnter() => Interlocked.CompareExchange(ref Unsafe.As<AtomicLock, byte>(ref lockObj), 1, 0) == 0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Exit() => Interlocked.Exchange(ref Unsafe.As<AtomicLock, byte>(ref lockObj), 0);
    }
}
