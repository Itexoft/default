// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;
using Itexoft.Formats.Yaml.Internal.Representation;
using Itexoft.Formats.Yaml.Internal.Serialization;

namespace Itexoft.Formats.Yaml.Internal.Session;

internal sealed class YamlSession(
    YamlRuntimeProfile profile,
    YamlIdGenerator ids,
    YamlDiagnostics diagnostics,
    YamlProjectionLedger ledger,
    PresentationStream? presentation,
    IReadOnlyList<YamlDocumentSession> documents)
{
    public YamlRuntimeProfile Profile { get; } = profile;

    public YamlIdGenerator Ids { get; } = ids;

    public YamlDiagnostics Diagnostics { get; } = diagnostics;

    public YamlProjectionLedger Ledger { get; } = ledger;

    public PresentationStream? Presentation { get; } = presentation;

    public IReadOnlyList<YamlDocumentSession> Documents { get; } = documents;

    public static YamlSession CreateForWrite(YamlRuntimeProfile profile) => new(profile, new(), new(), new(), null, []);

    public static YamlSession Load(string yaml, YamlRuntimeProfile profile)
    {
        var diagnostics = new YamlDiagnostics();
        var ids = new YamlIdGenerator();
        var ledger = new YamlProjectionLedger();
        var parser = new YamlPresentationParser(ids, diagnostics);
        var presentation = parser.Parse(yaml);
        var documents = new List<YamlDocumentSession>();

        foreach (var document in presentation.Documents)
        {
            var serialization = YamlSerializationComposer.Compose(document, ids, diagnostics, ledger);
            var representation = YamlRepresentationComposer.Compose(serialization, ids, diagnostics, ledger);
            documents.Add(new(document, serialization, representation));
        }

        diagnostics.ThrowIfAny();

        return new(profile, ids, diagnostics, ledger, presentation, documents);
    }
}

internal sealed class YamlDocumentSession(
    PresentationDocument presentationDocument,
    SerializationDocument serializationDocument,
    RepresentationGraph representationGraph)
{
    public PresentationDocument PresentationDocument { get; } = presentationDocument;

    public SerializationDocument SerializationDocument { get; } = serializationDocument;

    public RepresentationGraph RepresentationGraph { get; } = representationGraph;
}
