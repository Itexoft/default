// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading.Tasks;

namespace Itexoft.Threading;

partial struct CancelToken
{
    public static CancelToken New() => new(new CancelTokenSource());
    public static CancelToken New(TimeSpan timeout) => new(new CancelTokenSource(default, timeout));
    public static CancelToken New(int millisecondsTimeout) => new(new CancelTokenSource(default, millisecondsTimeout));

    private CancelToken(CancelTokenSource source) => this.source = source;
    private CancelToken(CancellationToken cancellationToken) => this.source = new CancelTokenSource(cancellationToken);

    private readonly CancelTokenSource source;

    private readonly struct CancelTokenSource : IEquatable<CancelTokenSource>
    {
        private readonly PromiseAwaiter awaiter;

        public CancelTokenSource() => this.awaiter = PromiseAwaiter.Uncompleted();

        public CancelTokenSource(CancelTokenSource parent, TimeSpan timeout) : this(parent, timeout.TimeoutMilliseconds) { }

        public CancelTokenSource(CancelTokenSource parent, int timeoutMilliseconds)
        {
            if (parent.IsRequested || timeoutMilliseconds == 0)
                this.awaiter = PromiseAwaiter.Completed();
            else
            {
                if (timeoutMilliseconds > 0)
                    this.awaiter = PromiseAwaiter.UncompletedTimer(timeoutMilliseconds);
                else
                    this.awaiter = PromiseAwaiter.Uncompleted();

                if (!parent.IsNull)
                    parent.awaiter.OnCompleted(this.awaiter.CompleteAction()!);
            }
        }

        public CancelTokenSource(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                this.awaiter = PromiseAwaiter.Completed();
            else
            {
                this.awaiter = PromiseAwaiter.Uncompleted();
                cancellationToken.UnsafeRegister(static ca => ((Action?)ca)!.Invoke(), this.awaiter.CompleteAction());
            }
        }

        public bool IsNull => this.awaiter.IsNull;
        public bool IsTimedOut => this.awaiter.IsTimerFinished;

        public bool IsRequested => !this.IsNull && this.awaiter.IsCompleted;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Cancel() => this.awaiter.Complete();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public IDisposable Bridge(out CancellationToken cancellationToken)
        {
            var cts = new CancellationTokenSource();
            var dispose = new DisposeOneOffAction(cts.Dispose);

            this.Register(() =>
            {
                try
                {
                    if (!dispose.IsDisposed)
                        cts.Cancel();
                }
                catch (ObjectDisposedException) { }
            });

            cancellationToken = cts.Token;

            return dispose;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CancelTokenSource ThrowIf(string callerMember = "", string callerFile = "", int callerLine = 0)
        {
            if (this.IsRequested)
                throw new OperationCanceledException($"Operation cancelled: {callerMember} ({callerFile}:{callerLine}).");

            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public CancelTokenSource Branch(TimeSpan timeout = default) => new(this, timeout);

        public void Register(Action action) => this.awaiter.OnCompleted(action);

        public bool Equals(CancelTokenSource other) => this.awaiter.Equals(other.awaiter);

        public override bool Equals(object? obj) => obj is CancelTokenSource other && this.Equals(other);

        public override int GetHashCode() => this.awaiter.GetHashCode();

        public static bool operator ==(CancelTokenSource left, CancelTokenSource right) => left.Equals(right);

        public static bool operator !=(CancelTokenSource left, CancelTokenSource right) => !left.Equals(right);

        public PromiseAwaiter GetAwaiter() => this.awaiter;
    }
}
