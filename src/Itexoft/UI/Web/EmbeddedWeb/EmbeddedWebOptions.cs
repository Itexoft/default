// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Web.EmbeddedWeb;

public sealed class EmbeddedWebOptions
{
    private readonly List<string> defaultFileNames = ["index.html"];

    public IList<string> DefaultFileNames => this.defaultFileNames;

    public bool EnableSpaFallback { get; set; } = true;

    public string? SpaFallbackFile { get; set; }

    public TimeSpan? StaticFilesCacheDuration { get; set; }

    public bool EnableDirectoryBrowsing { get; set; }

    public Action<EmbeddedWebResponseContext>? OnPrepareResponse { get; set; }

    public object? RpcHandler { get; set; }

    public string RpcPathPrefix { get; set; } = "/rpc";

    public bool EnableRpcClientScriptInjection { get; set; }

    public EmbeddedWebRequestHandler? RequestHandler { get; set; }

    public EmbeddedWebWebSocketRequestHandler? WebSocketHandler { get; set; }

    public bool EnableDebugResourceTicks { get; set; }

    public long? DebugResourceTicks { get; set; }
}
