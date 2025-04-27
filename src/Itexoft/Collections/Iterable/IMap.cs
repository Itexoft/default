// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;

namespace Itexoft.Collections;

public interface IMap<TKey, TValue> : IRoMap<TKey, TValue> where TKey : notnull
{
    public new TValue this[TKey key]
    {
        get => ((IRoMap<TKey, TValue>)this)[key];
        set => this.AddOrUpdate(in key, value);
    }

    public bool Add(in TKey key, in TValue value);
    public TValue GetOrAdd(in TKey key, in TValue value);
    public void AddOrUpdate(in TKey key, in TValue value);
    public bool Update(in TKey key, in TValue value);
    public bool Remove(in TKey key, [MaybeNullWhen(false)] out TValue value);
}
