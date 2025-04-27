// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Presentation;

namespace Itexoft.Formats.Yaml.Internal.Core;

internal sealed class YamlProjectionLedger
{
    private readonly Dictionary<OccurrenceId, SemanticNodeId> occurrenceToSemantic = [];
    private readonly Dictionary<SemanticNodeId, BindingId> semanticToBinding = [];
    private readonly Dictionary<SyntaxId, OccurrenceId> syntaxToOccurrence = [];
    private readonly Dictionary<string, List<PresentationTrivia>> triviaResidue = [];
    private readonly Dictionary<SemanticNodeId, IReadOnlyDictionary<string, object?>> unmatchedMembers = [];

    public void Link(SyntaxId syntaxId, OccurrenceId occurrenceId) => this.syntaxToOccurrence[syntaxId] = occurrenceId;

    public void Link(OccurrenceId occurrenceId, SemanticNodeId semanticNodeId) => this.occurrenceToSemantic[occurrenceId] = semanticNodeId;

    public void Link(SemanticNodeId semanticNodeId, BindingId bindingId) => this.semanticToBinding[semanticNodeId] = bindingId;

    public void CaptureTrivia(string key, IEnumerable<PresentationTrivia> trivia) => this.triviaResidue[key] = [..trivia];

    public void CaptureUnmatchedMembers(SemanticNodeId semanticNodeId, IReadOnlyDictionary<string, object?> members) =>
        this.unmatchedMembers[semanticNodeId] = members;

    public bool TryGetOccurrence(SyntaxId syntaxId, out OccurrenceId occurrenceId) => this.syntaxToOccurrence.TryGetValue(syntaxId, out occurrenceId);

    public bool TryGetSemantic(OccurrenceId occurrenceId, out SemanticNodeId semanticNodeId) =>
        this.occurrenceToSemantic.TryGetValue(occurrenceId, out semanticNodeId);
}
