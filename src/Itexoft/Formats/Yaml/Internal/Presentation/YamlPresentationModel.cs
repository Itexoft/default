// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Core;

namespace Itexoft.Formats.Yaml.Internal.Presentation;

internal enum PresentationTriviaKind
{
    BlankLine,
    Comment,
}

internal enum ScalarStyle
{
    Plain,
    SingleQuoted,
    DoubleQuoted,
    Literal,
    Folded,
}

internal enum TagPropertyKind
{
    Verbatim,
    Shorthand,
    NonSpecific,
}

internal sealed class PresentationTrivia(SyntaxId id, PresentationTriviaKind kind, YamlTextFragment rawText)
{
    public SyntaxId Id { get; } = id;

    public PresentationTriviaKind Kind { get; } = kind;

    public YamlTextFragment RawTextFragment { get; } = rawText;

    public YamlException.SourceSpan Span => this.RawTextFragment.Span;

    public string Text => this.RawTextFragment.Text;
}

internal sealed class PresentationDirective(SyntaxId id, YamlTextFragment rawText)
{
    public SyntaxId Id { get; } = id;

    public YamlTextFragment RawTextFragment { get; } = rawText;

    public string RawText => this.RawTextFragment.Text;

    public YamlException.SourceSpan Span => this.RawTextFragment.Span;
}

internal readonly record struct PresentationTagProperty(YamlTextFragment RawTextFragment, string Value, TagPropertyKind Kind)
{
    public string RawText => this.RawTextFragment.Text;

    public YamlException.SourceSpan Span => this.RawTextFragment.Span;
}

internal readonly record struct PresentationAnchorProperty(YamlTextFragment RawTextFragment, string Value)
{
    public string RawText => this.RawTextFragment.Text;

    public YamlException.SourceSpan Span => this.RawTextFragment.Span;
}

internal readonly record struct PresentationNodeProperties(PresentationTagProperty? Tag, PresentationAnchorProperty? Anchor);

internal readonly record struct BlockScalarHeader(
    char Indicator,
    int? IndentationIndicator,
    char? ChompingIndicator,
    YamlTextFragment RawTextFragment)
{
    public string RawText => this.RawTextFragment.Text;
}

internal sealed class PresentationStream(
    YamlSourceText source,
    IReadOnlyList<PresentationDocument> documents,
    IReadOnlyList<PresentationTrivia> trivia)
{
    public YamlSourceText Source { get; } = source;

    public IReadOnlyList<PresentationDocument> Documents { get; } = documents;

    public IReadOnlyList<PresentationTrivia> Trivia { get; } = trivia;
}

internal sealed class PresentationDocument(
    SyntaxId id,
    YamlTextFragment sourceText,
    PresentationNode? root,
    IReadOnlyList<PresentationDirective> directives,
    IReadOnlyList<PresentationTrivia> trivia,
    bool hasStartMarker,
    bool hasEndMarker,
    YamlException.SourceSpan span)
{
    public SyntaxId Id { get; } = id;

    public YamlTextFragment SourceTextFragment { get; } = sourceText;

    public string SourceText => this.SourceTextFragment.Text;

    public PresentationNode? Root { get; } = root;

    public IReadOnlyList<PresentationDirective> Directives { get; } = directives;

    public IReadOnlyList<PresentationTrivia> Trivia { get; } = trivia;

    public bool HasStartMarker { get; } = hasStartMarker;

    public bool HasEndMarker { get; } = hasEndMarker;

    public YamlException.SourceSpan Span { get; } = span;
}

internal abstract class PresentationNode(SyntaxId id, PresentationNodeProperties properties, YamlException.SourceSpan span)
{
    public SyntaxId Id { get; } = id;

    public PresentationNodeProperties Properties { get; } = properties;

    public YamlException.SourceSpan Span { get; } = span;
}

internal sealed class PresentationScalarNode(
    SyntaxId id,
    PresentationNodeProperties properties,
    YamlException.SourceSpan span,
    ScalarStyle style,
    YamlTextFragment rawText,
    string logicalText,
    BlockScalarHeader? blockHeader,
    bool isImplicitNull = false) : PresentationNode(id, properties, span)
{
    public ScalarStyle Style { get; } = style;

    public YamlTextFragment RawTextFragment { get; } = rawText;

    public string RawText => this.RawTextFragment.Text;

    public string LogicalText { get; } = logicalText;

    public BlockScalarHeader? BlockHeader { get; } = blockHeader;

    public bool IsImplicitNull { get; } = isImplicitNull;
}

internal sealed class PresentationAliasNode(
    SyntaxId id,
    PresentationNodeProperties properties,
    YamlException.SourceSpan span,
    YamlTextFragment rawText,
    string alias) : PresentationNode(id, properties, span)
{
    public YamlTextFragment RawTextFragment { get; } = rawText;

    public string RawText => this.RawTextFragment.Text;

    public string Alias { get; } = alias;
}

internal sealed record class PresentationSequenceItem(SyntaxId Id, YamlException.SourceSpan Span, PresentationNode Node);

internal sealed record class PresentationMappingEntry(
    SyntaxId Id,
    YamlException.SourceSpan Span,
    PresentationNode Key,
    PresentationNode Value,
    bool IsExplicit);

internal sealed class PresentationSequenceNode(
    SyntaxId id,
    PresentationNodeProperties properties,
    YamlException.SourceSpan span,
    bool isFlow,
    IReadOnlyList<PresentationSequenceItem> items) : PresentationNode(id, properties, span)
{
    public bool IsFlow { get; } = isFlow;

    public IReadOnlyList<PresentationSequenceItem> Items { get; } = items;
}

internal sealed class PresentationMappingNode(
    SyntaxId id,
    PresentationNodeProperties properties,
    YamlException.SourceSpan span,
    bool isFlow,
    IReadOnlyList<PresentationMappingEntry> entries) : PresentationNode(id, properties, span)
{
    public bool IsFlow { get; } = isFlow;

    public IReadOnlyList<PresentationMappingEntry> Entries { get; } = entries;
}
