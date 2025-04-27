// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.AI.OpenAI.Models;

public readonly record struct OpenAiModelsResponse()
{
    [JsonPropertyName("object")] public string Object { get; init; } = "list";

    [JsonPropertyName("data")] public List<OpenAiModel> Data { get; init; } = [];
}

public readonly record struct OpenAiModel()
{
    [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;

    [JsonPropertyName("object")] public string Object { get; init; } = "model";

    [JsonPropertyName("created")] public long Created { get; init; }

    [JsonPropertyName("owned_by")] public string? OwnedBy { get; init; }
}
