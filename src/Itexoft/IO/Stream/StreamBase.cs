// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.IO;

public abstract class StreamBase : IStream
{
    private Disposed disposed = new();

    public StackTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return default;

        GC.SuppressFinalize(this);

        return this.DisposeAny();
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        GC.SuppressFinalize(this);

        var task = this.DisposeAny();

        if (!task.IsCompletedSuccessfully)
            task.GetAwaiter().GetResult();
    }

    protected void ThrowIfDisposed() => this.disposed.ThrowIf();

    protected abstract StackTask DisposeAny();
}

public abstract class StreamBase<T> : StreamBase, IStream<T> where T : unmanaged
{
    protected StreamBase()
    {
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            throw new NotSupportedException();
    }
}

public static class StreamExtensions
{
    private static async StackTask<int> ReadAtLeastAsyncCore(IStreamRa stream, Memory<byte> buffer, int minimumBytes, CancelToken cancelToken)
    {
        var totalRead = 0;

        while (totalRead < minimumBytes)
        {
            var read = await stream.ReadAsync(buffer[totalRead..], cancelToken);

            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }

    extension(IStreamR stream)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count > buffer.Length - offset)
                throw new ArgumentOutOfRangeException(nameof(count));

            return stream.Read(buffer.AsSpan(offset, count));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadByte()
        {
            Span<byte> one = stackalloc byte[1];
            var read = stream.Read(one);

            return read == 0 ? -1 : one[0];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadExactly(Span<byte> buffer) => stream.ReadAtLeast(buffer, buffer.Length, true);

        public int ReadAtLeast(Span<byte> buffer, int minimumBytes, bool throwOnEndOfStream = true)
        {
            if ((uint)minimumBytes > (uint)buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));

            if (minimumBytes == 0)
                return 0;

            var totalRead = 0;

            while (totalRead < minimumBytes)
            {
                var read = stream.Read(buffer.Slice(totalRead));

                if (read == 0)
                    break;

                totalRead += read;
            }

            if (totalRead < minimumBytes && throwOnEndOfStream)
                throw new EndOfStreamException();

            return totalRead;
        }
    }

    extension(IStreamW stream)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(byte[] buffer, int offset, int count)
        {
            if (buffer is null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count > (uint)(buffer.Length - offset))
                throw new ArgumentOutOfRangeException(nameof(count));

            stream.Write(buffer.AsSpan(offset, count));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteByte(byte value) => stream.Write([value]);
    }

    extension(IStreamRa stream)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StackTask<int> ReadExactAsync(Memory<byte> buffer, CancelToken cancelToken = default)
        {
            if (buffer.Length == 0)
                return new(0);

            return ReadAtLeastAsyncCore(stream, buffer, buffer.Length, cancelToken);
        }

        public StackTask<int> ReadAtLeastAsync(Memory<byte> buffer, int minimumBytes, CancelToken cancelToken = default)
        {
            if (minimumBytes < 0 || minimumBytes > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));

            if (minimumBytes == 0 || buffer.Length == 0)
                return new(0);

            return ReadAtLeastAsyncCore(stream, buffer, minimumBytes, cancelToken);
        }
    }
}
