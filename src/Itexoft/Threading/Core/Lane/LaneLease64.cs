// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Core.Lane;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly ref struct LaneLease64(LaneIdPool64 pool, Lane64 lane)
{
    private readonly LaneIdPool64 pool = pool ?? throw new ArgumentNullException(nameof(pool));
    public readonly Lane64 Lane = lane;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose() => this.pool.Release(in this.Lane);
}

public static class LaneIdPool64Extensions
{
    extension(LaneIdPool64 pool)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LaneLease64 AcquireLease()
        {
            if (pool is null)
                throw new ArgumentNullException(nameof(pool));

            var lane = pool.Acquire();

            return new(pool, lane);
        }
    }
}
