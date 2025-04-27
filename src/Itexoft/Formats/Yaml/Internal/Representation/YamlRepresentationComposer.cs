// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using System.Numerics;
using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;
using Itexoft.Formats.Yaml.Internal.Serialization;

namespace Itexoft.Formats.Yaml.Internal.Representation;

internal static class YamlRepresentationComposer
{
    public static RepresentationGraph Compose(
        SerializationDocument document,
        YamlIdGenerator ids,
        YamlDiagnostics diagnostics,
        YamlProjectionLedger ledger)
    {
        var occurrenceToSemantic = new Dictionary<OccurrenceId, SemanticNodeId>();
        var nodes = new Dictionary<SemanticNodeId, RepresentationNode>();
        var occurrenceCounts = new Dictionary<SemanticNodeId, int>();
        var state = new ComposerState(document, ids, diagnostics, ledger, occurrenceToSemantic, nodes, occurrenceCounts);
        var rootId = state.ComposeOccurrence(document.RootId);

        foreach (var node in nodes.Values.OfType<RepresentationMappingNode>())
            ValidateDuplicateKeys(node, nodes, diagnostics);

        return new(rootId, nodes, occurrenceCounts, nodes.Values.Any(static x => x.TagState.Kind == RepresentationTagStateKind.Unresolved));
    }

    private static void ValidateDuplicateKeys(
        RepresentationMappingNode mapping,
        IReadOnlyDictionary<SemanticNodeId, RepresentationNode> nodes,
        YamlDiagnostics diagnostics)
    {
        for (var i = 0; i < mapping.Entries.Count; i++)
        {
            for (var j = i + 1; j < mapping.Entries.Count; j++)
            {
                if (!YamlNodeEqualityComparer.AreEqual(mapping.Entries[i].KeyId, mapping.Entries[j].KeyId, nodes, diagnostics))
                    continue;

                diagnostics.Add(
                    "YAML310",
                    YamlException.Phase.RepresentCompose,
                    "Mapping contains duplicate keys under YAML equality rules.",
                    stablePath: mapping.Id.ToString());
            }
        }
    }

    private static string NormalizeExplicitTag(PresentationTagProperty tag)
    {
        if (tag.Kind == TagPropertyKind.Verbatim && tag.Value.Length > 2 && tag.Value[0] == '<' && tag.Value[^1] == '>')
            return tag.Value[1..^1];

        return tag.Value switch
        {
            "!!str" => YamlTags.String,
            "!str" => YamlTags.String,
            "!!null" => YamlTags.Null,
            "!null" => YamlTags.Null,
            "!!bool" => YamlTags.Boolean,
            "!bool" => YamlTags.Boolean,
            "!!int" => YamlTags.Integer,
            "!int" => YamlTags.Integer,
            "!!float" => YamlTags.Float,
            "!float" => YamlTags.Float,
            "!!seq" => YamlTags.Sequence,
            "!seq" => YamlTags.Sequence,
            "!!map" => YamlTags.Mapping,
            "!map" => YamlTags.Mapping,
            _ => tag.Value,
        };
    }

    private static string ResolveCorePlainScalarTag(string text, bool isImplicitNull)
    {
        if (isImplicitNull)
            return YamlTags.Null;

        var span = text.AsSpan();

        if (span.Length == 0
            || span.Equals("null".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("~".AsSpan(), StringComparison.Ordinal))
            return YamlTags.Null;

        if (span.Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase) || span.Equals("false".AsSpan(), StringComparison.OrdinalIgnoreCase))
            return YamlTags.Boolean;

        if (TryParseInteger(span, out _))
            return YamlTags.Integer;

        if (TryParseFloat(span, out _))
            return YamlTags.Float;

        return YamlTags.String;
    }

    internal static ScalarCanonicalForm CreateCanonicalForm(string tag, string text)
    {
        if (tag == YamlTags.Null)
            return new(ScalarCanonicalKind.Null, "null");

        if (tag == YamlTags.Boolean)
        {
            var booleanValue = text.Equals("true", StringComparison.OrdinalIgnoreCase);

            return new(ScalarCanonicalKind.Boolean, booleanValue ? "true" : "false", booleanValue);
        }

        if (tag == YamlTags.Integer && TryParseInteger(text.AsSpan(), out var integerValue))
            return new(ScalarCanonicalKind.Integer, integerValue.ToString(CultureInfo.InvariantCulture), false, integerValue);

        if (tag == YamlTags.Float && TryParseFloat(text.AsSpan(), out var floatValue))
            return new(ScalarCanonicalKind.Float, floatValue.ToString("R", CultureInfo.InvariantCulture), false, default, floatValue);

        return new(ScalarCanonicalKind.String, text);
    }

    internal static bool TryParseInteger(ReadOnlySpan<char> span, out BigInteger value)
    {
        value = default;

        if (span.Length == 0)
            return false;

        var sign = 1;
        var index = 0;

        if (span[0] == '+' || span[0] == '-')
        {
            sign = span[0] == '-' ? -1 : 1;
            index++;
        }

        var baseValue = 10;

        if (span[index..].StartsWith("0x".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            baseValue = 16;
            index += 2;
        }
        else if (span[index..].StartsWith("0o".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            baseValue = 8;
            index += 2;
        }

        if (index >= span.Length)
            return false;

        BigInteger result = 0;

        for (; index < span.Length; index++)
        {
            var ch = span[index];
            int digit;

            if (ch is >= '0' and <= '9')
                digit = ch - '0';
            else if (ch is >= 'a' and <= 'f')
                digit = ch - 'a' + 10;
            else if (ch is >= 'A' and <= 'F')
                digit = ch - 'A' + 10;
            else
                return false;

            if (digit >= baseValue)
                return false;

            result = result * baseValue + digit;
        }

        value = sign < 0 ? -result : result;

        return true;
    }

    internal static bool TryParseFloat(ReadOnlySpan<char> span, out double value)
    {
        if (span.Equals(".nan".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = double.NaN;

            return true;
        }

        if (span.Equals(".inf".AsSpan(), StringComparison.OrdinalIgnoreCase) || span.Equals("+.inf".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = double.PositiveInfinity;

            return true;
        }

        if (span.Equals("-.inf".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            value = double.NegativeInfinity;

            return true;
        }

        return double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
               && (span.Contains('.') || span.IndexOfAny("eE".AsSpan()) >= 0);
    }

    private sealed class ComposerState(
        SerializationDocument document,
        YamlIdGenerator ids,
        YamlDiagnostics diagnostics,
        YamlProjectionLedger ledger,
        IDictionary<OccurrenceId, SemanticNodeId> occurrenceToSemantic,
        IDictionary<SemanticNodeId, RepresentationNode> nodes,
        IDictionary<SemanticNodeId, int> occurrenceCounts)
    {
        public SemanticNodeId ComposeOccurrence(OccurrenceId occurrenceId)
        {
            if (occurrenceToSemantic.TryGetValue(occurrenceId, out var existing))
            {
                occurrenceCounts[existing] = occurrenceCounts.TryGetValue(existing, out var count) ? count + 1 : 1;

                return existing;
            }

            if (!document.Occurrences.TryGetValue(occurrenceId, out var occurrence))
            {
                throw diagnostics.CreateException(
                    "YAML300",
                    YamlException.Phase.RepresentCompose,
                    $"Serialization occurrence '{occurrenceId}' is missing.");
            }

            switch (occurrence)
            {
                case SerializationAliasOccurrence alias:
                {
                    var targetId = this.ComposeOccurrence(alias.TargetId);
                    occurrenceToSemantic[occurrenceId] = targetId;
                    ledger.Link(occurrenceId, targetId);

                    return targetId;
                }
                case SerializationScalarOccurrence scalar:
                {
                    var semanticId = ids.NextSemantic();
                    occurrenceToSemantic[occurrenceId] = semanticId;
                    occurrenceCounts[semanticId] = 1;
                    var tagState = ResolveScalarTag(scalar.Scalar);
                    var canonical = CreateCanonicalForm(tagState.Tag, scalar.Scalar.LogicalText);

                    nodes[semanticId] = new RepresentationScalarNode(
                        semanticId,
                        tagState,
                        scalar.Scalar.LogicalText,
                        scalar.Scalar.Style,
                        canonical,
                        scalar.Scalar.IsImplicitNull);

                    ledger.Link(occurrenceId, semanticId);

                    return semanticId;
                }
                case SerializationSequenceOccurrence sequence:
                {
                    var semanticId = ids.NextSemantic();
                    occurrenceToSemantic[occurrenceId] = semanticId;
                    occurrenceCounts[semanticId] = 1;
                    ledger.Link(occurrenceId, semanticId);
                    var items = new List<SemanticNodeId>(sequence.Items.Count);

                    foreach (var itemId in sequence.Items)
                        items.Add(this.ComposeOccurrence(itemId));

                    nodes[semanticId] = new RepresentationSequenceNode(semanticId, ResolveCollectionTag(sequence.Properties, true), items);

                    return semanticId;
                }
                case SerializationMappingOccurrence mapping:
                {
                    var semanticId = ids.NextSemantic();
                    occurrenceToSemantic[occurrenceId] = semanticId;
                    occurrenceCounts[semanticId] = 1;
                    ledger.Link(occurrenceId, semanticId);
                    var entries = new List<RepresentationMappingEntry>(mapping.Entries.Count);

                    foreach (var entry in mapping.Entries)
                    {
                        var keyId = this.ComposeOccurrence(entry.KeyId);
                        var valueId = this.ComposeOccurrence(entry.ValueId);
                        entries.Add(new(keyId, valueId));
                    }

                    nodes[semanticId] = new RepresentationMappingNode(semanticId, ResolveCollectionTag(mapping.Properties, false), entries);

                    return semanticId;
                }
                default:
                    throw diagnostics.CreateException(
                        "YAML301",
                        YamlException.Phase.RepresentCompose,
                        $"Unsupported serialization occurrence '{occurrence.GetType().Name}'.");
            }
        }

        private static RepresentationTagState ResolveCollectionTag(PresentationNodeProperties properties, bool isSequence)
        {
            if (properties.Tag is { } tag)
            {
                if (tag.Kind == TagPropertyKind.NonSpecific)
                    return new(RepresentationTagStateKind.Resolved, isSequence ? YamlTags.Sequence : YamlTags.Mapping);

                return new(RepresentationTagStateKind.Explicit, NormalizeExplicitTag(tag));
            }

            return new(RepresentationTagStateKind.Resolved, isSequence ? YamlTags.Sequence : YamlTags.Mapping);
        }

        private static RepresentationTagState ResolveScalarTag(PresentationScalarNode scalar)
        {
            if (scalar.Properties.Tag is { } tag)
            {
                if (tag.Kind == TagPropertyKind.NonSpecific)
                    return new(RepresentationTagStateKind.Resolved, YamlTags.String);

                return new(RepresentationTagStateKind.Explicit, NormalizeExplicitTag(tag));
            }

            if (scalar.Style == ScalarStyle.Plain)
                return new(RepresentationTagStateKind.Resolved, ResolveCorePlainScalarTag(scalar.LogicalText, scalar.IsImplicitNull));

            return new(RepresentationTagStateKind.Resolved, YamlTags.String);
        }
    }
}

internal static class YamlTags
{
    public const string Null = "tag:yaml.org,2002:null";
    public const string Boolean = "tag:yaml.org,2002:bool";
    public const string Integer = "tag:yaml.org,2002:int";
    public const string Float = "tag:yaml.org,2002:float";
    public const string String = "tag:yaml.org,2002:str";
    public const string Sequence = "tag:yaml.org,2002:seq";
    public const string Mapping = "tag:yaml.org,2002:map";
}

internal static class YamlNodeEqualityComparer
{
    public static bool AreEqual(
        SemanticNodeId leftId,
        SemanticNodeId rightId,
        IReadOnlyDictionary<SemanticNodeId, RepresentationNode> nodes,
        YamlDiagnostics diagnostics) =>
        AreEqual(leftId, rightId, nodes, diagnostics, []);

    private static bool AreEqual(
        SemanticNodeId leftId,
        SemanticNodeId rightId,
        IReadOnlyDictionary<SemanticNodeId, RepresentationNode> nodes,
        YamlDiagnostics diagnostics,
        HashSet<(SemanticNodeId Left, SemanticNodeId Right)> active)
    {
        if (leftId == rightId)
            return true;

        if (!active.Add((leftId, rightId)))
        {
            diagnostics.Add("YAML311", YamlException.Phase.RepresentCompose, "Cyclic key equality is not supported.");

            return false;
        }

        try
        {
            var left = nodes[leftId];
            var right = nodes[rightId];

            if (left.Kind != right.Kind)
                return false;

            if (left.TagState.Tag != right.TagState.Tag)
                return false;

            switch (left)
            {
                case RepresentationScalarNode leftScalar when right is RepresentationScalarNode rightScalar:
                    return leftScalar.CanonicalForm.Kind == rightScalar.CanonicalForm.Kind
                           && leftScalar.CanonicalForm.Text == rightScalar.CanonicalForm.Text;
                case RepresentationSequenceNode leftSequence when right is RepresentationSequenceNode rightSequence:
                {
                    if (leftSequence.Items.Count != rightSequence.Items.Count)
                        return false;

                    for (var i = 0; i < leftSequence.Items.Count; i++)
                    {
                        if (!AreEqual(leftSequence.Items[i], rightSequence.Items[i], nodes, diagnostics, active))
                            return false;
                    }

                    return true;
                }
                case RepresentationMappingNode leftMapping when right is RepresentationMappingNode rightMapping:
                {
                    if (leftMapping.Entries.Count != rightMapping.Entries.Count)
                        return false;

                    var matched = new bool[rightMapping.Entries.Count];

                    foreach (var leftEntry in leftMapping.Entries)
                    {
                        var found = false;

                        for (var i = 0; i < rightMapping.Entries.Count; i++)
                        {
                            if (matched[i])
                                continue;

                            var rightEntry = rightMapping.Entries[i];

                            if (!AreEqual(leftEntry.KeyId, rightEntry.KeyId, nodes, diagnostics, active))
                                continue;

                            if (!AreEqual(leftEntry.ValueId, rightEntry.ValueId, nodes, diagnostics, active))
                                continue;

                            matched[i] = true;
                            found = true;

                            break;
                        }

                        if (!found)
                            return false;
                    }

                    return true;
                }
                default:
                    return false;
            }
        }
        finally
        {
            active.Remove((leftId, rightId));
        }
    }
}
