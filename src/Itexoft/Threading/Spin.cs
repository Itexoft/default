// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading;

public static class Spin
{
    private const int shortSpin = 32;
    private const int longSpin = shortSpin << 2;
    private const int yieldMask = 15;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Step(ref int i) => unchecked(++i);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Wait(ref int i)
    {
        var step = Step(ref i);

        if ((step & yieldMask) != 0)
        {
            Thread.SpinWait(shortSpin);
            return;
        }

        if (!Thread.Yield())
            Thread.SpinWait(longSpin);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Until(Func<bool> func)
    {
        func.Required();

        for (var i = 0; !func();)
            Wait(ref i);
    }

    public static void Delay(TimeSpan delay, CancelToken cancelToken = default)
    {
        if (delay == TimeSpan.Zero)
            return;

        if (delay < TimeSpan.Zero)
        {
            for (var i = 0;; cancelToken.ThrowIf())
                Wait(ref i);
        }

        var start = TimeUtils.CachedTimestamp;

        for (var i = 0;; cancelToken.ThrowIf())
        {
            if (TimeUtils.CachedTimestamp - start > delay)
                return;

            Wait(ref i);
        }
    }

    extension(ref Disposed disposed)
    {
        public void Wait()
        {
            for (var i = 0; !disposed;)
                Wait(ref i);
        }
    }
}
