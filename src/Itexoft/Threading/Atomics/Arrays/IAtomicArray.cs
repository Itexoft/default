// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Atomics.Arrays;

public interface IAtomicArray
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Exit(in byte index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void Enter(in byte index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void EnterAll();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void ExitAll();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool IsSet(in byte index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    bool TrySet(in byte index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    void SetAll();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearAll();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClear(in byte index);
}

public interface IAtomicArray<T> : IAtomicArray, IReadOnlyCollection<T>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static abstract ref T Ref<TAtomicArray>(ref TAtomicArray array, byte index)
        where TAtomicArray : struct, IAtomicArray<T>, allows ref struct;
}
