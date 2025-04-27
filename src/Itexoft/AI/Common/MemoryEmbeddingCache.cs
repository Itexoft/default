// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;

namespace Itexoft.AI.Common;

public sealed class MemoryEmbeddingCache : IEmbeddingCache
{
    private readonly ConcurrentDictionary<string, ReadOnlyMemory<float>> cache = new();

    public bool TryAdd(string text, ReadOnlyMemory<float> vector) => this.cache.TryAdd(text, vector);

    public bool TryGet(string text, out ReadOnlyMemory<float> vector) => this.cache.TryGetValue(text, out vector!);
}
