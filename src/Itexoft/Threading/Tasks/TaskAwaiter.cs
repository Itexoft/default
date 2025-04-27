// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Tasks;

public readonly struct TaskAwaiter : ICriticalNotifyCompletion
{
    private readonly TaskCore? core;
    private readonly TaskData data;

    internal TaskAwaiter(TaskCore? core, in TaskData data)
    {
        this.core = core;
        this.data = data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetResult()
    {
        if (this.core is TaskCore core)
        {
            core.GetResult();

            return;
        }

        if (this.data.HasValue)
            this.data.GetResult();
    }

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.core is TaskCore core)
                return core.IsCompleted;

            return !this.data.HasValue || this.data.IsCompleted;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        if (this.core is not null)
        {
            this.core.OnCompleted(continuation);

            return;
        }

        if (!this.data.HasValue)
        {
            continuation();

            return;
        }

        this.data.OnCompleted(continuation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation)
    {
        if (this.core is not null)
        {
            this.core.UnsafeOnCompleted(continuation);

            return;
        }

        if (!this.data.HasValue)
        {
            continuation();

            return;
        }

        this.data.UnsafeOnCompleted(continuation);
    }
}

public readonly struct TaskAwaiter<TResult> : ICriticalNotifyCompletion
{
    private readonly TaskCore<TResult>? core;
    private readonly TaskData<TResult> data;

    internal TaskAwaiter(TaskCore<TResult>? core, in TaskData<TResult> data)
    {
        this.core = core;
        this.data = data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult GetResult()
    {
        if (this.core is TaskCore<TResult> core)
            return core.GetResult();

        if (this.data.HasValue)
            return this.data.GetResult();

        return default!;
    }

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.core is TaskCore<TResult> core)
                return core.IsCompleted;

            return !this.data.HasValue || this.data.IsCompleted;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        if (this.core is TaskCore<TResult> core)
        {
            core.OnCompleted(continuation);

            return;
        }

        if (!this.data.HasValue)
        {
            continuation();

            return;
        }

        this.data.OnCompleted(continuation);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation)
    {
        if (this.core is TaskCore<TResult> core)
        {
            core.UnsafeOnCompleted(continuation);

            return;
        }

        if (!this.data.HasValue)
        {
            continuation();

            return;
        }

        this.data.UnsafeOnCompleted(continuation);
    }
}
