// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Splicing;

public readonly struct Clip<T> where T : unmanaged
{
    public readonly nint Token;

    internal Clip(nint token) => this.Token = token;

    public bool IsDefault => this.Token == 0;
}

public readonly ref struct Cut<T> where T : unmanaged
{
    private readonly nint rt;

    internal Cut(nint runtime) => this.rt = runtime;

    public Clip<T> In
    {
        get
        {
            var status = SpliceKernel.In<T>(this.rt, out var result);

            if (status != SpliceNativeStatus.Ok)
                throw new SpliceNativeException(status);

            return result;
        }
    }

    public Clip<T> Cue()
    {
        var status = SpliceKernel.Cue<T>(this.rt, out var result);

        if (status != SpliceNativeStatus.Ok)
            throw new SpliceNativeException(status);

        return result;
    }

    public Clip<T> Port()
    {
        var status = SpliceKernel.Port<T>(this.rt, out var result);

        if (status != SpliceNativeStatus.Ok)
            throw new SpliceNativeException(status);

        return result;
    }

    public Clip<T> Empty
    {
        get
        {
            var status = SpliceKernel.Empty<T>(out var result);

            if (status != SpliceNativeStatus.Ok)
                throw new SpliceNativeException(status);

            return result;
        }
    }

    public Clip<T> Insert(ReadOnlySpan<T> data)
    {
        var status = SpliceKernel.Insert(this.rt, data, out var result);

        if (status != SpliceNativeStatus.Ok)
            throw new SpliceNativeException(status);

        return result;
    }

    public Clip<T> Item(in T value)
    {
        var status = SpliceKernel.Item(this.rt, value, out var result);

        if (status != SpliceNativeStatus.Ok)
            throw new SpliceNativeException(status);

        return result;
    }

    public Clip<T> Render(Clip<T> src)
    {
        var status = SpliceKernel.Render(this.rt, src, out var result);

        if (status != SpliceNativeStatus.Ok)
            throw new SpliceNativeException(status);

        return result;
    }

    public Clip<T> Cat(ReadOnlySpan<Clip<T>> parts)
    {
        var status = SpliceKernel.Cat(this.rt, parts, out var result);

        if (status != SpliceNativeStatus.Ok)
            throw new SpliceNativeException(status);

        return result;
    }

    public SpliceStreamOptions Options => SpliceKernel.Options(this.rt);
}