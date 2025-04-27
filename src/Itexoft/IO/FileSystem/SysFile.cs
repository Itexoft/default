// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.IO.FileSystem;

public sealed class SysFile : IFileSystem
{
    private const int defaultBufferSize = 8192;
    internal SysFile() { }

    public IStreamRwsl<byte> Open(string path, SysFileMode mode) =>
        new FileStream(RequirePath(path), GetMode(mode), GetAccess(mode), FileShare.None, defaultBufferSize, FileOptions.None).AsStreamRwsl();

    public void CreateDirectory(string path) => Directory.CreateDirectory(RequirePath(path));

    public bool Exists(string path)
    {
        var fullPath = RequirePath(path);

        return File.Exists(fullPath) || Directory.Exists(fullPath);
    }

    public bool FileExists(string path) => File.Exists(RequirePath(path));

    public bool DirectoryExists(string path) => Directory.Exists(RequirePath(path));

    public void Delete(string path, bool recursive = false)
    {
        var fullPath = RequirePath(path);

        if (File.Exists(fullPath))
        {
            this.DeleteFile(fullPath);

            return;
        }

        if (Directory.Exists(fullPath))
        {
            this.DeleteDirectory(fullPath, recursive);

            return;
        }

        throw new FileNotFoundException($"Path '{fullPath}' was not found.", fullPath);
    }

    public void DeleteFile(string path)
    {
        var fullPath = RequirePath(path);
        File.Delete(fullPath);
    }

    public void DeleteDirectory(string path, bool recursive = false)
    {
        var fullPath = RequirePath(path);
        Directory.Delete(fullPath, recursive);
    }

    public IReadOnlyList<string> Enumerate(string path)
    {
        var fullPath = RequirePath(path);

        return Directory.EnumerateFileSystemEntries(fullPath).Select(Path.GetFileName).Select(name => name.Required()).ToArray();
    }

    private static FileMode GetMode(SysFileMode mode)
    {
        if (mode.HasFlag(SysFileMode.Overwrite))
            return FileMode.Create;

        if (mode.HasFlag(SysFileMode.Write))
            return FileMode.OpenOrCreate;

        if (mode.HasFlag(SysFileMode.Read))
            return FileMode.Open;

        throw new InvalidOperationException("Invalid mode.");
    }

    private static FileAccess GetAccess(SysFileMode mode)
    {
        if (mode.HasFlag(SysFileMode.Read) && mode.HasFlag(SysFileMode.Write))
            return FileAccess.ReadWrite;

        if (mode.HasFlag(SysFileMode.Read))
            return FileAccess.Read;

        if (mode.HasFlag(SysFileMode.Overwrite) || mode.HasFlag(SysFileMode.Write))
            return FileAccess.Write;

        throw new InvalidOperationException("Invalid mode.");
    }

    public IStreamRwsl<byte> Create(string path, FileAttributes attributes = FileAttributes.Normal, FileOptions options = FileOptions.None)
    {
        var fullPath = RequirePath(path);
        var stream = new FileStream(fullPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, defaultBufferSize, options);
        File.SetAttributes(fullPath, attributes);

        return stream.AsStreamRwsl();
    }

    private static string RequirePath(string path) => Path.GetFullPath(path.RequiredNotWhiteSpace());
}
