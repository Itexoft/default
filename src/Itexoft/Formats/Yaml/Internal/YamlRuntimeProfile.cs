// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Yaml.Internal.Binding;

namespace Itexoft.Formats.Yaml.Internal;

internal sealed class YamlRuntimeProfile(
    IMetadataAcl metadataAcl,
    ITypeDescriptorProvider typeDescriptorProvider,
    IScalarCodecProvider scalarCodecProvider)
{
    public IMetadataAcl MetadataAcl { get; } = metadataAcl;

    public ITypeDescriptorProvider TypeDescriptorProvider { get; } = typeDescriptorProvider;

    public IScalarCodecProvider ScalarCodecProvider { get; } = scalarCodecProvider;

    public static YamlRuntimeProfile CreateDefault()
    {
        var metadataAcl = new ReflectionMetadataAcl();
        var typeDescriptorProvider = new ReflectionTypeDescriptorProvider(metadataAcl);
        var scalarCodecProvider = new DefaultScalarCodecProvider();

        return new(metadataAcl, typeDescriptorProvider, scalarCodecProvider);
    }
}
