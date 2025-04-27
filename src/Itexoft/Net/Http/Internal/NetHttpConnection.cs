// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Net.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Http.Internal;

internal sealed class NetHttpConnection(NetHttpClient owner) : ITaskDisposable
{
    private readonly NetHttpClient owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private Context context = new();
    private int dead;
    private ITaskDisposable? disposable;
    private INetStream? stream;

    public async StackTask DisposeAsync()
    {
        if (await this.context.EnterDisposeAsync())
            return;

        await this.CloseCoreAsync();
    }

    public async StackTask<NetHttpConnectionLease> AcquireAsync(NetHttpRequest request, CancelToken cancelToken)
    {
        var enter = await this.context.EnterAsync(cancelToken);

        try
        {
            if (this.stream is null || Volatile.Read(ref this.dead) != 0)
                await this.OpenAsync(cancelToken);

            var reader = new NetHttpBufferedReader(this.stream!, request.ReceiveBufferSize);

            return new(this, enter, this.stream!, reader);
        }
        catch
        {
            await enter.DisposeAsync();

            throw;
        }
    }

    private async StackTask OpenAsync(CancelToken cancelToken)
    {
        await this.CloseCoreAsync();
        Volatile.Write(ref this.dead, 0);

        var connectToken = NetHttpClient.ApplyTimeout(cancelToken, this.owner.ConnectTimeout, false);

        var handle = await this.owner.Connector.ConnectAsync(this.owner.Endpoint, this.MarkDeadAsync, connectToken);

        if (handle is null)
            throw new IOException("Failed to establish TCP connection.");

        var netStream = handle.Stream;

        if (this.owner.TlsOptions is not null)
        {
            var sslStream = new NetSslStream(netStream, false);
            await sslStream.AuthenticateAsClientAsync(this.owner.TlsOptions, connectToken);
            this.stream = sslStream;
            this.disposable = sslStream;
        }
        else
        {
            this.stream = netStream;
            this.disposable = handle;
        }
    }

    private StackTask MarkDeadAsync()
    {
        Volatile.Write(ref this.dead, 1);

        return default;
    }

    internal async StackTask ReleaseAsync(NetHttpConnectionLease lease, bool close)
    {
        await lease.Reader.DisposeAsync();

        if (close)
            await this.CloseCoreAsync();

        await lease.Enter.DisposeAsync();
    }

    private async StackTask CloseCoreAsync()
    {
        this.stream = null;

        if (this.disposable is not null)
        {
            await this.disposable.DisposeAsync();
            this.disposable = null;
        }
    }
}

internal sealed class NetHttpConnectionLease(NetHttpConnection owner, ITaskDisposable enter, INetStream stream, NetHttpBufferedReader reader)
{
    private readonly NetHttpConnection owner = owner ?? throw new ArgumentNullException(nameof(owner));
    private int released;

    internal ITaskDisposable Enter { get; } = enter ?? throw new ArgumentNullException(nameof(enter));
    public INetStream Stream { get; } = stream ?? throw new ArgumentNullException(nameof(stream));
    public NetHttpBufferedReader Reader { get; } = reader ?? throw new ArgumentNullException(nameof(reader));

    public StackTask ReleaseAsync(bool close)
    {
        if (Interlocked.Exchange(ref this.released, 1) != 0)
            return default;

        return this.owner.ReleaseAsync(this, close);
    }
}
