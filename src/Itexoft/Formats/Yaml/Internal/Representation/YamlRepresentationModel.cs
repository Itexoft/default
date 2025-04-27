// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Numerics;
using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;

namespace Itexoft.Formats.Yaml.Internal.Representation;

internal enum RepresentationNodeKind
{
    Scalar,
    Sequence,
    Mapping,
}

internal enum RepresentationTagStateKind
{
    Explicit,
    Resolved,
    Unresolved,
}

internal enum ScalarCanonicalKind
{
    None,
    Null,
    Boolean,
    Integer,
    Float,
    String,
}

internal readonly record struct RepresentationTagState(RepresentationTagStateKind Kind, string Tag);

internal readonly record struct ScalarCanonicalForm(
    ScalarCanonicalKind Kind,
    string Text,
    bool BooleanValue = default,
    BigInteger IntegerValue = default,
    double FloatValue = default);

internal sealed record class RepresentationMappingEntry(SemanticNodeId KeyId, SemanticNodeId ValueId);

internal abstract class RepresentationNode(SemanticNodeId id, RepresentationNodeKind kind, RepresentationTagState tagState)
{
    public SemanticNodeId Id { get; } = id;

    public RepresentationNodeKind Kind { get; } = kind;

    public RepresentationTagState TagState { get; } = tagState;
}

internal sealed class RepresentationScalarNode(
    SemanticNodeId id,
    RepresentationTagState tagState,
    string logicalText,
    ScalarStyle style,
    ScalarCanonicalForm canonicalForm,
    bool isImplicitNull = false) : RepresentationNode(id, RepresentationNodeKind.Scalar, tagState)
{
    public string LogicalText { get; } = logicalText;

    public ScalarStyle Style { get; } = style;

    public ScalarCanonicalForm CanonicalForm { get; } = canonicalForm;

    public bool IsImplicitNull { get; } = isImplicitNull;
}

internal sealed class RepresentationSequenceNode(SemanticNodeId id, RepresentationTagState tagState, IReadOnlyList<SemanticNodeId> items)
    : RepresentationNode(id, RepresentationNodeKind.Sequence, tagState)
{
    public IReadOnlyList<SemanticNodeId> Items { get; } = items;
}

internal sealed class RepresentationMappingNode(SemanticNodeId id, RepresentationTagState tagState, IReadOnlyList<RepresentationMappingEntry> entries)
    : RepresentationNode(id, RepresentationNodeKind.Mapping, tagState)
{
    public IReadOnlyList<RepresentationMappingEntry> Entries { get; } = entries;
}

internal sealed class RepresentationGraph(
    SemanticNodeId rootId,
    IReadOnlyDictionary<SemanticNodeId, RepresentationNode> nodes,
    IReadOnlyDictionary<SemanticNodeId, int> occurrenceCounts,
    bool isPartial)
{
    public SemanticNodeId RootId { get; } = rootId;

    public IReadOnlyDictionary<SemanticNodeId, RepresentationNode> Nodes { get; } = nodes;

    public IReadOnlyDictionary<SemanticNodeId, int> OccurrenceCounts { get; } = occurrenceCounts;

    public bool IsPartial { get; } = isPartial;
}
