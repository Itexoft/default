// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Atomics.Arrays;

namespace Itexoft.Threading.Atomics.Memory;

public abstract class AtomicRam<T>
{
    internal delegate int RefCounter(ref T value);

    private long live;
    private protected AtomicArray6<AtomicRam<T>> next;
    private protected AtomicRoll64 route = new();
    private protected AtomicArray64<T> slots = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected byte Increment(out byte portal) => this.route.Increment(out portal);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TrySetSlot(in byte index) => this.slots.TrySet(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TrySetSlot(in byte index, in T value) => this.slots.TrySet(index, in value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryGetSlot(byte index, out Ref<T> @ref) => this.slots.TryGet(index, out @ref);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryClearSlot(in byte index) => this.slots.TryClear(index);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryClearSlot(in byte index, out T value) => this.slots.TryClear(index, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void ReserveLive() => _ = Interlocked.Increment(ref this.live);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected void ReleaseLive() => _ = Interlocked.Decrement(ref this.live);

    private protected bool HasLiveValues
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Volatile.Read(ref this.live) != 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int CountDense()
    {
        var total = 0;
        AtomicRam<T> node = this;
        nuint route = 0;
        nuint scale = 1;

        while (true)
        {
            total += node.slots.Count;

            if (!this.TryMoveToLiveNode(ref route, ref scale, ref node))
                return total;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int SumDense(RefCounter counter)
    {
        var total = 0;
        AtomicRam<T> node = this;
        nuint route = 0;
        nuint scale = 1;

        while (true)
        {
            for (var mask = node.slots.Mask; mask != 0; mask &= mask - 1)
            {
                var index = (byte)BitOperations.TrailingZeroCount(mask);

                if (!node.TryGetSlot(index, out var slotRef))
                    continue;

                total += counter(ref slotRef.Value);
            }

            if (!this.TryMoveToLiveNode(ref route, ref scale, ref node))
                return total;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected AtomicRam<T> GetOrSetNext(in byte portal)
    {
        if (this.next.TryGetPublishedValue(portal, out var child))
            return child;

        for (var i = 0;;)
        {
            if (this.next.TryEnterEmpty(portal))
                break;

            if (this.next.TryGetPublishedValue(portal, out child))
                return child;

            Spin.Wait(ref i);
        }

        try
        {
            child = this.CreateNext();
            this.next.PublishAndExit(portal, child);

            return child;
        }
        catch
        {
            this.next.Exit(portal);
            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected AtomicRam<T> GetOrReserveNext(in byte portal)
    {
        for (;;)
        {
            var child = this.GetOrSetNext(portal);
            child.ReserveLive();

            if (this.TryGetNext(portal, out var current) && ReferenceEquals(current, child))
                return child;

            child.ReleaseLive();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryGetNext(in byte portal, out AtomicRam<T> child) => this.next.TryGetPublishedValue(portal, out child);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryGetLiveNext(in byte portal, out AtomicRam<T> child)
    {
        if (!this.TryGetNext(portal, out var next))
        {
            child = default!;

            return false;
        }

        child = next;

        if (child.HasLiveValues)
            return true;

        _ = this.TryUnlinkNext(portal, child);

        if (!this.TryGetNext(portal, out next))
        {
            child = default!;

            return false;
        }

        child = next;

        return child.HasLiveValues;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryUnlinkNext(in byte portal, AtomicRam<T> child)
    {
        if (child.HasLiveValues || !this.next.TryEnterPublished(portal))
            return false;

        ref var current = ref AtomicArray6<AtomicRam<T>>.Ref(ref this.next, portal);

        if (!ReferenceEquals(current, child) || child.HasLiveValues)
        {
            this.next.RestorePublished(portal);

            return false;
        }

        this.next.Unpublish(portal);
        current = default!;
        this.next.Exit(portal);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryMoveToLiveNode(ref nuint route, ref nuint scale, ref AtomicRam<T> node)
    {
        const nuint dim = (nuint)AtomicRoll64.Dim;

        for (byte portal = 0; portal < AtomicRoll64.Dim; portal++)
        {
            if (!node.TryGetLiveNext(portal, out var child))
                continue;

            route = route * dim + (nuint)portal;
            scale *= dim;
            node = child;

            return true;
        }

        while (scale != 1)
        {
            var parentScale = scale / dim;
            var fromPortal = (byte)(route % dim);
            route /= dim;
            scale = parentScale;

            if (!this.TryResolveNodeAtRoute(route, scale, out node))
                return false;

            for (var portal = (byte)(fromPortal + 1); portal < AtomicRoll64.Dim; portal++)
            {
                if (!node.TryGetLiveNext(portal, out var child))
                    continue;

                route = route * dim + (nuint)portal;
                scale *= dim;
                node = child;

                return true;
            }
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryResolveNodeAtRoute(nuint route, nuint scale, out AtomicRam<T> node)
    {
        const nuint dim = (nuint)AtomicRoll64.Dim;
        node = this;

        while (scale != 1)
        {
            scale /= dim;
            var portal = (byte)(route / scale);
            route -= (nuint)portal * scale;

            if (!node.TryGetNext(portal, out var child))
            {
                node = default!;

                return false;
            }

            node = child;
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected abstract AtomicRam<T> CreateNext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected nuint AllocDense()
    {
        nuint a = 0;
        nuint m = 1;

        const nuint dim = (nuint)AtomicRoll64.Dim;
        const nuint max = (nuint)AtomicRoll64.Max;
        for (var node = this;;)
        {
            var index = node.Increment(out var portal);

            if (index != AtomicRoll64.Max)
            {
                if (node.TrySetSlot(index))
                    return a + m * (nuint)index;

                portal = (byte)((nuint)index % dim);
            }

            node = node.GetOrReserveNext(portal);
            a += m * (max + (nuint)portal);
            m *= dim;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected nuint AllocDense(in T value)
    {
        nuint a = 0;
        nuint m = 1;

        const nuint dim = (nuint)AtomicRoll64.Dim;
        const nuint max = (nuint)AtomicRoll64.Max;
        for (var node = this;;)
        {
            var index = node.Increment(out var portal);

            if (index != AtomicRoll64.Max)
            {
                if (node.TrySetSlot(index, in value))
                    return a + m * (nuint)index;

                portal = (byte)((nuint)index % dim);
            }

            node = node.GetOrReserveNext(portal);
            a += m * (max + (nuint)portal);
            m *= dim;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool AllocExact(nuint ptr) => this.TryAllocExact(ptr);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool AllocExact(nuint ptr, in T value) => this.TryAllocExact(ptr, in value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAllocExact(nuint ptr)
    {
        if (!Decode(ptr, out var portal, out var next, out var leaf))
            return this.TrySetSlot(leaf);

        var child = this.GetOrReserveNext(portal);

        if (child.TryAllocExact(next))
            return true;

        child.ReleaseLive();

        if (!child.HasLiveValues)
            _ = this.TryUnlinkNext(portal, child);

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAllocExact(nuint ptr, in T value)
    {
        if (!Decode(ptr, out var portal, out var next, out var leaf))
            return this.TrySetSlot(leaf, in value);

        var child = this.GetOrReserveNext(portal);

        if (child.TryAllocExact(next, in value))
            return true;

        child.ReleaseLive();

        if (!child.HasLiveValues)
            _ = this.TryUnlinkNext(portal, child);

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected ref T RefDense(nuint ptr)
    {
        for (var node = this;;)
        {
            if (Decode(ptr, out var portal, out var next, out var leaf))
            {
                if (!node.TryGetNext(portal, out var child))
                    break;

                node = child;
                ptr = next;
            }
            else
            {
                if (node.TryGetSlot(leaf, out var @ref))
                    return ref @ref.Value;

                break;
            }
        }

        return ref Unsafe.NullRef<T>();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool TryRefDense(nuint ptr, out Ref<T> @ref)
    {
        for (var node = this;;)
        {
            if (!Decode(ptr, out var portal, out var next, out var leaf))
                return node.TryGetSlot(leaf, out @ref);

            if (node.TryGetNext(portal, out var child))
            {
                node = child;
                ptr = next;
            }
            else
            {
                @ref = default;

                return false;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool FreeDense(nuint ptr) => this.TryFreeDense(ptr, trackLive: false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected bool FreeDense(nuint ptr, out T value) => this.TryFreeDense(ptr, trackLive: false, out value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryFreeDense(nuint ptr, bool trackLive)
    {
        if (!Decode(ptr, out var portal, out var next, out var leaf))
        {
            if (!this.TryClearSlot(leaf))
                return false;

            if (trackLive)
                this.ReleaseLive();

            return true;
        }

        if (!this.TryGetLiveNext(portal, out var child) || !child.TryFreeDense(next, trackLive: true))
            return false;

        if (!child.HasLiveValues)
            _ = this.TryUnlinkNext(portal, child);

        if (trackLive)
            this.ReleaseLive();

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryFreeDense(nuint ptr, bool trackLive, out T value)
    {
        if (!Decode(ptr, out var portal, out var next, out var leaf))
        {
            if (!this.TryClearSlot(leaf, out value))
                return false;

            if (trackLive)
                this.ReleaseLive();

            return true;
        }

        if (!this.TryGetLiveNext(portal, out var child) || !child.TryFreeDense(next, trackLive: true, out value))
        {
            value = default!;

            return false;
        }

        if (!child.HasLiveValues)
            _ = this.TryUnlinkNext(portal, child);

        if (trackLive)
            this.ReleaseLive();

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected static bool Decode(nuint ptr, out byte portal, out nuint next, out byte leaf)
    {
        if (ptr < (nuint)AtomicRoll64.Max)
        {
            leaf = (byte)ptr;
            portal = 0;
            next = 0;

            return false;
        }

        var x = ptr - (nuint)AtomicRoll64.Max;
        next = x / (nuint)AtomicRoll64.Dim;
        portal = (byte)(x - next * (nuint)AtomicRoll64.Dim);
        leaf = 0;

        return true;
    }
}
