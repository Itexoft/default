// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text.Json.Serialization;
using Itexoft.Threading;

namespace Itexoft.AI.Api.OpenAI.Assembling;

public sealed class OpenAiInferenceAssemblyClient(
    JsonSerializerContext contractContext,
    OpenAiInferenceClient inferenceClient,
    OpenAiEmbeddingsClient embeddingsClient)
{
    private readonly JsonSerializerContext? contractContext = contractContext;
    private readonly OpenAiEmbeddingsClient? embeddingsClient = embeddingsClient;
    private readonly OpenAiInferenceClient? inferenceClient = inferenceClient;

    public T Request<T>(string input, CancelToken cancelToken = default) => throw new NotImplementedException();
}
