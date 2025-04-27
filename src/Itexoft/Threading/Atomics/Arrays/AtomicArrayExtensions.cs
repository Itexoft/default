// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;

namespace Itexoft.Threading.Atomics.Arrays;

public static class AtomicArrayExtensions
{
    extension<TAtomicArray, T>(ref TAtomicArray array) where TAtomicArray : struct, IAtomicArray, IAtomicArray<T>, allows ref struct
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSetRef(in byte index, out Ref<T> value)
        {
            if (!array.IsSet(index))
            {
                value = default!;

                return false;
            }

            value = new(ref TAtomicArray.Ref(ref array, index));

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGetSetValue(in byte index, out T value)
        {
            if (!array.IsSet(index))
            {
                value = default!;

                return false;
            }

            value = TAtomicArray.Ref(ref array, index);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool GetOrSetValue(in byte index, Func<T> factory, out T value)
        {
            if (array.IsSet(index))
            {
                value = TAtomicArray.Ref(ref array, index);

                return false;
            }

            array.Enter(index);

            if (array.IsSet(index))
            {
                value = TAtomicArray.Ref(ref array, index);
                array.Exit(index);

                return false;
            }

            var created = factory();
            TAtomicArray.Ref(ref array, index) = created;
            _ = array.TrySet(index);
            array.Exit(index);
            value = created;

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryClear(in byte index, out T value)
        {
            if (!array.IsSet(index))
            {
                value = default!;

                return false;
            }

            array.Enter(index);

            if (array.TryClear(index))
            {
                ref var item = ref TAtomicArray.Ref(ref array, index);
                value = item;
                item = default!;
                array.Exit(index);

                return true;
            }

            array.Exit(index);
            value = default!;

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryGet(byte index, out Ref<T> value)
        {
            if (!array.IsSet(index))
            {
                value = default!;

                return false;
            }

            array.Enter(index);

            if (!array.IsSet(index))
            {
                array.Exit(index);
                value = default!;

                return false;
            }

            value = new(ref TAtomicArray.Ref(ref array, index));
            array.Exit(index);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrySet(in byte index, in T value)
        {
            if (array.IsSet(index))
                return false;

            array.Enter(index);

            if (array.IsSet(index))
            {
                array.Exit(index);

                return false;
            }

            TAtomicArray.Ref(ref array, index) = value;
            _ = array.TrySet(index);
            array.Exit(index);

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T GetOrSet(in byte index, in T value)
        {
            array.Enter(index);

            ref var item = ref TAtomicArray.Ref(ref array, index);

            if (array.IsSet(index))
            {
                var result = item;
                array.Exit(index);

                return result;
            }

            item = value;
            _ = array.TrySet(index);
            array.Exit(index);

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T GetOrSet(in byte index, Func<T> factory)
        {
            array.Enter(index);

            ref var item = ref TAtomicArray.Ref(ref array, index);

            if (array.IsSet(index))
            {
                ref var result = ref item;
                array.Exit(index);

                return ref result!;
            }

            try
            {
                var value = factory();
                item = value;
                _ = array.TrySet(index);

                return ref item!;
            }
            finally
            {
                array.Exit(index);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Set(in byte index, in T value)
        {
            array.Enter(index);
            TAtomicArray.Ref(ref array, index) = value;
            _ = array.TrySet(index);
            array.Exit(index);

            return value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ClearAll()
        {
            array.EnterAll();
            array.ClearAll();

            for (byte index = 0; index < AtomicLane64.BitSize; index++)
                TAtomicArray.Ref(ref array, index) = default!;

            array.ExitAll();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetAll(Func<byte, T> factory)
        {
            array.EnterAll();
            array.SetAll();

            try
            {
                for (byte index = 0; index < AtomicLane64.BitSize; index++)
                    TAtomicArray.Ref(ref array, index) = factory(index);
            }
            finally
            {
                array.ExitAll();
            }
        }
    }
}
