// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Threading.Atomics;

namespace Itexoft.Threading.Core.Lane;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct EpochPublished64<T>() where T : unmanaged
{
    private AtomicState64<T> published;
    private Epoch64 epoch;

    static EpochPublished64()
    {
        if (Unsafe.SizeOf<T>() != sizeof(ulong))
            throw new InvalidOperationException($"{typeof(T)} must occupy exactly 8 bytes.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Read() => this.published.Read();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T Publish(T value)
    {
        this.published.Write(value);

        return value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int AdvanceAndWait() => this.epoch.AdvanceAndWait();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T EnterRead(in Lane64 lane, out int enteredEpoch)
    {
        enteredEpoch = this.epoch.Enter(in lane);

        return this.published.Read();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ExitRead(in Lane64 lane, int enteredEpoch) => this.epoch.Exit(in lane, enteredEpoch);
}
