// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Itexoft.Formats.Json;

public static class JsonUtilities
{
    public static string? Indent(string json) => JsonNode.Parse(json)?.ToJsonString(JsonDefaultOptions.Indented);
#if !NativeAOT
    public static string Serialize(object? value) => JsonSerializer.Serialize(value, JsonDefaultOptions.ReflectionDefault);

    public static string SerializeIndented(object? value) => JsonSerializer.Serialize(value, JsonDefaultOptions.ReflectionIndented);

    public static string SerializeCamelCase(object? value) => JsonSerializer.Serialize(value, JsonDefaultOptions.ReflectionCamelCase);

    public static string SerializeCamelCaseIndented(object? value) => JsonSerializer.Serialize(value, JsonDefaultOptions.ReflectionCamelCaseIndented);

    public static JsonElement SerializeToElement<T>(T value) => JsonSerializer.SerializeToElement(value, JsonDefaultOptions.ReflectionDefault);

    public static JsonElement SerializeToCamelCaseElement<T>(T value) =>
        JsonSerializer.SerializeToElement(value, JsonDefaultOptions.ReflectionCamelCase);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonDefaultOptions.ReflectionDefault);

    public static T? DeserializeCamelCase<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonDefaultOptions.ReflectionCamelCase);

    public static T? Deserialize<T>(JsonNode node) => Deserialize<T>(node.ToJsonString(JsonDefaultOptions.ReflectionDefault));

    public static T? DeserializeCamelCase<T>(JsonNode node) => DeserializeCamelCase<T>(node.ToJsonString(JsonDefaultOptions.ReflectionCamelCase));

    public static JsonElement? ToElement(JsonNode? node)
    {
        if (node is null)
            return null;

        using var document = JsonDocument.Parse(node.ToJsonString(JsonDefaultOptions.ReflectionDefault));

        return document.RootElement.Clone();
    }

    public static string? ToJsonString(JsonNode? node, bool indented = false)
    {
        if (node?.GetValueKind() == JsonValueKind.String)
            return node.GetValue<string>();

        return node?.ToJsonString(indented ? JsonDefaultOptions.ReflectionCamelCase : JsonDefaultOptions.ReflectionDefault) ?? null;
    }

    public static void Serialize<T>(Stream stream, T typed, JsonSerializerOptions serializerOptions) =>
        JsonSerializer.Serialize(stream, typed, serializerOptions);

    public static T? Deserialize<T>(FileStream stream, JsonSerializerOptions serializerOptions) =>
        JsonSerializer.Deserialize<T>(stream, serializerOptions);
#endif
}
