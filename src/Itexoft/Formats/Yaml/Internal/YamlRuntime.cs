// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Binding;
using Itexoft.Formats.Yaml.Internal.Emission;
using Itexoft.Formats.Yaml.Internal.Serialization;
using Itexoft.Formats.Yaml.Internal.Session;

namespace Itexoft.Formats.Yaml.Internal;

internal sealed class YamlRuntime
{
    private readonly YamlRuntimeProfile profile;

    public YamlRuntime(YamlRuntimeProfile profile) => this.profile = profile;
    public static YamlRuntime Default { get; } = new(YamlRuntimeProfile.CreateDefault());

    public object? Deserialize(string yaml, Type type)
    {
        var session = YamlSession.Load(yaml, this.profile);

        if (session.Documents.Count == 0)
            throw session.Diagnostics.CreateException("YAML001", YamlException.Phase.Parse, "YAML stream does not contain any document.");

        if (session.Documents.Count != 1)
            throw session.Diagnostics.CreateException("YAML002", YamlException.Phase.Parse, "Public deserialize expects exactly one YAML document.");

        try
        {
            return YamlObjectBinder.Bind(session, session.Documents[0].RepresentationGraph, type);
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw session.Diagnostics.CreateException("YAML900", YamlException.Phase.Bind, ex.Message, innerException: ex);
        }
    }

    public string Serialize(object? value, Type type)
    {
        var session = YamlSession.CreateForWrite(this.profile);

        try
        {
            var representation = YamlObjectExtractor.Extract(session, value, type);
            var forest = YamlSerializationProjector.Project(session, representation);
            var document = YamlPresentationEmitter.Project(session, forest);

            return document.SourceText;
        }
        catch (YamlException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw session.Diagnostics.CreateException("YAML901", YamlException.Phase.Emit, ex.Message, innerException: ex);
        }
    }
}
