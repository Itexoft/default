// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using Itexoft.AI.OpenAI;
using Itexoft.AI.OpenAI.Models;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.AI.Server.OpenAi;

public sealed class OpenAiServer : IDisposable
{
    private const string embeddingsPath = "/v1/embeddings";
    private const string modelsPath = "/v1/models";
    private const string chatCompletionsPath = "/v1/chat/completions";
    private readonly OpenAiServerOptions options;

    private readonly NetHttpServer server;
    private Promise completion = Promise.Completed;
    private OpenAiServerStreamFactory? createStream;
    private IOpenAiServerHandler? handler;
    private CancelToken runToken;
    private Disposed started;

    public OpenAiServer(OpenAiServerOptions options)
    {
        this.options = options;
        this.server = new NetHttpServer(options.Endpoint);
        this.server.Http(this.HandleRequest);
    }

    public NetIpEndpoint Endpoint => this.options.Endpoint;

    private OpenAiServerAuthorize? Authorize => this.options.Authorize;

    private OpenAiServerStreamFactory CreateStream =>
        this.createStream ?? throw new InvalidOperationException("OpenAi server stream factory is not set.");

    private IOpenAiServerHandler Handler =>
        this.handler ?? throw new InvalidOperationException("OpenAi server handler is not set.");

    public void Dispose() => this.Stop();

    public void Stream(OpenAiServerStreamFactory createStream)
    {
        createStream.Required();

        if (this.started)
            throw new InvalidOperationException("Server already started.");

        if (this.createStream is not null)
            throw new InvalidOperationException("OpenAi server stream factory already set.");

        this.createStream = createStream;
    }

    public void Handle(IOpenAiServerHandler handler)
    {
        handler.Required();

        if (this.started)
            throw new InvalidOperationException("Server already started.");

        if (this.handler is not null)
            throw new InvalidOperationException("OpenAi server handler already set.");

        this.handler = handler;
    }

    public void Start(CancelToken cancelToken = default)
    {
        if (this.started.Enter())
            throw new InvalidOperationException("Server already started.");

        this.runToken = CancelToken.New();

        if (!cancelToken.IsNone)
            cancelToken.Register(() => this.runToken.Cancel());

        this.completion = this.server.Start(this.runToken);
    }

    public void Stop(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.runToken.Cancel();
        this.server.Dispose();
        cancelToken.ThrowIf();

        if (!this.completion.IsCompleted)
            this.completion.GetAwaiter().GetResult();
    }

    private NetHttpResponse HandleRequest(NetHttpRequest request, CancelToken cancelToken)
    {
        try
        {
            if (!TryMatchRoute(request.PathAndQuery.Path, out var route))
                return CreateErrorResponse(NetHttpStatus.NotFound, "Route was not found.");

            if (!TryMatchMethod(route, request.Method))
                return CreateErrorResponse(NetHttpStatus.MethodNotAllowed, "Method is not allowed for this route.");

            if (!this.TryAuthorize(request, cancelToken, out var response))
                return response;

            return route switch
            {
                OpenAiServerRoute.Models => this.HandleModels(cancelToken),
                OpenAiServerRoute.Embeddings => this.HandleEmbeddings(request, cancelToken),
                OpenAiServerRoute.ChatCompletions => this.HandleChatCompletions(request, cancelToken),
                _ => CreateErrorResponse(NetHttpStatus.NotFound, "Route was not found."),
            };
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested)
        {
            throw;
        }
        catch
        {
            return CreateErrorResponse(NetHttpStatus.InternalServerError, "Internal server error.");
        }
    }

    private NetHttpResponse HandleModels(CancelToken cancelToken)
    {
        IStreamRw<char>? stream = null;

        try
        {
            stream = this.CreateStream(cancelToken);
            var response = this.Handler.GetModels(stream, cancelToken);
            stream.Dispose();

            return CreateDtoResponse(NetHttpStatus.Ok, response);
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested)
        {
            stream?.Dispose();

            throw;
        }
        catch
        {
            stream?.Dispose();

            return CreateErrorResponse(NetHttpStatus.InternalServerError, "Internal server error.");
        }
    }

    private NetHttpResponse HandleEmbeddings(NetHttpRequest request, CancelToken cancelToken)
    {
        if (!TryDeserializeJson(request, out OpenAiEmbeddingsRequest payload, out var errorResponse))
            return errorResponse;

        if (!IsValid(payload))
            return CreateErrorResponse(NetHttpStatus.BadRequest, "Embeddings request is invalid.");

        IStreamRw<char>? stream = null;

        try
        {
            stream = this.CreateStream(cancelToken);
            var response = this.Handler.CreateEmbeddings(payload, stream, cancelToken);
            stream.Dispose();

            return CreateDtoResponse(NetHttpStatus.Ok, response);
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested)
        {
            stream?.Dispose();

            throw;
        }
        catch
        {
            stream?.Dispose();

            return CreateErrorResponse(NetHttpStatus.InternalServerError, "Internal server error.");
        }
    }

    private NetHttpResponse HandleChatCompletions(NetHttpRequest request, CancelToken cancelToken)
    {
        if (!TryDeserializeJson(request, out OpenAiChatCompletionRequest payload, out var errorResponse))
            return errorResponse;

        if (!IsValid(payload))
            return CreateErrorResponse(NetHttpStatus.BadRequest, "Chat completion request is invalid.");

        IStreamRw<char>? stream = null;

        try
        {
            stream = this.CreateStream(cancelToken);

            if (payload.Stream)
            {
                var deltas = this.Handler.StreamChat(payload with { Stream = true }, stream, cancelToken)
                             ?? throw new InvalidOperationException("OpenAi server stream handler returned null.");

                return CreateSseResponse(deltas, stream);
            }

            var response = this.Handler.CompleteChat(payload with { Stream = false }, stream, cancelToken);
            stream.Dispose();

            return CreateDtoResponse(NetHttpStatus.Ok, response);
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested)
        {
            stream?.Dispose();

            throw;
        }
        catch
        {
            stream?.Dispose();

            return CreateErrorResponse(NetHttpStatus.InternalServerError, "Internal server error.");
        }
    }

    private bool TryAuthorize(NetHttpRequest request, CancelToken cancelToken, out NetHttpResponse response)
    {
        if (!TryGetBearerToken(request.Headers.Authorization, out var token))
        {
            response = CreateUnauthorizedResponse("Authorization header must be 'Bearer <token>'.");

            return false;
        }

        try
        {
            if (this.Authorize?.Invoke(token, cancelToken) ?? true)
            {
                response = default;

                return true;
            }

            response = CreateUnauthorizedResponse("API key is not authorized.");

            return false;
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested)
        {
            throw;
        }
        catch
        {
            response = CreateErrorResponse(NetHttpStatus.InternalServerError, "Internal server error.");

            return false;
        }
    }

    private static bool TryMatchRoute(string path, out OpenAiServerRoute route)
    {
        if (path.Equals(chatCompletionsPath, StringComparison.Ordinal))
        {
            route = OpenAiServerRoute.ChatCompletions;

            return true;
        }

        if (path.Equals(embeddingsPath, StringComparison.Ordinal))
        {
            route = OpenAiServerRoute.Embeddings;

            return true;
        }

        if (path.Equals(modelsPath, StringComparison.Ordinal))
        {
            route = OpenAiServerRoute.Models;

            return true;
        }

        route = default;

        return false;
    }

    private static bool TryMatchMethod(OpenAiServerRoute route, NetHttpMethod method) => route switch
    {
        OpenAiServerRoute.ChatCompletions => method == NetHttpMethod.Post,
        OpenAiServerRoute.Embeddings => method == NetHttpMethod.Post,
        OpenAiServerRoute.Models => method == NetHttpMethod.Get,
        _ => false,
    };

    private static bool TryGetBearerToken(string? authorization, out string token)
    {
        token = string.Empty;

        if (string.IsNullOrEmpty(authorization))
            return false;

        var span = authorization.AsSpan();
        const string prefix = "Bearer ";

        if (span.Length <= prefix.Length || !span.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        token = new string(span[prefix.Length..]);

        return token.Length != 0;
    }

    private static bool TryDeserializeJson<T>(NetHttpRequest request, out T payload, out NetHttpResponse response)
    {
        payload = default!;

        if (request.Content is null || request.Length <= 0)
        {
            response = CreateErrorResponse(NetHttpStatus.BadRequest, "Request body is required.");

            return false;
        }

        if (!request.Headers.ContentType.MediaType.Equals(NetHttpContentType.ApplicationJson, StringComparison.OrdinalIgnoreCase))
        {
            response = CreateErrorResponse(NetHttpStatus.BadRequest, "Content-Type must be application/json.");

            return false;
        }

        try
        {
            using var body = request.Content;
            var content = body.ReadToEnd();
            payload = OpenAiJson.Deserialize<T>(content.Span);

            if (payload is null)
            {
                response = CreateErrorResponse(NetHttpStatus.BadRequest, "Request body is not valid JSON.");

                return false;
            }

            response = default;

            return true;
        }
        catch (JsonException)
        {
            response = CreateErrorResponse(NetHttpStatus.BadRequest, "Request body is not valid JSON.");

            return false;
        }
    }

    private static bool IsValid(OpenAiChatCompletionRequest request) =>
        request.Messages is { Count: > 0 };

    private static bool IsValid(OpenAiEmbeddingsRequest request) =>
        request.Input is { Length: > 0 } && request.Input.All(static value => !string.IsNullOrEmpty(value));

    private static NetHttpResponse CreateDtoResponse<T>(NetHttpStatus status, T payload) =>
        new(
            status,
            new NetHttpHeaders
            {
                ContentType = OpenAiJson.ContentType,
                CacheControl = "no-store",
            },
            new StreamTrs<byte>(OpenAiJson.Serialize(payload)));

    private static NetHttpResponse CreateJsonResponse<T>(NetHttpStatus status, T payload) =>
        new(
            status,
            new NetHttpHeaders
            {
                ContentType = OpenAiJson.ContentType,
                CacheControl = "no-store",
            },
            new StreamTrs<byte>(OpenAiJson.Serialize(payload)));

    private static NetHttpResponse CreateErrorResponse(NetHttpStatus status, string message) =>
        CreateJsonResponse(
            status,
            new OpenAiErrorResponse
            {
                Error = new OpenAiError
                {
                    Message = message,
                },
            });

    private static NetHttpResponse CreateUnauthorizedResponse(string message)
    {
        var response = CreateErrorResponse(NetHttpStatus.Unauthorized, message);
        response.Headers["WWW-Authenticate"] = "Bearer";

        return response;
    }

    private static NetHttpResponse CreateSseResponse(IEnumerable<OpenAiChatCompletionResponseDelta> deltas, IStreamRw<char> stream)
    {
        var headers = new NetHttpHeaders
        {
            ContentType = $"{NetHttpContentType.TextEventStream}; charset=utf-8",
            CacheControl = "no-cache",
        };

        headers["X-Accel-Buffering"] = "no";

        return new NetHttpResponse(NetHttpStatus.Ok, headers, new OpenAiServerSseStream(deltas, stream));
    }

    private enum OpenAiServerRoute
    {
        ChatCompletions,
        Embeddings,
        Models,
    }
}
