// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;

namespace Itexoft.Threading.SysTimerInternal;

internal sealed partial class SysTimerWindows(int millesecondsTimeout, bool callIfCancelled, Action source)
    : SysTimer(millesecondsTimeout, callIfCancelled, source)
{
    private static readonly TpTimerCallback sCallback = OnTimer;
    private nint timer;

    private protected override unsafe void StartInternal()
    {
        this.timer = Native.CreateThreadpoolTimer(sCallback, this.handle, nint.Zero);

        if (this.timer == nint.Zero)
            throw new InvalidOperationException("CreateThreadpoolTimer failed.");

        var dueTime = -(long)this.millesecondsTimeout * 10_000L;

        Native.SetThreadpoolTimer(this.timer, (nint)(&dueTime), 0, 0);
    }

    private protected override void DisposeInternal()
    {
        Native.SetThreadpoolTimer(this.timer, nint.Zero, 0, 0);
        Native.CloseThreadpoolTimer(this.timer);
    }

    private static void OnTimer(nint instance, nuint context, nint t) => Invoke(context, false);

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate void TpTimerCallback(nint instance, nuint context, nint timer);

    private static partial class Native
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static partial nint CreateThreadpoolTimer(TpTimerCallback pfnti, nuint pv, nint pcbe);

        [LibraryImport("kernel32.dll", EntryPoint = "SetThreadpoolTimer", SetLastError = true)]
        internal static partial void SetThreadpoolTimer(nint timer, nint pftDueTime, uint msPeriod, uint msWindowLength);

        [LibraryImport("kernel32.dll", EntryPoint = "CloseThreadpoolTimer", SetLastError = true)]
        internal static partial void CloseThreadpoolTimer(nint timer);
    }
}
