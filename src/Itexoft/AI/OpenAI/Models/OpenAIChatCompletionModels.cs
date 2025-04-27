// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;
using Itexoft.Extensions;

namespace Itexoft.AI.OpenAI.Models;

public readonly record struct OpenAiChatCompletionChoice
{
    [JsonPropertyName("index")] public int Index { get; init; }

    [JsonPropertyName("message")] public OpenAiChatCompletionMessage Message { get; init; }
}

public readonly record struct OpenAiChatCompletionDelta()
{
    [JsonPropertyName("index")] public int Index { get; init; }

    [JsonPropertyName("finish_reason")] public string? FinishReason { get; init; }

    [JsonPropertyName("delta")] public OpenAiChatCompletionMessage Delta { get; init; }

    [JsonExtensionData] public IDictionary<string, object?> Extra { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);
}

public readonly record struct OpenAiChatCompletionMessage
{
    public OpenAiChatCompletionMessage() { }

    public OpenAiChatCompletionMessage(string role) => this.Role = role.RequiredNotWhiteSpace();
    public OpenAiChatCompletionMessage(string role, string content) : this(role) => this.Content = content.RequiredNotWhiteSpace();

    [JsonPropertyName("role")] public string? Role { get; init; }

    [JsonPropertyName("content")] public string? Content { get; init; }

    [JsonPropertyName("reasoning_content")]
    public string? ReasoningContent { get; init; }

    [JsonExtensionData] public IDictionary<string, object?> Extra { get; } = new Dictionary<string, object?>(StringComparer.Ordinal);

    public OpenAiChatCompletionMessage AddContent(string key, string content)
    {
        this.Extra[key.RequiredNotWhiteSpace()] = content.Required();

        return this;
    }
}

public readonly record struct OpenAiChatCompletionRequest()
{
    [JsonPropertyName("model")] public string? Model { get; init; }

    [JsonPropertyName("messages")] public List<OpenAiChatCompletionMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")] public bool Stream { get; init; }

    [JsonPropertyName("audio")] public object? Audio { get; init; }

    [JsonPropertyName("frequency_penalty")]
    public float? FrequencyPenalty { get; init; } = 0;

    [JsonPropertyName("function_call")] public object? FunctionCall { get; init; }

    [JsonPropertyName("functions")] public List<object>? Functions { get; init; }

    [JsonPropertyName("logit_bias")] public Dictionary<string, float>? LogitBias { get; init; }

    [JsonPropertyName("logprobs")] public bool? LogProbs { get; init; }

    [JsonPropertyName("max_completion_tokens")]
    public int? MaxCompletionTokens { get; init; }

    [JsonPropertyName("max_tokens")] public int? MaxTokens { get; init; }

    [JsonPropertyName("metadata")] public Dictionary<string, string>? Metadata { get; init; }

    [JsonPropertyName("modalities")] public List<string>? Modalities { get; init; }

    [JsonPropertyName("n")] public int? N { get; init; } = 1;

    [JsonPropertyName("parallel_tool_calls")]
    public bool? ParallelToolCalls { get; init; }

    [JsonPropertyName("presence_penalty")] public float? PresencePenalty { get; init; }

    [JsonPropertyName("prediction")] public object? Prediction { get; init; }

    [JsonPropertyName("reasoning_effort")] public string? ReasoningEffort { get; init; }

    [JsonPropertyName("response_format")] public OpenAiResponseFormat? ResponseFormat { get; init; }

    [JsonPropertyName("seed")] public int? Seed { get; init; } = 1;

    [JsonPropertyName("service_tier")] public string? ServiceTier { get; init; }

    [JsonPropertyName("stop")] public object? Stop { get; init; }

    [JsonPropertyName("store")] public bool? Store { get; init; } = false;

    [JsonPropertyName("stream_options")] public object? StreamOptions { get; init; }

    [JsonPropertyName("temperature")] public float? Temperature { get; init; }

    [JsonPropertyName("top_logprobs")] public int? TopLogprobs { get; init; }

    [JsonPropertyName("top_p")] public float? TopP { get; init; } = 1;

    [JsonPropertyName("user")] public string? User { get; init; }

    [JsonPropertyName("web_search_options")]
    public object? WebSearchOptions { get; init; }

    [JsonPropertyName("cache_prompt")] public bool? CachePrompt { get; init; }

    [JsonPropertyName("samplers")] public string? Samplers { get; init; }

    [JsonPropertyName("dynatemp_range")] public float? DynatempRange { get; init; }

    [JsonPropertyName("dynatemp_exponent")]
    public float? DynatempExponent { get; init; }

    [JsonPropertyName("top_k")] public int? TopK { get; init; }

    [JsonPropertyName("min_p")] public float? MinP { get; init; }

    [JsonPropertyName("typical_p")] public float? TypicalP { get; init; }

    [JsonPropertyName("xtc_probability")] public float? XtcProbability { get; init; }

    [JsonPropertyName("xtc_threshold")] public float? XtcThreshold { get; init; }

    [JsonPropertyName("repeat_last_n")] public int? RepeatLastN { get; init; }

    [JsonPropertyName("repeat_penalty")] public float? RepeatPenalty { get; init; }

    [JsonPropertyName("dry_multiplier")] public float? DryMultiplier { get; init; }

    [JsonPropertyName("dry_base")] public float? DryBase { get; init; }

    [JsonPropertyName("dry_allowed_length")]
    public int? DryAllowedLength { get; init; }

    [JsonPropertyName("dry_penalty_last_n")]
    public int? DryPenaltyLastN { get; init; }

    [JsonPropertyName("timings_per_token")]
    public bool? TimingsPerToken { get; init; }
}

public readonly record struct OpenAiChatCompletionResponse()
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("created")] public long Created { get; init; }

    [JsonPropertyName("model")] public string? Model { get; init; }

    [JsonPropertyName("choices")] public List<OpenAiChatCompletionChoice> Choices { get; init; } = [];

    [JsonPropertyName("usage")] public OpenAiChatCompletionUsage? Usage { get; init; }
}

public readonly record struct OpenAiChatCompletionResponseDelta()
{
    [JsonPropertyName("id")] public string? Id { get; init; }

    [JsonPropertyName("object")] public string? Object { get; init; }

    [JsonPropertyName("created")] public long Created { get; init; }

    [JsonPropertyName("model")] public string? Model { get; init; }

    [JsonPropertyName("choices")] public List<OpenAiChatCompletionDelta> Choices { get; init; } = [];

    [JsonPropertyName("usage")] public OpenAiChatCompletionUsage? Usage { get; init; }
}

public readonly record struct OpenAiChatCompletionUsage
{
    [JsonPropertyName("prompt_tokens")] public int PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; init; }

    [JsonPropertyName("total_tokens")] public int TotalTokens { get; init; }
}
