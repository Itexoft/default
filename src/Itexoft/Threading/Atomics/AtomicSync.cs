// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;

namespace Itexoft.Threading.Atomics;

public struct AtomicSync<T>()
{
    private T value = default!;
    private Latch reset = new(true);
    private Latch canRead = new(true);
    private Latch canWrite = new(false);

    public bool LoadValue(ref T value)
    {
        for (var wi = 0; !this.canRead.Try(); wi++)
            Spin.Wait(ref wi);

        value = this.value;
        this.canWrite.Reset();

        return true;
    }

    public void SaveValue(T value)
    {
        for (var wi = 0; !this.canWrite.Try(); wi++)
            Spin.Wait(ref wi);

        this.value = value;
        this.canRead.Reset();
    }

    public bool Reset() => this.reset.Try();
    public bool ResetRead() => this.canRead.Reset();
    public bool ResetWrite() => this.canWrite.Reset();
}
