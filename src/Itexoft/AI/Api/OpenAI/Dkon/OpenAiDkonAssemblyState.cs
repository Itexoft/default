// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using Itexoft.Formats.Dkon;

namespace Itexoft.AI.Api.OpenAI.Dkon;

internal static class DkonAssemblyPathSyntax
{
    public const string Root = "$";
    public const string CountSuffix = "$count";
    public const string KeysSuffix = "$keys";
}

internal sealed class DkonAssemblyGraph
{
    private readonly DkonFormat format;

    public DkonAssemblyGraph(DkonFormat format, Type responseType)
    {
        ArgumentNullException.ThrowIfNull(format);
        ArgumentNullException.ThrowIfNull(responseType);

        this.format = format;
        DkonAssemblyTreeGuard.Validate(format, responseType);
        this.Root = DkonAssemblyNodeFactory.Create(format, responseType, DkonAssemblyPathSyntax.Root, DkonAssemblyPathSyntax.Root, null);
    }

    public DkonAssemblyNode Root { get; }

    public DkonAssemblyFrontierItem[] GetFrontier()
    {
        var items = new List<DkonAssemblyFrontierItem>();
        this.Root.CollectFrontier(items, this.format);

        return [.. items];
    }

    public DkonAssemblyResolvedFact[] GetResolvedFacts()
    {
        var facts = new List<DkonAssemblyResolvedFact>();
        this.Root.CollectResolvedFacts(facts);

        return [.. facts];
    }

    public DkonAssemblyNode GetNode(string pathId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathId);

        return this.Root.Find(pathId) ?? throw new InvalidOperationException($"Unknown DKON path '{pathId}'.");
    }

    public object? Materialize() => this.Root.Materialize(this.format);
}

file static class DkonAssemblyTreeGuard
{
    public static void Validate(DkonFormat format, Type type)
    {
        var active = new HashSet<Type>();
        Walk(format, type, active);
    }

    private static void Walk(DkonFormat format, Type type, HashSet<Type> active)
    {
        var typeInfo = DkonAssemblyTypeInfo.GetRequired(format, type);

        if (!DkonAssemblyNodeFactory.IsContainer(typeInfo))
            return;

        var shapeType = Nullable.GetUnderlyingType(typeInfo.Type) ?? typeInfo.Type;

        if (!active.Add(shapeType))
        {
            throw new NotSupportedException(
                $"DkonAssemblyClient v1 supports only tree topology. Recursive/shared contract type '{shapeType}' is not supported.");
        }

        try
        {
            switch (typeInfo.Kind)
            {
                case JsonTypeInfoKind.Object:
                    for (var i = 0; i < typeInfo.Properties.Count; i++)
                    {
                        var property = typeInfo.Properties[i];

                        if (property.Set is null)
                            continue;

                        Walk(format, property.PropertyType, active);
                    }

                    break;
                case JsonTypeInfoKind.Dictionary:
                    Walk(format, typeInfo.ElementType ?? typeof(object), active);

                    break;
                case JsonTypeInfoKind.Enumerable when typeInfo.Type != typeof(string):
                    Walk(format, typeInfo.ElementType ?? typeof(object), active);

                    break;
            }
        }
        finally
        {
            active.Remove(shapeType);
        }
    }
}

file static class DkonAssemblyTypeInfo
{
    public static JsonTypeInfo GetRequired(DkonFormat format, Type type) =>
        format.GetTypeInfo(type) ?? throw new ArgumentException($"The specified type {type} is not a known type.", nameof(type));
}

file static class DkonAssemblyNodeFactory
{
    public static DkonAssemblyNode Create(DkonFormat format, Type type, string pathId, string displayPath, JsonPropertyInfo? property)
    {
        var typeInfo = DkonAssemblyTypeInfo.GetRequired(format, type);

        if (!IsContainer(typeInfo))
            return new DkonAssemblyScalarNode(pathId, displayPath, type, typeInfo, property);

        return typeInfo.Kind switch
        {
            JsonTypeInfoKind.Object => CreateObject(format, typeInfo, pathId, displayPath, property),
            JsonTypeInfoKind.Dictionary => CreateDictionary(format, typeInfo, pathId, displayPath, property),
            JsonTypeInfoKind.Enumerable when typeInfo.Type != typeof(string) => CreateList(format, typeInfo, pathId, displayPath, property),
            _ => new DkonAssemblyScalarNode(pathId, displayPath, type, typeInfo, property),
        };
    }

    public static bool IsContainer(JsonTypeInfo typeInfo) =>
        typeInfo.Kind == JsonTypeInfoKind.Object
        || typeInfo.Kind == JsonTypeInfoKind.Dictionary
        || (typeInfo.Kind == JsonTypeInfoKind.Enumerable && typeInfo.Type != typeof(string));

    private static DkonAssemblyObjectNode CreateObject(
        DkonFormat format,
        JsonTypeInfo typeInfo,
        string pathId,
        string displayPath,
        JsonPropertyInfo? property)
    {
        var children = new List<DkonAssemblyNode>(typeInfo.Properties.Count);

        for (var i = 0; i < typeInfo.Properties.Count; i++)
        {
            var childProperty = typeInfo.Properties[i];

            if (childProperty.Set is null)
                continue;

            children.Add(
                Create(format, childProperty.PropertyType, $"{pathId}.{childProperty.Name}", $"{displayPath}.{childProperty.Name}", childProperty));
        }

        return new(pathId, displayPath, typeInfo.Type, typeInfo, property, [.. children]);
    }

    private static DkonAssemblyListNode CreateList(
        DkonFormat format,
        JsonTypeInfo typeInfo,
        string pathId,
        string displayPath,
        JsonPropertyInfo? property)
    {
        var elementType = typeInfo.ElementType ?? typeof(object);
        var elementTypeInfo = DkonAssemblyTypeInfo.GetRequired(format, elementType);

        return new(pathId, displayPath, typeInfo.Type, typeInfo, property, elementType, elementTypeInfo);
    }

    private static DkonAssemblyDictionaryNode CreateDictionary(
        DkonFormat format,
        JsonTypeInfo typeInfo,
        string pathId,
        string displayPath,
        JsonPropertyInfo? property)
    {
        var keyType = typeInfo.KeyType ?? typeof(object);
        var valueType = typeInfo.ElementType ?? typeof(object);
        var valueTypeInfo = DkonAssemblyTypeInfo.GetRequired(format, valueType);

        return new(pathId, displayPath, typeInfo.Type, typeInfo, property, keyType, valueType, valueTypeInfo);
    }
}

internal abstract class DkonAssemblyNode(string pathId, string displayPath, Type declaredType, JsonTypeInfo typeInfo, JsonPropertyInfo? property)
{
    public string PathId { get; } = pathId;
    public string DisplayPath { get; } = displayPath;
    public Type DeclaredType { get; } = declaredType;
    public JsonTypeInfo TypeInfo { get; } = typeInfo;
    public JsonPropertyInfo? Property { get; } = property;

    public abstract void CollectFrontier(List<DkonAssemblyFrontierItem> items, DkonFormat format);
    public abstract void CollectResolvedFacts(List<DkonAssemblyResolvedFact> facts);
    public abstract DkonAssemblyNode? Find(string pathId);
    public abstract object? Materialize(DkonFormat format);
}

internal sealed class DkonAssemblyScalarNode(string pathId, string displayPath, Type declaredType, JsonTypeInfo typeInfo, JsonPropertyInfo? property)
    : DkonAssemblyNode(pathId, displayPath, declaredType, typeInfo, property)
{
    public bool IsResolved { get; private set; }
    public object? Value { get; private set; }
    public string DkonValue { get; private set; } = string.Empty;

    public void Resolve(object? value, string dkonValue)
    {
        this.IsResolved = true;
        this.Value = value;
        this.DkonValue = dkonValue;
    }

    public override void CollectFrontier(List<DkonAssemblyFrontierItem> items, DkonFormat format)
    {
        if (this.IsResolved)
            return;

        items.Add(
            new DkonAssemblyFrontierItem
            {
                PathId = this.PathId,
                DisplayPath = this.DisplayPath,
                Kind = DkonAssemblyFrontierKind.Leaf,
                Contract = format.GetContract(this.DeclaredType, out _),
            });
    }

    public override void CollectResolvedFacts(List<DkonAssemblyResolvedFact> facts)
    {
        if (!this.IsResolved)
            return;

        facts.Add(
            new DkonAssemblyResolvedFact
            {
                PathId = this.PathId,
                DisplayPath = this.DisplayPath,
                DkonValue = this.DkonValue,
            });
    }

    public override DkonAssemblyNode? Find(string pathId) => string.Equals(this.PathId, pathId, StringComparison.Ordinal) ? this : null;

    public override object? Materialize(DkonFormat format) => this.Value;
}

internal sealed class DkonAssemblyObjectNode(
    string pathId,
    string displayPath,
    Type declaredType,
    JsonTypeInfo typeInfo,
    JsonPropertyInfo? property,
    DkonAssemblyNode[] children) : DkonAssemblyNode(pathId, displayPath, declaredType, typeInfo, property)
{
    public DkonAssemblyNode[] Children { get; } = children;

    public override void CollectFrontier(List<DkonAssemblyFrontierItem> items, DkonFormat format)
    {
        for (var i = 0; i < this.Children.Length; i++)
            this.Children[i].CollectFrontier(items, format);
    }

    public override void CollectResolvedFacts(List<DkonAssemblyResolvedFact> facts)
    {
        for (var i = 0; i < this.Children.Length; i++)
            this.Children[i].CollectResolvedFacts(facts);
    }

    public override DkonAssemblyNode? Find(string pathId)
    {
        if (string.Equals(this.PathId, pathId, StringComparison.Ordinal))
            return this;

        for (var i = 0; i < this.Children.Length; i++)
        {
            var child = this.Children[i].Find(pathId);

            if (child is not null)
                return child;
        }

        return null;
    }

    public override object? Materialize(DkonFormat format)
    {
        var instance = DkonAssemblyRuntimeActivator.CreateObject(this.TypeInfo)
                       ?? throw new InvalidOperationException($"Unable to create runtime object for type '{this.DeclaredType}'.");

        for (var i = 0; i < this.Children.Length; i++)
        {
            var child = this.Children[i];
            var property = child.Property;

            if (property?.Set is null)
                continue;

            property.Set(instance, child.Materialize(format));
        }

        return instance;
    }
}

internal sealed class DkonAssemblyListNode(
    string pathId,
    string displayPath,
    Type declaredType,
    JsonTypeInfo typeInfo,
    JsonPropertyInfo? property,
    Type elementType,
    JsonTypeInfo elementTypeInfo) : DkonAssemblyNode(pathId, displayPath, declaredType, typeInfo, property)
{
    public Type ElementType { get; } = elementType;
    public JsonTypeInfo ElementTypeInfo { get; } = elementTypeInfo;
    public DkonAssemblyNode[]? Items { get; private set; }

    public void Expand(DkonFormat format, int count)
    {
        if (this.Items is not null)
            throw new InvalidOperationException($"List path '{this.PathId}' is already expanded.");

        var items = new DkonAssemblyNode[count];

        for (var i = 0; i < count; i++)
        {
            var path = $"{this.PathId}[{i}]";
            var display = $"{this.DisplayPath}[{i}]";
            items[i] = DkonAssemblyNodeFactory.Create(format, this.ElementType, path, display, null);
        }

        this.Items = items;
    }

    public override void CollectFrontier(List<DkonAssemblyFrontierItem> items, DkonFormat format)
    {
        if (this.Items is null)
        {
            items.Add(
                new DkonAssemblyFrontierItem
                {
                    PathId = this.PathId + DkonAssemblyPathSyntax.CountSuffix,
                    DisplayPath = this.DisplayPath + DkonAssemblyPathSyntax.CountSuffix,
                    Kind = DkonAssemblyFrontierKind.CollectionCount,
                    Contract = format.GetContract(this.ElementType, out _),
                });

            return;
        }

        for (var i = 0; i < this.Items.Length; i++)
            this.Items[i].CollectFrontier(items, format);
    }

    public override void CollectResolvedFacts(List<DkonAssemblyResolvedFact> facts)
    {
        if (this.Items is null)
            return;

        for (var i = 0; i < this.Items.Length; i++)
            this.Items[i].CollectResolvedFacts(facts);
    }

    public override DkonAssemblyNode? Find(string pathId)
    {
        if (string.Equals(this.PathId, pathId, StringComparison.Ordinal))
            return this;

        if (this.Items is null)
            return null;

        for (var i = 0; i < this.Items.Length; i++)
        {
            var item = this.Items[i].Find(pathId);

            if (item is not null)
                return item;
        }

        return null;
    }

    public override object? Materialize(DkonFormat format)
    {
        var items = this.Items ?? [];

        if (this.DeclaredType.IsArray)
        {
            var array = Array.CreateInstance(this.ElementType, items.Length);

            for (var i = 0; i < items.Length; i++)
                array.SetValue(items[i].Materialize(format), i);

            return array;
        }

        var collection = DkonAssemblyRuntimeActivator.CreateCollection(this.TypeInfo)
                         ?? throw new InvalidOperationException($"Unable to create runtime collection for type '{this.DeclaredType}'.");

        for (var i = 0; i < items.Length; i++)
            collection.Add(items[i].Materialize(format));

        return collection;
    }
}

internal sealed class DkonAssemblyDictionaryNode(
    string pathId,
    string displayPath,
    Type declaredType,
    JsonTypeInfo typeInfo,
    JsonPropertyInfo? property,
    Type keyType,
    Type valueType,
    JsonTypeInfo valueTypeInfo) : DkonAssemblyNode(pathId, displayPath, declaredType, typeInfo, property)
{
    public Type KeyType { get; } = keyType;
    public Type ValueType { get; } = valueType;
    public JsonTypeInfo ValueTypeInfo { get; } = valueTypeInfo;
    public DkonAssemblyDictionaryEntry[]? Entries { get; private set; }

    public void Expand(DkonFormat format, IReadOnlyList<(object? Value, string DkonValue)> keys)
    {
        if (this.Entries is not null)
            throw new InvalidOperationException($"Dictionary path '{this.PathId}' is already expanded.");

        var entries = new DkonAssemblyDictionaryEntry[keys.Count];

        for (var i = 0; i < entries.Length; i++)
        {
            var slotPath = $"{this.PathId}{{{i}}}";
            var slotDisplay = $"{this.DisplayPath}[{keys[i].DkonValue}]";
            entries[i] = new(keys[i].Value, keys[i].DkonValue, DkonAssemblyNodeFactory.Create(format, this.ValueType, slotPath, slotDisplay, null));
        }

        this.Entries = entries;
    }

    public override void CollectFrontier(List<DkonAssemblyFrontierItem> items, DkonFormat format)
    {
        if (this.Entries is null)
        {
            items.Add(
                new DkonAssemblyFrontierItem
                {
                    PathId = this.PathId + DkonAssemblyPathSyntax.KeysSuffix,
                    DisplayPath = this.DisplayPath + DkonAssemblyPathSyntax.KeysSuffix,
                    Kind = DkonAssemblyFrontierKind.DictionaryKeys,
                    Contract = format.GetContract(this.ValueType, out _),
                });

            return;
        }

        for (var i = 0; i < this.Entries.Length; i++)
            this.Entries[i].Node.CollectFrontier(items, format);
    }

    public override void CollectResolvedFacts(List<DkonAssemblyResolvedFact> facts)
    {
        if (this.Entries is null)
            return;

        for (var i = 0; i < this.Entries.Length; i++)
            this.Entries[i].Node.CollectResolvedFacts(facts);
    }

    public override DkonAssemblyNode? Find(string pathId)
    {
        if (string.Equals(this.PathId, pathId, StringComparison.Ordinal))
            return this;

        if (this.Entries is null)
            return null;

        for (var i = 0; i < this.Entries.Length; i++)
        {
            var node = this.Entries[i].Node.Find(pathId);

            if (node is not null)
                return node;
        }

        return null;
    }

    public override object? Materialize(DkonFormat format)
    {
        var dictionary = DkonAssemblyRuntimeActivator.CreateDictionary(this.TypeInfo)
                         ?? throw new InvalidOperationException($"Unable to create runtime dictionary for type '{this.DeclaredType}'.");

        var entries = this.Entries ?? [];

        for (var i = 0; i < entries.Length; i++)
        {
            var key = entries[i].Key;

            if (key is null)
                throw new InvalidOperationException($"Dictionary key for path '{this.PathId}' is null.");

            dictionary[key] = entries[i].Node.Materialize(format);
        }

        return dictionary;
    }
}

internal readonly record struct DkonAssemblyDictionaryEntry(object? Key, string DkonValue, DkonAssemblyNode Node);

file static class DkonAssemblyRuntimeActivator
{
    public static object? CreateObject(JsonTypeInfo typeInfo) => CreateObjectInstance(typeInfo.CreateObject, typeInfo.Type);

    public static IDictionary? CreateDictionary(JsonTypeInfo typeInfo) =>
        CreateObjectInstance(typeInfo.CreateObject, typeInfo.Type) as IDictionary;

    public static IList? CreateCollection(JsonTypeInfo typeInfo) =>
        CreateObjectInstance(typeInfo.CreateObject, typeInfo.Type) as IList;

    private static object? CreateObjectInstance(Func<object>? factory, Type declaredType)
    {
        if (factory is not null)
            return factory();

        if (declaredType.IsInterface || declaredType.IsAbstract)
            return null;

        if (declaredType.IsValueType)
            return RuntimeHelpers.GetUninitializedObject(declaredType);

        var constructor = declaredType.GetConstructor(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            Type.EmptyTypes,
            null);

        return constructor?.Invoke(null);
    }
}
