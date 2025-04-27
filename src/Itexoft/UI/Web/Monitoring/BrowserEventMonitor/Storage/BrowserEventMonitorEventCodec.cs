// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;
using System.Text;
using Itexoft.IO;

namespace Itexoft.UI.Web.Monitoring.BrowserEventMonitor.Storage;

internal static class BrowserEventMonitorEventCodec
{
    private const int headerSize = sizeof(long) + sizeof(long) + sizeof(int);

    public static void Write(IStreamW<byte> stream, in BrowserEventMonitorStoredEvent value)
    {
        Span<byte> header = stackalloc byte[headerSize];
        BinaryPrimitives.WriteInt64LittleEndian(header, value.TimestampUtcMs);
        BinaryPrimitives.WriteInt64LittleEndian(header[sizeof(long)..], BitConverter.DoubleToInt64Bits(value.Value));

        if (value.Text is null)
        {
            BinaryPrimitives.WriteInt32LittleEndian(header[(sizeof(long) + sizeof(long))..], -1);
            stream.Write(header);

            return;
        }

        var bytes = Encoding.UTF8.GetBytes(value.Text);
        BinaryPrimitives.WriteInt32LittleEndian(header[(sizeof(long) + sizeof(long))..], bytes.Length);
        stream.Write(header);
        stream.Write(bytes);
    }

    public static bool TryRead(IStreamR<byte> stream, out BrowserEventMonitorStoredEvent value)
    {
        Span<byte> first = stackalloc byte[1];

        if (stream.Read(first) == 0)
        {
            value = default;

            return false;
        }

        Span<byte> header = stackalloc byte[headerSize];
        first.CopyTo(header);
        stream.ReadExactly(header[1..]);

        var timestampUtcMs = BinaryPrimitives.ReadInt64LittleEndian(header);
        var valueBits = BinaryPrimitives.ReadInt64LittleEndian(header[sizeof(long)..]);
        var textByteLength = BinaryPrimitives.ReadInt32LittleEndian(header[(sizeof(long) + sizeof(long))..]);

        if (textByteLength < -1)
            throw new InvalidDataException($"Event text length cannot be {textByteLength}.");

        if (textByteLength == -1)
        {
            value = new BrowserEventMonitorStoredEvent(timestampUtcMs, BitConverter.Int64BitsToDouble(valueBits), null);

            return true;
        }

        if (textByteLength == 0)
        {
            value = new BrowserEventMonitorStoredEvent(timestampUtcMs, BitConverter.Int64BitsToDouble(valueBits), null);

            return true;
        }

        var buffer = new byte[textByteLength];
        stream.ReadExactly(buffer);
        value = new BrowserEventMonitorStoredEvent(timestampUtcMs, BitConverter.Int64BitsToDouble(valueBits), Encoding.UTF8.GetString(buffer));

        return true;
    }
}
