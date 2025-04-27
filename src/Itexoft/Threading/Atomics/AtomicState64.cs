// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Atomics;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct AtomicState64<T>() where T : unmanaged
{
    private ulong value;

    static AtomicState64()
    {
        if (Unsafe.SizeOf<T>() != sizeof(ulong))
            throw new InvalidOperationException($"{typeof(T)} must occupy exactly 8 bytes.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ref ulong Ref(ref AtomicState64<T> state) => ref state.value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static T Decode(ulong value) => Unsafe.As<ulong, T>(ref value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong Encode(T value) => Unsafe.As<T, ulong>(ref value);
}

public static class AtomicState64Extensions
{
    extension<T>(ref AtomicState64<T> state) where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Read() => AtomicState64<T>.Decode(Atomic.Read(ref AtomicState64<T>.Ref(ref state)));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(T value) => Atomic.Write(ref AtomicState64<T>.Ref(ref state), AtomicState64<T>.Encode(value));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Exchange(T value)
        {
            var raw = Interlocked.Exchange(ref AtomicState64<T>.Ref(ref state), AtomicState64<T>.Encode(value));

            return AtomicState64<T>.Decode(raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T CompareExchange(T value, T comparand)
        {
            var raw = Interlocked.CompareExchange(
                ref AtomicState64<T>.Ref(ref state),
                AtomicState64<T>.Encode(value),
                AtomicState64<T>.Encode(comparand));

            return AtomicState64<T>.Decode(raw);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryWrite(T value, T comparand)
        {
            var expected = AtomicState64<T>.Encode(comparand);

            return Interlocked.CompareExchange(ref AtomicState64<T>.Ref(ref state), AtomicState64<T>.Encode(value), expected) == expected;
        }
    }
}
