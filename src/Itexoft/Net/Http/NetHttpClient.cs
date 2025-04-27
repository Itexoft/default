// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Net.Security;
using System.Text;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Net.Http.Internal;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Http;

public sealed class NetHttpClient : IDisposable
{
    public delegate bool CookieHandlerDelegate(NetCookie cookie);

    private Disposed disposed = new();

    private NetLazyConnector lazyConnector;

    public NetHttpClient(NetEndpoint endpoint) : this(endpoint, GetConnector()) { }

    public NetHttpClient(Uri uri) : this(new NetEndpoint(uri.DnsSafeHost, uri.Port, NetProtocol.Tcp))
    {
        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            this.TlsOptions ??= new SslClientAuthenticationOptions { TargetHost = uri.DnsSafeHost };
    }

    public NetHttpClient(NetEndpoint endpoint, NetConnector connector)
    {
        this.Connector = connector.Required();
        this.Endpoint = endpoint;
        this.TlsOptions = CreateDefaultTlsOptions(endpoint);

        this.lazyConnector = new NetLazyConnector(token =>
        {
            var stream = connector.Connect(endpoint, token);

            if (this.TlsOptions is null)
                return stream;

            var sslStream = new NetSslStream(stream, false);
            sslStream.AuthenticateAsClient(this.TlsOptions, token);

            return sslStream;
        });
    }

    internal INetConnector Connector { get; }

    public NetEndpoint Endpoint { get; }

    public NetHttpHeaders DefaultHeaders { get; } = new();
    public NetCookieContainer Cookies { get; } = new();

    public SslClientAuthenticationOptions? TlsOptions { get; init; }
    public CookieHandlerDelegate? CookieHandler { get; init; }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.lazyConnector.Dispose();
    }

    private static NetTcpConnector GetConnector()
    {
        var connector = new NetTcpConnector
        {
            NoDelay = true,
            SendBufferSize = 16 * 1024,
            ReceiveBufferSize = 16 * 1024,
            ReceiveTimeout = Timeout.InfiniteTimeSpan,
            SendTimeout = Timeout.InfiniteTimeSpan,
        };

        return connector;
    }

    public void Disconnect() => this.lazyConnector.Disconnect();

    public NetHttpResponse Get(NetHttpPathQuery pathAndQuery, CancelToken cancelToken = default) =>
        this.Send(new(NetHttpMethod.Get, pathAndQuery), cancelToken);

    public NetHttpResponse Post(
        NetHttpPathQuery pathAndQuery,
        IStreamRs<byte>? content = null,
        NetHttpHeaders? headers = null,
        CancelToken cancelToken = default) =>
        this.Send(new(NetHttpMethod.Post, pathAndQuery) { Content = content, Headers = headers ?? new NetHttpHeaders() }, cancelToken);

    public NetHttpResponse Post(
        NetHttpPathQuery pathAndQuery,
        ReadOnlyMemory<byte> content,
        string? contentType = null,
        CancelToken cancelToken = default)
    {
        var headers = new NetHttpHeaders();

        if (!string.IsNullOrEmpty(contentType))
            headers.ContentType = contentType;

        return this.Post(pathAndQuery, new StreamTrs<byte>(content), headers, cancelToken);
    }

    public NetHttpResponse Post(
        NetHttpPathQuery pathAndQuery,
        string content,
        Encoding? encoding = null,
        string? contentType = null,
        CancelToken cancelToken = default)
    {
        encoding ??= Encoding.UTF8;
        var bytes = encoding.GetBytes(content ?? string.Empty);
        contentType ??= $"text/plain; charset={encoding.WebName}";

        return this.Post(pathAndQuery, bytes, contentType, cancelToken);
    }

    public NetHttpResponse Send(NetHttpRequest request, CancelToken cancelToken = default) =>
        this.SendCore(request.Required(), cancelToken);

    private NetHttpResponse SendCore(NetHttpRequest request, CancelToken cancelToken)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();

        var contentLength = GetContentLength(request.Content);

        if (contentLength is null && request.Method == NetHttpMethod.Post)
            contentLength = 0;

        var cookieHeader = this.GetCookieHeader(request);
        var secure = this.TlsOptions is not null;

        this.DefaultHeaders.ApplyDefaults(request.Headers);

        cancelToken = ApplyTimeout(cancelToken, request.Timeout, true);

        try
        {
            if (request.ConnectionType != ConnectionType.KeepAlive)
                this.lazyConnector.Disconnect();

            var stream = this.lazyConnector.Connect();

            NetHttpRequestWriter.WriteHeaders(stream, request, this.DefaultHeaders, this.Endpoint, cookieHeader, contentLength, secure, cancelToken);

            if (request.Content is not null && contentLength.GetValueOrDefault() > 0)
                this.SendBody(stream, request.Content, request.SendBufferSize, cancelToken);

            var response = NetHttpResponseReader.Read(stream, request, this.lazyConnector.Disconnect, cancelToken);
            this.ProcessCookies(response.Headers, request.PathAndQuery);

            return response;
        }
        catch (Exception ex)
        {
            if (cancelToken.IsTimedOut)
            {
                this.lazyConnector.Disconnect();

                throw new TimeoutException("Request timed out", ex);
            }

            throw;
        }
    }

    private static long? GetContentLength(IStreamRs<byte>? content)
    {
        if (content is null)
            return null;

        var remaining = content.Length - content.Position;

        return remaining < 0 ? 0 : remaining;
    }

    private string? GetCookieHeader(NetHttpRequest request)
    {
        if (request.Headers.Cookie is not null)
            return null;

        var container = request.CookieContainer ?? this.Cookies;
        var secure = this.TlsOptions is not null;

        return container.GetCookieHeader(this.Endpoint, request.PathAndQuery.Path, secure);
    }

    private void ProcessCookies(NetHttpHeaders headers, NetHttpPathQuery pathAndQuery)
    {
        var values = headers.SetCookie;

        if (values.Count == 0)
            return;

        var now = DateTimeOffset.UtcNow;
        var domain = (string)this.Endpoint.Host;
        var path = GetDefaultCookiePath(pathAndQuery.Path);

        foreach (var value in values)
        {
            if (!NetCookie.TryParseSetCookie(value.AsSpan(), domain, path, now, out var cookie))
                continue;

            if (this.CookieHandler is not null && !this.CookieHandler(cookie))
                continue;

            this.Cookies.Add(cookie);
        }
    }

    private static SslClientAuthenticationOptions? CreateDefaultTlsOptions(NetEndpoint endpoint)
    {
        if ((int)endpoint.Port != 443)
            return null;

        return new() { TargetHost = endpoint.Host.Host };
    }

    private long SendBody(INetStream stream, IStreamRs<byte> content, int bufferSize, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();
        var total = 0L;
        var rented = ArrayPool<byte>.Shared.Rent(Math.Max(bufferSize, 1024));

        try
        {
            while (true)
            {
                var read = content.Read(rented, cancelToken);

                if (read == 0)
                    break;

                stream.Write(rented.AsSpan(0, read), cancelToken);
                total += read;
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented);
        }
        cancelToken.ThrowIf();
        return total;
    }

    private static string GetDefaultCookiePath(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
            return "/";

        var lastSlash = path.LastIndexOf('/');

        return lastSlash <= 0 ? "/" : path[..lastSlash];
    }

    internal static CancelToken ApplyTimeout(CancelToken cancelToken, TimeSpan timeout, bool allowMutate)
    {
        if (timeout.IsInfinite || timeout <= TimeSpan.Zero)
            return cancelToken;

        return cancelToken.Branch(timeout);
    }
}
