// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Web.Communication.StreamChat;

internal static class StreamChatProtocol
{
    public const string BundleId = "stream-chat";
    public const string WebSocketPath = "/ws";
    public const string StatusLive = "live";
    public const string StatusEnded = "ended";
    public const string StatusError = "error";
    public const string DirectionIncoming = "incoming";
    public const string DirectionOutgoing = "outgoing";
    public const string TypeSnapshot = "snapshot";
    public const string TypeChatAdded = "chat-added";
    public const string TypeChatRemoved = "chat-removed";
    public const string TypeChatStatus = "chat-status";
    public const string TypeMessageAdded = "message-added";
    public const string TypeMessageAppended = "message-appended";
    public const string TypeMessageCompleted = "message-completed";
    public const string TypeSend = "send";
    public const int ReadBufferChars = 256;
}
