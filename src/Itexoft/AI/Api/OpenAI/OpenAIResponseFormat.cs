// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;
using Itexoft.Extensions;

namespace Itexoft.AI.Api.OpenAI;

public sealed record OpenAiResponseFormat
{
    public OpenAiResponseFormat(string type, OpenAiResponseFormatJsonSchema? jsonSchema = null)
    {
        this.Type = type.RequiredNotWhiteSpace();
        this.JsonSchema = jsonSchema;

        if (this.Type == "json_schema" && this.JsonSchema is null)
            throw new ArgumentException("OpenAI response format type 'json_schema' requires a schema descriptor.", nameof(jsonSchema));
    }

    [JsonPropertyName("type")] public string Type { get; init; }

    [JsonPropertyName("json_schema")] public OpenAiResponseFormatJsonSchema? JsonSchema { get; init; }
}

public sealed record OpenAiResponseFormatJsonSchema
{
    public OpenAiResponseFormatJsonSchema(string name, JsonElement schema, bool strict = true)
    {
        this.Name = name.RequiredNotWhiteSpace();
        this.Schema = schema;
        this.Strict = strict;
    }

    [JsonPropertyName("name")] public string Name { get; init; }

    [JsonPropertyName("schema")] public JsonElement Schema { get; init; }

    [JsonPropertyName("strict")] public bool Strict { get; init; } = true;
}

public static class OpenAiResponseFormats
{
    public static OpenAiResponseFormat Default => null!;

    public static OpenAiResponseFormat JsonObject { get; } = new("json_object");

    public static OpenAiResponseFormat JsonSchema(string schema, string name = "response", bool strict = true)
    {
        schema.RequiredNotWhiteSpace();
        name.RequiredNotWhiteSpace();

        using var document = JsonDocument.Parse(schema);
        var schemaElement = document.RootElement.Clone();

        return new OpenAiResponseFormat("json_schema", new OpenAiResponseFormatJsonSchema(name, schemaElement, strict));
    }

    public static OpenAiResponseFormat JsonSchema<T>(string name = "response", bool strict = true) =>
        JsonSchema(typeof(T), name, strict);

    public static OpenAiResponseFormat JsonSchema(Type type, string name = "response", bool strict = true)
    {
        type.Required();
        name.RequiredNotWhiteSpace();

        var generatedSchema = OpenAiJsonSchemaGenerator.GenerateSchema(type);
        var schema = JsonSerializer.SerializeToElement(generatedSchema, OpenAiJson.Options);

        return new OpenAiResponseFormat("json_schema", new OpenAiResponseFormatJsonSchema(name, schema, strict));
    }
}
