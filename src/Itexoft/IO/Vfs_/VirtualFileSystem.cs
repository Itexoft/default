// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.IO.FileSystem;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;
using Itexoft.Threading.Core.Lane;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem : IFileSystem, IDisposable
{
    private const byte chunkData = 1;
    private const byte chunkMetadata = 2;
    private const byte chunkChunkLog = 3;

    private const int prefixSlotCount = 2;
    private const int prefixSlotSize = 92;
    private const int prefixLength = prefixSlotCount * prefixSlotSize;
    private const int prefixVersion = 4;

    private const int chunkHeaderSize = 24;
    private const int prefixChecksumOffset = 88;
    internal const long InvalidChunkId = -1;
    private readonly IStreamRwsl<byte> primary;
    private readonly IStreamRwsl<byte>? mirror;
    private readonly int bufferSize;
    private readonly int chunkSize;

    private PositionalByteStreamSync primaryIoGate;
    private PositionalByteStreamSync mirrorIoGate;
    private readonly LaneIdPool64 lanePool = new();
    private readonly InlineLaneSlotCombiner64<VirtualFileSystem, byte, VfsMutationOp> mutationCombiner;
    private PrefixState committedPrefix;
    private long publishedChunkCapacity;
    private long nextDraftSessionId = 1;
    private long nextDraftChunkId;
    private EpochPublished64<long> publishedHead;
    private VfsMutationSlotArray mutationSlots;
    private readonly Dictionary<long, long> dirtyWriterByInode = [];
    private AtomicLock apiSync;
    private AtomicLock draftSync;
    private Disposed disposed;

    public VirtualFileSystem(IStreamRwsl<byte> image, in VirtualFileSystemOptions options = default)
    {
        if (image is FileHandle)
            throw new NotSupportedException("Boundary file handles cannot be mounted as VFS carriers. Use a carrier view instead.");

        var primaryCarrier = new CarrierStream(image ?? throw new ArgumentNullException(nameof(image)));
        this.primary = primaryCarrier;
        this.mirror = null;
        this.chunkSize = InitializeSingle(this.primary, in options, out var prefix);
        this.bufferSize = ValidateBufferSize(options.BufferSize, this.chunkSize);
        this.mutationCombiner = new(this);
        this.committedPrefix = prefix;
        this.publishedChunkCapacity = prefix.Snapshot.PublishedChunkCapacity;
        primaryCarrier.SetPublishedLength(this.GetPublishedLength(prefix.Snapshot.PublishedChunkCount));
        this.publishedHead.Publish(prefix.Generation);
    }

    public VirtualFileSystem(IStreamRwsl<byte> primary, IStreamRwsl<byte> mirror, in VirtualFileSystemOptions options = default)
    {
        if (primary is FileHandle || mirror is FileHandle)
            throw new NotSupportedException("Boundary file handles cannot be mounted as VFS carriers. Use a carrier view instead.");

        var primaryCarrier = new CarrierStream(primary ?? throw new ArgumentNullException(nameof(primary)));
        var mirrorCarrier = new CarrierStream(mirror ?? throw new ArgumentNullException(nameof(mirror)));
        this.primary = primaryCarrier;
        this.mirror = mirrorCarrier;
        this.chunkSize = InitializeMirrored(this.primary, this.mirror, in options, out var prefix);
        this.bufferSize = ValidateBufferSize(options.BufferSize, this.chunkSize);
        this.mutationCombiner = new(this);
        this.committedPrefix = prefix;
        this.publishedChunkCapacity = prefix.Snapshot.PublishedChunkCapacity;
        var publishedLength = this.GetPublishedLength(prefix.Snapshot.PublishedChunkCount);
        primaryCarrier.SetPublishedLength(publishedLength);
        mirrorCarrier.SetPublishedLength(publishedLength);
        this.publishedHead.Publish(prefix.Generation);
    }

    public IStreamRwsl<byte> Open(string path, SysFileMode mode)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        var normalized = NormalizePath(path);

        if (TryGetNodeKind(normalized, out var kind))
        {
            if (kind != NodeKind.File)
                throw new IOException($"Path '{path}' is not a file.");

            if (mode.HasFlag(SysFileMode.Overwrite))
                ResetFile(normalized);
        }
        else
        {
            if (!mode.HasFlag(SysFileMode.Write) && !mode.HasFlag(SysFileMode.Overwrite))
                throw new FileNotFoundException($"File '{path}' was not found.", path);

            CreateFileInternal(normalized, overwriteExisting: false, mustNotExist: false);
        }

        return new FileHandle(this, normalized, this.ResolveOpenFileInodeId(normalized), mode);
    }

    public void CreateDirectory(string path)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();
        this.InvokeMutation(VfsMutationKind.CreateDirectory, NormalizePath(path));
    }

    public bool Exists(string path)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        var normalized = NormalizePath(path, true);

        return normalized.Length == 0 || TryGetNodeKind(normalized, out _);
    }

    public bool FileExists(string path)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        var normalized = NormalizePath(path);

        return TryGetNodeKind(normalized, out var kind) && kind == NodeKind.File;
    }

    public bool DirectoryExists(string path)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        var normalized = NormalizePath(path, true);

        return normalized.Length == 0 || (TryGetNodeKind(normalized, out var kind) && kind == NodeKind.Directory);
    }

    public void Delete(string path, bool recursive = true)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        var normalized = NormalizePath(path, true);

        if (normalized.Length == 0)
            throw new IOException("Cannot delete the root directory.");

        if (!TryGetNodeKind(normalized, out var kind))
            throw new FileNotFoundException($"Path '{path}' was not found.", path);

        if (kind == NodeKind.File)
        {
            this.InvokeMutation(VfsMutationKind.DeleteFile, normalized);

            return;
        }

        this.InvokeMutation(VfsMutationKind.DeleteDirectory, normalized, recursive: recursive);
    }

    public void DeleteFile(string path)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();
        this.InvokeMutation(VfsMutationKind.DeleteFile, NormalizePath(path));
    }

    public void DeleteDirectory(string path, bool recursive = true)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();
        this.InvokeMutation(VfsMutationKind.DeleteDirectory, NormalizePath(path), recursive: recursive);
    }

    public IReadOnlyList<string> Enumerate(string path)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        var normalized = NormalizePath(path, true);

        if (normalized.Length != 0 && (!TryGetNodeKind(normalized, out var kind) || kind != NodeKind.Directory))
            throw new DirectoryNotFoundException($"Directory '{path}' was not found.");

        return EnumerateCore(normalized);
    }

    public void CreateFile(string path, FileAttributes attributes = FileAttributes.Normal)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();
        this.InvokeMutation(VfsMutationKind.CreateFile, NormalizePath(path), mustNotExist: true);
    }

    public void SetAttribute(string path, string attributeName, ReadOnlySpan<byte> value)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(attributeName))
            throw new ArgumentException("Attribute name cannot be empty.", nameof(attributeName));
        this.InvokeBufferMutation(VfsMutationKind.SetAttribute, NormalizePath(path), attributeName, value);
    }

    public bool TryGetAttribute(string path, string attributeName, out byte[] value)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        var normalized = NormalizePath(path);

        if (string.IsNullOrWhiteSpace(attributeName))
            throw new ArgumentException("Attribute name cannot be empty.", nameof(attributeName));

        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            if (!TryGetAttributeFrom(this.primary, in prefix, normalized, attributeName, out value))
                return false;

            return true;
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    public bool RemoveAttribute(string path, string attributeName)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();

        if (string.IsNullOrWhiteSpace(attributeName))
            throw new ArgumentException("Attribute name cannot be empty.", nameof(attributeName));
        return this.InvokeMutationBool(VfsMutationKind.RemoveAttribute, NormalizePath(path), attributeName);
    }

    public void Dispose()
    {
        using var hold = this.apiSync.Enter();

        if (this.disposed.Enter())
            return;

        this.FlushBoundaryCarrier(this.primary);

        if (this.mirror is not null)
            this.FlushBoundaryCarrier(this.mirror);
    }

    private void InvokeMutation(
        VfsMutationKind kind,
        string path,
        string? name = null,
        long inodeId = InvalidChunkId,
        long position = 0,
        long length = 0,
        bool recursive = true,
        bool mustNotExist = false)
    {
        using var lease = this.lanePool.AcquireLease();
        ref var slot = ref this.mutationSlots[lease.Lane.Index];
        slot.Reset();
        slot.Kind = kind;
        slot.Path = path;
        slot.Name = name;
        slot.InodeId = inodeId;
        slot.Position = position;
        slot.Length = length;
        slot.Recursive = recursive;
        slot.MustNotExist = mustNotExist;
        var result = this.mutationCombiner.Invoke(in lease.Lane);
        var error = slot.Error;
        var succeeded = result != 0;
        slot.Reset();
        error?.Throw();

        _ = succeeded;
    }

    private bool InvokeMutationBool(
        VfsMutationKind kind,
        string path,
        string? name = null,
        long inodeId = InvalidChunkId,
        long position = 0,
        long length = 0,
        bool recursive = true,
        bool mustNotExist = false)
    {
        using var lease = this.lanePool.AcquireLease();
        ref var slot = ref this.mutationSlots[lease.Lane.Index];
        slot.Reset();
        slot.Kind = kind;
        slot.Path = path;
        slot.Name = name;
        slot.InodeId = inodeId;
        slot.Position = position;
        slot.Length = length;
        slot.Recursive = recursive;
        slot.MustNotExist = mustNotExist;
        var result = this.mutationCombiner.Invoke(in lease.Lane);
        var value = result != 0;
        var error = slot.Error;
        slot.Reset();
        error?.Throw();

        return value;
    }

    private unsafe void InvokeBufferMutation(
        VfsMutationKind kind,
        string path,
        string? name,
        ReadOnlySpan<byte> buffer,
        long inodeId = InvalidChunkId,
        long position = 0,
        long length = 0,
        bool recursive = true,
        bool mustNotExist = false)
    {
        using var lease = this.lanePool.AcquireLease();
        ref var slot = ref this.mutationSlots[lease.Lane.Index];
        slot.Reset();
        slot.Kind = kind;
        slot.Path = path;
        slot.Name = name;
        slot.InodeId = inodeId;
        slot.Position = position;
        slot.Length = length;
        slot.Recursive = recursive;
        slot.MustNotExist = mustNotExist;

        fixed (byte* bufferPointer = buffer)
        {
            slot.BufferPointer = bufferPointer;
            slot.BufferLength = buffer.Length;
            _ = this.mutationCombiner.Invoke(in lease.Lane);
        }

        var error = slot.Error;
        slot.Reset();
        error?.Throw();
    }

    private void InvokeFileDeltaMutation(string path, long inodeId, FileDeltaMutation delta)
    {
        using var lease = this.lanePool.AcquireLease();
        ref var slot = ref this.mutationSlots[lease.Lane.Index];
        slot.Reset();
        slot.Kind = VfsMutationKind.CommitFileDelta;
        slot.Path = path;
        slot.InodeId = inodeId;
        slot.FileDelta = delta;
        _ = this.mutationCombiner.Invoke(in lease.Lane);
        var error = slot.Error;
        slot.Reset();
        error?.Throw();
    }

    private static int MeasureString(string value) => MeasureString(value.Length);

    private static int MeasureString(int charCount) => sizeof(int) + charCount * sizeof(char);

    private static int MeasureUpsertDirectory(string path) => 1 + MeasureString(path);

    private static int MeasureUpsertDirectory(int pathLength) => 1 + MeasureString(pathLength);

    private static int MeasureUpsertFile(string path) => 1 + MeasureString(path) + sizeof(long);

    private static int MeasureDeleteExact(string path) => 1 + MeasureString(path);

    private static int MeasureDeletePrefix(string path) => 1 + MeasureString(path);

    private static int MeasureFileLength(string path) => 1 + MeasureString(path) + sizeof(long);

    private static int MeasureContentReset(string path) => 1 + MeasureString(path) + sizeof(long);

    private static int MeasureContentMap(string path) => 1 + MeasureString(path) + sizeof(long) + sizeof(long);

    private static int MeasureAttributeSet(string path, string name, int valueLength) =>
        1 + MeasureString(path) + MeasureString(name) + sizeof(int) + valueLength;

    private static int MeasureAttributeDelete(string path, string name) => 1 + MeasureString(path) + MeasureString(name);

    private void ThrowIfDisposed() => this.disposed.ThrowIf();
}
