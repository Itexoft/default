// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly ref struct RefDispose<T>(ref T @ref) : IDisposable where T : IDisposable
{
    private readonly ref T @ref = ref @ref;

    public readonly bool IsNull { get; } = Unsafe.IsNullRef(ref @ref);

    public void Dispose()
    {
        if (!this.IsNull)
            this.@ref.Dispose();
    }
}
