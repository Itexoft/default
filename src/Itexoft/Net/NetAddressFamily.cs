// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Sockets;

namespace Itexoft.Net;

public enum NetAddressFamily
{
    Unspecified = 0,
    InterNetwork = 2,
    InterNetworkV6 = 23,
}

internal static class NetAddressFamilyExtensions
{
    public static NetAddressFamily ToNetAddressFamily(this AddressFamily family) => family switch
    {
        AddressFamily.InterNetwork => NetAddressFamily.InterNetwork,
        AddressFamily.InterNetworkV6 => NetAddressFamily.InterNetworkV6,
        _ => NetAddressFamily.Unspecified,
    };

    public static AddressFamily ToBclAddressFamily(this NetAddressFamily family) => family switch
    {
        NetAddressFamily.InterNetwork => AddressFamily.InterNetwork,
        NetAddressFamily.InterNetworkV6 => AddressFamily.InterNetworkV6,
        _ => AddressFamily.Unspecified,
    };
}
