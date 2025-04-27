// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.IO;

file class BclStreamRa(Stream stream, bool ownStream) : StreamWrapper(stream, ownStream), IStreamR<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        return this.BclStream.Read(buffer);
    }
}

file class BclStreamRwa(Stream stream, bool ownStream) : BclStreamRa(stream, ownStream), IStreamRw<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Write(buffer);
    }
}

file class BclStreamRas(Stream stream, bool ownStream) : BclStreamRa(stream, ownStream), IStreamRs<byte>
{
    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
    }
}

file class BclStreamRwas(Stream stream, bool ownStream) : BclStreamRwa(stream, ownStream), IStreamRws<byte>
{
    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
    }
}

public static partial class BclStreamWrapperExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamR<byte> AsAstreamR(this Stream stream, bool ownStream = true) => new BclStreamRa(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamW<byte> AsAstreamW(this Stream stream, bool ownStream = true) => new BclStreamRwa(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamWs<byte> AsAstreamWs(this Stream stream, bool ownStream = true) => new BclStreamRwas(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRw<byte> AsAstreamRw(this Stream stream, bool ownStream = true) => new BclStreamRwa(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRs<byte> AsAstreamRs(this Stream stream, bool ownStream = true) => new BclStreamRas(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRws<byte> AsAstreamRws(this Stream stream, bool ownStream = true) => new BclStreamRwas(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamR<byte> AsStreamR(this Stream stream, bool ownStream = true) => new BclStreamR(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRw<byte> AsStreamRw(this Stream stream, bool ownStream = true) => new BclStreamRw(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRs<byte> AsStreamRs(this Stream stream, bool ownStream = true) => new BclStreamRs(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRwsl<byte> AsStreamRwsl(this Stream stream, bool ownStream = true) => new BclStreamRwsl(stream, ownStream);
}

file class BclStreamR(Stream stream, bool ownStream) : StreamWrapper(stream, ownStream), IStreamR<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        return this.BclStream.Read(buffer);
    }
}

file class BclStreamRw(Stream stream, bool ownStream) : BclStreamR(stream, ownStream), IStreamRw<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Write(buffer);
    }
}

file class BclStreamRs(Stream stream, bool ownStream) : BclStreamR(stream, ownStream), IStreamRs<byte>
{
    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
    }

    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }
}

file class BclStreamRwsl(Stream stream, bool ownStream) : BclStreamRw(stream, ownStream), IStreamRwsl<byte>, IPositionalByteStream
{
    private AtomicLock cursor;

    public int ReadAt(long offset, Span<byte> destination)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (this.BclStream is IPositionalByteStream positional)
            return positional.ReadAt(offset, destination);

        if (this.BclStream is not FileStream fileStream)
        {
            if (offset >= this.BclStream.Length)
                return 0;

            using var hold = this.cursor.Enter();
            this.BclStream.Position = offset;
            var bufferedRead = 0;

            while (bufferedRead < destination.Length)
            {
                var delta = this.BclStream.Read(destination[bufferedRead..]);

                if (delta == 0)
                    break;

                bufferedRead += delta;
            }

            return bufferedRead;
        }

        var read = 0;

        while (read < destination.Length)
        {
            var delta = RandomAccess.Read(fileStream.SafeFileHandle, destination[read..], offset + read);

            if (delta == 0)
                break;

            read += delta;
        }

        return read;
    }

    public void WriteAt(long offset, ReadOnlySpan<byte> source)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        if (this.BclStream is IPositionalByteStream positional)
        {
            positional.WriteAt(offset, source);

            return;
        }

        if (this.BclStream is not FileStream fileStream)
        {
            using var hold = this.cursor.Enter();
            var requiredLength = checked(offset + (long)source.Length);

            if (requiredLength > this.BclStream.Length)
                this.BclStream.SetLength(requiredLength);

            this.BclStream.Position = offset;
            this.BclStream.Write(source);

            return;
        }

        RandomAccess.Write(fileStream.SafeFileHandle, source, offset);
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
        set => this.BclStream.SetLength(value);
    }

    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }
}
