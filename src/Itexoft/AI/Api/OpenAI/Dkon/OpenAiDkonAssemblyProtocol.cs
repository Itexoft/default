// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;
using DkonContextBase = Itexoft.Formats.Dkon.DkonFormat;

namespace Itexoft.AI.Api.OpenAI.Dkon;

internal enum DkonAssemblyUnitKind
{
    LeafBatch,
    CollectionCount,
    DictionaryKeys,
}

internal enum DkonAssemblyFrontierKind
{
    Leaf,
    CollectionCount,
    DictionaryKeys,
}

internal sealed class DkonAssemblyPlanRequest
{
    public DkonAssemblyFrontierItem[] Frontier { get; set; } = [];
    public DkonAssemblyResolvedFact[] ResolvedFacts { get; set; } = [];
}

internal sealed class DkonAssemblyPlanResponse
{
    public Dictionary<string, DkonAssemblyPlanUnit> Units { get; set; } = [];
}

internal sealed class DkonAssemblyPlanUnit
{
    public string Kind { get; set; } = string.Empty;
    public string[] Paths { get; set; } = [];
    public string[] Dependencies { get; set; } = [];
}

internal sealed class LeafValueRequest
{
    public DkonAssemblyField Target { get; set; } = new();
    public DkonAssemblyResolvedFact[] ResolvedFacts { get; set; } = [];
}

internal sealed class LeafValueResponse
{
    public string DkonValue { get; set; } = string.Empty;
}

internal sealed class CollectionCountRequest
{
    public DkonAssemblyFrontierItem Target { get; set; } = new();
    public DkonAssemblyResolvedFact[] ResolvedFacts { get; set; } = [];
}

internal sealed class CollectionCountResponse
{
    public string Count { get; set; } = string.Empty;
}

internal sealed class DictionaryKeysRequest
{
    public DkonAssemblyFrontierItem Target { get; set; } = new();
    public DkonAssemblyResolvedFact[] ResolvedFacts { get; set; } = [];
}

internal sealed class DictionaryKeysResponse
{
    public DkonScalarToken[] Keys { get; set; } = [];
}

internal sealed class DkonScalarToken
{
    public string DkonValue { get; set; } = string.Empty;
}

internal sealed class DkonAssemblyFrontierItem
{
    public string PathId { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public DkonAssemblyFrontierKind Kind { get; set; }
    public string Contract { get; set; } = string.Empty;
}

internal sealed class DkonAssemblyField
{
    public string PathId { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public string Contract { get; set; } = string.Empty;
}

internal sealed class DkonAssemblyResolvedFact
{
    public string PathId { get; set; } = string.Empty;
    public string DisplayPath { get; set; } = string.Empty;
    public string DkonValue { get; set; } = string.Empty;
}

[JsonSerializable(typeof(DkonAssemblyPlanRequest)), JsonSerializable(typeof(DkonAssemblyPlanResponse)), JsonSerializable(typeof(LeafValueRequest)),
 JsonSerializable(typeof(LeafValueResponse)), JsonSerializable(typeof(CollectionCountRequest)), JsonSerializable(typeof(CollectionCountResponse)),
 JsonSerializable(typeof(DictionaryKeysRequest)), JsonSerializable(typeof(DictionaryKeysResponse)),
 JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
internal sealed partial class DkonAssemblyProtocolContext : DkonContextBase;
