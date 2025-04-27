// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices.JavaScript;

namespace Itexoft.Threading.SysTimerInternal;

internal sealed partial class SysTimerBrowserWasm(int millesecondsTimeout, bool callIfCancelled, Action source)
    : SysTimer(millesecondsTimeout, callIfCancelled, source)
{
    private bool hasTimeoutId;
    private int timeoutId;

    private protected override void StartInternal()
    {
        this.timeoutId = Native.SetTimeout(this.TimeoutAction, this.millesecondsTimeout);
        this.hasTimeoutId = true;
    }

    private void TimeoutAction() => Invoke(this.handle, false);

    private protected override void DisposeInternal()
    {
        if (!this.hasTimeoutId)
            return;

        this.hasTimeoutId = false;
        Native.ClearTimeout(this.timeoutId);
    }

    private static partial class Native
    {
        [JSImport("globalThis.setTimeout")]
        internal static partial int SetTimeout([JSMarshalAs<JSType.Function>] Action callback, int delayMilliseconds);

        [JSImport("globalThis.clearTimeout")]
        internal static partial void ClearTimeout(int timeoutId);
    }
}
