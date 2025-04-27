// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Net.Dns;
using Itexoft.Threading;

namespace Itexoft.Net;

public readonly struct NetEndpoint
{
    private readonly INetDnsResolver dnsResolver;
    public NetPort Port { get; }
    public NetHost Host { get; }
    public NetProtocol Protocol { get; }

    public NetEndpoint((NetHost host, NetPort port, NetProtocol protocol) endpoint) : this(endpoint.host, endpoint.port, endpoint.protocol) { }

    public NetEndpoint(NetHost host, NetPort port, NetProtocol protocol, INetDnsResolver? dnsResolver = null)
    {
        this.dnsResolver = dnsResolver ?? NetDnsResolver.System;
        this.Port = port;
        this.Host = host;
        this.Protocol = protocol;
    }

    public NetEndpoint(NetIpEndpoint endpoint) : this(endpoint.IpAddress, endpoint.Port, endpoint.Protocol) { }

    public NetEndpoint(NetIpAddress ipAddress, NetPort port, NetProtocol protocol)
    {
        this.dnsResolver = NetDnsResolver.System;
        this.Port = port;
        this.Host = ipAddress.ToString().Required();
        this.Protocol = protocol;
    }

    public static implicit operator NetEndpoint(NetIpEndpoint endpoint) => new(endpoint);

    public static implicit operator NetEndpoint((NetHost Host, NetPort Port, NetProtocol protocol) endpoint) =>
        new(endpoint.Host, endpoint.Port, endpoint.protocol);

    public static implicit operator NetEndpoint((NetIpAddress IpAddress, NetPort Port, NetProtocol protocol) endpoint) =>
        new(endpoint.IpAddress, endpoint.Port, endpoint.protocol);

    public bool TryCreate(out NetIpEndpoint ipEndpoint, NetProtocol protocol) => NetIpEndpoint.TryParse(this.ToString(), protocol, out ipEndpoint);

    public static bool TryParse(string value, NetProtocol protocol, out NetEndpoint ipEndpoint)
    {
        ipEndpoint = default;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        var span = value.AsSpan().Trim();

        ReadOnlySpan<char> hostSpan;
        ReadOnlySpan<char> portSpan;

        if (span[0] == '[')
        {
            var endBracketIndex = span.IndexOf(']');

            if (endBracketIndex <= 1)
                return false;

            hostSpan = span[1..endBracketIndex].Trim();

            if (hostSpan.Length == 0)
                return false;

            var rest = span[(endBracketIndex + 1)..].TrimStart();

            if (rest.Length < 2 || rest[0] != ':')
                return false;

            portSpan = rest[1..].Trim();

            if (portSpan.Length == 0)
                return false;
        }
        else
        {
            var colonIndex = span.LastIndexOf(':');

            if (colonIndex <= 0 || colonIndex == span.Length - 1)
                return false;

            if (span[..colonIndex].IndexOf(':') >= 0)
                return false;

            hostSpan = span[..colonIndex].Trim();
            portSpan = span[(colonIndex + 1)..].Trim();

            if (hostSpan.Length == 0 || portSpan.Length == 0)
                return false;
        }

        if (!tryParsePort(portSpan, out var portValue))
            return false;

        ipEndpoint = new(new string(hostSpan), new(portValue), protocol);

        return true;

        static bool tryParsePort(ReadOnlySpan<char> span, out ushort portValue)
        {
            portValue = 0;

            if (span.Length is 0 or > 5)
                return false;

            uint acc = 0;

            foreach (var c in span)
            {
                var d = (uint)(c - '0');

                if (d > 9)
                    return false;

                acc = acc * 10u + d;

                if (acc > 65535u)
                    return false;
            }

            portValue = (ushort)acc;

            return true;
        }
    }

    public override string ToString() => $"{this.Protocol}://{this.Host}:{this.Port}";

    public NetIpEndpoint Resolve(CancelToken cancelToken = default) => this.dnsResolver.Resolve(this, cancelToken);
}
