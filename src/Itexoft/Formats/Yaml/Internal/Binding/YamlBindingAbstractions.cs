// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;

namespace Itexoft.Formats.Yaml.Internal.Binding;

internal enum YamlTypeKind
{
    Scalar,
    Array,
    List,
    Dictionary,
    Object,
}

internal enum ConstructionRoute
{
    Scalar,
    ShellFill,
    Constructor,
}

internal interface IMetadataAcl
{
    bool IsTypeBindable(Type type);

    bool IsConstructorBindable(ConstructorInfo constructor);

    bool IsPropertyBindable(PropertyInfo property);

    string GetCanonicalMemberLabel(PropertyInfo property);

    bool IsRequired(PropertyInfo property);

    bool IsExtensionData(PropertyInfo property);
}

internal interface ITypeDescriptorProvider
{
    TypeDescriptor GetDescriptor(Type type);
}

internal interface IValueActivator
{
    object? CreateShell();

    object? CreateFromArguments(object?[] arguments);
}

internal interface IValueAccessor
{
    object? Get(object target);

    void Set(object target, object? value);
}

internal interface IScalarCodecProvider
{
    bool TryGetCodec(Type type, out IScalarCodec? codec);
}

internal interface IScalarCodec
{
    object? Read(string text, Type targetType);

    string Write(object? value, Type targetType);
}

internal sealed class MemberDescriptor(
    string label,
    Type memberType,
    int order,
    bool required,
    bool canRead,
    bool canWrite,
    bool isExtensionData,
    IValueAccessor accessor)
{
    public string Label { get; } = label;

    public Type MemberType { get; } = memberType;

    public int Order { get; } = order;

    public bool Required { get; } = required;

    public bool CanRead { get; } = canRead;

    public bool CanWrite { get; } = canWrite;

    public bool IsExtensionData { get; } = isExtensionData;

    public IValueAccessor Accessor { get; } = accessor;
}

internal sealed class ConstructorParameterDescriptor(string label, Type parameterType, int position, bool hasDefaultValue, object? defaultValue)
{
    public string Label { get; } = label;

    public Type ParameterType { get; } = parameterType;

    public int Position { get; } = position;

    public bool HasDefaultValue { get; } = hasDefaultValue;

    public object? DefaultValue { get; } = defaultValue;
}

internal sealed class ConstructorDescriptor(IReadOnlyList<ConstructorParameterDescriptor> parameters, IValueActivator activator)
{
    public IReadOnlyList<ConstructorParameterDescriptor> Parameters { get; } = parameters;

    public IValueActivator Activator { get; } = activator;
}

internal sealed class TypeDescriptor(
    Type type,
    Type implementationType,
    YamlTypeKind kind,
    ConstructionRoute constructionRoute,
    bool isReferenceType,
    Type? elementType,
    Type? keyType,
    Type? valueType,
    IReadOnlyList<MemberDescriptor> members,
    MemberDescriptor? extensionDataMember,
    ConstructorDescriptor? constructor,
    IValueActivator? activator)
{
    public Type Type { get; } = type;

    public Type ImplementationType { get; } = implementationType;

    public YamlTypeKind Kind { get; } = kind;

    public ConstructionRoute ConstructionRoute { get; } = constructionRoute;

    public bool IsReferenceType { get; } = isReferenceType;

    public Type? ElementType { get; } = elementType;

    public Type? KeyType { get; } = keyType;

    public Type? ValueType { get; } = valueType;

    public IReadOnlyList<MemberDescriptor> Members { get; } = members;

    public MemberDescriptor? ExtensionDataMember { get; } = extensionDataMember;

    public ConstructorDescriptor? Constructor { get; } = constructor;

    public IValueActivator? Activator { get; } = activator;

    public IReadOnlyDictionary<string, MemberDescriptor> MembersByLabel { get; } = members.ToDictionary(
        static x => x.Label,
        static x => x,
        StringComparer.Ordinal);
}
