// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Net.Http;

namespace Itexoft.EmbeddedWeb;

public sealed class EmbeddedWebResponseContext
{
    internal EmbeddedWebResponseContext(EmbeddedWebStaticFile file, string relativePath, NetHttpMethod method, NetHttpHeaders headers)
    {
        file = file ?? throw new ArgumentNullException(nameof(file));
        this.RelativePath = relativePath ?? throw new ArgumentNullException(nameof(relativePath));
        this.Method = method;
        this.Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        this.Content = file.Content;
        this.Length = file.Length;
        this.LastModified = file.LastModified;
    }

    public string RelativePath { get; }
    public NetHttpMethod Method { get; }
    public NetHttpHeaders Headers { get; }
    public ReadOnlyMemory<byte> Content { get; }
    public long Length { get; }
    public DateTimeOffset LastModified { get; }
}
