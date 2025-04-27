// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Splicing;

public enum SpliceNativeStatus : byte
{
    Ok = 0,
    InputError,
    OwnerMismatch,
    UnboundCue,
    Backpressure,
    ClosedPromise,
    DoubleBind,
    ConcurrentWriter,
    Disposed,
    CrossThreadAccess,
    RuntimeNull,
    TokenEmpty,
    PromiseTokenEmpty,
    ClipNotPromise,
    UnknownError
}

public sealed class SpliceNativeException(SpliceNativeStatus status) : Exception
{
    public SpliceNativeStatus Status { get; } = status;
}