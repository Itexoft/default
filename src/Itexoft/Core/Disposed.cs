// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using Itexoft.Threading;

namespace Itexoft.Core;

[StructLayout(LayoutKind.Explicit, Size = 1)]
public readonly record struct Disposed
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(in Disposed disposed)
    {
        return Atomic.Read(ref Unsafe.As<Disposed, byte>(ref Unsafe.AsRef(in disposed))) > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void ThrowIf(
        ExceptionDispatchInfo? exception = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        exception?.Throw();

        if (this)
            throw new ObjectDisposedException(callerMember, $"Cannot access a disposed object: {callerMember} ({callerFile}:{callerLine}).");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void ThrowIf(
        in CancelToken cancelToken,
        ExceptionDispatchInfo? exception = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        this.ThrowIf(exception, callerMember, callerFile, callerLine);
        cancelToken.ThrowIf();
    }

    public override string ToString() => ((bool)this).ToString();
}

public static class DisposedExtensions
{
    extension(ref Disposed disposed)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Enter() => Interlocked.Exchange(ref Unsafe.As<Disposed, byte>(ref disposed), 1) > 0;
    }
}
