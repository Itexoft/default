// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Security;
using System.Security.Authentication;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public sealed class NetHttpsProxy(NetEndpoint endpoint, NetCredential credentials = default) : NetHttpProxy(endpoint, credentials)
{
    public NetHttpsProxy(NetHost host, NetPort port, NetCredential credentials = default) : this(new(host, port, NetProtocol.Tcp), credentials) { }

    public override INetStream Connect(INetStream stream, NetEndpoint endpoint, CancelToken cancelToken = default)
    {
        var sslStream = this.EnsureSslAsync(stream, false, cancelToken);

        return base.Connect(sslStream, endpoint, cancelToken);
    }

    private INetStream EnsureSslAsync(INetStream stream, bool leaveInnerOpen, CancelToken cancelToken)
    {
        var sslStream = new NetSslStream(stream, leaveInnerOpen);

        if (!sslStream.IsAuthenticated)
        {
            var proxyHost = (string)this.Endpoint.Host;

            var opts = new SslClientAuthenticationOptions
            {
                TargetHost = proxyHost,
                EnabledSslProtocols = SslProtocols.None,
                RemoteCertificateValidationCallback = static (_, _, _, _) => true,
            };

            if (string.IsNullOrWhiteSpace(opts.TargetHost))
                opts.TargetHost = proxyHost;

            sslStream.AuthenticateAsClient(opts, cancelToken);
        }

        return sslStream;
    }
}
