// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

#if !NativeAOT || BlazorWebAssembly

using System.Reflection;
using System.Runtime.InteropServices;

namespace Itexoft.Native;

internal static class DefaultNativeLibrary
{
    public const string Name = "itexoft-native";

    internal static void Init() => NativeLibrary.SetDllImportResolver(typeof(NativeResolver).Assembly, Resolve);

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, Name, StringComparison.Ordinal))
            return nint.Zero;

        return NativeResolver.LoadLibrary(libraryName, assembly, searchPath);
    }
}
#endif
