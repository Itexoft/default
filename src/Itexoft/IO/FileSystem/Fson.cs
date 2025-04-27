// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.IO.FileSystem;
using Itexoft.Text.Codecs;
using Itexoft.Threading;

namespace Itexoft.Formats.Fson;

public readonly struct FsonDir : IIterable<TildeSeg>
{
    internal TildePath Path { get; }
    internal IFileSystem Fs { get; }

    private FsonDir(FsonDir parent, TildeSeg segment)
    {
        this.Fs = parent.Fs;
        this.Path = parent.Path / segment;
    }

    public FsonDir(IFileSystem fs)
    {
        this.Fs = fs.Required();
        this.Path = new TildePath();
    }

    public FsonDir(IFileSystem fs, string relativePath)
    {
        this.Fs = new RelativeFileSystem(fs.Required(), relativePath);
        this.Path = new TildePath();
    }

    public bool Add(in TildeSeg key, string value) => this.Add(in key, value, out _);

    public bool Add(in TildeSeg key, string value, out FsonFile file)
    {
        var childPath = FsonFsPath.Get(this.Path, key);

        if (this.Fs.Exists(childPath))
        {
            file = default;

            return false;
        }

        this.Fs.CreateDirectory(FsonFsPath.Get(this.Path));

        using (var stream = this.Fs.OpenString(childPath, SysFileMode.Overwrite))
            stream.WriteAllText(value);

        file = new(this, key);

        return true;
    }

    public bool AddDir(in TildeSeg key, out FsonDir dir)
    {
        var childPath = FsonFsPath.Get(this.Path, key);

        if (this.Fs.Exists(childPath))
        {
            dir = default;

            return false;
        }

        this.Fs.CreateDirectory(childPath);
        dir = new(this, key);

        return true;
    }

    public bool Remove(in TildeSeg key)
    {
        var childPath = FsonFsPath.Get(this.Path, key);

        if (!this.Fs.Exists(childPath))
            return false;

        this.Fs.Delete(childPath, true);

        return true;
    }

    public bool GetDir(in TildeSeg key, out FsonDir dir)
    {
        var childPath = FsonFsPath.Get(this.Path, key);

        if (!this.Fs.DirectoryExists(childPath))
        {
            dir = default;

            return false;
        }

        dir = new(this, key);

        return true;
    }

    public bool GetFile(in TildeSeg key, out FsonFile dir)
    {
        var childPath = FsonFsPath.Get(this.Path, key);

        if (!this.Fs.FileExists(childPath))
        {
            dir = default;

            return false;
        }

        dir = new(this, key);

        return true;
    }

    public IEnumerator<TildeSeg> GetEnumerator()
    {
        var entries = this.Fs.Enumerate(FsonFsPath.Get(this.Path));

        for (var i = 0; i < entries.Count; i++)
            yield return new TildeSeg(entries[i], false);
    }

    public bool IsDir(in TildeSeg key) => this.Fs.DirectoryExists(FsonFsPath.Get(this.Path, key));

    public bool IsFile(in TildeSeg key) => this.Fs.FileExists(FsonFsPath.Get(this.Path, key));
}

public readonly struct FsonFile
{
    private readonly Lock @lock = new();
    public TildePath Path { get; }
    private IFileSystem Fs { get; }

    internal FsonFile(FsonDir parent, TildeSeg segment)
    {
        this.Path = parent.Path / segment;
        this.Fs = parent.Fs;
    }

    public string String
    {
        get
        {
            lock (this.@lock)
            {
                using var stream = this.Fs.OpenString(FsonFsPath.Get(this.Path), SysFileMode.Read);

                return stream.ReadAllText();
            }
        }
        set
        {
            lock (this.@lock)
            {
                using var stream = this.Fs.OpenString(FsonFsPath.Get(this.Path), SysFileMode.Overwrite);
                stream.WriteAllText(value);
            }
        }
    }

    public ReadOnlyMemory<byte> Bytes
    {
        get
        {
            lock (this.@lock)
            {
                using var stream = this.Fs.Open(FsonFsPath.Get(this.Path), SysFileMode.Read);

                return stream.ReadToEnd();
            }
        }
        set
        {
            lock (this.@lock)
            {
                using var stream = this.Fs.Open(FsonFsPath.Get(this.Path), SysFileMode.Overwrite);
                stream.Overwrite(value.Span);
            }
        }
    }

    public void Append(string text, CancelToken cancelToken = default)
    {
        lock (this.@lock)
        {
            using (var stream = this.Fs.OpenString(FsonFsPath.Get(this.Path), SysFileMode.Write))
            {
                stream.SeekEnd();
                stream.Write(text, cancelToken);
            }
        }
    }

    public void AppendLine(string text, CancelToken cancelToken = default)
    {
        lock (this.@lock)
        {
            using (var stream = this.Fs.OpenString(FsonFsPath.Get(this.Path), SysFileMode.Write))
            {
                stream.SeekEnd();
                stream.WriteLine(text, cancelToken);
            }
        }
    }

    //public IStreamRwsl<byte> GetStream() => this.Fs.Open(this.Path, SysFileMode.Read | SysFileMode.Write);
    //public CharStream GetCharStream() => this.Fs.OpenString(this.Path, SysFileMode.Read | SysFileMode.Write);
}

file static class FsonFsPath
{
    public static string Get(TildePath path)
    {
        var text = path.ToString();

        if (text.Length == 1)
            return ".";

        return text[1..].Replace('/', Path.DirectorySeparatorChar);
    }

    public static string Get(TildePath path, TildeSeg segment) => Get(path / segment);
}
