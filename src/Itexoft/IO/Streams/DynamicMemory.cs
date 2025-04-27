// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Extensions;

namespace Itexoft.IO.Streams;

[StructLayout(LayoutKind.Sequential, Size = 8, Pack = 1)]
public readonly unsafe struct DynamicMemory<T> where T : unmanaged
{
    public static readonly nuint TSize = (nuint)sizeof(T);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DynamicMemory(int initCapacity) : this((nuint)initCapacity.RequiredPositiveOrZero()) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DynamicMemory(nuint initCapacity)
    {
        var byteCount = checked(TSize * initCapacity);
        var ptr = NativeMemory.AllocZeroed(DynamicMemoryExtensions.HeaderSize + byteCount);
        Unsafe.AsRef<nuint>(ptr) = initCapacity;
        var ptrValue = (nuint)ptr;
        this = Unsafe.As<nuint, DynamicMemory<T>>(ref ptrValue);
    }

    public ref T this[Index index] => ref this.Get(index);
    public ReadOnlySpan<T> this[Range range] => this.Get(range);
}

public static unsafe class DynamicMemoryExtensions
{
    private static readonly nuint pSize = (nuint)sizeof(nuint);
    internal static nuint HeaderSize => (nuint)sizeof(nuint) * 3;

    extension<T>(scoped ref readonly DynamicMemory<T> dmi) where T : unmanaged
    {
        private void* Ptr => Unsafe.As<DynamicMemory<T>, nuint>(ref Unsafe.AsRef(in dmi)).ToPointer();
        private void* DataPtr => (Unsafe.As<DynamicMemory<T>, nuint>(ref Unsafe.AsRef(in dmi)) + HeaderSize).ToPointer();
        private ref nuint Pcapacity => ref Unsafe.AsRef<nuint>(dmi.Ptr);
        private ref nuint Plength => ref Unsafe.AddByteOffset(ref Unsafe.AsRef<nuint>(dmi.Ptr), pSize);
        private ref nuint Pposition => ref Unsafe.AddByteOffset(ref Unsafe.AsRef<nuint>(dmi.Ptr), pSize * 2);

        public nuint Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dmi.IsNull ? 0 : dmi.Pposition;
        }

        public nuint Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dmi.IsNull ? 0 : dmi.Plength;
        }

        public nuint Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dmi.IsNull ? 0 : dmi.Pcapacity;
        }

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dmi.IsNull || dmi.Plength == 0;
        }

        private bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => dmi.Ptr is null;
        }

        private void ThrowIf()
        {
            if (dmi.IsNull)
                throw new ObjectDisposedException(nameof(DynamicMemory<T>));
        }

        public ReadOnlySpan<T> Get(Range range)
        {
            dmi.ThrowIf();
            var (start, length) = range.GetOffsetAndLength(checked((int)dmi.Plength));

            return length == 0 ? [] : new((T*)dmi.DataPtr + start, length);
        }

        public ref T Get(Index index)
        {
            dmi.ThrowIf();

            var length = checked((int)dmi.Plength);
            var offset = index.GetOffset(length);

            if ((uint)offset >= (uint)length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return ref Unsafe.Add(ref Unsafe.AsRef<T>(dmi.DataPtr), offset);
        }

        public int Read(Span<T> buffer)
        {
            dmi.ThrowIf();

            if (buffer.IsEmpty)
                return 0;

            var length = (int)Math.Min((nuint)buffer.Length, dmi.Plength - dmi.Pposition);

            if (length == 0)
                return 0;

            new ReadOnlySpan<T>((T*)dmi.DataPtr + checked((int)dmi.Pposition), length).CopyTo(buffer);
            dmi.Pposition += (nuint)length;

            return length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<T> AsSpan()
        {
            dmi.ThrowIf();

            return dmi.Plength == 0 ? [] : new(dmi.DataPtr, checked((int)dmi.Plength));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<T> ToMemory()
        {
            if (dmi.IsEmpty)
                return ReadOnlyMemory<T>.Empty;

            var length = dmi.Plength;
            Memory<T> memory = new T[checked((int)length)];

            fixed (void* ptr = &memory.Span.GetPinnableReference())
                NativeMemory.Copy(dmi.DataPtr, ptr, length * DynamicMemory<T>.TSize);

            return memory;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Commit(nuint next)
        {
            dmi.Pposition = next;

            if (next > dmi.Plength)
                dmi.Plength = next;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Clear()
        {
            if (dmi.IsNull)
                return;

            dmi.Plength = 0;
            dmi.Pposition = 0;
        }
    }

    extension<T>(ref DynamicMemory<T> dmi) where T : unmanaged
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            if (dmi.Ptr is not null)
                NativeMemory.Free(dmi.Ptr);

            dmi = default;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(in T item)
        {
            var next = dmi.EnsureCapacity(1);
            Unsafe.WriteUnaligned((byte*)dmi.DataPtr + dmi.Pposition * DynamicMemory<T>.TSize, item);
            dmi.Commit(next);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Write(ReadOnlySpan<T> items)
        {
            if (items.IsEmpty)
                return;

            var start = dmi.Position;
            var next = dmi.EnsureCapacity((nuint)items.Length);
            items.CopyTo(new Span<T>((T*)dmi.DataPtr + checked((int)start), items.Length));
            dmi.Commit(next);
        }

        public void Advance(int length)
        {
            if (length.RequiredPositive() == 0)
                return;

            dmi.Commit(dmi.EnsureCapacity((nuint)length));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> GetSpan(int sizeHint = 64)
        {
            sizeHint.RequiredPositive();
            dmi.EnsureCapacity((nuint)sizeHint);

            return new Span<T>((T*)dmi.DataPtr + checked((int)dmi.Pposition), sizeHint);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private nuint EnsureCapacity(nuint offset)
        {
            if (dmi.IsNull)
            {
                dmi = new DynamicMemory<T>(offset);

                return offset;
            }

            offset = checked(dmi.Pposition + offset);

            if (offset <= dmi.Pcapacity)
                return offset;

            var newCapacity = Math.Max(offset, 1);

            if (dmi.Pcapacity > 0)
            {
                var growBy = dmi.Pcapacity < 64 ? dmi.Pcapacity : Math.Max(dmi.Pcapacity / 2, (nuint)8);
                var candidate = checked(dmi.Pcapacity + growBy);
                newCapacity = candidate < offset ? offset : candidate;
            }

            var byteCount = checked(DynamicMemory<T>.TSize * newCapacity);
            var ptr = (nuint)NativeMemory.Realloc(dmi.Ptr, HeaderSize + byteCount);
            dmi = Unsafe.As<nuint, DynamicMemory<T>>(ref ptr);
            dmi.Pcapacity = newCapacity;

            return offset;
        }
    }
}
