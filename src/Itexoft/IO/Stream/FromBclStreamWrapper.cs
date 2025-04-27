// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;
#if !NativeAOT
using Itexoft.Net.Core;
#endif

namespace Itexoft.IO;

file class BclStreamRa(Stream stream) : StreamWrapper(stream), IStreamRa
{
    public async StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            return await this.BclStream.ReadAsync(buffer, token);
    }

    protected override StackTask DisposeAny() => default;
}

file class BclStreamRwa(Stream stream) : BclStreamRa(stream), IStreamRwa
{
    public StackTask FlushAsync(CancelToken cancelToken = default) => this.BclStream.FlushAsync();

    public async StackTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            await this.BclStream.WriteAsync(buffer, token);
    }
}

file class BclNetStreamRa(Stream stream) : BclStreamRa(stream)
#if !NativeAOT
    , INetStream
#endif
{
    public StackTask FlushAsync(CancelToken cancelToken = default) => this.BclStream.FlushAsync();

    public async StackTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        using (cancelToken.Bridge(out var token))
            await this.BclStream.WriteAsync(buffer, token);
    }

    public TimeSpan WriteTimeout
    {
        get => TimeSpan.FromMilliseconds(this.BclStream.WriteTimeout);
        set => this.BclStream.WriteTimeout = value.TimeoutMilliseconds;
    }

    public TimeSpan ReadTimeout
    {
        get => TimeSpan.FromMilliseconds(this.BclStream.ReadTimeout);
        set => this.BclStream.ReadTimeout = value.TimeoutMilliseconds;
    }
}

file class BclStreamRal(Stream stream) : BclStreamRa(stream), IStreamRal
{
    public long Length => this.BclStream.Length;
    public long Position => this.BclStream.Position;
}

file class BclStreamRals(Stream stream) : BclStreamRal(stream), IStreamRals
{
    public long Seek(long offset, SeekOrigin origin) => this.BclStream.Seek(offset, origin);
}

public static partial class BclStreamWrapperExtensions
{
    public static IStreamRa AsStreamRa(this Stream stream) => new BclStreamRa(stream);
    public static IStreamRwa AsStreamRwa(this Stream stream) => new BclStreamRwa(stream);
    public static IStreamRal AsStreamRal(this Stream stream) => new BclStreamRal(stream);
    public static IStreamRals AsStreamRals(this Stream stream) => new BclStreamRals(stream);
#if !NativeAOT
    public static INetStream AsNetStream(this System.Net.Sockets.NetworkStream stream) => new BclNetStreamRa(stream);
#endif
}
