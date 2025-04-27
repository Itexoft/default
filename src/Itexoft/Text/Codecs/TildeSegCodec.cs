// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Globalization;
using System.Text;
using Itexoft.IO;
using Itexoft.IO.Streams;
using Itexoft.Threading;

namespace Itexoft.Text.Codecs;

public static class TildeSegCodec
{
    private static readonly UTF8Encoding utf8 = new(false, true);

    public static string Encode(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return "~";

        var output = new DynamicMemory<char>(Math.Max(value.Length, 8));
        var stemBuffer = new DynamicMemory<char>(8);
        Span<char> stem = stackalloc char[4];

        try
        {
            var stemOpen = true;
            var stemCandidate = true;
            var stemLength = 0;
            var pendingDot = false;

            for (var index = 0; index < value.Length;)
            {
                var status = Rune.DecodeFromUtf16(value[index..], out var rune, out var consumed);

                if (status != OperationStatus.Done)
                    throw new FormatException("Input contains an invalid UTF-16 sequence.");

                index += consumed;

                if (stemOpen && rune.Value == '.')
                {
                    FlushStem(ref output, ref stemBuffer, stem[..stemLength], stemCandidate);
                    stemOpen = false;
                    pendingDot = true;

                    continue;
                }

                if (stemOpen)
                {
                    WriteEncodedRune(ref stemBuffer, rune);
                    UpdateStemCandidate(rune, ref stemCandidate, ref stemLength, stem);

                    if (!stemCandidate)
                    {
                        output.Write(stemBuffer.AsSpan());
                        stemOpen = false;
                    }

                    continue;
                }

                if (pendingDot)
                {
                    output.Write('.');
                    pendingDot = false;
                }

                if (rune.Value == '.')
                {
                    pendingDot = true;

                    continue;
                }

                WriteEncodedRune(ref output, rune);
            }

            if (stemOpen)
                FlushStem(ref output, ref stemBuffer, stem[..stemLength], stemCandidate);

            if (pendingDot)
                WriteEscapedByte(ref output, (byte)'.');

            return new string(output.AsSpan());
        }
        finally
        {
            stemBuffer.Dispose();
            output.Dispose();
        }
    }

    public static string Decode(ReadOnlySpan<char> value)
    {
        if (value.Length == 1 && value[0] == '~')
            return string.Empty;

        var output = new DynamicMemory<char>(Math.Max(value.Length, 8));
        var bytes = new DynamicMemory<byte>(8);

        try
        {
            for (var index = 0; index < value.Length;)
            {
                var c = value[index];

                if (c == '~')
                {
                    if (index + 2 >= value.Length || !TryParseHexByte(value[(index + 1)..(index + 3)], out var parsed))
                        throw new FormatException("Input contains an invalid TSE escape sequence.");

                    bytes.Write(parsed);
                    index += 3;

                    continue;
                }

                FlushDecodedBytes(ref output, ref bytes);

                if (char.IsSurrogate(c))
                    throw new FormatException("Input contains an invalid UTF-16 sequence.");

                output.Write(c);
                index++;
            }

            FlushDecodedBytes(ref output, ref bytes);

            return output.IsEmpty ? string.Empty : new string(output.AsSpan());
        }
        finally
        {
            bytes.Dispose();
            output.Dispose();
        }
    }

    public static void Encode<TReader, TWriter>(ref TReader reader, ref TWriter writer, CancelToken cancelToken = default)
        where TReader : struct, IStreamR<char> where TWriter : struct, IStreamW<char>
    {
        cancelToken.ThrowIf();
        var value = ReadAll(ref reader, cancelToken);
        var encoded = Encode(value.AsSpan());
        writer.Write(encoded.AsSpan(), cancelToken);
    }

    public static void Decode<TReader, TWriter>(ref TReader reader, ref TWriter writer, CancelToken cancelToken = default)
        where TReader : struct, IStreamR<char> where TWriter : struct, IStreamW<char>
    {
        cancelToken.ThrowIf();
        var value = ReadAll(ref reader, cancelToken);
        var decoded = Decode(value.AsSpan());
        writer.Write(decoded.AsSpan(), cancelToken);
    }

    private static void UpdateStemCandidate(Rune rune, ref bool stemCandidate, ref int stemLength, Span<char> stem)
    {
        if (!stemCandidate)
            return;

        if (rune.Value > 0x7F || !char.IsAsciiLetterOrDigit((char)rune.Value))
        {
            stemCandidate = false;

            return;
        }

        if (stemLength == stem.Length)
        {
            stemCandidate = false;

            return;
        }

        stem[stemLength++] = char.ToUpperInvariant((char)rune.Value);
    }

    private static void FlushStem(ref DynamicMemory<char> output, ref DynamicMemory<char> stemBuffer, ReadOnlySpan<char> stem, bool stemCandidate)
    {
        var encodedStem = stemBuffer.AsSpan();

        if (!encodedStem.IsEmpty && stemCandidate && IsReservedStem(stem))
        {
            WriteEscapedByte(ref output, (byte)encodedStem[0]);

            if (encodedStem.Length > 1)
                output.Write(encodedStem[1..]);
        }
        else if (!encodedStem.IsEmpty)
            output.Write(encodedStem);

        stemBuffer.Clear();
    }

    private static bool IsReservedStem(ReadOnlySpan<char> stem) => stem.Length switch
    {
        3 => stem is "CON" || stem is "PRN" || stem is "AUX" || stem is "NUL",
        4 => (stem[..3] is "COM" || stem[..3] is "LPT") && stem[3] is >= '1' and <= '9',
        _ => false,
    };

    private static bool IsLiteral(Rune rune)
    {
        if (rune.Value <= 0x7F)
            return rune.Value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.';

        return Rune.GetUnicodeCategory(rune) is not UnicodeCategory.Control
            and not UnicodeCategory.Format
            and not UnicodeCategory.PrivateUse
            and not UnicodeCategory.Surrogate
            and not UnicodeCategory.OtherNotAssigned
            and not UnicodeCategory.SpaceSeparator
            and not UnicodeCategory.LineSeparator
            and not UnicodeCategory.ParagraphSeparator;
    }

    private static void WriteEncodedRune(ref DynamicMemory<char> writer, Rune rune)
    {
        if (IsLiteral(rune))
        {
            Span<char> chars = stackalloc char[2];
            var written = rune.EncodeToUtf16(chars);
            writer.Write(chars[..written]);

            return;
        }

        Span<byte> utf8 = stackalloc byte[4];
        var length = rune.EncodeToUtf8(utf8);

        for (var i = 0; i < length; i++)
            WriteEscapedByte(ref writer, utf8[i]);
    }

    private static void WriteEscapedByte(ref DynamicMemory<char> writer, byte value)
    {
        writer.Write('~');
        writer.Write(ToHex(value >> 4));
        writer.Write(ToHex(value & 0xF));
    }

    private static char ToHex(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);

    private static bool TryParseHexByte(ReadOnlySpan<char> value, out byte result)
    {
        result = 0;

        if (value.Length != 2)
            return false;

        var hi = ParseHex(value[0]);
        var lo = ParseHex(value[1]);

        if (hi < 0 || lo < 0)
            return false;

        result = (byte)((hi << 4) | lo);

        return true;
    }

    private static int ParseHex(char c) => c switch
    {
        >= '0' and <= '9' => c - '0',
        >= 'a' and <= 'f' => c - 'a' + 10,
        >= 'A' and <= 'F' => c - 'A' + 10,
        _ => -1,
    };

    private static void FlushDecodedBytes(ref DynamicMemory<char> output, ref DynamicMemory<byte> bytes)
    {
        if (bytes.IsEmpty)
            return;

        var text = utf8.GetString(bytes.AsSpan());
        output.Write(text.AsSpan());
        bytes.Clear();
    }

    private static string ReadAll<TReader>(ref TReader reader, CancelToken cancelToken) where TReader : struct, IStreamR<char>
    {
        var memory = new DynamicMemory<char>(64);

        try
        {
            for (;;)
            {
                var span = memory.GetSpan(256);
                var read = reader.Read(span, cancelToken);

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
}
