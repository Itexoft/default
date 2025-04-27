// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Runtime.InteropServices;
using Itexoft.Core;

namespace Itexoft.Native;

internal static class SslNativeLibrary
{
    private const string appleCryptoNative = "libSystem.Security.Cryptography.Native.Apple";
    private const string openSslCryptoNative = "libSystem.Security.Cryptography.Native.OpenSsl";

    private static Latch initialized = new();

    internal static void Init()
    {
        if (!initialized.Try())
            return;

        NativeLibrary.SetDllImportResolver(typeof(NativeResolver).Assembly, Resolve);
    }

    private static nint Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, appleCryptoNative, StringComparison.Ordinal)
            && !string.Equals(libraryName, openSslCryptoNative, StringComparison.Ordinal))
            return nint.Zero;

        var path = NativeResolver.ResolveLibraryPath(libraryName);

        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return nint.Zero;

        return NativeLibrary.Load(path, assembly, searchPath);
    }
}
