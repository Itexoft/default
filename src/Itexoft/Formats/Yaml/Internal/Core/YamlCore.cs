// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.Formats.Yaml.Internal.Core;

internal readonly record struct SyntaxId(int Value)
{
    public override string ToString() => $"syntax:{this.Value}";
}

internal readonly record struct OccurrenceId(int Value)
{
    public override string ToString() => $"occurrence:{this.Value}";
}

internal readonly record struct SemanticNodeId(int Value)
{
    public override string ToString() => $"semantic:{this.Value}";
}

internal readonly record struct BindingId(int Value)
{
    public override string ToString() => $"binding:{this.Value}";
}

internal sealed class YamlIdGenerator
{
    private int bindingId;
    private int occurrenceId;
    private int semanticId;
    private int syntaxId;

    public SyntaxId NextSyntax() => new(checked(++this.syntaxId));

    public OccurrenceId NextOccurrence() => new(checked(++this.occurrenceId));

    public SemanticNodeId NextSemantic() => new(checked(++this.semanticId));

    public BindingId NextBinding() => new(checked(++this.bindingId));
}

internal sealed class YamlLineMap
{
    private readonly List<int> starts = [0];

    public YamlLineMap(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                this.starts.Add(i + 1);

                continue;
            }

            if (text[i] == '\n')
                this.starts.Add(i + 1);
        }
    }

    public YamlException.SourceSpan CreateSpan(int start, int length)
    {
        var lineIndex = this.GetLineIndex(start);
        var lineStart = this.starts[lineIndex];

        return new(start, length, lineIndex + 1, start - lineStart + 1);
    }

    public int GetLineIndex(int offset)
    {
        var low = 0;
        var high = this.starts.Count - 1;

        while (low <= high)
        {
            var mid = low + ((high - low) >> 1);
            var value = this.starts[mid];

            if (value == offset)
                return mid;

            if (value < offset)
                low = mid + 1;
            else
                high = mid - 1;
        }

        return Math.Max(high, 0);
    }
}

internal sealed class YamlDiagnostics
{
    private readonly List<YamlException.Diagnostic> items = [];

    public IReadOnlyList<YamlException.Diagnostic> Items => this.items;

    public void Add(string code, YamlException.Phase phase, string message, YamlException.SourceSpan? sourceSpan = null, string? stablePath = null) =>
        this.items.Add(new(code, phase, message, sourceSpan, stablePath));

    public YamlException CreateException(
        string code,
        YamlException.Phase phase,
        string message,
        YamlException.SourceSpan? sourceSpan = null,
        string? stablePath = null,
        Exception? innerException = null)
    {
        if (this.items.Count == 0 || this.items[^1].Code != code)
            this.Add(code, phase, message, sourceSpan, stablePath);

        return new(this.items[^1], this.items.ToArray(), innerException);
    }

    public void ThrowIfAny()
    {
        if (this.items.Count != 0)
            throw new YamlException(this.items[0], this.items.ToArray());
    }
}

internal sealed class YamlSourceText(string text)
{
    public string Text { get; } = text;

    public YamlLineMap Lines { get; } = new(text);

    public ReadOnlySpan<char> AsSpan(int start, int length) => this.Text.AsSpan(start, length);

    public string Slice(YamlException.SourceSpan span) => this.Text.Substring(span.Start, span.Length);

    public YamlTextFragment CreateFragment(YamlException.SourceSpan span) => YamlTextFragment.FromSource(this, span);

    public YamlTextFragment CreateFragment(int start, int length) => this.CreateFragment(this.Lines.CreateSpan(start, length));
}

internal readonly record struct YamlTextFragment
{
    private readonly YamlSourceText? source;
    private readonly string? text;

    private YamlTextFragment(YamlSourceText? source, YamlException.SourceSpan span, string? text)
    {
        this.source = source;
        this.Span = span;
        this.text = text;
    }

    public YamlException.SourceSpan Span { get; }

    public bool IsSourceBacked => this.source is not null;

    public bool IsOwned => this.text is not null;

    public string Text => this.text ?? this.source!.Slice(this.Span);

    public static YamlTextFragment FromSource(YamlSourceText source, YamlException.SourceSpan span) => new(source, span, null);

    public static YamlTextFragment FromOwned(string text, YamlException.SourceSpan span) => new(null, span, text);
}

internal readonly record struct YamlLine(
    int Index,
    int Start,
    int End,
    int BreakLength,
    int Indent,
    bool IsBlank,
    bool IsCommentOnly,
    bool HasTabIndent);

internal sealed class YamlScanner
{
    public static IReadOnlyList<YamlLine> Scan(YamlSourceText source, YamlDiagnostics diagnostics)
    {
        var lines = new List<YamlLine>();
        var text = source.Text;
        var lineStart = 0;
        var lineIndex = 0;

        while (lineStart <= text.Length)
        {
            if (lineStart == text.Length)
            {
                lines.Add(new(lineIndex, lineStart, lineStart, 0, 0, true, false, false));

                break;
            }

            var cursor = lineStart;

            while (cursor < text.Length && text[cursor] is not '\r' and not '\n')
                cursor++;

            var lineEnd = cursor;
            var breakLength = 0;

            if (cursor < text.Length)
            {
                breakLength = 1;

                if (text[cursor] == '\r' && cursor + 1 < text.Length && text[cursor + 1] == '\n')
                    breakLength = 2;
            }

            var indent = 0;
            var hasTabIndent = false;

            while (lineStart + indent < lineEnd)
            {
                var ch = text[lineStart + indent];

                if (ch == ' ')
                {
                    indent++;

                    continue;
                }

                if (ch == '\t')
                {
                    hasTabIndent = true;

                    diagnostics.Add(
                        "YAML010",
                        YamlException.Phase.Scan,
                        "Tabs are not allowed in YAML indentation.",
                        source.Lines.CreateSpan(lineStart + indent, 1));
                }

                break;
            }

            var contentIndex = lineStart + indent;
            var isBlank = contentIndex >= lineEnd;
            var isCommentOnly = !isBlank && text[contentIndex] == '#';

            lines.Add(new(lineIndex, lineStart, lineEnd, breakLength, indent, isBlank, isCommentOnly, hasTabIndent));

            lineIndex++;
            lineStart = lineEnd + breakLength;
        }

        return lines;
    }
}

internal static class YamlText
{
    public static string NormalizeLineBreaks(string text)
    {
        if (!text.Contains('\r'))
            return text;

        var builder = new StringBuilder(text.Length);

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n')
                    i++;

                builder.Append('\n');

                continue;
            }

            builder.Append(ch);
        }

        return builder.ToString();
    }
}
