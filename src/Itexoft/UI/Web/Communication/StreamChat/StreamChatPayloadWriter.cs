// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using System.Text.Json;

namespace Itexoft.UI.Web.Communication.StreamChat;

internal static class StreamChatPayloadWriter
{
    public static string CreateChatAdded(StreamChatSnapshot chat) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", StreamChatProtocol.TypeChatAdded);
        WriteChatFields(writer, chat);
        writer.WriteEndObject();
    });

    public static string CreateChatRemoved(string key) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", StreamChatProtocol.TypeChatRemoved);
        writer.WriteString("key", key);
        writer.WriteEndObject();
    });

    public static string CreateChatStatus(string key, string status) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", StreamChatProtocol.TypeChatStatus);
        writer.WriteString("key", key);
        writer.WriteString("status", status);
        writer.WriteEndObject();
    });

    public static string CreateMessageAdded(string key, StreamChatMessageSnapshot message) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", StreamChatProtocol.TypeMessageAdded);
        writer.WriteString("key", key);
        writer.WritePropertyName("message");
        WriteMessage(writer, message);
        writer.WriteEndObject();
    });

    public static string CreateMessageAppended(string key, long messageId, string textFragment) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", StreamChatProtocol.TypeMessageAppended);
        writer.WriteString("key", key);
        writer.WriteNumber("messageId", messageId);
        writer.WriteString("textFragment", textFragment);
        writer.WriteEndObject();
    });

    public static string CreateMessageCompleted(string key, long messageId) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", StreamChatProtocol.TypeMessageCompleted);
        writer.WriteString("key", key);
        writer.WriteNumber("messageId", messageId);
        writer.WriteEndObject();
    });

    public static string CreateSnapshot(StreamChatSnapshot[] chats) => WriteJson(writer =>
    {
        writer.WriteStartObject();
        writer.WriteString("type", StreamChatProtocol.TypeSnapshot);
        writer.WritePropertyName("chats");
        writer.WriteStartArray();

        foreach (var chat in chats)
            WriteChat(writer, chat);

        writer.WriteEndArray();
        writer.WriteEndObject();
    });

    private static string WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();

        using (var writer = new Utf8JsonWriter(stream))
            write(writer);

        return Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
    }

    private static void WriteChat(Utf8JsonWriter writer, StreamChatSnapshot chat)
    {
        writer.WriteStartObject();
        WriteChatFields(writer, chat);
        writer.WriteEndObject();
    }

    private static void WriteChatFields(Utf8JsonWriter writer, StreamChatSnapshot chat)
    {
        writer.WriteString("key", chat.Key);
        writer.WriteNumber("order", chat.Order);
        writer.WriteString("status", chat.Status);
        writer.WritePropertyName("messages");
        writer.WriteStartArray();

        foreach (var message in chat.Messages)
            WriteMessage(writer, message);

        writer.WriteEndArray();
    }

    private static void WriteMessage(Utf8JsonWriter writer, StreamChatMessageSnapshot message)
    {
        writer.WriteStartObject();
        writer.WriteNumber("id", message.Id);
        writer.WriteString("direction", message.Direction);
        writer.WriteString("text", message.Text);
        writer.WriteNumber("timestampUtcMs", message.TimestampUtcMs);
        writer.WriteBoolean("completed", message.Completed);
        writer.WriteEndObject();
    }
}
