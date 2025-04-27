// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.EmbeddedWeb;

public sealed class EmbeddedWebRpcClient : IDisposable
{
    private readonly NetHttpClient client;
    private readonly bool ownsClient;
    private readonly string pathPrefix;

    public EmbeddedWebRpcClient(NetHttpClient client, string pathPrefix = "/rpc")
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.pathPrefix = ValidatePrefix(pathPrefix);
    }

    public EmbeddedWebRpcClient(Uri endpoint, string pathPrefix = "/rpc")
    {
        endpoint.Required();
        this.client = new NetHttpClient(endpoint);
        this.pathPrefix = ValidatePrefix(pathPrefix);
        this.ownsClient = true;
    }

    public void Dispose()
    {
        if (this.ownsClient)
            this.client.Dispose();
    }

    public TResult Call<TArgs, TResult>(string method, TArgs args, CancelToken cancelToken = default)
    {
        ValidateMethod(method);

        var payload = EmbeddedWebRpcJson.Serialize(args);
        var response = this.client.Post(this.BuildPath(method), payload, EmbeddedWebRpcJson.ContentType, cancelToken);

        response.EnsureSuccess();

        var bytes = response.ReadAsBytes(cancelToken);

        if (bytes.Length == 0)
            return default!;

        return EmbeddedWebRpcJson.Deserialize<TResult>(bytes);
    }

    public TResult Call<TResult>(string method, CancelToken cancelToken = default) =>
        this.CallEmpty<TResult>(method, cancelToken);

    public void Call<TArgs>(string method, TArgs args, CancelToken cancelToken = default)
    {
        ValidateMethod(method);

        var payload = EmbeddedWebRpcJson.Serialize(args);
        var response = this.client.Post(this.BuildPath(method), payload, EmbeddedWebRpcJson.ContentType, cancelToken);

        response.EnsureSuccess();
    }

    public void Call(string method, CancelToken cancelToken = default) =>
        this.CallEmpty(method, cancelToken);

    private static void ValidateMethod(string method)
    {
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("RPC method cannot be empty.", nameof(method));

        if (method.Contains('/', StringComparison.Ordinal))
            throw new ArgumentException("RPC method cannot contain '/'.", nameof(method));
    }

    private string BuildPath(string method) => $"{this.pathPrefix}/{method}";

    private TResult CallEmpty<TResult>(string method, CancelToken cancelToken)
    {
        ValidateMethod(method);

        var response = this.client.Post(this.BuildPath(method), ReadOnlyMemory<byte>.Empty, null, cancelToken);

        response.EnsureSuccess();

        var bytes = response.ReadAsBytes(cancelToken);

        if (bytes.Length == 0)
            return default!;

        return EmbeddedWebRpcJson.Deserialize<TResult>(bytes);
    }

    private void CallEmpty(string method, CancelToken cancelToken)
    {
        ValidateMethod(method);

        var response = this.client.Post(this.BuildPath(method), ReadOnlyMemory<byte>.Empty, null, cancelToken);

        response.EnsureSuccess();
    }

    private static string ValidatePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("RPC path prefix cannot be empty.", nameof(prefix));

        if (!prefix.StartsWith('/'))
            throw new ArgumentException("RPC path prefix must start with '/'.", nameof(prefix));

        if (prefix.Length > 1 && prefix.EndsWith('/'))
            throw new ArgumentException("RPC path prefix must not end with '/'.", nameof(prefix));

        return prefix;
    }

    private static class EmbeddedWebRpcJson
    {
        public const string ContentType = "application/json; charset=utf-8";

        private static readonly JsonSerializerOptions options = CreateOptions();

        public static ReadOnlyMemory<byte> Serialize<T>(T value) =>
            JsonSerializer.SerializeToUtf8Bytes(value, options);

        public static TResult Deserialize<TResult>(ReadOnlyMemory<byte> value) =>
            JsonSerializer.Deserialize<TResult>(value.Span, options)!;

        private static JsonSerializerOptions CreateOptions()
        {
            var jsonOptions = new JsonSerializerOptions
            {
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            };

            return jsonOptions;
        }
    }
}

public readonly record struct EmbeddedWebRpcEmpty;
