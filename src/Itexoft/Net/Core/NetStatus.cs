// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net;
using System.Net.NetworkInformation;
using Itexoft.Net.Sockets;
using Itexoft.Threading;
using Itexoft.Threading.ControlFlow;

namespace Itexoft.Net.Core;

public static class NetStatus
{
    private const int refreshIntervalMilliseconds = 1000;
    private static readonly NetIpAddress ipv4Probe = NetIpAddress.Parse("192.0.2.1"); // TEST-NET-1
    private static readonly NetIpAddress ipv6Probe = NetIpAddress.Parse("2001:db8::1"); // Documentation prefix

    private static InvokeGateLatch<AvailabilitySnapshot> availabilitySnapshot = InvokeGate.Latch(
        static () => GetAvailabilitySnapshot(default),
        TimeSpan.FromMilliseconds(refreshIntervalMilliseconds));

    public static bool Ipv4Available => availabilitySnapshot.Invoke().Ipv4Available;

    public static bool Ipv6Available => availabilitySnapshot.Invoke().Ipv6Available;

    private static AvailabilitySnapshot GetAvailabilitySnapshot(CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        var ipv4Usable = NetSocket.OsSupportsIPv4 && HasUsableInterface(NetAddressFamily.InterNetwork, cancelToken);

        var ipv6Usable = NetSocket.OsSupportsIPv6 && HasUsableInterface(NetAddressFamily.InterNetworkV6, cancelToken);

        if (ipv4Usable)
            ipv4Usable = ProbeRoute(ipv4Probe, cancelToken);

        if (ipv6Usable)
            ipv6Usable = ProbeRoute(ipv6Probe, cancelToken);

        return new(ipv4Usable, ipv6Usable);
    }

    private static bool HasUsableInterface(NetAddressFamily addressFamily, CancelToken cancelToken)
    {
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                cancelToken.ThrowIf();

                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                    continue;

                var props = nic.GetIPProperties();

                var hasGateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily.ToNetAddressFamily() == addressFamily
                                                                 && IsGlobalUnicast(g.Address));

                if (!hasGateway)
                    continue;

                if (props.UnicastAddresses.Any(ua => ua.Address.AddressFamily.ToNetAddressFamily() == addressFamily && IsGlobalUnicast(ua.Address)))
                    return true;
            }
        }
        catch
        {
            // Ignore enumeration errors; fallback handled by caller.
        }

        return false;
    }

    private static bool ProbeRoute(NetIpAddress target, CancelToken cancelToken)
    {
        try
        {
            return NetSocket.Probe(new(target, 9, NetProtocol.Udp), cancelToken);
        }
        catch
        {
            // On unexpected errors treat as unavailable to stay conservative.
            return false;
        }
    }

    private static bool IsGlobalUnicast(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return false;

        if (address.AddressFamily.ToNetAddressFamily() == NetAddressFamily.InterNetworkV6)
            return !(address.IsIPv6LinkLocal || address.IsIPv6Multicast);

        return true;
    }

    private readonly struct AvailabilitySnapshot(bool ipv4Available, bool ipv6Available)
    {
        public bool Ipv4Available { get; } = ipv4Available;

        public bool Ipv6Available { get; } = ipv6Available;
    }
}
