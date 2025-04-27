// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Text;

namespace Itexoft.Native;

internal static partial class NativeLibraryLoader
{
    private const string resourcePrefix = "Itexoft.Native.Library." + Name;
    public const string Name = "itexoft-native";
    private static readonly Lock sync = new();
    private static string? libPath;
    private static nint libraryHandle;

    internal static void RegisterResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(NativeLibraryLoader).Assembly, Resolve);
    }

    private static nint Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(name, NativeLibraryLoader.Name, StringComparison.Ordinal))
            return nint.Zero;

        if (libraryHandle != nint.Zero)
            return libraryHandle;

        var path = EnsureExtracted();
        libraryHandle = NativeLibrary.Load(path);

        return libraryHandle;
    }

    private static string EnsureExtracted()
    {
        if (libPath is not null)
            return libPath;

        lock (sync)
        {
            if (libPath is not null)
                return libPath;

            var extension = GetNativeExtension();
            var rid = RuntimeInformation.RuntimeIdentifier;
            var assembly = typeof(NativeLibraryLoader).Assembly;
            var resourceName = $"{resourcePrefix}.{rid}{extension}";
            var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                resourceName = FindResourceName(assembly, extension);

                if (resourceName.Length != 0)
                    stream = assembly.GetManifestResourceStream(resourceName);
            }

            if (stream is null)
                throw new InvalidOperationException($"Missing embedded native resource '{resourceName}'.");

            using var input = stream;

            if (OperatingSystem.IsBrowser())
            {
                var bytes = Encoding.UTF8.GetBytes(wasmModuleSource);
                var wasmModuleUrl = "data:text/javascript;base64," + Convert.ToBase64String(bytes);
                JSHost.ImportAsync(resourcePrefix, wasmModuleUrl).GetAwaiter().GetResult();
                using var ms = new MemoryStream();
                input.CopyTo(ms);

                return libPath = LoadNativeWasm(ms.ToArray(), Name);
            }
            else
            {
                var baseDir = AppContext.BaseDirectory;
                var fileName = Name + extension;
                var outputPath = Path.Combine(baseDir, fileName);

                if (!File.Exists(outputPath))
                {
                    using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.Read);
                    input.CopyTo(output);
                }

                return libPath = outputPath;
            }
        }
    }

    private static string FindResourceName(Assembly assembly, string extension)
    {
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (name.StartsWith(resourcePrefix + ".", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                return name;
        }

        return string.Empty;
    }

    private static string GetNativeExtension()
    {
        if (OperatingSystem.IsBrowser())
            return ".wasm";

        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return ".dylib";
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            return ".so";
        
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ".dll";
        
        throw new NotSupportedException($"Unsupported OS: {RuntimeInformation.OSDescription}");
    }

    [JSImport("loadNative", resourcePrefix)]
    private static partial string LoadNativeWasm(byte[] wasmBytes, string name);

    private const string wasmModuleSource = """
const loaded = new Map();

function toU8(value) {
  if (value instanceof Uint8Array) return value;
  if (value instanceof ArrayBuffer) return new Uint8Array(value);
  return new Uint8Array(value);
}

export function loadNative(wasmBytes, name) {
  const libName = (name && name.length) ? name : "itexoft-native";
  if (loaded.has(libName)) return loaded.get(libName);

  const bytes = toU8(wasmBytes);
  const path = "/lib" + libName + ".so";

  if (!globalThis.FS) throw new Error("FS is not available.");
  if (!FS.analyzePath(path).exists) FS.writeFile(path, bytes);

  if (globalThis.Module && typeof Module.loadDynamicLibrary === "function") {
    Module.loadDynamicLibrary(path, { global: true, nodelete: true });
  } else if (typeof globalThis.loadDynamicLibrary === "function") {
    globalThis.loadDynamicLibrary(path, { global: true, nodelete: true });
  } else {
    throw new Error("Dynamic loader is not available.");
  }

  loaded.set(libName, path);
  return path;
}
""";
}
