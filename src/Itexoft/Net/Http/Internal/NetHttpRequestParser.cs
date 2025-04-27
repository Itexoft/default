// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpRequestParser
{
    private const int maxHeaderBytes = 64 * 1024;
    private const int maxLineBytes = 16 * 1024;
    private static ReadOnlySpan<byte> Http10 => "HTTP/1.0"u8;
    private static ReadOnlySpan<byte> Http11 => "HTTP/1.1"u8;

    private static bool TryParseRequestLine(
        ReadOnlySpan<byte> line,
        out NetHttpMethod method,
        out NetHttpPathQuery pathAndQuery,
        out NetHttpVersion version)
    {
        method = default;
        pathAndQuery = default;
        version = NetHttpVersion.Version11;

        var space1 = line.IndexOf((byte)' ');

        if (space1 <= 0)
            return false;

        var rest = line[(space1 + 1)..];
        var space2 = rest.IndexOf((byte)' ');

        if (space2 <= 0)
            return false;

        var methodSpan = line[..space1];
        var targetSpan = rest[..space2];
        var versionSpan = rest[(space2 + 1)..];

        if (!IsValidToken(methodSpan))
            return false;

        if (!IsValidTarget(targetSpan))
            return false;

        if (!TryParseVersion(versionSpan, out version))
            return false;

        if (!TryGetAsciiString(methodSpan, out var methodText))
            return false;

        if (!TryGetAsciiString(targetSpan, out var target))
            return false;

        if (string.IsNullOrEmpty(target) || target[0] != '/')
            return false;

        method = new NetHttpMethod(methodText);
        pathAndQuery = target;

        return true;
    }

    private static bool TryParseVersion(ReadOnlySpan<byte> span, out NetHttpVersion version)
    {
        if (span.SequenceEqual(Http10))
        {
            version = NetHttpVersion.Version10;

            return true;
        }

        if (span.SequenceEqual(Http11))
        {
            version = NetHttpVersion.Version11;

            return true;
        }

        version = NetHttpVersion.Version11;

        return false;
    }

    private static bool IsValidToken(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return false;

        foreach (var b in span)
        {
            if (b <= 0x20 || b >= 0x7F)
                return false;
        }

        return true;
    }

    private static bool IsValidTarget(ReadOnlySpan<byte> span)
    {
        if (span.IsEmpty)
            return false;

        foreach (var b in span)
        {
            if (b <= 0x20 || b >= 0x7F)
                return false;
        }

        return true;
    }

    private static bool TryGetAsciiString(ReadOnlySpan<byte> span, out string value)
    {
        if (span.IsEmpty)
        {
            value = string.Empty;

            return true;
        }

        foreach (var b in span)
        {
            if (b >= 0x80)
            {
                value = string.Empty;

                return false;
            }
        }

        value = string.Create(
            span.Length,
            span,
            static (chars, source) =>
            {
                for (var i = 0; i < source.Length; i++)
                    chars[i] = (char)source[i];
            });

        return true;
    }

    public static NetHttpRequest Read(IStreamR<byte> stream, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();
        var requestLine = stream.ReadLineBytes(cancelToken);

        if (requestLine.Length == 0)
            throw new IOException("HTTP request is missing request line.");

        if (requestLine.Length > maxLineBytes)
            throw new IOException("HTTP request line is too long.");

        if (!TryParseRequestLine(requestLine.Span, out var method, out var pathAndQuery, out var version))
            throw new IOException("Invalid HTTP request line.");

        var headers = new NetHttpHeaders();
        var totalHeaderBytes = requestLine.Length + 2;

        while (true)
        {
            var line = stream.ReadLineBytes(cancelToken);
            totalHeaderBytes += line.Length + 2;

            if (totalHeaderBytes > maxHeaderBytes)
                throw new IOException("HTTP request headers are too large.");

            if (line.Length > maxLineBytes)
                throw new IOException("HTTP request line is too long.");

            if (line.Length == 0)
                break;

            var span = line.Span;
            var colon = span.IndexOf((byte)':');

            if (colon <= 0)
                throw new IOException("Invalid HTTP header line.");

            var nameSpan = NetHttpParsing.TrimOws(span[..colon]);
            var valueSpan = NetHttpParsing.TrimOws(span[(colon + 1)..]);

            if (nameSpan.IsEmpty)
                throw new IOException("HTTP header name is empty.");

            if (!TryGetAsciiString(nameSpan, out var name))
                throw new IOException("HTTP header name must be ASCII.");

            if (!TryGetAsciiString(valueSpan, out var value))
                throw new IOException("HTTP header value must be ASCII.");

            headers.Add(name, value);
        }

        return new NetHttpRequest(method, pathAndQuery, version, headers);
    }
}
