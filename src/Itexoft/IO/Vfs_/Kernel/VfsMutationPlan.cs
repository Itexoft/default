// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Vfs;

internal struct VfsMutationPlan(VirtualFileSystem owner, IStreamRwsl<byte> target, long generation, long reusableRoot, long deferredReuseRoot)
{
    private readonly VirtualFileSystem owner = owner;
    private readonly IStreamRwsl<byte> target = target;
    private long reusableRoot = reusableRoot;
    private long deferredReuseRoot = deferredReuseRoot;
    private long retiredChunkLogRoot = VirtualFileSystem.InvalidChunkId;
    private long retiredChunkLogTail = VirtualFileSystem.InvalidChunkId;
    private long takenReusableChunkLogRoot = VirtualFileSystem.InvalidChunkId;
    private long pendingDeferredLogChunkId = VirtualFileSystem.InvalidChunkId;
    private readonly long appendStartChunkId = owner.GetVisibleChunkCount(target);
    private long nextAppendChunkId = owner.GetVisibleChunkCount(target);
    private int appendOnlyDepth;
    private int noRetireDepth;

    public long Generation { get; } = generation;

    public long AppendStartChunkId => this.appendStartChunkId;

    public long AppendEndChunkIdExclusive => this.nextAppendChunkId;

    public long RetiredChunkLogRoot => this.retiredChunkLogRoot;

    public long ReusableRoot => this.reusableRoot;

    public long TakenReusableChunkLogRoot => this.takenReusableChunkLogRoot;

    public bool ShouldRetireRewrittenRoots => this.noRetireDepth == 0;

    public long DeferredReuseRoot => this.retiredChunkLogRoot == VirtualFileSystem.InvalidChunkId
        ? this.deferredReuseRoot
        : this.retiredChunkLogRoot;

    public long AllocateChunkId()
    {
        if (this.appendOnlyDepth != 0 || this.owner.HasActiveDraftSessions())
            return this.AllocateAppendChunkId();

        if (this.TryTakeDeferredChunk(out var deferredChunkId))
            return this.RecordTakenReusableChunk(deferredChunkId);

        if (this.owner.TryTakeReusableChunk(this.target, ref this.reusableRoot, ref this, out var chunkId))
            return this.RecordTakenReusableChunk(chunkId);

        return this.AllocateAppendChunkId();
    }

    public long AllocateAppendChunkId()
    {
        this.owner.EnsurePublishedChunkCapacity(this.nextAppendChunkId + 1);
        return this.nextAppendChunkId++;
    }

    public void EnterAppendOnly() => this.appendOnlyDepth++;

    public void ExitAppendOnly()
    {
        if (this.appendOnlyDepth == 0)
            throw new InvalidOperationException("Append-only scope was not entered.");

        this.appendOnlyDepth--;
    }

    public void EnterNoRetire() => this.noRetireDepth++;

    public void ExitNoRetire()
    {
        if (this.noRetireDepth == 0)
            throw new InvalidOperationException("No-retire scope was not entered.");

        this.noRetireDepth--;
    }

    public void RetireChunkId(long chunkId)
    {
        if (chunkId == VirtualFileSystem.InvalidChunkId)
            return;

        this.retiredChunkLogRoot = this.owner.PrependDeferredChunkLogEntry(this.target, this.retiredChunkLogRoot, chunkId, ref this);

        if (this.retiredChunkLogTail == VirtualFileSystem.InvalidChunkId)
            this.retiredChunkLogTail = this.retiredChunkLogRoot;
    }

    public void RetireStream(long rootChunkId)
    {
        if (rootChunkId == VirtualFileSystem.InvalidChunkId)
            return;

        var remaining = this.owner.GetVisibleChunkCount(this.target);
        var current = rootChunkId;

        while (current != VirtualFileSystem.InvalidChunkId)
        {
            if (--remaining < 0)
                throw new InvalidDataException("Chunk stream is cyclic.");

            var header = this.owner.ReadChunkHeader(this.target, current);
            this.RetireChunkId(current);
            current = header.LinkChunkId;
        }
    }

    public long FinalizeDeferredReuseRoot()
    {
        var carryRoot = this.deferredReuseRoot;

        if (this.pendingDeferredLogChunkId != VirtualFileSystem.InvalidChunkId)
            carryRoot = this.owner.PrependDeferredChunkLogEntry(this.target, carryRoot, this.pendingDeferredLogChunkId, ref this);

        if (this.retiredChunkLogRoot == VirtualFileSystem.InvalidChunkId)
            return carryRoot;

        if (carryRoot != VirtualFileSystem.InvalidChunkId)
            this.owner.RewriteDeferredChunkLogTailLink(this.target, this.retiredChunkLogTail, carryRoot);

        return this.retiredChunkLogRoot;
    }

    private long RecordTakenReusableChunk(long chunkId)
    {
        this.takenReusableChunkLogRoot = this.owner.PrependTakenReusableChunkLogEntry(this.target, this.takenReusableChunkLogRoot, chunkId, ref this);
        return chunkId;
    }

    private bool TryTakeDeferredChunk(out long chunkId)
    {
        if (this.pendingDeferredLogChunkId != VirtualFileSystem.InvalidChunkId)
        {
            chunkId = this.pendingDeferredLogChunkId;
            this.pendingDeferredLogChunkId = VirtualFileSystem.InvalidChunkId;
            return true;
        }

        if (this.deferredReuseRoot == VirtualFileSystem.InvalidChunkId)
        {
            chunkId = VirtualFileSystem.InvalidChunkId;
            return false;
        }

        chunkId = this.owner.ReadDeferredReuseChunkId(this.target, this.deferredReuseRoot, out var nextLogRootChunkId);
        this.pendingDeferredLogChunkId = this.deferredReuseRoot;
        this.deferredReuseRoot = nextLogRootChunkId;
        return true;
    }
}
