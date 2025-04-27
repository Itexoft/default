// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using Itexoft.Collections;
using Itexoft.IO;

namespace Itexoft.UI.Web.Communication.StreamChat;

internal sealed class StreamChatRegistry(StreamChat owner) : IMap<string, IStreamRw<char>>
{
    public bool Add(in string key, in IStreamRw<char> value) => owner.AddStream(key, value);

    public void AddOrUpdate(in string key, in IStreamRw<char> value) => owner.AddOrUpdateStream(key, value);

    public bool Get(in string key, [MaybeNullWhen(false)] out IStreamRw<char> value) => owner.TryGetStream(key, out value);

    public IEnumerator<KeyValue<string, IStreamRw<char>>> GetEnumerator()
    {
        var items = owner.SnapshotBindings();

        for (var i = 0; i < items.Length; i++)
            yield return items[i];
    }

    public IStreamRw<char> GetOrAdd(in string key, in IStreamRw<char> value)
    {
        if (owner.TryGetStream(key, out var existing))
            return existing;

        owner.AddOrUpdateStream(key, value);

        return value;
    }

    public bool Remove(in string key, [MaybeNullWhen(false)] out IStreamRw<char> value) => owner.RemoveStream(key, out value);

    public bool Update(in string key, in IStreamRw<char> value) => owner.UpdateStream(key, value);

    public TValue GetOrAdd<TValue>(in string key, in TValue value) where TValue : IStreamRw<char>
    {
        if (owner.TryGetStream(key, out var existing))
            return (TValue)existing;

        owner.AddOrUpdateStream(key, value);

        return value;
    }
}
