// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Itexoft.Threading.Tasks;

[AsyncMethodBuilder(typeof(TaskMethodBuilder))]
public readonly ref struct StackTask
{
    private readonly TaskData data;
    private readonly TaskCore? core;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Task task)
    {
        this.core = null;
        this.data = new TaskData(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(ValueTask task)
    {
        this.core = null;
        this.data = new TaskData(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Action factory)
    {
        this.core = null;
        this.data = new TaskData(factory, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Action<CancelToken> factory, CancelToken cancelToken)
    {
        this.core = null;
        this.data = new TaskData(factory, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask(TaskCore? core, in TaskData data)
    {
        this.core = core;
        this.data = data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask(TaskCore? core)
    {
        this.core = core;
        this.data = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask(ExceptionDispatchInfo exceptionDispatchInfo)
    {
        this.core = null;
        this.data = new TaskData(exceptionDispatchInfo);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StackTask(ValueTask task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StackTask(Task task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StackTask(HeapTask task) => task.AsStackTask();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HeapTask AsHeapTask() => new(this.core, in this.data);

    public bool IsCompletedSuccessfully
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.core is not null)
                return this.core.IsCompletedSuccessfully;

            return !this.data.HasValue || this.data.IsCompletedSuccessfully;
        }
    }

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.core is not null)
                return this.core.IsCompleted;

            return this.data.IsCompleted;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TaskAwaiter GetAwaiter() => new(this.core, in this.data);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static StackTask FromException(Exception exception) => new(ExceptionDispatchInfo.Capture(exception));

    public Task AsTask()
    {
        if (this.core is { } core)
        {
            var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            core.OnCompleted(() =>
            {
                try
                {
                    tcs.SetResult();
                }
                catch (Exception exception)
                {
                    tcs.SetException(exception);
                }
            });

            return tcs.Task;
        }

        return Task.CompletedTask;
    }
}
