// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Reflection;
using Itexoft.Extensions;

namespace Itexoft.AI.Api.OpenAI;

public enum OpenAiJsonSchemaMode
{
    Ignore,
    Required,
}

[AttributeUsage(AttributeTargets.Property)]
public sealed class OpenAiJsonSchemaAttribute(OpenAiJsonSchemaMode mode) : Attribute
{
    public OpenAiJsonSchemaMode Mode { get; } = mode;
}

internal static class OpenAiJsonSchemaGenerator
{
    private const int maxDepth = 64;

    public static object GenerateSchema(Type type) => GenerateTypeSchema(type.Required(), new HashSet<Type>(), 0);

    private static object GenerateTypeSchema(Type sourceType, HashSet<Type> chain, int depth)
    {
        if (depth > maxDepth)
            throw new InvalidOperationException($"OpenAI JSON schema depth exceeds {maxDepth} for type {sourceType}.");

        var type = Nullable.GetUnderlyingType(sourceType) ?? sourceType;

        if (IsSimpleType(type))
            return BuildSimpleSchema(type);

        if (TryGetDictionaryValueType(type, out var valueType))
        {
            return new
            {
                type = "object",
                additionalProperties = GenerateTypeSchema(valueType!, chain, depth + 1),
            };
        }

        if (TryGetEnumerableElementType(type, out var elementType))
        {
            return new
            {
                type = "array",
                items = GenerateTypeSchema(elementType!, chain, depth + 1),
            };
        }

        if (!chain.Add(type))
            throw new InvalidOperationException($"OpenAI JSON schema does not support recursive type graphs. Type '{type}' is recursive.");

        try
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(static property => property.CanRead)
                .Where(static property => property.GetCustomAttribute<OpenAiJsonSchemaAttribute>()?.Mode != OpenAiJsonSchemaMode.Ignore).ToArray();

            var schemaProperties = properties.ToDictionary(
                static property => property.Name,
                property => GenerateTypeSchema(property.PropertyType, chain, depth + 1),
                StringComparer.Ordinal);

            var required = properties.Where(IsRequiredProperty).Select(static property => property.Name).ToArray();

            return new
            {
                type = "object",
                properties = schemaProperties,
                required,
                additionalProperties = false,
            };
        }
        finally
        {
            chain.Remove(type);
        }
    }

    private static bool IsSimpleType(Type type) =>
        type.IsPrimitive
        || type == typeof(decimal)
        || type == typeof(string)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(Guid)
        || type == typeof(Uri)
        || type.IsEnum;

    private static object BuildSimpleSchema(Type type)
    {
        if (type == typeof(string) || type == typeof(DateTime) || type == typeof(DateTimeOffset) || type == typeof(Guid) || type == typeof(Uri))
            return new { type = "string" };

        if (type.IsEnum)
            return new { type = "string", @enum = Enum.GetNames(type) };

        if (type == typeof(bool))
            return new { type = "boolean" };

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new { type = "number" };

        if (type == typeof(int)
            || type == typeof(uint)
            || type == typeof(long)
            || type == typeof(ulong)
            || type == typeof(short)
            || type == typeof(ushort)
            || type == typeof(byte)
            || type == typeof(sbyte))
            return new { type = "integer" };

        return new { type = "object" };
    }

    private static bool TryGetDictionaryValueType(Type type, out Type? valueType)
    {
        valueType = null;

        if (type == typeof(string))
            return false;

        if (!typeof(IDictionary).IsAssignableFrom(type) && !ImplementsGenericDictionary(type))
            return false;

        var args = type.GetGenericArguments();
        valueType = args.Length >= 2 ? args[1] : typeof(object);

        return true;
    }

    private static bool ImplementsGenericDictionary(Type type) =>
        type.GetInterfaces().Any(static iface => iface.IsGenericType && iface.GetGenericTypeDefinition() == typeof(IDictionary<,>));

    private static bool TryGetEnumerableElementType(Type type, out Type? elementType)
    {
        elementType = null;

        if (type == typeof(string))
            return false;

        if (type.IsArray)
        {
            elementType = type.GetElementType() ?? typeof(object);

            return true;
        }

        if (!typeof(IEnumerable).IsAssignableFrom(type))
            return false;

        if (type.IsGenericType)
            elementType = type.GetGenericArguments().FirstOrDefault() ?? typeof(object);
        else
            elementType = typeof(object);

        return true;
    }

    private static bool IsRequiredProperty(PropertyInfo property) =>
        property.GetCustomAttribute<OpenAiJsonSchemaAttribute>()?.Mode == OpenAiJsonSchemaMode.Required;
}
