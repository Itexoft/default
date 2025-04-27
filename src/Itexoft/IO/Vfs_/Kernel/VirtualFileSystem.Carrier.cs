// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private sealed class CarrierStream(IStreamRwsl<byte> inner) : IStreamRwsl<byte>
    {
        private readonly IStreamRwsl<byte> inner = inner ?? throw new ArgumentNullException(nameof(inner));
        private long publishedLength = inner.Length;

        public IStreamRwsl<byte> Inner => this.inner;

        public long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        public long Length
        {
            get => this.publishedLength;
            set
            {
                if (value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value));

                if (value > this.inner.Length)
                    this.inner.Length = value;

                this.publishedLength = value;
            }
        }

        public int Read(Span<byte> span, CancelToken cancelToken = default) => this.inner.Read(span, cancelToken);

        public void Write(ReadOnlySpan<byte> buffer, CancelToken cancelToken = default) => this.inner.Write(buffer, cancelToken);

        public void Flush(CancelToken cancelToken = default) => this.inner.Flush(cancelToken);

        public void Dispose()
        {
        }

        public void SetPublishedLength(long value) => this.publishedLength = value;

        public void TrimRawLength(long value)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(nameof(value));

            this.inner.Length = value;
            this.publishedLength = value;
        }
    }
}
