// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Itexoft.Formats.Dkon;

abstract partial class DkonFormat(JsonSerializerOptions? options = null) : JsonSerializerContext(options)
{
    public T Deserialize<T>(string dkon)
    {
        if (string.IsNullOrWhiteSpace(dkon))
            return default!;

        if (!this.TryGetDkonTypeInfo<T>(out var typeInfo))
            return default!;

        var root = DeserializeNodeOrEmpty(dkon);
        var value = this.ReadNodeAsValue(root, typeInfo.JsonTypeInfo, true);

        return value is null ? default! : (T)value;
    }

    public string Serialize(object obj, bool beautify = false) => this.Serialize(obj, obj.GetType(), beautify);

    public string Serialize(object obj, Type type, bool beautify = false)
    {
        if (obj is null)
            return string.Empty;

        var node = this.WriteValueAsNode(obj, type, true) ?? new DkonNode();

        if (beautify)
            DkonFormatters.Beautify(node);

        return SerializeNodeOrEmpty(node);
    }

    public string Serialize<T>(T obj, bool beautify = false)
    {
        if (obj is null)
            return string.Empty;

        var node = this.WriteValueAsNode(obj, typeof(T), true) ?? new DkonNode();

        if (beautify)
            DkonFormatters.Beautify(node);

        return SerializeNodeOrEmpty(node);
    }

    private object? ReadNodeAsValue(DkonNode node, JsonTypeInfo typeInfo, bool isRoot)
    {
        switch (typeInfo.Kind)
        {
            case JsonTypeInfoKind.Object:
                return this.ReadObjectAsValue(node, typeInfo, isRoot);
            case JsonTypeInfoKind.Dictionary:
                return this.ReadDictionaryAsValue(node, typeInfo, isRoot);
            case JsonTypeInfoKind.Enumerable:
                return this.ReadEnumerableAsValue(node, typeInfo, isRoot);
            default:
                return this.ReadScalarAsValue(node, typeInfo, isRoot);
        }
    }

    private object? ReadObjectAsValue(DkonNode node, JsonTypeInfo typeInfo, bool isRoot)
    {
        var instance = CreateObjectInstance(typeInfo.CreateObject);

        if (instance is null)
            return DefaultForType(typeInfo.Type);

        var first = GetObjectListHead(node, isRoot);

        for (var current = first; current is not null; current = current.Next)
        {
            if (current.Ref is null || current.Alt is not null || string.IsNullOrWhiteSpace(current.Value))
                continue;

            if (!TryFindProperty(typeInfo, current.Value, out var property))
                continue;

            if (property.Set is null)
                continue;

            object? value;

            if (this.TryGetDkonTypeInfo(property.PropertyType, out var valueTypeInfo))
                value = this.ReadNodeAsValue(current.Ref, valueTypeInfo, false);
            else
                value = this.ReadTextAsType(current.Ref.Value, property.PropertyType);

            value = CoerceToTargetOrDefault(value, property.PropertyType);
            property.Set(instance, value);
        }

        return instance;
    }

    private object? ReadDictionaryAsValue(DkonNode node, JsonTypeInfo typeInfo, bool isRoot)
    {
        var keyType = typeInfo.KeyType ?? typeof(object);
        var valueType = typeInfo.ElementType ?? typeof(object);
        var instance = CreateDictionaryInstance(typeInfo.CreateObject);

        if (instance is null)
            return DefaultForType(typeInfo.Type);

        var first = GetObjectListHead(node, isRoot);
        var hasTypedValueInfo = this.TryGetDkonTypeInfo(valueType, out var valueTypeInfo);

        for (var current = first; current is not null; current = current.Next)
        {
            if (current.Ref is null || current.Alt is not null)
                continue;

            var key = this.ReadTextAsType(current.Value, keyType);
            object? value;

            if (hasTypedValueInfo)
                value = this.ReadNodeAsValue(current.Ref, valueTypeInfo, false);
            else if (TryReadScalarNode(current.Ref, out var raw))
                value = this.ReadTextAsType(raw, valueType);
            else
                value = DefaultForType(valueType);

            value = CoerceToTargetOrDefault(value, valueType);
            key = CoerceToTargetOrDefault(key, keyType);

            SetDictionaryValue(instance, key, value);
        }

        return instance;
    }

    private object? ReadEnumerableAsValue(DkonNode node, JsonTypeInfo typeInfo, bool isRoot)
    {
        var elementType = typeInfo.ElementType ?? typeof(object);
        var first = GetArrayListHead(node, isRoot);
        var hasTypedElementInfo = this.TryGetDkonTypeInfo(elementType, out var elementTypeInfo);

        if (typeInfo.Type.IsArray)
            return this.ReadArrayAsValue(first, elementType, hasTypedElementInfo, elementTypeInfo);

        var collection = CreateCollectionInstance(typeInfo.CreateObject);

        if (collection is null)
            return DefaultForType(typeInfo.Type);

        for (var current = first; current is not null; current = current.Next)
        {
            if (current.Ref is not null && !current.Ref.IsEmpty)
                continue;

            object? value;

            if (hasTypedElementInfo)
                value = this.ReadNodeAsValue(current, elementTypeInfo, false);
            else if (TryReadScalarNode(current, out var raw))
                value = this.ReadTextAsType(raw, elementType);
            else
                value = DefaultForType(elementType);

            value = CoerceToTargetOrDefault(value, elementType);
            AddCollectionItem(collection, value);
        }

        return collection;
    }

    private object? ReadArrayAsValue(DkonNode? first, Type elementType, bool hasTypedElementInfo, JsonTypeInfo? elementTypeInfo)
    {
        if (elementType == typeof(int))
            return this.ReadScalarArray<int>(first);

        if (elementType == typeof(long))
            return this.ReadScalarArray<long>(first);

        if (elementType == typeof(uint))
            return this.ReadScalarArray<uint>(first);

        if (elementType == typeof(ulong))
            return this.ReadScalarArray<ulong>(first);

        if (elementType == typeof(short))
            return this.ReadScalarArray<short>(first);

        if (elementType == typeof(ushort))
            return this.ReadScalarArray<ushort>(first);

        if (elementType == typeof(byte))
            return this.ReadScalarArray<byte>(first);

        if (elementType == typeof(sbyte))
            return this.ReadScalarArray<sbyte>(first);

        if (elementType == typeof(float))
            return this.ReadScalarArray<float>(first);

        if (elementType == typeof(double))
            return this.ReadScalarArray<double>(first);

        if (elementType == typeof(decimal))
            return this.ReadScalarArray<decimal>(first);

        if (elementType == typeof(bool))
            return this.ReadScalarArray<bool>(first);

        if (elementType == typeof(string))
            return this.ReadScalarArray<string>(first);

        return this.ReadEnumerableAsValue(first!, elementTypeInfo!, hasTypedElementInfo);
    }

    private TItem[] ReadScalarArray<TItem>(DkonNode? first)
    {
        var items = new List<TItem>(8);

        for (var current = first; current is not null; current = current.Next)
        {
            if (!TryReadScalarNode(current, out var raw))
                continue;

            var parsed = this.ReadTextAsType(raw, typeof(TItem));

            if (parsed is TItem item)
                items.Add(item);
            else
                items.Add(default!);
        }

        return [..items];
    }

    private object? ReadScalarAsValue(DkonNode node, JsonTypeInfo typeInfo, bool isRoot)
    {
        if (node.Alt is not null || node.Ref is not null || (isRoot && node.Next is not null))
            return DefaultForType(typeInfo.Type);

        return this.ReadTextAsType(node.Value, typeInfo.Type);
    }

    private object? ReadTextAsType(string raw, Type targetType)
    {
        if (IsNullLike(raw))
            return DefaultForType(targetType);

        var scalarType = GetScalarType(targetType);

        if (scalarType == typeof(string) || scalarType == typeof(object))
            return raw;

        if (scalarType == typeof(bool))
        {
            if (TryParseBoolean(raw.AsSpan(), out var boolValue))
                return boolValue;

            return DefaultForType(targetType);
        }

        if (TryReadNumericScalar(raw, scalarType, out var number, out var numericParseFailed))
        {
            if (numericParseFailed)
                throw new FormatException($"Cannot convert DKON numeric value '{raw}' to '{targetType}'.");

            return CoerceToTargetOrDefault(number, targetType);
        }

        if (scalarType.IsEnum)
        {
            if (Enum.TryParse(scalarType, raw, true, out var enumValue))
                return enumValue;

            return DefaultForType(targetType);
        }

        if (TryReadSpecialScalar(raw, scalarType, out var special))
            return CoerceToTargetOrDefault(special, targetType);

        return DefaultForType(targetType);
    }

    private static bool TryReadSpecialScalar(string value, Type scalarType, out object? parsed)
    {
        if (scalarType == typeof(Guid))
        {
            if (Guid.TryParse(value.AsSpan().Trim(), out var guid))
            {
                parsed = guid;

                return true;
            }

            parsed = null;

            return false;
        }

        if (scalarType == typeof(DateTime))
        {
            if (TryParseDateTime(value, out var dateTime))
            {
                parsed = dateTime;

                return true;
            }

            parsed = null;

            return false;
        }

        if (scalarType == typeof(DateTimeOffset))
        {
            if (TryParseDateTimeOffset(value, out var dateTimeOffset))
            {
                parsed = dateTimeOffset;

                return true;
            }

            parsed = null;

            return false;
        }

        if (scalarType == typeof(DateOnly))
        {
            if (DateOnly.TryParse(value.AsSpan().Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateOnly)
                || DateOnly.TryParse(value.AsSpan().Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out dateOnly))
            {
                parsed = dateOnly;

                return true;
            }

            parsed = null;

            return false;
        }

        if (scalarType == typeof(TimeOnly))
        {
            if (TimeOnly.TryParse(value.AsSpan().Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var timeOnly)
                || TimeOnly.TryParse(value.AsSpan().Trim(), CultureInfo.CurrentCulture, DateTimeStyles.None, out timeOnly))
            {
                parsed = timeOnly;

                return true;
            }

            parsed = null;

            return false;
        }

        if (scalarType == typeof(TimeSpan))
        {
            if (TimeSpan.TryParse(value.AsSpan().Trim(), CultureInfo.InvariantCulture, out var timeSpan)
                || TimeSpan.TryParse(value.AsSpan().Trim(), CultureInfo.CurrentCulture, out timeSpan))
            {
                parsed = timeSpan;

                return true;
            }

            parsed = null;

            return false;
        }

        if (scalarType == typeof(char))
        {
            var span = value.AsSpan();

            if (span.Length == 1)
            {
                parsed = span[0];

                return true;
            }

            parsed = null;

            return false;
        }

        if (scalarType == typeof(Uri))
        {
            if (Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out var uri))
            {
                parsed = uri;

                return true;
            }
        }

        parsed = null;

        return false;
    }

    private static bool TryReadNumericScalar(string value, Type scalarType, out object? parsed, out bool parseFailed)
    {
        parsed = null;
        parseFailed = false;

        switch (Type.GetTypeCode(scalarType))
        {
            case TypeCode.Byte:
                if (TryParseSignedInteger(value, out var i8) && i8 >= byte.MinValue && i8 <= byte.MaxValue)
                {
                    parsed = (byte)i8;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.SByte:
                if (TryParseSignedInteger(value, out var s8) && s8 >= sbyte.MinValue && s8 <= sbyte.MaxValue)
                {
                    parsed = (sbyte)s8;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.Int16:
                if (TryParseSignedInteger(value, out var i16) && i16 >= short.MinValue && i16 <= short.MaxValue)
                {
                    parsed = (short)i16;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.UInt16:
                if (TryParseUnsignedInteger(value, out var u16) && u16 <= ushort.MaxValue)
                {
                    parsed = (ushort)u16;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.Int32:
                if (TryParseSignedInteger(value, out var i32) && i32 >= int.MinValue && i32 <= int.MaxValue)
                {
                    parsed = (int)i32;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.UInt32:
                if (TryParseUnsignedInteger(value, out var u32) && u32 <= uint.MaxValue)
                {
                    parsed = (uint)u32;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.Int64:
                if (TryParseSignedInteger(value, out var i64))
                {
                    parsed = i64;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.UInt64:
                if (TryParseUnsignedInteger(value, out var u64))
                {
                    parsed = u64;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.Single:
                if (TryParseSingle(value, out var f32) && float.IsFinite(f32))
                {
                    parsed = f32;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.Double:
                if (TryParseDouble(value, out var f64) && double.IsFinite(f64))
                {
                    parsed = f64;

                    return true;
                }

                parseFailed = true;

                return true;

            case TypeCode.Decimal:
                if (TryParseDecimal(value, out var dec))
                {
                    parsed = dec;

                    return true;
                }

                parseFailed = true;

                return true;

            default:
                return false;
        }
    }

    private DkonNode? WriteValueAsNode<T>(T value, Type declaredType, bool isRoot)
    {
        if (value is null)
            return isRoot ? new DkonNode() : null;

        var runtimeType = typeof(T);

        if (!runtimeType.IsValueType)
            runtimeType = value.GetType();

        if (!this.TryGetDkonTypeInfo(runtimeType, out var typeInfo) && !this.TryGetDkonTypeInfo(declaredType, out typeInfo))
            throw new ArgumentException($"The specified type {runtimeType} is not a known type.");

        switch (typeInfo.Kind)
        {
            case JsonTypeInfoKind.Object:
                return this.WriteObjectAsNode(value, typeInfo, isRoot);
            case JsonTypeInfoKind.Dictionary:
                return this.WriteDictionaryAsNode(value, typeInfo, isRoot);
            case JsonTypeInfoKind.Enumerable:
                if (value is string)
                    return WriteScalarAsNode(value);

                return this.WriteEnumerableAsNode(value, typeInfo, isRoot);
            default:
                return WriteScalarAsNode(value);
        }
    }

    private DkonNode? WriteObjectAsNode<T>(T value, JsonTypeInfo typeInfo, bool isRoot)
    {
        DkonNode? head = null;
        DkonNode? tail = null;
        var properties = typeInfo.Properties;

        for (var i = 0; i < properties.Count; i++)
        {
            var property = properties[i];

            if (property.Get is null)
                continue;

            var propertyValue = property.Get(value!);

            if (propertyValue is null)
                continue;

            var valueNode = this.WriteValueAsNode(propertyValue, property.PropertyType, false);

            if (valueNode is null)
                continue;

            var keyNode = new DkonNode(property.Name)
            {
                Ref = valueNode,
            };

            Append(ref head, ref tail, keyNode);
        }

        if (isRoot)
            return head ?? new DkonNode();

        if (head is null)
            return null;

        return new DkonNode { Alt = head };
    }

    private DkonNode? WriteDictionaryAsNode<T>(T value, JsonTypeInfo typeInfo, bool isRoot)
    {
        DkonNode? head = null;
        DkonNode? tail = null;
        var elementType = typeInfo.ElementType ?? typeof(object);

        if (value is IDictionary dictionary)
        {
            foreach (DictionaryEntry entry in dictionary)
                AppendDictionaryItem(ref head, ref tail, entry.Key, entry.Value, elementType, this);
        }
        else if (value is IEnumerable enumerable)
        {
            foreach (var item in enumerable)
            {
                if (!TryReadDictionaryPair(item, out var key, out var itemValue))
                    continue;

                AppendDictionaryItem(ref head, ref tail, key, itemValue, elementType, this);
            }
        }

        if (isRoot)
            return head ?? new DkonNode();

        if (head is null)
            return null;

        return new DkonNode { Alt = head };
    }

    private DkonNode? WriteEnumerableAsNode<T>(T value, JsonTypeInfo typeInfo, bool isRoot)
    {
        if (value is int[] valuesInt32)
            return this.WriteScalarEnumerableAsNode(valuesInt32, isRoot);

        if (value is long[] valuesInt64)
            return this.WriteScalarEnumerableAsNode(valuesInt64, isRoot);

        if (value is uint[] valuesUInt32)
            return this.WriteScalarEnumerableAsNode(valuesUInt32, isRoot);

        if (value is ulong[] valuesUInt64)
            return this.WriteScalarEnumerableAsNode(valuesUInt64, isRoot);

        if (value is short[] valuesInt16)
            return this.WriteScalarEnumerableAsNode(valuesInt16, isRoot);

        if (value is ushort[] valuesUInt16)
            return this.WriteScalarEnumerableAsNode(valuesUInt16, isRoot);

        if (value is byte[] valuesByte)
            return this.WriteScalarEnumerableAsNode(valuesByte, isRoot);

        if (value is sbyte[] valuesSByte)
            return this.WriteScalarEnumerableAsNode(valuesSByte, isRoot);

        if (value is float[] valuesSingle)
            return this.WriteScalarEnumerableAsNode(valuesSingle, isRoot);

        if (value is double[] valuesDouble)
            return this.WriteScalarEnumerableAsNode(valuesDouble, isRoot);

        if (value is decimal[] valuesDecimal)
            return this.WriteScalarEnumerableAsNode(valuesDecimal, isRoot);

        if (value is bool[] valuesBoolean)
            return this.WriteScalarEnumerableAsNode(valuesBoolean, isRoot);

        if (value is string[] valuesString)
            return this.WriteScalarEnumerableAsNode(valuesString, isRoot);

        if (value is not IEnumerable enumerable)
            return WriteScalarAsNode(value);

        DkonNode? head = null;
        DkonNode? tail = null;
        var elementType = typeInfo.ElementType ?? typeof(object);

        foreach (var item in enumerable)
        {
            var itemNode = this.WriteValueAsNode(item, elementType, false);

            if (itemNode is null)
                continue;

            Append(ref head, ref tail, itemNode);
        }

        if (isRoot)
            return head ?? new DkonNode();

        if (head is null)
            return null;

        return new DkonNode { Alt = head };
    }

    private DkonNode? WriteScalarEnumerableAsNode<TItem>(TItem[] values, bool isRoot)
    {
        DkonNode? head = null;
        DkonNode? tail = null;

        for (var i = 0; i < values.Length; i++)
        {
            var itemNode = WriteScalarAsNode(values[i]);
            Append(ref head, ref tail, itemNode);
        }

        if (isRoot)
            return head ?? new DkonNode();

        if (head is null)
            return null;

        return new DkonNode { Alt = head };
    }

    private static DkonNode WriteScalarAsNode<T>(T value)
    {
        var text = ScalarToText(value);

        var node = new DkonNode(text)
        {
            Bracing = DkonBracing.Inline,
        };

        return node;
    }

    private static string ScalarToText<T>(T value)
    {
        if (value is null)
            return string.Empty;

        if (value is string text)
            return text;

        if (value is bool b)
            return b ? "true" : "false";

        if (value is DateTime dt)
            return dt.ToString("O", CultureInfo.InvariantCulture);

        if (value is DateTimeOffset dto)
            return dto.ToString("O", CultureInfo.InvariantCulture);

        if (value is DateOnly dateOnly)
            return dateOnly.ToString("O", CultureInfo.InvariantCulture);

        if (value is TimeOnly timeOnly)
            return timeOnly.ToString("O", CultureInfo.InvariantCulture);

        if (value is TimeSpan ts)
            return ts.ToString("c", CultureInfo.InvariantCulture);

        if (value is Guid guid)
            return guid.ToString("D", CultureInfo.InvariantCulture);

        if (value is float f)
            return f.ToString("R", CultureInfo.InvariantCulture);

        if (value is double d)
            return d.ToString("R", CultureInfo.InvariantCulture);

        if (value is decimal dec)
            return dec.ToString(CultureInfo.InvariantCulture);

        if (value is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty;

        return value.ToString() ?? string.Empty;
    }

    private static void AppendDictionaryItem(ref DkonNode? head, ref DkonNode? tail, object? key, object? value, Type elementType, DkonFormat context)
    {
        var keyText = KeyToText(key);

        if (string.IsNullOrEmpty(keyText))
            return;

        var valueNode = context.WriteValueAsNode(value, elementType, false);

        if (valueNode is null)
            return;

        var keyNode = new DkonNode(keyText)
        {
            Ref = valueNode,
        };

        Append(ref head, ref tail, keyNode);
    }

    private static bool TryReadDictionaryPair(object? pair, out object? key, out object? value)
    {
        if (pair is null)
        {
            key = null;
            value = null;

            return false;
        }

        if (pair is DictionaryEntry entry)
        {
            key = entry.Key;
            value = entry.Value;

            return true;
        }

        key = null;
        value = null;

        return false;
    }

    private static string? KeyToText(object? key)
    {
        if (key is null)
            return null;

        if (key is string text)
            return text;

        if (key is IFormattable formattable)
            return formattable.ToString(null, CultureInfo.InvariantCulture);

        return key.ToString();
    }

    private static T CreateObjectInstance<T>(Func<T>? factory)
    {
        if (factory is null)
            return default!;

        return factory();
    }

    private static IDictionary? CreateDictionaryInstance(Func<object>? factory)
    {
        var instance = CreateObjectInstance(factory);

        return instance as IDictionary;
    }

    private static IList? CreateCollectionInstance(Func<object>? factory)
    {
        var instance = CreateObjectInstance(factory);

        return instance as IList;
    }

    private static void AddCollectionItem(IList collection, object? value) => collection.Add(value);

    private static void SetDictionaryValue(IDictionary dictionary, object? key, object? value)
    {
        if (key is null)
            return;

        dictionary[key] = value;
    }

    private static object? CoerceToTargetOrDefault(object? value, Type targetType)
    {
        if (value is null)
            return DefaultForType(targetType);

        if (targetType == typeof(object) || targetType.IsInstanceOfType(value))
            return value;

        var nullable = Nullable.GetUnderlyingType(targetType);

        if (nullable is not null && nullable.IsInstanceOfType(value))
            return value;

        return DefaultForType(targetType);
    }

    private static object? DefaultForType(Type type)
    {
        if (!type.IsValueType)
            return null;

        if (Nullable.GetUnderlyingType(type) is not null)
            return null;

        if (type == typeof(bool))
            return false;

        if (type == typeof(byte))
            return (byte)0;

        if (type == typeof(sbyte))
            return (sbyte)0;

        if (type == typeof(short))
            return (short)0;

        if (type == typeof(ushort))
            return (ushort)0;

        if (type == typeof(int))
            return 0;

        if (type == typeof(uint))
            return 0U;

        if (type == typeof(long))
            return 0L;

        if (type == typeof(ulong))
            return 0UL;

        if (type == typeof(float))
            return 0F;

        if (type == typeof(double))
            return 0D;

        if (type == typeof(decimal))
            return 0M;

        if (type == typeof(char))
            return '\0';

        if (type == typeof(DateTime))
            return default(DateTime);

        if (type == typeof(DateTimeOffset))
            return default(DateTimeOffset);

        if (type == typeof(TimeSpan))
            return default(TimeSpan);

        if (type == typeof(DateOnly))
            return default(DateOnly);

        if (type == typeof(TimeOnly))
            return default(TimeOnly);

        if (type == typeof(Guid))
            return default(Guid);

        return RuntimeHelpers.GetUninitializedObject(type);
    }

    private static DkonNode? GetObjectListHead(DkonNode node, bool isRoot)
    {
        if (isRoot)
        {
            if (node.Alt is not null && node.Ref is null && node.Next is null && node.IsEmptyValue)
                return node.Alt;

            return node.IsEmpty ? null : node;
        }

        return node.Alt;
    }

    private static DkonNode? GetArrayListHead(DkonNode node, bool isRoot)
    {
        if (isRoot)
        {
            if (node.Alt is not null && node.Ref is null && node.Next is null && node.IsEmptyValue)
                return node.Alt;

            return node.IsEmpty ? null : node;
        }

        return node.Alt;
    }

    private static bool TryReadScalarNode(DkonNode node, out string raw)
    {
        if (node.Ref is null && node.Alt is null)
        {
            raw = node.Value;

            return true;
        }

        raw = string.Empty;

        return false;
    }

    private static bool TryFindProperty(JsonTypeInfo typeInfo, string fieldName, out JsonPropertyInfo property)
    {
        var properties = typeInfo.Properties;

        for (var i = 0; i < properties.Count; i++)
        {
            var current = properties[i];

            if (!string.Equals(current.Name, fieldName, StringComparison.Ordinal))
                continue;

            property = current;

            return true;
        }

        property = null!;

        return false;
    }

    private bool TryGetDkonTypeInfo(Type type, out JsonTypeInfo typeInfo)
    {
        var raw = this.GetTypeInfo(type);

        if (raw is null)
        {
            typeInfo = null!;

            return false;
        }

        typeInfo = raw;

        return true;
    }

    private bool TryGetDkonTypeInfo<T>(out DkonTypeInfo<T> typeInfo)
    {
        if (this.GetTypeInfo(typeof(T)) is not JsonTypeInfo<T> raw)
        {
            typeInfo = default;

            return false;
        }

        typeInfo = new DkonTypeInfo<T>(raw);

        return true;
    }

    private static bool IsNullLike(string value)
    {
        var span = value.AsSpan().Trim();

        return span.Length == 0 || span.Equals("null".AsSpan(), StringComparison.OrdinalIgnoreCase);
    }

    private static Type GetScalarType(Type type) => Nullable.GetUnderlyingType(type) ?? type;

    private static bool TryParseSignedInteger(string value, out long parsed)
    {
        var span = value.AsSpan().Trim();

        if (span.Length == 0)
        {
            parsed = 0;

            return false;
        }

        var normalized = NormalizeNumber(span, false);

        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            return true;

        if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
            return true;

        if (TryParseSignedHex(normalized, out parsed))
            return true;

        if (decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDecimal)
            && asDecimal == decimal.Truncate(asDecimal)
            && asDecimal >= long.MinValue
            && asDecimal <= long.MaxValue)
        {
            parsed = (long)asDecimal;

            return true;
        }

        parsed = 0;

        return false;
    }

    private static bool TryParseUnsignedInteger(string value, out ulong parsed)
    {
        var span = value.AsSpan().Trim();

        if (span.Length == 0)
        {
            parsed = 0;

            return false;
        }

        var normalized = NormalizeNumber(span, false);

        if (normalized.Length > 0 && normalized[0] == '+')
            normalized = normalized[1..];

        if (ulong.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            return true;

        if (ulong.TryParse(normalized, NumberStyles.Integer, CultureInfo.CurrentCulture, out parsed))
            return true;

        if (TryParseUnsignedHex(normalized, out parsed))
            return true;

        if (decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var asDecimal)
            && asDecimal == decimal.Truncate(asDecimal)
            && asDecimal >= 0
            && asDecimal <= ulong.MaxValue)
        {
            parsed = (ulong)asDecimal;

            return true;
        }

        parsed = 0;

        return false;
    }

    private static bool TryParseSingle(string value, out float parsed)
    {
        var span = value.AsSpan().Trim();

        if (span.Length == 0)
        {
            parsed = 0;

            return false;
        }

        if (float.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
            return true;

        if (float.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed))
            return true;

        var normalized = NormalizeNumber(span, true);

        return float.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed)
               || float.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed);
    }

    private static bool TryParseDouble(string value, out double parsed)
    {
        var span = value.AsSpan().Trim();

        if (span.Length == 0)
        {
            parsed = 0;

            return false;
        }

        if (double.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
            return true;

        if (double.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed))
            return true;

        var normalized = NormalizeNumber(span, true);

        return double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed)
               || double.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed);
    }

    private static bool TryParseDecimal(string value, out decimal parsed)
    {
        var span = value.AsSpan().Trim();

        if (span.Length == 0)
        {
            parsed = 0;

            return false;
        }

        if (decimal.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed))
            return true;

        if (decimal.TryParse(span, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed))
            return true;

        var normalized = NormalizeNumber(span, true);

        return decimal.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out parsed)
               || decimal.TryParse(normalized, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.CurrentCulture, out parsed);
    }

    private static bool TryParseBoolean(ReadOnlySpan<char> value, out bool parsed)
    {
        var span = value.Trim();

        if (span.Equals("true".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("1".AsSpan(), StringComparison.Ordinal)
            || span.Equals("yes".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("on".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("y".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("t".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parsed = true;

            return true;
        }

        if (span.Equals("false".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("0".AsSpan(), StringComparison.Ordinal)
            || span.Equals("no".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("off".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("n".AsSpan(), StringComparison.OrdinalIgnoreCase)
            || span.Equals("f".AsSpan(), StringComparison.OrdinalIgnoreCase))
        {
            parsed = false;

            return true;
        }

        parsed = false;

        return false;
    }

    private static bool TryParseDateTime(string value, out DateTime parsed)
    {
        var span = value.AsSpan().Trim();

        return DateTime.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)
               || DateTime.TryParse(span, CultureInfo.CurrentCulture, DateTimeStyles.RoundtripKind, out parsed);
    }

    private static bool TryParseDateTimeOffset(string value, out DateTimeOffset parsed)
    {
        var span = value.AsSpan().Trim();

        return DateTimeOffset.TryParse(span, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out parsed)
               || DateTimeOffset.TryParse(span, CultureInfo.CurrentCulture, DateTimeStyles.RoundtripKind, out parsed);
    }

    private static string NormalizeNumber(ReadOnlySpan<char> value, bool allowDecimal)
    {
        var span = value.Trim();

        if (span.Length == 0)
            return string.Empty;

        var stack = span.Length <= 256 ? stackalloc char[span.Length] : default;
        var chars = span.Length <= 256 ? stack : new char[span.Length];
        var p = 0;
        var hasDot = false;
        var hasComma = false;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];

            if (IsIgnoredNumericChar(c))
                continue;

            if (c == ',')
            {
                hasComma = true;
                chars[p++] = c;

                continue;
            }

            if (c == '.')
                hasDot = true;

            chars[p++] = c;
        }

        if (!allowDecimal)
        {
            var w = 0;

            for (var i = 0; i < p; i++)
            {
                var c = chars[i];

                if (c == ',' || c == '.')
                    continue;

                chars[w++] = c;
            }

            p = w;

            return new string(chars[..p]);
        }

        if (hasComma && !hasDot)
        {
            for (var i = 0; i < p; i++)
            {
                if (chars[i] == ',')
                    chars[i] = '.';
            }

            return new string(chars[..p]);
        }

        if (hasComma && hasDot)
        {
            var w = 0;

            for (var i = 0; i < p; i++)
            {
                if (chars[i] == ',')
                    continue;

                chars[w++] = chars[i];
            }

            p = w;
        }

        return new string(chars[..p]);
    }

    private static bool IsIgnoredNumericChar(char c) => c is '_' or '\'' or ' ' or '\t' or '\r' or '\n' or '\u00A0' or '\u202F';

    private static bool TryParseSignedHex(ReadOnlySpan<char> value, out long parsed)
    {
        var sign = 1;
        var span = value;

        if (span.Length == 0)
        {
            parsed = 0;

            return false;
        }

        if (span[0] == '+')
            span = span[1..];
        else if (span[0] == '-')
        {
            sign = -1;
            span = span[1..];
        }

        if (!TryParseUnsignedHex(span, out var unsigned))
        {
            parsed = 0;

            return false;
        }

        if (sign > 0)
        {
            if (unsigned > long.MaxValue)
            {
                parsed = 0;

                return false;
            }

            parsed = (long)unsigned;

            return true;
        }

        if (unsigned > (ulong)long.MaxValue + 1)
        {
            parsed = 0;

            return false;
        }

        parsed = unsigned == (ulong)long.MaxValue + 1 ? long.MinValue : -(long)unsigned;

        return true;
    }

    private static bool TryParseUnsignedHex(ReadOnlySpan<char> value, out ulong parsed)
    {
        var span = value;

        if (span.StartsWith("0x".AsSpan(), StringComparison.OrdinalIgnoreCase))
            span = span[2..];

        if (span.Length == 0)
        {
            parsed = 0;

            return false;
        }

        var result = 0UL;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            int digit;

            if (c is >= '0' and <= '9')
                digit = c - '0';
            else if (c is >= 'a' and <= 'f')
                digit = c - 'a' + 10;
            else if (c is >= 'A' and <= 'F')
                digit = c - 'A' + 10;
            else
            {
                parsed = 0;

                return false;
            }

            if (result > (ulong.MaxValue - (ulong)digit) / 16UL)
            {
                parsed = 0;

                return false;
            }

            result = result * 16UL + (ulong)digit;
        }

        parsed = result;

        return true;
    }

    private static void Append(ref DkonNode? head, ref DkonNode? tail, DkonNode node)
    {
        if (head is null)
        {
            head = node;
            tail = node;

            return;
        }

        tail!.Next = node;
        tail = node;
    }

    private readonly struct DkonTypeInfo<T>(JsonTypeInfo<T> jsonTypeInfo)
    {
        public JsonTypeInfo<T> JsonTypeInfo { get; } = jsonTypeInfo;
    }
}
