// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Core;

public interface INetLazyStream : INetStream
{
    StackTask DisconnectAsync(CancelToken cancelToken = default);
}

public sealed class NetLazyStream(Func<CancelToken, StackTask<INetStream>> connector) : INetLazyStream
{
    private readonly Func<CancelToken, StackTask<INetStream>> connector = connector.Required();
    private Context context = new();
    private INetStream? inner;
    private TimeSpan readTimeout;
    private bool readTimeoutSet;
    private TimeSpan writeTimeout;
    private bool writeTimeoutSet;

    public NetLazyStream(Func<StackTask<INetStream>> connector) : this(_ => connector()) { }

    public async StackTask<int> ReadAsync(Memory<byte> buffer, CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken))
            return await this.ReadCoreAsync(buffer, cancelToken);
    }

    public async StackTask WriteAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken))
            await this.WriteCoreAsync(buffer, cancelToken);
    }

    public async StackTask FlushAsync(CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken))
            await this.FlushCoreAsync(cancelToken);
    }

    public async StackTask DisconnectAsync(CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken))
        {
            var stream = this.inner;
            this.inner = null;

            if (stream is not null)
                await stream.DisposeAsync();
        }
    }

    public async StackTask DisposeAsync()
    {
        if (await this.context.EnterDisposeAsync())
            return;

        var stream = this.inner;
        this.inner = null;

        if (stream is not null)
            await stream.DisposeAsync();
    }

    public TimeSpan WriteTimeout
    {
        get => this.inner is { } stream ? stream.WriteTimeout : this.writeTimeoutSet ? this.writeTimeout : TimeSpan.Zero;
        set
        {
            this.writeTimeout = value;
            this.writeTimeoutSet = true;

            if (this.inner is { } stream)
                stream.WriteTimeout = value;
        }
    }

    public TimeSpan ReadTimeout
    {
        get => this.inner is { } stream ? stream.ReadTimeout : this.readTimeoutSet ? this.readTimeout : TimeSpan.Zero;
        set
        {
            this.readTimeout = value;
            this.readTimeoutSet = true;

            if (this.inner is { } stream)
                stream.ReadTimeout = value;
        }
    }

    private async StackTask<int> ReadCoreAsync(Memory<byte> buffer, CancelToken cancelToken)
    {
        var stream = await this.EnsureStreamAsync(cancelToken);

        return await stream.ReadAsync(buffer, cancelToken);
    }

    private async StackTask WriteCoreAsync(ReadOnlyMemory<byte> buffer, CancelToken cancelToken)
    {
        var stream = await this.EnsureStreamAsync(cancelToken);

        await stream.WriteAsync(buffer, cancelToken);
    }

    private async StackTask FlushCoreAsync(CancelToken cancelToken)
    {
        var stream = await this.EnsureStreamAsync(cancelToken);

        await stream.FlushAsync(cancelToken);
    }

    private async StackTask<INetStream> EnsureStreamAsync(CancelToken cancelToken)
    {
        if (this.inner is { } stream)
            return stream;

        stream = await this.connector(cancelToken.ThrowIf());
        this.ApplyTimeouts(stream);
        this.inner = stream;

        return stream;
    }

    private void ApplyTimeouts(INetStream stream)
    {
        if (this.readTimeoutSet)
            stream.ReadTimeout = this.readTimeout;

        if (this.writeTimeoutSet)
            stream.WriteTimeout = this.writeTimeout;
    }
}
