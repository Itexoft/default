// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Extensions;

namespace Itexoft.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct SysTimeout(TimeSpan timeout)
{
    private readonly TimeSpan timeout = timeout;
    private long start = TimeUtils.CachedTimestampTicks;

    public TimeSpan Start
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromTicks(this.start);
    }

    public TimeSpan Elapsed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => TimeSpan.FromTicks(TimeUtils.CachedTimestampTicks - this.start);
    }

    public bool Expired
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !this.timeout.IsInfinite && this.Elapsed > this.timeout;
    }

    public TimeSpan Timeout
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.timeout;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reset() => this.start = TimeUtils.CachedTimestampTicks;
}
