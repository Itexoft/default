// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;
using Itexoft.Formats.Yaml.Internal.Serialization;
using Itexoft.Formats.Yaml.Internal.Session;

namespace Itexoft.Formats.Yaml.Internal.Emission;

internal static class YamlPresentationEmitter
{
    public static PresentationDocument Project(YamlSession session, SerializationDocument document)
    {
        if (session.Presentation is { Documents.Count: 1 }
            && session.Documents.Count == 1
            && ReferenceEquals(session.Documents[0].SerializationDocument, document))
            return session.Presentation.Documents[0];

        var builder = new StringBuilder();
        builder.AppendLine("---");
        builder.Append(RenderOccurrence(document.RootId, document, 0));

        if (builder.Length == 0 || builder[^1] != '\n')
            builder.Append('\n');

        var text = builder.ToString();
        var source = new YamlSourceText(text);

        return new(session.Ids.NextSyntax(), source.CreateFragment(0, text.Length), null, [], [], true, false, new(0, text.Length, 1, 1));
    }

    private static string RenderOccurrence(OccurrenceId occurrenceId, SerializationDocument document, int indent)
    {
        var occurrence = document.Occurrences[occurrenceId];
        var decorator = RenderProperties(occurrence.Properties);

        return occurrence switch
        {
            SerializationAliasOccurrence alias => DecorateInline(decorator, $"*{alias.AliasName}"),
            SerializationScalarOccurrence scalar => RenderScalarOccurrence(scalar, decorator, indent),
            SerializationSequenceOccurrence sequence => RenderSequenceOccurrence(sequence, decorator, document, indent),
            SerializationMappingOccurrence mapping => RenderMappingOccurrence(mapping, decorator, document, indent),
            _ => throw new InvalidOperationException($"Unsupported serialization occurrence '{occurrence.GetType().Name}'."),
        };
    }

    private static string RenderSequenceOccurrence(
        SerializationSequenceOccurrence sequence,
        string decorator,
        SerializationDocument document,
        int indent)
    {
        if (sequence.Items.Count == 0)
            return DecorateInline(decorator, "[]");

        var builder = new StringBuilder();

        if (decorator.Length != 0)
            builder.AppendLine(Indent(indent) + decorator);

        foreach (var itemId in sequence.Items)
        {
            var itemOccurrence = document.Occurrences[itemId];
            var item = RenderOccurrence(itemId, document, indent + 2);

            if (CanInlineSequenceItem(itemOccurrence, item))
            {
                AppendInlineValue(builder, indent, "- ", item, indent + 2);

                continue;
            }

            if (CanCompactSequenceItem(itemOccurrence))
            {
                AppendInlineValue(builder, indent, "- ", item, indent + 2);

                continue;
            }

            builder.Append(Indent(indent));
            builder.AppendLine("-");
            builder.AppendLine(item);
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string RenderMappingOccurrence(
        SerializationMappingOccurrence mapping,
        string decorator,
        SerializationDocument document,
        int indent)
    {
        if (mapping.Entries.Count == 0)
            return DecorateInline(decorator, "{}");

        var builder = new StringBuilder();

        if (decorator.Length != 0)
            builder.AppendLine(Indent(indent) + decorator);

        foreach (var entry in mapping.Entries)
        {
            var key = RenderOccurrence(entry.KeyId, document, indent + 2);
            var value = RenderOccurrence(entry.ValueId, document, indent + 2);
            var keyOccurrence = document.Occurrences[entry.KeyId];
            var valueOccurrence = document.Occurrences[entry.ValueId];
            var simpleKey = IsSingleLine(key) && keyOccurrence.Kind is SerializationNodeKind.Scalar or SerializationNodeKind.Alias;
            var simpleValue = CanInlineMappingValue(valueOccurrence, value);

            if (simpleKey)
            {
                if (simpleValue)
                {
                    AppendInlineValue(builder, indent, $"{key}: ", value, indent + 2);

                    continue;
                }

                if (CanCompactMappingValue(valueOccurrence))
                {
                    AppendInlineValue(builder, indent, $"{key}: ", value, indent + 2);

                    continue;
                }

                builder.Append(Indent(indent));
                builder.Append(key);
                builder.AppendLine(":");
                builder.AppendLine(value);

                continue;
            }

            builder.Append(Indent(indent));
            builder.AppendLine("?");
            builder.AppendLine(key);
            builder.Append(Indent(indent));
            builder.AppendLine(":");
            builder.AppendLine(value);
        }

        return builder.ToString().TrimEnd('\n');
    }

    private static string RenderScalarOccurrence(SerializationScalarOccurrence scalar, string decorator, int indent)
    {
        var rendered = RenderScalarValue(scalar.Scalar, indent);

        if (IsSingleLine(rendered))
            return DecorateInline(decorator, rendered);

        if (decorator.Length == 0)
            return rendered;

        return $"{Indent(indent)}{decorator}\n{IndentFirstLine(rendered, indent)}";
    }

    private static string RenderScalarValue(PresentationScalarNode scalar, int indent)
    {
        if (scalar.IsImplicitNull)
            return "null";

        return scalar.Style switch
        {
            ScalarStyle.Literal => RenderBlockScalar("|", scalar.LogicalText, indent),
            ScalarStyle.Folded => RenderBlockScalar(">", scalar.LogicalText, indent),
            ScalarStyle.SingleQuoted => $"'{scalar.LogicalText.Replace("'", "''", StringComparison.Ordinal)}'",
            ScalarStyle.DoubleQuoted => RenderDoubleQuoted(scalar.LogicalText),
            ScalarStyle.Plain when YamlPlainScalarSyntax.RequiresQuotedStyle(scalar.LogicalText) => RenderQuotedString(scalar.LogicalText),
            _ => scalar.LogicalText.Length == 0 ? "''" : scalar.LogicalText,
        };
    }

    private static string RenderQuotedString(string text) =>
        text.Contains('\'', StringComparison.Ordinal) ? RenderDoubleQuoted(text) : $"'{text}'";

    private static string RenderDoubleQuoted(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        builder.Append('"');

        foreach (var ch in text)
        {
            builder.Append(
                ch switch
                {
                    '\\' => "\\\\",
                    '"' => "\\\"",
                    '\n' => "\\n",
                    '\r' => "\\r",
                    '\t' => "\\t",
                    _ => ch.ToString(),
                });
        }

        builder.Append('"');

        return builder.ToString();
    }

    private static string RenderBlockScalar(string header, string text, int indent)
    {
        var builder = new StringBuilder();
        builder.Append(header);
        AppendIndentedLines(builder, text, indent + 2, true);

        return builder.ToString();
    }

    private static string RenderProperties(PresentationNodeProperties properties)
    {
        if (properties.Tag is { } tag)
            return properties.Anchor is { } anchor ? $"{tag.RawText} {anchor.RawText}" : tag.RawText;

        return properties.Anchor is { } onlyAnchor ? onlyAnchor.RawText : string.Empty;
    }

    private static string DecorateInline(string decorator, string value) => decorator.Length == 0 ? value : $"{decorator} {value}";

    private static string Indent(int count) => count <= 0 ? string.Empty : new(' ', count);

    private static bool IsSingleLine(string text) => !text.Contains('\n');

    private static bool CanInlineSequenceItem(SerializationOccurrence occurrence, string rendered) =>
        IsSingleLine(rendered)
        && (occurrence.Kind is SerializationNodeKind.Scalar or SerializationNodeKind.Alias || IsInlineEmptyCollection(occurrence));

    private static bool CanInlineMappingValue(SerializationOccurrence occurrence, string rendered) =>
        IsSingleLine(rendered)
        && (occurrence.Kind is SerializationNodeKind.Scalar or SerializationNodeKind.Alias || IsInlineEmptyCollection(occurrence));

    private static bool CanCompactSequenceItem(SerializationOccurrence occurrence) =>
        !HasProperties(occurrence.Properties) && occurrence.Kind is SerializationNodeKind.Mapping or SerializationNodeKind.Scalar;

    private static bool CanCompactMappingValue(SerializationOccurrence occurrence) =>
        !HasProperties(occurrence.Properties) && occurrence.Kind == SerializationNodeKind.Scalar;

    private static bool HasProperties(PresentationNodeProperties properties) => properties.Tag is not null || properties.Anchor is not null;

    private static bool IsInlineEmptyCollection(SerializationOccurrence occurrence) =>
        occurrence is SerializationSequenceOccurrence { Items.Count: 0 } or SerializationMappingOccurrence { Entries.Count: 0 };

    private static void AppendInlineValue(StringBuilder builder, int indent, string prefix, string value, int childIndent)
    {
        builder.Append(Indent(indent));
        builder.Append(prefix);
        var lineBreak = value.IndexOf('\n');

        if (lineBreak < 0)
        {
            builder.AppendLine(StripExpectedIndent(value, childIndent));

            return;
        }

        builder.AppendLine(StripExpectedIndent(value[..lineBreak], childIndent));
        builder.AppendLine(value[(lineBreak + 1)..]);
    }

    private static string StripExpectedIndent(string text, int indent)
    {
        if (indent <= 0 || text.Length < indent)
            return text;

        for (var i = 0; i < indent; i++)
        {
            if (text[i] != ' ')
                return text;
        }

        return text[indent..];
    }

    private static string IndentFirstLine(string text, int indent)
    {
        if (indent <= 0 || text.Length == 0)
            return text;

        return Indent(indent) + text;
    }

    private static void AppendIndentedLines(StringBuilder builder, string text, int indent, bool prefixFirstLineWithNewLine)
    {
        var prefix = Indent(indent);
        var start = 0;
        var first = true;

        while (true)
        {
            if (!first || prefixFirstLineWithNewLine)
                builder.Append('\n');

            builder.Append(prefix);
            var end = text.IndexOf('\n', start);
            first = false;

            if (end < 0)
            {
                builder.Append(text, start, text.Length - start);

                return;
            }

            builder.Append(text, start, end - start);
            start = end + 1;
        }
    }
}
