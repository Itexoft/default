// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.UI.Web.Communication.StreamChat;

internal sealed class StreamChatState(string key, long order, IStreamRw<char> stream, long transportGeneration, CancelToken readCancel)
{
    public AtomicLock WriteLock;
    public CancelToken ReadCancel { get; set; } = readCancel;
    public string Key { get; } = key;
    public long NextMessageId { get; set; } = 1;
    public long Order { get; } = order;
    public StreamChatMessage? OpenIncomingMessage { get; set; }
    public string Status { get; set; } = StreamChatProtocol.StatusLive;
    public IStreamRw<char> Stream { get; set; } = stream;
    public long TransportGeneration { get; set; } = transportGeneration;
    public List<StreamChatMessage> Messages { get; } = [];
}

internal sealed class StreamChatMessage(long id, string direction, string text, long timestampUtcMs, bool completed)
{
    public bool Completed { get; set; } = completed;
    public string Direction { get; } = direction;
    public long Id { get; } = id;
    public string Text { get; set; } = text;
    public long TimestampUtcMs { get; } = timestampUtcMs;
}

internal readonly record struct StreamChatMessageSnapshot(long Id, string Direction, string Text, long TimestampUtcMs, bool Completed);

internal readonly record struct StreamChatSnapshot(string Key, long Order, string Status, StreamChatMessageSnapshot[] Messages);
