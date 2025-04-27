// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Numerics;
using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;
using Itexoft.Threading.Atomics.Arrays;
using Itexoft.Threading.Atomics.Memory;

namespace Itexoft.Collections.Atomics;

public sealed class AtomicBag<T> : AtomicRam<T>, IEnumerable<T>
{
    private readonly IEqualityComparer<T> comparer;

    public AtomicBag(IEqualityComparer<T> comparer) => this.comparer = comparer.Required();

    public AtomicBag() => this.comparer = EqualityComparer<T>.Default;

    private ulong SlotsMask
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.slots.Mask;
    }

    public IEnumerator<T> GetEnumerator()
    {
        var node = this;
        nuint route = 0;
        nuint scale = 1;

        while (true)
        {
            for (var mask = node.SlotsMask; mask != 0; mask &= mask - 1)
            {
                var index = (byte)BitOperations.TrailingZeroCount(mask);

                if (node.TryReadValue(index, out var item))
                    yield return item;
            }

            if (!this.TryMoveToNextNode(ref route, ref scale, ref node))
                yield break;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T value) => _ = this.AllocDense(in value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(T value) => this.RemoveCore(value, false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool RemoveCore(T value, bool trackLive)
    {
        if (trackLive && !this.HasLiveValues)
            return false;

        if (this.TryRemoveLocal(value, out var busyMask))
        {
            if (trackLive)
                this.ReleaseLive();

            return true;
        }

        for (byte portal = 0; portal < AtomicRoll64.Dim; portal++)
        {
            if (!this.TryGetLiveNext(portal, out var child))
                continue;

            var bag = (AtomicBag<T>)child;

            if (!bag.RemoveCore(value, true))
                continue;

            if (!bag.HasLiveValues)
                _ = this.TryUnlinkNext(portal, bag);

            if (trackLive)
                this.ReleaseLive();

            return true;
        }

        if (!this.TryRemoveBusyLocal(value, busyMask))
            return false;

        if (trackLive)
            this.ReleaseLive();

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRemoveLocal(T value, out ulong busyMask)
    {
        var mask = this.SlotsMask;
        var locked = this.slots.TryEnter(mask);
        busyMask = mask & ~locked;

        return locked != 0 && this.TryRemoveLockedLocal(value, locked);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRemoveBusyLocal(T value, ulong busyMask)
    {
        for (var i = 0; (busyMask &= this.SlotsMask) != 0;)
        {
            var locked = this.slots.TryEnter(busyMask);

            if (locked == 0)
            {
                Spin.Wait(ref i);

                continue;
            }

            i = 0;
            busyMask &= ~locked;

            if (this.TryRemoveLockedLocal(value, locked))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected override AtomicRam<T> CreateNext() => new AtomicBag<T>(this.comparer);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryMoveToNextNode(ref nuint route, ref nuint scale, ref AtomicBag<T> node)
    {
        AtomicRam<T> current = node;

        if (!this.TryMoveToLiveNode(ref route, ref scale, ref current))
            return false;

        node = (AtomicBag<T>)current;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryReadValue(in byte index, out T value)
    {
        if (!this.slots.IsSet(index))
        {
            value = default!;

            return false;
        }

        this.slots.Enter(index);

        try
        {
            if (!this.slots.IsSet(index))
            {
                value = default!;

                return false;
            }

            value = AtomicArray64<T>.Ref(ref this.slots, index);

            return true;
        }
        finally
        {
            this.slots.Exit(index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRemoveLockedLocal(T value, ulong locked)
    {
        try
        {
            for (var mask = locked; mask != 0; mask &= mask - 1)
            {
                var index = (byte)BitOperations.TrailingZeroCount(mask);

                if (!this.slots.IsSet(index))
                    continue;

                ref var item = ref AtomicArray64<T>.Ref(ref this.slots, index);

                if (!this.comparer.Equals(item, value))
                    continue;

                _ = this.slots.TryClear(index);
                item = default!;

                return true;
            }

            return false;
        }
        finally
        {
            this.slots.Exit(locked);
        }
    }
}
