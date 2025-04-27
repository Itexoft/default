// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.ExceptionServices;
using System.Runtime.CompilerServices;
using Itexoft.Threading.Core.Lane;

namespace Itexoft.IO.Vfs;

internal enum VfsMutationKind : byte
{
    None,
    CreateDirectory,
    DeleteFile,
    DeleteDirectory,
    CreateFile,
    ResetFile,
    SetAttribute,
    RemoveAttribute,
    WriteFile,
    ReplaceFile,
    SetFileLength,
    CommitFileDelta,
}

internal unsafe struct VfsMutationSlot
{
    public VfsMutationKind Kind;
    public string? Path;
    public string? Name;
    public long InodeId;
    public long Position;
    public long Length;
    public byte* BufferPointer;
    public int BufferLength;
    public FileDeltaMutation? FileDelta;
    public bool Recursive;
    public bool MustNotExist;
    public bool ResultBool;
    public ExceptionDispatchInfo? Error;

    public void Reset()
    {
        this.Kind = VfsMutationKind.None;
        this.Path = null;
        this.Name = null;
        this.InodeId = VirtualFileSystem.InvalidChunkId;
        this.Position = 0;
        this.Length = 0;
        this.BufferPointer = null;
        this.BufferLength = 0;
        this.FileDelta = null;
        this.Recursive = false;
        this.MustNotExist = false;
        this.ResultBool = false;
        this.Error = null;
    }
}

[InlineArray(64)]
internal struct VfsMutationSlotArray
{
    private VfsMutationSlot element0;
}

internal struct VfsMutationOp : ILaneSlotOp64<VirtualFileSystem, byte>
{
    public static void Invoke(ref VirtualFileSystem state, int laneIndex, out byte result)
    {
        state.ExecuteMutation(laneIndex, out result);
    }
}
