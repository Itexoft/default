// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections.Atomics;
using Itexoft.Threading;

namespace Itexoft.Optimization.Caching;

public class DeferredCache<TKey, TValue> where TKey : notnull
{
    private readonly AtomicDictionary<TKey, Deferred<TValue>> source;

    public DeferredCache() => this.source = new();

    public DeferredCache(IEnumerable<KeyValuePair<TKey, Deferred<TValue>>> collection) => this.source = new(collection);

    public DeferredCache(IEqualityComparer<TKey> comparer) => this.source = new(comparer);

    public DeferredCache(IEnumerable<KeyValuePair<TKey, Deferred<TValue>>> collection, IEqualityComparer<TKey> comparer)
    {
        this.source = new(comparer);

        foreach (var kvp in collection)
            this.source.TryAdd(kvp.Key, kvp.Value);
    }

    public bool TryRemove(TKey item, Func<TKey, TValue, bool> predicate, out TValue value)
    {
        if (this.source.TryRemove(item, (k, v) => predicate(k, v.Value), out var deferred))
        {
            value = deferred.Value;

            return true;
        }

        value = default!;

        return false;
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory) =>
        this.source.GetOrAdd(key, k => new(() => valueFactory(k))).Value;

    public bool ContainsKey(TKey key) => this.source.ContainsKey(key);
}
