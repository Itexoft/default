// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO.FileSystem;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    internal IStreamRwsl<byte> OpenCarrierView(string path, SysFileMode mode)
    {
        using var hold = this.apiSync.Enter();
        this.ThrowIfDisposed();
        var normalized = NormalizePath(path);

        if (!TryGetNodeKind(normalized, out var kind) || kind != NodeKind.File)
            throw new FileNotFoundException($"File '{path}' was not found.", path);

        return new CarrierFileView(this, normalized, this.ResolveOpenFileInodeId(normalized), mode);
    }

    private void FlushMutationCarrier(IStreamRwsl<byte> stream)
    {
        if (TryGetCarrierFileView(stream, out _))
            return;

        FlushCarrier(stream);
    }

    private void FlushBoundaryCarrier(IStreamRwsl<byte> stream)
    {
        if (stream is CarrierStream wrapper && wrapper.Inner is CarrierFileView wrapped)
        {
            wrapped.SynchronizeVisibleLength(wrapper.Length);
            wrapped.FlushBoundary();
            return;
        }

        if (!TryGetCarrierFileView(stream, out var carrierView))
        {
            FlushCarrier(stream);
            return;
        }

        carrierView!.FlushBoundary();
    }

    private static bool TryGetCarrierFileView(IStreamRwsl<byte> stream, out CarrierFileView? carrierView)
    {
        if (stream is CarrierFileView direct)
        {
            carrierView = direct;
            return true;
        }

        if (stream is CarrierStream wrapper && wrapper.Inner is CarrierFileView wrapped)
        {
            carrierView = wrapped;
            return true;
        }

        carrierView = null;
        return false;
    }

    private sealed class CarrierFileView(VirtualFileSystem owner, string path, long inodeId, SysFileMode mode) : IStreamRwsl<byte>, IPositionalByteStream
    {
        private readonly SysFileMode mode = mode;
        private readonly FileDeltaView view = new(owner, path, inodeId);

        public long Position { get; set; }

        public long Length
        {
            get => this.view.GetLength();
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                if (!this.mode.HasFlag(SysFileMode.Write) && !this.mode.HasFlag(SysFileMode.Overwrite))
                    throw new NotSupportedException("This carrier view was not opened for writing.");

                this.view.SetLength(value);

                if (this.Position > value)
                    this.Position = value;
            }
        }

        public int Read(Span<byte> span, CancelToken cancelToken = default)
        {
            cancelToken.ThrowIf();

            if (!this.mode.HasFlag(SysFileMode.Read))
                throw new NotSupportedException("This carrier view was not opened for reading.");

            var read = this.view.Read(this.Position, span, in cancelToken);
            this.Position += read;
            return read;
        }

        public int ReadAt(long offset, Span<byte> destination)
        {
            if (!this.mode.HasFlag(SysFileMode.Read))
                throw new NotSupportedException("This carrier view was not opened for reading.");

            return this.view.Read(offset, destination);
        }

        public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default)
        {
            cancelToken.ThrowIf();

            if (!this.mode.HasFlag(SysFileMode.Write) && !this.mode.HasFlag(SysFileMode.Overwrite))
                throw new NotSupportedException("This carrier view was not opened for writing.");

            this.view.Write(this.Position, buffer, in cancelToken);
            this.Position += buffer.Length;
        }

        public void WriteAt(long offset, ReadOnlySpan<byte> source)
        {
            if (!this.mode.HasFlag(SysFileMode.Write) && !this.mode.HasFlag(SysFileMode.Overwrite))
                throw new NotSupportedException("This carrier view was not opened for writing.");

            this.view.Write(offset, source);
        }

        public void Flush(CancelToken cancelToken = default)
        {
            cancelToken.ThrowIf();
            this.view.ThrowIfDisposed(in cancelToken);
        }

        internal void SynchronizeVisibleLength(long value) => this.view.SetLength(value);

        internal void FlushBoundary(CancelToken cancelToken = default)
            => this.view.Flush(in cancelToken);

        public void Dispose()
            => this.FlushBoundary();
    }
}
