// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;

namespace Itexoft.Threading.SysTimerInternal;

internal sealed partial class SysTimerMacIos(int millesecondsTimeout, bool callIfCancelled, Action source)
    : SysTimer(millesecondsTimeout, callIfCancelled, source)
{
    private const ulong dispatchTimeForever = ulong.MaxValue;
    private const nint rtldDefault = (nint)(-2);
    private static readonly DispatchFunction sEventHandler = OnEvent;
    private static readonly DispatchFunction sCancelHandler = OnCancel;
    private nint source;

    private protected override void StartInternal()
    {
        var type = Native.dlsym(rtldDefault, "_dispatch_source_type_timer");

        if (type == nint.Zero)
            throw new InvalidOperationException("dlsym(_dispatch_source_type_timer) failed.");

        var queue = Native.dispatch_get_global_queue(0, 0);

        if (queue == nint.Zero)
            throw new InvalidOperationException("dispatch_get_global_queue failed.");

        this.source = Native.dispatch_source_create(type, nuint.Zero, nuint.Zero, queue);

        if (this.source == nint.Zero)
            throw new InvalidOperationException("dispatch_source_create failed.");

        Native.dispatch_set_context(this.source, this.handle);

        var start = Native.dispatch_time(0, (long)this.millesecondsTimeout * 1_000_000L);
        Native.dispatch_source_set_timer(this.source, start, dispatchTimeForever, 0);

        Native.dispatch_source_set_event_handler_f(this.source, sEventHandler);
        Native.dispatch_source_set_cancel_handler_f(this.source, sCancelHandler);

        Native.dispatch_resume(this.source);
    }

    private protected override void DisposeInternal()
    {
        Native.dispatch_source_cancel(this.source);
        Native.dispatch_release(this.source);
    }

    private static void OnEvent(nuint context) => Invoke(context, false);

    private static void OnCancel(nuint context) => Invoke(context, true);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void DispatchFunction(nuint context);

    private static partial class Native
    {
        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_get_global_queue", SetLastError = true)]
        internal static partial nint dispatch_get_global_queue(nint identifier, nuint flags);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_source_create", SetLastError = true)]
        internal static partial nint dispatch_source_create(nint type, nuint handle, nuint mask, nint queue);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_source_set_timer", SetLastError = true)]
        internal static partial void dispatch_source_set_timer(nint source, ulong start, ulong interval, ulong leeway);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_time", SetLastError = true)]
        internal static partial ulong dispatch_time(ulong when, long delta);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_set_context", SetLastError = true)]
        internal static partial void dispatch_set_context(nint obj, nuint context);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_source_set_event_handler_f", SetLastError = true)]
        internal static partial void dispatch_source_set_event_handler_f(nint source, DispatchFunction handler);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_source_set_cancel_handler_f", SetLastError = true)]
        internal static partial void dispatch_source_set_cancel_handler_f(nint source, DispatchFunction handler);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_resume", SetLastError = true)]
        internal static partial void dispatch_resume(nint obj);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_source_cancel", SetLastError = true)]
        internal static partial void dispatch_source_cancel(nint source);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dispatch_release", SetLastError = true)]
        internal static partial void dispatch_release(nint obj);

        [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "dlsym", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
        internal static partial nint dlsym(nint handle, string symbol);
    }
}
