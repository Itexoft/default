// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Interop;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.IO;

file sealed class ToBclStreamWrapper : Stream, IStreamRa, IStreamWa, IStreamRt, IStreamWt
{
    private readonly IStreamRa? asyncReader;
    private readonly IAsyncStream? asyncStream;
    private readonly IStreamWa? asyncWriter;
    private readonly bool leaveOpen;
    private readonly IStreamR? reader;
    private readonly IStreamRt? readerT;
    private readonly IStreamS? seeker;
    private readonly ISyncStream? syncStream;
    private readonly IStreamW? writer;
    private readonly IStreamSl? writerSl;
    private readonly IStreamWt? writerT;

    public ToBclStreamWrapper(IStream stream, bool leaveOpen)
    {
        this.leaveOpen = leaveOpen;
        this.syncStream = stream as ISyncStream;
        this.asyncStream = stream as IAsyncStream;
        this.asyncReader = stream as IStreamRa;
        this.asyncWriter = stream as IStreamWa;
        this.reader = stream as IStreamR;
        this.seeker = stream as IStreamS;
        this.readerT = stream as IStreamRt;
        this.writerT = stream as IStreamWt;
        this.writer = stream as IStreamW;
        this.writerSl = stream as IStreamSl;
        this.CanWrite = this.writer is not null || this.asyncWriter is not null;
        this.CanRead = this.reader is not null || this.asyncReader is not null;
        this.CanSeek = this.seeker is not null;
        this.CanTimeout = this.writerT is not null || this.readerT is not null || this.asyncReader is IStreamRta || this.asyncWriter is IStreamWta;
    }

    public override long Length => this.seeker!.Length;

    public override long Position
    {
        get => this.seeker!.Position;
        set => this.seeker!.Position = value;
    }

    public override bool CanWrite { get; }
    public override bool CanSeek { get; }
    public override bool CanTimeout { get; }
    public override bool CanRead { get; }

    public override int ReadTimeout
    {
        get
        {
            if (this.readerT is not null)
                return this.readerT.ReadTimeout.TimeoutMilliseconds;

            if (this.asyncReader is IStreamRta asyncReaderT)
                return asyncReaderT.ReadTimeout.TimeoutMilliseconds;

            throw new NotSupportedException();
        }
        set
        {
            if (this.readerT is not null)
            {
                this.readerT.ReadTimeout = TimeSpan.FromMilliseconds(value);

                return;
            }

            if (this.asyncReader is IStreamRta asyncReaderT)
            {
                asyncReaderT.ReadTimeout = TimeSpan.FromMilliseconds(value);

                return;
            }

            throw new NotSupportedException();
        }
    }

    public override int WriteTimeout
    {
        get
        {
            if (this.writerT is not null)
                return this.writerT.WriteTimeout.TimeoutMilliseconds;

            if (this.asyncWriter is IStreamWta asyncWriterT)
                return asyncWriterT.WriteTimeout.TimeoutMilliseconds;

            throw new NotSupportedException();
        }
        set
        {
            if (this.writerT is not null)
            {
                this.writerT.WriteTimeout = TimeSpan.FromMilliseconds(value);

                return;
            }

            if (this.asyncWriter is IStreamWta asyncWriterT)
            {
                asyncWriterT.WriteTimeout = TimeSpan.FromMilliseconds(value);

                return;
            }

            throw new NotSupportedException();
        }
    }

    StackTask<int> IStreamRa.ReadAsync(Memory<byte> buffer, CancelToken cancelToken) => this.asyncReader!.ReadAsync(buffer, cancelToken);

    StackTask ITaskDisposable.DisposeAsync() => this.DisposeAsync();

    public async override ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        if (this.asyncStream is ITaskDisposable ad)
            await ad.DisposeAsync();
    }

    public override int Read(Span<byte> buffer)
    {
        if (this.reader is not null)
            return this.reader.Read(buffer);

        if (this.asyncReader is null)
            throw new NotSupportedException();

        var temp = ArrayPool<byte>.Shared.Rent(buffer.Length);

        try
        {
            var read = this.asyncReader.ReadAsync(temp.AsMemory(0, buffer.Length), CancelToken.None).GetAwaiter().GetResult();
            temp.AsSpan(0, read).CopyTo(buffer);

            return read;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(temp);
        }
    }

    TimeSpan IStreamTr.ReadTimeout
    {
        get => this.readerT!.ReadTimeout;
        set => this.readerT!.ReadTimeout = value;
    }

    StackTask IStreamWa.WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken) => this.asyncWriter!.WriteAsync(buffer, cancelToken);

    StackTask IStreamWa.FlushAsync(CancelToken cancelToken) => this.asyncWriter!.FlushAsync(cancelToken);

    public override void Flush() => this.FlushFallback();

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (this.writer is not null)
        {
            this.writer.Write(buffer);

            return;
        }

#pragma warning disable AsMemory
        this.asyncWriter!.WriteAsync(buffer.AsMemory(), CancelToken.None).GetAwaiter().GetResult();
#pragma warning restore AsMemory
    }

    TimeSpan IStreamTw.WriteTimeout
    {
        get => this.writerT!.WriteTimeout;
        set => this.writerT!.WriteTimeout = value;
    }

    public async override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (this.asyncReader is null)
            return await base.ReadAsync(buffer, cancellationToken);

        return await this.asyncReader.ReadAsync(buffer, cancellationToken);
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        return this.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public async override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (this.asyncWriter is null)
        {
            await base.WriteAsync(buffer, cancellationToken);

            return;
        }

        await this.asyncWriter.WriteAsync(buffer, cancellationToken);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        return this.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }

    public async override Task FlushAsync(CancellationToken cancellationToken)
    {
        if (this.asyncWriter is null)
        {
            await base.FlushAsync(cancellationToken);

            return;
        }

        await this.asyncWriter.FlushAsync(cancellationToken);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer is null or { Length: 0 } || count <= 0)
            return 0;

        if (this.reader is not null)
            return this.reader.Read(buffer, offset, count);

        if (this.asyncReader is null)
            throw new NotSupportedException();

        return this.asyncReader.ReadAsync(buffer.AsMemory(offset, count), CancelToken.None).GetAwaiter().GetResult();
    }

    public override int ReadByte()
    {
        if (this.reader is not null)
            return this.reader.ReadByte();

        if (this.asyncReader is null)
            throw new NotSupportedException();

        var buffer = new byte[1];
        var read = this.asyncReader.ReadAsync(buffer.AsMemory(), CancelToken.None).GetAwaiter().GetResult();

        return read == 0 ? -1 : buffer[0];
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (this.writer is not null)
        {
            this.writer.Write(buffer, offset, count);

            return;
        }

        this.asyncWriter!.WriteAsync(buffer.AsMemory(offset, count), CancelToken.None).GetAwaiter().GetResult();
    }

    public override void WriteByte(byte value)
    {
        if (this.writer is not null)
        {
            this.writer.WriteByte(value);

            return;
        }

        this.asyncWriter!.WriteAsync(new[] { value }.AsMemory(), CancelToken.None).GetAwaiter().GetResult();
    }

    private void FlushFallback()
    {
        if (this.writer is not null)
        {
            this.writer.Flush();

            return;
        }

        this.asyncWriter?.FlushAsync(CancelToken.None).GetAwaiter().GetResult();
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        this.seeker!.Seek(offset, origin);

    public override void SetLength(long value) => this.writerSl!.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.leaveOpen)
            this.syncStream?.Dispose();

        base.Dispose(disposing);
    }
}

partial class StreamWrapper
{
    protected StreamWrapper() => this.wrapper = new(() => new ToBclStreamWrapper(this, false));
}

public static partial class BclStreamWrapperExtensions
{
    extension<T>(T stream) where T : class, IStream
    {
        public Stream AsStream()
        {
            if (stream is StreamWrapper streamBase)
                return streamBase;

            return new ToBclStreamWrapper(stream, false);
        }

        //public T Overlay(T overlay) => Interfaces.Overlay(stream, overlay);
    }
}
