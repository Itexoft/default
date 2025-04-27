// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using Itexoft.Extensions;

namespace Itexoft.Collections;

public interface IRoMap<TKey, TValue> : IIterable<KeyValue<TKey, TValue>> where TKey : notnull
{
    public TValue this[TKey key]
    {
        get
        {
            if (!this.Get(key.Required(), out var value))
                throw new KeyNotFoundException(key!.ToString());

            return value;
        }
    }

    public bool Get(in TKey key, [MaybeNullWhen(false)] out TValue value);
}
