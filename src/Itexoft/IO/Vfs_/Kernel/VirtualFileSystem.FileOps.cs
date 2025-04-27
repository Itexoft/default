// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading.Core.Lane;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private void ResetFile(string normalizedPath)
        => this.InvokeMutation(VfsMutationKind.ResetFile, normalizedPath);

    private long GetFileLength(string normalizedPath)
    {
        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            if (!TryGetFileLengthFrom(this.primary, in prefix, normalizedPath, out var length))
                throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

            return length;
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private long GetFileLength(long inodeId)
    {
        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            if (!TryGetFileLengthFrom(this.primary, in prefix, inodeId, out var length))
                throw new FileNotFoundException($"File inode '{inodeId}' was not found.");

            return length;
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private InodeRecord GetInodeRecord(long inodeId)
    {
        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            if (!TryGetInodeRecordFrom(null, this.primary, in prefix, inodeId, out var inode))
                throw new FileNotFoundException($"File inode '{inodeId}' was not found.");

            return inode;
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private int ReadFile(string normalizedPath, long position, Span<byte> destination)
    {
        if (destination.Length == 0)
            return 0;

        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            if (!TryGetFileLengthFrom(this.primary, in prefix, normalizedPath, out var length))
                throw new FileNotFoundException($"File '{normalizedPath}' was not found.", normalizedPath);

            if (position < 0)
                throw new ArgumentOutOfRangeException(nameof(position));

            if (position >= length)
                return 0;

            var total = 0;
            var available = Math.Min((long)destination.Length, length - position);

            while (total < available)
            {
                var absolute = position + total;
                var logicalChunk = absolute / this.chunkSize;
                var chunkOffset = (int)(absolute % this.chunkSize);
                var segmentLength = (int)Math.Min(this.chunkSize - chunkOffset, available - total);

                if (TryResolveContentChunkFrom(this.primary, in prefix, normalizedPath, logicalChunk, out var chunkId))
                    ReadChunkPayload(this.primary, chunkId, chunkOffset, destination.Slice(total, segmentLength));
                else
                    destination.Slice(total, segmentLength).Clear();

                total += segmentLength;
            }

            return total;
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private int ReadFile(long inodeId, long position, Span<byte> destination)
    {
        if (destination.Length == 0)
            return 0;

        using var lease = this.lanePool.AcquireLease();
        var prefix = this.EnterPublishedSnapshot(in lease.Lane, out var enteredEpoch);

        try
        {
            if (!TryGetFileLengthFrom(this.primary, in prefix, inodeId, out var length))
                throw new FileNotFoundException($"File inode '{inodeId}' was not found.");

            if (position < 0)
                throw new ArgumentOutOfRangeException(nameof(position));

            if (position >= length)
                return 0;

            var total = 0;
            var available = Math.Min((long)destination.Length, length - position);

            while (total < available)
            {
                var absolute = position + total;
                var logicalChunk = absolute / this.chunkSize;
                var chunkOffset = (int)(absolute % this.chunkSize);
                var segmentLength = (int)Math.Min(this.chunkSize - chunkOffset, available - total);

                if (TryResolveContentChunkFrom(this.primary, in prefix, inodeId, logicalChunk, out var chunkId))
                    ReadChunkPayload(this.primary, chunkId, chunkOffset, destination.Slice(total, segmentLength));
                else
                    destination.Slice(total, segmentLength).Clear();

                total += segmentLength;
            }

            return total;
        }
        finally
        {
            this.ExitPublishedSnapshot(in lease.Lane, enteredEpoch);
        }
    }

    private void WriteFile(string normalizedPath, long position, ReadOnlySpan<byte> source)
    {
        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));

        if (source.Length == 0)
            return;

        this.InvokeBufferMutation(VfsMutationKind.WriteFile, normalizedPath, null, source, position: position);
    }

    private void WriteFile(string normalizedPath, long inodeId, long position, ReadOnlySpan<byte> source)
    {
        if (position < 0)
            throw new ArgumentOutOfRangeException(nameof(position));

        if (source.Length == 0)
            return;

        this.InvokeBufferMutation(VfsMutationKind.WriteFile, normalizedPath, null, source, inodeId: inodeId, position: position);
    }

    private void ReplaceFile(string normalizedPath, long inodeId, ReadOnlySpan<byte> source)
        => this.InvokeBufferMutation(VfsMutationKind.ReplaceFile, normalizedPath, null, source, inodeId: inodeId);

    private void SetFileLength(string normalizedPath, long newLength)
    {
        if (newLength < 0)
            throw new ArgumentOutOfRangeException(nameof(newLength));

        this.InvokeMutation(VfsMutationKind.SetFileLength, normalizedPath, length: newLength);
    }

    private void SetFileLength(string normalizedPath, long inodeId, long newLength)
    {
        if (newLength < 0)
            throw new ArgumentOutOfRangeException(nameof(newLength));

        this.InvokeMutation(VfsMutationKind.SetFileLength, normalizedPath, inodeId: inodeId, length: newLength);
    }
}
