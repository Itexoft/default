// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;

namespace Itexoft.Interop;

public static class InteropExtensions
{
    public static Span<T> AsSpan<T>(ref this T value) where T : unmanaged => MemoryMarshal.CreateSpan(ref value, 1);
    public static Memory<T> AsMemory<T>(this T value) where T : unmanaged => new([value]);
}
