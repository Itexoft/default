// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.IO;

file sealed class ToBclStreamWrapper(IStream<byte> stream, bool leaveOpen) : Stream
{
    private readonly bool leaveOpen = leaveOpen;
    private readonly IStream<byte> stream = stream.Required();
    private Disposed disposed = new();

    public override long Length => ((IStreamSeek)this.stream).Length;

    public override long Position
    {
        get => ((IStreamSeek)this.stream).Position;
        set => ((IStreamSeek)this.stream).Position = value;
    }

    public override bool CanWrite { get; } = stream is IStreamW<byte> or IStreamW<byte>;
    public override bool CanSeek { get; } = stream is IStreamSeek;
    public override bool CanTimeout { get; } = false;
    public override bool CanRead { get; } = stream is IStreamR<byte> or IStreamR<byte>;

    protected override void Dispose(bool disposing)
    {
        if (this.leaveOpen || this.disposed.Enter())
            return;

        if (this.stream is IDisposable d)
            d.Dispose();
    }

    public override int Read(Span<byte> buffer) => this.stream switch
    {
        IStreamR<byte> s => s.Read(buffer),
        _ => 0,
    };

    public override void Flush()
    {
        if (this.stream is IStreamW stream)
            stream.Flush();
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (this.stream is IStreamW<byte> s)
            s.Write(buffer);
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var memory = buffer.AsMemory(offset, count);

        if (memory.Length == 0)
            return 0;

        return this.stream switch
        {
            IStreamR<byte> s => s.Read(memory.Span),
            _ => 0,
        };
    }

    public override int ReadByte()
    {
        if (this.stream is IStreamR<byte> s)
            return s.TryRead(out var value) ? value : -1;

        return 0;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        var memory = buffer.AsMemory(offset, count);

        if (this.stream is IStreamW<byte> s)
            s.Write(memory.Span);
    }

    public override void WriteByte(byte value)
    {
        if (this.stream is IStreamW<byte> s)
            s.Write(value);
    }

    public override long Seek(long offset, SeekOrigin origin) => origin switch
    {
        SeekOrigin.Begin => ((IStreamSeek)this.stream).Position = offset,
        SeekOrigin.Current => ((IStreamSeek)this.stream).Position += offset,
        SeekOrigin.End => ((IStreamSeek)this.stream).Position = this.Length + offset,
        _ => throw new ArgumentOutOfRangeException(nameof(origin), origin, null),
    };

    public override void SetLength(long value)
    {
        if (this.stream is IStreamRwsl<byte> stream)
        {
            stream.Length = value;

            return;
        }

        if (this.stream is IStreamWs<byte> seekable)
        {
            seekable.Position = value;

            return;
        }

        throw new NotSupportedException();
    }
}

partial class StreamWrapper
{
    protected StreamWrapper() => this.wrapper = new(() => new ToBclStreamWrapper(this, false));
}

public static partial class BclStreamWrapperExtensions
{
    extension(IStream<byte> stream)
    {
        public Stream AsStream()
        {
            if (stream is StreamWrapper streamBase)
                return streamBase;

            return new ToBclStreamWrapper(stream, false);
        }
    }
}
