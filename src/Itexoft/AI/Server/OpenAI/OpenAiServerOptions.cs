// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.AI.OpenAI.Models;
using Itexoft.IO;
using Itexoft.Net;
using Itexoft.Threading;

namespace Itexoft.AI.Server.OpenAi;

public delegate bool OpenAiServerAuthorize(string apiKey, CancelToken cancelToken);

public delegate IStreamRw<char> OpenAiServerStreamFactory(CancelToken cancelToken);

public interface IOpenAiServerHandler
{
    OpenAiChatCompletionResponse CompleteChat(OpenAiChatCompletionRequest request, IStreamRw<char> stream, CancelToken cancelToken);

    IEnumerable<OpenAiChatCompletionResponseDelta> StreamChat(OpenAiChatCompletionRequest request, IStreamRw<char> stream, CancelToken cancelToken);

    OpenAiEmbeddingsResponse CreateEmbeddings(OpenAiEmbeddingsRequest request, IStreamRw<char> stream, CancelToken cancelToken);

    OpenAiModelsResponse GetModels(IStreamRw<char> stream, CancelToken cancelToken);
}

public readonly struct OpenAiServerOptions(NetIpEndpoint endpoint, OpenAiServerAuthorize? authorize = null)
{
    public OpenAiServerOptions() : this(default, null!) { }

    public NetIpEndpoint Endpoint { get; } = endpoint;

    public OpenAiServerAuthorize? Authorize { get; } = authorize;
}
