// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.ComponentModel;
using System.Text;
using System.Text.Json.Serialization.Metadata;
using Itexoft.Formats.Dkon.Internal;

namespace Itexoft.Formats.Dkon;

public partial class DkonFormat
{
    public string GetContract(Type type, out DkonSyntaxLevel syntaxLevel, bool beautify = true)
    {
        ArgumentNullException.ThrowIfNull(type);

        var node = this.BuildContractNode(type, true, null, new HashSet<Type>());
        syntaxLevel = GetSyntaxLevel(node);

        if (beautify)
            DkonFormatters.Beautify(node);

        return SerializeNodeOrEmpty(node);
    }

    private DkonNode BuildContractNode(Type type, bool isRoot, string? label, Dictionary<Type, DkonNode> contractBodies)
    {
        if (!this.TryGetDkonTypeInfo(type, out var typeInfo))
            throw new ArgumentException($"The specified type {type} is not a known type.", nameof(type));

        if (!IsContainer(typeInfo))
            return CreatePlaceholderNode(typeInfo.Type, label ?? "value", null);

        var contractType = Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type;

        if (contractBodies.TryGetValue(contractType, out var cachedBody))
            return WrapContractBody(cachedBody, isRoot);

        var body = new DkonNode();
        contractBodies.Add(contractType, body);

        PopulateContractBody(
            body,
            typeInfo.Kind switch
            {
                JsonTypeInfoKind.Object => this.BuildObjectContractNode(typeInfo, contractBodies),
                JsonTypeInfoKind.Dictionary => this.BuildDictionaryContractNode(typeInfo, label, contractBodies),
                JsonTypeInfoKind.Enumerable => this.BuildEnumerableContractNode(typeInfo, label, contractBodies),
                _ => new DkonNode(),
            });

        return WrapContractBody(body, isRoot);
    }

    private DkonNode BuildObjectContractNode(JsonTypeInfo typeInfo, Dictionary<Type, DkonNode> contractBodies)
    {
        DkonNode? head = null;
        DkonNode? tail = null;
        var properties = typeInfo.Properties;

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var description = TryGetContractDescription(property);
            var propertyLabel = HumanizeLabel(property.Name);
            var valueNode = this.BuildPropertyValueNode(property, propertyLabel, description, contractBodies);

            var keyNode = new DkonNode(property.Name)
            {
                Ref = valueNode,
            };

            Append(ref head, ref tail, keyNode);
        }

        return head ?? new DkonNode();
    }

    private DkonNode BuildDictionaryContractNode(JsonTypeInfo typeInfo, string? label, Dictionary<Type, DkonNode> contractBodies)
    {
        var keyType = typeInfo.KeyType ?? typeof(object);

        if (!IsScalarCompatibleType(keyType))
            throw new InvalidOperationException($"DKON contract requires scalar-compatible dictionary keys. Type '{keyType}' is not supported.");

        var valueType = typeInfo.ElementType ?? typeof(object);

        return new DkonNode(CreatePlaceholderValue(keyType, BuildCollectionLabel(label, "key"), null))
        {
            Bracing = DkonBracing.Inline,
            Ref = this.BuildContractNode(valueType, false, BuildCollectionLabel(label, "value"), contractBodies),
        };
    }

    private DkonNode BuildEnumerableContractNode(JsonTypeInfo typeInfo, string? label, Dictionary<Type, DkonNode> contractBodies)
    {
        var elementType = typeInfo.ElementType ?? typeof(object);

        return this.BuildContractNode(elementType, false, BuildCollectionLabel(label, "item"), contractBodies);
    }

    private DkonNode BuildPropertyValueNode(JsonPropertyInfo property, string label, string? description, Dictionary<Type, DkonNode> contractBodies)
    {
        if (!this.TryGetDkonTypeInfo(property.PropertyType, out var propertyTypeInfo))
            throw new ArgumentException($"The specified type {property.PropertyType} is not a known type.", nameof(property));

        return IsContainer(propertyTypeInfo)
            ? this.BuildContractNode(property.PropertyType, false, null, contractBodies)
            : CreatePlaceholderNode(property.PropertyType, label, description);
    }

    private static DkonNode WrapContractBody(DkonNode body, bool isRoot)
    {
        if (isRoot)
            return body;

        return new DkonNode
        {
            Alt = body,
        };
    }

    private static void PopulateContractBody(DkonNode target, DkonNode source)
    {
        target.Value = source.Value;
        target.Bracing = source.Bracing;
        target.Ref = source.Ref;
        target.Alt = source.Alt;
        target.Next = source.Next;
    }

    public string GetContract<T>(out DkonSyntaxLevel syntaxLevel, bool beautify = true) => this.GetContract(typeof(T), out syntaxLevel, beautify);

    private DkonNode BuildContractNode(Type type, bool isRoot, string? label, HashSet<Type> activeTypes)
    {
        if (!this.TryGetDkonTypeInfo(type, out var typeInfo))
            throw new ArgumentException($"The specified type {type} is not a known type.", nameof(type));

        if (!IsContainer(typeInfo))
            return CreatePlaceholderNode(typeInfo.Type, label ?? "value", null);

        var contractType = Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type;

        if (!activeTypes.Add(contractType))
            throw new InvalidOperationException($"DKON contract does not support recursive type graphs. Type '{contractType}' is recursive.");

        try
        {
            return typeInfo.Kind switch
            {
                JsonTypeInfoKind.Object => this.BuildObjectContractNode(typeInfo, isRoot, activeTypes),
                JsonTypeInfoKind.Dictionary => this.BuildDictionaryContractNode(typeInfo, isRoot, label, activeTypes),
                JsonTypeInfoKind.Enumerable => this.BuildEnumerableContractNode(typeInfo, isRoot, label, activeTypes),
                _ => CreatePlaceholderNode(typeInfo.Type, label ?? "value", null),
            };
        }
        finally
        {
            activeTypes.Remove(contractType);
        }
    }

    private DkonNode BuildObjectContractNode(JsonTypeInfo typeInfo, bool isRoot, HashSet<Type> activeTypes)
    {
        DkonNode? head = null;
        DkonNode? tail = null;
        var properties = typeInfo.Properties;

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];
            var description = TryGetContractDescription(property);
            var propertyLabel = HumanizeLabel(property.Name);
            var valueNode = this.BuildPropertyValueNode(property, propertyLabel, description, activeTypes);

            var keyNode = new DkonNode(property.Name)
            {
                Ref = valueNode,
            };

            Append(ref head, ref tail, keyNode);
        }

        return WrapContainerNode(head, isRoot);
    }

    private DkonNode BuildDictionaryContractNode(JsonTypeInfo typeInfo, bool isRoot, string? label, HashSet<Type> activeTypes)
    {
        var keyType = typeInfo.KeyType ?? typeof(object);

        if (!IsScalarCompatibleType(keyType))
            throw new InvalidOperationException($"DKON contract requires scalar-compatible dictionary keys. Type '{keyType}' is not supported.");

        var valueType = typeInfo.ElementType ?? typeof(object);

        var pair = new DkonNode(CreatePlaceholderValue(keyType, BuildCollectionLabel(label, "key"), null))
        {
            Bracing = DkonBracing.Inline,
            Ref = this.BuildContractNode(valueType, false, BuildCollectionLabel(label, "value"), activeTypes),
        };

        return WrapContainerNode(pair, isRoot);
    }

    private DkonNode BuildEnumerableContractNode(JsonTypeInfo typeInfo, bool isRoot, string? label, HashSet<Type> activeTypes)
    {
        var elementType = typeInfo.ElementType ?? typeof(object);
        var itemNode = this.BuildContractNode(elementType, false, BuildCollectionLabel(label, "item"), activeTypes);

        return WrapContainerNode(itemNode, isRoot);
    }

    private DkonNode BuildPropertyValueNode(JsonPropertyInfo property, string label, string? description, HashSet<Type> activeTypes)
    {
        if (!this.TryGetDkonTypeInfo(property.PropertyType, out var propertyTypeInfo))
            throw new ArgumentException($"The specified type {property.PropertyType} is not a known type.", nameof(property));

        return IsContainer(propertyTypeInfo)
            ? this.BuildContractNode(property.PropertyType, false, null, activeTypes)
            : CreatePlaceholderNode(property.PropertyType, label, description);
    }

    private static DkonNode WrapContainerNode(DkonNode? head, bool isRoot)
    {
        if (isRoot)
            return head ?? new DkonNode();

        return new DkonNode
        {
            Alt = head ?? new DkonNode(),
        };
    }

    private static DkonNode CreatePlaceholderNode(Type type, string label, string? description) =>
        WriteScalarAsNode(CreatePlaceholderValue(type, label, description));

    private static string CreatePlaceholderValue(Type type, string label, string? description)
    {
        var text = description ?? label;
        var scalarType = GetScalarType(type);
        var guidance = GetScalarTextGuidance(scalarType);

        return string.IsNullOrWhiteSpace(guidance) ? $"<string: {text}>" : $"<string: {guidance} {text}>";
    }

    private static string GetScalarTextGuidance(Type type)
    {
        var scalarType = GetScalarType(type);

        if (scalarType.IsEnum)
            return $"Use exactly one of {string.Join(" | ", Enum.GetNames(scalarType).Select(static x => $"'{x}'"))}.";

        if (scalarType == typeof(bool))
            return "Use exactly 'true' or 'false'.";

        if (scalarType == typeof(DateTime) || scalarType == typeof(DateTimeOffset))
            return "Use ISO-8601 date-time text.";

        if (scalarType == typeof(DateOnly))
            return "Use ISO-8601 date text.";

        if (scalarType == typeof(TimeOnly))
            return "Use ISO-8601 time text.";

        if (scalarType == typeof(TimeSpan))
            return "Use duration text.";

        if (scalarType == typeof(Guid))
            return "Use canonical GUID text.";

        return Type.GetTypeCode(scalarType) switch
        {
            TypeCode.Byte
                or TypeCode.SByte
                or TypeCode.Int16
                or TypeCode.UInt16
                or TypeCode.Int32
                or TypeCode.UInt32
                or TypeCode.Int64
                or TypeCode.UInt64 => "Use decimal digit text.",
            TypeCode.Single or TypeCode.Double or TypeCode.Decimal => "Use decimal text.",
            _ => string.Empty,
        };
    }

    private static bool IsContainer(JsonTypeInfo typeInfo) =>
        typeInfo.Kind == JsonTypeInfoKind.Object
        || typeInfo.Kind == JsonTypeInfoKind.Dictionary
        || (typeInfo.Kind == JsonTypeInfoKind.Enumerable && typeInfo.Type != typeof(string));

    private static bool IsScalarCompatibleType(Type type)
    {
        var scalarType = GetScalarType(type);

        if (scalarType.IsEnum)
            return true;

        if (scalarType == typeof(string)
            || scalarType == typeof(char)
            || scalarType == typeof(bool)
            || scalarType == typeof(DateTime)
            || scalarType == typeof(DateTimeOffset)
            || scalarType == typeof(DateOnly)
            || scalarType == typeof(TimeOnly)
            || scalarType == typeof(TimeSpan)
            || scalarType == typeof(Guid)
            || scalarType == typeof(Uri))
            return true;

        return Type.GetTypeCode(scalarType) is TypeCode.Byte
            or TypeCode.SByte
            or TypeCode.Int16
            or TypeCode.UInt16
            or TypeCode.Int32
            or TypeCode.UInt32
            or TypeCode.Int64
            or TypeCode.UInt64
            or TypeCode.Single
            or TypeCode.Double
            or TypeCode.Decimal;
    }

    private static string BuildCollectionLabel(string? label, string suffix) =>
        string.IsNullOrWhiteSpace(label) ? suffix : label + " " + suffix;

    private static string? TryGetContractDescription(JsonPropertyInfo property)
    {
        if (property.AttributeProvider is null)
            return null;

        var attributes = property.AttributeProvider.GetCustomAttributes(typeof(DescriptionAttribute), true);

        if (attributes.Length == 0 || attributes[0] is not DescriptionAttribute contract)
            return null;

        return string.IsNullOrWhiteSpace(contract.Description) ? null : contract.Description;
    }

    private static string HumanizeLabel(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "value";

        var builder = new StringBuilder(name.Length + 8);

        for (var i = 0; i < name.Length; i++)
        {
            var current = name[i];

            if (current == '_' || current == '-' || char.IsWhiteSpace(current))
            {
                AppendSeparator(builder);

                continue;
            }

            if (builder.Length > 0 && NeedsWordBoundary(name, i))
                AppendSeparator(builder);

            builder.Append(char.ToLowerInvariant(current));
        }

        return builder.Length == 0 ? "value" : builder.ToString();
    }

    private static bool NeedsWordBoundary(string text, int index)
    {
        var current = text[index];
        var previous = text[index - 1];
        var next = index + 1 < text.Length ? text[index + 1] : '\0';

        if (char.IsDigit(current))
            return !char.IsDigit(previous);

        if (char.IsDigit(previous))
            return !char.IsDigit(current);

        if (!char.IsUpper(current))
            return false;

        return char.IsLower(previous) || char.IsDigit(previous) || (char.IsUpper(previous) && next != '\0' && char.IsLower(next));
    }

    private static void AppendSeparator(StringBuilder builder)
    {
        if (builder.Length > 0 && builder[^1] != ' ')
            builder.Append(' ');
    }
}
