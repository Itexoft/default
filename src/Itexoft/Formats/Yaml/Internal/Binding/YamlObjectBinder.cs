// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Numerics;
using System.Reflection;
using Itexoft.Extensions;
using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Representation;
using Itexoft.Formats.Yaml.Internal.Session;

namespace Itexoft.Formats.Yaml.Internal.Binding;

internal static class YamlObjectBinder
{
    public static object? Bind(YamlSession session, RepresentationGraph graph, Type type) =>
        new BinderContext(session, graph).Bind(graph.RootId, type);

    private static void AddToList(object list, object? value)
    {
        if (list is IList nonGeneric)
        {
            nonGeneric.Add(value);

            return;
        }

        var add = list.GetType().GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, [value?.GetType() ?? typeof(object)]);

        if (add is null)
        {
            add = list.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(static x => x.Name == "Add" && x.GetParameters().Length == 1);
        }

        add?.Invoke(list, [value]);
    }

    private static void AddToDictionary(object dictionary, object? key, object? value)
    {
        if (dictionary is IDictionary nonGeneric)
        {
            nonGeneric.Add(key ?? throw new InvalidOperationException("YAML dictionary key cannot be null for CLR dictionary binding."), value);

            return;
        }

        var add = dictionary.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(static x => x.Name == "Add" && x.GetParameters().Length == 2);

        add?.Invoke(dictionary, [key, value]);
    }

    private sealed class BinderContext(YamlSession session, RepresentationGraph graph)
    {
        private readonly Dictionary<SemanticNodeId, object?> completed = [];
        private readonly HashSet<SemanticNodeId> constructing = [];
        private readonly Dictionary<SemanticNodeId, object?> shells = [];

        public object? Bind(SemanticNodeId nodeId, Type targetType)
        {
            var underlyingNullable = Nullable.GetUnderlyingType(targetType);
            var effectiveType = underlyingNullable ?? targetType;
            var node = graph.Nodes[nodeId];

            if (IsNullNode(node))
            {
                if (underlyingNullable is not null || !effectiveType.IsValueType)
                    return null;

                throw session.Diagnostics.CreateException(
                    "YAML400",
                    YamlException.Phase.Bind,
                    $"Cannot bind YAML null to non-nullable '{effectiveType}'.");
            }

            if (effectiveType == typeof(object))
                return this.BindDynamic(nodeId);

            var descriptor = session.Profile.TypeDescriptorProvider.GetDescriptor(targetType);

            return descriptor.Kind switch
            {
                YamlTypeKind.Scalar => this.BindScalar(nodeId, effectiveType),
                YamlTypeKind.Array => this.BindArray(nodeId, descriptor),
                YamlTypeKind.List => this.BindList(nodeId, descriptor),
                YamlTypeKind.Dictionary => this.BindDictionary(nodeId, descriptor),
                YamlTypeKind.Object => this.BindObject(nodeId, descriptor),
                _ => throw session.Diagnostics.CreateException(
                    "YAML401",
                    YamlException.Phase.Bind,
                    $"Unsupported descriptor kind '{descriptor.Kind}'."),
            };
        }

        private object? BindScalar(SemanticNodeId nodeId, Type targetType)
        {
            if (graph.Nodes[nodeId] is not RepresentationScalarNode scalar)
                throw session.Diagnostics.CreateException("YAML402", YamlException.Phase.Bind, $"Node '{nodeId}' is not a scalar.");

            if (targetType == typeof(string))
                return scalar.LogicalText;

            if (targetType == typeof(bool))
                return ParseBoolean(scalar.LogicalText);

            if (targetType.IsEnum)
            {
                return session.Profile.ScalarCodecProvider.TryGetCodec(targetType, out var enumCodec)
                    ? enumCodec!.Read(scalar.LogicalText, targetType)
                    : throw session.Diagnostics.CreateException("YAML403", YamlException.Phase.Bind, $"Enum codec is missing for '{targetType}'.");
            }

            if (session.Profile.ScalarCodecProvider.TryGetCodec(targetType, out var codec))
                return codec!.Read(scalar.LogicalText, targetType);

            if (TryBindNumeric(targetType, scalar, out var numeric))
                return numeric;

            throw session.Diagnostics.CreateException("YAML404", YamlException.Phase.Bind, $"Unsupported scalar target type '{targetType}'.");
        }

        private object? BindArray(SemanticNodeId nodeId, TypeDescriptor descriptor)
        {
            if (graph.Nodes[nodeId] is not RepresentationSequenceNode sequence)
                throw session.Diagnostics.CreateException("YAML405", YamlException.Phase.Bind, $"Node '{nodeId}' is not a sequence.");

            if (this.shells.TryGetValue(nodeId, out var existing))
                return existing;

            var elementType = descriptor.ElementType.Required();
            var array = Array.CreateInstance(elementType, sequence.Items.Count);
            this.shells[nodeId] = array;
            this.LinkBinding(nodeId);

            for (var i = 0; i < sequence.Items.Count; i++)
                array.SetValue(this.Bind(sequence.Items[i], elementType), i);

            this.completed[nodeId] = array;

            return array;
        }

        private object BindList(SemanticNodeId nodeId, TypeDescriptor descriptor)
        {
            if (graph.Nodes[nodeId] is not RepresentationSequenceNode sequence)
                throw session.Diagnostics.CreateException("YAML406", YamlException.Phase.Bind, $"Node '{nodeId}' is not a sequence.");

            if (this.completed.TryGetValue(nodeId, out var existing) && existing is not null)
                return existing;

            if (this.shells.TryGetValue(nodeId, out var shell) && shell is not null)
                return shell;

            var instance = descriptor.Activator?.CreateShell()
                           ?? throw session.Diagnostics.CreateException(
                               "YAML407",
                               YamlException.Phase.Bind,
                               $"List descriptor for '{descriptor.Type}' has no shell activator.");

            this.shells[nodeId] = instance;
            this.LinkBinding(nodeId);
            var elementType = descriptor.ElementType.Required();

            foreach (var itemId in sequence.Items)
                AddToList(instance, this.Bind(itemId, elementType));

            this.completed[nodeId] = instance;

            return instance;
        }

        private object BindDictionary(SemanticNodeId nodeId, TypeDescriptor descriptor)
        {
            if (graph.Nodes[nodeId] is not RepresentationMappingNode mapping)
                throw session.Diagnostics.CreateException("YAML408", YamlException.Phase.Bind, $"Node '{nodeId}' is not a mapping.");

            if (this.completed.TryGetValue(nodeId, out var existing) && existing is not null)
                return existing;

            if (this.shells.TryGetValue(nodeId, out var shell) && shell is not null)
                return shell;

            var instance = descriptor.Activator?.CreateShell()
                           ?? throw session.Diagnostics.CreateException(
                               "YAML409",
                               YamlException.Phase.Bind,
                               $"Dictionary descriptor for '{descriptor.Type}' has no shell activator.");

            this.shells[nodeId] = instance;
            this.LinkBinding(nodeId);
            var keyType = descriptor.KeyType.Required();
            var valueType = descriptor.ValueType.Required();

            foreach (var entry in mapping.Entries)
                AddToDictionary(instance, this.Bind(entry.KeyId, keyType), this.Bind(entry.ValueId, valueType));

            this.completed[nodeId] = instance;

            return instance;
        }

        private object BindObject(SemanticNodeId nodeId, TypeDescriptor descriptor)
        {
            if (graph.Nodes[nodeId] is not RepresentationMappingNode mapping)
                throw session.Diagnostics.CreateException("YAML410", YamlException.Phase.Bind, $"Node '{nodeId}' is not a mapping.");

            if (descriptor.ConstructionRoute == ConstructionRoute.Constructor)
                return this.BindConstructorObject(nodeId, descriptor, mapping);

            if (this.completed.TryGetValue(nodeId, out var completed) && completed is not null)
                return completed;

            if (this.shells.TryGetValue(nodeId, out var existingShell) && existingShell is not null)
                return existingShell;

            var shell = descriptor.Activator?.CreateShell()
                        ?? throw session.Diagnostics.CreateException(
                            "YAML411",
                            YamlException.Phase.Bind,
                            $"Object descriptor for '{descriptor.Type}' has no shell activator.");

            this.shells[nodeId] = shell;
            this.constructing.Add(nodeId);
            this.LinkBinding(nodeId);

            try
            {
                var members = this.MapObjectMembers(mapping);
                var unknown = new Dictionary<string, object?>(StringComparer.Ordinal);

                foreach (var pair in members)
                {
                    if (!descriptor.MembersByLabel.TryGetValue(pair.Key, out var member))
                    {
                        unknown.Add(pair.Key, this.BindDynamic(pair.Value));

                        continue;
                    }

                    if (!member.CanWrite)
                    {
                        throw session.Diagnostics.CreateException(
                            "YAML412",
                            YamlException.Phase.Bind,
                            $"Member '{pair.Key}' on '{descriptor.Type}' is not writable.");
                    }

                    var value = this.Bind(pair.Value, member.MemberType);
                    member.Accessor.Set(shell, value);
                }

                foreach (var member in descriptor.Members.Where(static x => x.Required))
                {
                    if (members.ContainsKey(member.Label))
                        continue;

                    throw session.Diagnostics.CreateException(
                        "YAML413",
                        YamlException.Phase.Bind,
                        $"Required member '{member.Label}' is missing for '{descriptor.Type}'.");
                }

                if (unknown.Count != 0)
                    this.ApplyExtensionData(nodeId, shell, descriptor, unknown);

                this.completed[nodeId] = shell;

                return shell;
            }
            finally
            {
                this.constructing.Remove(nodeId);
            }
        }

        private object BindConstructorObject(SemanticNodeId nodeId, TypeDescriptor descriptor, RepresentationMappingNode mapping)
        {
            if (descriptor.IsReferenceType && graph.OccurrenceCounts.TryGetValue(nodeId, out var count) && count > 1)
            {
                throw session.Diagnostics.CreateException(
                    "YAML414",
                    YamlException.Phase.Bind,
                    $"Constructor-bound type '{descriptor.Type}' cannot preserve shared identity.");
            }

            if (!this.constructing.Add(nodeId))
            {
                throw session.Diagnostics.CreateException(
                    "YAML415",
                    YamlException.Phase.Bind,
                    $"Constructor-bound type '{descriptor.Type}' cannot represent cyclic references.");
            }

            try
            {
                var members = this.MapObjectMembers(mapping);
                var constructor = descriptor.Constructor.Required();
                var args = new object?[constructor.Parameters.Count];
                var consumed = new HashSet<string>(StringComparer.Ordinal);

                foreach (var parameter in constructor.Parameters)
                {
                    if (members.TryGetValue(parameter.Label, out var node))
                    {
                        args[parameter.Position] = this.Bind(node, parameter.ParameterType);
                        consumed.Add(parameter.Label);

                        continue;
                    }

                    if (parameter.HasDefaultValue)
                    {
                        args[parameter.Position] = parameter.DefaultValue;

                        continue;
                    }

                    throw session.Diagnostics.CreateException(
                        "YAML416",
                        YamlException.Phase.Bind,
                        $"Constructor parameter '{parameter.Label}' is missing for '{descriptor.Type}'.");
                }

                var instance = constructor.Activator.CreateFromArguments(args)
                               ?? throw session.Diagnostics.CreateException(
                                   "YAML417",
                                   YamlException.Phase.Bind,
                                   $"Constructor activator returned null for '{descriptor.Type}'.");

                this.LinkBinding(nodeId);
                this.completed[nodeId] = instance;

                var unknown = new Dictionary<string, object?>(StringComparer.Ordinal);

                foreach (var pair in members)
                {
                    if (consumed.Contains(pair.Key))
                        continue;

                    if (!descriptor.MembersByLabel.TryGetValue(pair.Key, out var member))
                    {
                        unknown[pair.Key] = this.BindDynamic(pair.Value);

                        continue;
                    }

                    if (!member.CanWrite)
                    {
                        throw session.Diagnostics.CreateException(
                            "YAML418",
                            YamlException.Phase.Bind,
                            $"Member '{pair.Key}' on '{descriptor.Type}' is not writable after constructor binding.");
                    }

                    member.Accessor.Set(instance, this.Bind(pair.Value, member.MemberType));
                }

                foreach (var member in descriptor.Members.Where(static x => x.Required))
                {
                    if (members.ContainsKey(member.Label))
                        continue;

                    throw session.Diagnostics.CreateException(
                        "YAML419",
                        YamlException.Phase.Bind,
                        $"Required member '{member.Label}' is missing for '{descriptor.Type}'.");
                }

                if (unknown.Count != 0)
                    this.ApplyExtensionData(nodeId, instance, descriptor, unknown);

                return instance;
            }
            finally
            {
                this.constructing.Remove(nodeId);
            }
        }

        private object? BindDynamic(SemanticNodeId nodeId)
        {
            if (this.completed.TryGetValue(nodeId, out var completed))
                return completed;

            var node = graph.Nodes[nodeId];

            return node switch
            {
                RepresentationScalarNode scalar => this.DynamicScalar(scalar),
                RepresentationSequenceNode sequence => this.BindDynamicSequence(nodeId, sequence),
                RepresentationMappingNode mapping => this.BindDynamicMapping(nodeId, mapping),
                _ => throw session.Diagnostics.CreateException(
                    "YAML420",
                    YamlException.Phase.Bind,
                    $"Unsupported dynamic node '{node.GetType().Name}'."),
            };
        }

        private static bool IsNullNode(RepresentationNode node) =>
            node is RepresentationScalarNode scalar && scalar.TagState.Tag == YamlTags.Null;

        private object? DynamicScalar(RepresentationScalarNode scalar) =>
            scalar.TagState.Tag switch
            {
                YamlTags.Null => null,
                YamlTags.Boolean => scalar.CanonicalForm.BooleanValue,
                YamlTags.Integer => FitsInt64(scalar.CanonicalForm.IntegerValue)
                    ? (object)(long)scalar.CanonicalForm.IntegerValue
                    : scalar.CanonicalForm.IntegerValue,
                YamlTags.Float => scalar.CanonicalForm.FloatValue,
                _ => scalar.LogicalText,
            };

        private List<object?> BindDynamicSequence(SemanticNodeId nodeId, RepresentationSequenceNode sequence)
        {
            if (this.shells.TryGetValue(nodeId, out var shell) && shell is List<object?> existing)
                return existing;

            var list = new List<object?>(sequence.Items.Count);
            this.shells[nodeId] = list;
            this.LinkBinding(nodeId);

            foreach (var itemId in sequence.Items)
                list.Add(this.BindDynamic(itemId));

            this.completed[nodeId] = list;

            return list;
        }

        private object BindDynamicMapping(SemanticNodeId nodeId, RepresentationMappingNode mapping)
        {
            if (this.shells.TryGetValue(nodeId, out var existing))
                return existing!;

            foreach (var entry in mapping.Entries)
            {
                if (graph.Nodes[entry.KeyId] is RepresentationScalarNode scalarKey && scalarKey.TagState.Tag == YamlTags.String)
                    continue;

                return this.BindDynamicObjectKeyMapping(nodeId, mapping);
            }

            var stringKeys = new Dictionary<string, object?>(mapping.Entries.Count, StringComparer.Ordinal);
            this.shells[nodeId] = stringKeys;
            this.LinkBinding(nodeId);

            foreach (var entry in mapping.Entries)
            {
                var scalarKey = (RepresentationScalarNode)graph.Nodes[entry.KeyId];
                stringKeys[scalarKey.LogicalText] = this.BindDynamic(entry.ValueId);
            }

            this.completed[nodeId] = stringKeys;

            return stringKeys;
        }

        private Dictionary<object, object?> BindDynamicObjectKeyMapping(SemanticNodeId nodeId, RepresentationMappingNode mapping)
        {
            if (this.shells.TryGetValue(nodeId, out var shell) && shell is Dictionary<object, object?> existing)
                return existing;

            var objectKeys = new Dictionary<object, object?>(mapping.Entries.Count);
            this.shells[nodeId] = objectKeys;
            this.LinkBinding(nodeId);

            foreach (var entry in mapping.Entries)
                objectKeys[this.BindDynamic(entry.KeyId) ?? string.Empty] = this.BindDynamic(entry.ValueId);

            this.completed[nodeId] = objectKeys;

            return objectKeys;
        }

        private Dictionary<string, SemanticNodeId> MapObjectMembers(RepresentationMappingNode mapping)
        {
            var result = new Dictionary<string, SemanticNodeId>(StringComparer.Ordinal);

            foreach (var entry in mapping.Entries)
            {
                if (graph.Nodes[entry.KeyId] is not RepresentationScalarNode keyScalar)
                    throw session.Diagnostics.CreateException("YAML421", YamlException.Phase.Bind, "Object mapping key must be a scalar string.");

                result[keyScalar.LogicalText] = entry.ValueId;
            }

            return result;
        }

        private void ApplyExtensionData(SemanticNodeId nodeId, object target, TypeDescriptor descriptor, Dictionary<string, object?> unknown)
        {
            if (descriptor.ExtensionDataMember is null)
            {
                throw session.Diagnostics.CreateException(
                    "YAML422",
                    YamlException.Phase.Bind,
                    $"Unknown YAML members are not allowed for '{descriptor.Type}'.");
            }

            IDictionary<string, object?> sink;

            if (descriptor.ExtensionDataMember.CanRead
                && descriptor.ExtensionDataMember.Accessor.Get(target) is IDictionary<string, object?> existing)
                sink = existing;
            else if (descriptor.ExtensionDataMember.CanWrite)
            {
                sink = new Dictionary<string, object?>(StringComparer.Ordinal);
                descriptor.ExtensionDataMember.Accessor.Set(target, sink);
            }
            else
            {
                throw session.Diagnostics.CreateException(
                    "YAML423",
                    YamlException.Phase.Bind,
                    $"Extension-data member '{descriptor.ExtensionDataMember.Label}' cannot accept YAML members.");
            }

            foreach (var pair in unknown)
                sink[pair.Key] = pair.Value;

            session.Ledger.CaptureUnmatchedMembers(nodeId, unknown);
        }

        private void LinkBinding(SemanticNodeId nodeId)
        {
            var bindingId = session.Ids.NextBinding();
            session.Ledger.Link(nodeId, bindingId);
        }

        private static bool TryBindNumeric(Type targetType, RepresentationScalarNode scalar, out object? value)
        {
            value = null;

            if (scalar.CanonicalForm.Kind == ScalarCanonicalKind.Integer)
            {
                var integer = scalar.CanonicalForm.IntegerValue;

                value = targetType switch
                {
                    var t when t == typeof(byte) => (byte)integer,
                    var t when t == typeof(sbyte) => (sbyte)integer,
                    var t when t == typeof(short) => (short)integer,
                    var t when t == typeof(ushort) => (ushort)integer,
                    var t when t == typeof(int) => (int)integer,
                    var t when t == typeof(uint) => (uint)integer,
                    var t when t == typeof(long) => (long)integer,
                    var t when t == typeof(ulong) => (ulong)integer,
                    var t when t == typeof(decimal) => (decimal)integer,
                    var t when t == typeof(float) => (float)integer,
                    var t when t == typeof(double) => (double)integer,
                    _ => null,
                };

                return value is not null;
            }

            if (scalar.CanonicalForm.Kind == ScalarCanonicalKind.Float)
            {
                var floating = scalar.CanonicalForm.FloatValue;

                value = targetType switch
                {
                    var t when t == typeof(float) => (float)floating,
                    var t when t == typeof(double) => floating,
                    var t when t == typeof(decimal) => (decimal)floating,
                    _ => null,
                };

                return value is not null;
            }

            return false;
        }

        private static bool ParseBoolean(string text) => text switch
        {
            "true" or "True" or "TRUE" => true,
            "false" or "False" or "FALSE" => false,
            _ => throw new FormatException($"Invalid YAML boolean '{text}'."),
        };

        private static bool FitsInt64(BigInteger value) => value >= long.MinValue && value <= long.MaxValue;
    }
}
