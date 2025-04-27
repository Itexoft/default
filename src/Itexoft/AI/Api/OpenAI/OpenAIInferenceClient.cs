// Copyright Aspose (c) Denis Kudelin

using System.Text;
using Itexoft.AI.Api.OpenAI.Models;
using Itexoft.Extensions;
using Itexoft.IO.Streams;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.AI.Api.OpenAI;

public sealed class OpenAiInferenceClient
{
    private readonly OpenAiApiExecutor executor;
    private readonly string? model;

    internal OpenAiInferenceClient(OpenAiApiExecutor executor, string? model)
    {
        this.executor = executor.Required();
        this.model = string.IsNullOrWhiteSpace(model) ? null : model;
    }

    public OpenAiChatCompletionResponse GetChatCompletionResponse(OpenAiChatCompletionRequest request, CancelToken cancelToken = default)
    {
        request.Required();

        if (request.Messages.Count == 0)
            throw new ArgumentException("OpenAI chat completion request must contain at least one message.", nameof(request));

        var effectiveRequest = request with
        {
            Model = ResolveModel(request.Model, this.model),
        };

        return this.executor.PostJson<OpenAiChatCompletionResponse>("chat/completions", effectiveRequest, cancelToken);
    }

    public PipeStream<(bool Reasoning, char Char)> GetChatCompletionPipe(OpenAiChatCompletionRequest request, CancelToken cancelToken)
    {
        var result = new PipeStream<(bool, char)>(1024);

        _ = Promise.Run(
            () =>
            {
                try
                {
                    request = request with { Stream = true, Model = string.IsNullOrEmpty(request.Model) ? this.model : request.Model };
                    var chunks = this.executor.PostJsonAsStream("chat/completions", request, cancelToken);

                    foreach (var chunk in chunks)
                    {
                        foreach (var choice in chunk.Choices)
                        {
                            if (string.IsNullOrEmpty(choice.Delta.ReasoningContent) && string.IsNullOrEmpty(choice.Delta.Content))
                                continue;

                            var reasoning = string.IsNullOrEmpty(choice.Delta.Content);

                            var text = reasoning ? choice.Delta.ReasoningContent! : choice.Delta.Content!;

                            if (string.IsNullOrEmpty(text))
                                continue;

                            var memory = new (bool, char)[text.Length].AsSpan();

                            for (var i = 0; i < memory.Length; i++)
                                memory[i] = (reasoning, text[i]);

                            result.Write(memory, cancelToken);
                        }
                    }
                }
                catch
                {
                    // ignored
                }
                finally
                {
                    result.Valve = 0;
                }
            },
            false,
            cancelToken);

        return result;
    }

    public string GetChatCompletion(string prompt, CancelToken cancelToken = default)
    {
        var response = this.GetChatCompletionResponse(
            this.BuildRequest([new OpenAiChatCompletionMessage("user") { Content = prompt.Required() }]),
            cancelToken);

        return GetChatCompletionText((OpenAiChatCompletionResponse)response);
    }

    public T GetChatCompletion<T>(string prompt, CancelToken cancelToken = default)
    {
        var request = this.BuildRequest(
            [new OpenAiChatCompletionMessage("user") { Content = prompt.Required() }],
            null,
            OpenAiResponseFormats.JsonSchema<T>());

        var response = this.GetChatCompletionResponse(request, cancelToken);
        var text = GetChatCompletionText((OpenAiChatCompletionResponse)response);

        return OpenAiJson.Deserialize<T>(text);
    }

    public IEnumerable<OpenAiChatCompletionResponseDelta> GetChatCompletionStream(
        OpenAiChatCompletionRequest request,
        CancelToken cancelToken = default)
    {
        request.Required();

        if (request.Messages.Count == 0)
            throw new ArgumentException("OpenAI chat completion request must contain at least one message.", nameof(request));

        var effectiveRequest = request with
        {
            Model = ResolveModel(request.Model, this.model),
            Stream = true,
        };

        return this.executor.PostJsonAsStream("chat/completions", effectiveRequest, cancelToken);
    }

    private OpenAiChatCompletionRequest BuildRequest(
        OpenAiChatCompletionMessage[] messages,
        int? maxCompletionTokens = null,
        OpenAiResponseFormat? responseFormat = null)
    {
        messages.RequiredNotEmpty();

        return new OpenAiChatCompletionRequest
        {
            Model = this.model,
            Messages = [.. messages],
            ResponseFormat = responseFormat,
            MaxCompletionTokens = maxCompletionTokens,
        };
    }

    private static string GetChatCompletionText(OpenAiChatCompletionResponse response)
    {
        response.Required();

        if (response.Choices.Count == 0)
            throw new InvalidDataException("OpenAI chat completion response does not contain choices.");

        var builder = new StringBuilder();

        foreach (var choice in response.Choices)
        {
            var text = choice.Message.Content;

            if (!string.IsNullOrEmpty(text))
                builder.Append(text);
        }

        if (builder.Length == 0)
            throw new InvalidDataException("OpenAI chat completion response does not contain textual content.");

        return builder.ToString();
    }

    private static string ResolveModel(string? requestedModel, string? fallbackModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
            return requestedModel;

        if (!string.IsNullOrWhiteSpace(fallbackModel))
            return fallbackModel;

        throw new InvalidOperationException("OpenAI model is not specified for chat completion request.");
    }
}
