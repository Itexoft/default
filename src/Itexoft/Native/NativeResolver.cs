// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

#if !NativeAOT || BlazorWebAssembly

#region

using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using Itexoft.Extensions;

#endregion

namespace Itexoft.Native;

public static partial class NativeResolver
{
    private static readonly string? currentDirectory = OperatingSystem.IsBrowser() ? null : Path.GetDirectoryName(Environment.ProcessPath);

    private static readonly string? toolsDirectory = GetPackageRoot();
    private static readonly string runtimeIdentifier = GetRuntimeIdentifier();

    [DebuggerStepThrough]
    public static nint LoadLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (OperatingSystem.IsBrowser())
            return NativeLibrary.Load("__Internal", assembly, searchPath);

        var name = ResolveLibraryPath(libraryName);

        if (name == null)
            return nint.Zero;

        return NativeLibrary.Load(name, assembly, searchPath);
    }

    [SupportedOSPlatform("browser")]
    public static async Task<nint> LoadWasmLibrary(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        var handle = await WasmSideModuleLoadByName(libraryName);

        if (string.IsNullOrWhiteSpace(handle))
            throw new DllNotFoundException(libraryName);

        return LoadLibrary("__Internal", assembly, searchPath);
    }

    [JSImport("globalThis.itexoft.wasmSideModule.loadByName")]
    private static partial Task<string> WasmSideModuleLoadByName(string libraryName);

    [DebuggerStepThrough]
    public static string? ResolveLibraryPath(string name) => ResolvePath(false, name);

    [DebuggerStepThrough]
    public static string? ResolveExePath(string name) => ResolvePath(true, name);

    [DebuggerStepThrough]
    public static string? ResolveToolLibraryPath(string name) => ResolvePath(false, name, toolsDirectory);

    [DebuggerStepThrough]
    public static string? ResolveToolExePath(string name) => ResolvePath(true, name, toolsDirectory);

    [DebuggerStepThrough]
    public static string LibraryFromResource(Assembly resourceAssembly, string libraryName, string? targetName = null)
    {
        var targetPath = Path.Combine(currentDirectory ?? string.Empty, targetName ?? libraryName.RequiredNotWhiteSpace());
        targetPath = Path.ChangeExtension(targetPath, GetFileExt(executable: false));
        string? manifestName = null;

        foreach (var name in resourceAssembly.Required().GetManifestResourceNames())
        {
            var split = name.Split('.', '/', '\\');
            var i = 0;

            if (i >= split.Length || !split[i++].Equals(libraryName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i >= split.Length || !split[i++].Equals(runtimeIdentifier, StringComparison.OrdinalIgnoreCase))
                continue;

            if (i < split.Length && split[i].Equals("native", StringComparison.OrdinalIgnoreCase))
                i++;

            if (i >= split.Length || !split[i].StartsWith(libraryName, StringComparison.OrdinalIgnoreCase))
                continue;

            manifestName = name;

            break;
        }

        if (manifestName == null)
            throw new InvalidOperationException($"Native library resource not found: {libraryName}");

        if (!File.Exists(targetPath))
        {
            var dir = Path.GetDirectoryName(targetPath);

            if (string.IsNullOrWhiteSpace(dir))
                throw new InvalidOperationException("Native library output directory is empty.");

            Directory.CreateDirectory(dir);

            using var resource = resourceAssembly.GetManifestResourceStream(manifestName);

            if (resource == null)
                throw new IOException($"Resource {libraryName} does not exist.");

            using var file = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            resource.CopyTo(file);
        }

        return targetPath;
    }

    [DebuggerStepThrough]
    private static string? ResolvePath(bool executable, string name) => ResolvePath(executable, name, currentDirectory);

    [DebuggerStepThrough]
    private static string? ResolvePath(bool executable, string name, string? packageRoot)
    {
        if (OperatingSystem.IsBrowser())
            return null;

        if (packageRoot == null)
            return null;

        var fileName = Path.ChangeExtension(name, GetFileExt(executable));

        return Path.Combine(packageRoot, "runtimes", runtimeIdentifier, "native", fileName);
    }

    [DebuggerStepThrough]
    private static string? GetPackageRoot()
    {
        if (currentDirectory == null)
            return null;

        var dir = new DirectoryInfo(currentDirectory);

        while (dir is not null && !dir.Name.Equals("tools", StringComparison.OrdinalIgnoreCase))
            dir = dir.Parent;

        return dir?.Parent?.FullName;
    }

    [DebuggerStepThrough]
    private static string GetRuntimeIdentifier()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "win-x64",
                Architecture.X86 => "win-x86",
                Architecture.Arm64 => "win-arm64",
                Architecture.Arm => "win-arm",
                _ => throw new PlatformNotSupportedException("Unsupported Windows architecture"),
            };
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.X64 => "linux-x64",
                Architecture.X86 => "linux-x86",
                Architecture.Arm64 => "linux-arm64",
                Architecture.Arm => "linux-arm",
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

        if (OperatingSystem.IsBrowser())
            return "browser-wasm";

        throw new PlatformNotSupportedException("Unsupported OS");
    }

    [DebuggerStepThrough]
    private static string? GetFileExt(bool executable)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return executable ? "exe" : "dll";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return executable ? null : "so";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return executable ? null : "dylib";

        if (OperatingSystem.IsBrowser())
            return ".wasm";

        throw new PlatformNotSupportedException("Unsupported OS");
    }

    [DebuggerStepThrough]
    private sealed class ResolverKey(string libraryName, Assembly assembly) : IEquatable<ResolverKey>
    {
        public string LibraryName { get; } = libraryName;
        public Assembly Assembly { get; } = assembly;

        public bool Equals(ResolverKey? other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return this.LibraryName == other.LibraryName && this.Assembly.Equals(other.Assembly);
        }

        public override bool Equals(object? obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            if (obj.GetType() != this.GetType())
                return false;

            return this.Equals((ResolverKey)obj);
        }

        public override int GetHashCode() => HashCode.Combine(this.LibraryName, this.Assembly);
    }
}
#endif
