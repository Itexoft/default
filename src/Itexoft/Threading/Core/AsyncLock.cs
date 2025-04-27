// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Collections;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading.Tasks;

namespace Itexoft.Threading.Core;

public sealed class AsyncLock : ITaskDisposable
{
    private readonly AtomicSet<IEnter> enters = [];
    private Disposed disposed;

    private Latch latch = new();

    public async StackTask DisposeAsync()
    {
        if (this.disposed.Enter())
            return;

        while (this.enters.Count != 0 && this.latch.Try())
            await Task.Yield();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async StackTask<IEnter> EnterAsync(IEnter enter)
    {
        enter.Required();
        this.disposed.ThrowIf();

        while (!enter.Contains(this) && this.latch.Try())
        {
            await Task.Yield();
            this.disposed.ThrowIf();
        }

        return new EnterValue(this);
    }

    public StackTask<IEnter> EnterAsync(CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        return this.EnterAsync();
    }

    public StackTask<IEnter> EnterAsync(IEnter enter, CancelToken cancelToken)
    {
        cancelToken.ThrowIf();

        return this.EnterAsync(enter);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public async StackTask<IEnter> EnterAsync()
    {
        this.disposed.ThrowIf();

        while (this.latch.Try())
        {
            await Task.Yield();
            this.disposed.ThrowIf();
        }

        return new EnterValue(this);
    }

    public interface IEnter : ITaskDisposable
    {
        bool Contains(AsyncLock asyncLock);
    }

    private sealed class EnterValue : IEnter
    {
        private readonly AsyncLock owner;

        internal EnterValue(AsyncLock owner)
        {
            this.owner = owner;
            this.owner.enters.Add(this);
        }

        public bool Contains(AsyncLock asyncLock)
        {
            if (!ReferenceEquals(this.owner, asyncLock))
                return false;

            return asyncLock.enters.Contains(this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StackTask DisposeAsync()
        {
            this.owner.enters.Remove(this);
            this.owner.latch.Reset();

            return default;
        }
    }
}
