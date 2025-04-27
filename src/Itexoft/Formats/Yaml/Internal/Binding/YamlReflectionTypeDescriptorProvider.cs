// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Itexoft.Formats.Yaml.Internal.Binding;

internal sealed class ReflectionMetadataAcl : IMetadataAcl
{
    public bool IsTypeBindable(Type type) =>
        !type.IsPointer && !type.IsByRef && !type.ContainsGenericParameters && type != typeof(nint) && type != typeof(nuint);

    public bool IsConstructorBindable(ConstructorInfo constructor) =>
        constructor.IsPublic && !constructor.IsStatic;

    public bool IsPropertyBindable(PropertyInfo property) =>
        property.GetIndexParameters().Length == 0
        && ((property.GetMethod?.IsPublic ?? false) || (property.SetMethod?.IsPublic ?? false))
        && !(property.GetMethod?.IsStatic ?? property.SetMethod?.IsStatic ?? false);

    public string GetCanonicalMemberLabel(PropertyInfo property) =>
        property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;

    public bool IsRequired(PropertyInfo property) =>
        property.GetCustomAttribute<RequiredMemberAttribute>() is not null;

    public bool IsExtensionData(PropertyInfo property) =>
        property.GetCustomAttribute<JsonExtensionDataAttribute>() is not null;
}

internal sealed class ReflectionTypeDescriptorProvider(IMetadataAcl acl) : ITypeDescriptorProvider
{
    public TypeDescriptor GetDescriptor(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type);
        var effectiveType = underlying ?? type;

        if (!acl.IsTypeBindable(effectiveType))
            throw new InvalidOperationException($"Type '{effectiveType}' is not bindable by YAML reflection ACL.");

        if (effectiveType.IsArray)
        {
            return new(
                type,
                effectiveType,
                YamlTypeKind.Array,
                ConstructionRoute.ShellFill,
                !type.IsValueType,
                effectiveType.GetElementType()!,
                null,
                null,
                [],
                null,
                null,
                null);
        }

        if (TryGetListType(effectiveType, out var elementType))
        {
            return new(
                type,
                GetListImplementationType(effectiveType, elementType),
                YamlTypeKind.List,
                ConstructionRoute.ShellFill,
                !type.IsValueType,
                elementType,
                null,
                null,
                [],
                null,
                null,
                new ReflectionShellActivator(GetListImplementationType(effectiveType, elementType)));
        }

        if (TryGetDictionaryType(effectiveType, out var keyType, out var valueType))
        {
            return new(
                type,
                GetDictionaryImplementationType(effectiveType, keyType, valueType),
                YamlTypeKind.Dictionary,
                ConstructionRoute.ShellFill,
                !type.IsValueType,
                null,
                keyType,
                valueType,
                [],
                null,
                null,
                new ReflectionShellActivator(GetDictionaryImplementationType(effectiveType, keyType, valueType)));
        }

        if (IsScalarType(effectiveType))
            return new(type, effectiveType, YamlTypeKind.Scalar, ConstructionRoute.Scalar, !type.IsValueType, null, null, null, [], null, null, null);

        if (effectiveType.IsInterface || effectiveType.IsAbstract)
            throw new InvalidOperationException($"Type '{effectiveType}' is not constructible for YAML object binding.");

        var properties = effectiveType.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(acl.IsPropertyBindable)
            .OrderBy(static x => x.MetadataToken).ToArray();

        var members = new List<MemberDescriptor>(properties.Length);
        MemberDescriptor? extensionDataMember = null;

        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];

            var member = new MemberDescriptor(
                acl.GetCanonicalMemberLabel(property),
                property.PropertyType,
                i,
                acl.IsRequired(property),
                property.GetMethod?.IsPublic == true,
                property.SetMethod?.IsPublic == true,
                acl.IsExtensionData(property),
                new ReflectionValueAccessor(property));

            if (member.IsExtensionData)
            {
                if (extensionDataMember is not null)
                    throw new InvalidOperationException($"Type '{effectiveType}' has more than one extension-data property.");

                extensionDataMember = member;
            }

            members.Add(member);
        }

        var constructors = effectiveType.GetConstructors(BindingFlags.Instance | BindingFlags.Public).Where(acl.IsConstructorBindable)
            .OrderBy(static x => x.GetParameters().Length).ThenBy(static x => x.MetadataToken).ToArray();

        var parameterless = constructors.FirstOrDefault(static x => x.GetParameters().Length == 0);

        var route = parameterless is not null && !effectiveType.IsAbstract && !effectiveType.IsInterface
            ? ConstructionRoute.ShellFill
            : ConstructionRoute.Constructor;

        ConstructorDescriptor? constructorDescriptor = null;

        if (route == ConstructionRoute.Constructor)
        {
            if (constructors.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Type '{effectiveType}' must expose exactly one bindable public constructor when shell-fill is unavailable.");
            }

            var constructor = constructors[0];

            var parameters = constructor.GetParameters().Select(p => new ConstructorParameterDescriptor(
                p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? p.Name ?? throw new InvalidOperationException("Constructor parameter has no name."),
                p.ParameterType,
                p.Position,
                p.HasDefaultValue,
                p.DefaultValue)).ToArray();

            constructorDescriptor = new(parameters, new ReflectionConstructorActivator(constructor));
        }

        return new(
            type,
            effectiveType,
            YamlTypeKind.Object,
            route,
            !type.IsValueType,
            null,
            null,
            null,
            members,
            extensionDataMember,
            constructorDescriptor,
            route == ConstructionRoute.ShellFill ? new ReflectionShellActivator(effectiveType) : constructorDescriptor?.Activator);
    }

    private static bool IsScalarType(Type type)
    {
        if (type.IsEnum)
            return true;

        return type == typeof(string)
               || type == typeof(bool)
               || type == typeof(byte)
               || type == typeof(sbyte)
               || type == typeof(short)
               || type == typeof(ushort)
               || type == typeof(int)
               || type == typeof(uint)
               || type == typeof(long)
               || type == typeof(ulong)
               || type == typeof(float)
               || type == typeof(double)
               || type == typeof(decimal)
               || type == typeof(DateTime)
               || type == typeof(DateTimeOffset)
               || type == typeof(DateOnly)
               || type == typeof(TimeOnly);
    }

    private static bool TryGetListType(Type type, out Type elementType)
    {
        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IList<>))
        {
            elementType = type.GetGenericArguments()[0];

            return true;
        }

        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>))
        {
            elementType = type.GetGenericArguments()[0];

            return true;
        }

        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
        {
            elementType = type.GetGenericArguments()[0];

            return true;
        }

        var listInterface = type.GetInterfaces().FirstOrDefault(static x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IList<>));

        if (listInterface is not null)
        {
            elementType = listInterface.GetGenericArguments()[0];

            return true;
        }

        elementType = null!;

        return false;
    }

    private static bool TryGetDictionaryType(Type type, out Type keyType, out Type valueType)
    {
        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IDictionary<,>))
        {
            var args = type.GetGenericArguments();
            keyType = args[0];
            valueType = args[1];

            return true;
        }

        if (type.IsInterface && type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyDictionary<,>))
        {
            var args = type.GetGenericArguments();
            keyType = args[0];
            valueType = args[1];

            return true;
        }

        var dictionaryInterface = type.GetInterfaces()
            .FirstOrDefault(static x => x.IsGenericType && x.GetGenericTypeDefinition() == typeof(IDictionary<,>));

        if (dictionaryInterface is not null)
        {
            var args = dictionaryInterface.GetGenericArguments();
            keyType = args[0];
            valueType = args[1];

            return true;
        }

        keyType = null!;
        valueType = null!;

        return false;
    }

    private static Type GetListImplementationType(Type type, Type elementType)
    {
        if (!type.IsInterface && !type.IsAbstract)
            return type;

        return typeof(List<>).MakeGenericType(elementType);
    }

    private static Type GetDictionaryImplementationType(Type type, Type keyType, Type valueType)
    {
        if (!type.IsInterface && !type.IsAbstract)
            return type;

        return typeof(Dictionary<,>).MakeGenericType(keyType, valueType);
    }
}

internal sealed class ReflectionConstructorActivator(ConstructorInfo constructor) : IValueActivator
{
    public object? CreateShell() => throw new InvalidOperationException($"Constructor '{constructor}' does not support shell creation.");

    public object? CreateFromArguments(object?[] arguments) => constructor.Invoke(arguments);
}

internal sealed class ReflectionShellActivator(Type type) : IValueActivator
{
    public object? CreateShell() => Activator.CreateInstance(type);

    public object? CreateFromArguments(object?[] arguments) =>
        throw new InvalidOperationException($"Type '{type}' is configured for shell-fill construction.");
}

internal sealed class ReflectionValueAccessor(PropertyInfo property) : IValueAccessor
{
    public object? Get(object target) => property.GetValue(target);

    public void Set(object target, object? value) => property.SetValue(target, value);
}
