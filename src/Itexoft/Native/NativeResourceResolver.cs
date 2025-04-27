// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

#if !NativeAOT || BlazorWebAssembly

using System.Reflection;
using System.Runtime.InteropServices;

namespace Itexoft.Native;

internal static class NativeResourceResolver
{
    private const string ResourcePrefix = "Itexoft.NativePayload";

    private static readonly object sync = new();
    private static readonly Assembly assembly = typeof(NativeResourceResolver).Assembly;
    private static readonly string assemblyDirectory = GetAssemblyDirectory();
    private static readonly string runtimeIdentifier = GetRuntimeIdentifier();

    internal static string? ResolveLibraryPath(string libraryName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryName);

        var libraryFileName = GetLibraryFileName(libraryName);
        var targetPath = Path.Combine(assemblyDirectory, libraryFileName);

        if (File.Exists(targetPath))
            return targetPath;

        var resourceName = GetResourceName(libraryFileName);

        lock (sync)
        {
            if (File.Exists(targetPath))
                return targetPath;

            using var resource = assembly.GetManifestResourceStream(resourceName);

            if (resource == null)
                return null;

            using var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            resource.CopyTo(file);
        }

        return targetPath;
    }

    private static string GetResourceName(string libraryFileName) => $"{ResourcePrefix}.{runtimeIdentifier}.{libraryFileName}";

    private static string GetAssemblyDirectory()
    {
        if (string.IsNullOrWhiteSpace(assembly.Location))
            throw new InvalidOperationException("Native resolver assembly location is empty.");

        var directory = Path.GetDirectoryName(assembly.Location);

        if (string.IsNullOrWhiteSpace(directory))
            throw new InvalidOperationException("Native resolver assembly directory is empty.");

        return directory;
    }

    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.Arm64 => "win-arm64",
                _ => throw new PlatformNotSupportedException("Unsupported Windows architecture"),
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.Arm64 => "linux-arm64",
                _ => throw new PlatformNotSupportedException("Unsupported Linux architecture"),
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "osx-x64",
                Architecture.Arm64 => "osx-arm64",
                _ => throw new PlatformNotSupportedException("Unsupported macOS architecture"),
            };
        }

        throw new PlatformNotSupportedException("Unsupported OS");
    }

    private static string GetLibraryFileName(string libraryName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return $"{libraryName}.dll";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return $"{libraryName}.so";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return $"{libraryName}.dylib";

        throw new PlatformNotSupportedException("Unsupported OS");
    }
}
#endif
