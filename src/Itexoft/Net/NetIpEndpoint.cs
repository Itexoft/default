// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;

namespace Itexoft.Net;

public readonly struct NetIpEndpoint(NetIpAddress ipAddress, NetPort port, NetProtocol protocol = NetProtocol.Unknown)
{
    public NetIpAddress IpAddress { get; } = ipAddress;
    public NetPort Port { get; } = port;
    public NetProtocol Protocol { get; } = protocol;

    public NetAddressFamily AddressFamily => this.IpAddress.AddressFamily;

    public static implicit operator NetIpEndpoint((NetIpAddress IpAddress, NetPort Port, NetProtocol protocol) endpoint) =>
        new(endpoint.IpAddress, endpoint.Port, endpoint.protocol);

    public static bool TryParse(string value, NetProtocol socketProtocol, out NetIpEndpoint ipEndpoint)
    {
        if (!IPEndPoint.TryParse(value, out var point))
        {
            ipEndpoint = default;

            return false;
        }

        ipEndpoint = new(point.Address, point.Port, socketProtocol);

        return true;
    }
}
