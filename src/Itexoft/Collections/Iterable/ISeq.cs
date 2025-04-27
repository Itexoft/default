// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Collections;

public interface ISeq<T> : IRoSeq<T>
{
    new T this[long index] { get; set; }
    void Add(T value);
    bool Remove(T value);
    bool RemoveAt(long index);
    bool Contains(T value);
    long IndexOf(T value);
    bool Insert(long index, T item);
    void Clear();
}
