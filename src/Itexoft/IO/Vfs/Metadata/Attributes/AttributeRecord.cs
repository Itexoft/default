// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Immutable;
using Itexoft.IO.Vfs.Metadata.Models;

namespace Itexoft.IO.Vfs.Metadata.Attributes;

internal sealed record AttributeRecord
{
    public FileId FileId { get; init; }
    public string Name { get; init; } = string.Empty;
    public ImmutableArray<byte> Data { get; init; } = ImmutableArray<byte>.Empty;
    public DateTime CreatedUtc { get; init; }
    public DateTime ModifiedUtc { get; init; }
}
