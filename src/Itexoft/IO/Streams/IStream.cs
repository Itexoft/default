// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.IO;

public interface IStream;

public interface IStream<T> : IStream;

public interface IStreamW : IStream
{
    void Flush(CancelToken cancelToken = default);
}

public interface IStreamR<T> : IStream<T>, IDisposable
{
    int Read(Span<T> span, CancelToken cancelToken = default);
}

public interface IStreamW<T> : IStreamW, IStream<T>, IDisposable
{
    void Write(ReadOnlySpan<T> buffer, CancelToken cancelToken = default);
}

public interface IStreamRw<T> : IStreamW<T>, IStreamR<T>;

public interface IStreamRs<T> : IStreamR<T>, IStreamSeek;

public interface IStreamWs<T> : IStreamW<T>, IStreamSeek;

public interface IStreamRws<T> : IStreamRw<T>, IStreamWs<T>, IStreamRs<T>;

public interface IStreamRwsl<T> : IStreamRws<T>
{
    new long Length { get; set; }
    long IStreamSeek.Length => this.Length;
}

public interface IStreamSeek : IStream
{
    long Position { get; set; }
    long Length { get; }
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
