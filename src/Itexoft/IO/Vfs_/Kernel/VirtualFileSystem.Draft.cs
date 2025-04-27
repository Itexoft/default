// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using Itexoft.Threading.Atomics;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private long BeginFileDeltaSession(long inodeId)
    {
        using var hold = this.draftSync.Enter();

        if (this.dirtyWriterByInode.TryGetValue(inodeId, out _))
            throw new IOException($"A transactional writer is already open for inode '{inodeId}'.");

        if (this.dirtyWriterByInode.Count == 0)
            this.ReserveDraftHeadroom();

        var sessionId = this.nextDraftSessionId++;
        this.dirtyWriterByInode.Add(inodeId, sessionId);
        return sessionId;
    }

    internal bool HasActiveDraftSessions()
    {
        using var hold = this.draftSync.Enter();
        return this.dirtyWriterByInode.Count != 0;
    }

    private void EndFileDeltaSession(long inodeId, long sessionId)
    {
        using var hold = this.draftSync.Enter();

        if (this.dirtyWriterByInode.TryGetValue(inodeId, out var currentSessionId) && currentSessionId == sessionId)
            this.dirtyWriterByInode.Remove(inodeId);

        if (this.dirtyWriterByInode.Count == 0)
        {
            this.nextDraftChunkId = 0;
            var publishedArenaLength = this.GetPublishedArenaLength(this.publishedChunkCapacity);
            var publishedLength = this.GetPublishedLength(this.committedPrefix.Snapshot.PublishedChunkCount);
            var rawPrimary = GetRawCarrierStream(this.primary);

            this.SetRawCarrierLength(rawPrimary, publishedArenaLength);

            if (TryGetCarrierFileView(rawPrimary, out var primaryCarrierView))
                primaryCarrierView!.SynchronizeVisibleLength(publishedLength);

            if (this.mirror is not null)
            {
                var rawMirror = GetRawCarrierStream(this.mirror);
                this.SetRawCarrierLength(rawMirror, publishedArenaLength);

                if (TryGetCarrierFileView(rawMirror, out var mirrorCarrierView))
                    mirrorCarrierView!.SynchronizeVisibleLength(publishedLength);
            }
        }
    }

    internal void EnsurePublishedChunkCapacity(long requiredChunkCount)
    {
        if (requiredChunkCount <= this.publishedChunkCapacity)
            return;

        using var hold = this.draftSync.Enter();

        if (requiredChunkCount <= this.publishedChunkCapacity)
            return;

        if (this.dirtyWriterByInode.Count != 0)
        {
            this.MoveDraftArea(this.publishedChunkCapacity, requiredChunkCount);
            this.publishedChunkCapacity = requiredChunkCount;
            return;
        }

        this.publishedChunkCapacity = requiredChunkCount;
        this.EnsureRawCarrierLength(this.GetPublishedArenaLength(this.publishedChunkCapacity));
    }

    private long AllocateDraftChunkId()
    {
        using var hold = this.draftSync.Enter();
        var draftChunkId = this.nextDraftChunkId++;
        var requiredLength = this.GetDraftChunkOffset(draftChunkId) + this.GetChunkRecordSize();
        this.EnsureRawCarrierLength(requiredLength);

        return draftChunkId;
    }

    private void ReserveDraftHeadroom()
    {
        var visibleChunkCount = this.committedPrefix.Snapshot.PublishedChunkCount;
        var reservedCapacity = Math.Max(this.publishedChunkCapacity, Math.Max(1L, visibleChunkCount) * 2);

        if (reservedCapacity == this.publishedChunkCapacity)
            return;

        this.publishedChunkCapacity = reservedCapacity;
        this.EnsureRawCarrierLength(this.GetPublishedArenaLength(this.publishedChunkCapacity));
    }

    private void EnsureRawCarrierLength(long requiredLength)
    {
        var rawPrimary = GetRawCarrierStream(this.primary);

        if (requiredLength > rawPrimary.Length)
            this.SetRawCarrierLength(rawPrimary, requiredLength);

        if (this.mirror is null)
            return;

        var rawMirror = GetRawCarrierStream(this.mirror);

        if (requiredLength > rawMirror.Length)
            this.SetRawCarrierLength(rawMirror, requiredLength);
    }

    private void SetRawCarrierLength(IStreamRwsl<byte> rawCarrier, long value)
    {
        ref var gate = ref this.GetIoGate(rawCarrier);

        using var hold = gate.Cursor.Enter();
        rawCarrier.Length = value;
    }

    private void MoveDraftArea(long oldCapacity, long newCapacity)
    {
        if (newCapacity <= oldCapacity || this.nextDraftChunkId == 0)
        {
            this.EnsureRawCarrierLength(this.GetPublishedArenaLength(newCapacity));
            return;
        }

        this.MoveDraftAreaOnCarrier(GetRawCarrierStream(this.primary), oldCapacity, newCapacity, this.nextDraftChunkId);

        if (this.mirror is not null)
            this.MoveDraftAreaOnCarrier(GetRawCarrierStream(this.mirror), oldCapacity, newCapacity, this.nextDraftChunkId);
    }

    private void MoveDraftAreaOnCarrier(IStreamRwsl<byte> rawCarrier, long oldCapacity, long newCapacity, long draftChunkCount)
    {
        var recordSize = this.GetChunkRecordSize();
        var oldBaseOffset = this.GetPublishedArenaLength(oldCapacity);
        var newBaseOffset = this.GetPublishedArenaLength(newCapacity);
        var requiredLength = newBaseOffset + draftChunkCount * (long)recordSize;
        this.SetRawCarrierLength(rawCarrier, requiredLength);

        for (var draftChunkId = draftChunkCount - 1; draftChunkId >= 0; draftChunkId--)
        {
            var sourceOffset = oldBaseOffset + draftChunkId * (long)recordSize;
            var destinationOffset = newBaseOffset + draftChunkId * (long)recordSize;
            CopyRange(this, rawCarrier, sourceOffset, rawCarrier, destinationOffset, recordSize, this.GetCarrierWindowSize(recordSize));
        }
    }

    private void WriteDraftChunk(long draftChunkId, ReadOnlySpan<byte> payload)
    {
        if (payload.Length != this.chunkSize)
            throw new ArgumentOutOfRangeException(nameof(payload));

        var payloadChecksum = ComputeHash(payload);
        var rawPrimary = GetRawCarrierStream(this.primary);
        this.WriteDraftChunkToRaw(rawPrimary, draftChunkId, payload, payloadChecksum);

        if (this.mirror is not null)
            this.WriteDraftChunkToRaw(GetRawCarrierStream(this.mirror), draftChunkId, payload, payloadChecksum);
    }

    private void ReadDraftChunk(long draftChunkId, Span<byte> destination)
    {
        if (destination.Length > this.chunkSize)
            throw new ArgumentOutOfRangeException(nameof(destination));

        var rawPrimary = GetRawCarrierStream(this.primary);
        var header = this.ReadDraftChunkHeader(rawPrimary, draftChunkId);
        this.ValidateRawChunkChecksum(rawPrimary, this.GetDraftChunkOffset(draftChunkId) + chunkHeaderSize, header.PayloadChecksum, this.chunkSize);
        ReadAt(this, rawPrimary, this.GetDraftChunkOffset(draftChunkId) + chunkHeaderSize, destination);
    }

    private void CopyDraftChunkToPublished(IStreamRwsl<byte> target, long publishedChunkId, long draftChunkId)
    {
        var rawPrimary = GetRawCarrierStream(this.primary);
        var draftHeader = this.ReadDraftChunkHeader(rawPrimary, draftChunkId);
        var destinationOffset = this.GetChunkOffset(publishedChunkId);
        var requiredLength = destinationOffset + this.GetChunkRecordSize();

        if (requiredLength > target.Length)
            target.Length = requiredLength;

        Span<byte> payload = stackalloc byte[Math.Min(this.chunkSize, prefixLength)];
        var payloadOffset = this.GetDraftChunkOffset(draftChunkId) + chunkHeaderSize;

        for (var copied = 0; copied < this.chunkSize; copied += payload.Length)
        {
            var chunk = Math.Min(payload.Length, this.chunkSize - copied);
            var slice = payload[..chunk];
            ReadAt(this, rawPrimary, payloadOffset + copied, slice);
            WriteAt(this, target, destinationOffset + chunkHeaderSize + copied, slice);
        }

        this.WriteChunkHeader(target, publishedChunkId, chunkData, InvalidChunkId, this.chunkSize, draftHeader.PayloadChecksum);
    }

    private void WriteDraftChunkToRaw(IStreamRwsl<byte> rawCarrier, long draftChunkId, ReadOnlySpan<byte> payload, uint payloadChecksum)
    {
        var chunkOffset = this.GetDraftChunkOffset(draftChunkId);
        WriteAt(this, rawCarrier, chunkOffset + chunkHeaderSize, payload);
        Span<byte> header = stackalloc byte[chunkHeaderSize];
        EncodeChunkHeader(header, chunkData, InvalidChunkId, this.chunkSize, payloadChecksum);
        WriteAt(this, rawCarrier, chunkOffset, header);
    }

    private ChunkHeader ReadDraftChunkHeader(IStreamRwsl<byte> rawCarrier, long draftChunkId)
    {
        var chunkOffset = this.GetDraftChunkOffset(draftChunkId);
        Span<byte> header = stackalloc byte[chunkHeaderSize];
        ReadAt(this, rawCarrier, chunkOffset, header);

        if (BinaryPrimitives.ReadUInt32LittleEndian(header[20..24]) != ComputeHash(header[..20]))
            throw new InvalidDataException($"Draft chunk '{draftChunkId}' has an invalid header checksum.");

        if (header[0] != chunkData)
            throw new InvalidDataException($"Draft chunk '{draftChunkId}' has invalid kind '{header[0]}'.");

        var usedBytes = BinaryPrimitives.ReadInt32LittleEndian(header[12..16]);

        if (usedBytes != this.chunkSize)
            throw new InvalidDataException($"Draft chunk '{draftChunkId}' declares invalid payload length '{usedBytes}'.");

        return new(chunkData, InvalidChunkId, usedBytes, BinaryPrimitives.ReadUInt32LittleEndian(header[16..20]));
    }

    private void ValidateRawChunkChecksum(IStreamRwsl<byte> rawCarrier, long payloadOffset, uint expectedChecksum, int length)
    {
        var hash = 2166136261u;
        var windowLength = Math.Min(length, prefixLength);
        Span<byte> window = stackalloc byte[windowLength];

        for (var copied = 0; copied < length; copied += windowLength)
        {
            var chunk = Math.Min(windowLength, length - copied);
            var slice = window[..chunk];
            ReadAt(this, rawCarrier, payloadOffset + copied, slice);

            foreach (var value in slice)
                hash = (hash ^ value) * 16777619u;
        }

        if (hash != expectedChecksum)
            throw new InvalidDataException("Draft chunk payload checksum mismatch.");
    }
}
