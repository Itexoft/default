// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Runtime.CompilerServices;
using Itexoft.Threading.ControlFlow;

namespace Itexoft.Core;

public static class TimeUtils
{
    private static readonly double tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
    private static readonly long initTimestamp = RealTimestamp;
    private static InvokeGateLatch<long> timestampLatch = InvokeGate.Latch(GetTimestamp);

    public static long RealTimestamp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => GetTimestamp();
    }

    public static TimeSpan CachedTimestamp
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromTicks(timestampLatch.Invoke());
    }

    public static long CachedTimestampTicks
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => timestampLatch.Invoke();
    }

    public static int CachedTimestampMs
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (int)(timestampLatch.Invoke() / TimeSpan.TicksPerMillisecond);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long GetTimestamp() => (long)((double)Stopwatch.GetTimestamp() * tickFrequency) - initTimestamp;
}
