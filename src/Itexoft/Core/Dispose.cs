// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Threading;

namespace Itexoft.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public readonly struct DisposeAction(Action? action) : IDisposable
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator DisposeAction(Action? action) => new(action);

    public void Dispose() => action?.Invoke();
}

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
public struct DisposeOneOffAction(Action? action) : IDisposable
{
    private Action? action = action;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator DisposeOneOffAction(Action? action) => new(action);

    public bool IsDisposed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Atomic.Read(ref this.action) is null;
    }

    public void Dispose()
    {
        if (Atomic.NullOut(ref this.action, out var action))
            action.Invoke();
    }
}
