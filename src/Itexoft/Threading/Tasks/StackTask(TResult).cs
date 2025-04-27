// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Itexoft.Threading.Tasks;

[AsyncMethodBuilder(typeof(TaskMethodBuilder<>))]
public readonly ref struct StackTask<TResult>
{
    private readonly TaskCore<TResult>? core;
    private readonly TaskData<TResult> data;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(ValueTask<TResult> task)
    {
        this.core = null;
        this.data = new TaskData<TResult>(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Task<TResult> task)
    {
        this.core = null;
        this.data = new TaskData<TResult>(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(TResult result)
    {
        this.core = null;
        this.data = new TaskData<TResult>(in result);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Func<StackTask<TResult>> factory)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Func<TResult> factory)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Func<CancelToken, TResult> factory, CancelToken cancelToken)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask(Func<CancelToken, StackTask<TResult>> factory, CancelToken cancelToken)
    {
        this.core = null;
        this.data = new TaskData<TResult>(factory, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask(TaskCore<TResult>? core, in TaskData<TResult> data)
    {
        this.core = core;
        this.data = data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask(TaskCore<TResult>? core)
    {
        this.data = default;
        this.core = core;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask(ExceptionDispatchInfo exceptionDispatchInfo)
    {
        this.core = null;
        this.data = new TaskData<TResult>(exceptionDispatchInfo);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StackTask<TResult>(ValueTask<TResult> task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StackTask<TResult>(Task<TResult> task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StackTask<TResult>(HeapTask<TResult> task) => task.AsStackTask();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator StackTask<TResult>(TResult result) => new(result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HeapTask<TResult> AsHeapTask() => new(this.core, in this.data);

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
    public static StackTask<TResult> FromException(Exception exception) => new(ExceptionDispatchInfo.Capture(exception));

    internal static StackTask<TResult> FromException(ExceptionDispatchInfo exceptionDispatchInfo) => new(exceptionDispatchInfo);

    public Task<TResult> AsTask()
    {
        if (this.core is { } taskCore)
            return StackTaskBridge<TResult>.AsTask(taskCore);

        if (!this.data.HasValue)
            return Task.FromResult(default(TResult)!);

        return this.data.AsTask();
    }
}

internal static class StackTaskBridge<TResult>
{
    internal static Task<TResult> AsTask(TaskCore<TResult> core)
    {
        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        core.OnCompleted(() =>
        {
            try
            {
                tcs.SetResult(core.GetResult());
            }
            catch (Exception exception)
            {
                tcs.SetException(exception);
            }
        });

        return tcs.Task;
    }
}
