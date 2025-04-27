// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;
using Itexoft.UI.Web.Communication.StreamChat.Transport;
using Itexoft.UI.Web.EmbeddedWeb;

namespace Itexoft.UI.Web.Communication.StreamChat;

public sealed partial class StreamChat
{
    private StreamChatWebSocketSession[] DetachAllSessions()
    {
        using (this.sessionLock.Enter())
        {
            if (this.sessions.Count == 0)
                return [];

            var result = new StreamChatWebSocketSession[this.sessions.Count];
            this.sessions.Values.CopyTo(result, 0);
            this.sessions.Clear();

            return result;
        }
    }

    private void Broadcast(params string[] payloads)
    {
        if (payloads.Length == 0)
            return;

        StreamChatWebSocketSession[] sessions;

        using (this.sessionLock.Enter())
        {
            if (this.sessions.Count == 0)
                return;

            sessions = new StreamChatWebSocketSession[this.sessions.Count];
            this.sessions.Values.CopyTo(sessions, 0);
        }

        for (var i = 0; i < sessions.Length; i++)
        {
            for (var j = 0; j < payloads.Length; j++)
                _ = sessions[i].TrySend(payloads[j]);
        }
    }

    private EmbeddedWebWebSocketRequestHandler CreateWebSocketHandler() =>
        (request, _) => request.PathAndQuery.Path.Equals(StreamChatProtocol.WebSocketPath, StringComparison.Ordinal) ? this.RunBrowserSession : null;

    private string CreateSnapshotPayload()
    {
        using (this.stateLock.Enter())
            return StreamChatPayloadWriter.CreateSnapshot(SnapshotChats(this.chats));
    }

    private void RemoveSession(long id)
    {
        using (this.sessionLock.Enter())
            this.sessions.Remove(id);
    }

    private void RunBrowserSession(NetHttpWebSocket webSocket, CancelToken cancelToken)
    {
        this.disposed.ThrowIf(in cancelToken);
        var session = new StreamChatWebSocketSession(Interlocked.Increment(ref this.nextSessionId), webSocket, this.RemoveSession);

        using (this.sessionLock.Enter())
            this.sessions.Add(session.Id, session);

        if (!session.TrySend(this.CreateSnapshotPayload(), cancelToken))
            return;

        try
        {
            while (session.TryReceive(out var message, cancelToken))
            {
                if (message.Type == NetHttpWebSocketMessageType.Close)
                    return;

                this.HandleBrowserMessage(message.Text);
            }
        }
        finally
        {
            session.Dispose();
        }
    }

    private void HandleBrowserMessage(string text)
    {
        using var json = JsonDocument.Parse(text);
        var root = json.RootElement;
        var type = root.TryGetProperty("type", out var typeNode) ? typeNode.GetString() : null;

        if (!StreamChatProtocol.TypeSend.Equals(type, StringComparison.Ordinal)
            || !root.TryGetProperty("key", out var keyNode)
            || !root.TryGetProperty("text", out var textNode))
            throw new InvalidDataException("Unsupported StreamChat websocket payload.");

        var key = keyNode.GetString();
        var payload = textNode.GetString();

        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(payload))
            throw new InvalidDataException("Invalid StreamChat websocket payload.");

        _ = this.TrySendMessage(key, payload);
    }
}
