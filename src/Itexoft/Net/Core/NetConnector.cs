// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public abstract class NetConnector(LString kind) : INetConnector
{
    public LString Kind { get; } = kind;

    public INetStream Connect(NetEndpoint endpoint, CancelToken cancelToken = default)
    {
        var ipEndpoint = endpoint.Resolve(cancelToken);

        return this.Connect(ipEndpoint, cancelToken);
    }

    public abstract INetStream Connect(NetIpEndpoint endpoint, CancelToken cancelToken = default);
}
