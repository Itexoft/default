// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading;

/// <summary>
/// Thread-safe lazy holder with explicit lifetime control and safe disposal.
/// </summary>
[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct Deferred<TResult>(Func<TResult> factory)
{
    private Func<TResult>? factory = factory.Required();
    private Latch latch = new();
    private TResult value = default!;
    private ExceptionDispatchInfo? exceptionDispatchInfo;

    public TResult Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.GetValue();
    }

    public bool TryGetValueIfCreated(out TResult? value)
    {
        if (this.latch)
        {
            value = this.value;

            return true;
        }
        else
        {
            value = default;

            return false;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TResult GetValue()
    {
        if (this.factory is Func<TResult> factory && this.latch.Try())
        {
            try
            {
                this.value = factory();
            }
            catch (Exception ex)
            {
                this.exceptionDispatchInfo = ExceptionDispatchInfo.Capture(ex);

                throw;
            }
            finally
            {
                this.factory = null;
            }
        }
        else
        {
            this.exceptionDispatchInfo?.Throw();
            var sw = new SpinWait();

            while (this.factory is not null)
                sw.SpinOnce();

            this.exceptionDispatchInfo?.Throw();
        }

        return this.value;
    }

    public bool IsValueCreated => this.factory is null;
}
