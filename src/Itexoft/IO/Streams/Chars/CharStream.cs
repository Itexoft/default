// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.IO.Streams.Chars;

public class CharStream(IStreamRwsl<byte> stream, Encoding encoding) : IDisposable
{
    private readonly char[] carry = new char[encoding.Required().GetMaxCharCount(1)];
    private readonly Encoding encoding = encoding.Required();
    private readonly IStreamRwsl<byte> stream = stream.Required();
    private int carryCount;
    private int carryOffset;
    private Decoder decoder = encoding.Required().GetDecoder();
    private Encoder encoder = encoding.Required().GetEncoder();
    public bool IsEmpty => this.stream is null;

    public bool IsEnd => this.IsEmpty || (this.carryCount == 0 && this.stream.Position == this.stream.Length);

    public void Dispose() => this.stream.Dispose();

    public void WriteAllText(ReadOnlySpan<char> text, CancelToken cancelToken = default)
    {
        this.stream.Position = 0;
        this.stream.Length = 0;
        this.Write(text, cancelToken);
    }

    public string ReadAllText(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        var memory = new DynamicMemory<char>();

        try
        {
            for (;;)
            {
                var read = this.Read(memory.GetSpan(256), cancelToken);

                if (read == 0)
                    break;

                memory.Advance(read);
            }

            return memory.IsEmpty ? string.Empty : new string(memory.AsSpan());
        }
        finally
        {
            memory.Dispose();
        }
    }

    public string ReadAtMost(int count)
    {
        if (count.RequiredPositiveOrZero() == 0)
            return string.Empty;

        Span<char> charBuffer = stackalloc char[count];
        var total = 0;

        while (total < charBuffer.Length)
        {
            var read = this.Read(charBuffer[total..]);

            if (read == 0)
                break;

            total += read;
        }

        return new string(charBuffer[..total]);
    }

    public long GetLength()
    {
        var start = this.stream.Position;
        Span<byte> byteBuffer = stackalloc byte[1];
        var charBuffer = new char[this.encoding.GetMaxCharCount(1)];
        var decoder = this.encoding.GetDecoder();
        long result = 0;

        try
        {
            this.stream.Position = 0;

            while (this.stream.Position < this.stream.Length)
            {
                var read = this.stream.Read(byteBuffer);

                if (read == 0)
                    throw new EndOfStreamException();

                decoder.Convert(byteBuffer[..read], charBuffer, false, out _, out var charsUsed, out _);
                result += charsUsed;
            }

            decoder.Convert(ReadOnlySpan<byte>.Empty, charBuffer, true, out _, out var flushedChars, out _);

            return result + flushedChars;
        }
        finally
        {
            this.stream.Position = start;
        }
    }

    public void Seek(long index)
    {
        if (index.RequiredPositiveOrZero() == 0)
        {
            this.SeekStart();

            return;
        }

        this.SeekStart();
        Span<char> buffer = stackalloc char[256];

        for (var remaining = index; remaining > 0;)
        {
            var read = this.Read(buffer[..(int)Math.Min((long)buffer.Length, remaining)]);

            if (read == 0)
                break;

            remaining -= read;
        }
    }

    public void SeekEnd()
    {
        this.encoder.Reset();
        this.decoder.Reset();
        this.carryOffset = 0;
        this.carryCount = 0;
        this.stream.Position = this.stream.Length;
    }

    public void SeekStart()
    {
        this.encoder.Reset();
        this.decoder.Reset();
        this.carryOffset = 0;
        this.carryCount = 0;
        this.stream.Position = 0;
    }

    public int Read(Span<char> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        if (buffer.IsEmpty)
            return 0;

        var total = this.Drain(buffer);
        Span<byte> byteBuffer = stackalloc byte[1];

        while (total < buffer.Length && this.stream.Position < this.stream.Length)
        {
            var read = this.stream.Read(byteBuffer, cancelToken);

            if (read == 0)
                throw new EndOfStreamException();

            if (this.Fill(byteBuffer[..read], false) == 0)
                continue;

            total += this.Drain(buffer[total..]);
        }

        if (total < buffer.Length && this.stream.Position == this.stream.Length)
        {
            if (this.Fill(ReadOnlySpan<byte>.Empty, true) != 0)
                total += this.Drain(buffer[total..]);
        }

        return total;
    }

    public void Write(ReadOnlySpan<char> buffer, CancelToken cancelToken = default)
    {
        if (buffer.IsEmpty)
            return;

        var byteCount = this.encoder.GetByteCount(buffer, false);

        if (byteCount == 0)
            return;

        Span<byte> bytes = stackalloc byte[byteCount];

        var written = this.encoder.GetBytes(buffer, bytes, false);

        if (written != 0)
            this.stream.Write(bytes[..written], cancelToken);
    }

    public void Write(char value, CancelToken cancelToken = default) => this.Write(stackalloc char[1] { value }, cancelToken);

    public void Flush(CancelToken cancelToken = default)
    {
        var byteCount = this.encoder.GetByteCount(ReadOnlySpan<char>.Empty, true);

        if (byteCount != 0)
        {
            Span<byte> bytes = stackalloc byte[byteCount];
            var written = this.encoder.GetBytes(ReadOnlySpan<char>.Empty, bytes, true);

            if (written != 0)
                this.stream.Write(bytes[..written], cancelToken);
        }

        this.stream.Flush(cancelToken);
    }

    public string ReadLine(CancelToken cancelToken = default)
    {
        this.ReadLine(out var line, cancelToken);

        return line;
    }

    public int ReadLine(out string line, CancelToken cancelToken = default)
    {
        if (this.IsEmpty || this.IsEnd)
        {
            line = string.Empty;

            return 0;
        }

        var memory = new DynamicMemory<char>();

        try
        {
            Span<char> span = stackalloc char[1];
            var r = 0;

            for (;;)
            {
                if (this.Read(span, cancelToken) == 0)
                    break;

                r++;

                if (span[0] == '\n')
                    break;

                memory.Write(span[0]);
            }

            if (memory.IsEmpty)
            {
                line = string.Empty;

                return r;
            }

            var result = memory.AsSpan();

            if (result.Length > 0 && result[^1] == '\r')
                result = result[..^1];

            line = new string(result);

            return r;
        }
        finally
        {
            memory.Dispose();
        }
    }

    public void WriteLine(CancelToken cancelToken = default) => this.Write('\n', cancelToken);

    public void WriteLine(ReadOnlySpan<char> line, CancelToken cancelToken = default)
    {
        this.Write(line, cancelToken);
        this.WriteLine(cancelToken);
    }

    private int Drain(Span<char> span)
    {
        if (span.IsEmpty || this.carryCount == 0)
            return 0;

        var copied = Math.Min(this.carryCount, span.Length);
        this.carry.AsSpan(this.carryOffset, copied).CopyTo(span);
        this.carryOffset += copied;
        this.carryCount -= copied;

        if (this.carryCount == 0)
            this.carryOffset = 0;

        return copied;
    }

    private int Fill(ReadOnlySpan<byte> bytes, bool flush)
    {
        this.decoder.Convert(bytes, this.carry, flush, out _, out var charsUsed, out _);
        this.carryOffset = 0;
        this.carryCount = charsUsed;

        return charsUsed;
    }
}
