// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpResponseReader
{
    private const int maxHeaderBytes = 64 * 1024;
    private const int maxLineBytes = 16 * 1024;
    private static readonly NetHttpMethod headMethod = new("HEAD");

    public static NetHttpResponse Read(INetStream stream, NetHttpRequest request, Action disconnect, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();
        var statusLine = stream.ReadLineBytes(cancelToken);

        if (statusLine.Length == 0)
            throw new IOException("HTTP response is missing status line.");

        ParseStatusLine(statusLine.Span, out var version, out var status);
        var headers = new NetHttpHeaders();
        var totalHeaderBytes = statusLine.Length + 2;

        if (statusLine.Length > maxLineBytes)
            throw new IOException("HTTP response line is too long.");

        while (true)
        {
            cancelToken.ThrowIf();
            var line = stream.ReadLineBytes(cancelToken);
            totalHeaderBytes += line.Length + 2;

            if (totalHeaderBytes > maxHeaderBytes)
                throw new IOException("HTTP response headers are too large.");

            if (line.Length > maxLineBytes)
                throw new IOException("HTTP response line is too long.");

            if (line.Length == 0)
                break;

            var span = line.Span;
            var colon = span.IndexOf((byte)':');

            if (colon <= 0)
                throw new IOException("Invalid HTTP header line.");

            var name = NetHttpParsing.TrimOws(span[..colon]);
            var value = NetHttpParsing.TrimOws(span[(colon + 1)..]);

            if (name.IsEmpty)
                throw new IOException("HTTP header name is empty.");

            headers.Add(GetAsciiString(name), GetAsciiString(value));
        }

        cancelToken.ThrowIf();
        return CreateResponse(stream, request, status, headers, version, disconnect);
    }

    private static void ParseStatusLine(ReadOnlySpan<byte> line, out NetHttpVersion version, out NetHttpStatus status)
    {
        version = NetHttpVersion.Version11;
        status = NetHttpStatus.Unknown;

        if (line.Length < 12)
            throw new IOException("HTTP status line is too short.");

        if (!line.StartsWith("HTTP/"u8))
            throw new IOException("Invalid HTTP status line.");

        var index = 5;
        var major = 0;

        while (index < line.Length && IsDigit(line[index]))
        {
            major = major * 10 + (line[index] - (byte)'0');
            index++;
        }

        if (index >= line.Length || line[index] != (byte)'.')
            throw new IOException("Invalid HTTP version.");

        index++;
        var minor = 0;

        while (index < line.Length && IsDigit(line[index]))
        {
            minor = minor * 10 + (line[index] - (byte)'0');
            index++;
        }

        if (major == 1 && minor == 0)
            version = NetHttpVersion.Version10;
        else if (major == 1 && minor == 1)
            version = NetHttpVersion.Version11;

        while (index < line.Length && line[index] == (byte)' ')
            index++;

        if (index >= line.Length)
            throw new IOException("Missing HTTP status code.");

        var statusSlice = line[index..];
        var space = statusSlice.IndexOf((byte)' ');

        if (space >= 0)
            statusSlice = statusSlice[..space];

        if (!NetHttpParsing.TryParseInt32(statusSlice, out var statusCode))
            throw new IOException("Invalid HTTP status code.");

        status = (NetHttpStatus)statusCode;
    }

    private static bool IsDigit(byte value) => value is >= (byte)'0' and <= (byte)'9';

    private static NetHttpResponse CreateResponse(
        INetStream stream,
        NetHttpRequest request,
        NetHttpStatus status,
        NetHttpHeaders headers,
        NetHttpVersion version,
        Action disconnect)
    {
        var bodyStream = CreateBodyStream(stream, request, status, headers, version, disconnect);

        return new NetHttpResponse(version, status, headers, bodyStream);
    }

    private static IStreamR<byte> CreateBodyStream(
        INetStream stream,
        NetHttpRequest request,
        NetHttpStatus status,
        NetHttpHeaders headers,
        NetHttpVersion version,
        Action disconnect)
    {
        var close = request.ConnectionType != ConnectionType.KeepAlive
                    || request.ReceiveHeadersOnly
                    || (version == NetHttpVersion.Version10 && headers.ConnectionType != ConnectionType.KeepAlive);

        if (request.ReceiveHeadersOnly || !ResponseHasBody(request, status, headers))
        {
            dispose();

            return new NetHttpEmptyBodyStream();
        }

        if (headers.TransferEncoding is not null && NetHttpParsing.ContainsToken(headers.TransferEncoding.AsSpan(), "chunked".AsSpan()))
            return NetHttpContentDecoder.Apply(headers, new NetHttpChunkedStream(stream, dispose));

        if (headers.ContentLength is long length)
            return NetHttpContentDecoder.Apply(headers, new NetHttpContentLengthStream(stream, length, dispose));

        return NetHttpContentDecoder.Apply(headers, new NetHttpResponseBodyStream(stream, dispose));

        void dispose()
        {
            if (close)
                disconnect();
        }
    }

    private static bool ResponseHasBody(NetHttpRequest request, NetHttpStatus status, NetHttpHeaders headers)
    {
        if (request.Method == headMethod)
            return false;

        var statusClass = status.GetClass();

        if (statusClass == NetHttpStatusClass.Informational)
            return false;

        if (status is NetHttpStatus.NoContent or NetHttpStatus.NotModified)
            return false;

        if (headers.ContentLength is 0)
            return false;

        return true;
    }

    private static string GetAsciiString(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return string.Empty;

        foreach (var b in span)
        {
            if (b >= 0x80)
                throw new IOException("HTTP headers must be ASCII.");
        }

        return string.Create(
            span.Length,
            span,
            static (chars, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                    chars[i] = (char)source[i];
            });
    }
}
