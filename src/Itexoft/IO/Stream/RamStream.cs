// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.IO;

internal unsafe sealed class PinnedRamStreamCore : IDisposable
{
    private static readonly int ptrSize = nint.Size;
    private static readonly int headerSize = nint.Size + 8;
    private static readonly int overheadSize = headerSize + nint.Size;
    private readonly bool clearOnReturn;
    private readonly int initialSegmentSize;
    private readonly int maxSegmentSize;

    private readonly ArrayPool<byte> pool;
    private long capacity;

    private byte* cursorBase;
    private int cursorCapacity;
    private long cursorStart;

    private Disposed disposed = new();

    private byte* head;
    private int lastSegmentRequestSize;

    private int segmentCount;
    private byte* tail;

    public PinnedRamStreamCore(
        int initialSegmentSize = 1024,
        int maxSegmentSize = 1024 * 1024,
        ArrayPool<byte>? pool = null,
        bool clearOnReturn = false)
    {
        if (initialSegmentSize < 1)
            throw new ArgumentOutOfRangeException(nameof(initialSegmentSize));

        if (maxSegmentSize < initialSegmentSize)
            throw new ArgumentOutOfRangeException(nameof(maxSegmentSize));

        if (maxSegmentSize > int.MaxValue - overheadSize)
            throw new ArgumentOutOfRangeException(nameof(maxSegmentSize));

        this.initialSegmentSize = initialSegmentSize;
        this.maxSegmentSize = maxSegmentSize;
        this.pool = pool ?? ArrayPool<byte>.Shared;
        this.clearOnReturn = clearOnReturn;
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
        private set;
    }

    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
        private set;
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.ReleaseAllSegments();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long Seek(long offset, SeekOrigin origin)
    {
        this.ThrowIfDisposed();

        var newPos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => checked(this.Position + offset),
            SeekOrigin.End => checked(this.Length + offset),
            _ => throw new ArgumentOutOfRangeException(nameof(origin)),
        };

        if (newPos < 0)
            throw new IOException("Attempted to seek before beginning of the stream.");

        this.Position = newPos;

        return newPos;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetLength(long value)
    {
        this.ThrowIfDisposed();

        if (value < 0)
            throw new ArgumentOutOfRangeException(nameof(value));

        this.EnsureCapacity(value);

        this.Length = value;

        if (this.Position > value)
            this.Position = value;

        if (value == 0)
            this.ResetCursor();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> buffer)
    {
        this.ThrowIfDisposed();

        if (buffer.Length == 0)
            return 0;

        var remaining = this.Length - this.Position;

        if (remaining <= 0)
            return 0;

        var toRead = (int)Math.Min(remaining, buffer.Length);
        this.ReadCore(this.Position, buffer.Slice(0, toRead));
        this.Position += toRead;

        return toRead;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush() => this.ThrowIfDisposed();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> buffer)
    {
        this.ThrowIfDisposed();

        if (buffer.Length == 0)
            return;

        var start = this.Position;
        var end = checked(start + buffer.Length);

        this.EnsureCapacity(end);
        this.WriteCore(start, buffer);

        this.Position = end;

        if (end > this.Length)
            this.Length = end;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write<T>(in T value) where T : unmanaged
    {
        this.ThrowIfDisposed();

        var size = Unsafe.SizeOf<T>();
        var start = this.Position;
        var end = checked(start + (long)size);

        this.EnsureCapacity(end);

        this.Locate(start, out var segBase, out var segStart, out var segCap);
        var offset = (int)(start - segStart);

        if (segCap - offset >= size)
        {
            Unsafe.WriteUnaligned(ref RefAt(DataPtr(segBase) + offset), value);

            this.Position = end;

            if (end > this.Length)
                this.Length = end;

            this.cursorBase = segBase;
            this.cursorStart = segStart;
            this.cursorCapacity = segCap;

            return;
        }

        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.AsRef(in value), 1));
        this.WriteCore(start, bytes);

        this.Position = end;

        if (end > this.Length)
            this.Length = end;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Read<T>(out T value) where T : unmanaged
    {
        this.ThrowIfDisposed();

        var size = Unsafe.SizeOf<T>();

        if (this.Length - this.Position < size)
        {
            value = default;

            throw new EndOfStreamException();
        }

        var start = this.Position;

        this.Locate(start, out var segBase, out var segStart, out var segCap);
        var offset = (int)(start - segStart);

        if (segCap - offset >= size)
        {
            value = Unsafe.ReadUnaligned<T>(ref RefAt(DataPtr(segBase) + offset));

            this.Position = start + size;

            this.cursorBase = segBase;
            this.cursorStart = segStart;
            this.cursorCapacity = segCap;

            return;
        }

        value = default;
        var bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateSpan(ref value, 1));
        this.ReadCore(start, bytes);
        this.Position = start + size;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryRead<T>(out T value) where T : unmanaged
    {
        var size = Unsafe.SizeOf<T>();

        if (this.Length - this.Position < size)
        {
            value = default;

            return false;
        }

        this.Read(out value);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask DisposeAsync()
    {
        this.Dispose();

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (!this.disposed)
            return;

        throw new ObjectDisposedException(nameof(PinnedRamStreamCore));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetCursor()
    {
        this.cursorBase = null;
        this.cursorStart = 0;
        this.cursorCapacity = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ref byte RefAt(byte* ptr) => ref Unsafe.AsRef<byte>(ptr);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint ReadNint(byte* ptr) => Unsafe.ReadUnaligned<nint>(ref RefAt(ptr));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteNint(byte* ptr, nint value) => Unsafe.WriteUnaligned(ref RefAt(ptr), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadInt(byte* ptr) => Unsafe.ReadUnaligned<int>(ref RefAt(ptr));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteInt(byte* ptr, int value) => Unsafe.WriteUnaligned(ref RefAt(ptr), value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static nint ReadHandlePtr(byte* basePtr) => ReadNint(basePtr);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteHandlePtr(byte* basePtr, nint handlePtr) => WriteNint(basePtr, handlePtr);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ReadCapacity(byte* basePtr) => ReadInt(basePtr + ptrSize);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteCapacity(byte* basePtr, int cap) => WriteInt(basePtr + ptrSize, cap);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* DataPtr(byte* basePtr) => basePtr + headerSize;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* LinkPtr(byte* basePtr, int cap) => basePtr + headerSize + cap;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte* ReadNext(byte* basePtr, int cap) => (byte*)ReadNint(LinkPtr(basePtr, cap));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteNext(byte* basePtr, int cap, byte* next) => WriteNint(LinkPtr(basePtr, cap), (nint)next);

    private void EnsureCapacity(long value)
    {
        if (value <= this.capacity)
            return;

        if (value < 0)
            throw new IOException("Stream too long.");

        while (this.capacity < value)
        {
            var remaining = value - this.capacity;
            var minNeeded = remaining > this.maxSegmentSize ? this.maxSegmentSize : (int)remaining;

            var request = this.GetNextSegmentRequestSize();

            if (request < minNeeded)
                request = minNeeded;

            this.AppendSegment(request);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetNextSegmentRequestSize()
    {
        if (this.segmentCount <= 1)
            return this.initialSegmentSize;

        if (this.lastSegmentRequestSize >= this.maxSegmentSize)
            return this.maxSegmentSize;

        var doubled = this.lastSegmentRequestSize <= this.maxSegmentSize / 2 ? this.lastSegmentRequestSize * 2 : this.maxSegmentSize;

        if (doubled < this.initialSegmentSize)
            return this.initialSegmentSize;

        return doubled;
    }

    private void AppendSegment(int requestedDataSize)
    {
        var rentSize = checked(requestedDataSize + overheadSize);
        var array = this.pool.Rent(rentSize);

        var handle = GCHandle.Alloc(array, GCHandleType.Pinned);
        var basePtr = (byte*)handle.AddrOfPinnedObject();

        var cap = array.Length - overheadSize;

        WriteHandlePtr(basePtr, (nint)GCHandle.ToIntPtr(handle));
        WriteCapacity(basePtr, cap);
        WriteNext(basePtr, cap, null);

        if (this.head == null)
        {
            this.head = basePtr;
            this.tail = basePtr;

            this.cursorBase = basePtr;
            this.cursorStart = 0;
            this.cursorCapacity = cap;
        }
        else
        {
            var tailCap = ReadCapacity(this.tail);
            WriteNext(this.tail, tailCap, basePtr);
            this.tail = basePtr;
        }

        this.capacity += cap;
        this.segmentCount++;
        this.lastSegmentRequestSize = requestedDataSize;
    }

    private void Locate(long pos, out byte* segBase, out long segStart, out int segCap)
    {
        var headBase = this.head;

        if (headBase == null)
            throw new InvalidOperationException();

        var curBase = this.cursorBase;

        if (curBase != null)
        {
            var start = this.cursorStart;
            var cap = this.cursorCapacity;

            if (pos >= start && pos < start + cap)
            {
                segBase = curBase;
                segStart = start;
                segCap = cap;

                return;
            }

            if (pos > start)
            {
                var b = curBase;
                var s = start;
                var c = cap;

                while (pos >= s + c)
                {
                    var next = ReadNext(b, c);

                    if (next == null)
                        throw new ArgumentOutOfRangeException(nameof(pos));

                    s += c;
                    b = next;
                    c = ReadCapacity(b);
                }

                this.cursorBase = b;
                this.cursorStart = s;
                this.cursorCapacity = c;

                segBase = b;
                segStart = s;
                segCap = c;

                return;
            }
        }

        var b0 = headBase;
        long s0 = 0;
        var c0 = ReadCapacity(b0);

        while (pos >= s0 + c0)
        {
            var next = ReadNext(b0, c0);

            if (next == null)
                throw new ArgumentOutOfRangeException(nameof(pos));

            s0 += c0;
            b0 = next;
            c0 = ReadCapacity(b0);
        }

        this.cursorBase = b0;
        this.cursorStart = s0;
        this.cursorCapacity = c0;

        segBase = b0;
        segStart = s0;
        segCap = c0;
    }

    private void WriteCore(long pos, ReadOnlySpan<byte> source)
    {
        if (source.Length == 0)
            return;

        this.Locate(pos, out var segBase, out var segStart, out var segCap);
        var offset = (int)(pos - segStart);

        ref var srcRef = ref MemoryMarshal.GetReference(source);

        fixed (byte* src0 = &srcRef)
        {
            var src = src0;
            var remaining = source.Length;

            while (remaining > 0)
            {
                var take = remaining <= segCap - offset ? remaining : segCap - offset;
                Unsafe.CopyBlockUnaligned(DataPtr(segBase) + offset, src, (uint)take);

                src += take;
                remaining -= take;

                if (remaining == 0)
                {
                    this.cursorBase = segBase;
                    this.cursorStart = segStart;
                    this.cursorCapacity = segCap;

                    return;
                }

                var next = ReadNext(segBase, segCap);

                if (next == null)
                    throw new InvalidOperationException();

                segStart += segCap;
                segBase = next;
                segCap = ReadCapacity(segBase);
                offset = 0;
            }
        }
    }

    private void ReadCore(long pos, Span<byte> destination)
    {
        if (destination.Length == 0)
            return;

        this.Locate(pos, out var segBase, out var segStart, out var segCap);
        var offset = (int)(pos - segStart);

        ref var dstRef = ref MemoryMarshal.GetReference(destination);

        fixed (byte* dst0 = &dstRef)
        {
            var dst = dst0;
            var remaining = destination.Length;

            while (remaining > 0)
            {
                var take = remaining <= segCap - offset ? remaining : segCap - offset;
                Unsafe.CopyBlockUnaligned(dst, DataPtr(segBase) + offset, (uint)take);

                dst += take;
                remaining -= take;

                if (remaining == 0)
                {
                    this.cursorBase = segBase;
                    this.cursorStart = segStart;
                    this.cursorCapacity = segCap;

                    return;
                }

                var next = ReadNext(segBase, segCap);

                if (next == null)
                    throw new InvalidOperationException();

                segStart += segCap;
                segBase = next;
                segCap = ReadCapacity(segBase);
                offset = 0;
            }
        }
    }

    private void ReleaseAllSegments()
    {
        var cur = this.head;

        this.head = null;
        this.tail = null;

        this.ResetCursor();

        this.Length = 0;
        this.Position = 0;
        this.capacity = 0;

        this.segmentCount = 0;
        this.lastSegmentRequestSize = 0;

        while (cur != null)
        {
            var cap = ReadCapacity(cur);
            var next = ReadNext(cur, cap);

            var handlePtr = ReadHandlePtr(cur);
            var handle = GCHandle.FromIntPtr((nint)handlePtr);
            var array = (byte[]?)handle.Target;
            handle.Free();

            if (array is not null)
                this.pool.Return(array, this.clearOnReturn);

            cur = next;
        }
    }
}

public sealed class RamStream : StreamWrapper, IStreamSlrw
{
    private readonly PinnedRamStreamCore stream;

    public RamStream() : base(Stream.Null) => this.stream = new();

    public RamStream(byte[] ints) : base(Stream.Null)
    {
        this.stream = new();

        if (ints.Length != 0)
            this.stream.Write(ints);

        this.stream.Seek(0, SeekOrigin.Begin);
    }

    public long Length => this.stream.Length;
    public long Position => this.stream.Position;
    public long Seek(long offset, SeekOrigin origin) => this.stream.Seek(offset, origin);

    public void SetLength(long value) => this.stream.SetLength(value);

    public int Read(Span<byte> buffer) => this.stream.Read(buffer);

    public void Flush() => this.stream.Flush();

    public void Write(ReadOnlySpan<byte> buffer) => this.stream.Write(buffer);

    protected override StackTask DisposeAny() => this.stream.DisposeAsync();
}

public sealed class RamAsyncStream : StreamWrapper, IStreamSlrwa
{
    private readonly PinnedRamStreamCore stream;

    public RamAsyncStream() : base(Stream.Null) => this.stream = new();

    public RamAsyncStream(byte[] ints) : base(Stream.Null)
    {
        this.stream = new();

        if (ints.Length != 0)
            this.stream.Write(ints);

        this.stream.Seek(0, SeekOrigin.Begin);
    }

    public long Length => this.stream.Length;
    public long Position => this.stream.Position;
    public long Seek(long offset, SeekOrigin origin) => this.stream.Seek(offset, origin);

    public void SetLength(long value) => this.stream.SetLength(value);

    public StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        return this.stream.Read(buffer.Span);
    }

    public StackTask FlushAsync(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.stream.Flush();

        return default;
    }

    public StackTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.stream.Write(buffer.Span);

        return default;
    }

    protected override StackTask DisposeAny() => this.stream.DisposeAsync();
}
