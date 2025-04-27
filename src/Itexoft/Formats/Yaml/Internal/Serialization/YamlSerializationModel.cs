// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;

namespace Itexoft.Formats.Yaml.Internal.Serialization;

internal enum SerializationNodeKind
{
    Scalar,
    Sequence,
    Mapping,
    Alias,
}

internal sealed record class SerializationMappingEntryOccurrence(SyntaxId Id, OccurrenceId KeyId, OccurrenceId ValueId);

internal abstract class SerializationOccurrence(OccurrenceId id, SyntaxId syntaxId, SerializationNodeKind kind, PresentationNodeProperties properties)
{
    public OccurrenceId Id { get; } = id;

    public SyntaxId SyntaxId { get; } = syntaxId;

    public SerializationNodeKind Kind { get; } = kind;

    public PresentationNodeProperties Properties { get; } = properties;
}

internal sealed class SerializationScalarOccurrence(
    OccurrenceId id,
    SyntaxId syntaxId,
    PresentationNodeProperties properties,
    PresentationScalarNode scalar) : SerializationOccurrence(id, syntaxId, SerializationNodeKind.Scalar, properties)
{
    public PresentationScalarNode Scalar { get; } = scalar;
}

internal sealed class SerializationAliasOccurrence(
    OccurrenceId id,
    SyntaxId syntaxId,
    PresentationNodeProperties properties,
    string aliasName,
    OccurrenceId targetId) : SerializationOccurrence(id, syntaxId, SerializationNodeKind.Alias, properties)
{
    public string AliasName { get; } = aliasName;

    public OccurrenceId TargetId { get; } = targetId;
}

internal sealed class SerializationSequenceOccurrence(
    OccurrenceId id,
    SyntaxId syntaxId,
    PresentationNodeProperties properties,
    bool isFlow,
    IReadOnlyList<OccurrenceId> items) : SerializationOccurrence(id, syntaxId, SerializationNodeKind.Sequence, properties)
{
    public bool IsFlow { get; } = isFlow;

    public IReadOnlyList<OccurrenceId> Items { get; } = items;
}

internal sealed class SerializationMappingOccurrence(
    OccurrenceId id,
    SyntaxId syntaxId,
    PresentationNodeProperties properties,
    bool isFlow,
    IReadOnlyList<SerializationMappingEntryOccurrence> entries) : SerializationOccurrence(id, syntaxId, SerializationNodeKind.Mapping, properties)
{
    public bool IsFlow { get; } = isFlow;

    public IReadOnlyList<SerializationMappingEntryOccurrence> Entries { get; } = entries;
}

internal sealed class SerializationDocument(
    SyntaxId syntaxId,
    OccurrenceId rootId,
    IReadOnlyDictionary<OccurrenceId, SerializationOccurrence> occurrences)
{
    public SyntaxId SyntaxId { get; } = syntaxId;

    public OccurrenceId RootId { get; } = rootId;

    public IReadOnlyDictionary<OccurrenceId, SerializationOccurrence> Occurrences { get; } = occurrences;
}

internal sealed class SerializationForest(IReadOnlyList<SerializationDocument> documents)
{
    public IReadOnlyList<SerializationDocument> Documents { get; } = documents;
}
