// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.IO;

public interface IStream;

public interface IStream<T> : IStream where T : unmanaged;

public interface IStreamW : IStream
{
    void Flush(CancelToken cancelToken = default);
}

public interface IStreamR<T> : IStream<T>, IDisposable where T : unmanaged
{
    int Read(Span<T> span, CancelToken cancelToken = default);
}

public interface IStreamW<T> : IStreamW, IStream<T>, IDisposable where T : unmanaged
{
    void Write(ReadOnlySpan<T> buffer, CancelToken cancelToken = default);
    public void Write(T value, CancelToken cancelToken = default) => this.Write(stackalloc T[] { value }, cancelToken);
}

public interface IStreamRw<T> : IStreamW<T>, IStreamR<T> where T : unmanaged;

public interface IStreamRs<T> : IStreamR<T>, IStreamSeek where T : unmanaged { }

public interface IStreamWs<T> : IStreamW<T>, IStreamSeek where T : unmanaged;

public interface IStreamRws<T> : IStreamRw<T>, IStreamWs<T>, IStreamRs<T> where T : unmanaged;

internal interface IStreamRl<T> : IStreamR<T>, IStreamLength where T : unmanaged;

public interface IStreamLength : IStream
{
    long Length { get; }

    long Position { get; }
}

public interface IStreamSeek : IStreamLength
{
    new long Position { get; set; }
}

#region BCL

public interface IStreamBcl : IStreamRws<byte>
{
    bool CanRead { get; }
    bool CanSeek { get; }
    bool CanWrite { get; }
    bool CanTimeout { get; }
}

#endregion
