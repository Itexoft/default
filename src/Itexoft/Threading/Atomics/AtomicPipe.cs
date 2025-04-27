// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;

namespace Itexoft.Threading.Atomics;

public struct AtomicPipe<T>()
{
    private T value = default!;
    private Latch canRead = new(true);
    private Latch canWrite = new(false);

    public T GetValue()
    {
        for (var wi = 0; !this.canRead.Try(); wi++)
            Spin.Wait(ref wi);

        var value = this.value;
        this.canWrite.Reset();

        return value;
    }

    public void SetValue(T value)
    {
        for (var wi = 0; !this.canWrite.Try(); wi++)
            Spin.Wait(ref wi);

        this.value = value;
        this.canRead.Reset();
    }

    public bool ResetRead() => this.canRead.Reset();
    public bool ResetWrite() => this.canWrite.Reset();
}
