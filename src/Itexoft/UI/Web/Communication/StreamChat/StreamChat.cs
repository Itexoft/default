// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Collections;
using Itexoft.Core;
using Itexoft.IO;
using Itexoft.Net;
using Itexoft.Threading.Atomics;
using Itexoft.UI.Web.Communication.StreamChat.Transport;
using Itexoft.UI.Web.EmbeddedWeb;

namespace Itexoft.UI.Web.Communication.StreamChat;

public sealed partial class StreamChat : IDisposable
{
    private static AtomicLock bundleRegistrationLock = new();
    private static Latch bundleRegistered = new();
    private readonly Dictionary<string, StreamChatState> chats = new(StringComparer.Ordinal);
    private readonly NetIpEndpoint endpoint;
    private readonly Dictionary<long, StreamChatWebSocketSession> sessions = [];
    private readonly StreamChatRegistry streams;
    private Disposed disposed;
    private EmbeddedWebHandle? handle;
    private long nextOrder;
    private long nextSessionId;
    private AtomicLock sessionLock = new();
    private Latch started = new();
    private AtomicLock stateLock = new();

    public StreamChat(NetIpEndpoint endpoint)
    {
        this.endpoint = endpoint;
        this.streams = new StreamChatRegistry(this);
    }

    public IMap<string, IStreamRw<char>> Streams => this.streams;

    public void Dispose() => this.Stop();

    public void Start()
    {
        this.disposed.ThrowIf();

        if (!this.started.Try())
            throw new InvalidOperationException("StreamChat already started.");

        EnsureBundleRegistered();

        this.handle = EmbeddedWebServer.Start(
            StreamChatProtocol.BundleId,
            this.endpoint,
            options =>
            {
                options.EnableSpaFallback = true;
                options.SpaFallbackFile = "index.html";
                options.WebSocketHandler = this.CreateWebSocketHandler();
            });
    }

    public void Stop()
    {
        if (this.disposed.Enter())
            return;

        var handle = Interlocked.Exchange(ref this.handle, null);
        var readCancels = this.DetachAllReadCancels();
        var activeSessions = this.DetachAllSessions();

        for (var i = 0; i < readCancels.Length; i++)
            readCancels[i].Cancel();

        for (var i = 0; i < activeSessions.Length; i++)
            activeSessions[i].Dispose();

        handle?.Dispose();
    }

    private static void EnsureBundleRegistered()
    {
        if (bundleRegistered)
            return;

        using (bundleRegistrationLock.Enter())
        {
            if (bundleRegistered)
                return;

            EmbeddedWebServer.RegisterBundle(StreamChatProtocol.BundleId, typeof(StreamChat).Assembly);
            bundleRegistered.Try();
        }
    }
}
