// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;

namespace Itexoft.Threading.SysTimerInternal;

internal sealed partial class SysTimerLinux(int millesecondsTimeout, bool callIfCancelled, Action source)
    : SysTimer(millesecondsTimeout, callIfCancelled, source)
{
    private const int sigevThread = 2;
    private const int clockMonotonic = 1;

    private static readonly PosixTimerCallback sCallback = OnTimer;
    private static readonly nint sCallbackPtr = Marshal.GetFunctionPointerForDelegate(sCallback);

    private nint timerId;

    private protected override unsafe void StartInternal()
    {
        var sev = stackalloc byte[128];
        new Span<byte>(sev, 128).Clear();

        *(nuint*)(sev + 0) = this.handle;
        *(int*)(sev + nint.Size) = 0;
        *(int*)(sev + nint.Size + 4) = sigevThread;

        var unionOffset = nint.Size + 8;
        *(nint*)(sev + unionOffset) = sCallbackPtr;
        *(nint*)(sev + unionOffset + nint.Size) = nint.Zero;

        var rc = Native.timer_create(clockMonotonic, (nint)sev, out this.timerId);

        if (rc != 0)
        {
            Invoke(this.handle, true);

            throw new InvalidOperationException("timer_create failed, errno=" + Marshal.GetLastWin32Error());
        }

        var spec = new Itimerspec
        {
            it_interval = default,
            it_value = ToTimespec(this.millesecondsTimeout),
        };

        var rcSet = Native.timer_settime(this.timerId, 0, in spec, nint.Zero);

        if (rcSet != 0)
        {
            Invoke(this.handle, true);

            throw new InvalidOperationException("timer_settime failed, errno=" + Marshal.GetLastWin32Error());
        }
    }

    private protected override void DisposeInternal() => Native.timer_delete(this.timerId);

    private static void OnTimer(nuint sigval) => Invoke(sigval, false);

    private static Timespec ToTimespec(int millesecondsTimeout)
    {
        var sec = millesecondsTimeout / 1000;
        var ms = millesecondsTimeout - sec * 1000;

        return new Timespec
        {
            tv_sec = (nint)sec,
            tv_nsec = (nint)(ms * 1_000_000),
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Timespec
    {
        public nint tv_sec;
        public nint tv_nsec;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Itimerspec
    {
        public Timespec it_interval;
        public Timespec it_value;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void PosixTimerCallback(nuint sigval);

    private static partial class Native
    {
        [LibraryImport("librt.so.1", EntryPoint = "timer_create", SetLastError = true)]
        internal static partial int timer_create(int clockid, nint sevp, out nint timerid);

        [LibraryImport("librt.so.1", EntryPoint = "timer_settime", SetLastError = true)]
        internal static partial int timer_settime(nint timerid, int flags, in Itimerspec newValue, nint oldValue);

        [LibraryImport("librt.so.1", EntryPoint = "timer_delete", SetLastError = true)]
        internal static partial int timer_delete(nint timerid);
    }
}
