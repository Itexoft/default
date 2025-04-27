// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
#if !NativeAOT
using System.Text.Json.Serialization.Metadata;
#endif

namespace Itexoft.Formats.Json;

public static class JsonDefaultOptions
{
    private static JsonSerializerOptions DefaultOptions { get; } = CreateOptions(false, encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping);
    private static JsonSerializerOptions IndentedOptions { get; } = CreateOptions(true, encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

    private static JsonSerializerOptions CamelCaseOptions { get; } = CreateOptions(
        false,
        JsonNamingPolicy.CamelCase,
        encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

    private static JsonSerializerOptions CamelCaseIndentedOptions { get; } = CreateOptions(
        true,
        JsonNamingPolicy.CamelCase,
        encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

    private static JsonSerializerOptions JsonNodeOptions { get; } = CreateOptions(
        false,
        propertyNameCaseInsensitive: false,
        encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

    private static JsonSerializerOptions JsonNodeIndentedOptions { get; } = CreateOptions(
        true,
        propertyNameCaseInsensitive: false,
        encoder: JavaScriptEncoder.UnsafeRelaxedJsonEscaping);

    public static JsonSerializerOptions Default => new(DefaultOptions);

    public static JsonSerializerOptions Indented => new(IndentedOptions);

    public static JsonSerializerOptions CamelCase => new(CamelCaseOptions);

    public static JsonSerializerOptions CamelCaseIndented => new(CamelCaseIndentedOptions);

    public static JsonSerializerOptions JsonNode => new(JsonNodeOptions);

    public static JsonSerializerOptions JsonNodeIndented => new(JsonNodeIndentedOptions);

    private static JsonSerializerOptions CreateOptions(
        bool writeIndented,
        JsonNamingPolicy? propertyNamingPolicy = null,
        bool propertyNameCaseInsensitive = true,
        JsonIgnoreCondition defaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        JavaScriptEncoder? encoder = null)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = propertyNamingPolicy,
            PropertyNameCaseInsensitive = propertyNameCaseInsensitive,
            DefaultIgnoreCondition = defaultIgnoreCondition,
            WriteIndented = writeIndented,
        };

        if (encoder is not null)
            options.Encoder = encoder;

        return options;
    }

#if !NativeAOT
    private static JsonSerializerOptions CreateReflectionOptions(JsonSerializerOptions options)
    {
        var result = new JsonSerializerOptions(options);
        result.TypeInfoResolverChain.Add(new DefaultJsonTypeInfoResolver());

        return result;
    }
#endif
#if !NativeAOT
    private static JsonSerializerOptions ReflectionDefaultOptions { get; } = CreateReflectionOptions(DefaultOptions);
    private static JsonSerializerOptions ReflectionIndentedOptions { get; } = CreateReflectionOptions(IndentedOptions);
    private static JsonSerializerOptions ReflectionCamelCaseOptions { get; } = CreateReflectionOptions(CamelCaseOptions);
    private static JsonSerializerOptions ReflectionCamelCaseIndentedOptions { get; } = CreateReflectionOptions(CamelCaseIndentedOptions);
#endif

#if !NativeAOT
    public static JsonSerializerOptions ReflectionDefault => new(ReflectionDefaultOptions);

    public static JsonSerializerOptions ReflectionIndented => new(ReflectionIndentedOptions);

    public static JsonSerializerOptions ReflectionCamelCase => new(ReflectionCamelCaseOptions);

    public static JsonSerializerOptions ReflectionCamelCaseIndented => new(ReflectionCamelCaseIndentedOptions);
#endif
}
