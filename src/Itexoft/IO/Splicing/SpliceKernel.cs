// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Splicing;

internal static unsafe class SpliceKernel
{
    internal static SpliceNativeStatus In<T>(nint runtime, out Clip<T> result) where T : unmanaged
    {
        var status = SpliceStreamRuntime.In(runtime.SpliceStreamRuntimeHandle(), out var node);

        if (status != SpliceNativeStatus.Ok)
        {
            result = default;

            return status;
        }

        result = new Clip<T>((nint)node);

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Cue<T>(nint runtime, out Clip<T> result) where T : unmanaged
    {
        var status = SpliceStreamRuntime.Cue(runtime.SpliceStreamRuntimeHandle(), out var node);

        if (status != SpliceNativeStatus.Ok)
        {
            result = default;

            return status;
        }

        result = new Clip<T>((nint)node);

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Port<T>(nint runtime, out Clip<T> result) where T : unmanaged
    {
        var status = SpliceStreamRuntime.Port(runtime.SpliceStreamRuntimeHandle(), out var node);

        if (status != SpliceNativeStatus.Ok)
        {
            result = default;

            return status;
        }

        result = new Clip<T>((nint)node);

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Empty<T>(out Clip<T> result) where T : unmanaged
    {
        result = default;

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Insert<T>(nint runtime, ReadOnlySpan<T> data, out Clip<T> result) where T : unmanaged
    {
        if (data.IsEmpty)
        {
            result = default;

            return SpliceNativeStatus.Ok;
        }

        fixed (T* dataPtr = &data.GetPinnableReference())
        {
            var status = SpliceStreamRuntime.Insert(runtime.SpliceStreamRuntimeHandle(), (nint)dataPtr, data.Length, out var node);

            if (status != SpliceNativeStatus.Ok)
            {
                result = default;

                return status;
            }

            result = new Clip<T>((nint)node);

            return SpliceNativeStatus.Ok;
        }
    }

    internal static SpliceNativeStatus Item<T>(nint runtime, in T value, out Clip<T> result) where T : unmanaged
    {
        Span<T> single = stackalloc T[1];
        single[0] = value;

        return Insert(runtime, single, out result);
    }

    internal static SpliceNativeStatus Render<T>(nint runtime, Clip<T> src, out Clip<T> clip) where T : unmanaged
    {
        if (src.Token == 0)
        {
            clip = default;

            return SpliceNativeStatus.Ok;
        }

        var status = SpliceStreamRuntime.EnsureOwner(runtime.SpliceStreamRuntimeHandle(), src.Token);

        if (status != SpliceNativeStatus.Ok)
        {
            clip = default;

            return status;
        }

        status = SpliceStreamRuntime.Freeze(runtime.SpliceStreamRuntimeHandle(), src.Token, out var result);

        if (status != SpliceNativeStatus.Ok)
        {
            clip = default;

            return status;
        }

        clip = new Clip<T>(result);

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Cat<T>(nint runtime, ReadOnlySpan<Clip<T>> parts, out Clip<T> clip) where T : unmanaged
    {
        if (parts.IsEmpty)
        {
            clip = default;

            return SpliceNativeStatus.Ok;
        }

        fixed (Clip<T>* partsPtr = &parts.GetPinnableReference())
        {
            var status2 = SpliceStreamRuntime.Cat(runtime.SpliceStreamRuntimeHandle(), (nint*)partsPtr, parts.Length, out var result);

            if (status2 != SpliceNativeStatus.Ok)
            {
                clip = default;

                return status2;
            }

            clip = new Clip<T>(result);

            return SpliceNativeStatus.Ok;
        }
    }

    internal static SpliceStreamOptions Options(nint runtime)
    {
        if ((nint)runtime == 0)
            return default;

        var native = (SpliceStreamOptionsNative*)runtime.SpliceStreamRuntimeHandle();

        return new SpliceStreamOptions
        {
            Backpressure = native->Backpressure,
            UnboundCue = native->UnboundCue,
            MaxPromiseLength = native->MaxPromiseLength,
        };
    }

    internal static SpliceNativeStatus Concat<T>(Clip<T> a, Clip<T> b, out Clip<T> clip) where T : unmanaged
    {
        if (a.Token == 0)
        {
            clip = b;

            return SpliceNativeStatus.Ok;
        }

        if (b.Token == 0)
        {
            clip = a;

            return SpliceNativeStatus.Ok;
        }

        var status = SpliceStreamRuntime.GetRuntime(a.Token, out var runtime);

        if (status != SpliceNativeStatus.Ok)
        {
            clip = default;

            return status;
        }

        status = SpliceStreamRuntime.Concat(runtime, a.Token, b.Token, out var result);

        if (status != SpliceNativeStatus.Ok)
        {
            clip = default;

            return status;
        }

        clip = new Clip<T>(result);

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Freeze<T>(Clip<T> x, out Clip<T> clip) where T : unmanaged
    {
        if (x.Token == 0)
        {
            clip = x;

            return SpliceNativeStatus.Ok;
        }

        var status = SpliceStreamRuntime.GetRuntime(x.Token, out var runtime);

        if (status != SpliceNativeStatus.Ok)
        {
            clip = default;

            return status;
        }

        status = SpliceStreamRuntime.Freeze(runtime, x.Token, out var result);

        if (status != SpliceNativeStatus.Ok)
        {
            clip = default;

            return status;
        }

        clip = new Clip<T>(result);

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Append<T>(ref Clip<T> promise, ReadOnlySpan<T> chunk) where T : unmanaged
    {
        if (chunk.IsEmpty)
            return SpliceNativeStatus.Ok;

        var status = SpliceStreamRuntime.GetRuntime(promise.Token, out var runtime);

        if (status != SpliceNativeStatus.Ok)
            return status;

        fixed (T* chunkPtr = &chunk.GetPinnableReference())
            return SpliceStreamRuntime.AppendChunk<T>(runtime, promise.Token, (nint)chunkPtr, chunk.Length);
    }

    internal static SpliceNativeStatus Append<T>(ref Clip<T> promise, in T item) where T : unmanaged
    {
        var status = SpliceStreamRuntime.GetRuntime(promise.Token, out var runtime);

        if (status != SpliceNativeStatus.Ok)
            return status;

        fixed (T* ptr = &item)
            return SpliceStreamRuntime.AppendItem(runtime, promise.Token, (nint)ptr);
    }

    internal static SpliceNativeStatus Append<T>(ref Clip<T> promise, Clip<T> piece) where T : unmanaged
    {
        if (piece.Token == 0)
            return SpliceNativeStatus.Ok;

        var status = SpliceStreamRuntime.GetRuntime(promise.Token, out var runtime);

        if (status != SpliceNativeStatus.Ok)
            return status;

        status = SpliceStreamRuntime.AppendStream(runtime, promise.Token, piece.Token);

        if (status != SpliceNativeStatus.Ok)
            return status;

        return SpliceNativeStatus.Ok;
    }

    internal static SpliceNativeStatus Bind<T>(ref Clip<T> hole, Clip<T> src) where T : unmanaged
    {
        var status = SpliceStreamRuntime.GetRuntime(hole.Token, out var runtime);

        if (status != SpliceNativeStatus.Ok)
            return status;

        return SpliceStreamRuntime.Bind(runtime, hole.Token, src.Token);
    }

    internal static SpliceNativeStatus Complete<T>(ref Clip<T> promise) where T : unmanaged
    {
        var status = SpliceStreamRuntime.GetRuntime(promise.Token, out var runtime);

        if (status != SpliceNativeStatus.Ok)
            return status;

        return SpliceStreamRuntime.Complete(runtime, promise.Token);
    }

    internal static SpliceNativeStatus Read(nint src, nint bytes, int length, out int result)
    {
        if (length == 0 || src == 0)
        {
            result = 0;

            return SpliceNativeStatus.Ok;
        }

        var status = SpliceStreamRuntime.GetRuntime(src, out var runtime);

        if (status != SpliceNativeStatus.Ok)
        {
            result = 0;

            return status;
        }

        return SpliceStreamRuntime.Read(runtime, src, bytes, length, out result);
    }
}
