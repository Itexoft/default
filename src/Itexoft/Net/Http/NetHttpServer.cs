// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Net.Http.Internal;
using Itexoft.Net.Sockets;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Http;

public sealed class NetHttpServer(NetIpEndpoint endpoint) : IDisposable
{
    private readonly NetTcpServer server = new(endpoint);
    private NetHttpRequestHandler? httpHandler;
    private Disposed started = new();
    private NetHttpWebSocketRequestHandler? webSocketHandler;

    public NetIpEndpoint Endpoint => this.server.Endpoint;

    public void Dispose() => this.server.Dispose();

    public void Http(NetHttpRequestHandler handler)
    {
        handler.Required();

        if (this.started)
            throw new InvalidOperationException("Server already started.");

        if (this.httpHandler is not null)
            throw new InvalidOperationException("HTTP handler already set.");

        this.httpHandler = handler;
    }

    public void Ws(NetHttpWebSocketRequestHandler handler)
    {
        handler.Required();

        if (this.started)
            throw new InvalidOperationException("Server already started.");

        if (this.webSocketHandler is not null)
            throw new InvalidOperationException("WebSocket handler already set.");

        this.webSocketHandler = handler;
    }

    public Promise Start(CancelToken cancelToken = default)
    {
        if (this.started.Enter())
            throw new InvalidOperationException("Server already started.");

        if (this.httpHandler is null)
            throw new InvalidOperationException("HTTP handler is not set.");

        return Promise.Run(() => this.server.Run((stream, ct) => this.Handle(stream, ct), cancelToken), false, cancelToken);
    }

    public void Handle(INetStream stream, CancelToken cancelToken = default)
    {
        var httpHandler = this.httpHandler ?? throw new InvalidOperationException("HTTP handler is not set.");
        var webSocketHandler = this.webSocketHandler;

        while (true)
        {
            try
            {
                var request = NetHttpRequestParser.Read(stream, cancelToken);

                if (request.HttpVersion is not NetHttpVersion.Version10 and not NetHttpVersion.Version11)
                {
                    NetHttpResponseWriter.Write(stream, new(NetHttpVersion.Version11, NetHttpStatus.HttpVersionNotSupported), cancelToken);

                    return;
                }

                if (!TryReadBodyLimits(request.Headers, out var contentLength, out var status))
                {
                    NetHttpResponseWriter.Write(stream, new(request.HttpVersion, status), cancelToken);

                    return;
                }

                var body = ReadOnlyMemory<byte>.Empty;

                if (contentLength > 0)
                {
                    try
                    {
                        body = ReadBody(stream, contentLength, cancelToken);
                    }
                    catch (EndOfStreamException)
                    {
                        NetHttpResponseWriter.Write(stream, new(request.HttpVersion, NetHttpStatus.BadRequest), cancelToken);

                        return;
                    }
                }

                request = new NetHttpRequest(request)
                {
                    Content = new StreamTrs<byte>(body),
                };

                if (webSocketHandler?.Invoke(request, cancelToken) is { } sessionHandler)
                {
                    HandleWebSocket(sessionHandler, request, stream, cancelToken);

                    return;
                }

                var response = httpHandler(request, cancelToken);
                PrepareResponse(response, request);

                NetHttpResponseWriter.Write(stream, response, cancelToken);

                if (!response.KeepAlive)
                    return;
            }
            catch (EndOfStreamException)
            {
                return;
            }
            catch (IOException exception)
            {
                if (IsClientConnectionAbort(exception))
                    return;

                NetHttpResponseWriter.Write(stream, new(NetHttpVersion.Version11, NetHttpStatus.BadRequest), cancelToken);

                return;
            }
        }
    }

    private static void PrepareResponse(NetHttpResponse response, NetHttpRequest request)
    {
        if (string.IsNullOrWhiteSpace(response.Headers.TransferEncoding))
        {
            if (response.Body is null)
                response.Headers.ContentLength = 0;
            else if (response.Body is IStreamRs<byte> body)
                response.Headers.ContentLength = checked(body.Length - body.Position);
        }

        var framed = !string.IsNullOrWhiteSpace(response.Headers.TransferEncoding) || response.Headers.ContentLength.HasValue;
        response.KeepAlive = framed && request.ConnectionType == ConnectionType.KeepAlive;
    }

    private static bool TryReadBodyLimits(NetHttpHeaders headers, out int contentLength, out NetHttpStatus status)
    {
        contentLength = 0;
        status = NetHttpStatus.Ok;

        if (!string.IsNullOrWhiteSpace(headers.TransferEncoding))
        {
            status = NetHttpStatus.BadRequest;

            return false;
        }

        if (!string.IsNullOrWhiteSpace(headers.Expect))
        {
            status = NetHttpStatus.BadRequest;

            return false;
        }

        if (headers.TryGetValue("Content-Length", out var rawLength))
        {
            if (!NetHttpParsing.TryParseInt64(rawLength.AsSpan(), out var parsed) || parsed < 0)
            {
                status = NetHttpStatus.BadRequest;

                return false;
            }

            if (parsed == 0)
                return true;

            contentLength = (int)parsed;

            return true;
        }

        var length = headers.ContentLength;

        if (length is not > 0)
            return true;

        contentLength = (int)length.Value;

        return true;
    }

    private static ReadOnlyMemory<byte> ReadBody(IStreamR<byte> reader, int contentLength, CancelToken cancelToken)
    {
        if (contentLength <= 0)
            return ReadOnlyMemory<byte>.Empty;

        var buffer = new byte[contentLength];
        reader.ReadExact(buffer, cancelToken);

        return buffer;
    }

    private static void HandleWebSocket(
        NetHttpWebSocketSessionHandler sessionHandler,
        NetHttpRequest request,
        IStreamRw<byte> stream,
        CancelToken cancelToken)
    {
        if (!NetHttpWebSocketHandshake.TryValidate(request, out var acceptKey, out var status))
        {
            NetHttpResponseWriter.Write(stream, new(request.HttpVersion, status), cancelToken);

            return;
        }

        NetHttpWebSocketHandshake.WriteAccepted(stream, request.HttpVersion, acceptKey, cancelToken);

        using var webSocket = new NetHttpWebSocket(stream);
        sessionHandler(webSocket, cancelToken);
    }

    private static bool IsClientConnectionAbort(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is NetSocketException socketException && IsClientConnectionAbort(socketException.SocketErrorCode))
                return true;
        }

        return false;
    }

    private static bool IsClientConnectionAbort(NetSocketError socketError) => socketError is NetSocketError.ConnectionReset
        or NetSocketError.ConnectionAborted
        or NetSocketError.Shutdown
        or NetSocketError.OperationAborted
        or NetSocketError.NotConnected
        or NetSocketError.Disconnecting;
}
