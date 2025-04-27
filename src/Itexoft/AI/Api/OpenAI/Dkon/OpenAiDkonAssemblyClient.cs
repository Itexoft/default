// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using Itexoft.Formats.Dkon;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI.Dkon;

internal interface IDkonAssemblyTransport
{
    TResponse GetChatCompletion<TResponse>(
        string prompt,
        object request,
        DkonInferenceClient.Settings settings,
        out string dkon,
        CancelToken cancelToken = default);
}

internal sealed class DkonAssemblyTransportClient(OpenAiInferenceClient inferenceClient) : IDkonAssemblyTransport
{
    private readonly DkonInferenceClient protocolClient = new(DkonAssemblyProtocolContext.Default, inferenceClient);

    public TResponse GetChatCompletion<TResponse>(
        string prompt,
        object request,
        DkonInferenceClient.Settings settings,
        out string dkon,
        CancelToken cancelToken = default) =>
        this.protocolClient.GetChatCompletion<TResponse>(prompt, request, settings, false, out dkon, cancelToken);
}

internal sealed class DkonAssemblyClient(DkonFormat targetFormat, IDkonAssemblyTransport transport)
{
    private readonly DkonFormat targetFormat = targetFormat;
    private readonly IDkonAssemblyTransport transport = transport;

    public TResponse GetChatCompletion<TResponse>(string prompt, object request, Settings settings, CancelToken cancelToken = default) =>
        this.GetChatCompletion<TResponse>(prompt, request, settings, out _, cancelToken);

    public TResponse GetChatCompletion<TResponse>(
        string prompt,
        object request,
        Settings settings,
        out string dkon,
        CancelToken cancelToken = default)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ArgumentNullException.ThrowIfNull(request);
        Validate(settings);

        var inputDkon = this.targetFormat.Serialize(request, request.GetType(), true);
        var graph = new DkonAssemblyGraph(this.targetFormat, typeof(TResponse));
        var rootContract = this.targetFormat.GetContract(typeof(TResponse), out _);

        for (var pass = 0; pass < settings.PlannerPassLimit; pass++)
        {
            var frontier = graph.GetFrontier();

            if (frontier.Length == 0)
                return this.FinalizeResult<TResponse>(graph.Materialize(), out dkon);

            var resolvedFacts = graph.GetResolvedFacts();
            var units = this.RequestPlan(prompt, inputDkon, rootContract, frontier, resolvedFacts, settings, cancelToken);

            for (var i = 0; i < units.Length; i++)
                this.ExecuteUnit(units[i], prompt, inputDkon, rootContract, frontier, graph, settings, cancelToken);
        }

        throw new InvalidOperationException($"DKON assembly exceeded the planner pass limit {settings.PlannerPassLimit}.");
    }

    private void ExecuteUnit(
        DkonAssemblyPlanUnit unit,
        string prompt,
        string inputDkon,
        string rootContract,
        IReadOnlyList<DkonAssemblyFrontierItem> frontier,
        DkonAssemblyGraph graph,
        Settings settings,
        CancelToken cancelToken)
    {
        switch (ParseUnitKind(unit.Kind))
        {
            case DkonAssemblyUnitKind.LeafBatch:
                this.ExecuteLeafBatch(unit, prompt, inputDkon, rootContract, frontier, graph, settings, cancelToken);

                return;
            case DkonAssemblyUnitKind.CollectionCount:
                this.ExecuteCollectionCount(unit, prompt, inputDkon, rootContract, frontier, graph, settings, cancelToken);

                return;
            case DkonAssemblyUnitKind.DictionaryKeys:
                this.ExecuteDictionaryKeys(unit, prompt, inputDkon, rootContract, frontier, graph, settings, cancelToken);

                return;
            default:
                throw new InvalidOperationException($"Unsupported assembly unit kind '{unit.Kind}'.");
        }
    }

    private void ExecuteLeafBatch(
        DkonAssemblyPlanUnit unit,
        string prompt,
        string inputDkon,
        string rootContract,
        IReadOnlyList<DkonAssemblyFrontierItem> frontier,
        DkonAssemblyGraph graph,
        Settings settings,
        CancelToken cancelToken)
    {
        var fields = new DkonAssemblyField[unit.Paths.Length];

        for (var i = 0; i < unit.Paths.Length; i++)
        {
            var item = FindFrontier(frontier, unit.Paths[i]);

            fields[i] = new DkonAssemblyField
            {
                PathId = item.PathId,
                DisplayPath = item.DisplayPath,
                Contract = item.Contract,
            };
        }

        this.ExecuteLeafBatch(prompt, inputDkon, rootContract, graph, settings, cancelToken, fields);
    }

    private void ExecuteLeafBatch(
        string prompt,
        string inputDkon,
        string rootContract,
        DkonAssemblyGraph graph,
        Settings settings,
        CancelToken cancelToken,
        DkonAssemblyField[] fields)
    {
        for (var i = 0; i < fields.Length; i++)
            this.ExecuteLeafValue(prompt, inputDkon, rootContract, graph, settings, cancelToken, fields[i]);
    }

    private void ExecuteLeafValue(
        string prompt,
        string inputDkon,
        string rootContract,
        DkonAssemblyGraph graph,
        Settings settings,
        CancelToken cancelToken,
        DkonAssemblyField target)
    {
        var request = new LeafValueRequest
        {
            Target = target,
            ResolvedFacts = graph.GetResolvedFacts(),
        };

        this.RequestWithRetry(
            DkonAssemblyPrompts.LeafValue(inputDkon, target.DisplayPath, target.Contract),
            request,
            settings,
            settings.ExtractionRepairPassLimit,
            static (adapter, promptText, currentRequest, innerSettings, out dkon, cancelToken) => adapter.GetChatCompletion<LeafValueResponse>(
                promptText,
                currentRequest,
                innerSettings,
                out dkon,
                cancelToken),
            (response, responseDkon) => ValidateLeafValue(response, responseDkon, target.PathId, graph, this.targetFormat),
            cancelToken);
    }

    private void ExecuteCollectionCount(
        DkonAssemblyPlanUnit unit,
        string prompt,
        string inputDkon,
        string rootContract,
        IReadOnlyList<DkonAssemblyFrontierItem> frontier,
        DkonAssemblyGraph graph,
        Settings settings,
        CancelToken cancelToken)
    {
        var target = FindFrontier(frontier, unit.Paths[0]);

        var request = new CollectionCountRequest
        {
            Target = target,
            ResolvedFacts = graph.GetResolvedFacts(),
        };

        this.RequestWithRetry(
            DkonAssemblyPrompts.CollectionCount(prompt, inputDkon, target.DisplayPath),
            request,
            settings,
            settings.ExtractionRepairPassLimit,
            static (adapter, promptText, currentRequest, innerSettings, out dkon, cancelToken) => adapter.GetChatCompletion<CollectionCountResponse>(
                promptText,
                currentRequest,
                innerSettings,
                out dkon,
                cancelToken),
            (response, responseDkon) => ValidateCount(response, responseDkon, target, graph, settings.CollectionExpansionLimit, this.targetFormat),
            cancelToken);
    }

    private void ExecuteDictionaryKeys(
        DkonAssemblyPlanUnit unit,
        string prompt,
        string inputDkon,
        string rootContract,
        IReadOnlyList<DkonAssemblyFrontierItem> frontier,
        DkonAssemblyGraph graph,
        Settings settings,
        CancelToken cancelToken)
    {
        var target = FindFrontier(frontier, unit.Paths[0]);

        var dictionary = graph.GetNode(RemoveControllerSuffix(target.PathId)) as DkonAssemblyDictionaryNode
                         ?? throw new InvalidOperationException($"Dictionary path '{target.PathId}' does not resolve to a dictionary node.");

        var request = new DictionaryKeysRequest
        {
            Target = target,
            ResolvedFacts = graph.GetResolvedFacts(),
        };

        this.RequestWithRetry(
            DkonAssemblyPrompts.DictionaryKeys(
                prompt,
                inputDkon,
                target.DisplayPath,
                this.targetFormat.GetContract(dictionary.KeyType, out _),
                this.targetFormat.GetContract(dictionary.ValueType, out _)),
            request,
            settings,
            settings.ExtractionRepairPassLimit,
            static (adapter, promptText, currentRequest, innerSettings, out dkon, cancelToken) => adapter.GetChatCompletion<DictionaryKeysResponse>(
                promptText,
                currentRequest,
                innerSettings,
                out dkon,
                cancelToken),
            (response, responseDkon) => ValidateKeys(
                response,
                responseDkon,
                target,
                dictionary,
                settings.CollectionExpansionLimit,
                this.targetFormat),
            cancelToken);
    }

    private DkonAssemblyPlanUnit[] RequestPlan(
        string prompt,
        string inputDkon,
        string rootContract,
        DkonAssemblyFrontierItem[] frontier,
        DkonAssemblyResolvedFact[] resolvedFacts,
        Settings settings,
        CancelToken cancelToken)
    {
        if (TryBuildDerivedPlan(frontier, out var derived))
            return derived;

        var request = new DkonAssemblyPlanRequest
        {
            Frontier = frontier,
            ResolvedFacts = resolvedFacts,
        };

        DkonAssemblyPlanUnit[]? ordered = null;

        this.RequestWithRetry(
            DkonAssemblyPrompts.Plan(prompt, inputDkon, rootContract),
            request,
            settings,
            settings.PlanRepairPassLimit,
            static (adapter, promptText, currentRequest, innerSettings, out dkon, cancelToken) => adapter.GetChatCompletion<DkonAssemblyPlanResponse>(
                promptText,
                currentRequest,
                innerSettings,
                out dkon,
                cancelToken),
            (response, responseDkon) => ordered = ValidatePlan(response, responseDkon, frontier),
            cancelToken);

        return ordered ?? throw new InvalidOperationException("Validated assembly plan is missing.");
    }

    private static bool TryBuildDerivedPlan(IReadOnlyList<DkonAssemblyFrontierItem> frontier, out DkonAssemblyPlanUnit[] units)
    {
        var leafPaths = new List<string>(frontier.Count);
        var controllerUnits = new List<DkonAssemblyPlanUnit>(frontier.Count);

        for (var i = 0; i < frontier.Count; i++)
        {
            switch (frontier[i].Kind)
            {
                case DkonAssemblyFrontierKind.Leaf:
                    leafPaths.Add(frontier[i].PathId);

                    break;
                case DkonAssemblyFrontierKind.CollectionCount:
                    controllerUnits.Add(
                        new DkonAssemblyPlanUnit
                        {
                            Kind = DkonAssemblyUnitKind.CollectionCount.ToString(),
                            Paths = [frontier[i].PathId],
                            Dependencies = [],
                        });

                    break;
                case DkonAssemblyFrontierKind.DictionaryKeys:
                    controllerUnits.Add(
                        new DkonAssemblyPlanUnit
                        {
                            Kind = DkonAssemblyUnitKind.DictionaryKeys.ToString(),
                            Paths = [frontier[i].PathId],
                            Dependencies = [],
                        });

                    break;
            }
        }

        if (controllerUnits.Count == 0 && CanDeriveLeafBatch(leafPaths))
        {
            units =
            [
                new DkonAssemblyPlanUnit
                {
                    Kind = DkonAssemblyUnitKind.LeafBatch.ToString(),
                    Paths = [.. leafPaths],
                    Dependencies = [],
                },
            ];

            return true;
        }

        if (controllerUnits.Count == 0)
        {
            units = [];

            return false;
        }

        if (leafPaths.Count == 0)
        {
            units = [.. controllerUnits];

            return true;
        }

        units =
        [
            new DkonAssemblyPlanUnit
            {
                Kind = DkonAssemblyUnitKind.LeafBatch.ToString(),
                Paths = [.. leafPaths],
                Dependencies = [],
            },
            .. controllerUnits,
        ];

        return true;
    }

    private static bool CanDeriveLeafBatch(IReadOnlyList<string> leafPaths)
    {
        if (leafPaths.Count == 0)
            return false;

        for (var i = 0; i < leafPaths.Count; i++)
        {
            var path = leafPaths[i];

            if (path.IndexOf('[', StringComparison.Ordinal) >= 0 || path.IndexOf('{', StringComparison.Ordinal) >= 0)
                continue;

            return false;
        }

        return true;
    }

    private TResponse RequestWithRetry<TRequest, TResponse>(
        string prompt,
        TRequest initialRequest,
        Settings settings,
        int retryLimit,
        DkonAssemblyCall<TRequest, TResponse> invoke,
        Action<TResponse, string> validate,
        CancelToken cancelToken) where TRequest : class
    {
        Exception? lastError = null;

        for (var attempt = 0; attempt <= retryLimit; attempt++)
        {
            string responseDkon = string.Empty;

            try
            {
                var response = invoke(this.transport, prompt, initialRequest, settings.InnerSettings, out responseDkon, cancelToken);
                validate(response, responseDkon);

                return response;
            }
            catch (Exception ex) when (attempt < retryLimit)
            {
                lastError = ex;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"DKON assembly retry loop failed after {attempt.ToString(CultureInfo.InvariantCulture)} retry attempts.",
                    ex);
            }
        }

        throw new InvalidOperationException("DKON assembly retry loop exhausted unexpectedly.", lastError);
    }

    private TResponse FinalizeResult<TResponse>(object? materialized, out string dkon)
    {
        dkon = materialized is null ? string.Empty : this.targetFormat.Serialize(materialized, typeof(TResponse));

        return materialized is null ? default! : (TResponse)materialized;
    }

    private static void Validate(Settings settings)
    {
        if (settings.PlannerPassLimit <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "PlannerPassLimit must be greater than zero.");

        if (settings.PlanRepairPassLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "PlanRepairPassLimit must be non-negative.");

        if (settings.ExtractionRepairPassLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "ExtractionRepairPassLimit must be non-negative.");

        if (settings.CollectionExpansionLimit < 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "CollectionExpansionLimit must be non-negative.");
    }

    private static DkonAssemblyFrontierItem FindFrontier(IReadOnlyList<DkonAssemblyFrontierItem> frontier, string pathId)
    {
        for (var i = 0; i < frontier.Count; i++)
        {
            if (string.Equals(frontier[i].PathId, pathId, StringComparison.Ordinal))
                return frontier[i];
        }

        throw new InvalidOperationException($"Unknown frontier path '{pathId}'.");
    }

    private static string RemoveControllerSuffix(string pathId) =>
        pathId.EndsWith(DkonAssemblyPathSyntax.CountSuffix, StringComparison.Ordinal) ? pathId[..^DkonAssemblyPathSyntax.CountSuffix.Length] :
        pathId.EndsWith(DkonAssemblyPathSyntax.KeysSuffix, StringComparison.Ordinal) ? pathId[..^DkonAssemblyPathSyntax.KeysSuffix.Length] : pathId;

    private static DkonAssemblyPlanUnit[] ValidatePlan(
        DkonAssemblyPlanResponse response,
        string responseDkon,
        IReadOnlyList<DkonAssemblyFrontierItem> frontier)
    {
        var units = response.Units ?? [];

        if (units.Count == 0)
            throw new FormatException($"Assembly plan is empty.\n{responseDkon}");

        var frontierByPath = new Dictionary<string, DkonAssemblyFrontierItem>(frontier.Count, StringComparer.Ordinal);

        for (var i = 0; i < frontier.Count; i++)
            frontierByPath.Add(frontier[i].PathId, frontier[i]);

        var unitsById = new Dictionary<string, DkonAssemblyPlanUnit>(units.Count, StringComparer.Ordinal);
        var coverage = new Dictionary<string, int>(frontier.Count, StringComparer.Ordinal);

        foreach (var pair in units)
        {
            var unitId = pair.Key;
            var unit = pair.Value;
            var kind = ParseUnitKind(unit.Kind, responseDkon, unitId);

            if (string.IsNullOrWhiteSpace(unitId))
                throw new FormatException($"Assembly unit id is empty.\n{responseDkon}");

            if (!unitsById.TryAdd(unitId, unit))
                throw new FormatException($"Duplicate assembly unit id '{unitId}'.\n{responseDkon}");

            if (unit.Paths is null || unit.Paths.Length == 0)
                throw new FormatException($"Assembly unit '{unitId}' does not cover any paths.\n{responseDkon}");

            if ((kind == DkonAssemblyUnitKind.CollectionCount || kind == DkonAssemblyUnitKind.DictionaryKeys) && unit.Paths.Length != 1)
                throw new FormatException($"Assembly unit '{unitId}' must cover exactly one controller path.\n{responseDkon}");

            for (var pathIndex = 0; pathIndex < unit.Paths.Length; pathIndex++)
            {
                var path = unit.Paths[pathIndex];

                if (!frontierByPath.TryGetValue(path, out var item))
                    throw new FormatException($"Assembly unit '{unitId}' references unknown path '{path}'.\n{responseDkon}");

                if (!IsCompatible(item.Kind, kind))
                    throw new FormatException($"Assembly unit '{unitId}' uses kind '{unit.Kind}' for incompatible path '{path}'.\n{responseDkon}");

                coverage[path] = coverage.TryGetValue(path, out var count) ? count + 1 : 1;
            }
        }

        for (var i = 0; i < frontier.Count; i++)
        {
            if (!coverage.TryGetValue(frontier[i].PathId, out var count))
                throw new FormatException($"Assembly plan does not cover path '{frontier[i].PathId}'.\n{responseDkon}");

            if (count != 1)
            {
                throw new FormatException(
                    $"Assembly path '{frontier[i].PathId}' is covered {count.ToString(CultureInfo.InvariantCulture)} times.\n{responseDkon}");
            }
        }

        var indegree = new Dictionary<string, int>(units.Count, StringComparer.Ordinal);
        var dependents = new Dictionary<string, List<string>>(units.Count, StringComparer.Ordinal);

        foreach (var pair in unitsById)
        {
            indegree[pair.Key] = 0;
            dependents[pair.Key] = [];
        }

        foreach (var pair in unitsById)
        {
            var dependencies = pair.Value.Dependencies ?? [];

            for (var dependencyIndex = 0; dependencyIndex < dependencies.Length; dependencyIndex++)
            {
                var dependency = dependencies[dependencyIndex];

                if (!unitsById.ContainsKey(dependency))
                    throw new FormatException($"Assembly unit '{pair.Key}' depends on unknown unit '{dependency}'.\n{responseDkon}");

                if (string.Equals(dependency, pair.Key, StringComparison.Ordinal))
                    throw new FormatException($"Assembly unit '{pair.Key}' depends on itself.\n{responseDkon}");

                indegree[pair.Key]++;
                dependents[dependency].Add(pair.Key);
            }
        }

        var queue = new Queue<string>();

        foreach (var pair in indegree)
        {
            if (pair.Value == 0)
                queue.Enqueue(pair.Key);
        }

        var ordered = new List<DkonAssemblyPlanUnit>(units.Count);

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            ordered.Add(unitsById[id]);
            var next = dependents[id];

            for (var i = 0; i < next.Count; i++)
            {
                var dependentId = next[i];
                indegree[dependentId]--;

                if (indegree[dependentId] == 0)
                    queue.Enqueue(dependentId);
            }
        }

        if (ordered.Count != units.Count)
            throw new FormatException($"Assembly plan dependencies are cyclic.\n{responseDkon}");

        return [.. ordered];
    }

    private static bool IsCompatible(DkonAssemblyFrontierKind frontierKind, DkonAssemblyUnitKind unitKind) =>
        unitKind switch
        {
            DkonAssemblyUnitKind.LeafBatch => frontierKind == DkonAssemblyFrontierKind.Leaf,
            DkonAssemblyUnitKind.CollectionCount => frontierKind == DkonAssemblyFrontierKind.CollectionCount,
            DkonAssemblyUnitKind.DictionaryKeys => frontierKind == DkonAssemblyFrontierKind.DictionaryKeys,
            _ => false,
        };

    private static DkonAssemblyUnitKind ParseUnitKind(string? kind) =>
        Enum.TryParse(kind, true, out DkonAssemblyUnitKind parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new InvalidOperationException($"Unsupported assembly unit kind '{kind}'.");

    private static DkonAssemblyUnitKind ParseUnitKind(string? kind, string responseDkon, string unitId)
    {
        if (Enum.TryParse(kind, true, out DkonAssemblyUnitKind parsed) && Enum.IsDefined(parsed))
            return parsed;

        throw new FormatException($"Assembly unit '{unitId}' uses unknown kind '{kind}'.\n{responseDkon}");
    }

    private static object? ParseCarrierValue(string raw, Type targetType, DkonFormat format) =>
        targetType == typeof(string) ? raw : format.Deserialize(raw, targetType);

    private static void ValidateLeafValue(
        LeafValueResponse response,
        string responseDkon,
        string pathId,
        DkonAssemblyGraph graph,
        DkonFormat format) => ResolveLeafValue(pathId, response.DkonValue, responseDkon, graph, format);

    private static void ResolveLeafValue(string pathId, string dkonValue, string responseDkon, DkonAssemblyGraph graph, DkonFormat format)
    {
        var node = graph.GetNode(pathId) as DkonAssemblyScalarNode ?? throw new InvalidOperationException($"Path '{pathId}' is not a scalar node.");

        if (node.DeclaredType != typeof(string) && string.IsNullOrWhiteSpace(dkonValue))
            throw new FormatException($"Leaf value for '{pathId}' is empty.\n{responseDkon}");

        var value = ParseCarrierValue(dkonValue, node.DeclaredType, format);
        var canonical = value is null ? string.Empty : format.Serialize(value, node.DeclaredType);
        node.Resolve(value, canonical);
    }

    private static void ValidateCount(
        CollectionCountResponse response,
        string responseDkon,
        DkonAssemblyFrontierItem target,
        DkonAssemblyGraph graph,
        int expansionLimit,
        DkonFormat format)
    {
        if (!int.TryParse(response.Count, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            throw new FormatException($"Collection count response for '{target.PathId}' is not a canonical integer.\n{responseDkon}");

        if (count < 0)
            throw new FormatException($"Collection count response for '{target.PathId}' is negative.\n{responseDkon}");

        if (count > expansionLimit)
        {
            throw new FormatException(
                $"Collection count response for '{
                    target.PathId
                }' exceeds expansion limit {
                    expansionLimit.ToString(CultureInfo.InvariantCulture)
                }.\n{
                    responseDkon
                }");
        }

        var node = graph.GetNode(RemoveControllerSuffix(target.PathId)) as DkonAssemblyListNode
                   ?? throw new InvalidOperationException($"Path '{target.PathId}' is not a list node.");

        node.Expand(format, count);
    }

    private static void ValidateKeys(
        DictionaryKeysResponse response,
        string responseDkon,
        DkonAssemblyFrontierItem target,
        DkonAssemblyDictionaryNode dictionary,
        int expansionLimit,
        DkonFormat format)
    {
        var keys = response.Keys ?? [];

        if (keys.Length > expansionLimit)
        {
            throw new FormatException(
                $"Dictionary keys response for '{
                    target.PathId
                }' exceeds expansion limit {
                    expansionLimit.ToString(CultureInfo.InvariantCulture)
                }.\n{
                    responseDkon
                }");
        }

        var values = new List<(object? Value, string DkonValue)>(keys.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < keys.Length; i++)
        {
            var value = ParseCarrierValue(keys[i].DkonValue, dictionary.KeyType, format);
            var canonical = value is null ? string.Empty : format.Serialize(value, dictionary.KeyType);

            if (!seen.Add(canonical))
                throw new FormatException($"Dictionary keys response for '{target.PathId}' contains duplicate key '{canonical}'.\n{responseDkon}");

            values.Add((value, canonical));
        }

        dictionary.Expand(format, values);
    }

    public readonly record struct Settings(
        DkonInferenceClient.Settings InnerSettings,
        int PlannerPassLimit,
        int PlanRepairPassLimit,
        int ExtractionRepairPassLimit,
        int CollectionExpansionLimit);
}

file static class DkonAssemblyPrompts
{
    public static string Plan(string userPrompt, string inputDkon, string rootContract) => $$"""
                                                                                             You plan deterministic assembly units for DKON assembly.
                                                                                             Return only a DKON object that matches the DkonAssemblyPlanResponse schema.
                                                                                             Source task:
                                                                                             {{
                                                                                                 userPrompt
                                                                                             }}

                                                                                             Source input DKON:
                                                                                             {{
                                                                                                 inputDkon
                                                                                             }}

                                                                                             Root output contract:
                                                                                             {{
                                                                                                 rootContract
                                                                                             }}

                                                                                             Rules:
                                                                                             - Use only the provided frontier PathId values.
                                                                                             - Cover every frontier item exactly once.
                                                                                             - Never invent paths, fields, or units outside the schema.
                                                                                             - Units is a dictionary keyed by unit id. Put the unit id in the dictionary key, not inside the payload.
                                                                                             - Kind is a text field. Its value must be one of: LeafBatch, CollectionCount, DictionaryKeys.
                                                                                             - Repeat every path string literally inside Paths and Dependencies.
                                                                                             - LeafBatch may target only frontier items whose Kind is Leaf.
                                                                                             - CollectionCount may target only one frontier item whose Kind is CollectionCount.
                                                                                             - DictionaryKeys may target only one frontier item whose Kind is DictionaryKeys.
                                                                                             - Dependencies must reference only declared unit ids and must be acyclic.
                                                                                             - Prefer batching leaf paths that depend on the same local context, but never at the cost of invalid coverage.

                                                                                             """;

    public static string LeafValue(string inputDkon, string targetDisplayPath, string targetContract) => $$"""
          You fill one scalar field value for DKON assembly.
          Return only a DKON object that matches the LeafValueResponse schema.
          Source input DKON:
          {{
              inputDkon
          }}

          Target display path:
          {{
              targetDisplayPath
          }}

          Target value contract:
          {{
              targetContract
          }}

          Rules:
          - Target is the only unresolved output field.
          - Use the exact target display path as the semantic location.
          - ResolvedFacts are context only.
          - DkonValue is the only output target.
          - Put only the target field value text in DkonValue.
          - When the target display path names a keyed dictionary entry, return only that exact entry value.
          - Return DkonValue as a field inside the response object. A bare scalar token is invalid.
          - Never serialize the whole root object, any sibling fields, any collection body, or any dictionary body.
          - Do not return PathId, field names, sentinels, prose, or explanations.
          """;

    public static string CollectionCount(string userPrompt, string inputDkon, string targetDisplayPath) => $$"""
          You infer the deterministic item count for one open DKON collection.
          Return only a DKON object that matches the CollectionCountResponse schema.
          Source task:
          {{
              userPrompt
          }}

          Source input DKON:
          {{
              inputDkon
          }}

          Target display path:
          {{
              targetDisplayPath
          }}

          Rules:
          - Target is the only unresolved output field.
          - Count is the only output target.
          - Put only the canonical non-negative count text in Count.
          - Return Count as a field inside the response object. A bare scalar token is invalid.
          - Never serialize the collection body or any sibling fields.
          - Do not return PathId, field names, sentinels, prose, or explanations.
          """;

    public static string DictionaryKeys(string userPrompt, string inputDkon, string targetDisplayPath, string keyContract, string valueContract) =>
        $$"""
          You infer the deterministic key set for one open DKON dictionary.
          Return only a DKON object that matches the DictionaryKeysResponse schema.
          Source task:
          {{
              userPrompt
          }}

          Source input DKON:
          {{
              inputDkon
          }}

          Target display path:
          {{
              targetDisplayPath
          }}

          Dictionary key contract:
          {{
              keyContract
          }}

          Dictionary value contract:
          {{
              valueContract
          }}

          Rules:
          - Target is the only unresolved output field.
          - Keys is the only output target.
          - Put only key texts in Keys, one DkonValue per key.
          - Return Keys as a field inside the response object. A bare array is invalid.
          - Preserve a stable natural order for the keys.
          - Do not return duplicate keys.
          - Never serialize the dictionary body or any sibling fields.
          - Do not return PathId, field names, sentinels, prose, or explanations.
          """;
}

internal delegate TResponse DkonAssemblyCall<in TRequest, out TResponse>(
    IDkonAssemblyTransport adapter,
    string prompt,
    TRequest request,
    DkonInferenceClient.Settings settings,
    out string dkon,
    CancelToken cancelToken);
