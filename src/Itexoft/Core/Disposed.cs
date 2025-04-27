// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Core;

[method: MethodImpl(MethodImplOptions.AggressiveInlining)]
[StructLayout(LayoutKind.Explicit, Size = 1)]
public readonly record struct Disposed()
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static unsafe implicit operator bool(in Disposed disposed)
    {
        fixed (Disposed* d = &disposed)
            return ((byte*)d)[0] > 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void ThrowIf(
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "",
        [CallerLineNumber] int callerLine = 0)
    {
        if (this)
            throw new ObjectDisposedException(callerMember, $"Cannot access a disposed object: {callerMember} ({callerFile}:{callerLine}).");
    }
}

public static class DisposedExtensions
{
    extension(ref Disposed disposed)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe bool Enter()
        {
            fixed (Disposed* d = &disposed)
                return Interlocked.Exchange(ref ((byte*)d)[0], 1) > 0;
        }
    }
}
