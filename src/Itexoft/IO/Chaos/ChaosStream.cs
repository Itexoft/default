// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Runtime.InteropServices;
using Itexoft.Core;
using Itexoft.Threading;

namespace Itexoft.IO.Chaos;

public sealed class ChaosStream<T> : IStreamRw<T> where T : unmanaged
{
    private readonly Action<TimeSpan, CancelToken> delayScheduler;
    private readonly bool leaveOpen;
    private readonly ChaosStreamOptions options;
    private readonly Random random;
    private readonly long startTimestamp;
    private readonly TimeProvider timeProvider;
    private long bytesRead;
    private long bytesWritten;
    private int disconnected;
    private Disposed disposed = new();
    private long flushOperations;

    private long readOperations;
    private long writeOperations;

    public ChaosStream(IStreamRw<T> inner, ChaosStreamOptions? options = null, bool leaveOpen = false)
    {
        this.InnerStream = inner ?? throw new ArgumentNullException(nameof(inner));
        this.options = (options ?? new ChaosStreamOptions()).Clone();
        this.options.Validate();
        this.leaveOpen = leaveOpen;
        this.random = this.options.Random ?? Random.Shared;
        this.delayScheduler = this.options.DelayScheduler ?? DefaultDelayScheduler;
        this.timeProvider = this.options.TimeProvider ?? TimeProvider.System;
        this.startTimestamp = this.timeProvider.GetTimestamp();
    }

    public IStreamRw<T> InnerStream { get; }

    public void Flush(CancelToken cancelToken = default)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Flush, 0);
        this.MaybeDisconnect(context);

        this.MaybeStall(context, default);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            this.MaybeDelay(this.options.MinWriteDelay, this.options.MaxWriteDelay, this.options.WriteDelayProbability, default);
            this.MaybeThrow(this.options.FlushFailureProbability, "flush");
        }

        this.InnerStream.Flush();
        this.RecordOperation(ChaosOperation.Flush, 0);
    }

    public int Read(Span<T> span, CancelToken cancelToken = default)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Read, span.Length);
        this.MaybeDisconnect(context);

        this.MaybeStall(context, default);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            this.MaybeDelay(this.options.MinReadDelay, this.options.MaxReadDelay, this.options.ReadDelayProbability, default);
            this.MaybeThrow(this.options.ReadFailureProbability, "read");

            if (this.ShouldSkip(this.options.DropReadProbability))
            {
                this.RecordOperation(ChaosOperation.Read, 0);

                return 0;
            }
        }

        var read = this.InnerStream.Read(span);

        if (read <= 0)
        {
            this.RecordOperation(ChaosOperation.Read, 0);

            return read;
        }

        if (inject)
        {
            read = this.MaybeTruncate(read, this.options.ReadTruncationProbability, this.options.MaxReadTruncationBytes);

            if (read > 0)
                this.MaybeFlipBits(span[..read], this.options.ReadBitFlipProbability, this.options.MaxReadBitFlips);
        }

        this.RecordOperation(ChaosOperation.Read, read);
        this.ApplyPerByteDelay(ChaosOperation.Read, read, default);

        return read;
    }

    public void Write(ReadOnlySpan<T> buffer, CancelToken cancelToken = default)
    {
        this.ThrowIfUnavailable();
        var context = this.PrepareContext(ChaosOperation.Write, buffer.Length);
        this.MaybeDisconnect(context);

        this.MaybeStall(context, default);

        var inject = this.ShouldInjectNoise(context);

        if (inject)
        {
            this.MaybeDelay(this.options.MinWriteDelay, this.options.MaxWriteDelay, this.options.WriteDelayProbability, default);
            this.MaybeThrow(this.options.WriteFailureProbability, "write");

            if (this.ShouldSkip(this.options.DropWriteProbability))
            {
                this.RecordOperation(ChaosOperation.Write, 0);

                return;
            }
        }

        var effectiveLength = buffer.Length;

        if (inject)
        {
            effectiveLength = this.MaybeTruncate(effectiveLength, this.options.WriteTruncationProbability, this.options.MaxWriteTruncationBytes);

            if (effectiveLength <= 0)
            {
                this.RecordOperation(ChaosOperation.Write, 0);

                return;
            }
        }

        var writtenBytes = effectiveLength;
        var slice = buffer[..effectiveLength];

        if (inject && this.ShouldFlip(this.options.WriteBitFlipProbability, this.options.MaxWriteBitFlips, out var flips))
        {
            var rented = ArrayPool<T>.Shared.Rent(effectiveLength);

            try
            {
                var temp = rented.AsSpan(0, effectiveLength);
                slice.CopyTo(temp);
                ChaosStreamUtils.ApplyBitFlips(this.random, temp, flips);
                this.InnerStream.Write(temp);
            }
            finally
            {
                ArrayPool<T>.Shared.Return(rented);
            }
        }
        else
            this.InnerStream.Write(slice);

        this.RecordOperation(ChaosOperation.Write, writtenBytes);
        this.ApplyPerByteDelay(ChaosOperation.Write, writtenBytes, default);
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        if (!this.leaveOpen)
            this.InnerStream.Dispose();
    }

    private static void DefaultDelayScheduler(TimeSpan delay, CancelToken cancelToken)
    {
        if (delay <= TimeSpan.Zero)
            return;

        Spin.Delay(delay, cancelToken);
    }

    private void ThrowIfUnavailable()
    {
        ObjectDisposedException.ThrowIf(this.disposed, this);

        if (Atomic.Read(ref this.disconnected) == 1)
            this.ThrowDisconnected();
    }

    private void ThrowDisconnected() => throw this.CreateDisconnectException();

    private IOException CreateDisconnectException() => new(this.options.DisconnectMessage ?? "ChaosStream injected a disconnection.");

    private bool ShouldInjectNoise(in ChaosOperationContext context)
    {
        if (this.options.ShouldApplyNoise is { } global && !global(context))
            return false;

        var specific = context.Operation switch
        {
            ChaosOperation.Read => this.options.ShouldApplyReadNoise,
            ChaosOperation.Write => this.options.ShouldApplyWriteNoise,
            ChaosOperation.Flush => this.options.ShouldApplyFlushNoise,
            _ => null,
        };

        if (specific is null)
            return true;

        return specific(context);
    }

    private ChaosOperationContext PrepareContext(ChaosOperation operation, int requestedBytes)
    {
        var metrics = this.GetMetrics();

        var operationNumber = operation switch
        {
            ChaosOperation.Read => metrics.ReadOperations + 1,
            ChaosOperation.Write => metrics.WriteOperations + 1,
            ChaosOperation.Flush => metrics.FlushOperations + 1,
            _ => metrics.TotalOperations + 1,
        };

        return new(operation, requestedBytes, operationNumber, metrics);
    }

    private ChaosMetrics GetMetrics()
    {
        var elapsed = this.timeProvider.GetElapsedTime(this.startTimestamp);

        return new(
            elapsed,
            Atomic.Read(ref this.readOperations),
            Atomic.Read(ref this.writeOperations),
            Atomic.Read(ref this.flushOperations),
            Atomic.Read(ref this.bytesRead),
            Atomic.Read(ref this.bytesWritten));
    }

    private void MaybeDisconnect(in ChaosOperationContext context)
    {
        if (Atomic.Read(ref this.disconnected) == 1)
            this.ThrowDisconnected();

        if (this.options.ShouldDisconnect is { } predicate && predicate(context))
            this.TriggerDisconnect();
        else if (this.options.DisconnectProbability > 0 && this.NextDouble() < this.options.DisconnectProbability)
            this.TriggerDisconnect();
    }

    private void TriggerDisconnect()
    {
        if (Interlocked.Exchange(ref this.disconnected, 1) == 0)
            throw this.CreateDisconnectException();

        this.ThrowDisconnected();
    }

    private void MaybeStall(in ChaosOperationContext context, CancelToken cancelToken)
    {
        var duration = this.GetStallDuration(context);

        if (duration is null || duration <= TimeSpan.Zero)
            return;

        this.delayScheduler(duration.Value, cancelToken);
    }

    private TimeSpan? GetStallDuration(in ChaosOperationContext context)
    {
        if (this.options.StallDurationProvider is { } provider)
        {
            var value = provider(context);

            return value is { } stall && stall > TimeSpan.Zero ? stall : null;
        }

        if (this.options.StallProbability <= 0)
            return null;

        if (this.NextDouble() >= this.options.StallProbability)
            return null;

        var duration = this.NextDelay(this.options.MinStallDuration, this.options.MaxStallDuration);

        return duration > TimeSpan.Zero ? duration : null;
    }

    private void RecordOperation(ChaosOperation operation, int affectedBytes)
    {
        switch (operation)
        {
            case ChaosOperation.Read:
                Interlocked.Increment(ref this.readOperations);

                if (affectedBytes > 0)
                    Interlocked.Add(ref this.bytesRead, affectedBytes);

                break;
            case ChaosOperation.Write:
                Interlocked.Increment(ref this.writeOperations);

                if (affectedBytes > 0)
                    Interlocked.Add(ref this.bytesWritten, affectedBytes);

                break;
            case ChaosOperation.Flush:
                Interlocked.Increment(ref this.flushOperations);

                break;
        }
    }

    private void ApplyPerByteDelay(ChaosOperation operation, int bytes, CancelToken cancelToken)
    {
        var duration = this.CalculatePerByteDelay(operation, bytes);

        if (duration is null || duration <= TimeSpan.Zero)
            return;

        this.delayScheduler(duration.Value, cancelToken);
    }

    private TimeSpan? CalculatePerByteDelay(ChaosOperation operation, int bytes)
    {
        if (bytes <= 0)
            return null;

        var perByte = operation switch
        {
            ChaosOperation.Read => this.options.ReadPerByteDelay,
            ChaosOperation.Write => this.options.WritePerByteDelay,
            _ => TimeSpan.Zero,
        };

        if (perByte <= TimeSpan.Zero)
            return null;

        try
        {
            var totalTicks = checked(perByte.Ticks * (long)bytes);

            if (totalTicks <= 0)
                return null;

            return TimeSpan.FromTicks(totalTicks);
        }
        catch (OverflowException)
        {
            return TimeSpan.MaxValue;
        }
    }

    private void MaybeThrow(double probability, string operation)
    {
        if (probability <= 0)
            return;

        if (this.NextDouble() < probability)
            throw new IOException($"ChaosStream injected a failure during {operation}.");
    }

    private bool ShouldSkip(double probability) => probability > 0 && this.NextDouble() < probability;

    private bool ShouldFlip(double probability, int maxFlips, out int flips)
    {
        flips = 0;

        if (probability <= 0 || maxFlips <= 0)
            return false;

        if (this.NextDouble() >= probability)
            return false;

        flips = Math.Min(maxFlips, int.MaxValue);

        flips = flips > 1 ? ChaosStreamUtils.NextInt32(this.random, 1, flips + 1) : 1;

        return flips > 0;
    }

    private int MaybeTruncate(int count, double probability, int maxBytes)
    {
        if (count <= 0 || probability <= 0 || maxBytes <= 0)
            return count;

        if (this.NextDouble() >= probability)
            return count;

        var dropLimit = Math.Min(count, maxBytes);
        var drop = dropLimit <= 1 ? 1 : ChaosStreamUtils.NextInt32(this.random, 1, dropLimit + 1);
        var remaining = count - drop;

        return remaining < 0 ? 0 : remaining;
    }

    private void MaybeFlipBits(Span<T> data, double probability, int maxFlips)
    {
        if (!this.ShouldFlip(probability, maxFlips, out var flips))
            return;

        ChaosStreamUtils.ApplyBitFlips(this.random, data, flips);
    }

    private void MaybeDelay(TimeSpan min, TimeSpan max, double probability, CancelToken cancelToken)
    {
        if (max <= TimeSpan.Zero || probability <= 0)
            return;

        if (this.NextDouble() >= probability)
            return;

        var delay = this.NextDelay(min, max);

        if (delay <= TimeSpan.Zero)
            return;

        this.delayScheduler(delay, cancelToken);
    }

    private TimeSpan NextDelay(TimeSpan min, TimeSpan max)
    {
        if (max <= min)
            return min;

        var rangeTicks = max.Ticks - min.Ticks;
        var sample = this.NextDouble();
        var offset = (long)(rangeTicks * sample);

        return min + TimeSpan.FromTicks(offset);
    }

    private double NextDouble() => this.random.NextDouble();
}

file static class ChaosStreamUtils
{
    public static void ApplyBitFlips<T>(Random random, Span<T> data, int flips) where T : unmanaged
    {
        if (data.IsEmpty || flips <= 0)
            return;

        var span = MemoryMarshal.AsBytes(data);
        var bitCount = span.Length * 8;

        for (var i = 0; i < flips; i++)
        {
            var bitIndex = NextInt32(random, 0, bitCount);
            var byteIndex = bitIndex / 8;
            var bitOffset = bitIndex % 8;

            span[byteIndex] ^= (byte)(1 << bitOffset);
        }
    }

    public static int NextInt32(Random random, int minValue, int maxValue) => minValue >= maxValue ? minValue : random.Next(minValue, maxValue);
}
