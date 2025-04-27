// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Itexoft.Threading.Tasks;

[AsyncMethodBuilder(typeof(TaskMethodBuilder<>))]
public sealed class HeapTask<TResult>
{
    private readonly TaskCore<TResult>? core;
    private readonly TaskData<TResult> data;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(ValueTask<TResult> task)
    {
        this.core = null;
        this.data = new TaskData<TResult>(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Task<TResult> task)
    {
        this.core = null;
        this.data = new TaskData<TResult>(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(TResult result)
    {
        this.core = null;
        this.data = new TaskData<TResult>(in result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Func<StackTask<TResult>> factory)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Func<TResult> factory)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Func<CancelToken, TResult> factory, CancelToken cancelToken)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Func<CancelToken, StackTask<TResult>> factory, CancelToken cancelToken)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HeapTask(TaskCore<TResult>? core, in TaskData<TResult> taskData)
    {
        this.core = core;
        this.data = taskData;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HeapTask(TaskCore<TResult>? core)
    {
        this.core = core;
        this.data = default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HeapTask(ExceptionDispatchInfo exceptionDispatchInfo)
    {
        this.core = null;
        this.data = new TaskData<TResult>(exceptionDispatchInfo);
    }

    public static HeapTask<TResult> CompletedTask { get; } = new(default(TResult)!);

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
    public static implicit operator HeapTask<TResult>(in ValueTask<TResult> task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator HeapTask<TResult>(in Task<TResult> task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator HeapTask<TResult>(in StackTask<TResult> task) => task.AsHeapTask();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask<TResult> AsStackTask() => new(this.core, in this.data);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TaskAwaiter<TResult> GetAwaiter()
    {
        if (this.data.TryGetTaskFactory(out var factory))
            return factory().GetAwaiter();

        if (this.data.TryGetTaskFactory(out var factoryWithToken, out var cancelToken))
        {
            cancelToken.ThrowIf();

            return factoryWithToken(cancelToken).GetAwaiter();
        }

        return new TaskAwaiter<TResult>(this.core, in this.data);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HeapTask<TResult> FromException(Exception exception) => new(ExceptionDispatchInfo.Capture(exception));

    public Task<TResult> AsTask()
    {
        if (this.core is { } taskCore)
            return StackTaskBridge<TResult>.AsTask(taskCore);

        if (!this.data.HasValue)
            return Task.FromResult(default(TResult)!);

        return this.data.AsTask();
    }
}
