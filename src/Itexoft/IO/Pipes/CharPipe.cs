// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Threading.Channels;
using Itexoft.Core;
using Itexoft.Threading.Tasks;

namespace Itexoft.IO.Pipes;

public sealed class CharPipe : IDisposable
{
    private readonly Channel<char> channel;
    private Disposed disposed = new();

    public CharPipe(int capacity)
    {
        this.channel = Channel.CreateBounded<char>(
            new BoundedChannelOptions(capacity) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });

        this.Reader = new CharPipeReader(this.channel.Reader);
        this.Writer = new CharPipeWriter(this.channel.Writer);
    }

    public IStreamR<char> Reader { get; }
    public IStreamW<char> Writer { get; }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        try
        {
            this.channel.Writer.TryComplete();
        }
        finally
        {
            this.Reader.Dispose();
            this.Writer.Dispose();
        }
    }

    public void Complete() => this.channel.Writer.TryComplete();

    private sealed class CharPipeReader(ChannelReader<char> reader) : StreamBase<char>, IStreamR<char>
    {
        private readonly ChannelReader<char> reader = reader;

        public int Read(Span<char> destination)
        {
            this.ThrowIfDisposed();

            if (destination.IsEmpty)
                return 0;

            var written = 0;

            while (written < destination.Length)
            {
                if (this.reader.TryRead(out var ch))
                {
                    destination[written++] = ch;

                    continue;
                }

                if (written > 0)
                    break;

                if (!this.reader.WaitToReadAsync().GetAwaiter().GetResult())
                    break;
            }

            return written;
        }

        protected override StackTask DisposeAny() => default;
    }

    private sealed class CharPipeWriter(ChannelWriter<char> writer) : StreamBase<char>, IStreamW<char>
    {
        public void Flush() { }

        public void Write(ReadOnlySpan<char> source)
        {
            this.ThrowIfDisposed();

            if (source.IsEmpty)
                return;

            foreach (var ch in source)
                this.WriteChar(ch);
        }

        private void WriteChar(char value)
        {
            if (writer.TryWrite(value))
                return;

            writer.WriteAsync(value).GetAwaiter().GetResult();
        }

        protected override StackTask DisposeAny() => default;
    }
}
