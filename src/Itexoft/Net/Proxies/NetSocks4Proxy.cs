// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using Itexoft.IO;
using Itexoft.Net.Core;
using Itexoft.Threading;

namespace Itexoft.Net.Proxies;

public sealed class NetSocks4Proxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    public NetSocks4Proxy(NetHost host, NetPort port, NetCredential credential = default) : this(new(host, port, NetProtocol.Tcp), credential) { }

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public INetStream Connect(INetStream stream, NetEndpoint endpoint, CancelToken cancelToken = default)
    {
        this.SendConnect(stream, endpoint, cancelToken);
        this.ReadResponse(stream, cancelToken);

        return stream;
    }

    private void SendConnect(INetStream stream, NetEndpoint endpoint, CancelToken cancelToken)
    {
        var user = this.Credential.UserName ?? string.Empty;
        var userLength = user.Length == 0 ? 0 : Encoding.ASCII.GetByteCount(user);

        int port;
        byte[] addressBytes;
        byte[]? hostBytes = null;
        var useSocks4A = false;

        if (endpoint.TryCreate(out var ipep, NetProtocol.Tcp) && ipep.AddressFamily == NetAddressFamily.InterNetwork)
        {
            port = ipep.Port;
            addressBytes = ipep.IpAddress.GetBytes();
        }
        else
        {
            port = endpoint.Port;
            useSocks4A = true;
            addressBytes = new byte[4]; // 0.0.0.1 marker will be set
            hostBytes = Encoding.ASCII.GetBytes(endpoint.Host);
        }

        var payloadLength = 8 + userLength + 1 + (useSocks4A && hostBytes is not null ? hostBytes.Length + 1 : 0);
        var buffer = ArrayPool<byte>.Shared.Rent(payloadLength);

        try
        {
            buffer[0] = 0x04;
            buffer[1] = 0x01; // CONNECT
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2, 2), (ushort)port);

            if (useSocks4A)
            {
                buffer[4] = 0x00;
                buffer[5] = 0x00;
                buffer[6] = 0x00;
                buffer[7] = 0x01; // SOCKS4a marker
            }
            else
                addressBytes.CopyTo(buffer.AsSpan(4, 4));

            var offset = 8;

            if (userLength > 0)
            {
                Encoding.ASCII.GetBytes(user, buffer.AsSpan(offset, userLength));
                offset += userLength;
            }

            buffer[offset++] = 0x00; // user terminator

            if (useSocks4A && hostBytes is { Length: > 0 })
            {
                hostBytes.CopyTo(buffer.AsSpan(offset));
                offset += hostBytes.Length;
                buffer[offset++] = 0x00; // host terminator
            }

            stream.Write(buffer.AsSpan(0, offset), cancelToken);
            stream.Flush(cancelToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void ReadResponse(IStreamRw<byte> stream, CancelToken cancelToken)
    {
        var response = ArrayPool<byte>.Shared.Rent(8);

        try
        {
            stream.ReadExact(response.AsSpan(0, 8), cancelToken);

            if (response[0] != 0x00 || response[1] != 0x5A)
                throw new IOException($"SOCKS4 connect failed with code 0x{response[1]:X2}.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(response);
        }
    }
}
