// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.ExceptionServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Net.Http.Internal;
using Itexoft.Net.Sockets;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Http;

public sealed class NetHttpServer(NetIpEndpoint endpoint, NetHttpRequestHandler handler) : IDisposable
{
    private const int backlog = 128;
    private const int maxBodyBytes = 4 * 1024 * 1024;

    private readonly NetHttpRequestHandler handler = handler.Required();
    private ExceptionDispatchInfo? connectionFailure;
    private NetSocket? listener;

    private Latch started = new();
    private Latch stopped = new();

    public NetIpEndpoint Endpoint => endpoint;

    public Promise Completion { get; private set; } = Promise.Completed;

    public void Dispose() => this.Stop();

    public void Start(CancelToken cancelToken = default)
    {
        if (!this.started.Try())
            throw new InvalidOperationException("Server already started.");

        var socket = new NetSocket(endpoint.AddressFamily, NetSocketType.Stream, NetProtocol.Tcp);

        socket.Bind(endpoint);

        socket.Listen(backlog);

        this.listener = socket;
        this.Completion = Promise.Run(() => this.RunAcceptLoopAsync(cancelToken), false, cancelToken);
    }

    public void Stop(CancelToken cancelToken = default)
    {
        if (!this.stopped.Try())
            return;

        var socket = Interlocked.Exchange(ref this.listener, null);

        if (socket is not null)
            socket.Dispose();

        var completion = this.Completion;

        if (!completion.IsCompleted)
            completion.GetAwaiter().GetResult();
    }

    private void RunAcceptLoopAsync(CancelToken cancelToken)
    {
        try
        {
            while (true)
            {
                cancelToken.ThrowIf();
                this.connectionFailure?.Throw();

                if (this.listener is null)
                    break;

                try
                {
                    var socket = this.listener.Accept(cancelToken);

                    _ = Promise.Run(
                        () =>
                        {
                            try
                            {
                                this.HandleConnectionAsync(socket, cancelToken);
                            }
                            catch (Exception exception)
                            {
                                if (cancelToken.IsRequested || IsClientConnectionAbort(exception))
                                    return;

                                var edi = ExceptionDispatchInfo.Capture(exception.GetBaseException());

                                if (Interlocked.CompareExchange(ref this.connectionFailure, edi, null) is null
                                    && Atomic.NullOut(ref this.listener, out var listener))
                                    listener.Dispose();
                            }
                        },
                        false,
                        cancelToken);
                }
                catch
                {
                    if (this.stopped || cancelToken.IsRequested)
                        break;

                    this.connectionFailure?.Throw();

                    throw;
                }
            }
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested) { }
        finally
        {
            if (this.listener is not null && !this.stopped)
                this.listener.Dispose();
        }
    }

    private void HandleConnectionAsync(NetSocket socket, CancelToken cancelToken)
    {
        using var stream = new NetStream(socket, true);

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
                        body = ReadBodyAsync(stream, contentLength, cancelToken);
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

                var response = this.handler(request, cancelToken);
                PrepareResponse(response, request);

                NetHttpResponseWriter.Write(stream, response, cancelToken);

                if (!response.KeepAlive)
                    return;
            }
            catch (EndOfStreamException)
            {
                return;
            }
            catch (IOException)
            {
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
            else if (response.Body is IStreamRl<byte> body)
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

            switch (parsed)
            {
                case 0:
                    return true;
                case > maxBodyBytes:
                    status = NetHttpStatus.PayloadTooLarge;

                    return false;
                default:
                    contentLength = (int)parsed;

                    return true;
            }
        }

        var length = headers.ContentLength;

        if (length is not > 0)
            return true;

        if (length.Value > maxBodyBytes)
        {
            status = NetHttpStatus.PayloadTooLarge;

            return false;
        }

        contentLength = (int)length.Value;

        return true;
    }

    private static ReadOnlyMemory<byte> ReadBodyAsync(IStreamR<byte> reader, int contentLength, CancelToken cancelToken)
    {
        if (contentLength <= 0)
            return ReadOnlyMemory<byte>.Empty;

        var buffer = new byte[contentLength];
        reader.ReadExact(buffer, cancelToken);

        return buffer;
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
