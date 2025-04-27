// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;

namespace Itexoft.Threading;

/// <summary>
/// Thread-safe lazy holder with explicit lifetime control and safe disposal.
/// </summary>
public readonly struct DeferredPool<TResult>
{
    private readonly Deferred<TResult>[] pool;
    private readonly Latch[] locks;

    /// <summary>
    /// Thread-safe lazy holder with explicit lifetime control and safe disposal.
    /// </summary>
    [method: MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DeferredPool(int size, Func<TResult> factory)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        this.pool = GC.AllocateUninitializedArray<Deferred<TResult>>(size, false);
        this.locks = new Latch[size];

        for (var i = 0; i < size; i++)
            this.pool[i] = new Deferred<TResult>(factory);
    }

    public readonly DisposeOneOffAction GetValue(out TResult value)
    {
        var locks = this.locks;

        for (var i = 0;; i = (i + 1) % this.locks.Length)
        {
            if (this.locks[i].Try())
            {
                value = this.pool[i].Value;

                return new DisposeOneOffAction(() => locks[i].Reset());
            }
        }
    }

    public IEnumerable<TResult> GetCreatedValues()
    {
        foreach (var value in this.pool)
        {
            if (value.TryGetValueIfCreated(out var result))
                yield return result;
        }
    }
}
