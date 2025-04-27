// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Threading.Atomics;

namespace Itexoft.IO;

internal ref struct PositionalByteStream(IStreamRwsl<byte> stream, ref PositionalByteStreamSync sync)
{
    private readonly IStreamRwsl<byte> stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly ref PositionalByteStreamSync sync = ref sync;

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.stream.Length;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => this.stream.Length = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int ReadAt(long offset, Span<byte> destination)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (this.stream is IPositionalByteStream positional)
        {
            var claim = this.sync.EnterReadRange(offset, destination.Length);

            try
            {
                return positional.ReadAt(offset, destination);
            }
            finally
            {
                this.sync.ExitRange(claim);
            }
        }

        destination.Clear();

        if (offset >= this.stream.Length)
            return 0;

        using var hold = this.sync.Cursor.Enter();
        this.stream.Position = offset;
        var read = 0;

        while (read < destination.Length)
        {
            var delta = this.stream.Read(destination[read..]);

            if (delta == 0)
                break;

            read += delta;
        }

        return read;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ReadExactlyAt(long offset, Span<byte> destination)
    {
        var read = this.ReadAt(offset, destination);

        if (read != destination.Length)
            throw new EndOfStreamException("Unexpected end of stream.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteAt(long offset, ReadOnlySpan<byte> source)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (this.stream is IPositionalByteStream positional)
        {
            var claim = this.sync.EnterWriteRange(offset, source.Length);

            try
            {
                positional.WriteAt(offset, source);

                return;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new IOException(
                    $"Positional write requires length {
                        checked(offset + (long)source.Length)
                    } at offset {
                        offset
                    } for payload length {
                        source.Length
                    }.",
                    exception);
            }
            finally
            {
                this.sync.ExitRange(claim);
            }
        }

        using var hold = this.sync.Cursor.Enter();
        var requiredLength = checked(offset + (long)source.Length);

        if (requiredLength > this.stream.Length)
        {
            try
            {
                this.stream.Length = requiredLength;
            }
            catch (ArgumentOutOfRangeException exception)
            {
                throw new IOException(
                    $"Positional write requires length {requiredLength} at offset {offset} for payload length {source.Length}.",
                    exception);
            }
        }

        this.stream.Position = offset;
        this.stream.Write(source);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteExactlyAt(long offset, ReadOnlySpan<byte> source) => this.WriteAt(offset, source);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush() => this.stream.Flush();
}
