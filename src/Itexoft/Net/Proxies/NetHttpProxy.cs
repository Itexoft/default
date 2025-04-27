// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public class NetHttpProxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    public NetHttpProxy(NetHost host, NetPort port, NetCredential credentials = default) : this(new(host, port, NetProtocol.Tcp), credentials) { }

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public virtual INetStream Connect(INetStream stream, NetEndpoint endpoint, CancelToken cancelToken = default)
    {
        this.SendConnect(stream, endpoint, cancelToken);
        this.ValidateResponse(stream, cancelToken);

        return stream;
    }

    public static bool CanHandle(INetStream stream, CancelToken cancelToken = default)
    {
        var probe = stream.ReadExactValues(7, cancelToken);
        var hasSpace = false;

        foreach (var b in probe)
        {
            if (b == (byte)' ')
            {
                hasSpace = true;

                break;
            }

            if (!IsAsciiLetter(b))
                return false;
        }

        return hasSpace || probe.Length >= 3;
    }

    private void SendConnect(INetStream stream, NetEndpoint endpoint, CancelToken cancelToken)
    {
        var builder = new StringBuilder();
        builder.Append("CONNECT ").Append(endpoint.Host).Append(':').Append(endpoint.Port).Append(" HTTP/1.1\r\n");
        builder.Append("Host: ").Append(endpoint.Host).Append("\r\n");

        if (this.Credential != default)
        {
            var user = this.Credential.UserName ?? string.Empty;
            var pass = this.Credential.Password ?? string.Empty;
            var token = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{user}:{pass}"));
            builder.Append("Proxy-Authorization: Basic ").Append(token).Append("\r\n");
        }

        builder.Append("\r\n");

        var request = builder.ToString();
        var bytes = Encoding.ASCII.GetBytes(request);

        stream.Write(bytes, cancelToken);
    }

    private void ValidateResponse(IStreamRw<byte> stream, CancelToken cancelToken)
    {
        var statusLine = ReadStatusLine(stream, cancelToken);
        var status = ParseStatusCode(statusLine);

        if (status is < 200 or >= 300)
            throw new IOException($"HTTP proxy CONNECT failed with status {status}.");

        DrainHeaders(stream, cancelToken);
    }

    private static string? ReadStatusLine(IStreamRw<byte> stream, CancelToken cancelToken)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[1];
        var lastWasCr = false;

        while (true)
        {
            var read = stream.Read(buffer.AsSpan(0, 1), cancelToken);

            if (read == 0)
                return null;

            var b = buffer[0];
            ms.WriteByte(b);

            if (b == '\r')
            {
                lastWasCr = true;

                continue;
            }

            if (lastWasCr && b == '\n')
                break;

            lastWasCr = false;

            if (ms.Length > 4096)
                throw new IOException("Proxy response too large.");
        }

        return Encoding.ASCII.GetString(ms.GetBuffer(), 0, (int)ms.Length - 2);
    }

    private static int ParseStatusCode(string? statusLine)
    {
        if (statusLine is null || statusLine.Length < 12 || !statusLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Invalid proxy response.");

        var statusStart = statusLine.IndexOf(' ');
        var statusEnd = statusStart >= 0 ? statusLine.IndexOf(' ', statusStart + 1) : -1;

        if (statusStart < 0 || statusEnd < 0 || !int.TryParse(statusLine.AsSpan(statusStart + 1, statusEnd - statusStart - 1), out var statusCode))
            throw new IOException($"Invalid proxy status line: {statusLine}");

        return statusCode;
    }

    private static void DrainHeaders(IStreamRw<byte> stream, CancelToken cancelToken)
    {
        var buffer = new byte[1];
        var lineLength = 0;
        var lastWasCr = false;

        while (true)
        {
            var read = stream.Read(buffer.AsSpan(0, 1), cancelToken);

            if (read == 0)
                throw new IOException("Unexpected end of proxy response.");

            var b = buffer[0];

            if (lastWasCr)
            {
                if (b == '\n')
                {
                    if (lineLength == 0)
                        return;

                    lineLength = 0;
                    lastWasCr = false;

                    continue;
                }

                lastWasCr = false;
            }

            if (b == '\r')
            {
                lastWasCr = true;

                continue;
            }

            lineLength++;
        }
    }

    private static bool IsAsciiLetter(byte b) =>
        b is >= (byte)'A' and <= (byte)'Z' || b is >= (byte)'a' and <= (byte)'z';
}
