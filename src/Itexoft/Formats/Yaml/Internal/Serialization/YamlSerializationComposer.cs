// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;

namespace Itexoft.Formats.Yaml.Internal.Serialization;

internal static class YamlSerializationComposer
{
    public static SerializationDocument Compose(
        PresentationDocument document,
        YamlIdGenerator ids,
        YamlDiagnostics diagnostics,
        YamlProjectionLedger ledger)
    {
        var occurrences = new Dictionary<OccurrenceId, SerializationOccurrence>();
        var anchors = new Dictionary<string, OccurrenceId>(StringComparer.Ordinal);

        if (document.Root is null)
        {
            throw diagnostics.CreateException(
                "YAML020",
                YamlException.Phase.SerializeCompose,
                "YAML document has no presentation root.",
                document.Span);
        }

        var rootId = ComposeNode(document.Root, occurrences, anchors, ids, diagnostics, ledger);

        return new(document.Id, rootId, occurrences);
    }

    private static OccurrenceId ComposeNode(
        PresentationNode node,
        IDictionary<OccurrenceId, SerializationOccurrence> occurrences,
        IDictionary<string, OccurrenceId> anchors,
        YamlIdGenerator ids,
        YamlDiagnostics diagnostics,
        YamlProjectionLedger ledger)
    {
        switch (node)
        {
            case PresentationScalarNode scalar:
            {
                var occurrenceId = ids.NextOccurrence();
                var occurrence = new SerializationScalarOccurrence(occurrenceId, scalar.Id, scalar.Properties, scalar);
                occurrences.Add(occurrenceId, occurrence);
                RegisterAnchor(scalar.Properties, occurrenceId, anchors);
                ledger.Link(scalar.Id, occurrenceId);

                return occurrenceId;
            }
            case PresentationAliasNode alias:
            {
                if (!anchors.TryGetValue(alias.Alias, out var targetId))
                {
                    throw diagnostics.CreateException(
                        "YAML021",
                        YamlException.Phase.SerializeCompose,
                        $"Alias '*{alias.Alias}' does not resolve to a preceding anchor occurrence.",
                        alias.Span);
                }

                var occurrenceId = ids.NextOccurrence();
                var occurrence = new SerializationAliasOccurrence(occurrenceId, alias.Id, alias.Properties, alias.Alias, targetId);
                occurrences.Add(occurrenceId, occurrence);
                ledger.Link(alias.Id, occurrenceId);

                return occurrenceId;
            }
            case PresentationSequenceNode sequence:
            {
                var occurrenceId = ids.NextOccurrence();
                RegisterAnchor(sequence.Properties, occurrenceId, anchors);
                var itemIds = new List<OccurrenceId>(sequence.Items.Count);

                foreach (var item in sequence.Items)
                    itemIds.Add(ComposeNode(item.Node, occurrences, anchors, ids, diagnostics, ledger));

                var occurrence = new SerializationSequenceOccurrence(occurrenceId, sequence.Id, sequence.Properties, sequence.IsFlow, itemIds);
                occurrences.Add(occurrenceId, occurrence);
                ledger.Link(sequence.Id, occurrenceId);

                return occurrenceId;
            }
            case PresentationMappingNode mapping:
            {
                var occurrenceId = ids.NextOccurrence();
                RegisterAnchor(mapping.Properties, occurrenceId, anchors);
                var entries = new List<SerializationMappingEntryOccurrence>(mapping.Entries.Count);

                foreach (var entry in mapping.Entries)
                {
                    var keyId = ComposeNode(entry.Key, occurrences, anchors, ids, diagnostics, ledger);
                    var valueId = ComposeNode(entry.Value, occurrences, anchors, ids, diagnostics, ledger);
                    entries.Add(new(entry.Id, keyId, valueId));
                }

                var occurrence = new SerializationMappingOccurrence(occurrenceId, mapping.Id, mapping.Properties, mapping.IsFlow, entries);
                occurrences.Add(occurrenceId, occurrence);
                ledger.Link(mapping.Id, occurrenceId);

                return occurrenceId;
            }
            default:
                throw diagnostics.CreateException(
                    "YAML022",
                    YamlException.Phase.SerializeCompose,
                    $"Unsupported presentation node '{node.GetType().Name}'.",
                    node.Span);
        }
    }

    private static void RegisterAnchor(PresentationNodeProperties properties, OccurrenceId occurrenceId, IDictionary<string, OccurrenceId> anchors)
    {
        if (properties.Anchor is not { } anchor)
            return;

        anchors[anchor.Value] = occurrenceId;
    }
}
