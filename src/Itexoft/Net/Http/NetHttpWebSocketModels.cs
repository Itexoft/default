// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;

namespace Itexoft.Net.Http;

public delegate void NetHttpWebSocketSessionHandler(NetHttpWebSocket webSocket, CancelToken cancelToken);

public delegate NetHttpWebSocketSessionHandler? NetHttpWebSocketRequestHandler(NetHttpRequest request, CancelToken cancelToken);

public enum NetHttpWebSocketMessageType
{
    Text = 1,
    Close = 8,
}

public readonly record struct NetHttpWebSocketMessage(NetHttpWebSocketMessageType Type, string Text = "");
