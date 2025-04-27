// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.IO.Streams;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpRequestWriter
{
    private static readonly string hostHeader = "Host";
    private static readonly string connectionHeader = "Connection";
    private static readonly string contentLengthHeader = "Content-Length";
    private static readonly string acceptEncodingHeader = "Accept-Encoding";
    private static readonly string cookieHeader = "Cookie";

    public static void WriteHeaders(
        IStreamRw<byte> stream,
        NetHttpRequest request,
        NetHttpHeaders defaultHeaders,
        NetEndpoint endpoint,
        string? cookieValue,
        long? contentLength,
        bool secure,
        CancelToken cancelToken)
    {
        var writer = new DynamicMemory<byte>(512);
        cancelToken.ThrowIf();
        try
        {
            WriteRequestLine(ref writer, request);

            var explicitCookie = request.Headers.Cookie ?? cookieValue;
            /*var path = request.PathAndQuery;
            var pathValue = path.Path ?? string.Empty;
            var queryCount = path.Query?.Count ?? 0;
            var hasAuth = request.Headers.Authorization is not null || request.Headers.ProxyAuthorization is not null;
            var hasBody = contentLength.HasValue && contentLength.Value > 0;
            var expectHeader = request.Headers.Expect;
            var transferHeader = request.Headers.TransferEncoding;*/

            var skipDefaults = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                hostHeader,
                connectionHeader,
                contentLengthHeader,
                acceptEncodingHeader,
            };

            foreach (var header in request.Headers)
                skipDefaults.Add(header.Key);

            if ((request.Headers.Cookie ?? cookieValue) is not null)
                skipDefaults.Add(cookieHeader);

            var host = request.Headers.Host ?? defaultHeaders.Host ?? BuildHost(endpoint, secure);
            WriteHeader(ref writer, hostHeader, host);

            WriteHeader(ref writer, connectionHeader, request.Headers.Connection);

            if (contentLength.HasValue)
                WriteHeader(ref writer, contentLengthHeader, contentLength.Value);

            var acceptEncoding = request.Headers.AcceptEncoding ?? defaultHeaders.AcceptEncoding ?? "gzip, br, deflate";
            WriteHeader(ref writer, acceptEncodingHeader, acceptEncoding);

            if (explicitCookie is not null)
                WriteHeader(ref writer, cookieHeader, explicitCookie);

            WriteHeaderSet(ref writer, defaultHeaders, skipDefaults);

            var skipRequest = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                hostHeader,
                connectionHeader,
                contentLengthHeader,
                acceptEncodingHeader,
            };

            if (explicitCookie is not null)
                skipRequest.Add(cookieHeader);

            WriteHeaderSet(ref writer, request.Headers, skipRequest);

            WriteCrlf(ref writer);

            stream.Write(writer.AsSpan(), cancelToken);
            stream.Flush(cancelToken);
        }
        finally
        {
            writer.Dispose();
        }
        
        cancelToken.ThrowIf();
    }

    private static void WriteRequestLine(ref DynamicMemory<byte> buffer, NetHttpRequest request)
    {
        WriteAscii(ref buffer, request.Method.ToString().AsSpan());
        WriteByte(ref buffer, (byte)' ');
        request.PathAndQuery.WriteTo(ref buffer);
        WriteByte(ref buffer, (byte)' ');
        WriteAscii(ref buffer, request.HttpVersion == NetHttpVersion.Version10 ? "HTTP/1.0".AsSpan() : "HTTP/1.1".AsSpan());
        WriteCrlf(ref buffer);
    }

    private static void WriteHeaderSet(ref DynamicMemory<byte> buffer, NetHttpHeaders headers, HashSet<string> skip)
    {
        foreach (var header in headers)
        {
            if (skip.Contains(header.Key))
                continue;

            WriteHeader(ref buffer, header.Key, header.Value);
        }
    }

    private static void WriteHeader(ref DynamicMemory<byte> buffer, string name, long value)
    {
        WriteAscii(ref buffer, name.AsSpan());
        WriteByte(ref buffer, (byte)':');
        WriteByte(ref buffer, (byte)' ');
        WriteInt64(ref buffer, value);
        WriteCrlf(ref buffer);
    }

    private static void WriteHeader(ref DynamicMemory<byte> buffer, ReadOnlySpan<char> name, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return;

        WriteAscii(ref buffer, name);
        WriteByte(ref buffer, (byte)':');
        WriteByte(ref buffer, (byte)' ');
        WriteAscii(ref buffer, value);
        WriteCrlf(ref buffer);
    }

    private static string BuildHost(NetEndpoint endpoint, bool secure)
    {
        var host = (string)endpoint.Host;
        var port = (int)endpoint.Port;
        var defaultPort = secure ? 443 : 80;

        return port == defaultPort ? host : $"{host}:{port}";
    }

    private static void WriteCrlf(ref DynamicMemory<byte> buffer)
    {
        var span = buffer.GetSpan(2);
        span[0] = (byte)'\r';
        span[1] = (byte)'\n';
        buffer.Advance(2);
    }

    private static void WriteByte(ref DynamicMemory<byte> buffer, byte value)
    {
        var span = buffer.GetSpan(1);
        span[0] = value;
        buffer.Advance(1);
    }

    private static void WriteAscii(ref DynamicMemory<byte> buffer, ReadOnlySpan<char> text)
    {
        var span = buffer.GetSpan(text.Length);

        for (var i = 0; i < text.Length; i++)
            span[i] = text[i] <= 0x7F ? (byte)text[i] : (byte)'?';

        buffer.Advance(text.Length);
    }

    private static void WriteInt64(ref DynamicMemory<byte> buffer, long value)
    {
        Span<char> chars = stackalloc char[20];
        var index = chars.Length;
        var negative = value < 0;

        if (negative)
            value = -value;

        do
        {
            var digit = (int)(value % 10);
            chars[--index] = (char)('0' + digit);
            value /= 10;
        }
        while (value != 0);

        if (negative)
            chars[--index] = '-';

        WriteAscii(ref buffer, chars[index..]);
    }
}
