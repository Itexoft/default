// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using Itexoft.Threading.Core.Lane;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private IReadOnlyList<string> EnumerateCore(string normalizedPath)
    {
        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            return EnumerateCore(this.primary, in prefix, normalizedPath);
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private IReadOnlyList<string> EnumerateCore(IStreamRwsl<byte> source, in PrefixState prefix, string normalizedPath)
    {
        var snapshot = prefix.Snapshot;
        var directoryInodeId = snapshot.RootDirectoryInodeId;
        var parentPath = normalizedPath;

        if (normalizedPath.Length != 0)
        {
            if (!TryResolvePathEntryFrom(this, source, in prefix, normalizedPath, out var entry) || entry.Kind != NodeKind.Directory)
                throw new DirectoryNotFoundException($"Directory '{normalizedPath}' was not found.");

            directoryInodeId = entry.InodeId;
        }

        var result = new List<string>();
        CollectDirectoryChildNames(source, snapshot.DirectoryEntryRoot, directoryInodeId, result);

        return result;
    }

    private bool TryGetNodeKind(string normalizedPath, out NodeKind kind)
    {
        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            return TryGetNodeKindFrom(this.primary, in prefix, normalizedPath, out kind);
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private static bool TryGetNodeKindFrom(IStreamRwsl<byte> source, in PrefixState prefix, ReadOnlySpan<char> normalizedPath, out NodeKind kind)
    {
        if (normalizedPath.Length == 0)
        {
            kind = NodeKind.Directory;

            return true;
        }

        if (!TryResolvePathEntryFrom(null, source, in prefix, normalizedPath, out var entry))
        {
            kind = default;

            return false;
        }

        kind = entry.Kind;

        return true;
    }

    private static bool TryGetFileLengthFrom(IStreamRwsl<byte> source, in PrefixState prefix, ReadOnlySpan<char> normalizedPath, out long length)
    {
        if (!TryGetFileRecordFrom(null, source, in prefix, normalizedPath, out var file))
        {
            length = default;

            return false;
        }

        length = file.Length;

        return true;
    }

    private static bool TryGetFileLengthFrom(IStreamRwsl<byte> source, in PrefixState prefix, long inodeId, out long length)
    {
        if (!TryGetInodeRecordFrom(null, source, in prefix, inodeId, out var file))
        {
            length = default;

            return false;
        }

        length = file.Length;

        return true;
    }

    private static bool TryGetAttributeFrom(
        IStreamRwsl<byte> source,
        in PrefixState prefix,
        ReadOnlySpan<char> normalizedPath,
        ReadOnlySpan<char> attributeName,
        out byte[] value)
    {
        if (!TryResolvePathEntryFrom(null, source, in prefix, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
        {
            value = [];

            return false;
        }

        var snapshot = prefix.Snapshot;
        Span<byte> key = stackalloc byte[GetAttributeKeyLength(attributeName)];
        WriteAttributeKey(key, entry.InodeId, attributeName);
        return TryMapGetValue(null, source, prefix.ChunkSize, snapshot.AttributeRoot, key, out value);
    }

    private static bool TryResolveContentChunkFrom(
        IStreamRwsl<byte> source,
        in PrefixState prefix,
        ReadOnlySpan<char> normalizedPath,
        long logicalChunk,
        out long chunkId)
    {
        if (!TryGetFileRecordFrom(null, source, in prefix, normalizedPath, out var file) || file.ContentRoot == InvalidChunkId)
        {
            chunkId = InvalidChunkId;

            return false;
        }

        if (!TryGetInt64MapValue(null, source, prefix.ChunkSize, file.ContentRoot, logicalChunk, out chunkId))
        {
            chunkId = InvalidChunkId;

            return false;
        }

        return true;
    }

    private static bool TryResolveContentChunkFrom(
        IStreamRwsl<byte> source,
        in PrefixState prefix,
        long inodeId,
        long logicalChunk,
        out long chunkId)
    {
        if (!TryGetInodeRecordFrom(null, source, in prefix, inodeId, out var file) || file.ContentRoot == InvalidChunkId)
        {
            chunkId = InvalidChunkId;

            return false;
        }

        if (!TryGetInt64MapValue(null, source, prefix.ChunkSize, file.ContentRoot, logicalChunk, out chunkId))
        {
            chunkId = InvalidChunkId;

            return false;
        }

        return true;
    }

    private static bool TryGetDirectoryEntryFrom(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long directoryEntryRoot,
        long parentInodeId,
        ReadOnlySpan<char> name,
        out NamespaceEntry entry)
        => TryGetDirectoryEntryMapValue(owner, source, chunkSize, directoryEntryRoot, parentInodeId, name, out entry);

    private static bool TryResolveParentDirectoryFrom(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        in PrefixState prefix,
        ReadOnlySpan<char> normalizedPath,
        out long parentInodeId,
        out int nameStart,
        out int nameLength)
    {
        var snapshot = prefix.Snapshot;
        var cursor = new VfsPathCursor(normalizedPath);
        parentInodeId = snapshot.RootDirectoryInodeId;
        nameStart = 0;
        nameLength = 0;

        if (!cursor.MoveNext())
            return false;

        nameStart = cursor.SegmentStart;
        nameLength = cursor.Segment.Length;

        while (cursor.MoveNext())
        {
            if (!TryGetDirectoryEntryFrom(owner, source, prefix.ChunkSize, snapshot.DirectoryEntryRoot, parentInodeId, normalizedPath.Slice(nameStart, nameLength), out var entry)
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

    private static bool TryResolvePathEntryFrom(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        in PrefixState prefix,
        ReadOnlySpan<char> normalizedPath,
        out NamespaceEntry entry)
    {
        if (normalizedPath.Length == 0)
        {
            var snapshot = prefix.Snapshot;
            entry = new(snapshot.RootDirectoryInodeId, NodeKind.Directory);

            return true;
        }

        if (!TryResolveParentDirectoryFrom(owner, source, in prefix, normalizedPath, out var parentInodeId, out var nameStart, out var nameLength))
        {
            entry = default;

            return false;
        }

        return TryGetDirectoryEntryFrom(
            owner,
            source,
            prefix.ChunkSize,
            prefix.Snapshot.DirectoryEntryRoot,
            parentInodeId,
            normalizedPath.Slice(nameStart, nameLength),
            out entry);
    }

    private static bool TryGetInodeRecordFrom(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        in PrefixState prefix,
        long inodeId,
        out InodeRecord inode)
    {
        var snapshot = prefix.Snapshot;

        if (!TryGetInodeMapValue(owner, source, prefix.ChunkSize, snapshot.InodeRoot, inodeId, out inode))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetFileRecordFrom(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        in PrefixState prefix,
        ReadOnlySpan<char> normalizedPath,
        out InodeRecord file)
    {
        if (!TryResolvePathEntryFrom(owner, source, in prefix, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
        {
            file = default;

            return false;
        }

        return TryGetInodeRecordFrom(owner, source, in prefix, entry.InodeId, out file);
    }

    private long ResolveOpenFileInodeId(string normalizedPath)
    {
        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            if (!TryResolvePathEntryFrom(this, this.primary, in prefix, normalizedPath, out var entry) || entry.Kind != NodeKind.File)
                throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

            return entry.InodeId;
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private void CollectDirectoryChildNames(IStreamRwsl<byte> source, long rootChunkId, long parentInodeId, List<string> destination)
    {
        if (rootChunkId == InvalidChunkId)
            return;

        ReadMapNodeHeader(this, source, this.chunkSize, rootChunkId, out var kind, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(this, source, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);

        if (kind == MapNodeKind.Internal)
        {
            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                CollectDirectoryChildNames(source, cursor.ReadInt64LittleEndian(), parentInodeId, destination);
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

            if (candidateParentInodeId == parentInodeId)
                destination.Add(ReadUtf16BigEndianString(ref cursor, nameByteCount, "Directory entry key length is invalid."));
            else
                cursor.Skip(nameByteCount);

            var valueLength = cursor.ReadInt32LittleEndian();

            if (valueLength < 0)
                throw new InvalidDataException("Map value length is invalid.");

            cursor.Skip(valueLength);
        }
    }
}
