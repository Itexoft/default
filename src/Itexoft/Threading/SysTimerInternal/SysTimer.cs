// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading.Atomics.Memory;

namespace Itexoft.Threading.SysTimerInternal;

internal abstract class SysTimer
{
    private static readonly AtomicDenseRam<SysTimer> memory = new();
    private readonly bool callIfCancelled;
    private protected readonly nuint handle;
    private protected readonly int millesecondsTimeout;
    private protected Disposed disposed = new();
    private Action source;
    private Latch started = new();

    private protected SysTimer(int millesecondsTimeout, bool callIfCancelled, Action source)
    {
        this.callIfCancelled = callIfCancelled;
        this.source = source.Required();
        this.millesecondsTimeout = millesecondsTimeout.RequiredPositive();
        this.handle = memory.Alloc();
        memory.Ref(this.handle) = this;
    }

    public static SysTimer New(int millesecondsTimeout, bool callIfCancelled, Action source)
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS() || OperatingSystem.IsWatchOS())
            return new SysTimerMacIos(millesecondsTimeout, callIfCancelled, source);

        if (OperatingSystem.IsLinux())
            return new SysTimerLinux(millesecondsTimeout, callIfCancelled, source);

        if (OperatingSystem.IsBrowser())
            return new SysTimerBrowserWasm(millesecondsTimeout, callIfCancelled, source);

        if (OperatingSystem.IsWindows())
            return new SysTimerWindows(millesecondsTimeout, callIfCancelled, source);

        if (OperatingSystem.IsAndroid())
            return new SysTimerAndroid(millesecondsTimeout, callIfCancelled, source);

        throw new PlatformNotSupportedException("Current platform is not supported by SysTimer.");
    }

    public bool IsStarted => this.started;
    public bool IsFinished => this.disposed;

    public void Wait() => this.disposed.Wait();

    public void Start()
    {
        if (!this.started.Try())
            return;

        this.StartInternal();
    }

    private protected abstract void StartInternal();

    private protected abstract void DisposeInternal();

    private void Invoke(bool cancelled)
    {
        if (this.disposed.Enter())
            return;

        memory.Free(this.handle);

        if (!cancelled || this.callIfCancelled)
            this.source();
    }

    protected static void Invoke(nuint handle, bool cancelled) => memory.Ref(handle).Invoke(cancelled);
}
