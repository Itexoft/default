// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Collections;

public interface IMapFactory<TKey, TValue> : IMap<TKey, TValue> where TKey : notnull
{
    public delegate bool RemovePredicate(TKey key, TValue value);

    public delegate TValue UpdateFactory(TKey key, TValue oldValue);

    public delegate TValue ValueFactory(TKey key);

    public bool Add(in TKey key, ValueFactory valueFactory);
    public TValue GetOrAdd(in TKey key, ValueFactory valueFactory);
    public TValue AddOrUpdate(in TKey key, ValueFactory addFactory, UpdateFactory updateFactory);
    public TValue AddOrUpdate(in TKey key, in TValue value, UpdateFactory updateFactory);
    public bool Update(in TKey key, ValueFactory valueFactory, in TValue comparisonValue, EqualityComparer<TValue>? comparer = null);
    public bool Remove(in TKey key, RemovePredicate predicate, out TValue value);
}
