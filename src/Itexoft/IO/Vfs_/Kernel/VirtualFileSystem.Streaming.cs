// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using Itexoft.Threading.Atomics;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private int GetCarrierWindowSize(int length)
    {
        if (length <= 0)
            return 0;

        var limit = this.bufferSize < prefixLength ? this.bufferSize : prefixLength;
        return length < limit ? length : limit;
    }

    private static int GetCarrierWindowSizeStatic(int length) => length <= 0 ? 0 : Math.Min(length, prefixLength);

    private static void CopyRange(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        long sourceOffset,
        IStreamRwsl<byte> destination,
        long destinationOffset,
        int length,
        int windowSize)
    {
        if (length <= 0)
            return;

        Span<byte> window = stackalloc byte[windowSize];

        for (var copied = 0; copied < length; copied += windowSize)
        {
            var chunk = Math.Min(windowSize, length - copied);
            var slice = window[..chunk];
            ReadAt(owner, source, sourceOffset + copied, slice);
            WriteAt(owner, destination, destinationOffset + copied, slice);
        }
    }

    private int GetChunkRecordSize() => this.chunkSize + chunkHeaderSize;

    private long GetPublishedLength(long publishedChunkCount) => prefixLength + publishedChunkCount * (long)this.GetChunkRecordSize();

    private long GetChunkOffset(long chunkId) => prefixLength + chunkId * (long)this.GetChunkRecordSize();

    private long GetPublishedArenaLength(long capacity) => prefixLength + capacity * (long)this.GetChunkRecordSize();

    private long GetDraftChunkOffset(long draftChunkId) => this.GetPublishedArenaLength(this.publishedChunkCapacity) + draftChunkId * (long)this.GetChunkRecordSize();

    private static CarrierStream GetCarrier(IStreamRwsl<byte> stream)
        => stream as CarrierStream ?? throw new InvalidOperationException("Carrier wrapper is required.");

    private static IStreamRwsl<byte> GetRawCarrierStream(IStreamRwsl<byte> stream)
        => stream is CarrierStream carrier ? carrier.Inner : stream;

    internal long GetVisibleChunkCount(IStreamRwsl<byte> stream)
    {
        if (ReferenceEquals(stream, this.primary) || (this.mirror is not null && ReferenceEquals(stream, this.mirror)))
            return this.committedPrefix.Snapshot.PublishedChunkCount;

        var tail = stream.Length - prefixLength;
        return tail <= 0 ? 0 : tail / this.GetChunkRecordSize();
    }

    private static long GetVisibleChunkCount(in PrefixState prefix) => prefix.Snapshot.PublishedChunkCount;

    private static long GetVisibleChunkCount(int chunkSize, in PrefixState prefix) => prefix.Snapshot.PublishedChunkCount;

    private static long GetVisibleChunkCount(int chunkSize, long streamLength)
    {
        var tail = streamLength - prefixLength;
        return tail <= 0 ? 0 : tail / (chunkSize + (long)chunkHeaderSize);
    }

    private static void ReadAt(VirtualFileSystem? owner, IStreamRwsl<byte> stream, long offset, Span<byte> destination)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (owner is not null)
        {
            ref var gate = ref owner.GetIoGate(stream);
            var io = new PositionalByteStream(stream, ref gate);
            io.ReadExactlyAt(offset, destination);
            return;
        }

        PositionalByteStreamSync localGate = default;
        var local = new PositionalByteStream(stream, ref localGate);
        local.ReadExactlyAt(offset, destination);
    }

    private static void WriteAt(VirtualFileSystem? owner, IStreamRwsl<byte> stream, long offset, ReadOnlySpan<byte> source)
    {
        if (offset < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));

        if (owner is not null)
        {
            ref var gate = ref owner.GetIoGate(stream);
            var io = new PositionalByteStream(stream, ref gate);
            io.WriteExactlyAt(offset, source);
            return;
        }

        PositionalByteStreamSync localGate = default;
        var local = new PositionalByteStream(stream, ref localGate);
        local.WriteExactlyAt(offset, source);
    }

    private ref PositionalByteStreamSync GetIoGate(IStreamRwsl<byte> stream)
    {
        if (ReferenceEquals(stream, this.primary))
            return ref this.primaryIoGate;

        if (this.primary is CarrierStream primaryCarrier && ReferenceEquals(stream, primaryCarrier.Inner))
            return ref this.primaryIoGate;

        if (this.mirror is not null && ReferenceEquals(stream, this.mirror))
            return ref this.mirrorIoGate;

        if (this.mirror is CarrierStream mirrorCarrier && ReferenceEquals(stream, mirrorCarrier.Inner))
            return ref this.mirrorIoGate;

        throw new InvalidOperationException("Unknown carrier stream.");
    }

    private static uint ComputeHash(ReadOnlySpan<byte> source)
    {
        var hash = 2166136261u;

        foreach (var value in source)
            hash = (hash ^ value) * 16777619u;

        return hash;
    }

    private static bool TryValidateSnapshot(VirtualFileSystem? owner, IStreamRwsl<byte> stream, in PrefixState prefix)
    {
        try
        {
            if (prefix.ChunkSize < 1 || prefix.Generation < 0)
                return false;

            var snapshot = prefix.Snapshot;
            var visibleChunkCount = GetVisibleChunkCount(prefix.ChunkSize, stream.Length);
            ValidateSnapshotRoot(snapshot.DirectoryEntryRoot, visibleChunkCount);
            ValidateSnapshotRoot(snapshot.InodeRoot, visibleChunkCount, required: true);
            ValidateSnapshotRoot(snapshot.AttributeRoot, visibleChunkCount);
            if (owner is null)
            {
                ValidateSnapshotRoot(snapshot.ReuseRoot, visibleChunkCount);
                ValidateSnapshotRoot(snapshot.DeferredReuseRoot, visibleChunkCount);
            }

            if (snapshot.RootDirectoryInodeId < 0 || snapshot.LastInodeId < snapshot.RootDirectoryInodeId)
                throw new InvalidDataException("Snapshot inode counters are invalid.");

            ValidateMapRoot(owner, stream, prefix.ChunkSize, snapshot.DirectoryEntryRoot);
            ValidateMapRoot(owner, stream, prefix.ChunkSize, snapshot.InodeRoot, required: true);
            ValidateMapRoot(owner, stream, prefix.ChunkSize, snapshot.AttributeRoot);
            if (owner is null)
            {
                ValidateMapRoot(owner, stream, prefix.ChunkSize, snapshot.ReuseRoot);
                ValidateDeferredReuseLogHead(owner, stream, prefix.ChunkSize, snapshot.DeferredReuseRoot);
            }

            if (!TryGetInodeRecordFrom(owner, stream, in prefix, snapshot.RootDirectoryInodeId, out var rootDirectory)
                || rootDirectory.Attributes != (int)FileAttributes.Directory)
            {
                throw new InvalidDataException("Root directory inode is invalid.");
            }

            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return false;
        }
    }

    private static void ValidateSnapshotRoot(long rootChunkId, long visibleChunkCount, bool required = false)
    {
        if (rootChunkId == InvalidChunkId)
        {
            if (required)
                throw new InvalidDataException("Required snapshot root is missing.");

            return;
        }

        if (rootChunkId < 0 || rootChunkId >= visibleChunkCount)
            throw new InvalidDataException("Snapshot root points outside the visible image.");
    }

    private static void ValidateMapRoot(VirtualFileSystem? owner, IStreamRwsl<byte> stream, int chunkSize, long rootChunkId, bool required = false)
    {
        if (rootChunkId == InvalidChunkId)
        {
            if (required)
                throw new InvalidDataException("Required map root is missing.");

            return;
        }

        ReadMapNodeHeader(owner, stream, chunkSize, rootChunkId, out _, out _, out _, out _);
    }

    private static void ValidateDeferredReuseLogHead(VirtualFileSystem? owner, IStreamRwsl<byte> stream, int chunkSize, long rootChunkId)
    {
        if (rootChunkId == InvalidChunkId)
            return;

        var header = ReadChunkHeader(owner, stream, chunkSize, rootChunkId);

        if (header.Kind != chunkChunkLog)
            throw new InvalidDataException("Deferred reuse log chunk is invalid.");
    }

    internal static ChunkHeader ReadChunkHeader(VirtualFileSystem? owner, IStreamRwsl<byte> stream, int chunkSize, long chunkId)
    {
        var visibleChunkCount = GetVisibleChunkCount(chunkSize, stream.Length);

        if (chunkId < 0 || chunkId >= visibleChunkCount)
            throw new InvalidDataException($"Chunk '{chunkId}' is outside the visible image.");

        var chunkOffset = prefixLength + chunkId * (long)(chunkSize + chunkHeaderSize);
        Span<byte> header = stackalloc byte[chunkHeaderSize];
        ReadAt(owner, stream, chunkOffset, header);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header[20..24]) != ComputeHash(header[..20]))
            throw new InvalidDataException($"Chunk '{chunkId}' has an invalid header checksum.");

        var kind = header[0];
        var flags = header[1];
        var reserved = BinaryPrimitives.ReadUInt16LittleEndian(header[2..4]);
        var link = BinaryPrimitives.ReadInt64LittleEndian(header[4..12]);
        var usedBytes = BinaryPrimitives.ReadInt32LittleEndian(header[12..16]);
        var payloadChecksum = BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]);

        if (kind != chunkData && kind != chunkMetadata && kind != chunkChunkLog)
            throw new InvalidDataException($"Chunk '{chunkId}' has an invalid kind '{kind}'.");

        if (flags != 0 || reserved != 0)
            throw new InvalidDataException($"Chunk '{chunkId}' has unsupported flags.");

        if (kind != chunkChunkLog && (usedBytes < 0 || usedBytes > chunkSize))
            throw new InvalidDataException($"Chunk '{chunkId}' declares an invalid payload length '{usedBytes}'.");

        if (link != InvalidChunkId && (link < 0 || link >= visibleChunkCount))
            throw new InvalidDataException($"Chunk '{chunkId}' references invalid continuation chunk '{link}'.");

        return new(kind, link, usedBytes, payloadChecksum);
    }

    private static void ValidateChunkChecksum(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> stream,
        int chunkSize,
        long chunkId,
        int usedBytes,
        uint expectedChecksum)
    {
        var payloadOffset = prefixLength + chunkId * (long)(chunkSize + chunkHeaderSize) + chunkHeaderSize;
        var hash = 2166136261u;
        if (usedBytes == 0)
        {
            if (hash != expectedChecksum)
                throw new InvalidDataException($"Chunk '{chunkId}' has an invalid payload checksum.");

            return;
        }

        var windowSize = Math.Min(usedBytes, prefixLength);
        Span<byte> window = stackalloc byte[windowSize];

        for (var i = 0; i < usedBytes; i += windowSize)
        {
            var chunk = Math.Min(windowSize, usedBytes - i);
            var slice = window[..chunk];
            ReadAt(owner, stream, payloadOffset + i, slice);

            foreach (var value in slice)
                hash = (hash ^ value) * 16777619u;
        }

        if (hash != expectedChecksum)
            throw new InvalidDataException($"Chunk '{chunkId}' checksum mismatch.");
    }

    private void ReadChunkPayload(IStreamRwsl<byte> stream, long chunkId, int payloadOffset, Span<byte> destination)
    {
        var header = ReadChunkHeader(stream, chunkId);

        if (header.Kind != chunkData)
            throw new InvalidDataException($"Chunk '{chunkId}' is not a data chunk.");

        if (payloadOffset < 0 || payloadOffset + destination.Length > header.UsedBytes)
            throw new InvalidDataException($"Chunk '{chunkId}' payload range is invalid.");

        ValidateChunkChecksum(this, stream, this.chunkSize, chunkId, header.UsedBytes, header.PayloadChecksum);
        ReadAt(this, stream, this.GetChunkOffset(chunkId) + chunkHeaderSize + payloadOffset, destination);
    }

    internal ChunkHeader ReadChunkHeader(IStreamRwsl<byte> stream, long chunkId)
        => ReadChunkHeader(this, stream, this.chunkSize, chunkId);

    private static void ValidateDataChunk(VirtualFileSystem? owner, IStreamRwsl<byte> stream, int chunkSize, long chunkId)
    {
        var header = ReadChunkHeader(owner, stream, chunkSize, chunkId);

        if (header.Kind != chunkData || header.UsedBytes != chunkSize)
            throw new InvalidDataException($"Chunk '{chunkId}' is not a valid data chunk.");

        ValidateChunkChecksum(owner, stream, chunkSize, chunkId, header.UsedBytes, header.PayloadChecksum);
    }

    private void WriteMergedDataChunk(
        IStreamRwsl<byte> target,
        long chunkId,
        long oldChunkId,
        long logicalChunk,
        long oldLength,
        long writeOffset,
        ReadOnlySpan<byte> buffer)
    {
        var recordOffset = this.GetChunkOffset(chunkId);
        var payloadOffset = recordOffset + chunkHeaderSize;
        var requiredLength = payloadOffset + this.chunkSize;

        if (requiredLength > target.Length)
            target.Length = requiredLength;

        if (oldChunkId != InvalidChunkId)
            _ = this.ValidateReadableDataChunk(target, oldChunkId);

        var hash = 2166136261u;
        var windowSize = this.GetCarrierWindowSize(this.chunkSize);
        Span<byte> window = stackalloc byte[windowSize];

        for (var payloadIndex = 0; payloadIndex < this.chunkSize; payloadIndex += windowSize)
        {
            var chunk = Math.Min(windowSize, this.chunkSize - payloadIndex);
            var slice = window[..chunk];
            var chunkAbsolute = logicalChunk * (long)this.chunkSize + payloadIndex;

            if (oldChunkId != InvalidChunkId)
                ReadAt(this, target, this.GetChunkOffset(oldChunkId) + chunkHeaderSize + payloadIndex, slice);
            else
                slice.Clear();

            for (var index = 0; index < chunk; index++)
            {
                var absolute = chunkAbsolute + index;
                var sourceIndex = absolute - writeOffset;
                var value = (ulong)sourceIndex < (ulong)buffer.Length
                    ? buffer[(int)sourceIndex]
                    : absolute < oldLength
                        ? slice[index]
                        : (byte)0;
                slice[index] = value;
                hash = (hash ^ value) * 16777619u;
            }

            WriteAt(this, target, payloadOffset + payloadIndex, slice);
        }

        WriteChunkHeader(target, chunkId, chunkData, InvalidChunkId, this.chunkSize, hash);
    }

    private void WriteResetDataChunk(
        IStreamRwsl<byte> target,
        long chunkId,
        long oldChunkId,
        long logicalChunk,
        long oldLength,
        long newLength)
    {
        var recordOffset = this.GetChunkOffset(chunkId);
        var payloadOffset = recordOffset + chunkHeaderSize;
        var requiredLength = payloadOffset + this.chunkSize;

        if (requiredLength > target.Length)
            target.Length = requiredLength;

        if (oldChunkId != InvalidChunkId)
            _ = this.ValidateReadableDataChunk(target, oldChunkId);

        var hash = 2166136261u;
        var windowSize = this.GetCarrierWindowSize(this.chunkSize);
        Span<byte> window = stackalloc byte[windowSize];

        for (var payloadIndex = 0; payloadIndex < this.chunkSize; payloadIndex += windowSize)
        {
            var chunk = Math.Min(windowSize, this.chunkSize - payloadIndex);
            var slice = window[..chunk];
            var chunkAbsolute = logicalChunk * (long)this.chunkSize + payloadIndex;

            if (oldChunkId != InvalidChunkId)
                ReadAt(this, target, this.GetChunkOffset(oldChunkId) + chunkHeaderSize + payloadIndex, slice);
            else
                slice.Clear();

            for (var index = 0; index < chunk; index++)
            {
                var absolute = chunkAbsolute + index;
                var value = absolute < newLength && absolute < oldLength
                    ? slice[index]
                    : (byte)0;
                slice[index] = value;
                hash = (hash ^ value) * 16777619u;
            }

            WriteAt(this, target, payloadOffset + payloadIndex, slice);
        }

        WriteChunkHeader(target, chunkId, chunkData, InvalidChunkId, this.chunkSize, hash);
    }

    private ChunkHeader ValidateReadableDataChunk(IStreamRwsl<byte> stream, long chunkId)
    {
        var header = this.ReadChunkHeader(stream, chunkId);

        if (header.Kind != chunkData || header.UsedBytes != this.chunkSize)
            throw new InvalidDataException($"Chunk '{chunkId}' is not a valid data chunk.");

        ValidateChunkChecksum(this, stream, this.chunkSize, chunkId, header.UsedBytes, header.PayloadChecksum);
        return header;
    }

    private void WriteChunkHeader(IStreamRwsl<byte> target, long chunkId, byte kind, long linkChunkId, int usedBytes, uint payloadChecksum)
    {
        var recordOffset = this.GetChunkOffset(chunkId);
        Span<byte> header = stackalloc byte[chunkHeaderSize];
        header.Clear();
        header[0] = kind;
        BinaryPrimitives.WriteInt64LittleEndian(header[4..12], linkChunkId);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..16], usedBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..20], payloadChecksum);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..24], ComputeHash(header[..20]));
        WriteAt(this, target, recordOffset, header);
    }
}
