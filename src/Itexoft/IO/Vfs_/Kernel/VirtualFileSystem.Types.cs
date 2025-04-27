// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private enum NodeKind : byte
    {
        Directory = 1,
        File = 2,
    }

    private enum MapNodeKind : byte
    {
        Leaf = 1,
        Internal = 2,
    }

    private enum MirrorWinner : byte
    {
        None,
        Primary,
        Mirror,
    }

    private enum MapValidationKind : byte
    {
        DirectoryEntry = 0,
        Attribute = 1,
        Inode = 2,
        Reuse = 3,
        Content = 4,
    }

    private readonly struct PrefixState
    {
        public PrefixState(bool isValid, int slotIndex, int chunkSize, long generation, in SnapshotState snapshot)
        {
            this.IsValid = isValid;
            this.SlotIndex = slotIndex;
            this.ChunkSize = chunkSize;
            this.Generation = generation;
            this.Snapshot = snapshot;
        }

        public bool IsValid { get; }

        public int SlotIndex { get; }

        public int ChunkSize { get; }

        public long Generation { get; }

        public SnapshotState Snapshot { get; }
    }

    private readonly struct SnapshotState
    {
        public SnapshotState(
            long directoryEntryRoot,
            long inodeRoot,
            long attributeRoot,
            long reuseRoot,
            long deferredReuseRoot,
            long rootDirectoryInodeId,
            long lastInodeId,
            long publishedChunkCount,
            long publishedChunkCapacity)
        {
            this.DirectoryEntryRoot = directoryEntryRoot;
            this.InodeRoot = inodeRoot;
            this.AttributeRoot = attributeRoot;
            this.ReuseRoot = reuseRoot;
            this.DeferredReuseRoot = deferredReuseRoot;
            this.RootDirectoryInodeId = rootDirectoryInodeId;
            this.LastInodeId = lastInodeId;
            this.PublishedChunkCount = publishedChunkCount;
            this.PublishedChunkCapacity = publishedChunkCapacity;
        }

        public long DirectoryEntryRoot { get; }

        public long InodeRoot { get; }

        public long AttributeRoot { get; }

        public long ReuseRoot { get; }

        public long DeferredReuseRoot { get; }

        public long RootDirectoryInodeId { get; }

        public long LastInodeId { get; }

        public long PublishedChunkCount { get; }

        public long PublishedChunkCapacity { get; }

        public bool Matches(SnapshotState other)
            => this.DirectoryEntryRoot == other.DirectoryEntryRoot
            && this.InodeRoot == other.InodeRoot
            && this.AttributeRoot == other.AttributeRoot
            && this.ReuseRoot == other.ReuseRoot
            && this.DeferredReuseRoot == other.DeferredReuseRoot
            && this.RootDirectoryInodeId == other.RootDirectoryInodeId
            && this.LastInodeId == other.LastInodeId
            && this.PublishedChunkCount == other.PublishedChunkCount
            && this.PublishedChunkCapacity == other.PublishedChunkCapacity;
    }

    private readonly struct VfsDeltaTape(
        long generation,
        long appendStartChunkId,
        long appendEndChunkIdExclusive,
        long reusedChunkLogRoot)
    {
        public long Generation { get; } = generation;

        public long AppendStartChunkId { get; } = appendStartChunkId;

        public long AppendEndChunkIdExclusive { get; } = appendEndChunkIdExclusive;

        public long ReusedChunkLogRoot { get; } = reusedChunkLogRoot;

        public bool HasCarrierWrites => this.AppendStartChunkId != this.AppendEndChunkIdExclusive || this.ReusedChunkLogRoot != InvalidChunkId;
    }

    internal readonly struct ChunkHeader
    {
        public ChunkHeader(byte kind, long linkChunkId, int usedBytes, uint payloadChecksum)
        {
            this.Kind = kind;
            this.LinkChunkId = linkChunkId;
            this.UsedBytes = usedBytes;
            this.PayloadChecksum = payloadChecksum;
        }

        public byte Kind { get; }

        public long LinkChunkId { get; }

        public int UsedBytes { get; }

        public uint PayloadChecksum { get; }
    }

    private readonly struct NamespaceEntry(long inodeId, NodeKind kind)
    {
        public long InodeId { get; } = inodeId;

        public NodeKind Kind { get; } = kind;
    }

    private readonly struct InodeRecord(long length, long contentRoot, int attributes)
    {
        public long Length { get; } = length;

        public long ContentRoot { get; } = contentRoot;

        public int Attributes { get; } = attributes;
    }

    private readonly struct MapRewriteResult(
        bool changed,
        bool exists,
        long rootId,
        int level,
        bool hasSplit,
        long splitRightRootId)
    {
        public bool Changed { get; } = changed;

        public bool Exists { get; } = exists;

        public long RootId { get; } = rootId;

        public int Level { get; } = level;

        public bool HasSplit { get; } = hasSplit;

        public long SplitRightRootId { get; } = splitRightRootId;
    }

    private readonly struct FileDeltaSessionState(
        long sessionId,
        long inodeId,
        long baseLength,
        long baseContentRoot,
        int baseAttributes,
        long currentLength)
    {
        public long SessionId { get; } = sessionId;

        public long InodeId { get; } = inodeId;

        public long BaseLength { get; } = baseLength;

        public long BaseContentRoot { get; } = baseContentRoot;

        public int BaseAttributes { get; } = baseAttributes;

        public long CurrentLength { get; } = currentLength;
    }
}
