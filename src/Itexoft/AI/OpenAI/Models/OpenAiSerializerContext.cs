// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;

namespace Itexoft.AI.OpenAI.Models;

[JsonSerializable(typeof(object)), JsonSerializable(typeof(string[])), JsonSerializable(typeof(float[])), JsonSerializable(typeof(List<string>)),
 JsonSerializable(typeof(List<object>)), JsonSerializable(typeof(Dictionary<string, string>)), JsonSerializable(typeof(Dictionary<string, float>)),
 JsonSerializable(typeof(Dictionary<string, object?>)), JsonSerializable(typeof(OpenAiErrorResponse)), JsonSerializable(typeof(OpenAiError)),
 JsonSerializable(typeof(OpenAiResponseFormat)),
 JsonSerializable(typeof(OpenAiResponseFormatJsonSchema)), JsonSerializable(typeof(OpenAiChatCompletionChoice)),
 JsonSerializable(typeof(OpenAiChatCompletionDelta)), JsonSerializable(typeof(OpenAiChatCompletionMessage)),
 JsonSerializable(typeof(OpenAiChatCompletionRequest)), JsonSerializable(typeof(OpenAiChatCompletionResponse)),
 JsonSerializable(typeof(OpenAiChatCompletionResponseDelta)), JsonSerializable(typeof(OpenAiChatCompletionUsage)),
 JsonSerializable(typeof(OpenAiEmbeddingsRequest)), JsonSerializable(typeof(OpenAiEmbeddingsResponse)), JsonSerializable(typeof(OpenAiEmbeddingData)),
 JsonSerializable(typeof(OpenAiEmbeddingUsage)), JsonSerializable(typeof(OpenAiModelsResponse)), JsonSerializable(typeof(OpenAiModel)),
 JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
public sealed partial class OpenAiSerializerContext : JsonSerializerContext;
