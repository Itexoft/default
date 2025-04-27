// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Threading.Atomics.Native;

[StructLayout(LayoutKind.Sequential, Size = 8, Pack = 1)]
public readonly unsafe struct AtomicLogicalBitMemory
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AtomicLogicalBitMemory(ulong capacityBits)
    {
        var words = AtomicBitMemoryCommon.WordsForBits(capacityBits);
        var bytes = checked(AtomicLogicalBitMemoryExtensions.HeaderSize + (nuint)words * AtomicLane64.Size);
        var ptr = NativeMemory.AllocZeroed(bytes);
        Unsafe.AsRef<ulong>(ptr) = capacityBits;
        var value = (nuint)ptr;
        this = Unsafe.As<nuint, AtomicLogicalBitMemory>(ref value);
    }
}

public static unsafe class AtomicLogicalBitMemoryExtensions
{
    internal static nuint HeaderSize => (nuint)sizeof(ulong);

    extension(scoped ref readonly AtomicLogicalBitMemory memory)
    {
        private void* Ptr => Unsafe.As<AtomicLogicalBitMemory, nuint>(ref Unsafe.AsRef(in memory)).ToPointer();
        private AtomicLane64* DataPtr => (AtomicLane64*)((byte*)memory.Ptr + HeaderSize);
        private ref ulong CapacityBits => ref Unsafe.AsRef<ulong>(memory.Ptr);

        private bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => memory.Ptr is null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIf()
        {
            if (memory.IsNull)
                throw new ObjectDisposedException(nameof(AtomicLogicalBitMemory));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void RequireBitIndex(ulong index)
        {
            if (index >= memory.CapacityBits)
                throw new ArgumentOutOfRangeException(nameof(index));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBitSet(ulong index)
        {
            memory.ThrowIf();
            memory.RequireBitIndex(index);

            return memory.DataPtr[index >> AtomicBitMemoryCommon.LaneBits].IsBitSet((byte)(index & AtomicBitMemoryCommon.LaneMask));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TrySetBit(ulong index)
        {
            memory.ThrowIf();
            memory.RequireBitIndex(index);

            return memory.DataPtr[index >> AtomicBitMemoryCommon.LaneBits].TrySetBit((byte)(index & AtomicBitMemoryCommon.LaneMask));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryClearBit(ulong index)
        {
            memory.ThrowIf();
            memory.RequireBitIndex(index);

            return memory.DataPtr[index >> AtomicBitMemoryCommon.LaneBits].TryClearBit((byte)(index & AtomicBitMemoryCommon.LaneMask));
        }
    }

    extension(ref AtomicLogicalBitMemory memory)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            var ptr = Unsafe.As<AtomicLogicalBitMemory, nuint>(ref memory).ToPointer();

            if (ptr is not null)
                NativeMemory.Free(ptr);

            memory = default;
        }
    }
}
