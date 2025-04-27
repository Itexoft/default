// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;
using Itexoft.Formats.Yaml.Internal.Representation;
using Itexoft.Formats.Yaml.Internal.Session;

namespace Itexoft.Formats.Yaml.Internal.Serialization;

internal static class YamlSerializationProjector
{
    public static SerializationDocument Project(YamlSession session, RepresentationGraph graph)
    {
        var occurrences = new Dictionary<OccurrenceId, SerializationOccurrence>();
        var firstOccurrences = new Dictionary<SemanticNodeId, OccurrenceId>();
        var anchorNames = new Dictionary<SemanticNodeId, string>();
        var anchorIndex = 0;
        var rootId = ProjectNode(graph.RootId);

        return new(session.Ids.NextSyntax(), rootId, occurrences);

        OccurrenceId ProjectNode(SemanticNodeId nodeId)
        {
            if (firstOccurrences.TryGetValue(nodeId, out var existing))
            {
                var aliasId = session.Ids.NextOccurrence();
                var aliasName = anchorNames[nodeId];
                occurrences[aliasId] = new SerializationAliasOccurrence(aliasId, session.Ids.NextSyntax(), default, aliasName, existing);

                return aliasId;
            }

            var node = graph.Nodes[nodeId];
            var props = CreateProperties(nodeId, node);
            var occurrenceId = session.Ids.NextOccurrence();
            firstOccurrences[nodeId] = occurrenceId;

            switch (node)
            {
                case RepresentationScalarNode scalar:
                {
                    var style = SelectScalarStyle(scalar);

                    var presentationScalar = new PresentationScalarNode(
                        session.Ids.NextSyntax(),
                        props,
                        new(0, 0, 1, 1),
                        style,
                        YamlTextFragment.FromOwned(scalar.LogicalText, new(0, scalar.LogicalText.Length, 1, 1)),
                        scalar.LogicalText,
                        null,
                        scalar.IsImplicitNull);

                    occurrences[occurrenceId] = new SerializationScalarOccurrence(occurrenceId, presentationScalar.Id, props, presentationScalar);

                    return occurrenceId;
                }
                case RepresentationSequenceNode sequence:
                {
                    var items = new OccurrenceId[sequence.Items.Count];

                    for (var i = 0; i < sequence.Items.Count; i++)
                        items[i] = ProjectNode(sequence.Items[i]);

                    occurrences[occurrenceId] = new SerializationSequenceOccurrence(occurrenceId, session.Ids.NextSyntax(), props, false, items);

                    return occurrenceId;
                }
                case RepresentationMappingNode mapping:
                {
                    var entries = new SerializationMappingEntryOccurrence[mapping.Entries.Count];

                    for (var i = 0; i < mapping.Entries.Count; i++)
                    {
                        var entry = mapping.Entries[i];
                        entries[i] = new(session.Ids.NextSyntax(), ProjectNode(entry.KeyId), ProjectNode(entry.ValueId));
                    }

                    occurrences[occurrenceId] = new SerializationMappingOccurrence(occurrenceId, session.Ids.NextSyntax(), props, false, entries);

                    return occurrenceId;
                }
                default:
                    throw session.Diagnostics.CreateException(
                        "YAML600",
                        YamlException.Phase.SerializeCompose,
                        $"Unsupported representation node '{node.GetType().Name}'.");
            }
        }

        PresentationNodeProperties CreateProperties(SemanticNodeId nodeId, RepresentationNode node)
        {
            PresentationAnchorProperty? anchor = null;

            if (graph.OccurrenceCounts.TryGetValue(nodeId, out var count) && count > 1)
            {
                var anchorName = $"a{++anchorIndex}";
                anchorNames[nodeId] = anchorName;
                anchor = new(YamlTextFragment.FromOwned($"&{anchorName}", new(0, anchorName.Length + 1, 1, 1)), anchorName);
            }

            PresentationTagProperty? tag = null;

            if (node.TagState.Kind == RepresentationTagStateKind.Explicit)
            {
                var raw = $"!<{node.TagState.Tag}>";
                tag = new(YamlTextFragment.FromOwned(raw, new(0, raw.Length, 1, 1)), $"<{node.TagState.Tag}>", TagPropertyKind.Verbatim);
            }

            return new(tag, anchor);
        }

        static ScalarStyle SelectScalarStyle(RepresentationScalarNode scalar)
        {
            if (scalar.TagState.Tag != YamlTags.String)
                return scalar.Style;

            if (scalar.LogicalText.Contains('\n'))
                return ScalarStyle.Literal;

            if (YamlPlainScalarSyntax.RequiresQuotedStyle(scalar.LogicalText))
                return scalar.LogicalText.Contains('\'', StringComparison.Ordinal) ? ScalarStyle.DoubleQuoted : ScalarStyle.SingleQuoted;

            return scalar.Style;
        }
    }
}
