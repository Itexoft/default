// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.AI.OpenAI.Models;
using Itexoft.Formats.Json;
using Itexoft.Net.Http;

namespace Itexoft.AI.OpenAI;

public static class OpenAiJson
{
    public static OpenAiSerializerContext SerializerContext { get; } = new(JsonDefaultOptions.Default);

    public static NetHttpContentType ContentType { get; } = $"{NetHttpContentType.ApplicationJson}; charset=utf-8";

    public static byte[] Serialize<T>(T value) => SerializerContext.SerializeToUtf8Bytes(value);

    public static T Deserialize<T>(string json) =>
        SerializerContext.Deserialize<T>(json)
        ?? throw new InvalidDataException($"JSON payload does not contain a '{typeof(T).Name}' value.");

    public static T Deserialize<T>(ReadOnlySpan<byte> utf8Json) =>
        SerializerContext.Deserialize<T>(utf8Json)
        ?? throw new InvalidDataException($"JSON payload does not contain a '{typeof(T).Name}' value.");
}
