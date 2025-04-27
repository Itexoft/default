// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Itexoft.Extensions;
using Itexoft.Formats.Json;

namespace Itexoft.AI.OpenAI;

public sealed record OpenAiResponseFormat([property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("json_schema")]
    OpenAiResponseFormatJsonSchema? JsonSchema = null);

public sealed record OpenAiResponseFormatJsonSchema([property: JsonPropertyName("name")] string Name, [property: JsonPropertyName("schema")] string Schema, [property: JsonPropertyName("strict")] bool Strict = true);

public static class OpenAiResponseFormats
{
    public static OpenAiResponseFormat Default => null!;

    public static OpenAiResponseFormat JsonObject { get; } = new("json_object");

    public static OpenAiResponseFormat JsonSchema(string schema, string name = "response", bool strict = true)
    {
        schema = schema.RequiredNotWhiteSpace();
        name = name.RequiredNotWhiteSpace();

        return new OpenAiResponseFormat("json_schema", new OpenAiResponseFormatJsonSchema(name, schema, strict));
    }

    public static OpenAiResponseFormat JsonSchema<T>(JsonSerializerContext context, string name = "response", bool strict = true) =>
        JsonSchema(context, typeof(T), name, strict);

    public static OpenAiResponseFormat JsonSchema(JsonSerializerContext context, Type type, string name = "response", bool strict = true)
    {
        context.Required();
        type.Required();
        name = name.RequiredNotWhiteSpace();

        var schema = context.GenerateSchema(type);

        return new OpenAiResponseFormat("json_schema", new OpenAiResponseFormatJsonSchema(name, schema, strict));
    }
}
