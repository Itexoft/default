// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.AI.Api.OpenAI.Models;

public sealed record class OpenAiEmbeddingsRequest
{
    [JsonPropertyName("model")] public string? Model { get; init; }

    [JsonPropertyName("input")] public string[] Input { get; init; } = [];

    [JsonPropertyName("encoding_format")] public string? EncodingFormat { get; init; }

    [JsonPropertyName("dimensions")] public int? Dimensions { get; init; }

    [JsonPropertyName("user")] public string? User { get; init; }
}

public sealed record class OpenAiEmbeddingsResponse
{
    [JsonPropertyName("model")] public string? Model { get; init; }

    [JsonPropertyName("data")] public List<OpenAiEmbeddingData> Data { get; init; } = [];

    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("usage")] public OpenAiEmbeddingUsage? Usage { get; init; }
}

public sealed record class OpenAiEmbeddingData
{
    [JsonPropertyName("index")] public int Index { get; init; }

    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("embedding")] public float[] Embedding { get; init; } = [];
}

public sealed record class OpenAiEmbeddingUsage
{
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; init; }

    [JsonPropertyName("total_tokens")] public int TotalTokens { get; init; }
}
