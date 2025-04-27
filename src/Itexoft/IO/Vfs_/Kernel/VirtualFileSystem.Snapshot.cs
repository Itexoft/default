// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private const int mapNodeHeaderSize = 1 + sizeof(int) + sizeof(int) + sizeof(int);

    private static SnapshotState CreateInitialSnapshotState(IStreamRwsl<byte> stream, int chunkSize)
    {
        const long rootDirectoryInodeId = 0;
        const int inodeValueSize = sizeof(long) + sizeof(long) + sizeof(int);
        const int initialMapPayloadSize = mapNodeHeaderSize + sizeof(int) + sizeof(long) + sizeof(int) + inodeValueSize;

        Span<byte> inodeMapPayload = stackalloc byte[initialMapPayloadSize];
        inodeMapPayload[0] = (byte)MapNodeKind.Leaf;
        BinaryPrimitives.WriteInt32LittleEndian(inodeMapPayload[1..5], 0);
        BinaryPrimitives.WriteInt32LittleEndian(inodeMapPayload[5..9], 1);
        BinaryPrimitives.WriteInt32LittleEndian(inodeMapPayload[9..13], inodeMapPayload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(inodeMapPayload[13..17], sizeof(long));
        BinaryPrimitives.WriteInt64BigEndian(inodeMapPayload[17..25], rootDirectoryInodeId);
        BinaryPrimitives.WriteInt32LittleEndian(inodeMapPayload[25..29], inodeValueSize);
        BinaryPrimitives.WriteInt64LittleEndian(inodeMapPayload[29..37], 0);
        BinaryPrimitives.WriteInt64LittleEndian(inodeMapPayload[37..45], InvalidChunkId);
        BinaryPrimitives.WriteInt32LittleEndian(inodeMapPayload[45..49], (int)FileAttributes.Directory);

        var nextChunkId = 0L;
        var inodeRoot = WriteChunkStreamPayloadStatic(stream, chunkSize, chunkMetadata, inodeMapPayload, ref nextChunkId);
        return new(
            InvalidChunkId,
            inodeRoot,
            InvalidChunkId,
            InvalidChunkId,
            InvalidChunkId,
            rootDirectoryInodeId,
            rootDirectoryInodeId,
            nextChunkId,
            nextChunkId);
    }
}
