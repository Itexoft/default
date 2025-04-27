// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Net.Sockets;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Core.Internal;

public sealed class NetConnectionManager(NetConnectionOptions options) : INetConnectionManager
{
    private readonly NetConnectionDialer dialer = new(options);
    private readonly DateTimeOffset lastDataActivity = DateTimeOffset.UtcNow;
    private readonly NetConnectionOptions options = options.Required();
    private Context context = new();
    private TimeSpan totalDowntime;
    private DateTimeOffset uptimeStart = DateTimeOffset.UtcNow;

    private NetFailureSeverity LastFailureSeverity { get; set; } = NetFailureSeverity.None;

    public NetConnectionState State { get; private set; }

    public Guid Id { get; } = Guid.NewGuid();

    public INetConnectionHandle? Connection { get; private set; }

    public event EventHandler<NetConnectionEventArgs>? StateChanged;

    public async StackTask<INetConnectionHandle?> ConnectAsync(CancelToken cancelToken = default)
    {
        await using (await this.context.EnterAsync(cancelToken))
            return this.Connection = await this.ConnectCoreAsync(cancelToken);
    }

    public async StackTask DisposeAsync()
    {
        if (await this.context.EnterDisposeAsync())
            return;

        await using (await this.context.EnterAsync())
        {
            this.ChangeState(NetConnectionState.Disposed, NetConnectionTransitionCause.Disposal);
            await this.DisconnectCoreAsync();
        }
    }

    public StackTask<INetConnectionMetrics> GetMetricsSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        List<NetDialRateMetric>? dialRates = null;

        if (!this.dialer.DialTrackers.IsEmpty)
        {
            dialRates = new(this.dialer.DialTrackers.Count);

            foreach (var (key, tracker) in this.dialer.DialTrackers)
            {
                var snapshot = tracker.GetSnapshot(now);
                dialRates.Add(new() { EndpointKey = $"{key.Host}:{key.Port}", AttemptsLastMinute = snapshot.AttemptsLastMinute });
            }
        }

        var attempts = dialRates?.Sum(r => r.AttemptsLastMinute) ?? 0;

        return new StackTask<INetConnectionMetrics>(
            new NetConnectionMetrics
            {
                State = this.State,
                Uptime = this.State == NetConnectionState.Established ? now - this.uptimeStart : TimeSpan.Zero,
                Downtime = this.totalDowntime,
                LastFailureSeverity = this.LastFailureSeverity,
                TotalDialAttemptsLastMinute = attempts,
                DialRates = dialRates is not null ? [..dialRates] : [],
                LastEndpointLabel = this.Connection?.Endpoint.ToString(),
                LastDataActivityUtc = this.lastDataActivity,
            });
    }

    private async StackTask DisconnectCoreAsync()
    {
        if (this.Connection is not INetConnectionHandle handle)
            return;

        this.Connection = null;
        await handle.DisposeAsync();
    }

    private async StackTask<INetConnectionHandle> ConnectCoreAsync(CancelToken cancelToken)
    {
        INetConnectionHandle? handle = null;

        try
        {
            if (this.Connection is not null)
            {
                this.ChangeState(NetConnectionState.Reconnecting, NetConnectionTransitionCause.Manual);
                await this.Connection.DisposeAsync();
                this.Connection = null;
            }

            this.ChangeState(NetConnectionState.Connecting, NetConnectionTransitionCause.DialStarted);
            handle = await this.DialAsync(cancelToken);

            if (handle is null)
            {
                await this.HandleDialFailureAsync(null, new InvalidOperationException("no endpoints"), cancelToken, TimeSpan.Zero);

                throw new InvalidOperationException("Failed to connect to any configured endpoint.");
            }

            this.ChangeState(NetConnectionState.Established, NetConnectionTransitionCause.DialSucceeded);

            return handle;
        }
        catch (Exception ex)
        {
            await this.HandleDialFailureAsync(handle, ex, cancelToken, TimeSpan.Zero);

            throw;
        }
    }

    private async StackTask HandleDialFailureAsync(INetConnectionHandle? connection, Exception? exception, CancelToken cancelToken, TimeSpan backoff)
    {
        this.LastFailureSeverity = exception switch
        {
            OperationCanceledException => NetFailureSeverity.Soft,
            TimeoutException or SocketException or IOException => NetFailureSeverity.Hard,
            ObjectDisposedException or InvalidOperationException => NetFailureSeverity.Fatal,
            _ => NetFailureSeverity.Soft,
        };

        this.ChangeState(NetConnectionState.Failed, NetConnectionTransitionCause.DialFailed);

        if (exception is not OperationCanceledException && connection is not null)
        {
            var endpoint = connection.Endpoint.Port == 0 ? this.options.Endpoints[0] : connection.Endpoint;
            var tracker = this.dialer.DialTrackers.GetOrAdd(endpoint, _ => new NetDialTracker());
            tracker.RecordFailure(DateTimeOffset.UtcNow, this.options.EndpointBlacklistDuration);
        }

        if (connection is not null)
            await connection.DisposeAsync();

        if (backoff > TimeSpan.Zero)
        {
            using (cancelToken.Bridge(out var token))
                await Task.Delay(backoff, token);
        }
    }

    private async StackTask<INetConnectionHandle?> DialAsync(CancelToken cancelToken)
    {
        foreach (var endpoint in this.options.Endpoints)
        {
            cancelToken.ThrowIf();

            var tracker = this.dialer.DialTrackers.GetOrAdd(endpoint, _ => new NetDialTracker());
            var now = DateTimeOffset.UtcNow;

            if (!tracker.TryStart(now, this.options.RetryBudgetCapacity, this.options.EndpointBlacklistDuration))
                continue;

            var dialOptions = new NetDialParameters(endpoint, (Func<StackTask>)this.DisconnectCoreAsync, tracker)
            {
                ChangeState = async (cause, exception) =>
                {
                    await this.HandleDialFailureAsync(null, exception, cancelToken, this.options.SoftFailureInitialBackoff);
                    this.ChangeState(this.State, cause);
                },
            };

            if (await this.dialer.DialAsync(dialOptions, cancelToken) is INetConnectionHandle handle)
                return handle;
        }

        return null;
    }

    private void ChangeState(NetConnectionState newState, NetConnectionTransitionCause cause)
    {
        if (this.State == NetConnectionState.Disposed)
            return;

        var previous = this.State;

        if (previous == newState)
            return;

        switch (newState)
        {
            case NetConnectionState.Established:
                this.uptimeStart = DateTimeOffset.UtcNow;

                break;
            case NetConnectionState.Disconnected or NetConnectionState.Failed:
                this.totalDowntime += DateTimeOffset.UtcNow - this.uptimeStart;

                break;
        }

        this.State = newState;
        this.StateChanged?.Invoke(this, new(previous, newState, cause));
    }
}
