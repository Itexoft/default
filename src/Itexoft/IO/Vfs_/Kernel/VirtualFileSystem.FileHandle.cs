// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.IO.FileSystem;
using Itexoft.Threading;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private struct FileHandle(VirtualFileSystem owner, string path, long inodeId, SysFileMode mode) : IStreamRwsl<byte>
    {
        private readonly SysFileMode mode = mode;
        private readonly FileDeltaView view = new(owner, path, inodeId);
        private long position;

        public long Position
        {
            readonly get => this.position;
            set
            {
                this.view.ThrowIfDisposed();

                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                this.position = value;
            }
        }

        public long Length
        {
            get => this.view.GetLength();
            set
            {
                EnsureWritable(this.mode);
                this.view.SetLength(value);

                if (this.position > value)
                    this.position = value;
            }
        }

        public void Dispose()
            => this.view.Dispose();

        public int Read(Span<byte> span, CancelToken cancelToken = default)
        {
            EnsureReadable(this.mode);
            var read = this.view.Read(this.position, span, in cancelToken);
            this.position += read;
            return read;
        }

        public void Flush(CancelToken cancelToken = default)
            => this.view.Flush(in cancelToken);

        public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default)
        {
            EnsureWritable(this.mode);
            this.view.Write(this.position, buffer, in cancelToken);
            this.position += buffer.Length;
        }

        private static void EnsureReadable(SysFileMode mode)
        {
            if (!mode.HasFlag(SysFileMode.Read))
                throw new NotSupportedException("This stream was not opened for reading.");
        }

        private static void EnsureWritable(SysFileMode mode)
        {
            if (!mode.HasFlag(SysFileMode.Write) && !mode.HasFlag(SysFileMode.Overwrite))
                throw new NotSupportedException("This stream was not opened for writing.");
        }
    }
}
