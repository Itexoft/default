// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Http.Internal;

internal sealed class NetHttpEmptyBodyStream : StreamBase, IStreamRa
{
    public static NetHttpEmptyBodyStream Instance { get; } = new();

    public StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default) => new(0);

    protected override StackTask DisposeAny() => default;
}

internal sealed class NetHttpContentLengthStream(NetHttpBufferedReader reader, long length) : StreamBase, IStreamRa
{
    private long remaining = length;

    public async StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        if (this.remaining <= 0)
            return 0;

        var toRead = (int)Math.Min(this.remaining, buffer.Length);

        if (toRead == 0)
            return 0;

        var read = await reader.ReadAsync(buffer[..toRead], cancelToken);

        if (read == 0)
            throw new EndOfStreamException();

        this.remaining -= read;

        return read;
    }

    protected override StackTask DisposeAny() => default;
}

internal sealed class NetHttpUntilCloseStream(NetHttpBufferedReader reader) : StreamBase, IStreamRa
{
    public StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default) =>
        reader.ReadAsync(buffer, cancelToken);

    protected override StackTask DisposeAny() => default;
}

internal sealed class NetHttpChunkedStream(NetHttpBufferedReader reader, int maxLineSize) : StreamBase, IStreamRa
{
    private bool completed;
    private long remaining;

    public async StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        if (this.completed)
            return 0;

        if (this.remaining == 0)
            await this.ReadNextChunkAsync(cancelToken);

        if (this.completed || this.remaining == 0)
            return 0;

        var toRead = (int)Math.Min(this.remaining, buffer.Length);

        if (toRead == 0)
            return 0;

        var read = await reader.ReadAsync(buffer[..toRead], cancelToken);

        if (read == 0)
            throw new EndOfStreamException();

        this.remaining -= read;

        if (this.remaining == 0)
            await this.ConsumeChunkTerminator(cancelToken);

        return read;
    }

    protected override StackTask DisposeAny() => default;

    private async StackTask ReadNextChunkAsync(CancelToken cancelToken)
    {
        var line = await reader.ReadLineAsync(maxLineSize, cancelToken);
        var span = line.Span;
        var semi = span.IndexOf((byte)';');

        if (semi >= 0)
            span = span[..semi];

        span = NetHttpParsing.TrimOws(span);

        if (!NetHttpParsing.TryParseHexInt64(span, out var length))
            throw new IOException("Invalid chunked length.");

        if (length == 0)
        {
            await this.DrainTrailers(cancelToken);
            this.completed = true;

            return;
        }

        this.remaining = length;
    }

    private async StackTask ConsumeChunkTerminator(CancelToken cancelToken)
    {
        var terminator = new byte[2];
        await reader.ReadExactAsync(terminator.AsMemory(), cancelToken);

        if (terminator[0] != (byte)'\r' || terminator[1] != (byte)'\n')
            throw new IOException("Invalid chunk terminator.");
    }

    private async StackTask DrainTrailers(CancelToken cancelToken)
    {
        while (true)
        {
            var line = await reader.ReadLineAsync(maxLineSize, cancelToken);

            if (line.Length == 0)
                break;
        }
    }
}

internal sealed class NetHttpResponseBodyStream(IStreamRa inner, bool closeAfterBody, NetHttpConnectionLease lease) : StreamBase, IStreamRa
{
    private readonly bool closeAfterBody = closeAfterBody;
    private readonly IStreamRa inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly NetHttpConnectionLease lease = lease ?? throw new ArgumentNullException(nameof(lease));
    private int completed;

    public async StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        if (Volatile.Read(ref this.completed) != 0)
            return 0;

        var read = await this.inner.ReadAsync(buffer, cancelToken);

        if (read == 0)
            await this.CompleteAsync(false);

        return read;
    }

    protected override StackTask DisposeAny() => this.CompleteAsync(true);

    private async StackTask CompleteAsync(bool forceClose)
    {
        if (Interlocked.Exchange(ref this.completed, 1) != 0)
            return;

        await this.inner.DisposeAsync();
        await this.lease.ReleaseAsync(forceClose || this.closeAfterBody);
    }
}
