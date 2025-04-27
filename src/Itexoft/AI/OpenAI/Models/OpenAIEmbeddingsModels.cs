// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.AI.OpenAI.Models;

public readonly record struct OpenAiEmbeddingsRequest()
{
    [JsonPropertyName("model")] public string? Model { get; init; }

    [JsonPropertyName("input")] public string[] Input { get; init; } = [];

    [JsonPropertyName("encoding_format")] public string? EncodingFormat { get; init; }

    [JsonPropertyName("dimensions")] public int? Dimensions { get; init; }

    [JsonPropertyName("user")] public string? User { get; init; }
}

public readonly record struct OpenAiEmbeddingsResponse()
{
    [JsonPropertyName("model")] public string? Model { get; init; }

    [JsonPropertyName("data")] public List<OpenAiEmbeddingData> Data { get; init; } = [];

    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("usage")] public OpenAiEmbeddingUsage? Usage { get; init; }
}

public readonly record struct OpenAiEmbeddingData()
{
    [JsonPropertyName("index")] public required int Index { get; init; }

    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("embedding")] public required float[] Embedding { get; init; } = [];
}

public readonly record struct OpenAiEmbeddingUsage
{
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; init; }

    [JsonPropertyName("total_tokens")] public int TotalTokens { get; init; }
}
