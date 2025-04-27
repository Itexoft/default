// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Itexoft.Extensions;

namespace Itexoft.Formats.Json;

public static class JsonSchemaGenerator
{
    public static string GenerateSchema(JsonTypeInfo typeInfo, JsonSchemaExporterOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        return typeInfo.GetJsonSchemaAsNode(CreateOptions(options)).ToJsonString(typeInfo.Options);
    }

    public static string GenerateSchema(JsonSerializerContext context, Type type, JsonSchemaExporterOptions? options = null)
    {
        context.Required();
        type.Required();

        return GenerateSchema(
            context.GetTypeInfo(type)
            ?? throw new ArgumentException(
                $"The specified type {type} is not a known JSON-serializable type in context {context.GetType()}.",
                nameof(type)),
            options);
    }

    public static string GenerateSchema<T>(JsonSerializerContext context, JsonSchemaExporterOptions? options = null) =>
        GenerateSchema(context, typeof(T), options);

    public static string GenerateSchemaElement(JsonTypeInfo typeInfo, JsonSchemaExporterOptions? options = null) => GenerateSchema(typeInfo, options);

    public static string GenerateSchemaElement(JsonSerializerContext context, Type type, JsonSchemaExporterOptions? options = null) =>
        GenerateSchema(context, type, options);

    public static string GenerateSchemaElement<T>(JsonSerializerContext context, JsonSchemaExporterOptions? options = null) =>
        GenerateSchemaElement(context, typeof(T), options);

    private static JsonSchemaExporterOptions CreateOptions(JsonSchemaExporterOptions? options)
    {
        var userTransform = options?.TransformSchemaNode;

        return new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                var schema = ApplyDescription(context, node);

                return userTransform is null ? schema : userTransform(context, schema);
            },
        };
    }

    private static JsonNode ApplyDescription(JsonSchemaExporterContext context, JsonNode? node)
    {
        if (node is not JsonObject schema)
            return node ?? new JsonObject();

        var description = GetDescription(context.PropertyInfo?.AttributeProvider);

        if (!string.IsNullOrWhiteSpace(description) && !schema.ContainsKey("description"))
            schema["description"] = description;

        return schema;
    }

    private static string? GetDescription(ICustomAttributeProvider? provider)
    {
        if (provider?.GetCustomAttributes(typeof(DescriptionAttribute), false) is not [DescriptionAttribute attribute, ..])
            return null;

        return string.IsNullOrWhiteSpace(attribute.Description) ? null : attribute.Description;
    }
}
