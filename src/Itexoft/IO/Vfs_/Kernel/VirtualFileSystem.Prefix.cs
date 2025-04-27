// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private static int InitializeSingle(IStreamRwsl<byte> image, in VirtualFileSystemOptions options, out PrefixState prefixState)
    {
        var prefix = ReadRequiredPrefix(null, image, allowMissing: true);

        if (prefix.IsValid)
        {
            if (options.ChunkSize.HasValue && options.ChunkSize.Value != prefix.ChunkSize)
            {
                throw new InvalidOperationException(
                    $"Requested chunk size {options.ChunkSize.Value} does not match existing chunk size {prefix.ChunkSize}.");
            }

            TrimCarrierToReachable(image, in prefix);
            prefixState = prefix;
            return prefix.ChunkSize;
        }

        if (image.Length != 0)
            throw CreateInvalidImageException();

        if (!options.ChunkSize.HasValue)
            throw new InvalidOperationException("ChunkSize must be specified when creating a new image.");

        if (options.ChunkSize.Value < 1)
            throw new ArgumentOutOfRangeException(nameof(options.ChunkSize));

        prefixState = InitializeEmptyCarrier(image, options.ChunkSize.Value);

        return options.ChunkSize.Value;
    }

    private static int InitializeMirrored(IStreamRwsl<byte> primary, IStreamRwsl<byte> mirror, in VirtualFileSystemOptions options, out PrefixState prefixState)
    {
        var primaryPrefix = ReadRequiredPrefix(null, primary, allowMissing: true);
        var mirrorPrefix = ReadRequiredPrefix(null, mirror, allowMissing: true);

        if (primaryPrefix.IsValid && !TryValidateReachableSnapshot(primary, in primaryPrefix))
            primaryPrefix = default;

        if (mirrorPrefix.IsValid && !TryValidateReachableSnapshot(mirror, in mirrorPrefix))
            mirrorPrefix = default;

        if (!primaryPrefix.IsValid && !mirrorPrefix.IsValid)
        {
            if (primary.Length != 0 || mirror.Length != 0)
                throw new InvalidDataException("Neither carrier contains a valid bootstrap prefix.");

            if (!options.ChunkSize.HasValue)
                throw new InvalidOperationException("ChunkSize must be specified when creating a new mirrored image.");

            if (options.ChunkSize.Value < 1)
                throw new ArgumentOutOfRangeException(nameof(options.ChunkSize));

            prefixState = InitializeEmptyCarrier(primary, options.ChunkSize.Value);
            RepairMirrorFrom(primary, mirror, in prefixState);

            return options.ChunkSize.Value;
        }

        if (primaryPrefix.IsValid && mirrorPrefix.IsValid && primaryPrefix.ChunkSize != mirrorPrefix.ChunkSize)
            throw new InvalidDataException("Mirrored carriers declare different chunk sizes.");

        var chosen = ChooseMirrorWinner(in primaryPrefix, in mirrorPrefix);

        if (chosen == MirrorWinner.Primary)
        {
            TrimCarrierToReachable(primary, in primaryPrefix);
            RepairMirrorFrom(primary, mirror, in primaryPrefix);
        }
        else if (chosen == MirrorWinner.Mirror)
        {
            TrimCarrierToReachable(mirror, in mirrorPrefix);
            RepairMirrorFrom(mirror, primary, in mirrorPrefix);
        }

        var effective = chosen == MirrorWinner.Mirror ? mirrorPrefix : primaryPrefix;

        if (options.ChunkSize.HasValue && options.ChunkSize.Value != effective.ChunkSize)
        {
            throw new InvalidOperationException(
                $"Requested chunk size {options.ChunkSize.Value} does not match existing chunk size {effective.ChunkSize}.");
        }

        prefixState = effective;
        return effective.ChunkSize;
    }

    private static bool TryValidateReachableSnapshot(IStreamRwsl<byte> stream, in PrefixState prefix)
    {
        try
        {
            _ = MeasureReachableSnapshot(prefix.ChunkSize, stream, in prefix);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException or EndOfStreamException)
        {
            return false;
        }
    }

    private static MirrorWinner ChooseMirrorWinner(in PrefixState primaryPrefix, in PrefixState mirrorPrefix)
    {
        if (primaryPrefix.IsValid && !mirrorPrefix.IsValid)
            return MirrorWinner.Primary;

        if (!primaryPrefix.IsValid && mirrorPrefix.IsValid)
            return MirrorWinner.Mirror;

        if (!primaryPrefix.IsValid)
            return MirrorWinner.None;

        if (primaryPrefix.Generation > mirrorPrefix.Generation)
            return MirrorWinner.Primary;

        if (mirrorPrefix.Generation > primaryPrefix.Generation)
            return MirrorWinner.Mirror;

        if (!primaryPrefix.Snapshot.Matches(mirrorPrefix.Snapshot))
            throw new InvalidDataException("Mirrored carriers diverged on the same generation.");

        return MirrorWinner.Primary;
    }

    private static PrefixState InitializeEmptyCarrier(IStreamRwsl<byte> stream, int chunkSize)
    {
        stream.Length = prefixLength;
        Span<byte> prefixBuffer = stackalloc byte[prefixLength];
        prefixBuffer.Clear();
        WriteAt(null, stream, 0, prefixBuffer);

        var snapshot = CreateInitialSnapshotState(stream, chunkSize);
        var prefix = new PrefixState(true, 0, chunkSize, 0, in snapshot);
        WritePrefixSlot(null, stream, in prefix, prefix.SlotIndex);
        FlushCarrier(stream);
        return prefix;
    }

    private static PrefixState ReadPublishedPrefix(VirtualFileSystem owner, IStreamRwsl<byte> stream, long generation)
    {
        if (generation < 0)
            throw CreateInvalidImageException();

        var slotIndex = (int)(generation & 1L);
        Span<byte> slot = stackalloc byte[prefixSlotSize];
        ReadAt(owner, stream, slotIndex * (long)prefixSlotSize, slot);
        var prefix = TryParsePrefixSlot(slot, slotIndex);

        if (!prefix.IsValid || prefix.Generation != generation || !TryValidateSnapshot(owner, stream, in prefix))
            throw CreateInvalidImageException();

        return prefix;
    }

    private static PrefixState ReadRequiredPrefix(VirtualFileSystem? owner, IStreamRwsl<byte> stream, bool allowMissing = false)
    {
        var prefix = ReadBestPrefix(owner, stream);

        if (prefix.IsValid || allowMissing)
            return prefix;

        throw CreateInvalidImageException();
    }

    private static PrefixState ReadBestPrefix(VirtualFileSystem? owner, IStreamRwsl<byte> stream)
    {
        if (stream.Length < prefixLength)
            return default;

        Span<byte> slot0 = stackalloc byte[prefixSlotSize];
        Span<byte> slot1 = stackalloc byte[prefixSlotSize];
        ReadAt(owner, stream, 0, slot0);
        ReadAt(owner, stream, prefixSlotSize, slot1);
        ThrowIfUnsupportedPrefixVersion(slot0, slot1);

        var prefix0 = TryParsePrefixSlot(slot0, 0);
        var prefix1 = TryParsePrefixSlot(slot1, 1);

        if (prefix0.IsValid && !TryValidateSnapshot(owner, stream, in prefix0))
            prefix0 = default;

        if (prefix1.IsValid && !TryValidateSnapshot(owner, stream, in prefix1))
            prefix1 = default;

        PrefixState winner;

        if (!prefix0.IsValid)
            winner = prefix1;
        else if (!prefix1.IsValid)
            winner = prefix0;
        else if (prefix1.Generation > prefix0.Generation)
            winner = prefix1;
        else if (prefix0.Generation > prefix1.Generation)
            winner = prefix0;
        else
        {
            if (!prefix0.Snapshot.Matches(prefix1.Snapshot))
                throw new InvalidDataException("Bootstrap slots diverged on the same generation.");

            var expectedSlot = (int)(prefix0.Generation & 1L);
            winner = prefix0.SlotIndex == expectedSlot ? prefix0 : prefix1;
        }

        if (stream is CarrierStream carrier && winner.IsValid)
            carrier.SetPublishedLength(prefixLength + winner.Snapshot.PublishedChunkCount * (winner.ChunkSize + (long)chunkHeaderSize));

        return winner;
    }

    private static PrefixState TryParsePrefixSlot(ReadOnlySpan<byte> slot, int slotIndex)
    {
        if (slot.Length != prefixSlotSize)
            return default;

        var version = BinaryPrimitives.ReadInt32LittleEndian(slot[..4]);

        if (version != prefixVersion)
            return default;

        var chunkSize = BinaryPrimitives.ReadInt32LittleEndian(slot[4..8]);

        if (chunkSize < 1)
            return default;

        var generation = BinaryPrimitives.ReadInt64LittleEndian(slot[8..16]);

        if (generation < 0)
            return default;

        var checksum = BinaryPrimitives.ReadUInt32LittleEndian(slot[prefixChecksumOffset..]);

        if (checksum != ComputeHash(slot[..prefixChecksumOffset]))
            return default;

        var snapshot = new SnapshotState(
            BinaryPrimitives.ReadInt64LittleEndian(slot[16..24]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[24..32]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[32..40]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[40..48]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[48..56]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[56..64]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[64..72]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[72..80]),
            BinaryPrimitives.ReadInt64LittleEndian(slot[80..88]));

        if (snapshot.PublishedChunkCount < 0 || snapshot.PublishedChunkCapacity < snapshot.PublishedChunkCount)
            return default;

        return new(true, slotIndex, chunkSize, generation, in snapshot);
    }

    private static void WritePrefixSlot(VirtualFileSystem? owner, IStreamRwsl<byte> stream, in PrefixState prefix, int slotIndex)
    {
        Span<byte> slot = stackalloc byte[prefixSlotSize];
        EncodePrefixSlot(slot, in prefix);
        WriteAt(owner, stream, slotIndex * (long)prefixSlotSize, slot);
    }

    private static long WriteChunkStreamPayloadStatic(
        IStreamRwsl<byte> target,
        int chunkSize,
        byte kind,
        ReadOnlySpan<byte> payload,
        ref long nextChunkId)
    {
        if (payload.Length == 0)
            throw new InvalidOperationException("Chunk stream payload cannot be empty.");

        var rootChunkId = nextChunkId++;
        var currentChunkId = rootChunkId;
        var sourceOffset = 0;
        var remaining = payload.Length;
        Span<byte> record = stackalloc byte[chunkHeaderSize + Math.Min(chunkSize, payload.Length)];

        while (remaining != 0)
        {
            var usedBytes = Math.Min(chunkSize, remaining);
            var nextLinkChunkId = remaining > chunkSize ? nextChunkId++ : InvalidChunkId;
            var recordOffset = prefixLength + currentChunkId * (long)(chunkSize + chunkHeaderSize);
            var payloadOffset = recordOffset + chunkHeaderSize;
            var requiredLength = payloadOffset + chunkSize;
            if (requiredLength > target.Length)
                target.Length = requiredLength;

            var segment = payload.Slice(sourceOffset, usedBytes);
            var chunkRecord = record[..(chunkHeaderSize + usedBytes)];
            chunkRecord.Clear();
            EncodeChunkHeader(chunkRecord[..chunkHeaderSize], kind, nextLinkChunkId, usedBytes, ComputeHash(segment));
            segment.CopyTo(chunkRecord[chunkHeaderSize..]);
            WriteAt(null, target, recordOffset, chunkRecord);
            currentChunkId = nextLinkChunkId;
            sourceOffset += usedBytes;
            remaining -= usedBytes;
        }

        return rootChunkId;
    }

    private static Exception CreateInvalidImageException() => new InvalidDataException("Carrier does not contain a valid bootstrap prefix.");

    private static Exception CreateUnsupportedImageVersionException(int version)
        => new InvalidDataException($"Carrier image format v{version} is not supported. Expected v{prefixVersion}.");

    private static void TrimCarrierToReachable(IStreamRwsl<byte> stream, in PrefixState prefix)
    {
        var maxChunkId = MeasureReachableSnapshot(prefix.ChunkSize, stream, in prefix);
        var requiredCount = maxChunkId < 0 ? 0 : maxChunkId + 1;

        if (requiredCount > prefix.Snapshot.PublishedChunkCount)
            throw CreateInvalidImageException();

        var requiredLength = prefixLength + prefix.Snapshot.PublishedChunkCapacity * (prefix.ChunkSize + (long)chunkHeaderSize);

        if (stream.Length != requiredLength)
        {
            if (stream is CarrierStream carrier)
                carrier.TrimRawLength(requiredLength);
            else
                stream.Length = requiredLength;
        }
    }

    private static void RepairMirrorFrom(IStreamRwsl<byte> source, IStreamRwsl<byte> destination, in PrefixState prefix)
    {
        if (!prefix.IsValid)
            throw new InvalidDataException("Cannot repair mirror from an invalid snapshot.");

        if (destination is CarrierStream destinationCarrier)
            destinationCarrier.TrimRawLength(source.Length);
        else
            destination.Length = source.Length;

        for (long copied = 0; copied < source.Length;)
        {
            var chunk = (int)Math.Min(source.Length - copied, int.MaxValue);
            CopyRange(null, source, copied, destination, copied, chunk, GetCarrierWindowSizeStatic(chunk));
            copied += chunk;
        }

        FlushCarrier(destination);
    }

    private static int ValidateBufferSize(int? requested, int chunkSize)
    {
        var value = requested ?? chunkSize;

        if (value < 1)
            throw new ArgumentOutOfRangeException(nameof(requested));

        return value;
    }

    private static void FlushCarrier(IStreamW stream) => stream.Flush();

    private static long ReplayReachableSnapshot(IStreamRwsl<byte> source, IStreamRwsl<byte> destination, in PrefixState prefix)
    {
        var snapshot = prefix.Snapshot;
        var maxChunkId = ReplayMapTree(source, destination, in prefix, snapshot.DirectoryEntryRoot, MapValidationKind.DirectoryEntry);
        maxChunkId = Math.Max(maxChunkId, ReplayMapTree(source, destination, in prefix, snapshot.AttributeRoot, MapValidationKind.Attribute));
        maxChunkId = Math.Max(maxChunkId, ReplayMapTree(source, destination, in prefix, snapshot.ReuseRoot, MapValidationKind.Reuse));
        maxChunkId = Math.Max(maxChunkId, CopyDeferredReuseLog(source, destination, prefix.ChunkSize, snapshot.PublishedChunkCount, snapshot.DeferredReuseRoot));
        maxChunkId = Math.Max(maxChunkId, ReplayMapTree(source, destination, in prefix, snapshot.InodeRoot, MapValidationKind.Inode));
        return maxChunkId;
    }

    private static long MeasureReachableSnapshot(int chunkSize, IStreamRwsl<byte> stream, in PrefixState prefix)
    {
        var snapshot = prefix.Snapshot;
        var maxChunkId = MeasureMapTree(chunkSize, stream, snapshot.DirectoryEntryRoot, MapValidationKind.DirectoryEntry);
        maxChunkId = Math.Max(maxChunkId, MeasureMapTree(chunkSize, stream, snapshot.AttributeRoot, MapValidationKind.Attribute));
        maxChunkId = Math.Max(maxChunkId, MeasureMapTree(chunkSize, stream, snapshot.ReuseRoot, MapValidationKind.Reuse));
        maxChunkId = Math.Max(maxChunkId, MeasureDeferredReuseLog(chunkSize, stream, snapshot.PublishedChunkCount, snapshot.DeferredReuseRoot));
        maxChunkId = Math.Max(maxChunkId, MeasureMapTree(chunkSize, stream, snapshot.InodeRoot, MapValidationKind.Inode));
        return maxChunkId;
    }

    private static long MeasureMapTree(int chunkSize, IStreamRwsl<byte> stream, long rootChunkId, MapValidationKind validationKind)
    {
        if (rootChunkId == InvalidChunkId)
            return InvalidChunkId;

        var maxChunkId = MeasureChunkStream(chunkSize, stream, rootChunkId);
        ReadMapNodeHeader(null, stream, chunkSize, rootChunkId, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(null, stream, chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var index = 0; index < recordCount; index++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                maxChunkId = Math.Max(maxChunkId, MeasureMapTree(chunkSize, stream, cursor.ReadInt64LittleEndian(), validationKind));
            }

            return maxChunkId;
        }

        for (var index = 0; index < recordCount; index++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();

            if (keyLength < 0)
                throw new InvalidDataException("Map key length is invalid.");

            cursor.Skip(keyLength);
            var valueLength = cursor.ReadInt32LittleEndian();

            switch (validationKind)
            {
                case MapValidationKind.Inode:
                {
                    var inode = ReadInodeValue(ref cursor, valueLength);
                    maxChunkId = Math.Max(maxChunkId, MeasureMapTree(chunkSize, stream, inode.ContentRoot, MapValidationKind.Content));
                    break;
                }
                case MapValidationKind.Content:
                {
                    if (valueLength != sizeof(long))
                        throw new InvalidDataException("Content map value length is invalid.");

                    var chunkId = cursor.ReadInt64LittleEndian();
                    ValidateDataChunk(null, stream, chunkSize, chunkId);
                    maxChunkId = Math.Max(maxChunkId, chunkId);
                    break;
                }
                default:
                {
                    if (valueLength < 0)
                        throw new InvalidDataException("Map value length is invalid.");

                    cursor.Skip(valueLength);
                    break;
                }
            }
        }

        return maxChunkId;
    }

    private static long MeasureChunkStream(int chunkSize, IStreamRwsl<byte> stream, long rootChunkId)
    {
        if (rootChunkId == InvalidChunkId)
            return InvalidChunkId;

        var visibleChunkCount = GetVisibleChunkCount(chunkSize, stream.Length);
        var remaining = visibleChunkCount;
        var current = rootChunkId;
        var maxChunkId = InvalidChunkId;

        while (current != InvalidChunkId)
        {
            if (--remaining < 0)
                throw new InvalidDataException("Chunk stream is cyclic.");

            maxChunkId = Math.Max(maxChunkId, current);
            current = ReadChunkHeader(null, stream, chunkSize, current).LinkChunkId;
        }

        return maxChunkId;
    }

    private static long ReplayMapTree(
        IStreamRwsl<byte> source,
        IStreamRwsl<byte> destination,
        in PrefixState prefix,
        long rootChunkId,
        MapValidationKind validationKind)
    {
        if (rootChunkId == InvalidChunkId)
            return InvalidChunkId;

        var maxChunkId = CopyChunkStream(source, destination, prefix.ChunkSize, rootChunkId);
        ReadMapNodeHeader(null, source, prefix.ChunkSize, rootChunkId, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(null, source, prefix.ChunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                maxChunkId = Math.Max(maxChunkId, ReplayMapTree(source, destination, in prefix, cursor.ReadInt64LittleEndian(), validationKind));
            }

            return maxChunkId;
        }

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();

            if (keyLength < 0)
                throw new InvalidDataException("Map key length is invalid.");

            cursor.Skip(keyLength);
            var valueLength = cursor.ReadInt32LittleEndian();

            switch (validationKind)
            {
                case MapValidationKind.Inode:
                {
                    var inode = ReadInodeValue(ref cursor, valueLength);
                    maxChunkId = Math.Max(maxChunkId, ReplayMapTree(source, destination, in prefix, inode.ContentRoot, MapValidationKind.Content));
                    break;
                }
                case MapValidationKind.Content:
                {
                    if (valueLength != sizeof(long))
                        throw new InvalidDataException("Content map value length is invalid.");

                    maxChunkId = Math.Max(maxChunkId, CopyChunkRecord(source, destination, prefix.ChunkSize, cursor.ReadInt64LittleEndian()));
                    break;
                }
                default:
                {
                    if (valueLength < 0)
                        throw new InvalidDataException("Map value length is invalid.");

                    cursor.Skip(valueLength);
                    break;
                }
            }
        }

        return maxChunkId;
    }

    private static long CopyChunkStream(IStreamRwsl<byte> source, IStreamRwsl<byte> destination, int chunkSize, long rootChunkId)
    {
        if (rootChunkId == InvalidChunkId)
            return InvalidChunkId;

        var visibleChunkCount = GetVisibleChunkCount(chunkSize, source.Length);
        var remaining = visibleChunkCount;
        var current = rootChunkId;
        var maxChunkId = InvalidChunkId;

        while (current != InvalidChunkId)
        {
            if (--remaining < 0)
                throw new InvalidDataException("Chunk stream is cyclic.");

            maxChunkId = Math.Max(maxChunkId, CopyChunkRecord(source, destination, chunkSize, current));
            current = ReadChunkHeader(null, source, chunkSize, current).LinkChunkId;
        }

        return maxChunkId;
    }

    private static long MeasureDeferredReuseLog(int chunkSize, IStreamRwsl<byte> stream, long visibleChunkCount, long rootChunkId)
    {
        if (rootChunkId == InvalidChunkId)
            return InvalidChunkId;

        var remaining = visibleChunkCount;
        var current = rootChunkId;
        var maxChunkId = InvalidChunkId;

        while (current != InvalidChunkId)
        {
            if (--remaining < 0)
                throw new InvalidDataException("Deferred reuse log is cyclic.");

            var header = ReadChunkHeader(null, stream, chunkSize, current);

            if (header.Kind != chunkChunkLog)
                throw new InvalidDataException("Deferred reuse log chunk is invalid.");

            maxChunkId = Math.Max(maxChunkId, current);
            current = header.LinkChunkId;
        }

        return maxChunkId;
    }

    private static long CopyDeferredReuseLog(IStreamRwsl<byte> source, IStreamRwsl<byte> destination, int chunkSize, long visibleChunkCount, long rootChunkId)
    {
        if (rootChunkId == InvalidChunkId)
            return InvalidChunkId;

        var remaining = visibleChunkCount;
        var current = rootChunkId;
        var maxChunkId = InvalidChunkId;

        while (current != InvalidChunkId)
        {
            if (--remaining < 0)
                throw new InvalidDataException("Deferred reuse log is cyclic.");

            var header = ReadChunkHeader(null, source, chunkSize, current);

            if (header.Kind != chunkChunkLog)
                throw new InvalidDataException("Deferred reuse log chunk is invalid.");

            maxChunkId = Math.Max(maxChunkId, CopyChunkRecord(source, destination, chunkSize, current));
            current = header.LinkChunkId;
        }

        return maxChunkId;
    }

    private static long CopyChunkRecord(IStreamRwsl<byte> source, IStreamRwsl<byte> destination, int chunkSize, long chunkId)
    {
        if (chunkId == InvalidChunkId)
            return InvalidChunkId;

        var recordSize = chunkSize + chunkHeaderSize;
        var sourceOffset = prefixLength + chunkId * (long)recordSize;
        var destinationOffset = prefixLength + chunkId * (long)recordSize;
        var requiredLength = destinationOffset + recordSize;

        if (requiredLength > destination.Length)
            destination.Length = requiredLength;

        CopyRange(null, source, sourceOffset, destination, destinationOffset, recordSize, GetCarrierWindowSizeStatic(recordSize));

        return chunkId;
    }

    private static void WriteChunkHeaderStatic(IStreamRwsl<byte> target, int chunkSize, long chunkId, byte kind, long linkChunkId, int usedBytes, uint payloadChecksum)
    {
        var recordOffset = prefixLength + chunkId * (long)(chunkSize + chunkHeaderSize);
        Span<byte> header = stackalloc byte[chunkHeaderSize];
        EncodeChunkHeader(header, kind, linkChunkId, usedBytes, payloadChecksum);
        WriteAt(null, target, recordOffset, header);
    }

    private static void EncodePrefixSlot(Span<byte> slot, in PrefixState prefix)
    {
        slot.Clear();
        BinaryPrimitives.WriteInt32LittleEndian(slot[..4], prefixVersion);
        BinaryPrimitives.WriteInt32LittleEndian(slot[4..8], prefix.ChunkSize);
        BinaryPrimitives.WriteInt64LittleEndian(slot[8..16], prefix.Generation);
        BinaryPrimitives.WriteInt64LittleEndian(slot[16..24], prefix.Snapshot.DirectoryEntryRoot);
        BinaryPrimitives.WriteInt64LittleEndian(slot[24..32], prefix.Snapshot.InodeRoot);
        BinaryPrimitives.WriteInt64LittleEndian(slot[32..40], prefix.Snapshot.AttributeRoot);
        BinaryPrimitives.WriteInt64LittleEndian(slot[40..48], prefix.Snapshot.ReuseRoot);
        BinaryPrimitives.WriteInt64LittleEndian(slot[48..56], prefix.Snapshot.DeferredReuseRoot);
        BinaryPrimitives.WriteInt64LittleEndian(slot[56..64], prefix.Snapshot.RootDirectoryInodeId);
        BinaryPrimitives.WriteInt64LittleEndian(slot[64..72], prefix.Snapshot.LastInodeId);
        BinaryPrimitives.WriteInt64LittleEndian(slot[72..80], prefix.Snapshot.PublishedChunkCount);
        BinaryPrimitives.WriteInt64LittleEndian(slot[80..88], prefix.Snapshot.PublishedChunkCapacity);
        BinaryPrimitives.WriteUInt32LittleEndian(slot[prefixChecksumOffset..], ComputeHash(slot[..prefixChecksumOffset]));
    }

    private static void ThrowIfUnsupportedPrefixVersion(ReadOnlySpan<byte> slot0, ReadOnlySpan<byte> slot1)
    {
        var version0 = BinaryPrimitives.ReadInt32LittleEndian(slot0[..4]);
        var version1 = BinaryPrimitives.ReadInt32LittleEndian(slot1[..4]);

        if (version0 != 0 && version0 != prefixVersion)
            throw CreateUnsupportedImageVersionException(version0);

        if (version1 != 0 && version1 != prefixVersion)
            throw CreateUnsupportedImageVersionException(version1);
    }

    private static void EncodeChunkHeader(Span<byte> header, byte kind, long linkChunkId, int usedBytes, uint payloadChecksum)
    {
        header.Clear();
        header[0] = kind;
        BinaryPrimitives.WriteInt64LittleEndian(header[4..12], linkChunkId);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..16], usedBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header[16..20], payloadChecksum);
        BinaryPrimitives.WriteUInt32LittleEndian(header[20..24], ComputeHash(header[..20]));
    }
}
