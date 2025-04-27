// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading.Atomics;

namespace Itexoft.IO;

internal struct PositionalByteStreamSync
{
    internal AtomicLock Cursor;
    internal AtomicRangeClaims64 Claims;

    internal byte EnterReadRange(long offset, int length) => this.Claims.EnterShared(offset, length);
    internal byte EnterWriteRange(long offset, int length) => this.Claims.EnterExclusive(offset, length);
    internal void ExitRange(byte slot) => this.Claims.Exit(slot);
}
