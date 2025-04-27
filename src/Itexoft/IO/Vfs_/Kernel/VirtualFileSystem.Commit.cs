// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.ExceptionServices;
using Itexoft.Threading.Core.Lane;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    internal void ExecuteMutation(int laneIndex, out byte result)
    {
        ref var slot = ref this.mutationSlots[laneIndex];
        result = 0;

        try
        {
            var previousHead = this.publishedHead.Read();
            long nextHead;
            bool boolResult;

            if (this.mirror is null)
            {
                var primaryPrefix = this.ApplyCommittedMutation(this.primary, ref slot, out boolResult, out _);
                nextHead = primaryPrefix.Generation;
                this.committedPrefix = primaryPrefix;
                this.publishedChunkCapacity = primaryPrefix.Snapshot.PublishedChunkCapacity;
            }
            else
            {
                this.EnsureMirrorBaseEquivalent(previousHead);
                var mirrorPrefix = this.ApplyCommittedMutation(this.mirror, ref slot, out boolResult, out var tape);

                if (tape.Generation != previousHead)
                {
                    this.ReplayDeltaTapeToCarrier(this.mirror, this.primary, in mirrorPrefix, in tape);
                    this.FlushMutationCarrier(this.primary);
                }

                var primaryPrefix = ReadPublishedPrefix(this, this.primary, tape.Generation);
                if (this.primary is CarrierStream primaryCarrier)
                    primaryCarrier.SetPublishedLength(this.GetPublishedLength(primaryPrefix.Snapshot.PublishedChunkCount));
                nextHead = primaryPrefix.Generation;
                this.committedPrefix = primaryPrefix;
                this.publishedChunkCapacity = primaryPrefix.Snapshot.PublishedChunkCapacity;

                if (mirrorPrefix.Generation != primaryPrefix.Generation
                    || mirrorPrefix.ChunkSize != primaryPrefix.ChunkSize
                    || !mirrorPrefix.Snapshot.Matches(primaryPrefix.Snapshot))
                {
                    throw new InvalidDataException("Mirrored mutation diverged.");
                }
            }

            this.CommitPublishedHead(previousHead, nextHead);
            result = boolResult ? (byte)1 : (byte)0;
        }
        catch (Exception exception)
        {
            slot.Error = ExceptionDispatchInfo.Capture(exception);
        }
    }

    private PrefixState ApplyCommittedMutation(IStreamRwsl<byte> target, ref VfsMutationSlot slot, out bool boolResult, out VfsDeltaTape tape)
    {
        tape = this.ApplyMutationToCarrier(target, ref slot, out boolResult);

        if (target is not FileHandle)
            this.FlushMutationCarrier(target);

        var prefix = ReadPublishedPrefix(this, target, tape.Generation);

        if (target is CarrierStream carrier)
            carrier.SetPublishedLength(this.GetPublishedLength(prefix.Snapshot.PublishedChunkCount));

        return prefix;
    }

    private void CommitPublishedHead(long previousHead, long nextHead)
    {
        if (previousHead == nextHead)
            return;

        this.publishedHead.Publish(nextHead);
        _ = this.publishedHead.AdvanceAndWait();
    }

    private PrefixState EnterPublishedSnapshot(in Lane64 lane, out int enteredEpoch)
    {
        var generation = this.publishedHead.EnterRead(in lane, out enteredEpoch);
        var committed = this.committedPrefix;
        return committed.Generation == generation
            ? committed
            : ReadPublishedPrefix(this, this.primary, generation);
    }

    private void ExitPublishedSnapshot(in Lane64 lane, int enteredEpoch) => this.publishedHead.ExitRead(in lane, enteredEpoch);

    private void EnsureMirrorBaseEquivalent(long generation)
    {
        if (this.mirror is null)
            throw new InvalidOperationException("Mirror carrier is not configured.");

        var primaryPrefix = ReadPublishedPrefix(this, this.primary, generation);
        var mirrorPrefix = ReadRequiredPrefix(this, this.mirror);
        var snapshotMatches = primaryPrefix.Snapshot.Matches(mirrorPrefix.Snapshot);
        var primaryLength = this.primary.Length;
        var mirrorLength = this.mirror.Length;
        var primaryVisible = this.GetVisibleChunkCount(this.primary);
        var mirrorVisible = this.GetVisibleChunkCount(this.mirror);

        if (primaryPrefix.Generation != mirrorPrefix.Generation
            || primaryPrefix.ChunkSize != mirrorPrefix.ChunkSize
            || !snapshotMatches
            || primaryLength != mirrorLength
            || primaryVisible != mirrorVisible)
        {
            throw new InvalidDataException(
                $"Mirrored carriers diverged before mutation. primaryGeneration={primaryPrefix.Generation}, mirrorGeneration={mirrorPrefix.Generation}, "
                + $"primaryChunkSize={primaryPrefix.ChunkSize}, mirrorChunkSize={mirrorPrefix.ChunkSize}, snapshotMatches={snapshotMatches}, "
                + $"primaryLength={primaryLength}, mirrorLength={mirrorLength}, primaryVisible={primaryVisible}, mirrorVisible={mirrorVisible}.");
        }
    }

    private void ReplayDeltaTapeToCarrier(IStreamRwsl<byte> source, IStreamRwsl<byte> destination, in PrefixState publishedPrefix, in VfsDeltaTape tape)
    {
        if (publishedPrefix.Generation != tape.Generation)
            throw new InvalidDataException("Replay prefix does not match the mutation generation.");

        for (var chunkId = tape.AppendStartChunkId; chunkId < tape.AppendEndChunkIdExclusive; chunkId++)
            this.CopyChunkRecordBetweenCarriers(source, destination, chunkId);

        var current = tape.ReusedChunkLogRoot;

        while (current != InvalidChunkId)
        {
            var reusedChunkId = this.ReadChunkIdLogEntry(source, current, out var next);
            this.CopyChunkRecordBetweenCarriers(source, destination, reusedChunkId);
            current = next;
        }

        WritePrefixSlot(this, destination, in publishedPrefix, publishedPrefix.SlotIndex);
    }

    private void CopyChunkRecordBetweenCarriers(IStreamRwsl<byte> source, IStreamRwsl<byte> destination, long chunkId)
    {
        if (chunkId == InvalidChunkId)
            return;

        var recordSize = this.GetChunkRecordSize();
        var sourceOffset = this.GetChunkOffset(chunkId);
        var destinationOffset = this.GetChunkOffset(chunkId);
        var requiredLength = destinationOffset + recordSize;

        if (requiredLength > destination.Length)
            destination.Length = requiredLength;

        CopyRange(this, source, sourceOffset, destination, destinationOffset, recordSize, this.GetCarrierWindowSize(recordSize));
    }

    private unsafe VfsDeltaTape ApplyMutationToCarrier(IStreamRwsl<byte> target, ref VfsMutationSlot slot, out bool boolResult)
    {
        boolResult = false;

        return slot.Kind switch
        {
            VfsMutationKind.CreateDirectory => this.ApplyCreateDirectoryToCarrier(target, slot.Path!),
            VfsMutationKind.DeleteFile => this.ApplyDeleteFileToCarrier(target, slot.Path!),
            VfsMutationKind.DeleteDirectory => this.ApplyDeleteDirectoryToCarrier(target, slot.Path!, slot.Recursive),
            VfsMutationKind.CreateFile => this.ApplyCreateFileToCarrier(target, slot.Path!, slot.MustNotExist),
            VfsMutationKind.ResetFile => this.ApplyResetFileToCarrier(target, slot.Path!),
            VfsMutationKind.SetAttribute => this.ApplySetAttributeToCarrier(target, slot.Path!, slot.Name!, new ReadOnlySpan<byte>(slot.BufferPointer, slot.BufferLength)),
            VfsMutationKind.RemoveAttribute => this.ApplyRemoveAttributeToCarrier(target, slot.Path!, slot.Name!, out boolResult),
            VfsMutationKind.WriteFile => slot.InodeId != InvalidChunkId
                ? this.ApplyWriteFileToCarrier(target, slot.InodeId, slot.Position, new ReadOnlySpan<byte>(slot.BufferPointer, slot.BufferLength))
                : this.ApplyWriteFileToCarrier(target, slot.Path!, slot.Position, new ReadOnlySpan<byte>(slot.BufferPointer, slot.BufferLength)),
            VfsMutationKind.ReplaceFile => slot.InodeId != InvalidChunkId
                ? this.ApplyReplaceFileToCarrier(target, slot.InodeId, new ReadOnlySpan<byte>(slot.BufferPointer, slot.BufferLength))
                : this.ApplyReplaceFileToCarrier(target, slot.Path!, new ReadOnlySpan<byte>(slot.BufferPointer, slot.BufferLength)),
            VfsMutationKind.SetFileLength => slot.InodeId != InvalidChunkId
                ? this.ApplySetFileLengthToCarrier(target, slot.InodeId, slot.Length)
                : this.ApplySetFileLengthToCarrier(target, slot.Path!, slot.Length),
            VfsMutationKind.CommitFileDelta => this.ApplyFileDeltaToCarrier(target, slot.Path!, slot.InodeId, slot.FileDelta!),
            _ => throw new InvalidOperationException($"Unknown mutation kind '{slot.Kind}'."),
        };
    }
}
