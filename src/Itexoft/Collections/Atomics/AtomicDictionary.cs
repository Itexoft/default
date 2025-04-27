// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading.Atomics;
using Itexoft.Threading.Atomics.Memory;

namespace Itexoft.Collections.Atomics;

public sealed partial class AtomicDictionary<TKey, TValue> where TKey : notnull
{
    public delegate bool AtomicRemovePredicate(TKey key, TValue value);

    public delegate TValue AtomicUpdateFactory(TKey key, TValue oldValue);

    public delegate TValue AtomicValueFactory(TKey key);

    private static readonly object singleEntryMarker = new();
    private readonly IEqualityComparer<TKey> comparer;
    private AtomicMappedRam<Entry> memory = new();
    public AtomicDictionary() : this(null, null) { }
    public AtomicDictionary(IEqualityComparer<TKey> comparer) : this(null, comparer) { }
    public AtomicDictionary(IEnumerable<KeyValuePair<TKey, TValue>> initValues) : this(initValues, null) { }

    private AtomicDictionary(IEnumerable<KeyValuePair<TKey, TValue>>? initValues, IEqualityComparer<TKey>? comparer)
    {
        this.comparer = comparer ?? EqualityComparer<TKey>.Default;

        if (initValues is null)
            return;

        foreach (var (key, value) in initValues)
            _ = this.TryAddCore(in key, in value, null);
    }

    public bool TryGet(in TKey key, out TValue value) => this.TryGetCore(in key, out value);
    public bool TryAdd(in TKey key, in TValue value) => this.TryAddCore(in key, in value, null);
    public bool TryAdd(in TKey key, AtomicValueFactory valueFactory) => this.TryAddCore(in key, default!, valueFactory.Required());
    public TValue GetOrAdd(in TKey key, in TValue value) => this.GetOrAddCore(in key, in value, null);
    public TValue GetOrAdd(in TKey key, AtomicValueFactory valueFactory) => this.GetOrAddCore(in key, default!, valueFactory.Required());

    public TValue AddOrUpdate(in TKey key, AtomicValueFactory addFactory, AtomicUpdateFactory updateFactory) =>
        this.AddOrUpdateCore(in key, default!, addFactory.Required(), updateFactory.Required());

    public TValue AddOrUpdate(in TKey key, in TValue value, AtomicUpdateFactory updateFactory) =>
        this.AddOrUpdateCore(in key, in value, null, updateFactory.Required());

    public bool TryUpdate(in TKey key, in TValue value, in TValue comparisonValue, EqualityComparer<TValue>? comparer = null) =>
        this.TryUpdateCore(in key, in value, null, in comparisonValue, comparer);

    public bool TryUpdate(in TKey key, AtomicValueFactory valueFactory, in TValue comparisonValue, EqualityComparer<TValue>? comparer = null) =>
        this.TryUpdateCore(in key, default!, valueFactory.Required(), in comparisonValue, comparer);

    public bool TryRemove(in TKey key, out TValue value) => this.TryRemoveCore(in key, null, out value);

    public bool TryRemove(in TKey key, AtomicRemovePredicate predicate, out TValue value) =>
        this.TryRemoveCore(in key, predicate.Required(), out value);

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var memory = this.memory;

            return memory.Sum(CountRoot);
        }
    }

    public void Clear() => this.ClearCore();
    public bool ContainsKey(in TKey key) => this.TryGet(in key, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetCore(in TKey key, out TValue value)
    {
        if (!this.memory.TryRef(this.Hash(in key), out var rootRef))
        {
            value = default!;

            return false;
        }

        ref var root = ref rootRef.Value;
        root.gate.Enter();

        try
        {
            if (ReferenceEquals(root.state, singleEntryMarker))
            {
                if (this.comparer.Equals(root.key, key))
                {
                    value = root.value;

                    return true;
                }

                value = default!;

                return false;
            }

            if (root.state is not AtomicDenseRam<Entry> chain)
            {
                value = default!;

                return false;
            }

            for (var current = root.next; current != 0; current = chain.Ref(Unpack(current)).next)
            {
                ref var entry = ref chain.Ref(Unpack(current));

                if (!this.comparer.Equals(entry.key, key))
                    continue;

                value = entry.value;

                return true;
            }

            value = default!;

            return false;
        }
        finally
        {
            root.gate.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryAddCore(in TKey key, in TValue value, AtomicValueFactory? valueFactory)
    {
        var hash = this.Hash(in key);
        _ = this.memory.Alloc(hash);
        ref var root = ref this.memory.Ref(hash);
        root.gate.Enter();

        try
        {
            if (root.state is null)
            {
                FillSingle(ref root, in key, CreateValue(in key, in value, valueFactory));

                return true;
            }

            if (ReferenceEquals(root.state, singleEntryMarker))
            {
                if (this.comparer.Equals(root.key, key))
                    return false;

                Promote(ref root, in key, CreateValue(in key, in value, valueFactory));

                return true;
            }

            var chain = (AtomicDenseRam<Entry>)root.state;

            for (var current = root.next; current != 0; current = chain.Ref(Unpack(current)).next)
            {
                if (this.comparer.Equals(chain.Ref(Unpack(current)).key, key))
                    return false;
            }

            root.next = Push(chain, in key, CreateValue(key, in value, valueFactory), root.next);

            return true;
        }
        finally
        {
            root.gate.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TValue GetOrAddCore(in TKey key, in TValue value, AtomicValueFactory? valueFactory)
    {
        var hash = this.Hash(in key);
        _ = this.memory.Alloc(hash);
        ref var root = ref this.memory.Ref(hash);
        root.gate.Enter();

        try
        {
            if (root.state is null)
            {
                var created = CreateValue(in key, in value, valueFactory);
                FillSingle(ref root, in key, in created);

                return created;
            }

            if (ReferenceEquals(root.state, singleEntryMarker))
            {
                if (this.comparer.Equals(root.key, key))
                    return root.value;

                var created = CreateValue(in key, in value, valueFactory);
                Promote(ref root, in key, in created);

                return created;
            }

            var chain = (AtomicDenseRam<Entry>)root.state;

            for (var current = root.next; current != 0; current = chain.Ref(Unpack(current)).next)
            {
                ref var entry = ref chain.Ref(Unpack(current));

                if (this.comparer.Equals(entry.key, key))
                    return entry.value;
            }

            var createdValue = CreateValue(in key, in value, valueFactory);
            root.next = Push(chain, in key, in createdValue, root.next);

            return createdValue;
        }
        finally
        {
            root.gate.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TValue AddOrUpdateCore(in TKey key, in TValue value, AtomicValueFactory? addFactory, AtomicUpdateFactory? updateFactory)
    {
        var hash = this.Hash(in key);
        _ = this.memory.Alloc(hash);
        ref var root = ref this.memory.Ref(hash);
        root.gate.Enter();

        try
        {
            if (root.state is null)
            {
                var created = CreateValue(in key, in value, addFactory);
                FillSingle(ref root, in key, in created);

                return created;
            }

            if (ReferenceEquals(root.state, singleEntryMarker))
            {
                if (this.comparer.Equals(root.key, key))
                    return root.value = updateFactory!(key, root.value);

                var created = CreateValue(in key, in value, addFactory);
                Promote(ref root, in key, in created);

                return created;
            }

            var chain = (AtomicDenseRam<Entry>)root.state;

            for (var current = root.next; current != 0; current = chain.Ref(Unpack(current)).next)
            {
                ref var entry = ref chain.Ref(Unpack(current));

                if (!this.comparer.Equals(entry.key, key))
                    continue;

                return entry.value = updateFactory!(key, entry.value);
            }

            var createdValue = CreateValue(in key, in value, addFactory);
            root.next = Push(chain, in key, in createdValue, root.next);

            return createdValue;
        }
        finally
        {
            root.gate.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryUpdateCore(
        in TKey key,
        in TValue value,
        AtomicValueFactory? valueFactory,
        in TValue comparisonValue,
        EqualityComparer<TValue>? comparer)
    {
        if (!this.memory.TryRef(this.Hash(in key), out var rootRef))
            return false;

        ref var root = ref rootRef.Value;
        root.gate.Enter();

        try
        {
            var valueComparer = comparer ?? EqualityComparer<TValue>.Default;

            if (ReferenceEquals(root.state, singleEntryMarker))
            {
                if (!this.comparer.Equals(root.key, key) || !valueComparer.Equals(root.value, comparisonValue))
                    return false;

                root.value = CreateValue(in key, in value, valueFactory);

                return true;
            }

            if (root.state is not AtomicDenseRam<Entry> chain)
                return false;

            for (var current = root.next; current != 0; current = chain.Ref(Unpack(current)).next)
            {
                ref var entry = ref chain.Ref(Unpack(current));

                if (!this.comparer.Equals(entry.key, key) || !valueComparer.Equals(entry.value, comparisonValue))
                    continue;

                entry.value = CreateValue(in key, in value, valueFactory);

                return true;
            }

            return false;
        }
        finally
        {
            root.gate.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRemoveCore(in TKey key, AtomicRemovePredicate? predicate, out TValue value)
    {
        if (!this.memory.TryRef(this.Hash(in key), out var rootRef))
        {
            value = default!;

            return false;
        }

        ref var root = ref rootRef.Value;
        root.gate.Enter();

        try
        {
            if (ReferenceEquals(root.state, singleEntryMarker))
            {
                if (!this.comparer.Equals(root.key, key))
                {
                    value = default!;

                    return false;
                }

                value = root.value;

                if (predicate is not null && !predicate(key, value))
                {
                    value = default!;

                    return false;
                }

                ClearEntry(ref root);

                return true;
            }

            if (root.state is not AtomicDenseRam<Entry> chain)
            {
                value = default!;

                return false;
            }

            nuint previous = 0;

            for (var current = root.next; current != 0; current = chain.Ref(Unpack(current)).next)
            {
                ref var entry = ref chain.Ref(Unpack(current));

                if (!this.comparer.Equals(entry.key, key))
                {
                    previous = current;

                    continue;
                }

                value = entry.value;

                if (predicate is not null && !predicate(key, value))
                {
                    value = default!;

                    return false;
                }

                if (previous == 0)
                    root.next = entry.next;
                else
                    chain.Ref(Unpack(previous)).next = entry.next;

                _ = chain.Free(Unpack(current));
                Collapse(ref root, chain);

                return true;
            }

            value = default!;

            return false;
        }
        finally
        {
            root.gate.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearCore() => this.memory = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static TValue CreateValue(in TKey key, in TValue value, AtomicValueFactory? valueFactory) =>
        valueFactory is null ? value : valueFactory(key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountRoot(ref Entry root)
    {
        root.gate.Enter();

        try
        {
            if (ReferenceEquals(root.state, singleEntryMarker))
                return 1;

            return root.state is AtomicDenseRam<Entry> chain ? chain.AllocatedCount : 0;
        }
        finally
        {
            root.gate.Exit();
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Promote(ref Entry root, in TKey key, in TValue value)
    {
        var chain = new AtomicDenseRam<Entry>();
        var head = Push(chain, in root.key, in root.value, 0);
        head = Push(chain, in key, in value, head);
        FillCollision(ref root, chain, head);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint Push(AtomicDenseRam<Entry> chain, in TKey key, in TValue value, nuint next)
    {
        var ptr = chain.Alloc();
        ref var entry = ref chain.Ref(ptr);
        entry.gate = default;
        entry.key = key;
        entry.value = value;
        entry.state = null;
        entry.next = next;

        return Pack(ptr);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void Collapse(ref Entry root, AtomicDenseRam<Entry> chain)
    {
        if (root.next == 0)
        {
            ClearEntry(ref root);

            return;
        }

        var headPtr = root.next;
        ref var head = ref chain.Ref(Unpack(headPtr));

        if (head.next != 0)
            return;

        FillSingle(ref root, in head.key, in head.value);
        _ = chain.Free(Unpack(headPtr));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillSingle(ref Entry entry, in TKey key, in TValue value)
    {
        entry.key = key;
        entry.value = value;
        entry.next = 0;
        entry.state = singleEntryMarker;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FillCollision(ref Entry entry, AtomicDenseRam<Entry> chain, nuint head)
    {
        entry.key = default!;
        entry.value = default!;
        entry.next = head;
        entry.state = chain;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClearEntry(ref Entry entry)
    {
        entry.key = default!;
        entry.value = default!;
        entry.next = 0;
        entry.state = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private nuint Hash(in TKey key)
    {
        ulong x = unchecked((uint)this.comparer.GetHashCode(key));
        x |= x << (sizeof(int) * 8);
        x ^= x >> 33;
        x *= 0xff51afd7ed558ccdUL;
        x ^= x >> 33;
        x *= 0xc4ceb9fe1a85ec53UL;
        x ^= x >> 33;

        return unchecked((nuint)x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint Pack(nuint ptr) => ptr + 1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nuint Unpack(nuint ptr) => ptr - 1;

    private struct Entry
    {
        internal AtomicLock gate;
        internal TKey key;
        internal TValue value;
        internal object? state;
        internal nuint next;
    }
}
