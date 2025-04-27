// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Extensions;
using Itexoft.IO;

namespace Itexoft.Net.Http;

public readonly struct NetHttpRequest(NetHttpMethod method, NetHttpPathQuery pathAndQuery, NetHttpVersion version, NetHttpHeaders headers)
{
    public NetHttpRequest(NetHttpMethod method, NetHttpPathQuery pathAndQuery) : this(method, pathAndQuery, NetHttpVersion.Version11, new()) { }

    public NetHttpRequest(NetHttpRequest request) : this(request.Method, request.PathAndQuery, request.HttpVersion, request.Headers)
    {
        this.Encoding = request.Encoding;
        this.Timeout = request.Timeout;
        this.ReceiveHeadersOnly = request.ReceiveHeadersOnly;
        this.SendBufferSize = request.SendBufferSize;
        this.ReceiveBufferSize = request.ReceiveBufferSize;
    }

    public IStreamRs<byte>? Content { get; init; }
    public NetCookieContainer? CookieContainer { get; init; }
    public NetHttpMethod Method { get; } = method;
    public NetHttpPathQuery PathAndQuery { get; init; } = pathAndQuery;
    public NetHttpHeaders Headers { get; init; } = headers.Required();
    public NetHttpVersion HttpVersion { get; init; } = version;

    public ConnectionType ConnectionType
    {
        get => this.Headers.ConnectionType switch
        {
            ConnectionType.None => this.HttpVersion >= NetHttpVersion.Version11 ? ConnectionType.KeepAlive : ConnectionType.None,
            _ => this.Headers.ConnectionType,
        };
        set => this.Headers.ConnectionType = value;
    }

    public long Length => this.Content?.Length ?? 0;

    public Encoding? Encoding { get; init; }
    public TimeSpan Timeout { get; init; } = System.Threading.Timeout.InfiniteTimeSpan;
    public bool ReceiveHeadersOnly { get; init; }

    public int SendBufferSize { get; init; } = 16 * 1024;
    public int ReceiveBufferSize { get; init; } = 16 * 1024;
}
