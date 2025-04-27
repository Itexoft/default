// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Net.Http.Internal;

internal abstract class NetHttpStream(Action? dispose = null) : IStreamR<byte>
{
    private Action? dispose = dispose;
    private Disposed disposed = new();

    public long Length { get; private protected set; }

    public long Position { get; private protected set; }

    public int Read(Span<byte> destination, CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf(cancelToken);

        if (Atomic.IsNull(ref this.dispose!))
            return 0;

        return this.ReadImpl(destination, cancelToken);
    }

    public virtual void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.DisposeStream();
    }

    private protected abstract int ReadImpl(Span<byte> destination, CancelToken cancelToken = default);

    private protected void DisposeStream()
    {
        if (Atomic.NullOut(ref this.dispose!, out var dispose))
            dispose.Invoke();
    }
}

internal sealed class NetHttpEmptyBodyStream : NetHttpStream
{
    private protected override int ReadImpl(Span<byte> destination, CancelToken cancelToken = default) => 0;
}

internal sealed class NetHttpContentLengthStream : NetHttpStream
{
    private readonly IStreamR<byte> reader;

    public NetHttpContentLengthStream(IStreamR<byte> reader, long length, Action dispose) : base(dispose)
    {
        this.Length = length;
        this.reader = reader.Required();
    }

    private protected override int ReadImpl(Span<byte> buffer, CancelToken cancelToken = default)
    {
        if (this.Position >= this.Length)
        {
            this.DisposeStream();

            return 0;
        }

        var toRead = (int)Math.Min(this.Length - this.Position, buffer.Length);

        if (toRead == 0)
        {
            this.DisposeStream();

            return 0;
        }

        var read = this.reader.ReadNonZero(buffer[..toRead], cancelToken);

        this.Position += read;

        if (this.Position >= this.Length)
            this.DisposeStream();

        return read;
    }
}

internal sealed class NetHttpChunkedStream : NetHttpStream
{
    private readonly Memory<byte> memory;
    private readonly IMemoryOwner<byte> memoryOwner = MemoryPool<byte>.Shared.Rent(32);
    private readonly IStreamR<byte> reader;
    private long chunkLength;

    public NetHttpChunkedStream(IStreamR<byte> reader, Action dispose) : base(dispose)
    {
        this.reader = reader;
        this.memory = this.memoryOwner.Memory;
    }

    private protected override int ReadImpl(Span<byte> buffer, CancelToken cancelToken = default)
    {
        for (var bi = 0;;)
        {
            for (var i = 0; this.chunkLength == 0 && i < this.memory.Length;)
            {
                i += this.reader.ReadNonZero(this.memory.Span.Slice(i, 1), cancelToken);

                if (i >= 2 && this.memory.Span[i - 2] == (byte)'\r' && this.memory.Span[i - 1] == (byte)'\n')
                {
                    if (i == 2)
                    {
                        this.DisposeStream();

                        return bi;
                    }

                    if (!NetHttpParsing.TryParseHexInt64(this.memory.Span[..(i - 2)], out var chunkLength) || chunkLength < 0)
                        throw new IOException("Invalid chunked length.");

                    this.chunkLength = chunkLength;

                    if (this.chunkLength == 0)
                    {
                        i = 0;

                        continue;
                    }

                    break;
                }
            }

            for (var bl = buffer.Length; bl > 0;)
            {
                if (bi == bl)
                    return bi;

                var r = this.reader.ReadNonZero(buffer.Slice(bi, (int)Math.Min(this.chunkLength, bl - bi)), cancelToken);
                bi += r;
                this.chunkLength -= r;

                for (var i = 0; this.chunkLength == 0;)
                {
                    i += this.reader.ReadNonZero(this.memory.Span.Slice(i, 1), cancelToken);

                    if (i == 2)
                    {
                        if (this.memory.Span[0] != (byte)'\r' || this.memory.Span[1] != (byte)'\n')
                            throw new IOException("Invalid chunk");

                        bl = 0;

                        break;
                    }
                }
            }
        }
    }

    public override void Dispose()
    {
        this.memoryOwner.Dispose();

        base.Dispose();
    }
}

internal sealed class NetHttpResponseBodyStream(IStreamR<byte> inner, Action dispose) : NetHttpStream(dispose)
{
    private readonly IStreamR<byte> inner = inner.Required();

    private protected override int ReadImpl(Span<byte> buffer, CancelToken cancelToken = default)
    {
        var read = this.inner.Read(buffer, cancelToken);

        if (read == 0)
            this.DisposeStream();

        return read;
    }
}
