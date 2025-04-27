// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.IO;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BitStream<T> where T : unmanaged
{
    private static readonly ushort bytesPerValue = checked((ushort)Unsafe.SizeOf<T>());
    private static readonly bool isScalar = bytesPerValue <= sizeof(ulong);
    private static readonly int bitsPerByte = BitOperations.TrailingZeroCount((uint)byte.MaxValue + 1U);
    private static readonly int bitMask = bitsPerByte - 1;
    private static readonly ushort bitsPerValue = checked((ushort)(bytesPerValue * bitsPerByte));
    private readonly IStreamR<T>? source;
    private readonly bool trimTrailingZeros;
    private ulong register;
    private T value;
    private ulong pendingZeroValues;
    private ushort state;
    private ushort offset;
    private bool buffered;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitStream(IStreamR<T> source, bool trimTrailingZeros = true)
    {
        ArgumentNullException.ThrowIfNull(source);
        this.source = source;
        this.trimTrailingZeros = trimTrailingZeros;
        this.register = 0;
        this.value = default;
        this.pendingZeroValues = 0;
        this.state = 0;
        this.offset = 0;
        this.buffered = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public BitStream(T value, bool trimTrailingZeros = true)
    {
        this.source = null;
        this.trimTrailingZeros = trimTrailingZeros;
        this.register = isScalar ? LoadScalar(in value) : 0;
        this.value = value;
        this.pendingZeroValues = 0;
        this.state = trimTrailingZeros ? GetBitLength(in value) : bitsPerValue;
        this.offset = 0;
        this.buffered = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Read(out byte bit)
    {
        if (this.state == 0 && !this.TryRefill())
        {
            bit = 0;

            return false;
        }

        return isScalar ? this.ReadScalar(out bit) : this.ReadPacked(out bit);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator BitStream<T>(T value) => new(value);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRefill()
    {
        if (this.source is null)
            return false;

        return this.trimTrailingZeros ? this.TryRefillTrimmed() : this.TryRefillFullWidth();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRefillFullWidth()
    {
        if (this.source!.Read(MemoryMarshal.CreateSpan(ref this.value, 1)) == 0)
            return false;

        this.LoadCurrent(in this.value, bitsPerValue);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRefillTrimmed()
    {
        if (this.pendingZeroValues != 0)
        {
            this.pendingZeroValues--;
            this.LoadCurrent(default, bitsPerValue);

            return true;
        }

        if (this.buffered)
        {
            var current = this.value;
            this.buffered = false;

            return this.TryRefillFromCurrent(in current);
        }

        while (this.source!.Read(MemoryMarshal.CreateSpan(ref this.value, 1)) > 0)
        {
            if (GetBitLength(in this.value) == 0)
            {
                this.pendingZeroValues++;

                continue;
            }

            if (this.pendingZeroValues != 0)
            {
                this.buffered = true;
                this.pendingZeroValues--;
                this.LoadCurrent(default, bitsPerValue);

                return true;
            }

            var current = this.value;

            return this.TryRefillFromCurrent(in current);
        }

        this.pendingZeroValues = 0;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryRefillFromCurrent(in T current)
    {
        T next = default;
        ulong zeroRun = 0;

        while (this.source!.Read(MemoryMarshal.CreateSpan(ref next, 1)) > 0)
        {
            if (GetBitLength(in next) == 0)
            {
                zeroRun++;

                continue;
            }

            this.pendingZeroValues = zeroRun;
            this.value = next;
            this.buffered = true;
            this.LoadCurrent(in current, bitsPerValue);

            return true;
        }

        var width = GetBitLength(in current);

        if (width == 0)
            return false;

        this.LoadCurrent(in current, width);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void LoadCurrent(in T current, ushort width)
    {
        this.value = current;
        this.register = isScalar ? LoadScalar(in current) : 0;
        this.state = width;
        this.offset = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReadScalar(out byte bit)
    {
        bit = (byte)(this.register & 1);
        this.register >>= 1;
        this.state--;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ReadPacked(out byte bit)
    {
        ref var bytes = ref Unsafe.As<T, byte>(ref this.value);
        bit = (byte)((Unsafe.Add(ref bytes, this.offset / bitsPerByte) >> (this.offset & bitMask)) & 1);
        this.offset++;
        this.state--;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetBitLength(in T value)
    {
        ref var bytes = ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value));

        for (var i = bytesPerValue; i > 0;)
        {
            i--;
            var current = Unsafe.Add(ref bytes, i);

            if (current != 0)
                return checked((ushort)(i * bitsPerByte + BitOperations.Log2(current) + sizeof(byte)));
        }

        return 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong LoadScalar(in T value)
    {
        ref var bytes = ref Unsafe.As<T, byte>(ref Unsafe.AsRef(in value));
        ulong result = 0;

        for (var i = bytesPerValue; i > 0;)
        {
            i--;
            result = (result << bitsPerByte) | Unsafe.Add(ref bytes, i);
        }

        return result;
    }
}

public static class BitStreamExtensions
{
    extension<T>(IStreamR<T> source) where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public BitStream<T> AsBitStream(bool trimTrailingZeros = true) => new(source, trimTrailingZeros);
    }
}
