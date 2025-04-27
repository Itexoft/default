// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.AI.Common;
using Itexoft.AI.OpenAI.Models;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI;

public sealed class OpenAiEmbeddingsClient
{
    private readonly IEmbeddingCache cache;
    private readonly OpenAiApiExecutor executor;
    private readonly string? model;

    internal OpenAiEmbeddingsClient(OpenAiApiExecutor executor, string? model) : this(executor, model, new MemoryEmbeddingCache()) { }

    internal OpenAiEmbeddingsClient(OpenAiApiExecutor executor, string? model, IEmbeddingCache cache)
    {
        this.cache = cache.Required();
        this.executor = executor.Required();
        this.model = string.IsNullOrWhiteSpace(model) ? null : model;
    }

    private OpenAiEmbeddingsResponse GetEmbeddingsResponse(string[] input, CancelToken cancelToken = default)
    {
        input.RequiredNotEmpty();

        if (input.Any(string.IsNullOrEmpty))
            throw new ArgumentException("OpenAi embedding input item cannot be empty.", nameof(input));

        return this.GetEmbeddings(
            new OpenAiEmbeddingsRequest
            {
                Model = this.model,
                Input = input,
            },
            cancelToken);
    }

    public OpenAiEmbeddingsResponse GetEmbeddingsResponse(string input, CancelToken cancelToken = default) =>
        this.GetEmbeddingsResponse([input.Required()], cancelToken);

    public ReadOnlySpan<float> GetEmbedding(string input, CancelToken cancelToken = default)
    {
        input.Required();

        if (string.IsNullOrEmpty(input))
            throw new ArgumentException("OpenAi embedding input item cannot be empty.", nameof(input));

        if (this.cache.TryGet(input, out var embedding))
            return embedding.Span;

        return this.RequestSingleEmbedding(input, cancelToken);
    }

    public ReadOnlyMemory<float>[] GetEmbeddings(string[] input, CancelToken cancelToken = default)
    {
        ValidateInput(input);
        var result = new ReadOnlyMemory<float>[input.Length];
        var missCount = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (this.cache.TryGet(input[i], out var embedding))
                result[i] = embedding;
            else
                missCount++;
        }

        if (missCount == 0)
            return result;

        if (missCount == input.Length)
            return this.RequestEmbeddings(input, cancelToken);

        var missInput = new string[missCount];
        var missIndices = new int[missCount];
        var missCursor = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (result[i].IsEmpty)
                continue;

            missInput[missCursor] = input[i];
            missIndices[missCursor] = i;
            missCursor++;
        }

        var response = this.GetEmbeddingsResponse(missInput, cancelToken);

        if (response.Data.Count != missInput.Length)
        {
            throw new InvalidDataException(
                $"OpenAi embeddings response vector count mismatch. Expected {missInput.Length}, got {response.Data.Count}.");
        }

        for (var i = 0; i < response.Data.Count; i++)
        {
            var entry = response.Data[i];

            if ((uint)entry.Index >= (uint)missInput.Length)
            {
                throw new InvalidDataException(
                    $"OpenAi embeddings response index is out of range. Got {entry.Index}, expected less than {missInput.Length}.");
            }

            var embedding = entry.Embedding;
            var outputIndex = missIndices[entry.Index];
            result[outputIndex] = embedding;
            _ = this.cache.TryAdd(missInput[entry.Index], embedding);
        }

        return this.RequestEmbeddings(input, cancelToken);
    }

    public ReadOnlyMemory<float>[] GetEmbeddings(IEnumerable<string> input, CancelToken cancelToken = default)
    {
        input.Required();

        return this.GetEmbeddings(input as string[] ?? [.. input], cancelToken);
    }

    public OpenAiEmbeddingsResponse GetEmbeddings(OpenAiEmbeddingsRequest request, CancelToken cancelToken = default)
    {
        request.Required();

        if (request.Input.Length == 0)
            throw new ArgumentException("OpenAi embeddings request input cannot be empty.", nameof(request));

        if (request.Input.Any(string.IsNullOrEmpty))
            throw new ArgumentException("OpenAi embeddings request input item cannot be enpty.", nameof(request));

        var effectiveRequest = request with
        {
            Model = ResolveModel(request.Model, this.model),
        };

        return this.executor.PostJson<OpenAiEmbeddingsResponse>("embeddings", effectiveRequest, cancelToken);
    }

    private static string ResolveModel(string? requestedModel, string? fallbackModel)
    {
        if (!string.IsNullOrWhiteSpace(requestedModel))
            return requestedModel;

        if (!string.IsNullOrWhiteSpace(fallbackModel))
            return fallbackModel;

        throw new InvalidOperationException("OpenAi model is not specified for embeddings request.");
    }

    private static void ValidateInput(string[] input)
    {
        input.RequiredNotEmpty();

        if (input.Any(string.IsNullOrEmpty))
            throw new ArgumentException("OpenAi embedding input item cannot be empty.", nameof(input));
    }

    private float[] RequestSingleEmbedding(string input, CancelToken cancelToken)
    {
        var response = this.GetEmbeddingsResponse(input, cancelToken);

        if (response.Data.Count != 1)
            throw new InvalidDataException($"OpenAi embeddings response vector count mismatch. Expected 1, got {response.Data.Count}.");

        var entry = response.Data[0];

        if (entry.Index != 0)
            throw new InvalidDataException($"OpenAi embeddings response index is out of range. Got {entry.Index}, expected 0.");

        var embedding = entry.Embedding;
        _ = this.cache.TryAdd(input, embedding);

        return embedding;
    }

    private ReadOnlyMemory<float>[] RequestEmbeddings(string[] input, CancelToken cancelToken)
    {
        var response = this.GetEmbeddingsResponse(input, cancelToken);

        if (response.Data.Count != input.Length)
            throw new InvalidDataException($"OpenAi embeddings response vector count mismatch. Expected {input.Length}, got {response.Data.Count}.");

        var result = new ReadOnlyMemory<float>[input.Length];

        for (var i = 0; i < response.Data.Count; i++)
        {
            var entry = response.Data[i];

            if ((uint)entry.Index >= (uint)input.Length)
            {
                throw new InvalidDataException(
                    $"OpenAi embeddings response index is out of range. Got {entry.Index}, expected less than {input.Length}.");
            }

            var embedding = entry.Embedding;
            result[entry.Index] = embedding;
            _ = this.cache.TryAdd(input[entry.Index], embedding);
        }

        return result;
    }
}
