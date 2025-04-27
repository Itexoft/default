// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Itexoft.Formats.Json;

public sealed class JsonSchemaDto
{
    public const string ArrayType = "array";
    public const string BooleanType = "boolean";
    public const string IntegerType = "integer";
    public const string NullType = "null";
    public const string NumberType = "number";
    public const string ObjectType = "object";
    public const string StringType = "string";

    [JsonPropertyName("$ref")] public string? Ref { get; set; }

    [JsonPropertyName("$defs")] public Dictionary<string, JsonSchemaDto>? Definitions { get; set; }

    [JsonPropertyName("type")] public JsonSchemaTypeExpression? Type { get; set; }

    [JsonPropertyName("format")] public string? Format { get; set; }

    [JsonPropertyName("const")] public JsonElement? Const { get; set; }

    [JsonPropertyName("enum")] public JsonElement[]? Enum { get; set; }

    [JsonPropertyName("properties")] public Dictionary<string, JsonSchemaDto>? Properties { get; set; }

    [JsonPropertyName("required")] public string[]? Required { get; set; }

    [JsonPropertyName("additionalProperties")]
    public JsonSchemaAdditionalProperties? AdditionalProperties { get; set; }

    [JsonPropertyName("items")] public JsonSchemaItems? Items { get; set; }

    [JsonPropertyName("prefixItems")] public JsonSchemaDto[]? PrefixItems { get; set; }

    [JsonPropertyName("oneOf")] public JsonSchemaDto[]? OneOf { get; set; }

    [JsonPropertyName("anyOf")] public JsonSchemaDto[]? AnyOf { get; set; }

    [JsonPropertyName("allOf")] public JsonSchemaDto[]? AllOf { get; set; }

    [JsonPropertyName("pattern")] public string? Pattern { get; set; }

    [JsonPropertyName("minLength")] public long? MinLength { get; set; }

    [JsonPropertyName("maxLength")] public long? MaxLength { get; set; }

    [JsonPropertyName("minItems")] public long? MinItems { get; set; }

    [JsonPropertyName("maxItems")] public long? MaxItems { get; set; }

    [JsonPropertyName("minimum")] public long? Minimum { get; set; }

    [JsonPropertyName("exclusiveMinimum")] public long? ExclusiveMinimum { get; set; }

    [JsonPropertyName("maximum")] public long? Maximum { get; set; }

    [JsonPropertyName("exclusiveMaximum")] public long? ExclusiveMaximum { get; set; }

    [JsonPropertyName("description")] public string? Description { get; set; }

    [JsonPropertyName("title")] public string? Title { get; set; }
}

[JsonConverter(typeof(JsonSchemaTypeExpressionJsonConverter))]
public sealed class JsonSchemaTypeExpression
{
    public JsonSchemaTypeExpression(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("JSON schema type cannot be empty.", nameof(name));

        this.Name = name;
    }

    public JsonSchemaTypeExpression(string[] names)
    {
        ArgumentNullException.ThrowIfNull(names);

        if (names.Length == 0)
            throw new ArgumentException("JSON schema type set cannot be empty.", nameof(names));

        this.Names = names;
    }

    public string? Name { get; }

    public string[]? Names { get; }

    public sealed class JsonSchemaTypeExpressionJsonConverter : JsonConverter<JsonSchemaTypeExpression>
    {
        public override JsonSchemaTypeExpression Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.String => new(reader.GetString() ?? throw new JsonException("JSON schema type cannot be null.")),
                JsonTokenType.StartArray => new(
                    JsonSerializer.Deserialize(ref reader, JsonSchemaContext.Default.StringArray)
                    ?? throw new JsonException("JSON schema type array cannot be null.")),
                _ => throw new JsonException("JSON schema type must be a string or string array."),
            };

        public override void Write(Utf8JsonWriter writer, JsonSchemaTypeExpression value, JsonSerializerOptions options)
        {
            if (value.Name is not null)
            {
                writer.WriteStringValue(value.Name);

                return;
            }

            JsonSerializer.Serialize(
                writer,
                value.Names ?? throw new JsonException("JSON schema type array is missing."),
                JsonSchemaContext.Default.StringArray);
        }
    }
}

[JsonConverter(typeof(JsonSchemaItemsJsonConverter))]
public sealed class JsonSchemaItems
{
    public JsonSchemaItems(JsonSchemaDto schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        this.Schema = schema;
    }

    public JsonSchemaItems(JsonSchemaDto[] tuple)
    {
        ArgumentNullException.ThrowIfNull(tuple);
        this.Tuple = tuple;
    }

    public JsonSchemaDto? Schema { get; }

    public JsonSchemaDto[]? Tuple { get; }

    public sealed class JsonSchemaItemsJsonConverter : JsonConverter<JsonSchemaItems>
    {
        public override JsonSchemaItems Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.StartObject => new(
                    JsonSerializer.Deserialize(ref reader, JsonSchemaContext.Default.JsonSchemaDto)
                    ?? throw new JsonException("JSON schema items definition cannot be null.")),
                JsonTokenType.StartArray => new(
                    JsonSerializer.Deserialize(ref reader, JsonSchemaContext.Default.JsonSchemaDtoArray)
                    ?? throw new JsonException("JSON schema tuple definition cannot be null.")),
                _ => throw new JsonException("JSON schema items must be an object or array."),
            };

        public override void Write(Utf8JsonWriter writer, JsonSchemaItems value, JsonSerializerOptions options)
        {
            if (value.Schema is not null)
            {
                JsonSerializer.Serialize(writer, value.Schema, JsonSchemaContext.Default.JsonSchemaDto);

                return;
            }

            JsonSerializer.Serialize(
                writer,
                value.Tuple ?? throw new JsonException("JSON schema tuple definition is missing."),
                JsonSchemaContext.Default.JsonSchemaDtoArray);
        }
    }
}

[JsonConverter(typeof(JsonSchemaAdditionalPropertiesJsonConverter))]
public sealed class JsonSchemaAdditionalProperties
{
    public JsonSchemaAdditionalProperties(bool allowed) => this.Allowed = allowed;

    public JsonSchemaAdditionalProperties(JsonSchemaDto schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        this.Schema = schema;
    }

    public bool? Allowed { get; }

    public JsonSchemaDto? Schema { get; }

    public sealed class JsonSchemaAdditionalPropertiesJsonConverter : JsonConverter<JsonSchemaAdditionalProperties>
    {
        public override JsonSchemaAdditionalProperties Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType switch
            {
                JsonTokenType.True => new(true),
                JsonTokenType.False => new(false),
                JsonTokenType.StartObject => new(
                    JsonSerializer.Deserialize(ref reader, JsonSchemaContext.Default.JsonSchemaDto)
                    ?? throw new JsonException("JSON schema additionalProperties schema cannot be null.")),
                _ => throw new JsonException("JSON schema additionalProperties must be a boolean or object."),
            };

        public override void Write(Utf8JsonWriter writer, JsonSchemaAdditionalProperties value, JsonSerializerOptions options)
        {
            if (value.Schema is not null)
            {
                JsonSerializer.Serialize(writer, value.Schema, JsonSchemaContext.Default.JsonSchemaDto);

                return;
            }

            writer.WriteBooleanValue(value.Allowed ?? throw new JsonException("JSON schema additionalProperties value is missing."));
        }
    }
}

[JsonSerializable(typeof(string)), JsonSerializable(typeof(string[])), JsonSerializable(typeof(JsonElement)), JsonSerializable(typeof(JsonElement[])),
 JsonSerializable(typeof(Dictionary<string, JsonSchemaDto>)), JsonSerializable(typeof(JsonSchemaDto)), JsonSerializable(typeof(JsonSchemaDto[])),
 JsonSerializable(typeof(JsonSchemaTypeExpression)), JsonSerializable(typeof(JsonSchemaItems)),
 JsonSerializable(typeof(JsonSchemaAdditionalProperties)),
 JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class JsonSchemaContext : JsonSerializerContext { }
