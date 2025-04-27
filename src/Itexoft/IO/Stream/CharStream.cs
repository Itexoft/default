// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Text;
using Itexoft.Extensions;
using Itexoft.Interop;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.IO;

public interface ICharStreamRwa : ICharStreamRa, ICharStreamWa;

public interface ICharStreamRwals : ICharStreamRals, ICharStreamWals;

public interface ICharStreamR : IStreamR<char>
{
    string ReadLine();
}

public interface ICharStreamRa : IStreamRa<char>
{
    StackTask<string> ReadLineAsync(CancelToken cancelToken = default);
}

public interface ICharStreamRals : ICharStreamRa, IStreamRals<char>;

public interface ICharStreamWals : ICharStreamWa, IStreamWals<char> { }

public interface ICharStreamW : IStreamW<char>
{
    void Write(string value);
    void WriteLine();
    void WriteLine(string value);
    void WriteLine(ReadOnlySpan<char> value);
}

public interface ICharStreamWa : IStreamWa<char>
{
    StackTask IStreamWa<char>.WriteAsync(ReadOnlyMemory<char> value, CancelToken cancelToken) => this.WriteAsync(value, cancelToken);
    StackTask WriteAsync(string value, CancelToken cancelToken = default);
    new StackTask WriteAsync(ReadOnlyMemory<char> value, CancelToken cancelToken = default);
    StackTask WriteLineAsync(CancelToken cancelToken = default);
    StackTask WriteLineAsync(string value, CancelToken cancelToken = default);
    StackTask WriteLineAsync(ReadOnlyMemory<char> value, CancelToken cancelToken = default);
}

public class CharStreamR : StreamBase<char>, ICharStreamR
{
    private const int defaultBufferSize = 4096;
    private readonly Decoder decoder;

    private readonly IStreamR stream;
    private byte[] byteBuffer;
    private int byteCount;
    private int byteOffset;
    private char[] charBuffer;
    private int charCount;
    private int charOffset;
    private bool completed;

    public CharStreamR(IStreamR stream, Encoding encoding)
    {
        this.stream = stream.Required();
        this.decoder = encoding.Required().GetDecoder();
        this.byteBuffer = ArrayPool<byte>.Shared.Rent(defaultBufferSize);
        var charBufferSize = Math.Max(encoding.GetMaxCharCount(this.byteBuffer.Length), 1);
        this.charBuffer = ArrayPool<char>.Shared.Rent(charBufferSize);
    }

    public CharStreamR(IStreamR stream) : this(stream, Encoding.UTF8) { }

    public int Read(Span<char> destination)
    {
        if (destination.IsEmpty)
            return 0;

        var total = 0;

        if (this.charCount > 0)
        {
            var toCopy = Math.Min(destination.Length, this.charCount);
            this.charBuffer.AsSpan(this.charOffset, toCopy).CopyTo(destination);
            this.charOffset += toCopy;
            this.charCount -= toCopy;
            total += toCopy;

            if (total == destination.Length)
                return total;
        }

        while (total < destination.Length)
        {
            if (this.byteCount == this.byteOffset && !this.ReadMoreBytes())
            {
                total += this.FlushDecoder(destination[total..]);

                break;
            }

            var bytes = this.byteBuffer.AsSpan(this.byteOffset, this.byteCount - this.byteOffset);
            var target = destination[total..];
            this.decoder.Convert(bytes, target, false, out var bytesUsed, out var charsUsed, out _);
            this.byteOffset += bytesUsed;

            if (this.byteOffset == this.byteCount)
            {
                this.byteOffset = 0;
                this.byteCount = 0;
            }

            total += charsUsed;

            if (charsUsed == 0)
            {
                if (!this.ReadMoreBytes())
                {
                    total += this.FlushDecoder(destination[total..]);

                    break;
                }
            }
        }

        return total;
    }

    public string ReadLine()
    {
        char[]? rented = null;
        var length = 0;

        try
        {
            while (true)
            {
                if (this.charCount == 0 && !this.FillCharBuffer())
                    return length == 0 ? string.Empty : new string(rented!, 0, length);

                var span = this.charBuffer.AsSpan(this.charOffset, this.charCount);
                var idx = span.IndexOfAny('\r', '\n');

                if (idx >= 0)
                {
                    if (idx > 0)
                    {
                        if (length == 0 && rented is null)
                        {
                            var line = new string(span[..idx]);
                            this.ConsumeLineBreak(span, idx);

                            return line;
                        }

                        rented = CharStreamHelpers.EnsureLineBufferCapacity(rented, length + idx, length, this.charBuffer.Length);
                        span[..idx].CopyTo(rented.AsSpan(length));
                        length += idx;
                    }
                    else if (length == 0 && rented is null)
                    {
                        this.ConsumeLineBreak(span, idx);

                        return string.Empty;
                    }

                    this.ConsumeLineBreak(span, idx);

                    return new string(rented!, 0, length);
                }

                if (!span.IsEmpty)
                {
                    rented = CharStreamHelpers.EnsureLineBufferCapacity(rented, length + span.Length, length, this.charBuffer.Length);
                    span.CopyTo(rented.AsSpan(length));
                    length += span.Length;
                    this.charOffset += span.Length;
                    this.charCount -= span.Length;
                }
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    protected override StackTask DisposeAny()
    {
        if (this.byteBuffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.byteBuffer);
            this.byteBuffer = [];
        }

        if (this.charBuffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(this.charBuffer);
            this.charBuffer = [];
        }

        return default;
    }

    private bool FillCharBuffer()
    {
        if (this.charCount > 0)
            return true;

        while (true)
        {
            var available = this.byteCount - this.byteOffset;

            if (available == 0)
            {
                if (!this.ReadMoreBytes())
                {
                    var flushed = this.FlushDecoder(this.charBuffer);

                    if (flushed > 0)
                    {
                        this.charOffset = 0;
                        this.charCount = flushed;

                        return true;
                    }

                    return false;
                }

                available = this.byteCount - this.byteOffset;
            }

            var bytes = this.byteBuffer.AsSpan(this.byteOffset, available);
            var destination = this.charBuffer.AsSpan();
            this.decoder.Convert(bytes, destination, false, out var bytesUsed, out var charsUsed, out _);
            this.byteOffset += bytesUsed;

            if (this.byteOffset == this.byteCount)
            {
                this.byteOffset = 0;
                this.byteCount = 0;
            }

            if (charsUsed > 0)
            {
                this.charOffset = 0;
                this.charCount = charsUsed;

                return true;
            }

            if (bytesUsed == 0)
            {
                if (!this.ReadMoreBytes())
                {
                    var flushed = this.FlushDecoder(this.charBuffer);

                    if (flushed > 0)
                    {
                        this.charOffset = 0;
                        this.charCount = flushed;

                        return true;
                    }

                    return false;
                }
            }
        }
    }

    private bool ReadMoreBytes()
    {
        if (this.completed)
            return false;

        CharStreamHelpers.EnsureByteBufferSpace(ref this.byteBuffer, ref this.byteOffset, ref this.byteCount);

        var read = this.stream.Read(this.byteBuffer.AsSpan(this.byteCount));

        if (read == 0)
        {
            this.completed = true;

            return false;
        }

        this.byteCount += read;

        return true;
    }

    private int FlushDecoder(Span<char> destination) => CharStreamHelpers.FlushDecoder(this.decoder, this.completed, destination);

    private void ConsumeLineBreak(ReadOnlySpan<char> span, int index)
    {
        if (span[index] == '\r')
        {
            if (index + 1 < span.Length)
            {
                var advance = span[index + 1] == '\n' ? 2 : 1;
                this.charOffset += index + advance;
                this.charCount -= index + advance;

                return;
            }

            this.charOffset += index + 1;
            this.charCount -= index + 1;

            if (this.charCount == 0 && this.FillCharBuffer())
            {
                if (this.charBuffer[this.charOffset] == '\n')
                {
                    this.charOffset++;
                    this.charCount--;
                }
            }

            return;
        }

        this.charOffset += index + 1;
        this.charCount -= index + 1;
    }
}

public class CharStreamW(IStreamW stream, Encoding encoding) : StreamBase<char>, ICharStreamW
{
    private const int defaultBufferSize = 4096;
    private static char newLine = '\n';
    private readonly Encoder encoder = encoding.Required().GetEncoder();

    private readonly IStreamW stream = stream.Required();
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(defaultBufferSize);
    private int bufferOffset;
    private bool hasPendingChar;
    private char pendingChar;

    public CharStreamW(IStreamW stream) : this(stream, Encoding.UTF8) { }

    public void Write(string value) => this.Write(value.AsSpan());

    public void WriteLine() => this.Write(newLine.AsSpan());

    public void WriteLine(string value)
    {
        this.Write(value.AsSpan());
        this.Write(newLine.AsSpan());
    }

    public void WriteLine(ReadOnlySpan<char> value)
    {
        this.Write(value);
        this.Write(newLine.AsSpan());
    }

    public virtual void Flush()
    {
        this.FlushEncoder();
        this.stream.Flush();
    }

    public virtual void Write(ReadOnlySpan<char> source)
    {
        if (source.IsEmpty)
            return;

        var span = source;

        if (this.hasPendingChar)
        {
            Span<char> prefix = stackalloc char[2];
            prefix[0] = this.pendingChar;
            prefix[1] = span[0];

            this.pendingChar = '\0';
            this.hasPendingChar = false;

            this.WriteCore(prefix);
            span = span[1..];

            if (span.IsEmpty)
                return;
        }

        this.WriteCore(span);
    }

    protected override StackTask DisposeAny()
    {
        this.FlushEncoder();

        if (this.buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.buffer);
            this.buffer = [];
        }

        this.bufferOffset = 0;
        this.pendingChar = '\0';
        this.hasPendingChar = false;

        return default;
    }

    private void WriteCore(ReadOnlySpan<char> source)
    {
        var span = source;

        while (!span.IsEmpty)
        {
            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();

            var target = this.buffer.AsSpan(this.bufferOffset);

            if (!CharStreamHelpers.TryConvert(
                    this.encoder,
                    span,
                    target,
                    ref this.hasPendingChar,
                    ref this.pendingChar,
                    out var charsUsed,
                    out var bytesUsed))
                return;

            this.bufferOffset += bytesUsed;
            span = span[charsUsed..];

            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();
        }
    }

    private void FlushEncoder()
    {
        if (this.hasPendingChar)
        {
            Span<char> tmp = stackalloc char[1];
            tmp[0] = this.pendingChar;

            this.pendingChar = '\0';
            this.hasPendingChar = false;

            while (true)
            {
                if (this.bufferOffset == this.buffer.Length)
                    this.FlushBuffer();

                var target = this.buffer.AsSpan(this.bufferOffset);
                this.encoder.Convert(tmp, target, true, out var charsUsed, out var bytesUsed, out _);

                this.bufferOffset += bytesUsed;
                tmp = tmp[charsUsed..];

                if (tmp.IsEmpty)
                    break;

                if (charsUsed == 0 && bytesUsed == 0)
                    this.FlushBuffer();
            }
        }

        while (true)
        {
            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();

            var target = this.buffer.AsSpan(this.bufferOffset);
            this.encoder.Convert(ReadOnlySpan<char>.Empty, target, true, out _, out var bytesUsed, out var completed);

            this.bufferOffset += bytesUsed;

            if (this.bufferOffset == this.buffer.Length)
                this.FlushBuffer();

            if (completed)
                break;

            if (bytesUsed == 0)
                this.FlushBuffer();
        }

        this.FlushBuffer();
    }

    private void FlushBuffer()
    {
        if (this.bufferOffset == 0)
            return;

        this.stream.Write(this.buffer.AsSpan(0, this.bufferOffset));
        this.bufferOffset = 0;
    }
}

public class CharStreamRa : StreamBase<char>, ICharStreamRa
{
    private const int defaultBufferSize = 4096;
    private readonly Decoder decoder;

    private readonly IStreamRa stream;
    private byte[] byteBuffer;
    private int byteCount;
    private int byteOffset;
    private char[] charBuffer;
    private int charCount;
    private int charOffset;
    private bool completed;

    public CharStreamRa(IStreamRa stream, Encoding encoding)
    {
        this.stream = stream.Required();
        this.decoder = encoding.Required().GetDecoder();
        this.byteBuffer = ArrayPool<byte>.Shared.Rent(defaultBufferSize);
        var charBufferSize = Math.Max(encoding.GetMaxCharCount(this.byteBuffer.Length), 1);
        this.charBuffer = ArrayPool<char>.Shared.Rent(charBufferSize);
    }

    public CharStreamRa(IStreamRa stream) : this(stream, Encoding.UTF8) { }

    public virtual async StackTask<int> ReadAsync(Memory<char> destination, CancelToken cancelToken = default)
    {
        if (destination.IsEmpty)
            return 0;

        var total = 0;

        if (this.charCount > 0)
        {
            var toCopy = Math.Min(destination.Length, this.charCount);
            this.charBuffer.AsSpan(this.charOffset, toCopy).CopyTo(destination.Span);
            this.charOffset += toCopy;
            this.charCount -= toCopy;
            total += toCopy;

            if (total == destination.Length)
                return total;
        }

        while (total < destination.Length)
        {
            if (this.byteCount == this.byteOffset)
            {
                if (!await this.ReadMoreBytesAsync(cancelToken))
                {
                    total += CharStreamHelpers.FlushDecoder(this.decoder, this.completed, destination.Span[total..]);

                    break;
                }
            }

            int bytesUsed;
            int charsUsed;

            {
                var bytes = this.byteBuffer.AsSpan(this.byteOffset, this.byteCount - this.byteOffset);
                var target = destination.Span[total..];
                this.decoder.Convert(bytes, target, false, out bytesUsed, out charsUsed, out _);
            }

            this.byteOffset += bytesUsed;

            if (this.byteOffset == this.byteCount)
            {
                this.byteOffset = 0;
                this.byteCount = 0;
            }

            total += charsUsed;

            if (charsUsed == 0)
            {
                if (!await this.ReadMoreBytesAsync(cancelToken))
                {
                    total += CharStreamHelpers.FlushDecoder(this.decoder, this.completed, destination.Span[total..]);

                    break;
                }
            }
        }

        return total;
    }

    public virtual async StackTask<string> ReadLineAsync(CancelToken cancelToken = default)
    {
        char[]? rented = null;
        var length = 0;

        try
        {
            while (true)
            {
                if (this.charCount == 0)
                {
                    if (!await this.FillCharBufferAsync(cancelToken))
                        return length == 0 ? string.Empty : new string(rented!, 0, length);
                }

                var idx = -1;
                var hasLineBreak = false;
                var returnEmpty = false;
                string? line = null;

                {
                    var span = this.charBuffer.AsSpan(this.charOffset, this.charCount);
                    idx = span.IndexOfAny('\r', '\n');

                    if (idx >= 0)
                    {
                        hasLineBreak = true;

                        if (idx > 0)
                        {
                            if (length == 0 && rented is null)
                                line = new string(span[..idx]);
                            else
                            {
                                rented = CharStreamHelpers.EnsureLineBufferCapacity(rented, length + idx, length, this.charBuffer.Length);
                                span[..idx].CopyTo(rented.AsSpan(length));
                                length += idx;
                            }
                        }
                        else if (length == 0 && rented is null)
                            returnEmpty = true;
                    }
                    else if (!span.IsEmpty)
                    {
                        rented = CharStreamHelpers.EnsureLineBufferCapacity(rented, length + span.Length, length, this.charBuffer.Length);
                        span.CopyTo(rented.AsSpan(length));
                        length += span.Length;
                        this.charOffset += span.Length;
                        this.charCount -= span.Length;
                    }
                }

                if (!hasLineBreak)
                    continue;

                await this.ConsumeLineBreakAsync(idx, cancelToken);

                if (line is not null)
                    return line;

                if (returnEmpty)
                    return string.Empty;

                return new string(rented!, 0, length);
            }
        }
        finally
        {
            if (rented is not null)
                ArrayPool<char>.Shared.Return(rented);
        }
    }

    protected override StackTask DisposeAny()
    {
        if (this.byteBuffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.byteBuffer);
            this.byteBuffer = [];
        }

        if (this.charBuffer.Length != 0)
        {
            ArrayPool<char>.Shared.Return(this.charBuffer);
            this.charBuffer = [];
        }

        return default;
    }

    private async StackTask<bool> FillCharBufferAsync(CancelToken cancelToken)
    {
        if (this.charCount > 0)
            return true;

        while (true)
        {
            var available = this.byteCount - this.byteOffset;

            if (available == 0)
            {
                if (!await this.ReadMoreBytesAsync(cancelToken))
                {
                    var flushed = CharStreamHelpers.FlushDecoder(this.decoder, this.completed, this.charBuffer);

                    if (flushed > 0)
                    {
                        this.charOffset = 0;
                        this.charCount = flushed;

                        return true;
                    }

                    return false;
                }

                available = this.byteCount - this.byteOffset;
            }

            int bytesUsed;
            int charsUsed;

            {
                var bytes = this.byteBuffer.AsSpan(this.byteOffset, available);
                var destination = this.charBuffer.AsSpan();
                this.decoder.Convert(bytes, destination, false, out bytesUsed, out charsUsed, out _);
            }

            this.byteOffset += bytesUsed;

            if (this.byteOffset == this.byteCount)
            {
                this.byteOffset = 0;
                this.byteCount = 0;
            }

            if (charsUsed > 0)
            {
                this.charOffset = 0;
                this.charCount = charsUsed;

                return true;
            }

            if (bytesUsed == 0)
            {
                if (!await this.ReadMoreBytesAsync(cancelToken))
                {
                    var flushed = CharStreamHelpers.FlushDecoder(this.decoder, this.completed, this.charBuffer);

                    if (flushed > 0)
                    {
                        this.charOffset = 0;
                        this.charCount = flushed;

                        return true;
                    }

                    return false;
                }
            }
        }
    }

    private async StackTask<bool> ReadMoreBytesAsync(CancelToken cancelToken)
    {
        if (this.completed)
            return false;

        CharStreamHelpers.EnsureByteBufferSpace(ref this.byteBuffer, ref this.byteOffset, ref this.byteCount);

        var read = await this.stream.ReadAsync(this.byteBuffer.AsMemory(this.byteCount), cancelToken);

        if (read == 0)
        {
            this.completed = true;

            return false;
        }

        this.byteCount += read;

        return true;
    }

    private async StackTask ConsumeLineBreakAsync(int index, CancelToken cancelToken)
    {
        var bufferIndex = this.charOffset + index;

        if (this.charBuffer[bufferIndex] == '\r')
        {
            if (index + 1 < this.charCount)
            {
                var advance = this.charBuffer[bufferIndex + 1] == '\n' ? 2 : 1;
                this.charOffset += index + advance;
                this.charCount -= index + advance;

                return;
            }

            this.charOffset += index + 1;
            this.charCount -= index + 1;

            if (this.charCount == 0 && await this.FillCharBufferAsync(cancelToken))
            {
                if (this.charBuffer[this.charOffset] == '\n')
                {
                    this.charOffset++;
                    this.charCount--;
                }
            }

            return;
        }

        this.charOffset += index + 1;
        this.charCount -= index + 1;
    }
}

public class CharStreamWa(IStreamWa stream, Encoding encoding) : StreamBase<char>, ICharStreamWa
{
    private const int defaultBufferSize = 4096;
    private static readonly ReadOnlyMemory<char> newLine = "\n".AsMemory();
    private readonly Encoder encoder = encoding.Required().GetEncoder();

    private readonly IStreamWa stream = stream.Required();
    private byte[] buffer = ArrayPool<byte>.Shared.Rent(defaultBufferSize);
    private int bufferOffset;
    private bool hasPendingChar;
    private char pendingChar;

    public CharStreamWa(IStreamWa stream) : this(stream, Encoding.UTF8) { }

    public StackTask WriteAsync(string value, CancelToken cancelToken = default) => this.WriteAsync(value.AsMemory(), cancelToken);

    public StackTask WriteLineAsync(CancelToken cancelToken = default) => this.WriteAsync(newLine, cancelToken);

    public async StackTask WriteLineAsync(string value, CancelToken cancelToken = default)
    {
        await this.WriteAsync(value.AsMemory(), cancelToken);
        await this.WriteAsync(newLine, cancelToken);
    }

    public StackTask WriteLineAsync(ReadOnlyMemory<char> value, CancelToken cancelToken = default)
    {
        if (value.IsEmpty)
            return this.WriteAsync(newLine, cancelToken);

        return this.WriteLineAsync(new string(value.Span), cancelToken);
    }

    public virtual async StackTask WriteAsync(ReadOnlyMemory<char> value, CancelToken cancelToken = default)
    {
        if (value.IsEmpty)
            return;

        var memory = value;

        if (this.hasPendingChar)
        {
            var first = memory.Span[0];
            char[] prefix = [this.pendingChar, first];

            this.pendingChar = '\0';
            this.hasPendingChar = false;

            await this.WriteCoreAsync(prefix.AsMemory(), cancelToken);
            memory = memory[1..];

            if (memory.IsEmpty)
                return;
        }

        await this.WriteCoreAsync(memory, cancelToken);
    }

    public virtual async StackTask FlushAsync(CancelToken cancelToken = default)
    {
        await this.FlushEncoderAsync(cancelToken);
        await this.stream.FlushAsync(cancelToken);
    }

    protected async override StackTask DisposeAny()
    {
        await this.FlushEncoderAsync(default);

        if (this.buffer.Length != 0)
        {
            ArrayPool<byte>.Shared.Return(this.buffer);
            this.buffer = [];
        }

        this.bufferOffset = 0;
        this.pendingChar = '\0';
        this.hasPendingChar = false;
    }

    private async StackTask WriteCoreAsync(ReadOnlyMemory<char> source, CancelToken cancelToken)
    {
        var memory = source;

        while (!memory.IsEmpty)
        {
            if (this.bufferOffset == this.buffer.Length)
                await this.FlushBufferAsync(cancelToken);

            bool converted;
            int charsUsed;
            int bytesUsed;

            {
                var span = memory.Span;
                var target = this.buffer.AsSpan(this.bufferOffset);

                converted = CharStreamHelpers.TryConvert(
                    this.encoder,
                    span,
                    target,
                    ref this.hasPendingChar,
                    ref this.pendingChar,
                    out charsUsed,
                    out bytesUsed);
            }

            if (!converted)
                return;

            this.bufferOffset += bytesUsed;
            memory = memory[charsUsed..];

            if (this.bufferOffset == this.buffer.Length)
                await this.FlushBufferAsync(cancelToken);
        }
    }

    private async StackTask FlushEncoderAsync(CancelToken cancelToken)
    {
        if (this.hasPendingChar)
        {
            char[] tmp = [this.pendingChar];

            this.pendingChar = '\0';
            this.hasPendingChar = false;

            var memory = tmp.AsMemory();

            while (!memory.IsEmpty)
            {
                if (this.bufferOffset == this.buffer.Length)
                    await this.FlushBufferAsync(cancelToken);

                int charsUsed;
                int bytesUsed;

                {
                    var span = memory.Span;
                    var target = this.buffer.AsSpan(this.bufferOffset);
                    this.encoder.Convert(span, target, true, out charsUsed, out bytesUsed, out _);
                }

                this.bufferOffset += bytesUsed;
                memory = memory[charsUsed..];

                if (memory.IsEmpty)
                    break;

                if (charsUsed == 0 && bytesUsed == 0)
                    await this.FlushBufferAsync(cancelToken);
            }
        }

        while (true)
        {
            if (this.bufferOffset == this.buffer.Length)
                await this.FlushBufferAsync(cancelToken);

            int bytesUsed;
            bool completed;

            {
                var target = this.buffer.AsSpan(this.bufferOffset);
                this.encoder.Convert(ReadOnlySpan<char>.Empty, target, true, out _, out bytesUsed, out completed);
            }

            this.bufferOffset += bytesUsed;

            if (this.bufferOffset == this.buffer.Length)
                await this.FlushBufferAsync(cancelToken);

            if (completed)
                break;

            if (bytesUsed == 0)
                await this.FlushBufferAsync(cancelToken);
        }

        await this.FlushBufferAsync(cancelToken);
    }

    private async StackTask FlushBufferAsync(CancelToken cancelToken)
    {
        if (this.bufferOffset == 0)
            return;

        await this.stream.WriteAsync(this.buffer.AsMemory(0, this.bufferOffset), cancelToken);
        this.bufferOffset = 0;
    }
}

public class CharStream(IStreamSlrwa stream, Encoding encoding) : ICharStreamRals, ICharStreamWals
{
    private readonly CharStreamRa streamRa = new(stream, encoding);
    private readonly CharStreamWa streamWa = new(stream, encoding);
    public CharStream(string value, Encoding encoding) : this(new RamAsyncStream(encoding.GetBytes(value)), encoding) { }
    public CharStream(Encoding encoding) : this(new RamAsyncStream(), encoding) { }
    public CharStream(string value) : this(value, Encoding.UTF8) { }
    public CharStream() : this(Encoding.UTF8) { }

    public long Length => stream.Length / sizeof(char);
    public long Seek(long offset, SeekOrigin origin) => stream.Seek(offset * sizeof(char), origin);

    public async StackTask DisposeAsync()
    {
        await this.streamRa.DisposeAsync();
        await this.streamWa.DisposeAsync();
    }

    public StackTask<int> ReadAsync(Memory<char> destination, CancelToken cancelToken = default) =>
        this.streamRa.ReadAsync(destination, cancelToken);

    public StackTask<string> ReadLineAsync(CancelToken cancelToken = default) => this.streamRa.ReadLineAsync();
    public StackTask FlushAsync(CancelToken cancelToken = default) => this.streamWa.FlushAsync(cancelToken);

    public StackTask WriteAsync(string value, CancelToken cancelToken = default) => this.streamWa.WriteAsync(value, cancelToken);

    public StackTask WriteAsync(ReadOnlyMemory<char> value, CancelToken cancelToken = default) => this.streamWa.WriteAsync(value, cancelToken);

    public StackTask WriteLineAsync(CancelToken cancelToken = default) => this.streamWa.WriteLineAsync(cancelToken);

    public StackTask WriteLineAsync(string value, CancelToken cancelToken = default) => this.streamWa.WriteLineAsync(value, cancelToken);

    public StackTask WriteLineAsync(ReadOnlyMemory<char> value, CancelToken cancelToken = default) =>
        this.streamWa.WriteLineAsync(value, cancelToken);
}

file static class CharStreamHelpers
{
    public static void EnsureByteBufferSpace(ref byte[] buffer, ref int offset, ref int count)
    {
        if (offset > 0 && offset < count)
        {
            var remaining = count - offset;
            Buffer.BlockCopy(buffer, offset, buffer, 0, remaining);
            offset = 0;
            count = remaining;
        }
        else if (offset == count)
        {
            offset = 0;
            count = 0;
        }

        if (count == buffer.Length)
        {
            var newBuffer = ArrayPool<byte>.Shared.Rent(buffer.Length * 2);
            Buffer.BlockCopy(buffer, 0, newBuffer, 0, count);
            ArrayPool<byte>.Shared.Return(buffer);
            buffer = newBuffer;
        }
    }

    public static int FlushDecoder(Decoder decoder, bool completed, Span<char> destination)
    {
        if (!completed)
            return 0;

        decoder.Convert(ReadOnlySpan<byte>.Empty, destination, true, out _, out var charsUsed, out _);

        return charsUsed;
    }

    public static char[] EnsureLineBufferCapacity(char[]? rented, int required, int length, int baseSize)
    {
        if (rented is null)
            return ArrayPool<char>.Shared.Rent(Math.Max(required, baseSize));

        if (rented.Length >= required)
            return rented;

        var newBuffer = ArrayPool<char>.Shared.Rent(Math.Max(required, rented.Length * 2));

        if (length > 0)
            rented.AsSpan(0, length).CopyTo(newBuffer);

        ArrayPool<char>.Shared.Return(rented);

        return newBuffer;
    }

    public static bool TryConvert(
        Encoder encoder,
        ReadOnlySpan<char> source,
        Span<byte> target,
        ref bool hasPendingChar,
        ref char pendingChar,
        out int charsUsed,
        out int bytesUsed)
    {
        encoder.Convert(source, target, false, out charsUsed, out bytesUsed, out _);

        if (charsUsed == 0 && bytesUsed == 0)
        {
            pendingChar = source[0];
            hasPendingChar = true;

            return false;
        }

        return true;
    }
}
