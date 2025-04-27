// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections.Atomics;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Optimization.Caching;

public sealed class DeferredCoalescingCache<TKey, TValue> where TKey : notnull
{
    private readonly AtomicDictionary<TKey, TValue> source;

    public DeferredCoalescingCache() => this.source = new();

    public DeferredCoalescingCache(IEqualityComparer<TKey> comparer) => this.source = new(comparer);

    public TValue GetOrAddAsync(TKey key, Func<TKey, CancelToken, TValue> valueFactory, CancelToken cancelToken = default)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));

        valueFactory.Required();
        cancelToken.ThrowIf();

        return this.source.GetOrAdd(key, k => valueFactory(k, cancelToken.ThrowIf()));
    }

    public bool TryRemove(TKey key) => this.source.TryRemove(key, out _);
}
