// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private void CreateFileInternal(string normalizedPath, bool overwriteExisting, bool mustNotExist)
    {
        if (overwriteExisting)
        {
            this.InvokeMutation(VfsMutationKind.ResetFile, normalizedPath);

            return;
        }

        this.InvokeMutation(VfsMutationKind.CreateFile, normalizedPath, mustNotExist: mustNotExist);
    }

    private void EnsureParentDirectoryExists(string normalizedPath)
    {
        var parent = GetParentPath(normalizedPath);

        if (parent.Length == 0)
            return;

        if (!TryGetNodeKind(parent, out var kind) || kind != NodeKind.Directory)
            throw new DirectoryNotFoundException($"Directory '{parent}' was not found.");
    }

    private VfsDeltaTape ApplyCreateDirectoryToCarrier(IStreamRwsl<byte> target, string normalized)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);
        var directoryEntryRoot = root.DirectoryEntryRoot;
        var inodeRoot = root.InodeRoot;
        var attributeRoot = root.AttributeRoot;
        var lastInodeId = root.LastInodeId;
        var currentDirectoryInodeId = root.RootDirectoryInodeId;
        var changed = false;
        var cursor = new VfsPathCursor(normalized);
        Span<byte> inodeKey = stackalloc byte[sizeof(long)];
        Span<byte> inodeValue = stackalloc byte[sizeof(long) + sizeof(long) + sizeof(int)];
        Span<byte> directoryEntryValue = stackalloc byte[sizeof(long) + 1];
        var maxSegmentLength = 0;
        var maxLengthCursor = new VfsPathCursor(normalized);

        while (maxLengthCursor.MoveNext())
            maxSegmentLength = Math.Max(maxSegmentLength, maxLengthCursor.Segment.Length);

        Span<byte> directoryEntryKey = stackalloc byte[sizeof(long) + maxSegmentLength * sizeof(char)];

        while (cursor.MoveNext())
        {
            if (this.TryGetDirectoryEntry(target, directoryEntryRoot, currentDirectoryInodeId, cursor.Segment, out var existing))
            {
                if (existing.Kind != NodeKind.Directory)
                    throw new IOException($"Path segment '{cursor.Segment.ToString()}' is not a directory.");

                currentDirectoryInodeId = existing.InodeId;
                continue;
            }

            lastInodeId++;
            WriteInt64Key(inodeKey, lastInodeId);
            WriteInodeValue(inodeValue, 0, InvalidChunkId, (int)FileAttributes.Directory);
            inodeRoot = this.UpsertMap(target, inodeRoot, inodeKey, inodeValue, ref plan);
            WriteDirectoryEntryValue(directoryEntryValue, lastInodeId, NodeKind.Directory);
            var directoryEntryKeyLength = GetDirectoryEntryKeyLength(cursor.Segment);
            WriteDirectoryEntryKey(directoryEntryKey[..directoryEntryKeyLength], currentDirectoryInodeId, cursor.Segment);
            directoryEntryRoot = this.UpsertMap(target, directoryEntryRoot, directoryEntryKey[..directoryEntryKeyLength], directoryEntryValue, ref plan);
            currentDirectoryInodeId = lastInodeId;
            changed = true;
        }

        return changed
            ? this.PublishMutation(target, in prefix, in root, ref plan, directoryEntryRoot, inodeRoot, attributeRoot, lastInodeId)
            : CreateDeltaTape(in plan, prefix.Generation);
    }

    private VfsDeltaTape ApplyDeleteFileToCarrier(IStreamRwsl<byte> target, string normalized)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolveParentDirectory(target, in root, normalized, out var parentInodeId, out var nameStart, out var nameLength)
            || !this.TryGetDirectoryEntry(target, root.DirectoryEntryRoot, parentInodeId, normalized.AsSpan(nameStart, nameLength), out var entry)
            || entry.Kind != NodeKind.File)
        {
            throw new FileNotFoundException($"File '{normalized}' was not found.", normalized);
        }

        if (!TryGetInodeRecord(target, root.InodeRoot, entry.InodeId, out var inode))
            throw new InvalidDataException($"Inode '{entry.InodeId}' was not found.");

        var directoryEntryRoot = root.DirectoryEntryRoot;
        var inodeRoot = root.InodeRoot;
        var attributeRoot = root.AttributeRoot;
        Span<byte> directoryEntryKey = stackalloc byte[GetDirectoryEntryKeyLength(normalized.AsSpan(nameStart, nameLength))];
        WriteDirectoryEntryKey(directoryEntryKey, parentInodeId, normalized.AsSpan(nameStart, nameLength));
        _ = this.DeleteMap(target, directoryEntryRoot, directoryEntryKey, ref plan, out directoryEntryRoot);
        Span<byte> inodeKey = stackalloc byte[sizeof(long)];
        WriteInt64Key(inodeKey, entry.InodeId);
        _ = this.DeleteMap(target, inodeRoot, inodeKey, ref plan, out inodeRoot);
        DeleteAllAttributesForInode(target, ref attributeRoot, entry.InodeId, ref plan);
        RetireMapSubtree(target, inode.ContentRoot, true, ref plan);

        return this.PublishMutation(target, in prefix, in root, ref plan, directoryEntryRoot, inodeRoot, attributeRoot, root.LastInodeId);
    }

    private VfsDeltaTape ApplyDeleteDirectoryToCarrier(IStreamRwsl<byte> target, string normalized, bool recursive)
    {
        if (normalized.Length == 0)
            throw new IOException("Cannot delete the root directory.");

        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolveParentDirectory(target, in root, normalized, out var parentInodeId, out var nameStart, out var nameLength)
            || !this.TryGetDirectoryEntry(target, root.DirectoryEntryRoot, parentInodeId, normalized.AsSpan(nameStart, nameLength), out var entry)
            || entry.Kind != NodeKind.Directory)
        {
            throw new DirectoryNotFoundException($"Directory '{normalized}' was not found.");
        }

        if (!TryGetInodeRecord(target, root.InodeRoot, entry.InodeId, out var inode))
            throw new InvalidDataException($"Inode '{entry.InodeId}' was not found.");

        if (!recursive && this.HasDirectoryChildren(target, root.DirectoryEntryRoot, entry.InodeId))
            throw new IOException("Directory is not empty.");

        var directoryEntryRoot = root.DirectoryEntryRoot;
        var inodeRoot = root.InodeRoot;
        var attributeRoot = root.AttributeRoot;
        this.DeleteDirectorySubtree(
            target,
            root.DirectoryEntryRoot,
            root.InodeRoot,
            parentInodeId,
            normalized.AsSpan(nameStart, nameLength),
            entry,
            inode,
            ref directoryEntryRoot,
            ref inodeRoot,
            ref attributeRoot,
            ref plan);

        return this.PublishMutation(target, in prefix, in root, ref plan, directoryEntryRoot, inodeRoot, attributeRoot, root.LastInodeId);
    }

    private VfsDeltaTape ApplyCreateFileToCarrier(IStreamRwsl<byte> target, string normalizedPath, bool mustNotExist)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);
        var directoryEntryRoot = root.DirectoryEntryRoot;
        var inodeRoot = root.InodeRoot;
        var attributeRoot = root.AttributeRoot;
        var lastInodeId = root.LastInodeId;

        if (!this.TryResolveParentDirectory(target, in root, normalizedPath, out var parentInodeId, out var nameStart, out var nameLength))
            throw new DirectoryNotFoundException($"Directory '{GetParentPath(normalizedPath)}' was not found.");

        var name = normalizedPath.AsSpan(nameStart, nameLength);

        if (this.TryGetDirectoryEntry(target, directoryEntryRoot, parentInodeId, name, out var existing))
        {
            if (existing.Kind != NodeKind.File)
                throw new IOException($"Path '{normalizedPath}' is not a file.");

            if (mustNotExist)
                throw new IOException($"File '{normalizedPath}' already exists.");

            return CreateDeltaTape(in plan, prefix.Generation);
        }

        lastInodeId++;
        Span<byte> inodeKey = stackalloc byte[sizeof(long)];
        Span<byte> inodeValue = stackalloc byte[sizeof(long) + sizeof(long) + sizeof(int)];
        Span<byte> directoryEntryValue = stackalloc byte[sizeof(long) + 1];
        WriteInt64Key(inodeKey, lastInodeId);
        WriteInodeValue(inodeValue, 0, InvalidChunkId, (int)FileAttributes.Normal);
        inodeRoot = this.UpsertMap(target, inodeRoot, inodeKey, inodeValue, ref plan);
        WriteDirectoryEntryValue(directoryEntryValue, lastInodeId, NodeKind.File);
        Span<byte> directoryEntryKey = stackalloc byte[GetDirectoryEntryKeyLength(name)];
        WriteDirectoryEntryKey(directoryEntryKey, parentInodeId, name);
        directoryEntryRoot = this.UpsertMap(target, directoryEntryRoot, directoryEntryKey, directoryEntryValue, ref plan);

        return this.PublishMutation(target, in prefix, in root, ref plan, directoryEntryRoot, inodeRoot, attributeRoot, lastInodeId);
    }

    private VfsDeltaTape ApplyResetFileToCarrier(IStreamRwsl<byte> target, string normalizedPath)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolvePathEntry(target, in root, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
            throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

        if (!TryGetInodeRecord(target, root.InodeRoot, entry.InodeId, out var inode))
            throw new InvalidDataException($"Inode '{entry.InodeId}' was not found.");

        var inodeRoot = root.InodeRoot;
        Span<byte> inodeKey = stackalloc byte[sizeof(long)];
        Span<byte> inodeValue = stackalloc byte[sizeof(long) + sizeof(long) + sizeof(int)];
        WriteInt64Key(inodeKey, entry.InodeId);
        WriteInodeValue(inodeValue, 0, InvalidChunkId, inode.Attributes);
        inodeRoot = this.UpsertMap(target, inodeRoot, inodeKey, inodeValue, ref plan);
        RetireMapSubtree(target, inode.ContentRoot, true, ref plan);

        return this.PublishMutation(target, in prefix, in root, ref plan, root.DirectoryEntryRoot, inodeRoot, root.AttributeRoot, root.LastInodeId);
    }

    private VfsDeltaTape ApplySetAttributeToCarrier(IStreamRwsl<byte> target, string normalizedPath, string attributeName, ReadOnlySpan<byte> value)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolvePathEntry(target, in root, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
            throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

        Span<byte> attributeKey = stackalloc byte[GetAttributeKeyLength(attributeName)];
        WriteAttributeKey(attributeKey, entry.InodeId, attributeName);
        var attributeRoot = this.UpsertMap(target, root.AttributeRoot, attributeKey, value, ref plan);

        return this.PublishMutation(target, in prefix, in root, ref plan, root.DirectoryEntryRoot, root.InodeRoot, attributeRoot, root.LastInodeId);
    }

    private VfsDeltaTape ApplyRemoveAttributeToCarrier(IStreamRwsl<byte> target, string normalizedPath, string attributeName, out bool removed)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolvePathEntry(target, in root, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
        {
            removed = false;

            return CreateDeltaTape(in plan, prefix.Generation);
        }

        var attributeRoot = root.AttributeRoot;
        Span<byte> attributeKey = stackalloc byte[GetAttributeKeyLength(attributeName)];
        WriteAttributeKey(attributeKey, entry.InodeId, attributeName);
        removed = this.DeleteMap(target, attributeRoot, attributeKey, ref plan, out attributeRoot);

        if (!removed)
            return CreateDeltaTape(in plan, prefix.Generation);

        return this.PublishMutation(target, in prefix, in root, ref plan, root.DirectoryEntryRoot, root.InodeRoot, attributeRoot, root.LastInodeId);
    }

    private VfsDeltaTape ApplyWriteFileToCarrier(IStreamRwsl<byte> target, string normalizedPath, long position, ReadOnlySpan<byte> source)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolvePathEntry(target, in root, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
            throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

        if (!TryGetInodeRecord(target, root.InodeRoot, entry.InodeId, out var inode))
            throw new InvalidDataException($"Inode '{entry.InodeId}' was not found.");

        var delta = this.BuildWriteFileDelta(target, inode, position, source);
        return this.ApplyFileDeltaToCarrier(target, in prefix, in root, entry.InodeId, ref plan, delta, validateBase: false);
    }

    private VfsDeltaTape ApplyWriteFileToCarrier(IStreamRwsl<byte> target, long inodeId, long position, ReadOnlySpan<byte> source)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!TryGetInodeRecord(target, root.InodeRoot, inodeId, out var inode))
            throw new FileNotFoundException($"File inode '{inodeId}' was not found.");

        var delta = this.BuildWriteFileDelta(target, inode, position, source);
        return this.ApplyFileDeltaToCarrier(target, in prefix, in root, inodeId, ref plan, delta, validateBase: false);
    }

    private VfsDeltaTape ApplyReplaceFileToCarrier(IStreamRwsl<byte> target, string normalizedPath, ReadOnlySpan<byte> source)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolvePathEntry(target, in root, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
            throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

        if (!TryGetInodeRecord(target, root.InodeRoot, entry.InodeId, out var inode))
            throw new FileNotFoundException($"File inode '{entry.InodeId}' was not found.");

        var delta = this.BuildReplaceFileDelta(source, inode);
        return this.ApplyFileDeltaToCarrier(target, in prefix, in root, entry.InodeId, ref plan, delta, validateBase: false);
    }

    private VfsDeltaTape ApplyReplaceFileToCarrier(IStreamRwsl<byte> target, long inodeId, ReadOnlySpan<byte> source)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);
        if (!TryGetInodeRecord(target, root.InodeRoot, inodeId, out var inode))
            throw new FileNotFoundException($"File inode '{inodeId}' was not found.");

        var delta = this.BuildReplaceFileDelta(source, inode);
        return this.ApplyFileDeltaToCarrier(target, in prefix, in root, inodeId, ref plan, delta, validateBase: false);
    }

    private VfsDeltaTape ApplySetFileLengthToCarrier(IStreamRwsl<byte> target, string normalizedPath, long newLength)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!this.TryResolvePathEntry(target, in root, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
            throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

        if (!TryGetInodeRecord(target, root.InodeRoot, entry.InodeId, out var inode))
            throw new InvalidDataException($"Inode '{entry.InodeId}' was not found.");

        if (newLength == inode.Length)
            return CreateDeltaTape(in plan, prefix.Generation);

        return this.ApplyFileDeltaToCarrier(
            target,
            in prefix,
            in root,
            entry.InodeId,
            ref plan,
            new FileDeltaMutation(inode.Length, inode.ContentRoot, inode.Attributes, newLength, []),
            validateBase: false);
    }

    private VfsDeltaTape ApplySetFileLengthToCarrier(IStreamRwsl<byte> target, long inodeId, long newLength)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (!TryGetInodeRecord(target, root.InodeRoot, inodeId, out var inode))
            throw new FileNotFoundException($"File inode '{inodeId}' was not found.");

        if (newLength == inode.Length)
            return CreateDeltaTape(in plan, prefix.Generation);

        return this.ApplyFileDeltaToCarrier(
            target,
            in prefix,
            in root,
            inodeId,
            ref plan,
            new FileDeltaMutation(inode.Length, inode.ContentRoot, inode.Attributes, newLength, []),
            validateBase: false);
    }

    private VfsDeltaTape ApplyFileDeltaToCarrier(IStreamRwsl<byte> target, string normalizedPath, long inodeId, FileDeltaMutation delta)
    {
        var plan = this.BeginMutation(target, out var prefix, out var root);

        if (inodeId == InvalidChunkId)
        {
            if (!this.TryResolvePathEntry(target, in root, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
                throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

            inodeId = entry.InodeId;
        }

        return this.ApplyFileDeltaToCarrier(target, in prefix, in root, inodeId, ref plan, delta, validateBase: true);
    }

    private VfsDeltaTape ApplyFileDeltaToCarrier(
        IStreamRwsl<byte> target,
        in PrefixState prefix,
        in SnapshotState root,
        long inodeId,
        ref VfsMutationPlan plan,
        FileDeltaMutation delta,
        bool validateBase)
    {
        if (!TryGetInodeRecord(target, root.InodeRoot, inodeId, out var inode))
            throw new FileNotFoundException($"File inode '{inodeId}' was not found.");

        if (validateBase
            && (inode.Length != delta.BaseLength
                || inode.ContentRoot != delta.BaseContentRoot
                || inode.Attributes != delta.BaseAttributes))
        {
            throw new IOException($"File inode '{inodeId}' changed since the file delta session started.");
        }

        var contentRoot = inode.ContentRoot;
        Span<byte> contentKey = stackalloc byte[sizeof(long)];
        Span<byte> contentValue = stackalloc byte[sizeof(long)];

        foreach (var chunk in delta.Chunks)
        {
            if (chunk.LogicalChunk * (long)this.chunkSize >= delta.NewLength)
                continue;

            var oldChunkId = TryGetContentChunk(target, contentRoot, chunk.LogicalChunk, out var resolvedOldChunkId)
                ? resolvedOldChunkId
                : InvalidChunkId;
            var newChunkId = plan.AllocateChunkId();

            switch (chunk.Kind)
            {
                case FileDeltaChunkKind.Draft:
                    this.CopyDraftChunkToPublished(target, newChunkId, chunk.DraftChunkId);
                    break;
                case FileDeltaChunkKind.Buffer:
                    this.WriteBufferedDataChunk(target, newChunkId, chunk.Buffer!);
                    break;
                default:
                    throw new InvalidOperationException($"Unknown file delta chunk kind '{chunk.Kind}'.");
            }

            WriteInt64Key(contentKey, chunk.LogicalChunk);
            WriteInt64LittleValue(contentValue, newChunkId);
            contentRoot = this.UpsertMap(target, contentRoot, contentKey, contentValue, ref plan);
            plan.RetireChunkId(oldChunkId);
        }

        if (delta.NewLength < inode.Length || delta.NewLength % this.chunkSize != 0)
        {
            var boundaryChunk = delta.NewLength / this.chunkSize;
            var resetPartial = delta.NewLength % this.chunkSize != 0;

            if (resetPartial)
            {
                var oldChunkId = TryGetContentChunk(target, contentRoot, boundaryChunk, out var resolvedOldChunkId)
                    ? resolvedOldChunkId
                    : InvalidChunkId;

                if (oldChunkId != InvalidChunkId)
                {
                    var newChunkId = plan.AllocateChunkId();
                    WriteResetDataChunk(target, newChunkId, oldChunkId, boundaryChunk, Math.Max(inode.Length, delta.NewLength), delta.NewLength);
                    WriteInt64Key(contentKey, boundaryChunk);
                    WriteInt64LittleValue(contentValue, newChunkId);
                    contentRoot = this.UpsertMap(target, contentRoot, contentKey, contentValue, ref plan);
                    plan.RetireChunkId(oldChunkId);
                }
            }

            if (delta.NewLength < inode.Length)
                TrimContentMapFromSource(target, inode.ContentRoot, boundaryChunk, resetPartial, ref contentRoot, ref plan);
        }

        Span<byte> inodeKey = stackalloc byte[sizeof(long)];
        Span<byte> inodeValue = stackalloc byte[sizeof(long) + sizeof(long) + sizeof(int)];
        WriteInt64Key(inodeKey, inodeId);
        WriteInodeValue(inodeValue, delta.NewLength, contentRoot, inode.Attributes);
        var inodeRoot = this.UpsertMap(target, root.InodeRoot, inodeKey, inodeValue, ref plan);
        return this.PublishMutation(target, in prefix, in root, ref plan, root.DirectoryEntryRoot, inodeRoot, root.AttributeRoot, root.LastInodeId);
    }

    private FileDeltaMutation BuildWriteFileDelta(IStreamRwsl<byte> target, in InodeRecord inode, long position, ReadOnlySpan<byte> source)
    {
        var end = checked(position + source.Length);
        var firstChunk = position / this.chunkSize;
        var lastChunkExclusive = (end + this.chunkSize - 1) / this.chunkSize;
        var chunks = new FileDeltaChunkMutation[lastChunkExclusive - firstChunk];
        var index = 0;

        for (var logicalChunk = firstChunk; logicalChunk < lastChunkExclusive; logicalChunk++)
        {
            var oldChunkId = TryGetContentChunk(target, inode.ContentRoot, logicalChunk, out var resolvedOldChunkId)
                ? resolvedOldChunkId
                : InvalidChunkId;
            var payload = this.BuildMergedChunkPayload(target, oldChunkId, logicalChunk, inode.Length, position, source);
            chunks[index++] = new(logicalChunk, FileDeltaChunkKind.Buffer, InvalidChunkId, payload);
        }

        return new(inode.Length, inode.ContentRoot, inode.Attributes, Math.Max(inode.Length, end), chunks);
    }

    private FileDeltaMutation BuildReplaceFileDelta(ReadOnlySpan<byte> source, in InodeRecord inode)
    {
        var chunkCount = (source.Length + this.chunkSize - 1) / this.chunkSize;
        var chunks = new FileDeltaChunkMutation[chunkCount];

        for (var logicalChunk = 0; logicalChunk < chunkCount; logicalChunk++)
        {
            var payload = this.BuildMergedChunkPayload(default!, InvalidChunkId, logicalChunk, 0, 0, source);
            chunks[logicalChunk] = new(logicalChunk, FileDeltaChunkKind.Buffer, InvalidChunkId, payload);
        }

        return new(inode.Length, inode.ContentRoot, inode.Attributes, source.Length, chunks);
    }

    private byte[] BuildMergedChunkPayload(
        IStreamRwsl<byte> target,
        long oldChunkId,
        long logicalChunk,
        long oldLength,
        long writeOffset,
        ReadOnlySpan<byte> source)
    {
        var payload = new byte[this.chunkSize];

        if (oldChunkId != InvalidChunkId)
            ReadAt(this, target, this.GetChunkOffset(oldChunkId) + chunkHeaderSize, payload);

        for (var index = 0; index < this.chunkSize; index++)
        {
            var absolute = logicalChunk * (long)this.chunkSize + index;
            var sourceIndex = absolute - writeOffset;

            if ((ulong)sourceIndex < (ulong)source.Length)
            {
                payload[index] = source[(int)sourceIndex];
                continue;
            }

            if (absolute >= oldLength)
                payload[index] = 0;
        }

        return payload;
    }

    private void WriteBufferedDataChunk(IStreamRwsl<byte> target, long chunkId, byte[] payload)
    {
        if (payload.Length != this.chunkSize)
            throw new InvalidDataException("Buffered file delta chunk has invalid payload length.");

        var chunkOffset = this.GetChunkOffset(chunkId);
        var requiredLength = chunkOffset + this.GetChunkRecordSize();

        if (requiredLength > target.Length)
            target.Length = requiredLength;

        WriteAt(this, target, chunkOffset + chunkHeaderSize, payload);
        this.WriteChunkHeader(target, chunkId, chunkData, InvalidChunkId, this.chunkSize, ComputeHash(payload));
    }

    private VfsMutationPlan BeginMutation(IStreamRwsl<byte> target, out PrefixState prefix, out SnapshotState snapshot)
    {
        prefix = this.committedPrefix;
        snapshot = prefix.Snapshot;
        return new(this, target, prefix.Generation + 1, snapshot.ReuseRoot, snapshot.DeferredReuseRoot);
    }

    private VfsDeltaTape PublishMutation(
        IStreamRwsl<byte> target,
        in PrefixState prefix,
        in SnapshotState currentSnapshot,
        ref VfsMutationPlan plan,
        long directoryEntryRoot,
        long inodeRoot,
        long attributeRoot,
        long lastInodeId)
    {
        var nextSnapshot = new SnapshotState(
            directoryEntryRoot,
            inodeRoot,
            attributeRoot,
            plan.ReusableRoot,
            plan.FinalizeDeferredReuseRoot(),
            currentSnapshot.RootDirectoryInodeId,
            lastInodeId,
            plan.AppendEndChunkIdExclusive,
            this.publishedChunkCapacity);
        var nextGeneration = prefix.Generation + 1;
        var nextSlot = (int)(nextGeneration & 1L);
        var nextPrefix = new PrefixState(true, nextSlot, this.chunkSize, nextGeneration, in nextSnapshot);
        WritePrefixSlot(this, target, in nextPrefix, nextSlot);

        return CreateDeltaTape(in plan, nextGeneration);
    }

    private static VfsDeltaTape CreateDeltaTape(in VfsMutationPlan plan, long generation)
        => new(generation, plan.AppendStartChunkId, plan.AppendEndChunkIdExclusive, plan.TakenReusableChunkLogRoot);

    internal bool TryTakeReusableChunk(IStreamRwsl<byte> target, ref long reuseRoot, ref VfsMutationPlan plan, out long chunkId)
    {
        if (!TryGetLastMapInt64Key(this, target, this.chunkSize, reuseRoot, out chunkId))
            return false;

        if (chunkId < 0 || chunkId >= plan.AppendStartChunkId)
            throw new InvalidDataException("Reuse map contains a chunk outside the reusable range.");

        Span<byte> key = stackalloc byte[sizeof(long)];
        WriteInt64Key(key, chunkId);
        var removed = false;
        plan.EnterAppendOnly();
        plan.EnterNoRetire();

        try
        {
            removed = this.DeleteMap(target, reuseRoot, key, ref plan, out reuseRoot);
        }
        finally
        {
            plan.ExitNoRetire();
            plan.ExitAppendOnly();
        }

        if (!removed)
            throw new InvalidDataException("Failed to remove a reusable chunk from the reuse map.");

        return true;
    }

    internal long PromoteDeferredReuseToReuseMap(IStreamRwsl<byte> target, long reuseRoot, long deferredReuseRoot, ref VfsMutationPlan plan)
    {
        var current = deferredReuseRoot;
        Span<byte> key = stackalloc byte[sizeof(long)];
        plan.EnterAppendOnly();
        plan.EnterNoRetire();

        try
        {
            while (current != InvalidChunkId)
            {
                var chunkId = ReadChunkIdLogEntry(target, current, out var next);
                WriteInt64Key(key, chunkId);
                reuseRoot = this.UpsertMap(target, reuseRoot, key, ReadOnlySpan<byte>.Empty, ref plan);
                WriteInt64Key(key, current);
                reuseRoot = this.UpsertMap(target, reuseRoot, key, ReadOnlySpan<byte>.Empty, ref plan);
                current = next;
            }
        }
        finally
        {
            plan.ExitNoRetire();
            plan.ExitAppendOnly();
        }

        return reuseRoot;
    }

    internal long ReadChunkIdLogEntry(IStreamRwsl<byte> target, long currentLogChunkId, out long nextLogChunkId)
    {
        var header = this.ReadChunkHeader(target, currentLogChunkId);

        if (header.Kind != chunkChunkLog)
            throw new InvalidDataException("Deferred reuse log chunk is invalid.");

        nextLogChunkId = header.LinkChunkId;
        return DecodeChunkIdLogValue(header.UsedBytes, header.PayloadChecksum);
    }

    internal long ReadDeferredReuseChunkId(IStreamRwsl<byte> target, long currentLogChunkId, out long nextLogChunkId)
        => this.ReadChunkIdLogEntry(target, currentLogChunkId, out nextLogChunkId);

    internal void RewriteDeferredChunkLogTailLink(IStreamRwsl<byte> target, long tailChunkId, long nextLogRootChunkId)
    {
        var header = this.ReadChunkHeader(target, tailChunkId);

        if (header.Kind != chunkChunkLog)
            throw new InvalidDataException("Deferred reuse log chunk is invalid.");

        this.WriteChunkHeader(target, tailChunkId, chunkChunkLog, nextLogRootChunkId, header.UsedBytes, header.PayloadChecksum);
    }

    internal long PrependTakenReusableChunkLogEntry(IStreamRwsl<byte> target, long nextLogRootChunkId, long chunkId, ref VfsMutationPlan plan)
        => this.PrependChunkIdLogEntry(target, nextLogRootChunkId, chunkId, ref plan, appendOnly: true);

    private bool TryGetDirectoryEntry(IStreamRwsl<byte> target, long directoryEntryRoot, long parentInodeId, ReadOnlySpan<char> name, out NamespaceEntry entry)
        => TryGetDirectoryEntryMapValue(this, target, this.chunkSize, directoryEntryRoot, parentInodeId, name, out entry);

    private bool TryResolveParentDirectory(IStreamRwsl<byte> target, in SnapshotState root, ReadOnlySpan<char> normalizedPath, out long parentInodeId, out int nameStart, out int nameLength)
    {
        var cursor = new VfsPathCursor(normalizedPath);
        parentInodeId = root.RootDirectoryInodeId;
        nameStart = 0;
        nameLength = 0;

        if (!cursor.MoveNext())
            return false;

        nameStart = cursor.SegmentStart;
        nameLength = cursor.Segment.Length;

        while (cursor.MoveNext())
        {
            if (!this.TryGetDirectoryEntry(target, root.DirectoryEntryRoot, parentInodeId, normalizedPath.Slice(nameStart, nameLength), out var entry)
                || entry.Kind != NodeKind.Directory)
            {
                parentInodeId = InvalidChunkId;
                nameStart = 0;
                nameLength = 0;

                return false;
            }

            parentInodeId = entry.InodeId;
            nameStart = cursor.SegmentStart;
            nameLength = cursor.Segment.Length;
        }

        return true;
    }

    private bool TryResolvePathEntry(IStreamRwsl<byte> target, in SnapshotState root, ReadOnlySpan<char> normalizedPath, out NamespaceEntry entry)
    {
        if (normalizedPath.Length == 0)
        {
            entry = new(root.RootDirectoryInodeId, NodeKind.Directory);

            return true;
        }

        if (!this.TryResolveParentDirectory(target, in root, normalizedPath, out var parentInodeId, out var nameStart, out var nameLength))
        {
            entry = default;

            return false;
        }

        return this.TryGetDirectoryEntry(target, root.DirectoryEntryRoot, parentInodeId, normalizedPath.Slice(nameStart, nameLength), out entry);
    }

    private bool TryGetInodeRecord(IStreamRwsl<byte> target, long inodeRoot, long inodeId, out InodeRecord inode)
        => TryGetInodeMapValue(this, target, this.chunkSize, inodeRoot, inodeId, out inode);

    private bool TryGetContentChunk(IStreamRwsl<byte> target, long contentRoot, long logicalChunk, out long chunkId)
    {
        if (contentRoot != InvalidChunkId
            && TryGetInt64MapValue(this, target, this.chunkSize, contentRoot, logicalChunk, out chunkId))
        {
            return true;
        }

        chunkId = InvalidChunkId;
        return false;
    }

    private void DeleteAllAttributesForInode(IStreamRwsl<byte> target, ref long attributeRoot, long inodeId, ref VfsMutationPlan plan)
    {
        DeleteAttributesForInodeFromSource(target, attributeRoot, inodeId, ref attributeRoot, ref plan);
    }

    private bool HasDirectoryChildren(IStreamRwsl<byte> target, long directoryEntryRoot, long parentInodeId)
        => this.HasDirectoryChildrenCore(target, directoryEntryRoot, parentInodeId);

    private bool HasDirectoryChildrenCore(
        IStreamRwsl<byte> target,
        long rootChunkId,
        long parentInodeId,
        bool stopOnFirst = true)
    {
        if (rootChunkId == InvalidChunkId)
            return false;

        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                if (this.HasDirectoryChildrenCore(target, cursor.ReadInt64LittleEndian(), parentInodeId, stopOnFirst))
                    return true;
            }

            return false;
        }

        Span<byte> parentBuffer = stackalloc byte[sizeof(long)];

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();

            if (keyLength < sizeof(long))
                throw new InvalidDataException("Directory entry key is too short.");

            cursor.ReadExactly(parentBuffer);
            var candidateParentInodeId = BinaryPrimitives.ReadInt64BigEndian(parentBuffer);
            var nameByteCount = keyLength - sizeof(long);

            if (candidateParentInodeId != parentInodeId)
            {
                cursor.Skip(nameByteCount);
                var valueLength = cursor.ReadInt32LittleEndian();

                if (valueLength < 0)
                    throw new InvalidDataException("Map value length is invalid.");

                cursor.Skip(valueLength);
                continue;
            }

            if (stopOnFirst)
                return true;

            cursor.Skip(nameByteCount);
            var valueLengthMatched = cursor.ReadInt32LittleEndian();

            if (valueLengthMatched < 0)
                throw new InvalidDataException("Map value length is invalid.");

            cursor.Skip(valueLengthMatched);
        }

        return false;
    }

    private void DeleteDirectorySubtree(
        IStreamRwsl<byte> target,
        long sourceDirectoryEntryRoot,
        long sourceInodeRoot,
        long parentInodeId,
        ReadOnlySpan<char> name,
        NamespaceEntry entry,
        in InodeRecord inode,
        ref long directoryEntryRoot,
        ref long inodeRoot,
        ref long attributeRoot,
        ref VfsMutationPlan plan)
    {
        if (entry.Kind == NodeKind.Directory)
            this.DeleteDirectoryChildrenFromSource(
                target,
                sourceDirectoryEntryRoot,
                sourceInodeRoot,
                entry.InodeId,
                ref directoryEntryRoot,
                ref inodeRoot,
                ref attributeRoot,
                ref plan);
        else
            RetireMapSubtree(target, inode.ContentRoot, true, ref plan);

        Span<byte> directoryEntryKey = stackalloc byte[GetDirectoryEntryKeyLength(name)];
        WriteDirectoryEntryKey(directoryEntryKey, parentInodeId, name);
        _ = this.DeleteMap(target, directoryEntryRoot, directoryEntryKey, ref plan, out directoryEntryRoot);
        Span<byte> inodeKey = stackalloc byte[sizeof(long)];
        WriteInt64Key(inodeKey, entry.InodeId);
        _ = this.DeleteMap(target, inodeRoot, inodeKey, ref plan, out inodeRoot);
        DeleteAllAttributesForInode(target, ref attributeRoot, entry.InodeId, ref plan);
    }

    private void DeleteDirectoryChildrenFromSource(
        IStreamRwsl<byte> target,
        long sourceDirectoryEntryRoot,
        long sourceInodeRoot,
        long parentInodeId,
        ref long directoryEntryRoot,
        ref long inodeRoot,
        ref long attributeRoot,
        ref VfsMutationPlan plan)
    {
        this.DeleteDirectoryChildrenFromSourceCore(
            target,
            sourceDirectoryEntryRoot,
            sourceInodeRoot,
            sourceDirectoryEntryRoot,
            parentInodeId,
            ref directoryEntryRoot,
            ref inodeRoot,
            ref attributeRoot,
            ref plan);
    }

    private void DeleteDirectoryChildrenFromSourceCore(
        IStreamRwsl<byte> target,
        long sourceDirectoryEntryRoot,
        long sourceInodeRoot,
        long sourceNodeRoot,
        long parentInodeId,
        ref long directoryEntryRoot,
        ref long inodeRoot,
        ref long attributeRoot,
        ref VfsMutationPlan plan)
    {
        if (sourceNodeRoot == InvalidChunkId)
            return;

        ReadMapNodeHeader(this, target, this.chunkSize, sourceNodeRoot, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceNodeRoot, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                this.DeleteDirectoryChildrenFromSourceCore(
                    target,
                    sourceDirectoryEntryRoot,
                    sourceInodeRoot,
                    cursor.ReadInt64LittleEndian(),
                    parentInodeId,
                    ref directoryEntryRoot,
                    ref inodeRoot,
                    ref attributeRoot,
                    ref plan);
            }

            return;
        }

        Span<byte> parentBuffer = stackalloc byte[sizeof(long)];

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();

            if (keyLength < sizeof(long))
                throw new InvalidDataException("Directory entry key is too short.");

            cursor.ReadExactly(parentBuffer);
            var candidateParentInodeId = BinaryPrimitives.ReadInt64BigEndian(parentBuffer);
            var nameByteCount = keyLength - sizeof(long);

            if (candidateParentInodeId != parentInodeId)
            {
                cursor.Skip(nameByteCount);
                var valueLength = cursor.ReadInt32LittleEndian();

                if (valueLength < 0)
                    throw new InvalidDataException("Map value length is invalid.");

                cursor.Skip(valueLength);
                continue;
            }

            var childName = ReadUtf16BigEndianString(ref cursor, nameByteCount, "Directory entry key length is invalid.");
            var entry = ReadDirectoryEntryValue(ref cursor, cursor.ReadInt32LittleEndian());

            if (!this.TryGetInodeRecord(target, sourceInodeRoot, entry.InodeId, out var inode))
                throw new InvalidDataException($"Inode '{entry.InodeId}' was not found.");

            this.DeleteDirectorySubtree(
                target,
                sourceDirectoryEntryRoot,
                sourceInodeRoot,
                parentInodeId,
                childName.AsSpan(),
                entry,
                inode,
                ref directoryEntryRoot,
                ref inodeRoot,
                ref attributeRoot,
                ref plan);
        }
    }

    private void DeleteAttributesForInodeFromSource(
        IStreamRwsl<byte> target,
        long sourceRoot,
        long inodeId,
        ref long currentRoot,
        ref VfsMutationPlan plan)
    {
        if (sourceRoot == InvalidChunkId)
            return;

        ReadMapNodeHeader(this, target, this.chunkSize, sourceRoot, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRoot, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                DeleteAttributesForInodeFromSource(target, cursor.ReadInt64LittleEndian(), inodeId, ref currentRoot, ref plan);
            }

            return;
        }

        var measureCursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRoot, chunkMetadata);
        SkipMapNodeHeader(ref measureCursor);
        var maxKeyLength = 0;

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = measureCursor.ReadInt32LittleEndian();

            if (keyLength < sizeof(long))
                throw new InvalidDataException("Attribute key is too short.");

            maxKeyLength = Math.Max(maxKeyLength, keyLength);
            measureCursor.Skip(keyLength);
            var valueLength = measureCursor.ReadInt32LittleEndian();

            if (valueLength < 0)
                throw new InvalidDataException("Map value length is invalid.");

            measureCursor.Skip(valueLength);
        }

        cursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRoot, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        Span<byte> key = stackalloc byte[maxKeyLength];

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();

            if (keyLength < sizeof(long))
                throw new InvalidDataException("Attribute key is too short.");

            cursor.ReadExactly(key[..keyLength]);
            var valueLength = cursor.ReadInt32LittleEndian();

            if (valueLength < 0)
                throw new InvalidDataException("Map value length is invalid.");

            cursor.Skip(valueLength);

            if (DecodeAttributeKeyInodeId(key[..keyLength]) == inodeId)
                _ = this.DeleteMap(target, currentRoot, key[..keyLength], ref plan, out currentRoot);
        }
    }

    private void TrimContentMapFromSource(
        IStreamRwsl<byte> target,
        long sourceRoot,
        long boundaryChunk,
        bool resetPartial,
        ref long currentRoot,
        ref VfsMutationPlan plan)
    {
        if (sourceRoot == InvalidChunkId)
            return;

        ReadMapNodeHeader(this, target, this.chunkSize, sourceRoot, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRoot, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                TrimContentMapFromSource(target, cursor.ReadInt64LittleEndian(), boundaryChunk, resetPartial, ref currentRoot, ref plan);
            }

            return;
        }

        Span<byte> value = stackalloc byte[sizeof(long)];
        Span<byte> key = stackalloc byte[sizeof(long)];

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();

            if (keyLength != sizeof(long))
                throw new InvalidDataException("Int64 key must occupy exactly 8 bytes.");

            var logicalChunk = cursor.ReadInt64BigEndian();
            var valueLength = cursor.ReadInt32LittleEndian();

            if (valueLength != sizeof(long))
                throw new InvalidDataException("Content map value length is invalid.");

            cursor.ReadExactly(value);
            var shouldDelete = resetPartial ? logicalChunk > boundaryChunk : logicalChunk >= boundaryChunk;

            if (!shouldDelete)
                continue;

            WriteInt64Key(key, logicalChunk);
            _ = this.DeleteMap(target, currentRoot, key, ref plan, out currentRoot);
            plan.RetireChunkId(BinaryPrimitives.ReadInt64LittleEndian(value));
        }
    }

    internal long PrependDeferredChunkLogEntry(IStreamRwsl<byte> target, long nextLogRootChunkId, long chunkId, ref VfsMutationPlan plan)
        => this.PrependChunkIdLogEntry(target, nextLogRootChunkId, chunkId, ref plan, appendOnly: true);

    private long PrependChunkIdLogEntry(IStreamRwsl<byte> target, long nextLogRootChunkId, long chunkId, ref VfsMutationPlan plan, bool appendOnly)
    {
        var currentChunkId = appendOnly ? plan.AllocateAppendChunkId() : plan.AllocateChunkId();
        var recordOffset = this.GetChunkOffset(currentChunkId);
        var requiredLength = recordOffset + chunkHeaderSize + this.chunkSize;

        if (requiredLength > target.Length)
            target.Length = requiredLength;

        EncodeChunkIdLogValue(chunkId, out var usedBytes, out var payloadChecksum);
        this.WriteChunkHeader(target, currentChunkId, chunkChunkLog, nextLogRootChunkId, usedBytes, payloadChecksum);
        return currentChunkId;
    }

    private static void EncodeChunkIdLogValue(long chunkId, out int usedBytes, out uint payloadChecksum)
    {
        usedBytes = unchecked((int)(uint)chunkId);
        payloadChecksum = (uint)(chunkId >> 32);
    }

    private static long DecodeChunkIdLogValue(int usedBytes, uint payloadChecksum)
        => ((long)payloadChecksum << 32) | (uint)usedBytes;

    private void RetireMapSubtree(IStreamRwsl<byte> target, long rootChunkId, bool retireContentChunks, ref VfsMutationPlan plan)
    {
        if (rootChunkId == InvalidChunkId)
            return;

        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                RetireMapSubtree(target, cursor.ReadInt64LittleEndian(), retireContentChunks, ref plan);
            }
        }
        else if (retireContentChunks)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                var valueLength = cursor.ReadInt32LittleEndian();

                if (valueLength != sizeof(long))
                    throw new InvalidDataException("Content map value length is invalid.");

                plan.RetireChunkId(cursor.ReadInt64LittleEndian());
            }
        }
        else
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                var valueLength = cursor.ReadInt32LittleEndian();

                if (valueLength < 0)
                    throw new InvalidDataException("Map value length is invalid.");

                cursor.Skip(valueLength);
            }
        }

        plan.RetireStream(rootChunkId);
    }

    private static string CombinePath(string parentPath, string childName)
        => parentPath.Length == 0 ? childName : parentPath + "/" + childName;

}
