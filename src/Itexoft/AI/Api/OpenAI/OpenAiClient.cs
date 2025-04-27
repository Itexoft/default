// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.AI.Common;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Net.Http;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI;

public sealed class OpenAiClient : IDisposable
{
    private readonly OpenAiApiExecutor executor;
    private Disposed disposed = new();
    private DeferredPool<NetHttpClient> httpClientPool;

    public OpenAiClient(string baseUrl, string? apiKey = null) : this(
        new OpenAiClientOptions
        {
            BaseUri = new Uri(baseUrl.RequiredNotWhiteSpace(), UriKind.Absolute),
            ApiKey = apiKey,
        }) { }

    public OpenAiClient(OpenAiClientOptions options)
    {
        options.Required();
        var baseUri = ValidateBaseUri(options.BaseUri);

        this.httpClientPool = new DeferredPool<NetHttpClient>(
            50,
            () =>
            {
                var client = new NetHttpClient(baseUri);

                if (!string.IsNullOrWhiteSpace(options.ApiKey))
                    client.DefaultHeaders.Authorization = $"Bearer {options.ApiKey}";

                if (!string.IsNullOrWhiteSpace(options.UserAgent))
                    client.DefaultHeaders.UserAgent = options.UserAgent;

                client.DefaultHeaders.Accept.Set(new NetHttpContentType(NetHttpContentType.ApplicationJson));

                return client;
            });

        var basePath = NormalizeBasePath(baseUri.AbsolutePath);
        this.executor = new OpenAiApiExecutor(this.httpClientPool, basePath, options.RequestTimeout, options.RetryPolicy);
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        foreach (var value in this.httpClientPool.GetCreatedValues())
            value.Dispose();
    }

    public OpenAiInferenceClient GetInferenceClient(string? model = null) => new(this.executor, model);

    public OpenAiInferenceClient GetInferenceClient(int maxAttempts, string? model = null) =>
        new(this.executor.WithRetryPolicy(CreateAttemptsPolicy(maxAttempts)), model);

    public OpenAiInferenceClient GetChatCompletionsClient(string? model = null) => this.GetInferenceClient(model);

    public OpenAiInferenceClient GetChatCompletionsClient(int maxAttempts, string? model = null) =>
        this.GetInferenceClient(maxAttempts, model);

    public OpenAiEmbeddingsClient GetEmbeddingClient(string? model = null) => new(this.executor, model);

    public OpenAiEmbeddingsClient GetEmbeddingsClient(IEmbeddingCache cache, string? model = null, int maxAttempts = 0) =>
        maxAttempts == 0 ? new(this.executor, model, cache) : new(this.executor.WithRetryPolicy(CreateAttemptsPolicy(maxAttempts)), model, cache);

    public OpenAiEmbeddingsClient GetEmbeddingsClient(string? model = null, int maxAttempts = 0) =>
        maxAttempts == 0 ? new(this.executor, model) : new(this.executor.WithRetryPolicy(CreateAttemptsPolicy(maxAttempts)), model);

    private static Uri ValidateBaseUri(Uri baseUri)
    {
        baseUri.Required();

        if (!baseUri.IsAbsoluteUri)
            throw new ArgumentException("OpenAi base URI must be absolute.", nameof(baseUri));

        if (!baseUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !baseUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("OpenAi base URI must use HTTP or HTTPS.", nameof(baseUri));

        if (!string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment))
            throw new ArgumentException("OpenAi base URI must not include query or fragment.", nameof(baseUri));

        return baseUri;
    }

    private static string NormalizeBasePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || path == "/")
            return string.Empty;

        var result = path.Trim();

        if (result[0] != '/')
            result = $"/{result}";

        if (result.Length > 1 && result[^1] == '/')
            result = result[..^1];

        return result;
    }

    private static RetryPolicy CreateAttemptsPolicy(int maxAttempts)
    {
        if (maxAttempts < 0)
            throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        if (maxAttempts == 0)
            return RetryPolicy.None;

        return RetryPolicy.Limit((ulong)maxAttempts);
    }
}
