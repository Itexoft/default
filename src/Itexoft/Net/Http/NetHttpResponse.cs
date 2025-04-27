// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.IO;
using Itexoft.IO.Streams;
using Itexoft.Threading;

namespace Itexoft.Net.Http;

public readonly record struct NetHttpResponse
{
    public NetHttpResponse(NetHttpStatus status, NetHttpHeaders? headers = null, IStreamR<byte>? body = null) : this(
        NetHttpVersion.Version11,
        status,
        headers,
        body) { }

    public NetHttpResponse(NetHttpVersion httpVersion, NetHttpStatus status, NetHttpHeaders? headers = null, IStreamR<byte>? body = null)
    {
        this.HttpVersion = httpVersion;
        this.Status = status;
        this.Headers = headers ?? new NetHttpHeaders();
        this.Body = body;

        if (body is IStreamRs<byte> lengthStream)
            this.Headers.ContentLength = lengthStream.Length - lengthStream.Position;
    }

    public NetHttpVersion HttpVersion { get; }
    public NetHttpStatus Status { get; }
    public NetHttpStatusClass StatusClass => this.Status.GetClass();
    public NetHttpHeaders Headers { get; }
    public IStreamR<byte>? Body { get; }
    public bool IsSuccess => this.StatusClass == NetHttpStatusClass.Success;
    public long? Length => this.Headers.ContentLength;

    public bool KeepAlive
    {
        get => this.Headers.Connection?.Equals("keep-alive", StringComparison.OrdinalIgnoreCase) ?? false;
        set => this.Headers.Connection = value ? "keep-alive" : "close";
    }

    public string ReadAsString(CancelToken cancelToken = default)
    {
        if (this.Body is null)
            return string.Empty;

        var memory = this.ReadAsBytes(cancelToken);

        return this.Headers.ContentTypeEncoding.GetString(memory.Span);
    }

    public ReadOnlyMemory<byte> ReadAsBytes(CancelToken cancelToken = default)
    {
        if (this.Body is null)
            return ReadOnlyMemory<byte>.Empty;

        using (this.Body)
        {
            using var poolBuffer = MemoryPool<byte>.Shared.Rent(16 * 1024);
            var writer = new DynamicMemory<byte>(16 * 1024);

            try
            {
                var memory = poolBuffer.Memory;

                while (true)
                {
                    var read = this.Body.Read(memory.Span, cancelToken);

                    if (read == 0)
                        break;

                    writer.Write(memory.Span[..read]);
                }

                return writer.ToMemory();
            }
            finally
            {
                writer.Dispose();
            }
        }
    }

    public void EnsureSuccess()
    {
        if (this.IsSuccess)
            return;

        throw new HttpProtocolException((int)this.Status, "HTTP Error", null);
    }
}
