// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;
using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.IO.Streams;

public unsafe sealed class PipeStream<T> : IStreamRw<T> where T : unmanaged
{
    private int access;
    private AtomicState64<PipeStreamState> state;
    private nint buffer;
    private int capacity;
    private Disposed disposed = new();

    public PipeStream(int valve = 0)
    {
        if (valve > 0)
            this.Valve = valve;
    }

    public int Valve
    {
        get => this.state.Read().Valve;
        set
        {
            if (value is < 0 or > ushort.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value));

            this.disposed.ThrowIf();
            this.ResizeValve((ushort)value);
        }
    }

    public int Read(Span<T> span, CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf(in cancelToken);

        if (span.IsEmpty)
            return 0;

        var read = 0;
        for (var wi = 0;;)
        {
            this.Enter(State.Reading, in cancelToken);

            var wait = false;
            try
            {
                var snapshot = this.state.Read();

                if (snapshot.Tank == 0)
                {
                    if (snapshot.Valve == 0 || read != 0)
                        return read;

                    wait = true;
                }
                else
                {
                    var chunk = Math.Min(span.Length - read, Math.Min(snapshot.Tank, this.capacity - snapshot.Head));
                    new ReadOnlySpan<T>((T*)Atomic.Read(ref this.buffer) + snapshot.Head, chunk).CopyTo(span[read..]);

                    var desired = snapshot;
                    var head = desired.Head + chunk;
                    desired.Head = (ushort)(head == this.capacity ? 0 : head);
                    desired.Tank = (ushort)(desired.Tank - chunk);

                    if (this.state.TryWrite(desired, snapshot))
                    {
                        read += chunk;
                        wi = 0;

                        if (read == span.Length)
                            return read;
                    }
                }
            }
            finally
            {
                this.Exit(State.Reading);
            }

            if (!wait)
                continue;

            Spin.Wait(ref wi);
        }
    }

    public void Write(ReadOnlySpan<T> buffer, CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf(in cancelToken);
        var written = 0;

        while (written < buffer.Length)
        {
            for (var wi = 0;;)
            {
                this.Enter(State.Writing, in cancelToken);

                var wait = false;
                try
                {
                    var snapshot = this.state.Read();

                    if (snapshot.Space == 0)
                    {
                        wait = true;
                    }
                    else
                    {
                        var chunk = Math.Min(buffer.Length - written, Math.Min(snapshot.Space, this.capacity - snapshot.Tail));
                        buffer.Slice(written, chunk).CopyTo(new Span<T>((T*)Atomic.Read(ref this.buffer) + snapshot.Tail, chunk));

                        var desired = snapshot;
                        var tail = desired.Tail + chunk;
                        desired.Tail = (ushort)(tail == this.capacity ? 0 : tail);
                        desired.Tank = (ushort)(desired.Tank + chunk);

                        if (this.state.TryWrite(desired, snapshot))
                        {
                            written += chunk;
                            break;
                        }
                    }
                }
                finally
                {
                    this.Exit(State.Writing);
                }

                if (!wait)
                    continue;

                Spin.Wait(ref wi);
            }
        }
    }

    public void Flush(CancelToken cancelToken = default) => this.disposed.ThrowIf(in cancelToken);

    public void Flush(bool force, CancelToken cancelToken = default) => this.Flush(cancelToken);

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.BeginResize(false);
        try
        {
            var buffer = Interlocked.Exchange(ref this.buffer, nint.Zero);
            if (buffer != 0)
                NativeMemory.Free((void*)buffer);

            this.capacity = 0;
            this.state.Write(default);
        }
        finally
        {
            Atomic.Write(ref this.access, 0);
        }
    }

    private void ResizeValve(ushort valve)
    {
        this.BeginResize(true);
        try
        {
            var snapshot = this.state.Read();

            var desiredCapacity = Math.Max(snapshot.Tank, (int)valve);
            if (desiredCapacity != this.capacity)
            {
                var scratch = CopyLogicalData((T*)Atomic.Read(ref this.buffer), this.capacity, snapshot.Head, snapshot.Tank);
                try
                {
                    var resized = Resize((void*)Atomic.Read(ref this.buffer), desiredCapacity);
                    Interlocked.Exchange(ref this.buffer, (nint)resized);
                    this.capacity = desiredCapacity;

                    if (snapshot.Tank != 0)
                        new ReadOnlySpan<T>(scratch, snapshot.Tank).CopyTo(new Span<T>(resized, snapshot.Tank));
                }
                finally
                {
                    if (scratch != null)
                        NativeMemory.Free(scratch);
                }

                snapshot.Head = 0;
                snapshot.Tail = snapshot.Tank == desiredCapacity ? (ushort)0 : snapshot.Tank;
            }

            snapshot.Valve = valve;
            this.state.Write(snapshot);
        }
        finally
        {
            Atomic.Write(ref this.access, 0);
        }
    }

    private void Enter(State state, in CancelToken cancelToken)
    {
        for (var wi = 0;; wi++)
        {
            this.disposed.ThrowIf(in cancelToken);

            var snapshot = Atomic.Read(ref this.access);
            if ((snapshot & ((int)state | (int)State.Resizing)) != 0
                || Interlocked.CompareExchange(ref this.access, snapshot | (int)state, snapshot) != snapshot)
            {
                Spin.Wait(ref wi);
                continue;
            }

            if (!this.disposed)
                return;

            Interlocked.And(ref this.access, ~(int)state);
            throw new ObjectDisposedException(nameof(PipeStream<T>));
        }
    }

    private void Exit(State state) => Interlocked.And(ref this.access, ~(int)state);

    private void BeginResize(bool throwIfDisposed)
    {
        for (var wi = 0;; wi++)
        {
            if (throwIfDisposed)
                this.disposed.ThrowIf();

            if ((Interlocked.Or(ref this.access, (int)State.Resizing) & (int)State.Resizing) == 0)
                break;

            Spin.Wait(ref wi);
        }

        for (var wi = 0;; wi++)
        {
            if (throwIfDisposed)
                this.disposed.ThrowIf();

            if ((Atomic.Read(ref this.access) & ((int)State.Reading | (int)State.Writing)) == 0)
                return;

            Spin.Wait(ref wi);
        }
    }

    private static T* CopyLogicalData(T* source, int capacity, int head, int tank)
    {
        if (source == null || tank == 0)
            return null;

        var scratch = (T*)NativeMemory.Alloc(checked((nuint)tank * (nuint)sizeof(T)));
        var firstLength = Math.Min(tank, capacity - head);
        new ReadOnlySpan<T>(source + head, firstLength).CopyTo(new Span<T>(scratch, firstLength));

        if (tank > firstLength)
            new ReadOnlySpan<T>(source, tank - firstLength).CopyTo(new Span<T>(scratch + firstLength, tank - firstLength));

        return scratch;
    }

    private static void* Resize(void* current, int capacity)
    {
        if (capacity == 0)
        {
            if (current != null)
                NativeMemory.Free(current);

            return null;
        }

        var bytes = checked((nuint)capacity * (nuint)sizeof(T));
        return current == null ? NativeMemory.Alloc(bytes) : NativeMemory.Realloc(current, bytes);
    }

    [Flags]
    private enum State
    {
        Reading = 1,
        Writing = 2,
        Resizing = 4,
    }

}

internal struct PipeStreamState
{
    public ulong Raw;

    private const ulong laneMask = ushort.MaxValue;
    private const int tailShift = 16;
    private const int tankShift = 32;
    private const int valveShift = 48;

    public PipeStreamState(ulong raw)
    {
        this.Raw = raw;
    }

    public PipeStreamState(ushort head, ushort tail, ushort tank, ushort valve)
    {
        this.Raw = (ulong)head
            | ((ulong)tail << tailShift)
            | ((ulong)tank << tankShift)
            | ((ulong)valve << valveShift);
    }

    public readonly int Space => this.Valve > this.Tank ? this.Valve - this.Tank : 0;

    public ushort Head
    {
        readonly get => (ushort)this.Raw;
        set => this.Raw = SetLane(this.Raw, 0, value);
    }

    public ushort Tail
    {
        readonly get => (ushort)(this.Raw >> tailShift);
        set => this.Raw = SetLane(this.Raw, tailShift, value);
    }

    public ushort Tank
    {
        readonly get => (ushort)(this.Raw >> tankShift);
        set => this.Raw = SetLane(this.Raw, tankShift, value);
    }

    public ushort Valve
    {
        readonly get => (ushort)(this.Raw >> valveShift);
        set => this.Raw = SetLane(this.Raw, valveShift, value);
    }

    private static ulong SetLane(ulong raw, int shift, ushort value) => (raw & ~(laneMask << shift)) | ((ulong)value << shift);
}
