// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;

namespace Itexoft.UI.Web.Communication.StreamChat.Transport;

internal sealed class StreamChatWebSocketSession(long id, NetHttpWebSocket webSocket, Action<long> onClosed) : IDisposable
{
    private readonly Action<long> onClosed = onClosed;
    private readonly NetHttpWebSocket webSocket = webSocket;
    private Disposed disposed;
    private AtomicLock sendLock;

    public long Id { get; } = id;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        try
        {
            this.webSocket.Close();
        }
        catch
        {
            // ignored
        }
        finally
        {
            this.onClosed(this.Id);
        }
    }

    public bool TryReceive(out NetHttpWebSocketMessage message, CancelToken cancelToken)
    {
        if (this.disposed)
        {
            message = default;

            return false;
        }

        try
        {
            message = this.webSocket.Receive(cancelToken);

            return true;
        }
        catch (OperationCanceledException) when (cancelToken.IsRequested)
        {
            message = default;

            return false;
        }
        catch
        {
            this.Dispose();
            message = default;

            return false;
        }
    }

    public bool TrySend(string payload, CancelToken cancelToken = default)
    {
        if (this.disposed)
            return false;

        using (this.sendLock.Enter())
        {
            if (this.disposed)
                return false;

            try
            {
                this.webSocket.SendText(payload, cancelToken);

                return true;
            }
            catch
            {
                this.Dispose();

                return false;
            }
        }
    }
}
