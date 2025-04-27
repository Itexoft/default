// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Itexoft.Extensions;

namespace Itexoft.Formats;

public abstract partial class RsonSerializerContext(JsonSerializerOptions? options = null) : JsonSerializerContext(options)
{
    public string Serialize(object obj) => this.RenderValueText(
        obj.Required(),
        this.GetTypeInfoOrThrow(obj.GetType()),
        new HashSet<object>(ReferenceEqualityComparer.Instance));

    private string RenderValueText(object? value, JsonTypeInfo typeInfo, HashSet<object> activePath)
    {
        if (value is null)
            return "null";

        return typeInfo.Kind switch
        {
            JsonTypeInfoKind.Object => this.RenderObjectText(value, typeInfo, activePath),
            JsonTypeInfoKind.Dictionary => this.RenderDictionaryText(value, activePath),
            JsonTypeInfoKind.Enumerable when value is not string => this.RenderEnumerableText(value, activePath),
            _ => this.RenderScalarText(value, typeInfo),
        };
    }

    private string RenderObjectText(object value, JsonTypeInfo typeInfo, HashSet<object> activePath)
    {
        this.EnterReference(value, activePath);

        try
        {
            var blocks = new List<string>(typeInfo.Properties.Count);

            for (var i = 0; i < typeInfo.Properties.Count; i++)
            {
                var property = typeInfo.Properties[i];

                if (property.Get is null)
                    continue;

                var propertyValue = property.Get(value);

                var block = this.RenderFieldBlock(
                    property.Name,
                    GetDescriptionText(property.AttributeProvider),
                    propertyValue is null
                        ? "null"
                        : this.RenderValueText(propertyValue, this.GetTypeInfoOrThrow(propertyValue.GetType()), activePath));

                blocks.Add(block);
            }

            return this.JoinBlocks(blocks);
        }
        finally
        {
            activePath.Remove(value);
        }
    }

    private string RenderEnumerableText(object value, HashSet<object> activePath)
    {
        if (value is not IEnumerable enumerable)
            throw new InvalidOperationException($"Type '{value.GetType()}' is marked as enumerable but does not implement IEnumerable.");

        this.EnterReference(value, activePath);

        try
        {
            var blocks = new List<string>();

            foreach (var item in enumerable)
            {
                var block = this.RenderListItemBlock(
                    item is null ? "null" : this.RenderValueText(item, this.GetTypeInfoOrThrow(item.GetType()), activePath));

                blocks.Add(block);
            }

            return this.JoinBlocks(blocks);
        }
        finally
        {
            activePath.Remove(value);
        }
    }

    private string RenderDictionaryText(object value, HashSet<object> activePath)
    {
        if (value is not IEnumerable enumerable)
            throw new InvalidOperationException($"Type '{value.GetType()}' is marked as dictionary but does not implement IEnumerable.");

        this.EnterReference(value, activePath);

        try
        {
            var blocks = new List<string>();

            foreach (var item in enumerable)
            {
                this.ReadDictionaryPair(item, out var key, out var itemValue);

                var block = this.RenderFieldBlock(
                    this.RenderHeaderValueText(key),
                    null,
                    itemValue is null ? "null" : this.RenderValueText(itemValue, this.GetTypeInfoOrThrow(itemValue.GetType()), activePath));

                blocks.Add(block);
            }

            return this.JoinBlocks(blocks);
        }
        finally
        {
            activePath.Remove(value);
        }
    }

    private string RenderScalarText(object value, JsonTypeInfo typeInfo)
    {
        if (value is string text)
            return this.RenderStringText(text);

        var serialized = JsonSerializer.Serialize(value, typeInfo);

        if (serialized.Length >= 2 && serialized[0] == '"' && serialized[^1] == '"')
        {
            var unquoted = JsonSerializer.Deserialize<string>(serialized)
                           ?? throw new InvalidOperationException($"Could not deserialize scalar text from '{serialized}'.");

            return this.RenderStringText(unquoted);
        }

        return serialized;
    }

    private string RenderFieldBlock(string header, string? description, string body)
    {
        var builder = new StringBuilder();
        var normalizedBody = this.NormalizeLineEndings(body);

        builder.Append(this.RenderHeaderText(header));

        if (!normalizedBody.Contains('\n'))
        {
            builder.Append(": ");
            builder.Append(normalizedBody);

            if (!string.IsNullOrWhiteSpace(description))
            {
                builder.Append("     # ");
                builder.Append(description);
            }

            return builder.ToString();
        }

        builder.Append(':');

        if (!string.IsNullOrWhiteSpace(description))
        {
            builder.Append("     # ");
            builder.Append(description);
        }

        builder.AppendLine();
        builder.Append(normalizedBody.PadLines(2));

        return builder.ToString();
    }

    private string RenderListItemBlock(string body)
    {
        var normalizedBody = this.NormalizeLineEndings(body);

        if (!normalizedBody.Contains('\n'))
            return "- " + normalizedBody;

        return "-\n" + normalizedBody.PadLines(2);
    }

    private string JoinBlocks(List<string> blocks)
    {
        var builder = new StringBuilder();
        var hasContent = false;

        for (var i = 0; i < blocks.Count; i++)
        {
            if (string.IsNullOrWhiteSpace(blocks[i]))
                continue;

            if (hasContent)
                builder.AppendLine();

            builder.Append(blocks[i]);
            hasContent = true;
        }

        return builder.ToString();
    }

    private JsonTypeInfo GetTypeInfoOrThrow(Type type) =>
        this.GetTypeInfo(type) ?? throw new InvalidOperationException($"Type '{type}' is not registered in serializer context '{this.GetType()}'.");

    private static string? GetDescriptionText(ICustomAttributeProvider? provider)
    {
        if (provider?.GetCustomAttributes(typeof(DescriptionAttribute), true) is not [DescriptionAttribute attribute, ..])
            return null;

        return string.IsNullOrWhiteSpace(attribute.Description) ? null : attribute.Description;
    }

    private string RenderHeaderValueText(object? value)
    {
        if (value is null)
            return "null";

        var text = this.RenderValueText(value, this.GetTypeInfoOrThrow(value.GetType()), new HashSet<object>(ReferenceEqualityComparer.Instance));

        return this.CollapseLineBreaks(text);
    }

    private void ReadDictionaryPair(object? pair, out object? key, out object? value)
    {
        switch (pair)
        {
            case null:
                throw new InvalidOperationException("Dictionary enumeration produced a null entry.");
            case DictionaryEntry entry:
                key = entry.Key;
                value = entry.Value;

                return;
        }

        var type = pair.GetType();
        var keyProperty = type.GetProperty("Key", BindingFlags.Instance | BindingFlags.Public);
        var valueProperty = type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);

        if (keyProperty is null || valueProperty is null)
            throw new InvalidOperationException($"Dictionary entry type '{type}' does not expose public Key/Value properties.");

        key = keyProperty.GetValue(pair);
        value = valueProperty.GetValue(pair);
    }

    private void EnterReference(object value, HashSet<object> activePath)
    {
        if (!activePath.Add(value))
        {
            throw new NotSupportedException(
                $"RSON serialization supports only tree-shaped data. Recursive/shared graph detected at type '{value.GetType()}'.");
        }
    }

    private string RenderHeaderText(string value) => this.CollapseLineBreaks(value);

    private string RenderStringText(string value)
    {
        var normalized = this.NormalizeLineEndings(value);

        if (normalized.Length == 0)
            return "\"\"";

        if (!normalized.Contains('\n'))
            return normalized;

        return $$"""
                 ```text
                 {{
                     normalized
                 }}
                 ```
                 """;
    }

    private string CollapseLineBreaks(string value) =>
        this.NormalizeLineEndings(value).Replace("\n", " ").Trim();

    private string NormalizeLineEndings(string value) => value.ReplaceLineEndings("\n");
}
