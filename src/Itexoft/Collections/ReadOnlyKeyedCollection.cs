// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Diagnostics.CodeAnalysis;

namespace Itexoft.Collections;

public abstract class ReadOnlyKeyedCollection<TKey, TValue>(IEnumerable<TValue> items, Func<TValue, TKey> map)
    : IReadOnlyCollection<TValue> where TKey : notnull
{
    private IReadOnlyDictionary<TKey, TValue> dictionary = items.ToDictionary(map, x => x);

    public TValue this[TKey key] => this.dictionary[key];

    public IEnumerable<TKey> Keys => this.dictionary.Keys;

    public IEnumerable<TValue> Values => this.dictionary.Values;

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)this.dictionary).GetEnumerator();

    public IEnumerator<TValue> GetEnumerator() => this.dictionary.Values.GetEnumerator();

    public int Count => this.dictionary.Count;

    public bool ContainsKey(TKey key) => this.dictionary.ContainsKey(key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => this.dictionary.TryGetValue(key, out value);
}
