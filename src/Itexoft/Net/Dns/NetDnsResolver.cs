// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Sockets;
using Itexoft.Threading;
using Itexoft.Threading.Parallelism;

namespace Itexoft.Net.Dns;

public interface INetDnsResolver
{
    NetIpAddress[] Resolve(NetHost host, CancelToken cancelToken = default);
}

public static class NetDnsResolver
{
    private const int dnsPort = 53;
    private const int dohPort = 443;
    private const int dotPort = 853;

    private static readonly NetIpEndpoint[] mullvadIp =
    [
        new(NetIpAddress.Parse("194.242.2.2"), dnsPort, NetProtocol.Udp), new(NetIpAddress.Parse("2a07:e340::2"), dnsPort),
    ];

    private static readonly NetIpEndpoint[] cloudflareIp =
    [
        new(NetIpAddress.Parse("1.1.1.1"), dnsPort),
        new(NetIpAddress.Parse("1.0.0.1"), dnsPort),
        new(NetIpAddress.Parse("2606:4700:4700::1111"), dnsPort),
        new(NetIpAddress.Parse("2606:4700:4700::1001"), dnsPort),
    ];

    private static readonly NetIpEndpoint[] quad9Ip =
    [
        new(new(9, 9, 9, 9), dnsPort),
        new(NetIpAddress.Parse("149.112.112.112"), dnsPort),
        new(NetIpAddress.Parse("2620:fe::fe"), dnsPort),
        new(NetIpAddress.Parse("2620:fe::9"), dnsPort),
    ];

    private static readonly NetEndpoint[] mullvadDot = [new("dns.mullvad.net", dotPort, NetProtocol.Tcp)];
    private static readonly NetEndpoint[] cloudflareDot = [new("one.one.one.one", dotPort, NetProtocol.Tcp)];
    private static readonly NetEndpoint[] quad9Dot = [new("dns.quad9.net", dotPort, NetProtocol.Tcp)];

    private static readonly NetEndpoint[] mullvadDoh = [new("dns.mullvad.net", dohPort, NetProtocol.Tcp)];
    private static readonly NetEndpoint[] cloudflareDoh = [new("cloudflare-dns.com", dohPort, NetProtocol.Tcp)];
    private static readonly NetEndpoint[] quad9Doh = [new("dns.quad9.net", dohPort, NetProtocol.Tcp)];

    public static INetDnsResolver Default { get; } = new NetDnsClient(
        ValuePolicy.PassThrough,
        RetryPolicy.None,
        [..mullvadIp, ..quad9Ip, ..cloudflareIp]);

    public static INetDnsResolver System { get; } = new SystemNetDnsResolver();

    private sealed class SystemNetDnsResolver : INetDnsResolver
    {
        public NetIpAddress[] Resolve(NetHost host, CancelToken cancelToken = default)
        {
            cancelToken.ThrowIf();

            if (host.TryParseIp(out var ip))
                return [ip];

            var addresses = global::System.Net.Dns.GetHostAddresses(host.Host, host.AddressFamily.ToBclAddressFamily());
            var result = new NetIpAddress[addresses.Length];

            for (var i = 0; i < result.Length; i++)
                result[i] = addresses[i];

            return result;
        }
    }

    extension(INetDnsResolver resolver)
    {
        public NetIpEndpoint Resolve(NetEndpoint endpoint, CancelToken cancelToken = default)
        {
            var host = endpoint.Host;

            if (host.TryParseIp(out var ip))
            {
                if (host.AddressFamily != NetAddressFamily.Unspecified && ip.AddressFamily != host.AddressFamily)
                    throw new NetSocketException(NetSocketError.AddressFamilyNotSupported);

                return new NetIpEndpoint(ip, endpoint.Port, endpoint.Protocol);
            }

            var resolved = resolver.Resolve(endpoint.Host, cancelToken);

            if (Parallelism.Any(
                    resolved.Select(x => new NetIpEndpoint(x, endpoint.Port, endpoint.Protocol)),
                    NetSocket.Probe,
                    out var result,
                    cancelToken))
                return result;

            throw new NetEndpointConnectException(endpoint);
        }
    }
}
