// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using Itexoft.Core;
using Itexoft.IO.Streams.Chars;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI;

internal sealed class OpenAiSseLineReader(CharStreamBr stream8) : IDisposable
{
    private const int defaultBufferSize = 2048;
    private char[] buffer = ArrayPool<char>.Shared.Rent(defaultBufferSize);
    private int bufferCount;
    private int bufferOffset;
    private Disposed disposed = new();

    private CharStreamBr reader = stream8;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        if (this.buffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(this.buffer);
            this.buffer = [];
            this.bufferCount = 0;
            this.bufferOffset = 0;
        }

        this.reader.Dispose();
    }

    public string? ReadLine(CancelToken cancelToken = default)
    {
        this.disposed.ThrowIf();
        cancelToken.ThrowIf();

        char[]? lineBuffer = null;
        var lineLength = 0;

        try
        {
            while (true)
            {
                if (this.bufferOffset == this.bufferCount)
                {
                    this.bufferCount = this.reader.Read(this.buffer, cancelToken);
                    this.bufferOffset = 0;

                    if (this.bufferCount == 0)
                    {
                        if (lineLength == 0)
                            return null;

                        return new string(lineBuffer!, 0, lineLength);
                    }
                }

                var chunk = this.buffer.AsSpan(this.bufferOffset, this.bufferCount - this.bufferOffset);
                var newlineIndex = chunk.IndexOfAny('\r', '\n');

                if (newlineIndex < 0)
                {
                    AppendChunk(ref lineBuffer, ref lineLength, chunk);
                    this.bufferOffset = this.bufferCount;

                    continue;
                }

                if (newlineIndex > 0)
                    AppendChunk(ref lineBuffer, ref lineLength, chunk[..newlineIndex]);

                this.bufferOffset += newlineIndex + 1;

                if (chunk[newlineIndex] == '\r')
                    this.TryConsumeLfAfterCr(cancelToken);

                if (lineLength == 0)
                    return string.Empty;

                return new string(lineBuffer!, 0, lineLength);
            }
        }
        finally
        {
            if (lineBuffer is not null)
                ArrayPool<char>.Shared.Return(lineBuffer);
        }
    }

    private void TryConsumeLfAfterCr(CancelToken cancelToken)
    {
        if (this.bufferOffset < this.bufferCount)
        {
            if (this.buffer[this.bufferOffset] == '\n')
                this.bufferOffset++;

            return;
        }

        this.bufferCount = this.reader.Read(this.buffer, cancelToken);
        this.bufferOffset = 0;

        if (this.bufferCount > 0 && this.buffer[0] == '\n')
            this.bufferOffset = 1;
    }

    private static void AppendChunk(ref char[]? lineBuffer, ref int lineLength, ReadOnlySpan<char> chunk)
    {
        if (chunk.IsEmpty)
            return;

        var required = lineLength + chunk.Length;

        if (lineBuffer is null)
            lineBuffer = ArrayPool<char>.Shared.Rent(Math.Max(256, required));
        else if (lineBuffer.Length < required)
        {
            var rented = ArrayPool<char>.Shared.Rent(Math.Max(required, lineBuffer.Length * 2));
            lineBuffer.AsSpan(0, lineLength).CopyTo(rented);
            ArrayPool<char>.Shared.Return(lineBuffer);
            lineBuffer = rented;
        }

        chunk.CopyTo(lineBuffer.AsSpan(lineLength));
        lineLength = required;
    }
}
