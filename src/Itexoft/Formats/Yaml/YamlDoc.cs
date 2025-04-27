// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Formats.Yaml.Internal;

namespace Itexoft.Formats;

public static class YamlDoc
{
    public static string Serialize<T>(T? value) => Serialize(value, typeof(T));

    public static string Serialize(object? value, Type type)
    {
        type.Required();

        return YamlRuntime.Default.Serialize(value, type);
    }

    public static T? Deserialize<T>(string yaml) => (T?)Deserialize(yaml, typeof(T));

    public static object? Deserialize(string yaml, Type type)
    {
        yaml.Required();
        type.Required();

        return YamlRuntime.Default.Deserialize(yaml, type);
    }
}
