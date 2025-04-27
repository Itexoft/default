// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Globalization;
using Itexoft.IO;
using Itexoft.IO.Streams;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal static class NetHttpResponseWriter
{
    private const int bodyBufferSize = 16 * 1024;

    public static void Write(IStreamW<byte> stream, NetHttpResponse response, CancelToken cancelToken)
    {
        var buffer = new DynamicMemory<byte>(256);

        try
        {
            WriteStatusLine(ref buffer, response.HttpVersion, response.Status);

            foreach (var header in response.Headers)
            {
                WriteAscii(ref buffer, header.Key);
                WriteAscii(ref buffer, ": ");
                WriteAscii(ref buffer, header.Value);
                WriteAscii(ref buffer, "\r\n");
            }

            WriteAscii(ref buffer, "\r\n");

            stream.Write(buffer.AsSpan(), cancelToken);

            if (response.Body is IStreamR<byte> body)
                WriteBody(stream, body, cancelToken);

            stream.Flush(cancelToken);
        }
        finally
        {
            if (response.Body is IDisposable disposable)
                disposable.Dispose();

            buffer.Dispose();
        }
    }

    private static void WriteBody(IStreamW<byte> stream, IStreamR<byte> body, CancelToken cancelToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(bodyBufferSize);

        try
        {
            while (true)
            {
                var read = body.Read(buffer, cancelToken);

                if (read == 0)
                    break;

                stream.Write(buffer.AsSpan(0, read), cancelToken);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private static void WriteStatusLine(ref DynamicMemory<byte> buffer, NetHttpVersion version, NetHttpStatus status)
    {
        WriteAscii(ref buffer, version == NetHttpVersion.Version10 ? "HTTP/1.0 " : "HTTP/1.1 ");
        WriteAscii(ref buffer, ((int)status).ToString(CultureInfo.InvariantCulture));
        WriteAscii(ref buffer, " ");
        WriteAscii(ref buffer, GetReasonPhrase(status));
        WriteAscii(ref buffer, "\r\n");
    }

    private static string GetReasonPhrase(NetHttpStatus status) => status switch
    {
        NetHttpStatus.SwitchingProtocols => "Switching Protocols",
        NetHttpStatus.Ok => "OK",
        NetHttpStatus.NoContent => "No Content",
        NetHttpStatus.BadRequest => "Bad Request",
        NetHttpStatus.Unauthorized => "Unauthorized",
        NetHttpStatus.NotFound => "Not Found",
        NetHttpStatus.MethodNotAllowed => "Method Not Allowed",
        NetHttpStatus.UnprocessableEntity => "Unprocessable Entity",
        NetHttpStatus.PayloadTooLarge => "Payload Too Large",
        NetHttpStatus.InternalServerError => "Internal Server Error",
        NetHttpStatus.HttpVersionNotSupported => "HTTP Version Not Supported",
        NetHttpStatus.BadGateway => "Bad Gateway",
        _ => throw new InvalidOperationException($"Unsupported HTTP status: {(int)status}."),
    };

    private static void WriteAscii(ref DynamicMemory<byte> buffer, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        WriteAscii(ref buffer, value.AsSpan());
    }

    private static void WriteAscii(ref DynamicMemory<byte> buffer, ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return;

        var span = buffer.GetSpan(value.Length);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (c > 0x7F)
                throw new InvalidOperationException("Non-ASCII character in HTTP header.");

            span[i] = (byte)c;
        }

        buffer.Advance(value.Length);
    }
}
