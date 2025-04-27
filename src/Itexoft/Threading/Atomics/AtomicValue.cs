// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;

namespace Itexoft.Threading.Atomics;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct AtomicValue<T>()
{
    private T value = default!;
    private Latch latch = new();
    private Latch isSet = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool IsSet() => this.isSet;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TrySet() => this.isSet.Try();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryClear() => this.isSet.Reset();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Exit() => this.latch.Reset();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Enter()
    {
        for (var i = 1; !this.latch.Try();)
            Spin.Wait(ref i);
    }

    internal static ref T Ref(ref AtomicValue<T> array) => ref array.value;
}

internal static class AtomicValueExtensions
{
    extension<T>(ref AtomicValue<T> array)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryClear(out T value)
        {
            if (!array.IsSet())
            {
                value = default!;

                return false;
            }

            array.Enter();

            if (array.TryClear())
            {
                ref var item = ref AtomicValue<T>.Ref(ref array);
                value = item;
                item = default!;
                array.Exit();

                return true;
            }

            array.Exit();
            value = default!;

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(out Ref<T> value)
        {
            if (!array.IsSet())
            {
                value = default!;

                return false;
            }

            array.Enter();

            if (!array.IsSet())
            {
                array.Exit();
                value = default!;

                return false;
            }

            value = new(ref AtomicValue<T>.Ref(ref array));
            array.Exit();

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrySet(in T value)
        {
            if (array.IsSet())
                return false;

            array.Enter();

            if (array.TrySet())
            {
                AtomicValue<T>.Ref(ref array) = value;
                array.Exit();

                return true;
            }

            array.Exit();

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetOrSet(in T value)
        {
            array.Enter();

            ref var item = ref AtomicValue<T>.Ref(ref array);

            if (!array.TrySet())
            {
                var result = item;
                array.Exit();

                return result;
            }

            item = value;
            array.Exit();

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetOrSet(Func<T> factory)
        {
            array.Enter();

            ref var item = ref AtomicValue<T>.Ref(ref array);

            if (!array.TrySet())
            {
                var result = item;
                array.Exit();

                return result;
            }

            var success = false;

            try
            {
                var value = factory();
                success = true;

                return item = value;
            }
            finally
            {
                if (!success)
                    array.TryClear();

                array.Exit();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Set(in T value)
        {
            array.Enter();
            array.TrySet();
            AtomicValue<T>.Ref(ref array) = value;
            array.Exit();

            return value;
        }
    }
}
