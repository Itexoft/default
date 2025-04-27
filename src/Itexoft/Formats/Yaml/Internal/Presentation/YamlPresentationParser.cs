// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using System.Text;
using Itexoft.Formats.Yaml.Internal.Core;

namespace Itexoft.Formats.Yaml.Internal.Presentation;

internal sealed class YamlPresentationParser(YamlIdGenerator ids, YamlDiagnostics diagnostics)
{
    private List<PresentationTrivia> documentTrivia = null!;
    private IReadOnlyList<YamlLine> lines = null!;
    private YamlSourceText source = null!;
    private List<PresentationTrivia> streamTrivia = null!;

    public PresentationStream Parse(string text)
    {
        this.source = new(text ?? string.Empty);
        this.lines = YamlScanner.Scan(this.source, diagnostics);
        this.streamTrivia = [];
        var documents = new List<PresentationDocument>();
        var lineIndex = 0;

        this.ValidateBomPlacement();

        while (true)
        {
            this.documentTrivia = [];
            this.SkipIgnorable(ref lineIndex);

            if (this.IsTerminalLine(lineIndex))
                break;

            var directives = new List<PresentationDirective>();

            while (this.TryParseDirective(ref lineIndex, directives))
                this.SkipIgnorable(ref lineIndex);

            if (this.IsTerminalLine(lineIndex))
                break;

            var hasStartMarker = false;
            var hasEndMarker = false;
            var documentStart = this.lines[Math.Min(lineIndex, this.lines.Count - 1)].Start;

            if (this.TryConsumeDocumentMarker(ref lineIndex, "---"))
            {
                hasStartMarker = true;
                documentStart = this.lines[Math.Max(0, lineIndex - 1)].Start;
                this.SkipIgnorable(ref lineIndex);
            }

            PresentationNode root;

            if (this.IsTerminalLine(lineIndex) || this.IsDocumentMarker(lineIndex, "---") || this.IsDocumentMarker(lineIndex, "..."))
                root = this.CreateImplicitNullNode(documentStart, default);
            else
            {
                var line = this.lines[lineIndex];
                root = this.ParseNodeAtPosition(ref lineIndex, line.Indent, line.Start + line.Indent, default, true);
            }

            this.SkipIgnorable(ref lineIndex);

            if (this.TryConsumeDocumentMarker(ref lineIndex, "..."))
            {
                hasEndMarker = true;
                this.SkipIgnorable(ref lineIndex);
            }

            var documentEnd = lineIndex >= this.lines.Count
                ? this.source.Text.Length
                : this.lines[Math.Max(0, lineIndex - 1)].End + this.lines[Math.Max(0, lineIndex - 1)].BreakLength;

            var span = this.source.Lines.CreateSpan(documentStart, Math.Max(documentEnd - documentStart, 0));
            var sourceText = this.source.CreateFragment(documentStart, Math.Max(documentEnd - documentStart, 0));

            documents.Add(new(ids.NextSyntax(), sourceText, root, directives, [..this.documentTrivia], hasStartMarker, hasEndMarker, span));
        }

        return new(this.source, documents, this.streamTrivia);
    }

    private void ValidateBomPlacement()
    {
        for (var i = 0; i < this.source.Text.Length; i++)
        {
            if (this.source.Text[i] != '\uFEFF')
                continue;

            if (i == 0)
                continue;

            diagnostics.Add(
                "YAML011",
                YamlException.Phase.Decode,
                "BOM is only allowed at the start of the YAML stream.",
                this.source.Lines.CreateSpan(i, 1));
        }
    }

    private void SkipIgnorable(ref int lineIndex)
    {
        while (!this.IsTerminalLine(lineIndex))
        {
            var line = this.lines[lineIndex];

            if (!line.IsBlank && !line.IsCommentOnly)
                break;

            this.CaptureWholeLineTrivia(line);
            lineIndex++;
        }
    }

    private void CaptureWholeLineTrivia(YamlLine line)
    {
        var kind = line.IsBlank ? PresentationTriviaKind.BlankLine : PresentationTriviaKind.Comment;
        var span = this.source.Lines.CreateSpan(line.Start, line.End - line.Start + line.BreakLength);
        var trivia = new PresentationTrivia(ids.NextSyntax(), kind, this.source.CreateFragment(span));
        this.streamTrivia.Add(trivia);
        this.documentTrivia.Add(trivia);
    }

    private bool TryParseDirective(ref int lineIndex, ICollection<PresentationDirective> directives)
    {
        if (this.IsTerminalLine(lineIndex))
            return false;

        var line = this.lines[lineIndex];
        var start = line.Start + line.Indent;

        if (start >= line.End || this.source.Text[start] != '%')
            return false;

        directives.Add(new(ids.NextSyntax(), this.source.CreateFragment(start, line.End - start)));
        lineIndex++;

        return true;
    }

    private bool TryConsumeDocumentMarker(ref int lineIndex, string marker)
    {
        if (!this.IsDocumentMarker(lineIndex, marker))
            return false;

        lineIndex++;

        return true;
    }

    private bool IsDocumentMarker(int lineIndex, string marker)
    {
        if (this.IsTerminalLine(lineIndex))
            return false;

        var line = this.lines[lineIndex];
        var start = line.Start + line.Indent;

        if (line.Indent != 0 || line.End - start < marker.Length)
            return false;

        if (!this.source.Text.AsSpan(start, marker.Length).SequenceEqual(marker.AsSpan()))
            return false;

        var rest = this.source.Text.AsSpan(start + marker.Length, line.End - start - marker.Length);

        return rest.TrimStart().Length == 0 || rest.TrimStart()[0] == '#';
    }

    private bool IsTerminalLine(int lineIndex) => lineIndex >= this.lines.Count
                                                  || (lineIndex == this.lines.Count - 1 && this.lines[lineIndex].Start == this.source.Text.Length);

    private PresentationNode ParseNodeAtPosition(
        ref int lineIndex,
        int column,
        int position,
        PresentationNodeProperties inheritedProperties,
        bool allowSameIndentAfterProperties)
    {
        if (this.IsTerminalLine(lineIndex))
            return this.CreateImplicitNullNode(this.source.Text.Length, inheritedProperties);

        var line = this.lines[lineIndex];

        if (line.Indent > column && position == line.Start + line.Indent && !allowSameIndentAfterProperties)
            return this.ParseNodeAtPosition(ref lineIndex, line.Indent, line.Start + line.Indent, inheritedProperties, true);

        var properties = this.ParseProperties(line, ref position, inheritedProperties);
        this.SkipInlineSpaces(line, ref position);

        if (position >= line.End || this.source.Text[position] == '#')
        {
            this.CaptureInlineComment(line, position);
            lineIndex++;
            this.SkipIgnorable(ref lineIndex);

            if (this.IsTerminalLine(lineIndex) || this.IsDocumentMarker(lineIndex, "---") || this.IsDocumentMarker(lineIndex, "..."))
                return this.CreateImplicitNullNode(line.End, properties);

            var nextLine = this.lines[lineIndex];
            var nextColumn = allowSameIndentAfterProperties ? nextLine.Indent : Math.Max(nextLine.Indent, column + 1);

            return this.ParseNodeAtPosition(ref lineIndex, nextColumn, nextLine.Start + nextLine.Indent, properties, true);
        }

        var nodeColumn = position - line.Start;

        if (this.IsBlockSequenceLead(line, position))
            return this.ParseBlockSequence(ref lineIndex, nodeColumn, position, properties);

        if (this.IsExplicitKeyLead(line, position))
            return this.ParseBlockMapping(ref lineIndex, nodeColumn, position, properties);

        if (this.TryFindMappingColon(line, position, out _))
            return this.ParseBlockMapping(ref lineIndex, nodeColumn, position, properties);

        return this.ParseInlineValueNode(ref lineIndex, column, position, properties);
    }

    private PresentationNode ParseInlineValueNode(ref int lineIndex, int column, int position, PresentationNodeProperties properties)
    {
        var line = this.lines[lineIndex];
        var ch = this.source.Text[position];

        if (ch is '[' or '{' or '\'' or '"' or '*')
        {
            var (node, end) = this.ParseFlowNode(position, false, properties);
            this.FinalizeInlineNode(ref lineIndex, end);

            return node;
        }

        if (ch is '|' or '>')
            return this.ParseBlockScalar(ref lineIndex, line, position, properties);

        return this.ParsePlainScalar(ref lineIndex, line, position, properties);
    }

    private PresentationNode ParseBlockSequence(ref int lineIndex, int column, int position, PresentationNodeProperties properties)
    {
        var start = position;
        var items = new List<PresentationSequenceItem>();
        var first = true;

        while (!this.IsTerminalLine(lineIndex))
        {
            this.SkipIgnorable(ref lineIndex);

            if (this.IsTerminalLine(lineIndex) || this.IsDocumentMarker(lineIndex, "---") || this.IsDocumentMarker(lineIndex, "..."))
                break;

            var line = this.lines[lineIndex];
            var dashPos = first ? position : line.Start + column;

            if ((!first && line.Indent != column) || !this.IsBlockSequenceLead(line, dashPos))
                break;

            var itemStart = dashPos;
            var valuePos = dashPos + 1;
            var lineBefore = lineIndex;

            if (valuePos < line.End && this.source.Text[valuePos] == ' ')
                valuePos++;

            PresentationNode value;

            if (valuePos >= line.End || this.source.Text[valuePos] == '#')
            {
                this.CaptureInlineComment(line, valuePos);
                lineIndex++;
                value = this.ParseNestedOrImplicitNull(ref lineIndex, column + 1, false);
            }
            else
                value = this.ParseNodeAtPosition(ref lineIndex, column + 1, valuePos, default, true);

            var itemEnd = value.Span.Start + value.Span.Length;
            items.Add(new(ids.NextSyntax(), this.source.Lines.CreateSpan(itemStart, Math.Max(itemEnd - itemStart, 0)), value));
            first = false;

            if (lineIndex == lineBefore)
            {
                throw diagnostics.CreateException(
                    "YAML123",
                    YamlException.Phase.Parse,
                    "Block sequence parser made no progress.",
                    this.source.Lines.CreateSpan(itemStart, Math.Max(line.End - itemStart, 1)));
            }
        }

        var end = items.Count == 0 ? start : items[^1].Span.Start + items[^1].Span.Length;

        return new PresentationSequenceNode(
            ids.NextSyntax(),
            properties,
            this.source.Lines.CreateSpan(start, Math.Max(end - start, 1)),
            false,
            items);
    }

    private PresentationNode ParseBlockMapping(ref int lineIndex, int column, int position, PresentationNodeProperties properties)
    {
        var start = position;
        var entries = new List<PresentationMappingEntry>();
        var first = true;

        while (!this.IsTerminalLine(lineIndex))
        {
            this.SkipIgnorable(ref lineIndex);

            if (this.IsTerminalLine(lineIndex) || this.IsDocumentMarker(lineIndex, "---") || this.IsDocumentMarker(lineIndex, "..."))
                break;

            var line = this.lines[lineIndex];

            if (!first && line.Indent != column)
                break;

            var entryStart = first ? position : line.Start + column;
            var lineBefore = lineIndex;

            var entry = this.IsExplicitKeyLead(line, entryStart)
                ? this.ParseExplicitMappingEntry(ref lineIndex, column)
                : this.ParseImplicitMappingEntry(ref lineIndex, column, entryStart);

            entries.Add(entry);
            first = false;

            if (lineIndex == lineBefore)
            {
                throw diagnostics.CreateException(
                    "YAML124",
                    YamlException.Phase.Parse,
                    "Block mapping parser made no progress.",
                    this.source.Lines.CreateSpan(entryStart, Math.Max(line.End - entryStart, 1)));
            }
        }

        var end = entries.Count == 0 ? start : entries[^1].Span.Start + entries[^1].Span.Length;

        return new PresentationMappingNode(
            ids.NextSyntax(),
            properties,
            this.source.Lines.CreateSpan(start, Math.Max(end - start, 1)),
            false,
            entries);
    }

    private PresentationMappingEntry ParseExplicitMappingEntry(ref int lineIndex, int column)
    {
        var line = this.lines[lineIndex];
        var start = line.Start + column;
        var keyPos = start + 1;

        if (keyPos < line.End && this.source.Text[keyPos] == ' ')
            keyPos++;

        PresentationNode key;

        if (keyPos >= line.End || this.source.Text[keyPos] == '#')
        {
            this.CaptureInlineComment(line, keyPos);
            lineIndex++;
            key = this.ParseNestedOrImplicitNull(ref lineIndex, column + 1, false);
        }
        else
            key = this.ParseInlineValueNode(ref lineIndex, column + 1, keyPos, default);

        this.SkipIgnorable(ref lineIndex);

        if (this.IsTerminalLine(lineIndex))
            throw diagnostics.CreateException("YAML120", YamlException.Phase.Parse, "Explicit mapping key is missing ':' value indicator.", key.Span);

        var valueLine = this.lines[lineIndex];
        var colonPos = valueLine.Start + column;

        if (colonPos >= valueLine.End || this.source.Text[colonPos] != ':')
        {
            throw diagnostics.CreateException(
                "YAML121",
                YamlException.Phase.Parse,
                "Explicit mapping key must be followed by ':' on the next mapping line.",
                valueLine.Start < this.source.Text.Length
                    ? this.source.Lines.CreateSpan(colonPos, Math.Max(Math.Min(1, valueLine.End - colonPos), 0))
                    : null);
        }

        var valuePos = colonPos + 1;

        if (valuePos < valueLine.End && this.source.Text[valuePos] == ' ')
            valuePos++;

        PresentationNode value;

        if (valuePos >= valueLine.End || this.source.Text[valuePos] == '#')
        {
            this.CaptureInlineComment(valueLine, valuePos);
            lineIndex++;
            value = this.ParseNestedMappingValueOrImplicitNull(ref lineIndex, column);
        }
        else
            value = this.ParseNodeAtPosition(ref lineIndex, column + 1, valuePos, default, true);

        var end = value.Span.Start + value.Span.Length;

        return new(ids.NextSyntax(), this.source.Lines.CreateSpan(start, Math.Max(end - start, 1)), key, value, true);
    }

    private PresentationMappingEntry ParseImplicitMappingEntry(ref int lineIndex, int column, int? startOverride = null)
    {
        var line = this.lines[lineIndex];
        var start = startOverride ?? line.Start + column;

        if (!this.TryFindMappingColon(line, start, out var colon))
        {
            throw diagnostics.CreateException(
                "YAML122",
                YamlException.Phase.Parse,
                "Expected ':' mapping value indicator.",
                this.source.Lines.CreateSpan(start, Math.Max(line.End - start, 1)));
        }

        var key = this.ParseSegmentNode(start, colon, default);
        var valuePos = colon + 1;

        if (valuePos < line.End && this.source.Text[valuePos] == ' ')
            valuePos++;

        PresentationNode value;

        if (valuePos >= line.End || this.source.Text[valuePos] == '#')
        {
            this.CaptureInlineComment(line, valuePos);
            lineIndex++;
            value = this.ParseNestedMappingValueOrImplicitNull(ref lineIndex, column);
        }
        else
            value = this.ParseNodeAtPosition(ref lineIndex, column + 1, valuePos, default, true);

        var end = value.Span.Start + value.Span.Length;

        return new(ids.NextSyntax(), this.source.Lines.CreateSpan(start, Math.Max(end - start, 1)), key, value, false);
    }

    private PresentationNode ParseNestedOrImplicitNull(ref int lineIndex, int minimumIndent, bool allowSameIndent)
    {
        this.SkipIgnorable(ref lineIndex);

        if (this.IsTerminalLine(lineIndex) || this.IsDocumentMarker(lineIndex, "---") || this.IsDocumentMarker(lineIndex, "..."))
            return this.CreateImplicitNullNode(this.source.Text.Length, default);

        var nextLine = this.lines[lineIndex];

        if (allowSameIndent ? nextLine.Indent < minimumIndent : nextLine.Indent <= minimumIndent)
            return this.CreateImplicitNullNode(nextLine.Start, default);

        return this.ParseNodeAtPosition(ref lineIndex, nextLine.Indent, nextLine.Start + nextLine.Indent, default, true);
    }

    private PresentationNode ParseNestedMappingValueOrImplicitNull(ref int lineIndex, int column)
    {
        this.SkipIgnorable(ref lineIndex);

        if (this.IsTerminalLine(lineIndex) || this.IsDocumentMarker(lineIndex, "---") || this.IsDocumentMarker(lineIndex, "..."))
            return this.CreateImplicitNullNode(this.source.Text.Length, default);

        var nextLine = this.lines[lineIndex];

        if (nextLine.Indent < column)
            return this.CreateImplicitNullNode(nextLine.Start, default);

        if (nextLine.Indent == column && !this.IsBlockSequenceLead(nextLine, nextLine.Start + nextLine.Indent))
            return this.CreateImplicitNullNode(nextLine.Start, default);

        return this.ParseNodeAtPosition(ref lineIndex, nextLine.Indent, nextLine.Start + nextLine.Indent, default, true);
    }

    private PresentationScalarNode ParsePlainScalar(ref int lineIndex, YamlLine line, int position, PresentationNodeProperties properties)
    {
        var commentPos = this.FindInlineCommentStart(line, position);
        var end = commentPos >= 0 ? commentPos : line.End;
        var rawEnd = end;

        while (rawEnd > position && this.source.Text[rawEnd - 1] == ' ')
            rawEnd--;

        var span = this.source.Lines.CreateSpan(position, rawEnd - position);
        this.CaptureInlineComment(line, commentPos >= 0 ? commentPos : line.End);
        lineIndex++;

        return new(ids.NextSyntax(), properties, span, ScalarStyle.Plain, this.source.CreateFragment(span), this.source.Slice(span), null);
    }

    private PresentationScalarNode ParseBlockScalar(ref int lineIndex, YamlLine line, int position, PresentationNodeProperties properties)
    {
        var headerStart = position;
        var indicator = this.source.Text[position++];
        int? indentationIndicator = null;
        char? chompingIndicator = null;

        while (position < line.End)
        {
            var ch = this.source.Text[position];

            if (ch is '+' or '-')
            {
                chompingIndicator = ch;
                position++;

                continue;
            }

            if (ch is >= '1' and <= '9')
            {
                indentationIndicator = ch - '0';
                position++;

                continue;
            }

            break;
        }

        this.SkipInlineSpaces(line, ref position);

        if (position < line.End && this.source.Text[position] != '#')
        {
            throw diagnostics.CreateException(
                "YAML130",
                YamlException.Phase.Parse,
                "Unexpected characters after block scalar header.",
                this.source.Lines.CreateSpan(position, line.End - position));
        }

        this.CaptureInlineComment(line, position);
        lineIndex++;

        var header = new BlockScalarHeader(
            indicator,
            indentationIndicator,
            chompingIndicator,
            this.source.CreateFragment(headerStart, line.End - headerStart));

        var contentIndent = indentationIndicator.HasValue ? line.Indent + indentationIndicator.Value : (int?)null;
        var textLines = new List<string>();
        var contentEnd = line.End;

        while (!this.IsTerminalLine(lineIndex))
        {
            if (this.IsDocumentMarker(lineIndex, "---") || this.IsDocumentMarker(lineIndex, "..."))
                break;

            var contentLine = this.lines[lineIndex];

            if (contentLine.IsBlank)
            {
                textLines.Add(string.Empty);
                contentEnd = contentLine.End + contentLine.BreakLength;
                lineIndex++;

                continue;
            }

            if (!contentIndent.HasValue)
            {
                if (contentLine.Indent <= line.Indent)
                    break;

                contentIndent = contentLine.Indent;
            }

            if (contentLine.Indent < contentIndent.Value)
                break;

            var lineTextStart = Math.Min(contentLine.Start + contentIndent.Value, contentLine.End);
            var lineText = lineTextStart < contentLine.End ? this.source.Text[lineTextStart..contentLine.End] : string.Empty;
            textLines.Add(lineText);
            contentEnd = contentLine.End + contentLine.BreakLength;
            lineIndex++;
        }

        var logical = ComposeBlockScalar(indicator == '>', textLines, chompingIndicator);
        var span = this.source.Lines.CreateSpan(headerStart, Math.Max(contentEnd - headerStart, 1));
        var rawText = this.source.CreateFragment(headerStart, Math.Max(contentEnd - headerStart, 1));

        return new(ids.NextSyntax(), properties, span, indicator == '>' ? ScalarStyle.Folded : ScalarStyle.Literal, rawText, logical, header);
    }

    private (PresentationNode Node, int End) ParseFlowNode(int position, bool stopAtColon, PresentationNodeProperties inheritedProperties)
    {
        var start = position;
        var properties = this.ParseProperties(ref position, inheritedProperties);
        this.SkipFlowTrivia(ref position);

        if (position >= this.source.Text.Length)
            return (this.CreateImplicitNullNode(start, properties), position);

        return this.source.Text[position] switch
        {
            '[' => this.ParseFlowSequence(position, properties),
            '{' => this.ParseFlowMapping(position, properties),
            '\'' => this.ParseSingleQuotedScalar(position, properties),
            '"' => this.ParseDoubleQuotedScalar(position, properties),
            '*' => this.ParseAlias(position, properties),
            _ => this.ParseFlowPlainScalar(position, stopAtColon, properties),
        };
    }

    private (PresentationNode Node, int End) ParseFlowSequence(int position, PresentationNodeProperties properties)
    {
        var start = position++;
        var items = new List<PresentationSequenceItem>();
        this.SkipFlowTrivia(ref position);

        while (position < this.source.Text.Length && this.source.Text[position] != ']')
        {
            var (node, end) = this.ParseFlowNode(position, false, default);
            items.Add(new(ids.NextSyntax(), node.Span, node));
            position = end;
            this.SkipFlowTrivia(ref position);

            if (position < this.source.Text.Length && this.source.Text[position] == ',')
            {
                position++;
                this.SkipFlowTrivia(ref position);

                continue;
            }

            break;
        }

        if (position >= this.source.Text.Length || this.source.Text[position] != ']')
        {
            throw diagnostics.CreateException(
                "YAML140",
                YamlException.Phase.Parse,
                "Flow sequence is missing closing ']'.",
                this.source.Lines.CreateSpan(start, Math.Max(position - start, 1)));
        }

        position++;
        var span = this.source.Lines.CreateSpan(start, position - start);

        return (new PresentationSequenceNode(ids.NextSyntax(), properties, span, true, items), position);
    }

    private (PresentationNode Node, int End) ParseFlowMapping(int position, PresentationNodeProperties properties)
    {
        var start = position++;
        var entries = new List<PresentationMappingEntry>();
        this.SkipFlowTrivia(ref position);

        while (position < this.source.Text.Length && this.source.Text[position] != '}')
        {
            var (key, keyEnd) = this.ParseFlowNode(position, true, default);
            position = keyEnd;
            this.SkipFlowTrivia(ref position);

            if (position >= this.source.Text.Length || this.source.Text[position] != ':')
                throw diagnostics.CreateException("YAML141", YamlException.Phase.Parse, "Flow mapping entry is missing ':'.", key.Span);

            position++;
            this.SkipFlowTrivia(ref position);

            PresentationNode value;

            if (position >= this.source.Text.Length || this.source.Text[position] is ',' or '}')
                value = this.CreateImplicitNullNode(position, default);
            else
            {
                var parsed = this.ParseFlowNode(position, false, default);
                value = parsed.Node;
                position = parsed.End;
            }

            var entryStart = key.Span.Start;
            var entryEnd = value.Span.Start + value.Span.Length;
            entries.Add(new(ids.NextSyntax(), this.source.Lines.CreateSpan(entryStart, Math.Max(entryEnd - entryStart, 1)), key, value, false));
            this.SkipFlowTrivia(ref position);

            if (position < this.source.Text.Length && this.source.Text[position] == ',')
            {
                position++;
                this.SkipFlowTrivia(ref position);

                continue;
            }

            break;
        }

        if (position >= this.source.Text.Length || this.source.Text[position] != '}')
        {
            throw diagnostics.CreateException(
                "YAML142",
                YamlException.Phase.Parse,
                "Flow mapping is missing closing '}'.",
                this.source.Lines.CreateSpan(start, Math.Max(position - start, 1)));
        }

        position++;
        var span = this.source.Lines.CreateSpan(start, position - start);

        return (new PresentationMappingNode(ids.NextSyntax(), properties, span, true, entries), position);
    }

    private (PresentationNode Node, int End) ParseSingleQuotedScalar(int position, PresentationNodeProperties properties)
    {
        var start = position++;
        var builder = new StringBuilder();

        while (position < this.source.Text.Length)
        {
            var ch = this.source.Text[position++];

            if (ch == '\'')
            {
                if (position < this.source.Text.Length && this.source.Text[position] == '\'')
                {
                    builder.Append('\'');
                    position++;

                    continue;
                }

                var span = this.source.Lines.CreateSpan(start, position - start);

                return (
                    new PresentationScalarNode(
                        ids.NextSyntax(),
                        properties,
                        span,
                        ScalarStyle.SingleQuoted,
                        this.source.CreateFragment(span),
                        builder.ToString(),
                        null), position);
            }

            builder.Append(ch);
        }

        throw diagnostics.CreateException(
            "YAML143",
            YamlException.Phase.Parse,
            "Single-quoted scalar is not terminated.",
            this.source.Lines.CreateSpan(start, this.source.Text.Length - start));
    }

    private (PresentationNode Node, int End) ParseDoubleQuotedScalar(int position, PresentationNodeProperties properties)
    {
        var start = position++;
        var builder = new StringBuilder();

        while (position < this.source.Text.Length)
        {
            var ch = this.source.Text[position++];

            if (ch == '"')
            {
                var span = this.source.Lines.CreateSpan(start, position - start);

                return (
                    new PresentationScalarNode(
                        ids.NextSyntax(),
                        properties,
                        span,
                        ScalarStyle.DoubleQuoted,
                        this.source.CreateFragment(span),
                        builder.ToString(),
                        null), position);
            }

            if (ch != '\\')
            {
                builder.Append(ch);

                continue;
            }

            if (position >= this.source.Text.Length)
                break;

            var escape = this.source.Text[position++];

            if (escape is '\r' or '\n')
            {
                if (escape == '\r' && position < this.source.Text.Length && this.source.Text[position] == '\n')
                    position++;

                while (position < this.source.Text.Length && this.source.Text[position] is ' ' or '\t')
                    position++;

                continue;
            }

            builder.Append(
                escape switch
                {
                    '0' => '\0',
                    'a' => '\a',
                    'b' => '\b',
                    't' => '\t',
                    '\t' => '\t',
                    'n' => '\n',
                    'v' => '\v',
                    'f' => '\f',
                    'r' => '\r',
                    'e' => '\u001B',
                    ' ' => ' ',
                    '"' => '"',
                    '\\' => '\\',
                    '/' => '/',
                    'N' => '\u0085',
                    '_' => '\u00A0',
                    'L' => '\u2028',
                    'P' => '\u2029',
                    'x' => this.ParseHexScalar(ref position, 2),
                    'u' => this.ParseHexScalar(ref position, 4),
                    'U' => this.ParseHexScalar(ref position, 8),
                    _ => throw diagnostics.CreateException(
                        "YAML144",
                        YamlException.Phase.Parse,
                        $"Unsupported double-quoted escape '\\{escape}'.",
                        this.source.Lines.CreateSpan(position - 2, 2)),
                });
        }

        throw diagnostics.CreateException(
            "YAML145",
            YamlException.Phase.Parse,
            "Double-quoted scalar is not terminated.",
            this.source.Lines.CreateSpan(start, this.source.Text.Length - start));
    }

    private char ParseHexScalar(ref int position, int length)
    {
        if (position + length > this.source.Text.Length)
        {
            throw diagnostics.CreateException(
                "YAML146",
                YamlException.Phase.Parse,
                "Incomplete hexadecimal escape sequence.",
                this.source.Lines.CreateSpan(position, this.source.Text.Length - position));
        }

        var slice = this.source.Text.AsSpan(position, length);

        if (!int.TryParse(slice, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var codePoint))
        {
            throw diagnostics.CreateException(
                "YAML147",
                YamlException.Phase.Parse,
                "Invalid hexadecimal escape sequence.",
                this.source.Lines.CreateSpan(position, length));
        }

        position += length;

        if (codePoint > char.MaxValue)
        {
            throw diagnostics.CreateException(
                "YAML148",
                YamlException.Phase.Parse,
                "Unicode escape outside BMP is not supported by this delivery.",
                this.source.Lines.CreateSpan(position - length, length));
        }

        return (char)codePoint;
    }

    private (PresentationNode Node, int End) ParseAlias(int position, PresentationNodeProperties properties)
    {
        var start = position++;

        while (position < this.source.Text.Length && IsAnchorNameChar(this.source.Text[position]))
            position++;

        var span = this.source.Lines.CreateSpan(start, position - start);
        var alias = position - start > 1 ? this.source.Text.Substring(start + 1, position - start - 1) : string.Empty;

        return (new PresentationAliasNode(ids.NextSyntax(), properties, span, this.source.CreateFragment(span), alias), position);
    }

    private (PresentationNode Node, int End) ParseFlowPlainScalar(int position, bool stopAtColon, PresentationNodeProperties properties)
    {
        var start = position;
        var depth = 0;

        while (position < this.source.Text.Length)
        {
            var ch = this.source.Text[position];

            if (ch == '[' || ch == '{')
                depth++;
            else if (ch == ']' || ch == '}')
            {
                if (depth == 0)
                    break;

                depth--;
            }

            if (depth == 0)
            {
                if (ch == ',' || ch == ']' || ch == '}' || ch == '#')
                    break;

                if (stopAtColon && ch == ':')
                    break;
            }

            if (ch is '\r' or '\n')
                break;

            position++;
        }

        var rawEnd = position;

        while (rawEnd > start && this.source.Text[rawEnd - 1] == ' ')
            rawEnd--;

        var span = this.source.Lines.CreateSpan(start, rawEnd - start);

        return (
            new PresentationScalarNode(
                ids.NextSyntax(),
                properties,
                span,
                ScalarStyle.Plain,
                this.source.CreateFragment(span),
                this.source.Slice(span),
                null), position);
    }

    private void FinalizeInlineNode(ref int lineIndex, int endPosition)
    {
        var endLineIndex = this.source.Lines.GetLineIndex(Math.Max(Math.Min(endPosition, this.source.Text.Length), 0));
        var endLine = this.lines[Math.Min(endLineIndex, this.lines.Count - 1)];
        this.CaptureInlineComment(endLine, endPosition);
        lineIndex = Math.Min(endLineIndex + 1, this.lines.Count);
    }

    private PresentationNode ParseSegmentNode(int start, int end, PresentationNodeProperties inheritedProperties)
    {
        while (start < end && this.source.Text[start] == ' ')
            start++;

        while (end > start && this.source.Text[end - 1] == ' ')
            end--;

        if (start >= end)
            return this.CreateImplicitNullNode(start, inheritedProperties);

        var position = start;
        var properties = this.ParseProperties(start, end, ref position, inheritedProperties);

        while (position < end && this.source.Text[position] == ' ')
            position++;

        if (position >= end)
            return this.CreateImplicitNullNode(start, properties);

        var ch = this.source.Text[position];

        if (ch == '\'')
        {
            var (node, _) = this.ParseSingleQuotedScalar(position, properties);

            return node;
        }

        if (ch == '"')
        {
            var (node, _) = this.ParseDoubleQuotedScalar(position, properties);

            return node;
        }

        if (ch == '[' || ch == '{')
        {
            var (node, _) = this.ParseFlowNode(position, false, properties);

            return node;
        }

        if (ch == '*')
        {
            var (node, _) = this.ParseAlias(position, properties);

            return node;
        }

        var span = this.source.Lines.CreateSpan(position, end - position);

        return new PresentationScalarNode(
            ids.NextSyntax(),
            properties,
            span,
            ScalarStyle.Plain,
            this.source.CreateFragment(span),
            this.source.Slice(span),
            null);
    }

    private PresentationScalarNode CreateImplicitNullNode(int position, PresentationNodeProperties properties) =>
        new(
            ids.NextSyntax(),
            properties,
            this.source.Lines.CreateSpan(Math.Min(position, this.source.Text.Length), 0),
            ScalarStyle.Plain,
            YamlTextFragment.FromOwned(string.Empty, this.source.Lines.CreateSpan(Math.Min(position, this.source.Text.Length), 0)),
            string.Empty,
            null,
            true);

    private PresentationNodeProperties ParseProperties(YamlLine line, ref int position, PresentationNodeProperties inheritedProperties) =>
        this.ParseProperties(line.Start + line.Indent, line.End, ref position, inheritedProperties);

    private PresentationNodeProperties ParseProperties(ref int position, PresentationNodeProperties inheritedProperties) =>
        this.ParseProperties(position, this.source.Text.Length, ref position, inheritedProperties);

    private PresentationNodeProperties ParseProperties(int start, int end, ref int position, PresentationNodeProperties inheritedProperties)
    {
        var tag = inheritedProperties.Tag;
        var anchor = inheritedProperties.Anchor;

        while (position < end)
        {
            while (position < end && this.source.Text[position] == ' ')
                position++;

            if (position >= end)
                break;

            var ch = this.source.Text[position];

            if (ch == '!')
            {
                if (tag is not null)
                {
                    throw diagnostics.CreateException(
                        "YAML150",
                        YamlException.Phase.Parse,
                        "Node contains more than one tag property.",
                        this.source.Lines.CreateSpan(position, 1));
                }

                tag = this.ParseTagProperty(ref position, end);

                continue;
            }

            if (ch == '&')
            {
                if (anchor is not null)
                {
                    throw diagnostics.CreateException(
                        "YAML151",
                        YamlException.Phase.Parse,
                        "Node contains more than one anchor property.",
                        this.source.Lines.CreateSpan(position, 1));
                }

                anchor = this.ParseAnchorProperty(ref position, end);

                continue;
            }

            break;
        }

        return new(tag, anchor);
    }

    private PresentationTagProperty ParseTagProperty(ref int position, int end)
    {
        var start = position++;
        TagPropertyKind kind;

        if (position < end && this.source.Text[position] == '<')
        {
            kind = TagPropertyKind.Verbatim;
            position++;

            while (position < end && this.source.Text[position] != '>')
                position++;

            if (position >= end)
            {
                throw diagnostics.CreateException(
                    "YAML152",
                    YamlException.Phase.Parse,
                    "Verbatim tag is not terminated by '>'.",
                    this.source.Lines.CreateSpan(start, end - start));
            }

            position++;
        }
        else
        {
            kind = TagPropertyKind.Shorthand;

            while (position < end && !char.IsWhiteSpace(this.source.Text[position]) && "{}[],:#".IndexOf(this.source.Text[position]) < 0)
                position++;
        }

        var span = this.source.Lines.CreateSpan(start, position - start);
        var value = position - start > 1 ? this.source.Text.Substring(start + 1, position - start - 1) : string.Empty;

        return new(this.source.CreateFragment(span), value, position - start == 1 ? TagPropertyKind.NonSpecific : kind);
    }

    private PresentationAnchorProperty ParseAnchorProperty(ref int position, int end)
    {
        var start = position++;

        while (position < end && IsAnchorNameChar(this.source.Text[position]))
            position++;

        var span = this.source.Lines.CreateSpan(start, position - start);
        var value = position - start > 1 ? this.source.Text.Substring(start + 1, position - start - 1) : string.Empty;

        return new(this.source.CreateFragment(span), value);
    }

    private static bool IsAnchorNameChar(char ch) => !char.IsWhiteSpace(ch) && "{}[],:#*'\"".IndexOf(ch) < 0;

    private void SkipInlineSpaces(YamlLine line, ref int position)
    {
        while (position < line.End && this.source.Text[position] == ' ')
            position++;
    }

    private void SkipFlowTrivia(ref int position)
    {
        while (position < this.source.Text.Length)
        {
            var ch = this.source.Text[position];

            if (ch == ' ' || ch == '\t')
            {
                position++;

                continue;
            }

            if (ch == '#')
            {
                var lineIndex = this.source.Lines.GetLineIndex(position);
                this.CaptureInlineComment(this.lines[Math.Min(lineIndex, this.lines.Count - 1)], position);

                position = this.lines[Math.Min(lineIndex, this.lines.Count - 1)].End
                           + this.lines[Math.Min(lineIndex, this.lines.Count - 1)].BreakLength;

                continue;
            }

            if (ch is '\r' or '\n')
            {
                position++;

                if (ch == '\r' && position < this.source.Text.Length && this.source.Text[position] == '\n')
                    position++;

                continue;
            }

            break;
        }
    }

    private int FindInlineCommentStart(YamlLine line, int start)
    {
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        for (var i = Math.Max(start, line.Start); i < line.End; i++)
        {
            var ch = this.source.Text[i];

            if (ch == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (ch == '"' && !inSingle)
                inDouble = !inDouble;
            else if (!inSingle && !inDouble)
            {
                if (ch is '[' or '{')
                    depth++;
                else if (ch is ']' or '}' && depth > 0)
                    depth--;
                else if (ch == '#' && (i == start || this.source.Text[i - 1] == ' '))
                    return i;
            }
        }

        return -1;
    }

    private bool TryFindMappingColon(YamlLine line, int start, out int colonIndex)
    {
        var depth = 0;
        var inSingle = false;
        var inDouble = false;

        for (var i = start; i < line.End; i++)
        {
            var ch = this.source.Text[i];

            if (ch == '\'' && !inDouble)
                inSingle = !inSingle;
            else if (ch == '"' && !inSingle)
                inDouble = !inDouble;
            else if (!inSingle && !inDouble)
            {
                if (ch is '[' or '{')
                    depth++;
                else if (ch is ']' or '}' && depth > 0)
                    depth--;
                else if (ch == '#' && (i == start || this.source.Text[i - 1] == ' '))
                    break;
                else if (depth == 0 && ch == ':' && (i + 1 >= line.End || this.source.Text[i + 1] is ' ' or '#' or '\r' or '\n'))
                {
                    colonIndex = i;

                    return true;
                }
            }
        }

        colonIndex = -1;

        return false;
    }

    private bool IsExplicitKeyLead(YamlLine line, int position) =>
        position < line.End
        && this.source.Text[position] == '?'
        && (position + 1 >= line.End || this.source.Text[position + 1] is ' ' or '#' or '\r' or '\n');

    private bool IsBlockSequenceLead(YamlLine line, int position) =>
        position < line.End
        && this.source.Text[position] == '-'
        && (position + 1 >= line.End || this.source.Text[position + 1] is ' ' or '#' or '\r' or '\n');

    private void CaptureInlineComment(YamlLine line, int start)
    {
        if (start < 0 || start >= line.End || this.source.Text[start] != '#')
            return;

        var trivia = new PresentationTrivia(ids.NextSyntax(), PresentationTriviaKind.Comment, this.source.CreateFragment(start, line.End - start));

        this.streamTrivia.Add(trivia);
        this.documentTrivia.Add(trivia);
    }

    private static string ComposeBlockScalar(bool folded, IReadOnlyList<string> lines, char? chomping)
    {
        if (lines.Count == 0)
            return string.Empty;

        var builder = new StringBuilder();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            if (!folded)
            {
                builder.Append(line);

                if (i < lines.Count - 1)
                    builder.Append('\n');

                continue;
            }

            if (i == 0)
            {
                builder.Append(line);

                continue;
            }

            var previousEmpty = lines[i - 1].Length == 0;
            var currentEmpty = line.Length == 0;

            if (previousEmpty || currentEmpty)
                builder.Append('\n');
            else
                builder.Append(' ');

            builder.Append(line);
        }

        return chomping switch
        {
            '-' => builder.ToString().TrimEnd('\n'),
            '+' => builder.Append('\n').ToString(),
            _ => builder.Append('\n').ToString(),
        };
    }
}
