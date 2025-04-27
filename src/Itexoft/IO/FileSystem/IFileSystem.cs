// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.IO.Streams.Chars;

namespace Itexoft.IO.FileSystem;

public interface IFileSystem
{
    public static IFileSystem Sys { get; } = new SysFile();

    IStreamRwsl<byte> Open(string path, SysFileMode mode);

    void CreateDirectory(string path);

    bool Exists(string path);

    bool FileExists(string path);

    bool DirectoryExists(string path);

    void Delete(string path, bool recursive = true);

    void DeleteFile(string path);

    void DeleteDirectory(string path, bool recursive = true);

    IReadOnlyList<string> Enumerate(string path);
}

[Flags]
public enum SysFileMode
{
    Read = 1 << 0,
    Write = 1 << 1,
    Overwrite = (1 << 2) | Write,
}

public static class FileSystemExtensions
{
    extension(IFileSystem fileSystem)
    {
        public CharStream OpenString(string path, SysFileMode mode) => fileSystem.OpenString(path, mode, Encoding.UTF8);
        public CharStream OpenString(string path, SysFileMode mode, Encoding encoding) => new(fileSystem.Open(path, mode), encoding);
    }
}
