// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers.Binary;

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private const int directoryEntryValueSize = sizeof(long) + 1;

    private static int GetDirectoryEntryKeyLength(ReadOnlySpan<char> name)
        => checked(sizeof(long) + name.Length * sizeof(char));

    private static void WriteDirectoryEntryKey(Span<byte> destination, long parentInodeId, ReadOnlySpan<char> name)
    {
        if (destination.Length != GetDirectoryEntryKeyLength(name))
            throw new ArgumentOutOfRangeException(nameof(destination));

        BinaryPrimitives.WriteInt64BigEndian(destination, parentInodeId);

        for (var i = 0; i < name.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(sizeof(long) + i * sizeof(char), sizeof(char)), name[i]);
    }

    private static long DecodeDirectoryEntryKeyParentInodeId(ReadOnlySpan<byte> key)
    {
        if (key.Length < sizeof(long))
            throw new InvalidDataException("Directory entry key is too short.");

        return BinaryPrimitives.ReadInt64BigEndian(key);
    }

    private static void WriteInt64Key(Span<byte> destination, long value)
    {
        if (destination.Length != sizeof(long))
            throw new ArgumentOutOfRangeException(nameof(destination));

        BinaryPrimitives.WriteInt64BigEndian(destination, value);
    }

    private static long DecodeInt64Key(ReadOnlySpan<byte> key)
    {
        if (key.Length != sizeof(long))
            throw new InvalidDataException("Int64 key must occupy exactly 8 bytes.");

        return BinaryPrimitives.ReadInt64BigEndian(key);
    }

    private static void WriteDirectoryEntryValue(Span<byte> destination, long inodeId, NodeKind kind)
    {
        if (destination.Length != directoryEntryValueSize)
            throw new ArgumentOutOfRangeException(nameof(destination));

        BinaryPrimitives.WriteInt64LittleEndian(destination, inodeId);
        destination[sizeof(long)] = (byte)kind;
    }

    private static NamespaceEntry DecodeDirectoryEntryValue(ReadOnlySpan<byte> value)
    {
        if (value.Length != directoryEntryValueSize)
            throw new InvalidDataException("Directory entry value has invalid length.");

        var kind = value[sizeof(long)] switch
        {
            (byte)NodeKind.Directory => NodeKind.Directory,
            (byte)NodeKind.File => NodeKind.File,
            _ => throw new InvalidDataException("Directory entry value has invalid node kind."),
        };

        return new(BinaryPrimitives.ReadInt64LittleEndian(value), kind);
    }

    private static NamespaceEntry ReadDirectoryEntryValue(ref ChunkStreamCursor cursor, int valueLength)
    {
        if (valueLength != directoryEntryValueSize)
            throw new InvalidDataException("Directory entry value has invalid length.");

        Span<byte> value = stackalloc byte[directoryEntryValueSize];
        cursor.ReadExactly(value);

        return DecodeDirectoryEntryValue(value);
    }

    private static void WriteInodeValue(Span<byte> destination, long length, long contentRoot, int attributes)
    {
        const int inodeValueSize = sizeof(long) + sizeof(long) + sizeof(int);

        if (destination.Length != inodeValueSize)
            throw new ArgumentOutOfRangeException(nameof(destination));

        BinaryPrimitives.WriteInt64LittleEndian(destination, length);
        BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(sizeof(long), sizeof(long)), contentRoot);
        BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(sizeof(long) + sizeof(long), sizeof(int)), attributes);
    }

    private static InodeRecord DecodeInodeValue(ReadOnlySpan<byte> value)
    {
        if (value.Length != sizeof(long) + sizeof(long) + sizeof(int))
            throw new InvalidDataException("Inode value has invalid length.");

        return new(
            BinaryPrimitives.ReadInt64LittleEndian(value),
            BinaryPrimitives.ReadInt64LittleEndian(value.Slice(sizeof(long), sizeof(long))),
            BinaryPrimitives.ReadInt32LittleEndian(value.Slice(sizeof(long) + sizeof(long), sizeof(int))));
    }

    private static InodeRecord ReadInodeValue(ref ChunkStreamCursor cursor, int valueLength)
    {
        const int inodeValueSize = sizeof(long) + sizeof(long) + sizeof(int);

        if (valueLength != inodeValueSize)
            throw new InvalidDataException("Inode value has invalid length.");

        Span<byte> value = stackalloc byte[inodeValueSize];
        cursor.ReadExactly(value);
        return DecodeInodeValue(value);
    }

    private static int GetAttributeKeyLength(ReadOnlySpan<char> attributeName)
        => checked(sizeof(long) + attributeName.Length * sizeof(char));

    private static void WriteAttributeKey(Span<byte> destination, long inodeId, ReadOnlySpan<char> attributeName)
    {
        if (destination.Length != GetAttributeKeyLength(attributeName))
            throw new ArgumentOutOfRangeException(nameof(destination));

        BinaryPrimitives.WriteInt64BigEndian(destination, inodeId);

        for (var i = 0; i < attributeName.Length; i++)
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(sizeof(long) + i * sizeof(char), sizeof(char)), attributeName[i]);
    }

    private static long DecodeAttributeKeyInodeId(ReadOnlySpan<byte> key)
    {
        if (key.Length < sizeof(long))
            throw new InvalidDataException("Attribute key is too short.");

        return BinaryPrimitives.ReadInt64BigEndian(key);
    }

    private static string ReadUtf16BigEndianString(ref ChunkStreamCursor cursor, int byteCount, string invalidLengthMessage)
    {
        if ((byteCount & 1) != 0)
            throw new InvalidDataException(invalidLengthMessage);

        if (byteCount == 0)
            return string.Empty;

        var chars = new char[byteCount / sizeof(char)];
        Span<byte> pair = stackalloc byte[sizeof(char)];

        for (var i = 0; i < chars.Length; i++)
        {
            cursor.ReadExactly(pair);
            chars[i] = (char)BinaryPrimitives.ReadUInt16BigEndian(pair);
        }

        return new string(chars);
    }

    private static void ValidateUtf16BigEndianByteCount(int byteCount, string invalidLengthMessage)
    {
        if ((byteCount & 1) != 0)
            throw new InvalidDataException(invalidLengthMessage);
    }

    private static void WriteInt64LittleValue(Span<byte> destination, long value)
    {
        if (destination.Length != sizeof(long))
            throw new ArgumentOutOfRangeException(nameof(destination));

        BinaryPrimitives.WriteInt64LittleEndian(destination, value);
    }
}
