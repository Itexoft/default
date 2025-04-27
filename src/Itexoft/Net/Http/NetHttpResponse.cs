// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text;
using Itexoft.Core;
using Itexoft.IO;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Http;

public sealed class NetHttpResponse : ITaskDisposable
{
    private Disposed disposed = new();

    internal NetHttpResponse(INetHttpRequestInfo request, NetHttpVersion version, NetHttpStatus status, NetHttpHeaders headers, IStreamRa body)
    {
        this.Request = request;
        this.Version = version;
        this.Status = status;
        this.Headers = headers;
        this.Body = body;
    }

    public INetHttpRequestInfo Request { get; }
    public NetHttpVersion Version { get; }
    public NetHttpStatus Status { get; }
    public NetHttpStatusClass StatusClass => this.Status.GetClass();
    public NetHttpHeaders Headers { get; }
    public IStreamRa Body { get; }
    public bool IsSuccess => this.StatusClass == NetHttpStatusClass.Success;

    public StackTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return default;

        return this.Body.DisposeAsync();
    }

    public StackTask<string> ReadAsStringAsync(CancelToken cancelToken = default) => this.ReadAsStringAsync(Encoding.UTF8, cancelToken);

    public async StackTask<string> ReadAsStringAsync(Encoding? encoding, CancelToken cancelToken = default)
    {
        var bytes = await this.ReadAsBytes(cancelToken);
        encoding ??= ResolveEncoding(this.Headers);

        return encoding.GetString(bytes);
    }

    public async StackTask<ReadOnlyMemory<byte>> ReadAsMemory(CancelToken cancelToken = default) =>
        await this.ReadAsBytes(cancelToken);

    public async StackTask<byte[]> ReadAsBytes(CancelToken cancelToken = default)
    {
        var timeout = this.Request.Timeout ?? this.Request.RequestTimeout;
        cancelToken = NetHttpClient.ApplyTimeout(cancelToken, timeout, false);
        var poolBuffer = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var writer = new ArrayBufferWriter<byte>(16 * 1024);

        try
        {
            while (true)
            {
                var read = await this.Body.ReadAsync(poolBuffer, cancelToken);

                if (read == 0)
                    break;

                writer.Write(poolBuffer.AsSpan(0, read));
            }

            return writer.WrittenSpan.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(poolBuffer);
            await this.Body.DisposeAsync();
        }
    }

    private static Encoding ResolveEncoding(NetHttpHeaders headers)
    {
        var charset = headers.ContentType.Charset;

        if (!string.IsNullOrEmpty(charset))
        {
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch
            {
                // ignored
            }
        }

        return Encoding.UTF8;
    }

    public void EnsureSuccess()
    {
        if (this.IsSuccess)
            return;

        throw new HttpProtocolException((int)this.Status, "HTTP Error", null);
    }
}
