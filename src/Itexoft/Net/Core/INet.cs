// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Net.Core;

public interface INetConnectionHandle : ITaskDisposable
{
    NetEndpoint Endpoint { get; }
    INetLazyStream Stream { get; }
}

public interface INetConnectionManager : ITaskDisposable
{
    Guid Id { get; }
    INetConnectionHandle? Connection { get; }
    StackTask<INetConnectionHandle?> ConnectAsync(CancelToken cancelToken = default);
    StackTask<INetConnectionMetrics> GetMetricsSnapshot();
    event EventHandler<NetConnectionEventArgs>? StateChanged;
}

public interface INetConnectionManager<TConnectionHandle> : INetConnectionManager where TConnectionHandle : INetConnectionHandle
{
    new TConnectionHandle? Connection { get; }
    INetConnectionHandle? INetConnectionManager.Connection => this.Connection;
    async StackTask<INetConnectionHandle?> INetConnectionManager.ConnectAsync(CancelToken cancelToken) => await this.ConnectAsync(cancelToken);
    StackTask<TConnectionHandle?> DisconnectAsync();
    new StackTask<TConnectionHandle?> ConnectAsync(CancelToken cancelToken = default);
}

public interface INetConnectionMetrics
{
    NetConnectionState State { get; }
    TimeSpan Uptime { get; }
    TimeSpan Downtime { get; }
    NetFailureSeverity LastFailureSeverity { get; }
    int TotalDialAttemptsLastMinute { get; }
    string? LastEndpointLabel { get; }
    DateTimeOffset LastDataActivityUtc { get; }
    IReadOnlyList<NetDialRateMetric> DialRates { get; }
}

public interface INetConnectionDialer
{
    NetDialTrackerCollection DialTrackers { get; }
    StackTask<INetConnectionHandle?> DialAsync(NetDialParameters dialParams, CancelToken cancelToken);
}

public interface INetDialTracker
{
    bool TryStart(DateTimeOffset now, int maxPerMinute, TimeSpan blacklistDuration);
    void RecordSuccess();
    void RecordFailure(DateTimeOffset now, TimeSpan blacklistDuration);
    NetDialSnapshot GetSnapshot(DateTimeOffset now);
    TimeSpan? GetBlacklistDelay(DateTimeOffset now);
}

public interface INetEventSource : IDisposable
{
    event EventHandler? NetworkAvailabilityLost;
    event EventHandler? NetworkAddressChanged;
}

public interface INetConnector
{
    LString Kind { get; }
    StackTask<INetConnectionHandle> ConnectAsync(NetEndpoint endpoint, Func<StackTask> dispose, CancelToken cancelToken);
}
