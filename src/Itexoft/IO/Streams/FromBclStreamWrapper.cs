// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Threading;

namespace Itexoft.IO;

file class BclStreamRa(System.IO.Stream stream, bool ownStream) : StreamWrapper(stream, ownStream), IStreamR<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        return this.BclStream.Read(buffer);
    }
}

file class BclStreamRwa(System.IO.Stream stream, bool ownStream) : BclStreamRa(stream, ownStream), IStreamRw<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Write(buffer);
    }
}

file class BclStreamRas(System.IO.Stream stream, bool ownStream) : BclStreamRa(stream, ownStream), IStreamRs<byte>
{
    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
    }
}

file class BclStreamRwas(System.IO.Stream stream, bool ownStream) : BclStreamRwa(stream, ownStream), IStreamRws<byte>
{
    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }

    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
    }
}

public static partial class BclStreamWrapperExtensions
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamR<byte> AsAstreamR(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRa(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamW<byte> AsAstreamW(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRwa(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamWs<byte> AsAstreamWs(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRwas(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRw<byte> AsAstreamRw(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRwa(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRs<byte> AsAstreamRs(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRas(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRws<byte> AsAstreamRws(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRwas(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamR<byte> AsSstreamR(this System.IO.Stream stream, bool ownStream = true) => new BclStreamR(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRw<byte> AsSstreamRw(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRw(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRs<byte> AsSstreamRs(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRs(stream, ownStream);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static IStreamRws<byte> AsSstreamRws(this System.IO.Stream stream, bool ownStream = true) => new BclStreamRws(stream, ownStream);
}

file class BclStreamR(System.IO.Stream stream, bool ownStream) : StreamWrapper(stream, ownStream), IStreamR<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Read(Span<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        return this.BclStream.Read(buffer);
    }
}

file class BclStreamRw(System.IO.Stream stream, bool ownStream) : BclStreamR(stream, ownStream), IStreamRw<byte>
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Flush(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Flush();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.BclStream.Write(buffer);
    }
}

file class BclStreamRs(System.IO.Stream stream, bool ownStream) : BclStreamR(stream, ownStream), IStreamRs<byte>
{
    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
    }

    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }
}

file class BclStreamRws(System.IO.Stream stream, bool ownStream) : BclStreamRw(stream, ownStream), IStreamRws<byte>
{
    public long Length
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Length;
    }

    public long Position
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.BclStream.Position;
        set => this.BclStream.Position = value;
    }
}
