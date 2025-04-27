// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.Net.Sockets;
using Itexoft.Threading;

namespace Itexoft.Net.Core;

public sealed class NetStream(NetSocket socket, bool ownsSocket) : INetStream
{
    private readonly bool ownsSocket = ownsSocket;
    private readonly NetSocket socket = socket.Required();

    public NetStream(NetSocket socket) : this(socket, true) { }

    public int Read(Span<byte> span, CancelToken cancelToken = default) =>
        this.socket.Receive(span, NetSocketFlags.None, cancelToken);

    public void Flush(CancelToken cancelToken) => cancelToken.ThrowIf();

    public void Write(ReadOnlySpan<byte> span, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        while (!span.IsEmpty)
        {
            var written = this.socket.Send(span, NetSocketFlags.None, cancelToken);

            if (written <= 0)
                throw new IOException("Socket write returned no data.");

            span = span[written..];
        }
    }

    public void Dispose()
    {
        if (this.ownsSocket)
            this.socket.Dispose();
    }
}
