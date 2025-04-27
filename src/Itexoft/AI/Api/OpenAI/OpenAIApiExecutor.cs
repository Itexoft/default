// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.AI.Api.OpenAI.Models;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.AI.Api.OpenAI;

internal sealed class OpenAiApiExecutor
{
    private readonly string basePath;
    private readonly NetHttpClient client;
    private readonly TimeSpan requestTimeout;
    private readonly RetryPolicy retryPolicy;
    private AtomicLock atomicLock = new();

    internal OpenAiApiExecutor(NetHttpClient client, string basePath, TimeSpan requestTimeout, RetryPolicy retryPolicy)
    {
        this.client = client.Required();
        this.basePath = NormalizeBasePath(basePath);
        this.requestTimeout = requestTimeout;
        this.retryPolicy = retryPolicy;
    }

    public OpenAiApiExecutor WithRetryPolicy(RetryPolicy retryPolicy) =>
        new(this.client, this.basePath, this.requestTimeout, retryPolicy);

    public TResponse PostJson<TResponse>(string path, object payload, CancelToken cancelToken = default)
    {
        path = path.RequiredNotWhiteSpace();
        payload.Required();
        var payloadBytes = OpenAiJson.Serialize(payload);

        using (this.atomicLock.Enter())
            return this.retryPolicy.Run<TResponse>(token => this.PostJsonCore<TResponse>(path, payloadBytes, token), cancelToken);
    }

    public IEnumerable<OpenAiChatCompletionResponseDelta> PostJsonAsStream(string path, object payload, CancelToken cancelToken = default)
    {
        path = path.RequiredNotWhiteSpace();
        payload.Required();
        var payloadBytes = OpenAiJson.Serialize(payload);

        try
        {
            this.atomicLock.Enter();

            var response = this.retryPolicy.Run(
                cancelToken =>
                {
                    try
                    {
                        var response = this.PostRaw(path, payloadBytes, cancelToken);

                        if (!response.IsSuccess)
                        {
                            var responsePayload = response.ReadAsBytes(cancelToken);

                            throw CreateApiException(path, response, responsePayload);
                        }

                        return response;
                    }
                    catch
                    {
                        this.client.Disconnect();

                        throw;
                    }
                },
                cancelToken);

            //var result = Encoding.UTF8.GetString((await response.Body!.ReadToEndAsync()).Span);
            using var enumerator = new OpenAiSseEnumerable(response, cancelToken).GetEnumerator();

            for (;;)
            {
                try
                {
                    if (!enumerator.MoveNext())
                        break;
                }
                catch
                {
                    this.client.Disconnect();

                    throw;
                }

                yield return enumerator.Current;
            }
        }
        finally
        {
            this.atomicLock.Exit();
        }
    }

    private TResponse PostJsonCore<TResponse>(string path, byte[] payload, CancelToken cancelToken)
    {
        try
        {
            var response = this.PostRaw(path, payload, cancelToken);

            var responsePayload = response.ReadAsBytes(cancelToken);

            if (!response.IsSuccess)
                throw CreateApiException(path, response, responsePayload);

            return OpenAiJson.Deserialize<TResponse>(responsePayload.Span);
        }
        catch
        {
            this.client.Disconnect();

            throw;
        }
    }

    private NetHttpResponse PostRaw(string path, byte[] payload, CancelToken cancelToken)
    {
        var request = new NetHttpRequest(NetHttpMethod.Post, this.BuildPath(path))
        {
            Content = new StreamTrs<byte>(payload),
            Timeout = this.requestTimeout,
            Headers = new NetHttpHeaders
            {
                ContentType = OpenAiJson.ContentType,
            },
        };

        return this.client.Send(request, cancelToken);
    }

    private string BuildPath(string path)
    {
        if (path[0] != '/')
            path = $"/{path}";

        if (this.basePath.Length == 0)
            return path;

        return $"{this.basePath}{path}";
    }

    private static string NormalizeBasePath(string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath) || basePath == "/")
            return string.Empty;

        var value = basePath.Trim();

        if (value[0] != '/')
            value = $"/{value}";

        if (value.Length > 1 && value[^1] == '/')
            value = value[..^1];

        return value;
    }

    private static OpenAiApiException CreateApiException(string path, NetHttpResponse response, ReadOnlyMemory<byte> responsePayload)
    {
        var body = responsePayload.IsEmpty ? null : Encoding.UTF8.GetString(responsePayload.Span);

        return new OpenAiApiException((int)response.Status, path, body);
    }
}
