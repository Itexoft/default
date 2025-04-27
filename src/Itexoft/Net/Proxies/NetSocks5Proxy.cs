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

public readonly struct NetSocks5Proxy(NetEndpoint endpoint, NetCredential credential = default) : INetProxy
{
    private readonly NetProtocol protocol;
    public NetSocks5Proxy(NetHost host, NetPort port, NetCredential credential = default, NetProtocol protocol = NetProtocol.Tcp) : this(new(host, port, protocol), credential)
    {
        this.protocol = protocol;
    }

    private const byte authVersion = 0x01;
    private static readonly byte[] greetingCredential = [0x05, 0x02, 0x00, 0x02];
    private static readonly byte[] greeting = [0x05, 0x01, 0x00];

    public NetEndpoint Endpoint { get; } = endpoint;
    public NetCredential Credential { get; } = credential;

    public INetStream Connect(INetStream stream, NetEndpoint endpoint, CancelToken cancelToken = default)
    {
        this.PerformHandshake(stream, cancelToken);
        this.SendConnect(stream, endpoint, cancelToken);

        return stream;
    }

    private void PerformHandshake(INetStream stream, CancelToken cancelToken)
    {
        var credential = this.Credential;
        stream.Write(credential == default ? greeting : greetingCredential, cancelToken);
        stream.Flush(cancelToken);

        var response = ArrayPool<byte>.Shared.Rent(2);

        try
        {
            stream.ReadExact(response.AsSpan(0, 2), cancelToken);

            if (response[0] != 0x05)
                throw new IOException("SOCKS5 handshake failed.");

            var method = response[1];

            if (method == 0x02)
            {
                if (credential == default)
                    throw new IOException("SOCKS5 proxy requires authentication.");

                this.Authenticate(stream, credential, cancelToken);
            }
            else if (method != 0x00)
                throw new IOException($"SOCKS5 authentication method not accepted: 0x{method:X2}.");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(response);
        }
    }

    private void Authenticate(IStreamRw<byte> stream, NetCredential credential, CancelToken cancelToken)
    {
        var userBytes = Encoding.ASCII.GetBytes(credential.UserName ?? string.Empty);
        var passBytes = Encoding.ASCII.GetBytes(credential.Password ?? string.Empty);

        if (userBytes.Length > byte.MaxValue || passBytes.Length > byte.MaxValue)
            throw new IOException("SOCKS5 credentials are too long.");

        var length = 3 + userBytes.Length + passBytes.Length;
        var payload = ArrayPool<byte>.Shared.Rent(length);
        payload[0] = authVersion;
        payload[1] = (byte)userBytes.Length;
        userBytes.CopyTo(payload.AsSpan(2));
        payload[2 + userBytes.Length] = (byte)passBytes.Length;
        passBytes.CopyTo(payload.AsSpan(3 + userBytes.Length));

        try
        {
            stream.Write(payload.AsSpan(0, length), cancelToken);
            stream.Flush(cancelToken);

            var response = ArrayPool<byte>.Shared.Rent(2);

            try
            {
                stream.ReadExact(response.AsSpan(0, 2), cancelToken);

                if (response[0] != authVersion || response[1] != 0x00)
                    throw new IOException("SOCKS5 authentication failed.");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(response);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(payload);
        }
    }

    private (byte attrType, byte[] data, int port) GetAttrDataPort(NetEndpoint endpoint)
    {
        if (endpoint.TryCreate(out var ip, protocol))
        {
            switch (ip.AddressFamily)
            {
                case NetAddressFamily.InterNetwork:
                    return ((byte)0x01, ip.IpAddress.GetBytes(), ip.Port);
                case NetAddressFamily.InterNetworkV6:
                    return ((byte)0x04, ip.IpAddress.GetBytes(), ip.Port);
            }
        }

        var hostBytes = Encoding.ASCII.GetBytes(endpoint.Host);
        var data = new byte[1 + hostBytes.Length];
        data[0] = (byte)hostBytes.Length;
        hostBytes.CopyTo(data, 1);

        return ((byte)0x03, data, endpoint.Port);
    }

    private void SendConnect(IStreamRw<byte> stream, NetEndpoint endpoint, CancelToken cancelToken)
    {
        var (attrType, data, port) = this.GetAttrDataPort(endpoint);
        var length = 4 + data.Length + 2;
        var buffer = ArrayPool<byte>.Shared.Rent(length);
        buffer[0] = 0x05;
        buffer[1] = 0x01;
        buffer[2] = 0x00;
        buffer[3] = attrType;
        data.CopyTo(buffer, 4);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(4 + data.Length), (ushort)port);

        try
        {
            stream.Write(buffer.AsSpan(0, length), cancelToken);
            stream.Flush(cancelToken);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var head = new byte[4];
        stream.ReadExact(head, cancelToken);

        if (head[0] != 0x05)
            throw new IOException("Invalid SOCKS5 response.");

        if (head[1] != 0x00)
            throw new IOException($"SOCKS5 connect failed with code 0x{head[1]:X2}.");

        var atyp = head[3];

        var toSkip = atyp switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => readLength(stream, cancelToken),
            _ => 0,
        };

        if (toSkip > 0)
        {
            var skip = ArrayPool<byte>.Shared.Rent(toSkip + 2);

            try
            {
                stream.ReadExact(skip.AsSpan(0, toSkip + 2), cancelToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(skip);
            }
        }

        return;

        static int readLength(IStreamRw<byte> stream, CancelToken cancelToken)
        {
            var buffer = new byte[1];
            stream.ReadExact(buffer, cancelToken);

            return buffer[0];
        }
    }
}
