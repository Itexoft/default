// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.IO.FileSystem;

public sealed class RelativeFileSystem(IFileSystem inner, string basePath, bool strict = true) : IFileSystem
{
    private readonly string basePathFull = Path.GetFullPath(basePath.RequiredNotWhiteSpace());
    private readonly IFileSystem inner = inner.Required();

    public IStreamRwsl<byte> Open(string path, SysFileMode mode) => this.inner.Open(this.ResolvePath(path), mode);

    public void CreateDirectory(string path) => this.inner.CreateDirectory(this.ResolvePath(path));

    public bool Exists(string path) => this.inner.Exists(this.ResolvePath(path));

    public bool FileExists(string path) => this.inner.FileExists(this.ResolvePath(path));

    public bool DirectoryExists(string path) => this.inner.DirectoryExists(this.ResolvePath(path));

    public void Delete(string path, bool recursive = true) => this.inner.Delete(this.ResolvePath(path), recursive);

    public void DeleteFile(string path) => this.inner.DeleteFile(this.ResolvePath(path));

    public void DeleteDirectory(string path, bool recursive = true) => this.inner.DeleteDirectory(this.ResolvePath(path), recursive);

    public IReadOnlyList<string> Enumerate(string path) => this.inner.Enumerate(this.ResolvePath(path));

    private string ResolvePath(string path)
    {
        path = path.RequiredNotWhiteSpace();

        if (strict && Path.IsPathRooted(path))
            throw new ArgumentException("Absolute path is not allowed in strict mode.", nameof(path));

        var fullPath = Path.IsPathRooted(path) ? Path.GetFullPath(path) : Path.GetFullPath(Path.Combine(this.basePathFull, path));

        if (!strict)
            return fullPath;

        if (fullPath.Equals(this.basePathFull, StringComparison.Ordinal))
            return fullPath;

        var prefix = this.basePathFull.EndsWith(Path.DirectorySeparatorChar) || this.basePathFull.EndsWith(Path.AltDirectorySeparatorChar)
            ? this.basePathFull
            : this.basePathFull + Path.DirectorySeparatorChar;

        if (fullPath.StartsWith(prefix, StringComparison.Ordinal))
            return fullPath;

        throw new ArgumentException($"Path '{path}' escapes the base path.", nameof(path));
    }
}
