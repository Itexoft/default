// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Security.Cryptography;
using System.Text;
using Itexoft.IO;
using Itexoft.Net.Http.Internal;
using Itexoft.Threading;

namespace Itexoft.Net.Http;

internal static class NetHttpWebSocketHandshake
{
    private const string connectionToken = "Upgrade";
    private const string upgradeToken = "websocket";
    private const string versionHeader = "Sec-WebSocket-Version";
    private const string keyHeader = "Sec-WebSocket-Key";
    private const string acceptHeader = "Sec-WebSocket-Accept";
    private const string webSocketVersion = "13";
    private const string webSocketGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

    public static bool TryValidate(NetHttpRequest request, out string acceptKey, out NetHttpStatus status)
    {
        acceptKey = string.Empty;
        status = NetHttpStatus.BadRequest;

        if (request.Method != NetHttpMethod.Get)
        {
            status = NetHttpStatus.MethodNotAllowed;

            return false;
        }

        if (request.Length != 0)
            return false;

        if (!request.Headers.TryGetValue(nameof(request.Headers.Connection), out var connection)
            || !NetHttpParsing.ContainsToken(connection.AsSpan(), connectionToken.AsSpan()))
            return false;

        if (!request.Headers.TryGetValue("Upgrade", out var upgrade) || !upgradeToken.Equals(upgrade, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!request.Headers.TryGetValue(versionHeader, out var version) || !webSocketVersion.Equals(version, StringComparison.Ordinal))
            return false;

        if (!request.Headers.TryGetValue(keyHeader, out var key) || string.IsNullOrWhiteSpace(key))
            return false;

        acceptKey = ComputeAcceptKey(key);
        status = NetHttpStatus.SwitchingProtocols;

        return true;
    }

    public static void WriteAccepted(IStreamW<byte> stream, NetHttpVersion version, string acceptKey, CancelToken cancelToken)
    {
        var headers = new NetHttpHeaders
        {
            Connection = connectionToken,
        };

        headers["Upgrade"] = upgradeToken;
        headers[acceptHeader] = acceptKey;
        NetHttpResponseWriter.Write(stream, new(version, NetHttpStatus.SwitchingProtocols, headers), cancelToken);
    }

    private static string ComputeAcceptKey(string key)
    {
        var source = Encoding.ASCII.GetBytes(string.Concat(key, webSocketGuid));
        var hash = SHA1.HashData(source);

        return Convert.ToBase64String(hash);
    }
}
