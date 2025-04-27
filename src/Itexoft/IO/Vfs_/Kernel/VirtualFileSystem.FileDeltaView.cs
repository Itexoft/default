// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private sealed class FileDeltaView(VirtualFileSystem owner, string path, long inodeId)
    {
        private readonly VirtualFileSystem owner = owner;
        private readonly string path = path;
        private readonly long inodeId = inodeId;
        private long sessionId;
        private long baseLength;
        private long baseContentRoot;
        private long currentLength;
        private int baseAttributes;
        private Dictionary<long, long>? draftChunks;
        private AtomicLock sync;
        private Disposed disposed;

        public long GetLength()
        {
            this.ThrowIfDisposed();
            using var ownerHold = this.owner.apiSync.Enter();
            using var hold = this.sync.Enter();
            return this.sessionId == 0 ? this.owner.GetFileLength(this.inodeId) : this.currentLength;
        }

        public void SetLength(long value)
        {
            this.ThrowIfDisposed();

            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            using var ownerHold = this.owner.apiSync.Enter();
            using var hold = this.sync.Enter();
            this.EnsureSession();
            this.currentLength = value;
        }

        public int Read(long offset, Span<byte> destination, in CancelToken cancelToken = default)
        {
            this.ThrowIfDisposed(in cancelToken);

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            using var ownerHold = this.owner.apiSync.Enter();
            using var hold = this.sync.Enter();
            return this.sessionId == 0
                ? this.owner.ReadFile(this.inodeId, offset, destination)
                : this.ReadDelta(offset, destination);
        }

        public void Write(long offset, ReadOnlySpan<byte> source, in CancelToken cancelToken = default)
        {
            this.ThrowIfDisposed(in cancelToken);

            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (source.Length == 0)
                return;

            using var ownerHold = this.owner.apiSync.Enter();
            using var hold = this.sync.Enter();
            this.WriteDelta(offset, source);
        }

        public void Commit(in CancelToken cancelToken = default)
        {
            this.ThrowIfDisposed(in cancelToken);
            using var ownerHold = this.owner.apiSync.Enter();
            using var hold = this.sync.Enter();
            this.FlushCore();
        }

        public void Flush(in CancelToken cancelToken = default)
        {
            this.Commit(in cancelToken);
            this.FlushCarriers();
        }

        public void Dispose()
        {
            if (this.disposed)
                return;

            using var ownerHold = this.owner.apiSync.Enter();
            using var hold = this.sync.Enter();

            if (this.disposed.Enter())
                return;

            this.FlushCore();
            this.FlushCarriers();
        }

        public void ThrowIfDisposed() => this.disposed.ThrowIf();

        public void ThrowIfDisposed(in CancelToken cancelToken) => this.disposed.ThrowIf(in cancelToken);

        private void EnsureSession()
        {
            if (this.sessionId != 0)
                return;

            var inode = this.owner.GetInodeRecord(this.inodeId);
            this.sessionId = this.owner.BeginFileDeltaSession(this.inodeId);
            this.baseLength = inode.Length;
            this.baseContentRoot = inode.ContentRoot;
            this.baseAttributes = inode.Attributes;
            this.currentLength = inode.Length;
            this.draftChunks ??= [];
        }

        private int ReadDelta(long offset, Span<byte> destination)
        {
            if (destination.Length == 0 || offset >= this.currentLength)
                return 0;

            var total = 0;
            var available = Math.Min((long)destination.Length, this.currentLength - offset);
            var window = new byte[this.owner.chunkSize];

            while (total < available)
            {
                var absolute = offset + total;
                var logicalChunk = absolute / this.owner.chunkSize;
                var chunkStart = logicalChunk * (long)this.owner.chunkSize;
                var chunkOffset = (int)(absolute - chunkStart);
                var readable = (int)Math.Min(this.owner.chunkSize - chunkOffset, available - total);
                this.LoadChunk(logicalChunk, window);
                window.AsSpan(chunkOffset, readable).CopyTo(destination.Slice(total, readable));
                total += readable;
            }

            return total;
        }

        private void WriteDelta(long offset, ReadOnlySpan<byte> source)
        {
            this.EnsureSession();
            var total = 0;
            var window = new byte[this.owner.chunkSize];

            while (total < source.Length)
            {
                var absolute = offset + total;
                var logicalChunk = absolute / this.owner.chunkSize;
                var chunkStart = logicalChunk * (long)this.owner.chunkSize;
                var chunkOffset = (int)(absolute - chunkStart);
                var writable = Math.Min(this.owner.chunkSize - chunkOffset, source.Length - total);
                this.LoadChunk(logicalChunk, window);
                source.Slice(total, writable).CopyTo(window.AsSpan(chunkOffset, writable));

                if (!this.draftChunks!.TryGetValue(logicalChunk, out var draftChunkId))
                {
                    draftChunkId = this.owner.AllocateDraftChunkId();
                    this.draftChunks.Add(logicalChunk, draftChunkId);
                }

                this.owner.WriteDraftChunk(draftChunkId, window);
                total += writable;
            }

            var end = checked(offset + source.Length);

            if (end > this.currentLength)
                this.currentLength = end;
        }

        private void LoadChunk(long logicalChunk, byte[] window)
        {
            var span = window.AsSpan();
            span.Clear();

            if (this.draftChunks is not null && this.draftChunks.TryGetValue(logicalChunk, out var draftChunkId))
            {
                this.owner.ReadDraftChunk(draftChunkId, span);
                return;
            }

            _ = this.owner.ReadFile(this.inodeId, logicalChunk * (long)this.owner.chunkSize, span);
            var visibleBytes = Math.Clamp(this.currentLength - logicalChunk * (long)this.owner.chunkSize, 0, this.owner.chunkSize);

            if (visibleBytes < this.owner.chunkSize)
                span[(int)visibleBytes..].Clear();
        }

        private void FlushCore()
        {
            if (this.sessionId == 0)
                return;

            if ((this.draftChunks is null || this.draftChunks.Count == 0) && this.currentLength == this.baseLength)
            {
                this.owner.EndFileDeltaSession(this.inodeId, this.sessionId);
                this.ResetSession();
                return;
            }

            var delta = this.BuildDeltaMutation();
            this.owner.InvokeFileDeltaMutation(this.path, this.inodeId, delta);
            this.owner.EndFileDeltaSession(this.inodeId, this.sessionId);
            this.ResetSession();
        }

        private FileDeltaMutation BuildDeltaMutation()
        {
            if (this.draftChunks is null || this.draftChunks.Count == 0)
                return new(this.baseLength, this.baseContentRoot, this.baseAttributes, this.currentLength, []);

            var entries = new KeyValuePair<long, long>[this.draftChunks.Count];
            var index = 0;

            foreach (var pair in this.draftChunks)
                entries[index++] = pair;

            Array.Sort(entries, static (left, right) => left.Key.CompareTo(right.Key));
            var chunks = new FileDeltaChunkMutation[entries.Length];

            for (var i = 0; i < entries.Length; i++)
                chunks[i] = new(entries[i].Key, FileDeltaChunkKind.Draft, entries[i].Value, null);

            return new(this.baseLength, this.baseContentRoot, this.baseAttributes, this.currentLength, chunks);
        }

        private void ResetSession()
        {
            this.sessionId = 0;
            this.baseLength = 0;
            this.baseContentRoot = InvalidChunkId;
            this.baseAttributes = 0;
            this.currentLength = 0;
            this.draftChunks?.Clear();
        }

        private void FlushCarriers()
        {
            FlushCarrier(this.owner.primary);

            if (this.owner.mirror is not null)
                FlushCarrier(this.owner.mirror);
        }
    }
}
