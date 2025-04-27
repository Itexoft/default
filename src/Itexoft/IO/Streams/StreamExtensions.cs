// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;
using Itexoft.Extensions;
using Itexoft.IO.Streams;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.IO;

public static class StreamExtensions
{
    private static int ReadAtLeastCore<T>(IStreamR<T> stream, Span<T> buffer, int minimumBytes, CancelToken cancelToken)
    {
        var totalRead = 0;

        while (totalRead < minimumBytes)
        {
            var read = stream.Read(buffer[totalRead..], cancelToken);

            if (read == 0)
                break;

            totalRead += read;
        }

        return totalRead;
    }

    extension(IStreamR<byte> stream)
    {
        public ReadOnlyMemory<byte> ReadLineBytes(CancelToken cancelToken = default)
        {
            var writer = new DynamicMemory<byte>(64);

            try
            {
                for (;;)
                {
                    var read = stream.Read(writer.GetSpan(1), cancelToken);

                    if (read == 0)
                        return writer.Length == 0 ? ReadOnlyMemory<byte>.Empty : writer.AsSpan().ToArray();

                    writer.Advance(read);

                    if (writer[^1] == '\n')
                        break;
                }

                var result = writer[..^1];

                if (!result.IsEmpty && result[^1] == '\r')
                    result = result[..^1];

                return result.ToArray();
            }
            finally
            {
                writer.Dispose();
            }
        }

        public string ReadLine(Encoding encoding, CancelToken cancelToken = default)
        {
            stream.Required();
            encoding.Required();

            return encoding.GetString(stream.ReadLineBytes(cancelToken).Span);
        }
    }

    extension(IStreamSeek stream)
    {
        public bool IsEnd => stream.Position >= stream.Length;
    }

    extension<T>(IStreamR<T> stream)
    {
        public void CopyTo(IStreamW<T> target, int bufferSize = 8192)
        {
            stream.Required();
            target.Required();

            for (Span<T> one = new T[bufferSize.RequiredPositive()];;)
            {
                var read = stream.Read(one);

                if (read == 0)
                    break;

                target.Write(one[..read]);
            }
        }
    }

    extension<T>(IStreamR<T> stream) where T : unmanaged
    {
        public ReadOnlyMemory<T> ReadToEnd(CancelToken cancelToken = default)
        {
            if (stream is IStreamRs<T> streamRs)
            {
                Memory<T> memory = new T[streamRs.Length];

                for (var r = 0; r < memory.Length;)
                    r += stream.Read(memory.Span[r..], cancelToken);

                return memory;
            }

            var dm = new DynamicMemory<T>();

            try
            {
                for (;;)
                {
                    var read = stream.Read(dm.GetSpan(1024), cancelToken);

                    if (read == 0)
                        break;

                    dm.Advance(read);
                }

                return dm.ToMemory();
            }
            finally
            {
                dm.Dispose();
            }
        }
    }

    extension<T>(IStreamR<T> stream)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryRead(out T value)
        {
            using (var pool = MemoryPool<T>.Shared.Rent(1))
            {
                var one = pool.Memory.Span[..1];

                if (stream.Read(one) > 0)
                {
                    value = one[0];

                    return true;
                }

                value = default!;

                return false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Read(T[] buffer, int offset, int count)
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
        public int ReadAtMost(Span<T> buffer) => stream.ReadAtLeast(buffer, buffer.Length, false);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void ReadExactly(Span<T> buffer) => stream.ReadAtLeast(buffer, buffer.Length, true);

        public int ReadAtLeast(Span<T> buffer, int minimumBytes, bool throwOnEndOfStream = true)
        {
            if ((uint)minimumBytes > (uint)buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));

            if (minimumBytes == 0)
                return 0;

            var totalRead = 0;

            while (totalRead < minimumBytes)
            {
                var read = stream.Read(buffer[totalRead..]);

                if (read == 0)
                    break;

                totalRead += read;
            }

            if (totalRead < minimumBytes && throwOnEndOfStream)
                throw new EndOfStreamException();

            return totalRead;
        }
    }

    extension<T>(IStreamR<T> stream)
    {
        public int ReadNonZero(Span<T> buffer, CancelToken cancelToken = default)
        {
            var read = 0;

            while (read == 0)
                read = stream.Read(buffer, cancelToken);

            return read;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T[] ReadExactValues(int count, CancelToken cancelToken = default)
        {
            if (count == 0)
                return [];

            var values = new T[count];
            ReadAtLeastCore(stream, values, count, cancelToken);

            return values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int ReadExact(Span<T> buffer, CancelToken cancelToken = default)
        {
            if (buffer.Length == 0)
                return 0;

            return ReadAtLeastCore(stream, buffer, buffer.Length, cancelToken);
        }

        public int ReadAtLeast(Span<T> buffer, int minimumBytes, CancelToken cancelToken = default)
        {
            if (minimumBytes < 0 || minimumBytes > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(minimumBytes));

            if (minimumBytes == 0 || buffer.Length == 0)
                return 0;

            return ReadAtLeastCore(stream, buffer, minimumBytes, cancelToken);
        }

        public Promise OnRead(Action<Memory<T>> onRead, Memory<T> memory, CancelToken cancelToken = default) => Promise.Run(
            () =>
            {
                for (;;)
                {
                    var read = stream.Read(memory.Span, cancelToken.ThrowIf());
                    cancelToken.ThrowIf();
                    onRead(memory[..read]);
                }
            },
            false,
            cancelToken);

        public Promise OnRead(Func<Memory<T>, Promise> onRead, Memory<T> memory, CancelToken cancelToken = default) => Promise.Run(
            () =>
            {
                for (;;)
                {
                    var read = stream.Read(memory.Span, cancelToken.ThrowIf());
                    cancelToken.ThrowIf();
                    onRead(memory[..read]);
                }
            },
            false,
            cancelToken);
    }

    extension<T>(IStreamRwsl<T> stream)
    {
        public void Overwrite(ReadOnlySpan<T> value, CancelToken cancelToken = default)
        {
            stream.Position = 0;
            stream.Length = 0;
            stream.Write(value, cancelToken);
        }
    }

    extension<T>(IStreamR<T> stream)
    {
        public IEnumerable<T> AsEnumerable(CancelToken cancelToken = default)
        {
            while (stream.TryRead(out var value))
                yield return value;
        }
    }

    extension<T>(IStreamW<T> stream)
    {
        public void Write(T item, CancelToken cancelToken = default)
        {
            using (var memory = MemoryPool<T>.Shared.Rent())
            {
                var span = memory.Memory.Span[..1];
                span[0] = item;

                stream.Write(span, cancelToken);
            }
        }
    }
}
