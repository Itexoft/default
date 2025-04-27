// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Core;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private ref struct ChunkStreamCursor(VirtualFileSystem? owner, IStreamRwsl<byte> source, int chunkSize, long rootChunkId, byte expectedKind)
    {
        private readonly VirtualFileSystem? owner = owner;
        private readonly IStreamRwsl<byte> source = source;
        private readonly int chunkSize = chunkSize;
        private readonly byte expectedKind = expectedKind;
        private long currentChunkId = rootChunkId;
        private long nextChunkId = InvalidChunkId;
        private int currentOffset;
        private int currentUsedBytes = -1;
        private CursorWindow window;
        private int windowOffset;
        private int windowCount;

        public byte ReadByte()
        {
            Span<byte> one = stackalloc byte[1];
            this.ReadExactly(one);
            return one[0];
        }

        public int ReadInt32LittleEndian()
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            this.ReadExactly(buffer);
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        public long ReadInt64LittleEndian()
        {
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            this.ReadExactly(buffer);
            return BinaryPrimitives.ReadInt64LittleEndian(buffer);
        }

        public long ReadInt64BigEndian()
        {
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            this.ReadExactly(buffer);
            return BinaryPrimitives.ReadInt64BigEndian(buffer);
        }

        public void ReadExactly(scoped Span<byte> destination)
        {
            var offset = 0;

            while (offset < destination.Length)
            {
                if (!this.TryEnsureWindow())
                    throw new EndOfStreamException("Chunk stream ended unexpectedly.");

                var readable = Math.Min(this.windowCount - this.windowOffset, destination.Length - offset);
                this.window[this.windowOffset..(this.windowOffset + readable)].CopyTo(destination[offset..]);
                this.windowOffset += readable;
                offset += readable;
            }
        }

        public void Skip(int length)
        {
            var remaining = length;

            while (remaining > 0)
            {
                if (!this.TryEnsureWindow())
                    throw new EndOfStreamException("Chunk stream ended unexpectedly.");

                var readable = Math.Min(this.windowCount - this.windowOffset, remaining);
                this.windowOffset += readable;
                remaining -= readable;
            }
        }

        public bool TryReadByte(out byte value)
        {
            if (!this.TryEnsureWindow())
            {
                value = 0;
                return false;
            }

            value = this.window[this.windowOffset++];
            return true;
        }

        private bool TryEnsureWindow()
        {
            if (this.windowOffset < this.windowCount)
                return true;

            if (!this.TryEnsureReadableBytes())
                return false;

            var readable = Math.Min(this.currentUsedBytes - this.currentOffset, prefixLength);
            ReadAt(
                this.owner,
                this.source,
                prefixLength + this.currentChunkId * (long)(this.chunkSize + chunkHeaderSize) + chunkHeaderSize + this.currentOffset,
                this.window[..readable]);
            this.currentOffset += readable;
            this.windowOffset = 0;
            this.windowCount = readable;
            return true;
        }

        private bool TryEnsureReadableBytes()
        {
            while (true)
            {
                if (this.currentChunkId == InvalidChunkId)
                    return false;

                if (this.currentUsedBytes < 0)
                    this.OpenCurrentChunk();

                if (this.currentOffset < this.currentUsedBytes)
                    return true;

                this.currentChunkId = this.nextChunkId;
                this.currentUsedBytes = -1;
                this.currentOffset = 0;
                this.windowOffset = 0;
                this.windowCount = 0;
            }
        }

        private void OpenCurrentChunk()
        {
            var header = ReadChunkHeader(this.owner, this.source, this.chunkSize, this.currentChunkId);

            if (header.Kind != this.expectedKind)
                throw new InvalidDataException($"Chunk '{this.currentChunkId}' has invalid kind '{header.Kind}'.");

            ValidateChunkChecksum(this.owner, this.source, this.chunkSize, this.currentChunkId, header.UsedBytes, header.PayloadChecksum);
            this.currentUsedBytes = header.UsedBytes;
            this.nextChunkId = header.LinkChunkId;
            this.windowOffset = 0;
            this.windowCount = 0;
        }
    }

    [InlineArray(prefixLength)]
    [StructLayout(LayoutKind.Sequential)]
    private struct CursorWindow
    {
        private byte value0;
    }

    private ref struct ChunkStreamWriter(VirtualFileSystem owner, IStreamRwsl<byte> target, byte kind, ref VfsMutationPlan plan)
    {
        private readonly VirtualFileSystem owner = owner;
        private readonly IStreamRwsl<byte> target = target;
        private readonly byte kind = kind;
        private readonly Ref<VfsMutationPlan> plan = new(ref plan);
        private readonly int chunkSize = owner.chunkSize;
        private long rootChunkId = InvalidChunkId;
        private long currentChunkId = InvalidChunkId;
        private int currentOffset;
        private int bufferedCount;
        private uint currentHash = 2166136261u;
        private WriterWindow window;

        public void WriteByte(byte value)
        {
            Span<byte> one = stackalloc byte[1];
            one[0] = value;
            this.Write(one);
        }

        public void WriteInt32LittleEndian(int value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            this.Write(buffer);
        }

        public void WriteInt64LittleEndian(long value)
        {
            Span<byte> buffer = stackalloc byte[sizeof(long)];
            BinaryPrimitives.WriteInt64LittleEndian(buffer, value);
            this.Write(buffer);
        }

        public void Write(scoped ReadOnlySpan<byte> source)
        {
            var offset = 0;

            while (offset < source.Length)
            {
                this.EnsureChunk();
                var writable = Math.Min(
                    Math.Min(this.chunkSize - this.currentOffset, source.Length - offset),
                    prefixLength - this.bufferedCount);
                var slice = source.Slice(offset, writable);
                slice.CopyTo(this.window[this.bufferedCount..]);
                this.currentHash = ComputeHash(this.currentHash, slice);
                this.currentOffset += writable;
                this.bufferedCount += writable;
                offset += writable;

                if (this.bufferedCount == prefixLength || this.currentOffset == this.chunkSize)
                    this.FlushBuffered();

                if (this.currentOffset == this.chunkSize && offset < source.Length)
                    this.AdvanceChunk();
            }
        }

        public long Complete()
        {
            if (this.currentChunkId == InvalidChunkId)
                throw new InvalidOperationException("Chunk stream payload cannot be empty.");

            this.FinalizeCurrentChunk(InvalidChunkId);
            return this.rootChunkId;
        }

        private void EnsureChunk()
        {
            if (this.currentChunkId != InvalidChunkId)
                return;

            var chunkId = this.plan.Value.AllocateChunkId();
            var requiredLength = this.owner.GetChunkOffset(chunkId) + chunkHeaderSize + this.chunkSize;

            if (requiredLength > this.target.Length)
                this.target.Length = requiredLength;

            this.rootChunkId = chunkId;
            this.currentChunkId = chunkId;
            this.currentOffset = 0;
            this.bufferedCount = 0;
            this.currentHash = 2166136261u;
        }

        private void AdvanceChunk()
        {
            this.FlushBuffered();
            var nextChunkId = this.plan.Value.AllocateChunkId();
            var requiredLength = this.owner.GetChunkOffset(nextChunkId) + chunkHeaderSize + this.chunkSize;

            if (requiredLength > this.target.Length)
                this.target.Length = requiredLength;

            this.FinalizeCurrentChunk(nextChunkId);
            this.currentChunkId = nextChunkId;
            this.currentOffset = 0;
            this.bufferedCount = 0;
            this.currentHash = 2166136261u;
        }

        private void FinalizeCurrentChunk(long nextChunkId)
        {
            this.FlushBuffered();
            var payloadOffset = this.owner.GetChunkOffset(this.currentChunkId) + chunkHeaderSize;
            var requiredLength = payloadOffset + this.chunkSize;

            if (requiredLength > this.target.Length)
                this.target.Length = requiredLength;

            this.owner.WriteChunkHeader(this.target, this.currentChunkId, this.kind, nextChunkId, this.currentOffset, this.currentHash);
        }

        private void FlushBuffered()
        {
            if (this.bufferedCount == 0)
                return;

            var chunkOffset = this.owner.GetChunkOffset(this.currentChunkId) + chunkHeaderSize + this.currentOffset - this.bufferedCount;
            WriteAt(this.owner, this.target, chunkOffset, this.window[..this.bufferedCount]);
            this.bufferedCount = 0;
        }
    }

    [InlineArray(prefixLength)]
    [StructLayout(LayoutKind.Sequential)]
    private struct WriterWindow
    {
        private byte value0;
    }

    private static bool TryMapGetValue(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long rootChunkId,
        ReadOnlySpan<byte> key,
        out byte[] value)
    {
        if (rootChunkId == InvalidChunkId)
        {
            value = [];

            return false;
        }

        var current = rootChunkId;

        while (true)
        {
            ReadMapNodeHeader(owner, source, chunkSize, current, out var kind, out _, out var recordCount, out _);
            var cursor = new ChunkStreamCursor(owner, source, chunkSize, current, chunkMetadata);
            SkipMapNodeHeader(ref cursor);

            if (kind == MapNodeKind.Leaf)
            {
                for (var i = 0; i < recordCount; i++)
                {
                    var keyLength = cursor.ReadInt32LittleEndian();
                    var comparison = CompareSerializedKey(ref cursor, keyLength, key);
                    var valueLength = cursor.ReadInt32LittleEndian();

                    if (comparison == 0)
                    {
                        value = new byte[valueLength];
                        cursor.ReadExactly(value);

                        return true;
                    }

                    cursor.Skip(valueLength);

                    if (comparison > 0)
                    {
                        value = [];

                        return false;
                    }
                }

                value = [];

                return false;
            }

            var selectedChild = InvalidChunkId;

            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();
                var comparison = CompareSerializedKey(ref cursor, keyLength, key);
                var childRootChunkId = cursor.ReadInt64LittleEndian();

                if (i == 0)
                {
                    selectedChild = childRootChunkId;

                    if (comparison > 0)
                        break;

                    continue;
                }

                if (comparison > 0)
                    break;

                selectedChild = childRootChunkId;
            }

            current = selectedChild;
        }
    }

    private static bool TryGetInt64MapValue(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long rootChunkId,
        long key,
        out long value)
    {
        if (rootChunkId == InvalidChunkId)
        {
            value = 0;
            return false;
        }

        var current = rootChunkId;

        while (true)
        {
            ReadMapNodeHeader(owner, source, chunkSize, current, out var kind, out _, out var recordCount, out _);
            var cursor = new ChunkStreamCursor(owner, source, chunkSize, current, chunkMetadata);
            SkipMapNodeHeader(ref cursor);

            if (kind == MapNodeKind.Leaf)
            {
                for (var i = 0; i < recordCount; i++)
                {
                    var keyLength = cursor.ReadInt32LittleEndian();
                    var comparison = CompareSerializedInt64Key(ref cursor, keyLength, key);
                    var valueLength = cursor.ReadInt32LittleEndian();

                    if (comparison == 0)
                    {
                        if (valueLength != sizeof(long))
                            throw new InvalidDataException("Int64 map value length is invalid.");

                        value = cursor.ReadInt64LittleEndian();
                        return true;
                    }

                    cursor.Skip(valueLength);

                    if (comparison > 0)
                    {
                        value = 0;
                        return false;
                    }
                }

                value = 0;
                return false;
            }

            var selectedChild = InvalidChunkId;

            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();
                var comparison = CompareSerializedInt64Key(ref cursor, keyLength, key);
                var childRootChunkId = cursor.ReadInt64LittleEndian();

                if (i == 0)
                {
                    selectedChild = childRootChunkId;

                    if (comparison > 0)
                        break;

                    continue;
                }

                if (comparison > 0)
                    break;

                selectedChild = childRootChunkId;
            }

            current = selectedChild;
        }
    }

    private static bool TryGetInodeMapValue(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long rootChunkId,
        long key,
        out InodeRecord value)
    {
        if (rootChunkId == InvalidChunkId)
        {
            value = default;
            return false;
        }

        var current = rootChunkId;

        while (true)
        {
            ReadMapNodeHeader(owner, source, chunkSize, current, out var kind, out _, out var recordCount, out _);
            var cursor = new ChunkStreamCursor(owner, source, chunkSize, current, chunkMetadata);
            SkipMapNodeHeader(ref cursor);

            if (kind == MapNodeKind.Leaf)
            {
                for (var i = 0; i < recordCount; i++)
                {
                    var keyLength = cursor.ReadInt32LittleEndian();
                    var comparison = CompareSerializedInt64Key(ref cursor, keyLength, key);
                    var valueLength = cursor.ReadInt32LittleEndian();

                    if (comparison == 0)
                    {
                        value = ReadInodeValue(ref cursor, valueLength);
                        return true;
                    }

                    cursor.Skip(valueLength);

                    if (comparison > 0)
                    {
                        value = default;
                        return false;
                    }
                }

                value = default;
                return false;
            }

            var selectedChild = InvalidChunkId;

            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();
                var comparison = CompareSerializedInt64Key(ref cursor, keyLength, key);
                var childRootChunkId = cursor.ReadInt64LittleEndian();

                if (i == 0)
                {
                    selectedChild = childRootChunkId;

                    if (comparison > 0)
                        break;

                    continue;
                }

                if (comparison > 0)
                    break;

                selectedChild = childRootChunkId;
            }

            current = selectedChild;
        }
    }

    private static bool TryGetDirectoryEntryMapValue(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long rootChunkId,
        long parentInodeId,
        ReadOnlySpan<char> name,
        out NamespaceEntry value)
    {
        if (rootChunkId == InvalidChunkId)
        {
            value = default;
            return false;
        }

        var current = rootChunkId;

        while (true)
        {
            ReadMapNodeHeader(owner, source, chunkSize, current, out var kind, out _, out var recordCount, out _);
            var cursor = new ChunkStreamCursor(owner, source, chunkSize, current, chunkMetadata);
            SkipMapNodeHeader(ref cursor);

            if (kind == MapNodeKind.Leaf)
            {
                for (var i = 0; i < recordCount; i++)
                {
                    var keyLength = cursor.ReadInt32LittleEndian();
                    var comparison = CompareSerializedDirectoryEntryKey(ref cursor, keyLength, parentInodeId, name);
                    var valueLength = cursor.ReadInt32LittleEndian();

                    if (comparison == 0)
                    {
                        value = ReadDirectoryEntryValue(ref cursor, valueLength);
                        return true;
                    }

                    cursor.Skip(valueLength);

                    if (comparison > 0)
                    {
                        value = default;
                        return false;
                    }
                }

                value = default;
                return false;
            }

            var selectedChild = InvalidChunkId;

            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();
                var comparison = CompareSerializedDirectoryEntryKey(ref cursor, keyLength, parentInodeId, name);
                var childRootChunkId = cursor.ReadInt64LittleEndian();

                if (i == 0)
                {
                    selectedChild = childRootChunkId;

                    if (comparison > 0)
                        break;

                    continue;
                }

                if (comparison > 0)
                    break;

                selectedChild = childRootChunkId;
            }

            current = selectedChild;
        }
    }

    private static bool TryGetLastMapInt64Key(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long rootChunkId,
        out long key)
    {
        if (rootChunkId == InvalidChunkId)
        {
            key = 0;

            return false;
        }

        var current = rootChunkId;

        while (true)
        {
            ReadMapNodeHeader(owner, source, chunkSize, current, out var kind, out _, out var recordCount, out _);
            var cursor = new ChunkStreamCursor(owner, source, chunkSize, current, chunkMetadata);
            SkipMapNodeHeader(ref cursor);

            if (kind == MapNodeKind.Leaf)
            {
                if (recordCount == 0)
                    throw new InvalidDataException("Leaf node cannot be empty.");

                for (var i = 0; i < recordCount - 1; i++)
                {
                    var skipKeyLength = cursor.ReadInt32LittleEndian();

                    if (skipKeyLength < 0)
                        throw new InvalidDataException("Map key length is invalid.");

                    cursor.Skip(skipKeyLength);
                    var skipValueLength = cursor.ReadInt32LittleEndian();

                    if (skipValueLength < 0)
                        throw new InvalidDataException("Map value length is invalid.");

                    cursor.Skip(skipValueLength);
                }

                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength != sizeof(long))
                    throw new InvalidDataException("Int64 key must occupy exactly 8 bytes.");

                key = cursor.ReadInt64BigEndian();

                return true;
            }

            long childRootChunkId = InvalidChunkId;

            for (var i = 0; i < recordCount; i++)
            {
                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                cursor.Skip(keyLength);
                childRootChunkId = cursor.ReadInt64LittleEndian();
            }

            current = childRootChunkId;
        }
    }

    private static void ReadMapNodeHeader(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long rootChunkId,
        out MapNodeKind kind,
        out int level,
        out int recordCount,
        out int encodedLength)
    {
        var cursor = new ChunkStreamCursor(owner, source, chunkSize, rootChunkId, chunkMetadata);
        kind = cursor.ReadByte() switch
        {
            (byte)MapNodeKind.Leaf => MapNodeKind.Leaf,
            (byte)MapNodeKind.Internal => MapNodeKind.Internal,
            _ => throw new InvalidDataException("Map node kind is invalid."),
        };
        level = cursor.ReadInt32LittleEndian();
        recordCount = cursor.ReadInt32LittleEndian();
        encodedLength = cursor.ReadInt32LittleEndian();

        if (recordCount < 0 || encodedLength < mapNodeHeaderSize)
            throw new InvalidDataException("Map node header is invalid.");
    }

    private static void SkipMapNodeHeader(ref ChunkStreamCursor cursor)
    {
        cursor.Skip(mapNodeHeaderSize);
    }

    private static int CompareSerializedKey(ref ChunkStreamCursor cursor, int keyLength, ReadOnlySpan<byte> key)
    {
        if (keyLength < 0)
            throw new InvalidDataException("Map key length is invalid.");

        var shared = Math.Min(keyLength, key.Length);

        for (var i = 0; i < shared; i++)
        {
            var current = cursor.ReadByte();

            if (current == key[i])
                continue;

            cursor.Skip(keyLength - i - 1);

            return current < key[i] ? -1 : 1;
        }

        if (keyLength > key.Length)
        {
            cursor.Skip(keyLength - key.Length);

            return 1;
        }

        return keyLength < key.Length ? -1 : 0;
    }

    private static int CompareSerializedInt64Key(ref ChunkStreamCursor cursor, int keyLength, long key)
    {
        if (keyLength != sizeof(long))
            throw new InvalidDataException("Int64 key must occupy exactly 8 bytes.");

        var current = cursor.ReadInt64BigEndian();
        return current < key ? -1 : current > key ? 1 : 0;
    }

    private static int CompareSerializedDirectoryEntryKey(ref ChunkStreamCursor cursor, int keyLength, long parentInodeId, ReadOnlySpan<char> name)
    {
        if (keyLength < sizeof(long))
            throw new InvalidDataException("Directory entry key is too short.");

        var currentParentInodeId = cursor.ReadInt64BigEndian();

        if (currentParentInodeId != parentInodeId)
        {
            cursor.Skip(keyLength - sizeof(long));
            return currentParentInodeId < parentInodeId ? -1 : 1;
        }

        var nameByteCount = keyLength - sizeof(long);
        ValidateUtf16BigEndianByteCount(nameByteCount, "Directory entry key length is invalid.");
        var currentLength = nameByteCount / sizeof(char);
        var shared = Math.Min(currentLength, name.Length);
        Span<byte> pair = stackalloc byte[sizeof(char)];

        for (var i = 0; i < shared; i++)
        {
            cursor.ReadExactly(pair);
            var current = (char)BinaryPrimitives.ReadUInt16BigEndian(pair);

            if (current == name[i])
                continue;

            cursor.Skip((currentLength - i - 1) * sizeof(char));
            return current < name[i] ? -1 : 1;
        }

        if (currentLength > name.Length)
        {
            cursor.Skip((currentLength - name.Length) * sizeof(char));
            return 1;
        }

        return currentLength < name.Length ? -1 : 0;
    }

    private long UpsertMap(IStreamRwsl<byte> target, long rootChunkId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ref VfsMutationPlan plan)
    {
        var result = this.UpsertMapCore(target, rootChunkId, key, value, ref plan);

        if (!result.Changed)
            return rootChunkId;

        if (!result.HasSplit)
            return result.RootId;

        return this.WriteInternalSplitRoot(target, result.RootId, result.SplitRightRootId, result.Level + 1, ref plan);
    }

    private bool DeleteMap(IStreamRwsl<byte> target, long rootChunkId, ReadOnlySpan<byte> key, ref VfsMutationPlan plan, out long nextRootChunkId)
    {
        var result = this.DeleteMapCore(target, rootChunkId, key, ref plan);

        if (!result.Changed)
        {
            nextRootChunkId = rootChunkId;

            return false;
        }

        if (!result.Exists)
        {
            nextRootChunkId = InvalidChunkId;

            return true;
        }

        nextRootChunkId = result.RootId;
        
        if (TryGetSingleInternalChildRoot(this, target, this.chunkSize, nextRootChunkId, out var singleChildRoot))
        {
            if (plan.ShouldRetireRewrittenRoots)
                plan.RetireStream(nextRootChunkId);
            nextRootChunkId = singleChildRoot;
        }

        return true;
    }

    private static bool TryGetSingleInternalChildRoot(
        VirtualFileSystem? owner,
        IStreamRwsl<byte> source,
        int chunkSize,
        long rootChunkId,
        out long childRootChunkId)
    {
        ReadMapNodeHeader(owner, source, chunkSize, rootChunkId, out var kind, out _, out var recordCount, out _);

        if (kind != MapNodeKind.Internal || recordCount != 1)
        {
            childRootChunkId = InvalidChunkId;
            return false;
        }

        var cursor = new ChunkStreamCursor(owner, source, chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        var keyLength = cursor.ReadInt32LittleEndian();

        if (keyLength < 0)
            throw new InvalidDataException("Map key length is invalid.");

        cursor.Skip(keyLength);
        childRootChunkId = cursor.ReadInt64LittleEndian();
        return true;
    }

    private MapRewriteResult UpsertMapCore(IStreamRwsl<byte> target, long rootChunkId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ref VfsMutationPlan plan)
    {
        if (rootChunkId == InvalidChunkId)
        {
            return new(true, true, this.WriteSingleLeafNode(target, key, value, ref plan), 0, false, InvalidChunkId);
        }

        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out var kind, out var level, out _, out _);

        if (kind == MapNodeKind.Leaf)
            return this.UpsertLeafNode(target, rootChunkId, level, key, value, ref plan);

        var childIndex = FindInternalChildIndex(this, target, this.chunkSize, rootChunkId, key, out var childRootChunkId);
        var childRewrite = this.UpsertMapCore(target, childRootChunkId, key, value, ref plan);

        if (!childRewrite.Changed)
            return new(false, true, rootChunkId, level, false, InvalidChunkId);

        return this.RewriteInternalNode(target, rootChunkId, level, childIndex, in childRewrite, ref plan);
    }

    private MapRewriteResult DeleteMapCore(IStreamRwsl<byte> target, long rootChunkId, ReadOnlySpan<byte> key, ref VfsMutationPlan plan)
    {
        if (rootChunkId == InvalidChunkId)
            return new(false, false, InvalidChunkId, 0, false, InvalidChunkId);

        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out var kind, out var level, out var recordCount, out _);

        if (kind == MapNodeKind.Leaf)
            return this.DeleteLeafNode(target, rootChunkId, level, recordCount, key, ref plan);

        var childIndex = FindInternalChildIndex(this, target, this.chunkSize, rootChunkId, key, out var childRootChunkId);
        var childRewrite = this.DeleteMapCore(target, childRootChunkId, key, ref plan);

        if (!childRewrite.Changed)
            return new(false, true, rootChunkId, level, false, InvalidChunkId);

        if (!childRewrite.Exists && recordCount == 1)
        {
            if (plan.ShouldRetireRewrittenRoots)
                plan.RetireStream(rootChunkId);

            return new(true, false, InvalidChunkId, level, false, InvalidChunkId);
        }

        return this.RewriteInternalNode(target, rootChunkId, level, childIndex, in childRewrite, ref plan);
    }

    private MapRewriteResult UpsertLeafNode(IStreamRwsl<byte> target, long rootChunkId, int level, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ref VfsMutationPlan plan)
    {
        AnalyzeLeafUpsert(target, rootChunkId, key, value, out var changed, out var outputRecordCount, out var encodedLength, out var splitLeftRecordCount, out var splitLeftEncodedLength);

        if (!changed)
            return new(false, true, rootChunkId, level, false, InvalidChunkId);

        if (splitLeftRecordCount == outputRecordCount)
        {
            var nextRoot = this.WriteLeafUpsertNode(target, rootChunkId, level, key, value, outputRecordCount, encodedLength, 0, 0, ref plan);
            if (plan.ShouldRetireRewrittenRoots)
                plan.RetireStream(rootChunkId);
            return new(true, true, nextRoot, level, false, InvalidChunkId);
        }

        var rightRecordCount = outputRecordCount - splitLeftRecordCount;
        var rightEncodedLength = encodedLength - splitLeftEncodedLength + mapNodeHeaderSize;
        var leftRoot = this.WriteLeafUpsertNode(target, rootChunkId, level, key, value, splitLeftRecordCount, splitLeftEncodedLength, splitLeftRecordCount, 0, ref plan);
        var rightRoot = this.WriteLeafUpsertNode(target, rootChunkId, level, key, value, rightRecordCount, rightEncodedLength, rightRecordCount, splitLeftRecordCount, ref plan);
        if (plan.ShouldRetireRewrittenRoots)
            plan.RetireStream(rootChunkId);
        return new(true, true, leftRoot, level, true, rightRoot);
    }

    private MapRewriteResult DeleteLeafNode(IStreamRwsl<byte> target, long rootChunkId, int level, int recordCount, ReadOnlySpan<byte> key, ref VfsMutationPlan plan)
    {
        AnalyzeLeafDelete(target, rootChunkId, key, out var changed, out var outputRecordCount, out var encodedLength);

        if (!changed)
            return new(false, true, rootChunkId, level, false, InvalidChunkId);

        if (outputRecordCount == 0)
        {
            if (plan.ShouldRetireRewrittenRoots)
                plan.RetireStream(rootChunkId);
            return new(true, false, InvalidChunkId, level, false, InvalidChunkId);
        }

        if (outputRecordCount >= recordCount)
            throw new InvalidDataException("Leaf delete analysis is invalid.");

        var nextRoot = this.WriteLeafDeleteNode(target, rootChunkId, level, key, outputRecordCount, encodedLength, ref plan);
        if (plan.ShouldRetireRewrittenRoots)
            plan.RetireStream(rootChunkId);
        return new(true, true, nextRoot, level, false, InvalidChunkId);
    }

    private MapRewriteResult RewriteInternalNode(IStreamRwsl<byte> target, long rootChunkId, int level, int childIndex, in MapRewriteResult childRewrite, ref VfsMutationPlan plan)
    {
        AnalyzeInternalRewrite(target, rootChunkId, childIndex, in childRewrite, out var outputRecordCount, out var encodedLength, out var splitLeftRecordCount, out var splitLeftEncodedLength);

        if (outputRecordCount == 0)
        {
            if (plan.ShouldRetireRewrittenRoots)
                plan.RetireStream(rootChunkId);
            return new(true, false, InvalidChunkId, level, false, InvalidChunkId);
        }

        if (splitLeftRecordCount == outputRecordCount)
        {
            var nextRoot = this.WriteInternalRewriteNode(target, rootChunkId, level, childIndex, in childRewrite, outputRecordCount, encodedLength, 0, 0, ref plan);
            if (plan.ShouldRetireRewrittenRoots)
                plan.RetireStream(rootChunkId);
            return new(true, true, nextRoot, level, false, InvalidChunkId);
        }

        var rightRecordCount = outputRecordCount - splitLeftRecordCount;
        var rightEncodedLength = encodedLength - splitLeftEncodedLength + mapNodeHeaderSize;
        var leftRoot = this.WriteInternalRewriteNode(target, rootChunkId, level, childIndex, in childRewrite, splitLeftRecordCount, splitLeftEncodedLength, splitLeftRecordCount, 0, ref plan);
        var rightRoot = this.WriteInternalRewriteNode(target, rootChunkId, level, childIndex, in childRewrite, rightRecordCount, rightEncodedLength, rightRecordCount, splitLeftRecordCount, ref plan);
        if (plan.ShouldRetireRewrittenRoots)
            plan.RetireStream(rootChunkId);
        return new(true, true, leftRoot, level, true, rightRoot);
    }

    private long WriteInternalSplitRoot(IStreamRwsl<byte> target, long leftRootChunkId, long rightRootChunkId, int level, ref VfsMutationPlan plan)
    {
        var leftRecordLength = GetInternalRecordLengthForChildRoot(this, target, this.chunkSize, leftRootChunkId);
        var rightRecordLength = GetInternalRecordLengthForChildRoot(this, target, this.chunkSize, rightRootChunkId);
        var encodedLength = checked(mapNodeHeaderSize + leftRecordLength + rightRecordLength);
        ChunkStreamWriter writer = new(this, target, chunkMetadata, ref plan);
        WriteMapNodeHeader(ref writer, MapNodeKind.Internal, level, 2, encodedLength);
        WriteInternalRecordForChildRoot(ref writer, this, target, this.chunkSize, leftRootChunkId);
        WriteInternalRecordForChildRoot(ref writer, this, target, this.chunkSize, rightRootChunkId);
        return writer.Complete();
    }

    private long WriteSingleLeafNode(IStreamRwsl<byte> target, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, ref VfsMutationPlan plan)
    {
        var encodedLength = checked(mapNodeHeaderSize + MeasureLeafRecord(key.Length, value.Length));
        ChunkStreamWriter writer = new(this, target, chunkMetadata, ref plan);
        WriteMapNodeHeader(ref writer, MapNodeKind.Leaf, 0, 1, encodedLength);
        WriteLeafRecord(ref writer, key, value);
        return writer.Complete();
    }

    private long WriteLeafUpsertNode(
        IStreamRwsl<byte> target,
        long sourceRootChunkId,
        int level,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> value,
        int outputRecordCount,
        int encodedLength,
        int takeCount,
        int skipCount,
        ref VfsMutationPlan plan)
    {
        ChunkStreamWriter writer = new(this, target, chunkMetadata, ref plan);
        WriteMapNodeHeader(ref writer, MapNodeKind.Leaf, level, outputRecordCount, encodedLength);
        var compareCursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRootChunkId, chunkMetadata);
        var copyCursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref compareCursor);
        SkipMapNodeHeader(ref copyCursor);
        var outputIndex = 0;
        var inserted = false;
        Span<byte> copyBuffer = stackalloc byte[sizeof(long)];
        ReadMapNodeHeader(this, target, this.chunkSize, sourceRootChunkId, out _, out _, out var recordCount, out _);

        for (var i = 0; i < recordCount; i++)
        {
            var compareKeyLength = compareCursor.ReadInt32LittleEndian();
            var copyKeyLength = copyCursor.ReadInt32LittleEndian();
            var comparison = CompareSerializedKey(ref compareCursor, compareKeyLength, key);

            if (!inserted && comparison > 0)
            {
                WriteLeafRecordAtIndex(ref writer, outputIndex++, takeCount, skipCount, key, value);
                inserted = true;
            }

            var compareValueLength = compareCursor.ReadInt32LittleEndian();

            if (comparison == 0)
            {
                compareCursor.Skip(compareValueLength);
                copyCursor.Skip(copyKeyLength);
                var copyValueLength = copyCursor.ReadInt32LittleEndian();
                copyCursor.Skip(copyValueLength);
                WriteLeafRecordAtIndex(ref writer, outputIndex++, takeCount, skipCount, key, value);
                inserted = true;
                continue;
            }

            compareCursor.Skip(compareValueLength);

            if (outputIndex < skipCount || (takeCount != 0 && outputIndex >= skipCount + takeCount))
            {
                copyCursor.Skip(copyKeyLength);
                var skippedValueLength = copyCursor.ReadInt32LittleEndian();

                if (skippedValueLength < 0)
                    throw new InvalidDataException("Map value length is invalid.");

                copyCursor.Skip(skippedValueLength);
                outputIndex++;
                continue;
            }

            writer.WriteInt32LittleEndian(copyKeyLength);
            var remainingKeyBytes = copyKeyLength;

            while (remainingKeyBytes != 0)
            {
                var currentLength = Math.Min(copyBuffer.Length, remainingKeyBytes);
                copyCursor.ReadExactly(copyBuffer[..currentLength]);
                writer.Write(copyBuffer[..currentLength]);
                remainingKeyBytes -= currentLength;
            }

            var copiedValueLength = copyCursor.ReadInt32LittleEndian();

            if (copiedValueLength < 0)
                throw new InvalidDataException("Map value length is invalid.");

            writer.WriteInt32LittleEndian(copiedValueLength);
            var remainingValueBytes = copiedValueLength;

            while (remainingValueBytes != 0)
            {
                var currentLength = Math.Min(copyBuffer.Length, remainingValueBytes);
                copyCursor.ReadExactly(copyBuffer[..currentLength]);
                writer.Write(copyBuffer[..currentLength]);
                remainingValueBytes -= currentLength;
            }

            outputIndex++;
        }

        if (!inserted)
            WriteLeafRecordAtIndex(ref writer, outputIndex, takeCount, skipCount, key, value);

        return writer.Complete();
    }

    private long WriteLeafDeleteNode(IStreamRwsl<byte> target, long sourceRootChunkId, int level, ReadOnlySpan<byte> key, int outputRecordCount, int encodedLength, ref VfsMutationPlan plan)
    {
        ChunkStreamWriter writer = new(this, target, chunkMetadata, ref plan);
        WriteMapNodeHeader(ref writer, MapNodeKind.Leaf, level, outputRecordCount, encodedLength);
        var compareCursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRootChunkId, chunkMetadata);
        var copyCursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref compareCursor);
        SkipMapNodeHeader(ref copyCursor);
        Span<byte> copyBuffer = stackalloc byte[sizeof(long)];
        ReadMapNodeHeader(this, target, this.chunkSize, sourceRootChunkId, out _, out _, out var recordCount, out _);

        for (var i = 0; i < recordCount; i++)
        {
            var compareKeyLength = compareCursor.ReadInt32LittleEndian();
            var copyKeyLength = copyCursor.ReadInt32LittleEndian();
            var comparison = CompareSerializedKey(ref compareCursor, compareKeyLength, key);
            var compareValueLength = compareCursor.ReadInt32LittleEndian();
            compareCursor.Skip(compareValueLength);

            if (comparison == 0)
            {
                copyCursor.Skip(copyKeyLength);
                var copyValueLength = copyCursor.ReadInt32LittleEndian();
                copyCursor.Skip(copyValueLength);
                continue;
            }

            writer.WriteInt32LittleEndian(copyKeyLength);
            var remainingKeyBytes = copyKeyLength;

            while (remainingKeyBytes != 0)
            {
                var currentLength = Math.Min(copyBuffer.Length, remainingKeyBytes);
                copyCursor.ReadExactly(copyBuffer[..currentLength]);
                writer.Write(copyBuffer[..currentLength]);
                remainingKeyBytes -= currentLength;
            }

            var copiedValueLength = copyCursor.ReadInt32LittleEndian();

            if (copiedValueLength < 0)
                throw new InvalidDataException("Map value length is invalid.");

            writer.WriteInt32LittleEndian(copiedValueLength);
            var remainingValueBytes = copiedValueLength;

            while (remainingValueBytes != 0)
            {
                var currentLength = Math.Min(copyBuffer.Length, remainingValueBytes);
                copyCursor.ReadExactly(copyBuffer[..currentLength]);
                writer.Write(copyBuffer[..currentLength]);
                remainingValueBytes -= currentLength;
            }
        }

        return writer.Complete();
    }

    private long WriteInternalRewriteNode(
        IStreamRwsl<byte> target,
        long sourceRootChunkId,
        int level,
        int childIndex,
        in MapRewriteResult childRewrite,
        int outputRecordCount,
        int encodedLength,
        int takeCount,
        int skipCount,
        ref VfsMutationPlan plan)
    {
        ChunkStreamWriter writer = new(this, target, chunkMetadata, ref plan);
        WriteMapNodeHeader(ref writer, MapNodeKind.Internal, level, outputRecordCount, encodedLength);
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, sourceRootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        ReadMapNodeHeader(this, target, this.chunkSize, sourceRootChunkId, out _, out _, out var recordCount, out _);
        var outputIndex = 0;
        Span<byte> copyBuffer = stackalloc byte[sizeof(long)];

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();

            if (i != childIndex)
            {
                if (outputIndex < skipCount || (takeCount != 0 && outputIndex >= skipCount + takeCount))
                {
                    cursor.Skip(keyLength);
                    _ = cursor.ReadInt64LittleEndian();
                    outputIndex++;
                    continue;
                }

                writer.WriteInt32LittleEndian(keyLength);
                var remainingKeyBytes = keyLength;

                while (remainingKeyBytes != 0)
                {
                    var currentLength = Math.Min(copyBuffer.Length, remainingKeyBytes);
                    cursor.ReadExactly(copyBuffer[..currentLength]);
                    writer.Write(copyBuffer[..currentLength]);
                    remainingKeyBytes -= currentLength;
                }

                writer.WriteInt64LittleEndian(cursor.ReadInt64LittleEndian());
                outputIndex++;
                continue;
            }

            cursor.Skip(keyLength);
            _ = cursor.ReadInt64LittleEndian();

            if (childRewrite.Exists)
                WriteInternalRecordForChildRootAtIndex(ref writer, outputIndex++, takeCount, skipCount, target, childRewrite.RootId);

            if (childRewrite.HasSplit)
                WriteInternalRecordForChildRootAtIndex(ref writer, outputIndex++, takeCount, skipCount, target, childRewrite.SplitRightRootId);
        }

        return writer.Complete();
    }

    private void AnalyzeLeafUpsert(IStreamRwsl<byte> target, long rootChunkId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, out bool changed, out int outputRecordCount, out int encodedLength, out int splitLeftRecordCount, out int splitLeftEncodedLength)
    {
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out _, out _, out var recordCount, out _);
        var inserted = false;
        var found = false;
        var unchanged = false;
        var totalMeasure = 0;
        outputRecordCount = 0;
        var insertedMeasure = MeasureLeafRecord(key.Length, value.Length);

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();
            var comparison = CompareSerializedKey(ref cursor, keyLength, key);
            var valueLength = cursor.ReadInt32LittleEndian();

            if (!inserted && comparison > 0)
            {
                totalMeasure = checked(totalMeasure + insertedMeasure);
                outputRecordCount++;
                inserted = true;
            }

            if (comparison == 0)
            {
                unchanged = CompareSerializedValue(ref cursor, valueLength, value);
                totalMeasure = checked(totalMeasure + insertedMeasure);
                outputRecordCount++;
                inserted = true;
                found = true;
                continue;
            }

            cursor.Skip(valueLength);
            totalMeasure = checked(totalMeasure + MeasureLeafRecord(keyLength, valueLength));
            outputRecordCount++;
        }

        if (!inserted)
        {
            totalMeasure = checked(totalMeasure + insertedMeasure);
            outputRecordCount++;
        }

        changed = !found || !unchanged;
        encodedLength = checked(mapNodeHeaderSize + totalMeasure);

        if (!changed || encodedLength <= this.chunkSize || outputRecordCount <= 1)
        {
            splitLeftRecordCount = outputRecordCount;
            splitLeftEncodedLength = encodedLength;
            return;
        }

        DetermineLeafUpsertSplit(target, rootChunkId, key, value, outputRecordCount, totalMeasure, out splitLeftRecordCount, out splitLeftEncodedLength);
    }

    private void AnalyzeLeafDelete(IStreamRwsl<byte> target, long rootChunkId, ReadOnlySpan<byte> key, out bool changed, out int outputRecordCount, out int encodedLength)
    {
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out _, out _, out var recordCount, out _);
        var totalMeasure = 0;
        outputRecordCount = 0;
        changed = false;

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();
            var comparison = CompareSerializedKey(ref cursor, keyLength, key);
            var valueLength = cursor.ReadInt32LittleEndian();
            cursor.Skip(valueLength);

            if (comparison == 0)
            {
                changed = true;
                continue;
            }

            totalMeasure = checked(totalMeasure + MeasureLeafRecord(keyLength, valueLength));
            outputRecordCount++;
        }

        encodedLength = checked(mapNodeHeaderSize + totalMeasure);
    }

    private void AnalyzeInternalRewrite(IStreamRwsl<byte> target, long rootChunkId, int childIndex, in MapRewriteResult childRewrite, out int outputRecordCount, out int encodedLength, out int splitLeftRecordCount, out int splitLeftEncodedLength)
    {
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out _, out _, out var recordCount, out _);
        var totalMeasure = 0;
        outputRecordCount = 0;

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();
            cursor.Skip(keyLength);
            _ = cursor.ReadInt64LittleEndian();

            if (i != childIndex)
            {
                totalMeasure = checked(totalMeasure + MeasureInternalRecord(keyLength));
                outputRecordCount++;
                continue;
            }

            if (childRewrite.Exists)
            {
                totalMeasure = checked(totalMeasure + GetInternalRecordLengthForChildRoot(this, target, this.chunkSize, childRewrite.RootId));
                outputRecordCount++;
            }

            if (childRewrite.HasSplit)
            {
                totalMeasure = checked(totalMeasure + GetInternalRecordLengthForChildRoot(this, target, this.chunkSize, childRewrite.SplitRightRootId));
                outputRecordCount++;
            }
        }

        encodedLength = checked(mapNodeHeaderSize + totalMeasure);

        if (outputRecordCount == 0 || encodedLength <= this.chunkSize || outputRecordCount <= 1)
        {
            splitLeftRecordCount = outputRecordCount;
            splitLeftEncodedLength = encodedLength;
            return;
        }

        DetermineInternalRewriteSplit(target, rootChunkId, childIndex, in childRewrite, outputRecordCount, totalMeasure, out splitLeftRecordCount, out splitLeftEncodedLength);
    }

    private void DetermineLeafUpsertSplit(IStreamRwsl<byte> target, long rootChunkId, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value, int outputRecordCount, int totalMeasure, out int splitLeftRecordCount, out int splitLeftEncodedLength)
    {
        var threshold = totalMeasure / 2;
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out _, out _, out var recordCount, out _);
        var inserted = false;
        var currentMeasure = 0;
        var currentCount = 0;
        var insertedMeasure = MeasureLeafRecord(key.Length, value.Length);

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();
            var comparison = CompareSerializedKey(ref cursor, keyLength, key);
            var valueLength = cursor.ReadInt32LittleEndian();

            if (!inserted && comparison > 0)
            {
                currentMeasure += insertedMeasure;
                currentCount++;

                if (currentCount < outputRecordCount && currentMeasure >= threshold)
                    break;

                inserted = true;
            }

            if (comparison == 0)
            {
                cursor.Skip(valueLength);
                currentMeasure += insertedMeasure;
                currentCount++;

                if (currentCount < outputRecordCount && currentMeasure >= threshold)
                    break;

                inserted = true;
                continue;
            }

            cursor.Skip(valueLength);
            currentMeasure += MeasureLeafRecord(keyLength, valueLength);
            currentCount++;

            if (currentCount < outputRecordCount && currentMeasure >= threshold)
                break;
        }

        if (!inserted && currentCount < outputRecordCount)
        {
            currentMeasure += insertedMeasure;
            currentCount++;
        }

        splitLeftRecordCount = currentCount;
        splitLeftEncodedLength = checked(mapNodeHeaderSize + currentMeasure);
    }

    private void DetermineInternalRewriteSplit(IStreamRwsl<byte> target, long rootChunkId, int childIndex, in MapRewriteResult childRewrite, int outputRecordCount, int totalMeasure, out int splitLeftRecordCount, out int splitLeftEncodedLength)
    {
        var threshold = totalMeasure / 2;
        var cursor = new ChunkStreamCursor(this, target, this.chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        ReadMapNodeHeader(this, target, this.chunkSize, rootChunkId, out _, out _, out var recordCount, out _);
        var currentMeasure = 0;
        var currentCount = 0;

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();
            cursor.Skip(keyLength);
            _ = cursor.ReadInt64LittleEndian();

            if (i != childIndex)
            {
                currentMeasure += MeasureInternalRecord(keyLength);
                currentCount++;

                if (currentCount < outputRecordCount && currentMeasure >= threshold)
                    break;

                continue;
            }

            if (childRewrite.Exists)
            {
                currentMeasure += GetInternalRecordLengthForChildRoot(this, target, this.chunkSize, childRewrite.RootId);
                currentCount++;

                if (currentCount < outputRecordCount && currentMeasure >= threshold)
                    break;
            }

            if (childRewrite.HasSplit)
            {
                currentMeasure += GetInternalRecordLengthForChildRoot(this, target, this.chunkSize, childRewrite.SplitRightRootId);
                currentCount++;

                if (currentCount < outputRecordCount && currentMeasure >= threshold)
                    break;
            }
        }

        splitLeftRecordCount = currentCount;
        splitLeftEncodedLength = checked(mapNodeHeaderSize + currentMeasure);
    }

    private static int FindInternalChildIndex(VirtualFileSystem? owner, IStreamRwsl<byte> source, int chunkSize, long rootChunkId, ReadOnlySpan<byte> key, out long childRootChunkId)
    {
        ReadMapNodeHeader(owner, source, chunkSize, rootChunkId, out _, out _, out var recordCount, out _);
        var cursor = new ChunkStreamCursor(owner, source, chunkSize, rootChunkId, chunkMetadata);
        SkipMapNodeHeader(ref cursor);
        var selectedIndex = 0;
        childRootChunkId = InvalidChunkId;

        for (var i = 0; i < recordCount; i++)
        {
            var keyLength = cursor.ReadInt32LittleEndian();
            var comparison = CompareSerializedKey(ref cursor, keyLength, key);
            var currentChildRoot = cursor.ReadInt64LittleEndian();

            if (i == 0)
            {
                selectedIndex = 0;
                childRootChunkId = currentChildRoot;

                if (comparison > 0)
                    break;

                continue;
            }

            if (comparison > 0)
                break;

            selectedIndex = i;
            childRootChunkId = currentChildRoot;
        }

        return selectedIndex;
    }

    private static int GetInternalRecordLengthForChildRoot(VirtualFileSystem? owner, IStreamRwsl<byte> source, int chunkSize, long childRootChunkId)
        => checked(sizeof(int) + GetFirstMapKeyLength(owner, source, chunkSize, childRootChunkId) + sizeof(long));

    private static int GetFirstMapKeyLength(VirtualFileSystem? owner, IStreamRwsl<byte> source, int chunkSize, long rootChunkId)
    {
        if (rootChunkId == InvalidChunkId)
            throw new InvalidOperationException("Cannot read a key from an empty map.");

        var current = rootChunkId;

        while (true)
        {
            ReadMapNodeHeader(owner, source, chunkSize, current, out var kind, out _, out var recordCount, out _);
            var cursor = new ChunkStreamCursor(owner, source, chunkSize, current, chunkMetadata);
            SkipMapNodeHeader(ref cursor);

            if (kind == MapNodeKind.Leaf)
            {
                if (recordCount == 0)
                    throw new InvalidDataException("Leaf node cannot be empty.");

                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                return keyLength;
            }

            var childKeyLength = cursor.ReadInt32LittleEndian();

            if (childKeyLength < 0)
                throw new InvalidDataException("Map key length is invalid.");

            cursor.Skip(childKeyLength);
            current = cursor.ReadInt64LittleEndian();
        }
    }

    private static void WriteFirstMapKey(ref ChunkStreamWriter writer, VirtualFileSystem? owner, IStreamRwsl<byte> source, int chunkSize, long rootChunkId)
    {
        var current = rootChunkId;

        while (true)
        {
            ReadMapNodeHeader(owner, source, chunkSize, current, out var kind, out _, out var recordCount, out _);
            var cursor = new ChunkStreamCursor(owner, source, chunkSize, current, chunkMetadata);
            SkipMapNodeHeader(ref cursor);

            if (kind == MapNodeKind.Leaf)
            {
                if (recordCount == 0)
                    throw new InvalidDataException("Leaf node cannot be empty.");

                var keyLength = cursor.ReadInt32LittleEndian();

                if (keyLength < 0)
                    throw new InvalidDataException("Map key length is invalid.");

                writer.WriteInt32LittleEndian(keyLength);
                CopyCursorBytes(ref cursor, ref writer, keyLength);
                return;
            }

            var separatorKeyLength = cursor.ReadInt32LittleEndian();

            if (separatorKeyLength < 0)
                throw new InvalidDataException("Map key length is invalid.");

            cursor.Skip(separatorKeyLength);
            current = cursor.ReadInt64LittleEndian();
        }
    }

    private static void WriteMapNodeHeader(ref ChunkStreamWriter writer, MapNodeKind kind, int level, int recordCount, int encodedLength)
    {
        writer.WriteByte((byte)kind);
        writer.WriteInt32LittleEndian(level);
        writer.WriteInt32LittleEndian(recordCount);
        writer.WriteInt32LittleEndian(encodedLength);
    }

    private static int MeasureLeafRecord(int keyLength, int valueLength)
        => checked(sizeof(int) + keyLength + sizeof(int) + valueLength);

    private static int MeasureInternalRecord(int keyLength)
        => checked(sizeof(int) + keyLength + sizeof(long));

    private static bool CompareSerializedValue(scoped ref ChunkStreamCursor cursor, int valueLength, ReadOnlySpan<byte> value)
    {
        if (valueLength != value.Length)
        {
            cursor.Skip(valueLength);
            return false;
        }

        Span<byte> buffer = stackalloc byte[sizeof(long)];
        var offset = 0;

        while (offset < value.Length)
        {
            var currentLength = Math.Min(buffer.Length, value.Length - offset);
            cursor.ReadExactly(buffer[..currentLength]);

            if (!buffer[..currentLength].SequenceEqual(value.Slice(offset, currentLength)))
            {
                cursor.Skip(value.Length - offset - currentLength);
                return false;
            }

            offset += currentLength;
        }

        return true;
    }

    private static void CopyCursorBytes(scoped ref ChunkStreamCursor cursor, scoped ref ChunkStreamWriter writer, int length)
    {
        Span<byte> buffer = stackalloc byte[sizeof(long)];
        var remaining = length;

        while (remaining != 0)
        {
            var currentLength = Math.Min(buffer.Length, remaining);
            cursor.ReadExactly(buffer[..currentLength]);
            writer.Write(buffer[..currentLength]);
            remaining -= currentLength;
        }
    }

    private static void WriteLeafRecord(scoped ref ChunkStreamWriter writer, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        writer.WriteInt32LittleEndian(key.Length);
        writer.Write(key);
        writer.WriteInt32LittleEndian(value.Length);
        writer.Write(value);
    }

    private static void WriteLeafRecordAtIndex(scoped ref ChunkStreamWriter writer, int outputIndex, int takeCount, int skipCount, ReadOnlySpan<byte> key, ReadOnlySpan<byte> value)
    {
        if (outputIndex < skipCount || (takeCount != 0 && outputIndex >= skipCount + takeCount))
            return;

        WriteLeafRecord(ref writer, key, value);
    }

    private static void WriteLeafRecordFromSource(scoped ref ChunkStreamWriter writer, scoped ref ChunkStreamCursor cursor, int keyLength)
    {
        writer.WriteInt32LittleEndian(keyLength);
        CopyCursorBytes(ref cursor, ref writer, keyLength);
        var valueLength = cursor.ReadInt32LittleEndian();

        if (valueLength < 0)
            throw new InvalidDataException("Map value length is invalid.");

        writer.WriteInt32LittleEndian(valueLength);
        CopyCursorBytes(ref cursor, ref writer, valueLength);
    }

    private static void WriteLeafRecordFromSourceAtIndex(scoped ref ChunkStreamWriter writer, scoped ref ChunkStreamCursor cursor, int keyLength, int outputIndex, int takeCount, int skipCount)
    {
        if (outputIndex < skipCount || (takeCount != 0 && outputIndex >= skipCount + takeCount))
        {
            cursor.Skip(keyLength);
            var skippedValueLength = cursor.ReadInt32LittleEndian();

            if (skippedValueLength < 0)
                throw new InvalidDataException("Map value length is invalid.");

            cursor.Skip(skippedValueLength);
            return;
        }

        WriteLeafRecordFromSource(ref writer, ref cursor, keyLength);
    }

    private static void WriteInternalRecordForChildRoot(scoped ref ChunkStreamWriter writer, VirtualFileSystem? owner, IStreamRwsl<byte> source, int chunkSize, long childRootChunkId)
    {
        WriteFirstMapKey(ref writer, owner, source, chunkSize, childRootChunkId);
        writer.WriteInt64LittleEndian(childRootChunkId);
    }

    private void WriteInternalRecordForChildRootAtIndex(scoped ref ChunkStreamWriter writer, int outputIndex, int takeCount, int skipCount, IStreamRwsl<byte> source, long childRootChunkId)
    {
        if (outputIndex < skipCount || (takeCount != 0 && outputIndex >= skipCount + takeCount))
            return;

        WriteInternalRecordForChildRoot(ref writer, this, source, this.chunkSize, childRootChunkId);
    }

    private static void WriteInternalRecordFromSource(scoped ref ChunkStreamWriter writer, scoped ref ChunkStreamCursor cursor, int keyLength)
    {
        writer.WriteInt32LittleEndian(keyLength);
        CopyCursorBytes(ref cursor, ref writer, keyLength);
        writer.WriteInt64LittleEndian(cursor.ReadInt64LittleEndian());
    }

    private static void WriteInternalRecordFromSourceAtIndex(scoped ref ChunkStreamWriter writer, scoped ref ChunkStreamCursor cursor, int keyLength, int outputIndex, int takeCount, int skipCount)
    {
        if (outputIndex < skipCount || (takeCount != 0 && outputIndex >= skipCount + takeCount))
        {
            cursor.Skip(keyLength);
            _ = cursor.ReadInt64LittleEndian();
            return;
        }

        WriteInternalRecordFromSource(ref writer, ref cursor, keyLength);
    }

    private static uint ComputeHash(uint seed, ReadOnlySpan<byte> source)
    {
        var hash = seed;

        foreach (var value in source)
            hash = (hash ^ value) * 16777619u;

        return hash;
    }
}
