// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.IO.Splicing;

public delegate Clip<T> SpliceStreamDelegate<T>(ref Cut<T> c) where T : unmanaged;

public abstract unsafe class SpliceStream : IStreamR
{
    private Disposed disposed = new();
    private Latch ended = new();
    private GCHandle optionsHandle;
    private protected nint root;
    private protected nint runtime;
    private GCHandle streamHandle;
    private GCHandle thisHandle;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected SpliceStream(IStreamR stream, SpliceStreamOptions options, int elementSize)
    {
        try
        {
            this.thisHandle = GCHandle.Alloc(stream.Required(), GCHandleType.Normal);
            this.optionsHandle = GCHandle.Alloc(new SpliceStreamOptionsNative(options, elementSize), GCHandleType.Pinned);
            this.streamHandle = GCHandle.Alloc(new SpliceStreamHandle((nint)this.thisHandle, &Read), GCHandleType.Pinned);

            this.runtime = SpliceStreamRuntime.Create(
                (SpliceStreamHandle*)this.streamHandle.AddrOfPinnedObject(),
                (SpliceStreamOptionsNative*)this.optionsHandle.AddrOfPinnedObject());
        }
        catch
        {
            this.Dispose();
            throw;
        }
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> buffer) => this.Read<byte>(MemoryMarshal.AsBytes(buffer));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private protected int Read<T>(Span<T> buffer) where T : unmanaged
    {
        this.disposed.ThrowIf();
        
        fixed (T* dstPtr = &buffer.GetPinnableReference())
        {
            var status = Read(this, (nint)dstPtr, buffer.Length, out var read);

            if (status == SpliceNativeStatus.Disposed)
                throw new ObjectDisposedException(nameof(SpliceStream));
            
            if (status != SpliceNativeStatus.Ok)
                throw new SpliceNativeException(status);

            return read;
        }
    }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        var runtime = this.runtime;
        this.runtime = 0;

        if (runtime != nint.Zero)
            SpliceStreamRuntime.Destroy(runtime.SpliceStreamRuntimeHandle());

        if (this.thisHandle.IsAllocated)
            this.thisHandle.Free();

        if (this.streamHandle.IsAllocated)
            this.streamHandle.Free();

        if (this.optionsHandle.IsAllocated)
            this.optionsHandle.Free();
    }

    [UnmanagedCallersOnly]
    private static SpliceNativeStatus Read(nint src, nint bytes, int length, int* read)
    {
        switch (GCHandle.FromIntPtr(src).Target)
        {
            case SpliceStream stream:
            {
                var status = Read(stream, bytes, length, out var readValue);
                *read = readValue;
                return status;
            }
            case IStreamR stream:
            {
                var status = Read(stream, bytes, length, out var readValue);
                *read = readValue;
                return status;
            }
            default:
                *read = 0;
                return SpliceNativeStatus.RuntimeNull;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SpliceNativeStatus Read(IStreamR stream, nint bytes, int length, out int read)
    {
        try
        {
            read = stream.Read(new Span<byte>((byte*)bytes, length));

            return SpliceNativeStatus.Ok;
        }
        catch (SpliceNativeException ex)
        {
            read = 0;
            
            return ex.Status;
        }
        catch
        {
            read = 0;

            return SpliceNativeStatus.UnknownError;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static SpliceNativeStatus Read(SpliceStream stream, nint bytes, int length, out int read)
    {
        if (stream.disposed)
        {
            read = 0;

            return SpliceNativeStatus.Disposed;
        }

        if (length == 0 || stream.ended)
        {
            read = 0;

            return SpliceNativeStatus.Ok;
        }

        var status = SpliceKernel.Read(stream.root, bytes, length, out read);

        if (status == SpliceNativeStatus.Ok && read <= 0)
            stream.ended.Try();

        return status;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly unsafe struct SpliceStreamHandle(nint context, delegate* unmanaged<nint, nint, int, int*, SpliceNativeStatus> readDelegate)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpliceNativeStatus Read(nint bytes, int length, out int read)
    {
        fixed (int* readPtr = &read)
            return readDelegate(context, bytes, length, readPtr);
    }
}

public sealed class SpliceStream<T> : SpliceStream, IStreamR<T> where T : unmanaged
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpliceStream(IStreamR<T> input, SpliceStreamDelegate<T> build) : this(input, build, new SpliceStreamOptions())
    {
        
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public SpliceStream(IStreamR<T> input, SpliceStreamDelegate<T> build, SpliceStreamOptions options) : base(input, options, Unsafe.SizeOf<T>())
    {
        build.Required();
        
        if(this.runtime == 0)
            throw new ObjectDisposedException(nameof(SpliceStream));
        
        SpliceStreamRuntime.ScopePush(this.runtime);

        try
        {
            var flow = new Cut<T>(this.runtime);
            this.root = build(ref flow).Token;
        }
        finally
        {
            SpliceStreamRuntime.ScopePop(this.runtime);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<T> buffer) => base.Read(buffer);
}
