// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Text;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.IO.Streams.Chars;

public struct CharStreamBr(IStreamR<byte> stream, Encoding encoding) : IStreamR<char>
{
    private readonly IStreamR<byte> source = stream.Required();
    private readonly char[] carry = new char[encoding.Required().GetMaxCharCount(1)];
    private Decoder decoder = encoding.Required().GetDecoder();
    private int carryOffset;
    private int carryCount;
    public bool IsEmpty => stream is null;

    public int Read(Span<char> span, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        if (span.IsEmpty)
            return 0;

        var total = this.Drain(span);
        Span<byte> byteBuffer = stackalloc byte[1];

        while (total < span.Length)
        {
            var read = this.source.Read(byteBuffer, cancelToken);

            if (read == 0)
            {
                if (this.Fill(ReadOnlySpan<byte>.Empty, true) == 0)
                    break;

                total += this.Drain(span[total..]);

                continue;
            }

            if (this.Fill(byteBuffer[..read], false) == 0)
                continue;

            total += this.Drain(span[total..]);
        }

        return total;
    }

    public void Dispose() => stream.Dispose();

    public string ReadLine(CancelToken cancelToken = default)
    {
        this.ReadLine(out var line, cancelToken);

        return line;
    }

    public int ReadLine(out string line, CancelToken cancelToken = default)
    {
        if (this.IsEmpty)
        {
            line = string.Empty;

            return 0;
        }

        var memory = new DynamicMemory<char>(64);

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

    public ReadOnlyMemory<char> ReadToEnd(CancelToken cancelToken = default)
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

            return memory.ToMemory();
        }
        finally
        {
            memory.Dispose();
        }
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

public struct CharStreamBw(IStreamW<byte> stream, Encoding encoding) : IStreamW<char>
{
    private readonly IStreamW<byte> target = stream.Required();
    private Encoder encoder = encoding.Required().GetEncoder();
    public bool IsEmpty => stream is null;

    public void Write(ReadOnlySpan<char> buffer, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        if (buffer.IsEmpty)
            return;

        var byteCount = this.encoder.GetByteCount(buffer, false);

        if (byteCount == 0)
            return;

        Span<byte> bytes = stackalloc byte[byteCount];

        var written = this.encoder.GetBytes(buffer, bytes, false);

        if (written != 0)
            this.target.Write(bytes[..written]);
    }

    public void Write(char value, CancelToken cancelToken = default) => this.Write(stackalloc char[1] { value }, cancelToken);

    public void Flush(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        var byteCount = this.encoder.GetByteCount(ReadOnlySpan<char>.Empty, true);

        if (byteCount != 0)
        {
            Span<byte> bytes = stackalloc byte[byteCount];
            var written = this.encoder.GetBytes(ReadOnlySpan<char>.Empty, bytes, true);

            if (written != 0)
                this.target.Write(bytes[..written]);
        }

        this.target.Flush();
    }

    public void Dispose() => stream.Dispose();
}

public static class CharStreamExtensions
{
    extension<TSstreamW>(TSstreamW writer) where TSstreamW : struct, IStreamW<char>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLine(ReadOnlySpan<char> value)
        {
            writer.Write(value);
            writer.WriteLine();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteLine() => writer.Write('\n');
    }
}
