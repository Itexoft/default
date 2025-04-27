// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.EmbeddedWeb;

[AttributeUsage(AttributeTargets.Method)]
public sealed class EmbeddedWebRpcMethodAttribute : Attribute
{
    public EmbeddedWebRpcMethodAttribute() { }

    public EmbeddedWebRpcMethodAttribute(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("RPC method name cannot be empty.", nameof(name));

        this.Name = name;
    }

    public string? Name { get; }
}
