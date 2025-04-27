// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.AI.OpenAI;
using Itexoft.AI.OpenAI.Models;
using Itexoft.Core;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.AI.Server.OpenAi;

internal sealed class OpenAiServerSseStream(IEnumerable<OpenAiChatCompletionResponseDelta> deltas, IStreamRw<char> ownedStream) : IStreamR<byte>
{
    private static readonly byte[] doneFrame = "data: [DONE]\n\n"u8.ToArray();
    private static readonly byte[] framePrefix = "data: "u8.ToArray();
    private static readonly byte[] frameSuffix = "\n\n"u8.ToArray();

    private readonly IEnumerator<OpenAiChatCompletionResponseDelta> enumerator = deltas.GetEnumerator();
    private readonly IStreamRw<char> ownedStream = ownedStream;
    private Disposed disposed = new();
    private bool finished;
    private byte[] pending = [];
    private int pendingOffset;
    private bool sentDone;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.pending = [];
        this.pendingOffset = 0;
        this.enumerator.Dispose();
        this.ownedStream.Dispose();
    }

    public int Read(Span<byte> buffer, CancelToken cancelToken = default)
    {
        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            cancelToken.ThrowIf();

            if (this.TryReadPending(buffer, out var read))
                return read;

            if (this.disposed)
                return 0;

            if (this.finished)
            {
                if (this.sentDone)
                    return 0;

                this.sentDone = true;
                this.Queue(doneFrame);

                continue;
            }

            if (!this.TryQueueNextFrame())
                this.finished = true;
        }
    }

    private bool TryQueueNextFrame()
    {
        try
        {
            if (!this.enumerator.MoveNext())
                return false;

            var payload = OpenAiJson.Serialize(this.enumerator.Current);
            var frame = new byte[framePrefix.Length + payload.Length + frameSuffix.Length];
            framePrefix.CopyTo(frame, 0);
            payload.CopyTo(frame, framePrefix.Length);
            frameSuffix.CopyTo(frame, framePrefix.Length + payload.Length);
            this.Queue(frame);

            return true;
        }
        catch
        {
            return false;
        }
    }

    private void Queue(byte[] buffer)
    {
        this.pending = buffer;
        this.pendingOffset = 0;
    }

    private bool TryReadPending(Span<byte> buffer, out int read)
    {
        if (this.pendingOffset >= this.pending.Length)
        {
            read = 0;

            return false;
        }

        read = Math.Min(buffer.Length, this.pending.Length - this.pendingOffset);
        this.pending.AsSpan(this.pendingOffset, read).CopyTo(buffer);
        this.pendingOffset += read;

        if (this.pendingOffset >= this.pending.Length)
        {
            this.pending = [];
            this.pendingOffset = 0;
        }

        return true;
    }
}
