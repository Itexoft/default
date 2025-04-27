// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Itexoft.Core;
using Itexoft.IO;
using Itexoft.IO.Streams;
using Itexoft.Threading;

namespace Itexoft.Net.Http;

public sealed class NetHttpWebSocket(IStreamRw<byte> stream) : IDisposable
{
    private const byte finalBit = 0x80;
    private const byte maskBit = 0x80;
    private const byte opcodeMask = 0x0F;
    private const byte reservedBitsMask = 0x70;
    private const byte closeOpcode = 0x08;
    private const byte pingOpcode = 0x09;
    private const byte pongOpcode = 0x0A;
    private const byte textOpcode = 0x01;
    private const byte continuationOpcode = 0x00;
    private const byte binaryOpcode = 0x02;
    private const byte shortPayloadMarker = 126;
    private const byte longPayloadMarker = 127;
    private const int closeCodeProtocolError = 1002;
    private const int closeCodeUnsupportedData = 1003;
    private static readonly UTF8Encoding utf8 = new(false, true);
    private readonly IStreamRw<byte> stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private Disposed closed;
    private bool fragmentedTextActive;
    private DynamicMemory<byte> textBuffer;

    public void Dispose()
    {
        if (this.closed.Enter())
            return;

        this.textBuffer.Dispose();
        this.stream.Dispose();
    }

    public NetHttpWebSocketMessage Receive(CancelToken cancelToken = default)
    {
        this.closed.ThrowIf(in cancelToken);
        Span<byte> header = stackalloc byte[2];
        Span<byte> maskingKey = stackalloc byte[sizeof(uint)];

        while (true)
        {
            ReadExactly(this.stream, header, cancelToken);
            var control = header[0];
            var prefix = header[1];

            if ((control & reservedBitsMask) != 0)
                return this.Fail(closeCodeProtocolError, "Reserved websocket bits are not supported.", cancelToken);

            var payloadLength = ReadPayloadLength(prefix, this.stream, cancelToken);

            if ((prefix & maskBit) == 0)
                return this.Fail(closeCodeProtocolError, "Client websocket frames must be masked.", cancelToken);

            ReadExactly(this.stream, maskingKey, cancelToken);
            var payload = ReadPayload(this.stream, payloadLength, cancelToken, out var payloadCount);

            try
            {
                var payloadSpan = payload is null ? Span<byte>.Empty : payload.AsSpan(0, payloadCount);
                Unmask(payloadSpan, maskingKey);
                var final = (control & finalBit) != 0;
                var opcode = (byte)(control & opcodeMask);

                switch (opcode)
                {
                    case continuationOpcode:
                        if (!this.fragmentedTextActive)
                            return this.Fail(closeCodeProtocolError, "Unexpected continuation frame.", cancelToken);

                        this.textBuffer.Write(payloadSpan);

                        if (!final)
                            continue;

                        this.fragmentedTextActive = false;

                        return new(NetHttpWebSocketMessageType.Text, DecodeText(this.textBuffer.AsSpan(), cancelToken));
                    case textOpcode:
                        if (this.fragmentedTextActive)
                            return this.Fail(closeCodeProtocolError, "Text frame started before completing the previous message.", cancelToken);

                        if (final)
                            return new(NetHttpWebSocketMessageType.Text, DecodeText(payloadSpan, cancelToken));

                        this.textBuffer.Clear();
                        this.textBuffer.Write(payloadSpan);
                        this.fragmentedTextActive = true;

                        continue;
                    case closeOpcode:
                        this.Close(cancelToken);

                        return new(NetHttpWebSocketMessageType.Close);
                    case pingOpcode:
                        this.SendControlFrame(pongOpcode, payloadSpan, cancelToken);

                        continue;
                    case pongOpcode:
                        continue;
                    case binaryOpcode:
                        return this.Fail(closeCodeUnsupportedData, "Binary websocket frames are not supported.", cancelToken);
                    default:
                        return this.Fail(closeCodeProtocolError, $"Unsupported websocket opcode: {opcode}.", cancelToken);
                }
            }
            finally
            {
                if (payload is not null)
                    ArrayPool<byte>.Shared.Return(payload);
            }
        }
    }

    public void SendText(string text, CancelToken cancelToken = default) => this.SendText(text.AsSpan(), cancelToken);

    public void SendText(ReadOnlySpan<char> text, CancelToken cancelToken = default)
    {
        this.closed.ThrowIf(in cancelToken);

        if (text.IsEmpty)
        {
            this.SendFrame((byte)(textOpcode | finalBit), ReadOnlySpan<byte>.Empty, cancelToken);

            return;
        }

        var byteCount = utf8.GetByteCount(text);
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);

        try
        {
            var written = utf8.GetBytes(text, buffer);
            this.SendFrame((byte)(textOpcode | finalBit), buffer.AsSpan(0, written), cancelToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public void Close(CancelToken cancelToken = default)
    {
        if (this.closed.Enter())
            return;

        try
        {
            this.SendControlFrame(closeOpcode, ReadOnlySpan<byte>.Empty, cancelToken);
        }
        catch
        {
            // ignored
        }
        finally
        {
            this.textBuffer.Dispose();
            this.stream.Dispose();
        }
    }

    private NetHttpWebSocketMessage Fail(int closeCode, string message, CancelToken cancelToken)
    {
        try
        {
            Span<byte> payload = stackalloc byte[sizeof(ushort)];
            BinaryPrimitives.WriteUInt16BigEndian(payload, checked((ushort)closeCode));
            this.SendControlFrame(closeOpcode, payload, cancelToken);
        }
        catch
        {
            // ignored
        }

        this.Dispose();

        throw new InvalidDataException(message);
    }

    private void SendControlFrame(byte opcode, ReadOnlySpan<byte> payload, CancelToken cancelToken) =>
        this.SendFrame((byte)(opcode | finalBit), payload, cancelToken);

    private void SendFrame(byte control, ReadOnlySpan<byte> payload, CancelToken cancelToken)
    {
        Span<byte> header = stackalloc byte[sizeof(ulong) + sizeof(ushort)];
        var written = 0;
        header[written++] = control;

        if (payload.Length < shortPayloadMarker)
            header[written++] = (byte)payload.Length;
        else if (payload.Length <= ushort.MaxValue)
        {
            header[written++] = shortPayloadMarker;
            BinaryPrimitives.WriteUInt16BigEndian(header[written..], checked((ushort)payload.Length));
            written += sizeof(ushort);
        }
        else
        {
            header[written++] = longPayloadMarker;
            BinaryPrimitives.WriteUInt64BigEndian(header[written..], checked((ulong)payload.Length));
            written += sizeof(ulong);
        }

        this.stream.Write(header[..written], cancelToken);

        if (!payload.IsEmpty)
            this.stream.Write(payload, cancelToken);

        this.stream.Flush(cancelToken);
    }

    private static long ReadPayloadLength(byte prefix, IStreamR<byte> stream, CancelToken cancelToken)
    {
        var marker = (byte)(prefix & ~maskBit);

        return marker switch
        {
            < shortPayloadMarker => marker,
            shortPayloadMarker => ReadUInt16(stream, cancelToken),
            longPayloadMarker => checked((long)ReadUInt64(stream, cancelToken)),
            _ => throw new InvalidDataException("Invalid websocket payload length."),
        };
    }

    private static ushort ReadUInt16(IStreamR<byte> stream, CancelToken cancelToken)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ushort)];
        ReadExactly(stream, buffer, cancelToken);

        return BinaryPrimitives.ReadUInt16BigEndian(buffer);
    }

    private static ulong ReadUInt64(IStreamR<byte> stream, CancelToken cancelToken)
    {
        Span<byte> buffer = stackalloc byte[sizeof(ulong)];
        ReadExactly(stream, buffer, cancelToken);

        return BinaryPrimitives.ReadUInt64BigEndian(buffer);
    }

    private static byte[]? ReadPayload(IStreamR<byte> stream, long payloadLength, CancelToken cancelToken, out int payloadCount)
    {
        if (payloadLength == 0)
        {
            payloadCount = 0;

            return null;
        }

        if (payloadLength > int.MaxValue)
            throw new InvalidDataException("Websocket frame payload is too large.");

        payloadCount = checked((int)payloadLength);
        var buffer = ArrayPool<byte>.Shared.Rent(payloadCount);
        ReadExactly(stream, buffer.AsSpan(0, payloadCount), cancelToken);

        return buffer;
    }

    private static void Unmask(Span<byte> payload, ReadOnlySpan<byte> maskingKey)
    {
        if (payload.IsEmpty)
            return;

        for (var i = 0; i < payload.Length; i++)
            payload[i] ^= maskingKey[i % maskingKey.Length];
    }

    private static string DecodeText(ReadOnlySpan<byte> payload, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        return payload.IsEmpty ? string.Empty : utf8.GetString(payload);
    }

    private static void ReadExactly(IStreamR<byte> stream, Span<byte> buffer, CancelToken cancelToken)
    {
        while (!buffer.IsEmpty)
        {
            var read = stream.Read(buffer, cancelToken);

            if (read == 0)
                throw new EndOfStreamException("Unexpected end of websocket stream.");

            buffer = buffer[read..];
        }
    }
}
