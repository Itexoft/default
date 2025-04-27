// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Itexoft.Formats.Json;

public static class JsonExtensions
{
    extension(JsonSerializerContext context)
    {
        public string Serialize<T>(T value, bool skipPrimitive)
        {
            var type = value?.GetType() ?? typeof(T);

            if (skipPrimitive && (type == typeof(string) || type.IsPrimitive))
                return value?.ToString() ?? string.Empty;

            if (context.GetTypeInfo(type) is not JsonTypeInfo typeInfo)
                throw new InvalidOperationException();

            return JsonSerializer.Serialize(value, typeInfo);
        }

        public byte[] SerializeToUtf8Bytes<T>(T value)
        {
            if (context.GetTypeInfo(value?.GetType() ?? typeof(T)) is not JsonTypeInfo typeInfo)
                throw new InvalidOperationException();

            return JsonSerializer.SerializeToUtf8Bytes(value, typeInfo);
        }

        public T? Deserialize<T>(string value)
        {
            if (context.GetTypeInfo(typeof(T)) is not JsonTypeInfo<T> typeInfo)
                throw new InvalidOperationException(value);

            return JsonSerializer.Deserialize(value, typeInfo);
        }

        public T? Deserialize<T>(ReadOnlySpan<byte> utf8Json)
        {
            if (context.GetTypeInfo(typeof(T)) is not JsonTypeInfo<T> typeInfo)
                throw new InvalidOperationException(nameof(utf8Json));

            return JsonSerializer.Deserialize(utf8Json, typeInfo);
        }

        public object? Deserialize(string value, Type type)
        {
            if (context.GetTypeInfo(type) is not JsonTypeInfo typeInfo)
                throw new InvalidOperationException(value);

            return JsonSerializer.Deserialize(value, typeInfo);
        }

        public string GenerateSchema(Type type)
        {
            if (context.GetTypeInfo(type) is not JsonTypeInfo typeInfo)
                throw new InvalidOperationException(type.Name);

            return JsonSchemaGenerator.GenerateSchemaElement(typeInfo);
        }

        public string GenerateSchema(Type type, JsonSchemaExporterOptions options)
        {
            if (context.GetTypeInfo(type) is not JsonTypeInfo typeInfo)
                throw new InvalidOperationException(type.Name);

            return JsonSchemaGenerator.GenerateSchemaElement(typeInfo, options);
        }

        public string GetPropertyName(Type type, string clrName)
        {
            if (context.GetTypeInfo(type) is not JsonTypeInfo typeInfo)
                throw new InvalidOperationException(type.Name);

            return typeInfo.Properties.FirstOrDefault(p => p.AttributeProvider is MemberInfo m && m.Name == clrName)?.Name
                   ?? typeInfo.Options.PropertyNamingPolicy?.ConvertName(clrName) ?? clrName;
        }
    }
}
