// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.AI.OpenAI.Models;
using Itexoft.Extensions;
using Itexoft.Formats.Dkon;
using Itexoft.Formats.Dkon.Internal;
using Itexoft.IO;
using Itexoft.IO.Streams;
using Itexoft.Text.Random;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI.Dkon;

public sealed class DkonInferenceClient(DkonFormat dkonFormat, OpenAiInferenceClient inferenceClient)
{
    private readonly OpenAiInferenceClient inferenceClient = inferenceClient.Required();
    internal DkonFormat Format { get; } = dkonFormat.Required();

    public TResponse GetChatCompletion<TResponse>(
        string prompt,
        object request,
        Settings settings,
        bool decompose,
        CancelToken cancelToken = default) =>
        this.GetChatCompletion<TResponse>(prompt, request, settings, decompose, out _, cancelToken);

    public TResponse GetChatCompletion<TResponse>(
        string prompt,
        object request,
        Settings settings,
        bool decompose,
        out string dkon,
        CancelToken cancelToken = default) => this.GetDirectChatCompletion<TResponse>(prompt, request, settings, out dkon, cancelToken);

    private TResponse GetDirectChatCompletion<TResponse>(string prompt, object request, Settings settings, out string dkon, CancelToken cancelToken)
    {
        var contract = this.Format.GetContract<TResponse>(out var syntaxLevel);
        var userPrompt = BuildUserPrompt(prompt, this.Format.Serialize(request.Required(), true), contract);

        var systemPrompt = BuildPromptState(syntaxLevel);

        var chatRequest = new OpenAiChatCompletionRequest
        {
            Messages = [new OpenAiChatCompletionMessage("system", systemPrompt.text), new OpenAiChatCompletionMessage("user", userPrompt)],
            Temperature = settings.Temperature,
            TopP = settings.TopP,
            Seed = settings.Seed,
            N = settings.N,
            MaxCompletionTokens = settings.MaxCompletionTokens,
            MaxTokens = settings.MaxTokens,
            ReasoningEffort = settings.ReasoningEffort,
        };

        var (result, dkonResult) = this.inferenceClient.RetryPolicy.Run(ct =>
        {
            using var pipe = this.inferenceClient.GetChatCompletionPipe(chatRequest, cancelToken);

            if (!DkonInferenceClientUtils.TryExtractDkon(pipe.AsEnumerable(cancelToken), systemPrompt.token, out var dkon, out var failure))
                throw new FormatException(failure + "\n\n" + userPrompt);

            dkon = dkon.Trim();

            try
            {
                var result = this.Format.Deserialize<TResponse>(dkon);

                return (result!, dkon);
            }
            catch (Exception ex)
            {
                throw new FormatException($"Invalid DKON response body.\nExtracted body:\n{dkon}\nDetails:\n{ex}");
            }
        });

        dkon = dkonResult;

        return result;
    }

    internal static int GetPromptLength(DkonFormat format, Type responseType, string prompt, object request)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(responseType);
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(request);

        var contract = format.GetContract(responseType, out var syntaxLevel);
        var userPrompt = BuildUserPrompt(prompt, format.Serialize(request, true), contract);
        var systemPrompt = BuildPromptState(syntaxLevel);

        return systemPrompt.text.Length + userPrompt.Length;
    }

    private static string BuildUserPrompt(string prompt, string inputDkon, string contract) =>
        $"""
         {
             prompt
         }

         INPUT:
         {
             inputDkon
         }

         OUTPUT CONTRACT:{
             contract
         }
         """;

    private static (string text, string token) BuildPromptState(DkonSyntaxLevel syntaxLevel)
    {
        var token = RandomString.Create(5);

        return new(
            $"""
             Return exactly one valid DKON document wrapped by the sentinels below.
             Prose before or after the sentinels is ignored.
             Rules:
             1. Emit exactly one begin sentinel and exactly one end sentinel.
             2. Do not repeat either sentinel inside the DKON document.
             3. The DKON document starts immediately after |{
                 token
             }|
                and ends immediately before |{
                    token
                }|.
             4. Do not repeat or restate the input request.

             Example:
             |{
                 token
             }|
             <valid DKON document>
             |{
                 token
             }|

             Documentation:
             {
                 DkonFormat.GetDoc(syntaxLevel)
             }
             """,
            token);
    }

    public readonly record struct Settings()
    {
        public int? Temperature { get; init; } = 0;
        public int? TopP { get; init; } = 1;
        public int? Seed { get; init; } = 1;
        public int? N { get; init; } = 1;
        public int? MaxCompletionTokens { get; init; }
        public int? MaxTokens { get; init; }
        public string? ReasoningEffort { get; init; }
    }
}

file static class DkonInferenceClientUtils
{
    internal static bool TryExtractDkon(IEnumerable<(bool Reasoning, char Char)> content, string token, out string dkon, out string failure)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(token);

        var boundary = $"|{token}|";
        var memory = new DynamicMemory<char>(256);

        try
        {
            foreach (var item in content)
            {
                if (item.Reasoning)
                    continue;

                memory.Write(item.Char);
            }

            var preview = new string(memory.AsSpan());

            var startIndex = preview.IndexOf(boundary, StringComparison.OrdinalIgnoreCase);
            var endIndex = preview.LastIndexOf(boundary, StringComparison.OrdinalIgnoreCase);

            if (startIndex == -1 || endIndex == -1)
            {
                failure = $"Invalid DKON response.\n\n{preview}";
                dkon = string.Empty;

                return false;
            }

            var contentStart = startIndex + boundary.Length;

            if (endIndex <= contentStart)
            {
                failure = $"Invalid DKON response.\n\n{preview}";
                dkon = string.Empty;

                return false;
            }

            dkon = preview[contentStart..endIndex];
            failure = string.Empty;

            return true;
        }
        finally
        {
            memory.Dispose();
        }
    }
}
