// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using Itexoft.Core;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.UI.Web.Monitoring.BrowserEventMonitor.Transport;

internal sealed class BrowserEventMonitorSseStream(BrowserEventMonitor monitor, long sinceRevision) : IStreamR<byte>
{
    private const long keepAliveTicks = TimeSpan.TicksPerSecond * 15;
    private const long coalesceTicks = TimeSpan.TicksPerMillisecond * 25;
    private static readonly byte[] retryFrame = "retry: 1000\n\n"u8.ToArray();
    private static readonly byte[] keepAliveFrame = ": keepalive\n\n"u8.ToArray();
    private readonly StringBuilder builder = new(128);

    private readonly BrowserEventMonitor monitor = monitor;
    private readonly string serverInstanceId = monitor.ServerInstanceId;
    private Disposed disposed = new();
    private long lastKeepAliveTicks = TimeUtils.CachedTimestampTicks;
    private long lastNoticeTicks;
    private byte[] pending = [];
    private int pendingOffset;
    private bool sentRetry;
    private long sinceRevision = sinceRevision;

    public void Dispose()
    {
        _ = this.disposed.Enter();
        this.pending = [];
        this.pendingOffset = 0;
    }

    public int Read(Span<byte> buffer, CancelToken cancelToken = default)
    {
        if (buffer.IsEmpty)
            return 0;

        while (true)
        {
            if (this.TryReadPending(buffer, out var read))
                return read;

            if (this.disposed)
                return 0;

            cancelToken.ThrowIf();

            if (!this.sentRetry)
            {
                this.sentRetry = true;
                this.Queue(retryFrame);

                continue;
            }

            if (this.TryQueueRevision())
                continue;

            var now = TimeUtils.CachedTimestampTicks;

            if (now - this.lastKeepAliveTicks >= keepAliveTicks)
            {
                this.lastKeepAliveTicks = now;
                this.Queue(keepAliveFrame);

                continue;
            }

            Thread.Sleep(10);
        }
    }

    private bool TryQueueRevision()
    {
        var revision = this.monitor.GlobalRevision;

        if (revision <= this.sinceRevision)
            return false;

        var now = TimeUtils.CachedTimestampTicks;

        if (this.lastNoticeTicks != 0 && now - this.lastNoticeTicks < coalesceTicks)
            return false;

        this.lastNoticeTicks = now;
        this.sinceRevision = revision;
        this.lastKeepAliveTicks = now;
        this.builder.Clear();
        this.builder.Append("event: revision\n");
        this.builder.Append("data: {\"serverInstanceId\":\"");
        this.builder.Append(this.serverInstanceId);
        this.builder.Append("\",\"globalRevision\":");
        this.builder.Append(revision);
        this.builder.Append("}\n\n");
        this.Queue(Encoding.UTF8.GetBytes(this.builder.ToString()));

        return true;
    }

    private void Queue(byte[] bytes)
    {
        this.pending = bytes;
        this.pendingOffset = 0;
    }

    private bool TryReadPending(Span<byte> buffer, out int read)
    {
        if (this.pendingOffset >= this.pending.Length)
        {
            read = 0;

            return false;
        }

        read = Math.Min(buffer.Length, this.pending.Length - this.pendingOffset);
        this.pending.AsSpan(this.pendingOffset, read).CopyTo(buffer);
        this.pendingOffset += read;

        if (this.pendingOffset >= this.pending.Length)
        {
            this.pending = [];
            this.pendingOffset = 0;
        }

        return true;
    }
}
