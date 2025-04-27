// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Itexoft.AI.Api.OpenAI;

internal static class OpenAiJson
{
    public const string ContentType = "application/json; charset=utf-8";

    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static byte[] Serialize<T>(T value) => JsonSerializer.SerializeToUtf8Bytes(value, Options);

    public static T Deserialize<T>(ReadOnlySpan<byte> value) =>
        JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException($"OpenAI JSON payload cannot be deserialized to {typeof(T)}.");

    public static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, Options)
        ?? throw new InvalidDataException($"OpenAI JSON payload cannot be deserialized to {typeof(T)}.");

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };

        options.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        return options;
    }
}
