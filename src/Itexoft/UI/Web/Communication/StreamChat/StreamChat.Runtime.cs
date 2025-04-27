// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;
using Itexoft.Threading.Tasks;

namespace Itexoft.UI.Web.Communication.StreamChat;

public sealed partial class StreamChat
{
    internal bool AddStream(string key, IStreamRw<char> stream)
    {
        key = key.RequiredNotWhiteSpace();
        stream.Required();
        this.disposed.ThrowIf();
        CancelToken readCancel;
        long generation;
        string payload;

        using (this.stateLock.Enter())
        {
            if (this.chats.ContainsKey(key))
                return false;

            readCancel = CancelToken.New();
            generation = 1;
            var chat = new StreamChatState(key, this.nextOrder++, stream, generation, readCancel);
            this.chats.Add(key, chat);
            payload = StreamChatPayloadWriter.CreateChatAdded(ToSnapshot(chat));
        }

        this.Broadcast(payload);
        this.StartReadLoop(key, stream, generation, readCancel);

        return true;
    }

    internal void AddOrUpdateStream(string key, IStreamRw<char> stream)
    {
        if (!this.UpdateStream(key, stream))
            _ = this.AddStream(key, stream);
    }

    internal bool RemoveStream(string key, out IStreamRw<char> value)
    {
        key = key.RequiredNotWhiteSpace();
        this.disposed.ThrowIf();
        CancelToken cancel;

        using (this.stateLock.Enter())
        {
            if (!this.chats.Remove(key, out var chat))
            {
                value = default!;

                return false;
            }

            value = chat.Stream;
            cancel = chat.ReadCancel;
        }

        cancel.Cancel();
        this.Broadcast(StreamChatPayloadWriter.CreateChatRemoved(key));

        return true;
    }

    internal KeyValue<string, IStreamRw<char>>[] SnapshotBindings()
    {
        using (this.stateLock.Enter())
        {
            var snapshots = SnapshotChats(this.chats);
            var result = new KeyValue<string, IStreamRw<char>>[snapshots.Length];

            for (var i = 0; i < snapshots.Length; i++)
                result[i] = new(snapshots[i].Key, this.chats[snapshots[i].Key].Stream);

            return result;
        }
    }

    internal bool TryGetStream(string key, out IStreamRw<char> value)
    {
        key = key.RequiredNotWhiteSpace();

        using (this.stateLock.Enter())
        {
            if (!this.chats.TryGetValue(key, out var chat))
            {
                value = default!;

                return false;
            }

            value = chat.Stream;

            return true;
        }
    }

    internal bool UpdateStream(string key, IStreamRw<char> stream)
    {
        key = key.RequiredNotWhiteSpace();
        stream.Required();
        this.disposed.ThrowIf();
        CancelToken previousCancel;
        CancelToken readCancel;
        long generation;
        string[] payloads;

        using (this.stateLock.Enter())
        {
            if (!this.chats.TryGetValue(key, out var chat))
                return false;

            previousCancel = chat.ReadCancel;
            readCancel = CancelToken.New();
            generation = checked(chat.TransportGeneration + 1);
            chat.Stream = stream;
            chat.ReadCancel = readCancel;
            chat.TransportGeneration = generation;
            chat.Status = StreamChatProtocol.StatusLive;
            payloads = this.CompleteOpenIncoming(chat, true);
        }

        previousCancel.Cancel();
        this.Broadcast(payloads);
        this.StartReadLoop(key, stream, generation, readCancel);

        return true;
    }

    private string[] CompleteOpenIncoming(StreamChatState chat, bool includeStatus = false, string? status = null)
    {
        var list = new List<string>(capacity: includeStatus ? 2 : 1);

        if (chat.OpenIncomingMessage is StreamChatMessage open)
        {
            open.Completed = true;
            chat.OpenIncomingMessage = null;
            list.Add(StreamChatPayloadWriter.CreateMessageCompleted(chat.Key, open.Id));
        }

        if (includeStatus)
            list.Add(StreamChatPayloadWriter.CreateChatStatus(chat.Key, status ?? chat.Status));

        return list.Count == 0 ? [] : list.ToArray();
    }

    private CancelToken[] DetachAllReadCancels()
    {
        using (this.stateLock.Enter())
        {
            var result = new CancelToken[this.chats.Count];
            var index = 0;

            foreach (var chat in this.chats.Values)
                result[index++] = chat.ReadCancel;

            return result;
        }
    }

    private static long GetTimestampUtcMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void StartReadLoop(string key, IStreamRw<char> stream, long generation, CancelToken cancelToken) =>
        _ = Promise.Run(() => this.RunReadLoop(key, stream, generation, cancelToken), false, cancelToken);

    private void RunReadLoop(string key, IStreamRw<char> stream, long generation, CancelToken cancelToken)
    {
        var buffer = new char[StreamChatProtocol.ReadBufferChars];

        try
        {
            while (true)
            {
                var read = stream.Read(buffer, cancelToken);

                if (read == 0)
                {
                    this.CompleteTransport(key, generation, StreamChatProtocol.StatusEnded);

                    return;
                }

                if (!this.AppendIncoming(key, generation, new string(buffer, 0, read)))
                    return;
            }
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested) { }
        catch (ObjectDisposedException) when (cancelToken.IsRequested) { }
        catch
        {
            this.CompleteTransport(key, generation, StreamChatProtocol.StatusError);
        }
    }

    private bool AppendIncoming(string key, long generation, string fragment)
    {
        if (string.IsNullOrEmpty(fragment))
            return true;

        string[] payloads;

        using (this.stateLock.Enter())
        {
            if (!this.chats.TryGetValue(key, out var chat) || chat.TransportGeneration != generation || chat.Status != StreamChatProtocol.StatusLive)
                return false;

            if (chat.OpenIncomingMessage is not StreamChatMessage message)
            {
                message = new StreamChatMessage(chat.NextMessageId++, StreamChatProtocol.DirectionIncoming, fragment, GetTimestampUtcMs(), false);
                chat.Messages.Add(message);
                chat.OpenIncomingMessage = message;
                payloads = [StreamChatPayloadWriter.CreateMessageAdded(key, ToSnapshot(message))];
            }
            else
            {
                message.Text = string.Concat(message.Text, fragment);
                payloads = [StreamChatPayloadWriter.CreateMessageAppended(key, message.Id, fragment)];
            }
        }

        this.Broadcast(payloads);

        return true;
    }

    private void CompleteTransport(string key, long generation, string status)
    {
        string[] payloads;

        using (this.stateLock.Enter())
        {
            if (!this.chats.TryGetValue(key, out var chat) || chat.TransportGeneration != generation)
                return;

            chat.Status = status;
            payloads = this.CompleteOpenIncoming(chat, true, status);
        }

        this.Broadcast(payloads);
    }

    private bool TrySendMessage(string key, string text)
    {
        key = key.RequiredNotWhiteSpace();
        text.RequiredNotWhiteSpace();
        StreamChatState chat;
        IStreamRw<char> stream;
        long generation;

        using (this.stateLock.Enter())
        {
            if (!this.chats.TryGetValue(key, out chat!) || chat.Status != StreamChatProtocol.StatusLive)
                return false;

            stream = chat.Stream;
            generation = chat.TransportGeneration;
        }

        using (chat.WriteLock.Enter())
        {
            using (this.stateLock.Enter())
            {
                if (!this.chats.TryGetValue(key, out var current)
                    || !ReferenceEquals(current, chat)
                    || current.TransportGeneration != generation
                    || current.Status != StreamChatProtocol.StatusLive
                    || !ReferenceEquals(current.Stream, stream))
                    return false;
            }

            try
            {
                stream.Write(text.AsSpan());
                stream.Flush();
            }
            catch
            {
                this.CompleteTransport(key, generation, StreamChatProtocol.StatusError);

                return false;
            }

            string[] payloads;

            using (this.stateLock.Enter())
            {
                if (!this.chats.TryGetValue(key, out var current) || !ReferenceEquals(current, chat) || current.TransportGeneration != generation)
                    return true;

                var list = new List<string>(2);

                if (current.OpenIncomingMessage is StreamChatMessage open)
                {
                    open.Completed = true;
                    current.OpenIncomingMessage = null;
                    list.Add(StreamChatPayloadWriter.CreateMessageCompleted(key, open.Id));
                }

                var message = new StreamChatMessage(current.NextMessageId++, StreamChatProtocol.DirectionOutgoing, text, GetTimestampUtcMs(), true);
                current.Messages.Add(message);
                list.Add(StreamChatPayloadWriter.CreateMessageAdded(key, ToSnapshot(message)));
                payloads = list.ToArray();
            }

            this.Broadcast(payloads);

            return true;
        }
    }

    private static StreamChatMessageSnapshot ToSnapshot(StreamChatMessage message) =>
        new(message.Id, message.Direction, message.Text, message.TimestampUtcMs, message.Completed);

    private static StreamChatSnapshot ToSnapshot(StreamChatState chat)
    {
        var messages = new StreamChatMessageSnapshot[chat.Messages.Count];

        for (var i = 0; i < chat.Messages.Count; i++)
            messages[i] = ToSnapshot(chat.Messages[i]);

        return new(chat.Key, chat.Order, chat.Status, messages);
    }

    private static StreamChatSnapshot[] SnapshotChats(Dictionary<string, StreamChatState> chats)
    {
        var snapshots = new StreamChatSnapshot[chats.Count];
        var index = 0;

        foreach (var chat in chats.Values)
            snapshots[index++] = ToSnapshot(chat);

        Array.Sort(snapshots, static (left, right) => left.Order.CompareTo(right.Order));

        return snapshots;
    }
}
