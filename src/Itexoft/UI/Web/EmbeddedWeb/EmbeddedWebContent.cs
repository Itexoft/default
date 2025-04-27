// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;

namespace Itexoft.UI.Web.EmbeddedWeb;

internal sealed class EmbeddedWebContent(Dictionary<string, EmbeddedWebStaticFile> files)
{
    public static readonly DateTimeOffset DefaultLastModified = DateTimeOffset.UnixEpoch;

    private readonly Dictionary<string, EmbeddedWebStaticFile> files = files ?? throw new ArgumentNullException(nameof(files));

    public IReadOnlyCollection<string> Paths => this.files.Keys;

    public bool TryGetFile(string path, [MaybeNullWhen(false)] out EmbeddedWebStaticFile file) =>
        this.files.TryGetValue(NormalizeRequestPath(path), out file);

    private static string NormalizeRequestPath(string path)
    {
        path = path.Replace('\\', '/');
        path = path.Trim('/');

        return path;
    }
}

internal sealed class EmbeddedWebStaticFile(byte[] content, DateTimeOffset lastModified)
{
    public byte[] Content { get; } = content ?? throw new ArgumentNullException(nameof(content));
    public DateTimeOffset LastModified { get; } = lastModified;
    public long Length => this.Content.Length;

    public Stream CreateReadStream() => new MemoryStream(this.Content, false);
}
