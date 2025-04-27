// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.AI.Api.OpenAI.Models;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI;

public sealed class OpenAiEmbeddingsClient
{
    private readonly OpenAiApiExecutor executor;
    private readonly string? model;

    internal OpenAiEmbeddingsClient(OpenAiApiExecutor executor, string? model)
    {
        this.executor = executor.Required();
        this.model = string.IsNullOrWhiteSpace(model) ? null : model;
    }

    private OpenAiEmbeddingsResponse GetEmbeddingsResponse(string[] input, CancelToken cancelToken = default)
    {
        input.RequiredNotEmpty();

        if (input.Any(string.IsNullOrEmpty))
            throw new ArgumentException("OpenAI embedding input item cannot be empty.", nameof(input));

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

    public float[] GetEmbedding(string input, CancelToken cancelToken = default)
    {
        var matrix = this.GetEmbeddings([input], cancelToken);

        return matrix[0];
    }

    public float[][] GetEmbeddings(string[] input, CancelToken cancelToken = default)
    {
        var response = this.GetEmbeddingsResponse(input, cancelToken);

        if (response.Data.Count != input.Length)
            throw new InvalidDataException($"OpenAI embeddings response vector count mismatch. Expected {input.Length}, got {response.Data.Count}.");

        var embeddings = new float[input.Length][];

        foreach (var entry in response.Data)
        {
            if ((uint)entry.Index >= (uint)embeddings.Length)
                throw new InvalidDataException($"OpenAI embeddings response contains out-of-range index {entry.Index}.");

            if (embeddings[entry.Index] is not null)
                throw new InvalidDataException($"OpenAI embeddings response contains duplicate index {entry.Index}.");

            embeddings[entry.Index] = entry.Embedding;
        }

        for (var i = 0; i < embeddings.Length; i++)
        {
            if (embeddings[i] is null)
                throw new InvalidDataException($"OpenAI embeddings response is missing vector index {i}.");
        }

        return embeddings;
    }

    public OpenAiEmbeddingsResponse GetEmbeddings(OpenAiEmbeddingsRequest request, CancelToken cancelToken = default)
    {
        request.Required();

        if (request.Input.Length == 0)
            throw new ArgumentException("OpenAI embeddings request input cannot be empty.", nameof(request));

        if (request.Input.Any(string.IsNullOrEmpty))
            throw new ArgumentException("OpenAI embeddings request input item cannot be enpty.", nameof(request));

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

        throw new InvalidOperationException("OpenAI model is not specified for embeddings request.");
    }
}
