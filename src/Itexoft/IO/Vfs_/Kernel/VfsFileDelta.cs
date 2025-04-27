// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Vfs;

internal enum FileDeltaChunkKind : byte
{
    Draft = 1,
    Buffer = 2,
}

internal sealed class FileDeltaMutation(
    long baseLength,
    long baseContentRoot,
    int baseAttributes,
    long newLength,
    FileDeltaChunkMutation[] chunks)
{
    public long BaseLength { get; } = baseLength;

    public long BaseContentRoot { get; } = baseContentRoot;

    public int BaseAttributes { get; } = baseAttributes;

    public long NewLength { get; } = newLength;

    public FileDeltaChunkMutation[] Chunks { get; } = chunks;
}

internal readonly struct FileDeltaChunkMutation(
    long logicalChunk,
    FileDeltaChunkKind kind,
    long draftChunkId,
    byte[]? buffer)
{
    public long LogicalChunk { get; } = logicalChunk;

    public FileDeltaChunkKind Kind { get; } = kind;

    public long DraftChunkId { get; } = draftChunkId;

    public byte[]? Buffer { get; } = buffer;
}
