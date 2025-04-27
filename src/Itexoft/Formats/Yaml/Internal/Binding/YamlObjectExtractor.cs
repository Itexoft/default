// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Globalization;
using System.Numerics;
using Itexoft.Extensions;
using Itexoft.Formats.Yaml.Internal.Core;
using Itexoft.Formats.Yaml.Internal.Presentation;
using Itexoft.Formats.Yaml.Internal.Representation;
using Itexoft.Formats.Yaml.Internal.Session;

namespace Itexoft.Formats.Yaml.Internal.Binding;

internal static class YamlObjectExtractor
{
    public static RepresentationGraph Extract(YamlSession session, object? value, Type type)
    {
        var nodes = new Dictionary<SemanticNodeId, RepresentationNode>();
        var counts = new Dictionary<SemanticNodeId, int>();
        var seen = new Dictionary<object, SemanticNodeId>(ReferenceEqualityComparer.Instance);
        var context = new ExtractorContext(session, nodes, counts, seen);
        var rootId = context.Extract(value, type);

        return new(rootId, nodes, counts, false);
    }

    private sealed class ExtractorContext(
        YamlSession session,
        IDictionary<SemanticNodeId, RepresentationNode> nodes,
        IDictionary<SemanticNodeId, int> counts,
        IDictionary<object, SemanticNodeId> seen)
    {
        public SemanticNodeId Extract(object? value, Type declaredType)
        {
            if (value is null)
                return this.AddScalar(YamlTags.Null, string.Empty, ScalarStyle.Plain, new(ScalarCanonicalKind.Null, "null"), true);

            var type = Nullable.GetUnderlyingType(declaredType) ?? declaredType;

            if (type == typeof(object))
                type = value.GetType();

            if (!type.IsValueType)
            {
                if (seen.TryGetValue(value, out var existing))
                {
                    counts[existing] = counts.TryGetValue(existing, out var count) ? count + 1 : 2;

                    return existing;
                }
            }

            var descriptor = session.Profile.TypeDescriptorProvider.GetDescriptor(type);

            if (descriptor.Kind == YamlTypeKind.Scalar)
                return this.ExtractScalar(value, type);

            if (descriptor.Kind == YamlTypeKind.Array || descriptor.Kind == YamlTypeKind.List)
                return this.ExtractSequence(value, descriptor);

            if (descriptor.Kind == YamlTypeKind.Dictionary)
                return this.ExtractDictionary(value, descriptor);

            return this.ExtractObject(value, descriptor);
        }

        private SemanticNodeId ExtractScalar(object value, Type type)
        {
            if (type == typeof(string))
            {
                var text = (string)value;
                var style = text.Contains('\n') ? ScalarStyle.Literal : ScalarStyle.Plain;

                return this.AddScalar(YamlTags.String, text, style, new(ScalarCanonicalKind.String, text));
            }

            if (type == typeof(bool))
            {
                var text = (bool)value ? "true" : "false";

                return this.AddScalar(YamlTags.Boolean, text, ScalarStyle.Plain, new(ScalarCanonicalKind.Boolean, text, (bool)value));
            }

            if (type.IsEnum)
            {
                var text = session.Profile.ScalarCodecProvider.TryGetCodec(type, out var codec)
                    ? codec!.Write(value, type)
                    : value.ToString() ?? string.Empty;

                var isNumeric = long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

                return this.AddScalar(
                    isNumeric ? YamlTags.Integer : YamlTags.String,
                    text,
                    ScalarStyle.Plain,
                    isNumeric
                        ? new(ScalarCanonicalKind.Integer, text, false, BigInteger.Parse(text, CultureInfo.InvariantCulture))
                        : new(ScalarCanonicalKind.String, text));
            }

            if (session.Profile.ScalarCodecProvider.TryGetCodec(type, out var typedCodec))
            {
                var text = typedCodec!.Write(value, type);

                return this.AddScalar(
                    YamlTags.String,
                    text,
                    text.Contains('\n') ? ScalarStyle.Literal : ScalarStyle.Plain,
                    new(ScalarCanonicalKind.String, text));
            }

            if (TryExtractNumeric(type, value, out var tag, out var textValue, out var canonical))
                return this.AddScalar(tag, textValue, ScalarStyle.Plain, canonical);

            throw session.Diagnostics.CreateException("YAML500", YamlException.Phase.Bind, $"Unsupported scalar extraction type '{type}'.");
        }

        private SemanticNodeId ExtractSequence(object value, TypeDescriptor descriptor)
        {
            var semanticId = session.Ids.NextSemantic();
            counts[semanticId] = 1;

            if (!descriptor.Type.IsValueType)
                seen[value] = semanticId;

            var elementType = descriptor.ElementType.Required();
            var items = new List<SemanticNodeId>();

            foreach (var item in (IEnumerable)value)
                items.Add(this.Extract(item, elementType));

            nodes[semanticId] = new RepresentationSequenceNode(semanticId, new(RepresentationTagStateKind.Resolved, YamlTags.Sequence), items);

            return semanticId;
        }

        private SemanticNodeId ExtractDictionary(object value, TypeDescriptor descriptor)
        {
            var semanticId = session.Ids.NextSemantic();
            counts[semanticId] = 1;

            if (!descriptor.Type.IsValueType)
                seen[value] = semanticId;

            var keyType = descriptor.KeyType.Required();
            var valueType = descriptor.ValueType.Required();
            var entries = new List<RepresentationMappingEntry>();

            if (value is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                    entries.Add(new(this.Extract(entry.Key, keyType), this.Extract(entry.Value, valueType)));
            }
            else
            {
                foreach (var pair in (IEnumerable)value)
                {
                    var pairType = pair.GetType();
                    var key = pairType.GetProperty("Key")?.GetValue(pair);
                    var itemValue = pairType.GetProperty("Value")?.GetValue(pair);
                    entries.Add(new(this.Extract(key, keyType), this.Extract(itemValue, valueType)));
                }
            }

            nodes[semanticId] = new RepresentationMappingNode(semanticId, new(RepresentationTagStateKind.Resolved, YamlTags.Mapping), entries);

            return semanticId;
        }

        private SemanticNodeId ExtractObject(object value, TypeDescriptor descriptor)
        {
            var semanticId = session.Ids.NextSemantic();
            counts[semanticId] = 1;
            seen[value] = semanticId;
            var entries = new List<RepresentationMappingEntry>();

            foreach (var member in descriptor.Members)
            {
                if (!member.CanRead || member.IsExtensionData)
                    continue;

                var keyId = this.AddScalar(YamlTags.String, member.Label, ScalarStyle.Plain, new(ScalarCanonicalKind.String, member.Label));
                var raw = member.Accessor.Get(value);
                var valueId = this.Extract(raw, member.MemberType);
                entries.Add(new(keyId, valueId));
            }

            nodes[semanticId] = new RepresentationMappingNode(semanticId, new(RepresentationTagStateKind.Resolved, YamlTags.Mapping), entries);

            return semanticId;
        }

        private SemanticNodeId AddScalar(
            string tag,
            string logicalText,
            ScalarStyle style,
            ScalarCanonicalForm canonical,
            bool isImplicitNull = false)
        {
            var semanticId = session.Ids.NextSemantic();
            counts[semanticId] = 1;

            nodes[semanticId] = new RepresentationScalarNode(
                semanticId,
                new(RepresentationTagStateKind.Resolved, tag),
                logicalText,
                style,
                canonical,
                isImplicitNull);

            return semanticId;
        }

        private static bool TryExtractNumeric(Type type, object value, out string tag, out string text, out ScalarCanonicalForm canonical)
        {
            tag = string.Empty;
            text = string.Empty;
            canonical = default;

            switch (Type.GetTypeCode(type))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                {
                    tag = YamlTags.Integer;
                    text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
                    canonical = new(ScalarCanonicalKind.Integer, text, false, BigInteger.Parse(text, CultureInfo.InvariantCulture));

                    return true;
                }
                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                {
                    tag = YamlTags.Float;

                    text = type == typeof(float) ? ((float)value).ToString("R", CultureInfo.InvariantCulture) :
                        type == typeof(double) ? ((double)value).ToString("R", CultureInfo.InvariantCulture) :
                        ((decimal)value).ToString(CultureInfo.InvariantCulture);

                    canonical = new(ScalarCanonicalKind.Float, text, false, default, Convert.ToDouble(value, CultureInfo.InvariantCulture));

                    return true;
                }
                default:
                    return false;
            }
        }
    }
}
