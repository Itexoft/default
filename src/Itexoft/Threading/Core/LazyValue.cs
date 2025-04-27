// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.Core;

public struct LazyValue<T>
{
    private T value;
    private Latch hasValue;
    private Latch factoryCalled = new();
    private Func<T>? factory;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LazyValue(T value)
    {
        this.value = value;
        this.hasValue = new Latch(true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public LazyValue(Func<T> factory)
    {
        this.value = default!;
        this.hasValue = new Latch(false);
        this.factory = factory.Required();
    }

    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!this.hasValue)
                return this.value;

            if (!this.factoryCalled.Try())
            {
                var spinner = new SpinWait();

                while (Volatile.Read(ref this.factory) != null)
                    spinner.SpinOnce();

                return this.value;
            }

            this.value = Interlocked.Exchange(ref this.factory, null)!();

            return this.value;
        }
    }
}
