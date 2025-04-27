// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Itexoft.Extensions;

namespace Itexoft.Threading.Tasks;

internal readonly struct TaskData
{
    private readonly TaskDataKind kind;
    private readonly object? value;
    private readonly ValueTask valueTask;
    private readonly CancelToken cancelToken;

    internal TaskData(HeapTask heapTask, CancelToken cancelToken)
    {
        this.kind = TaskDataKind.HeapTask;
        this.value = heapTask.Required();
        this.valueTask = default;
        this.cancelToken = cancelToken;
    }

    internal TaskData(ValueTask valueTask, CancelToken cancelToken)
    {
        this.kind = TaskDataKind.ValueTask;
        this.valueTask = valueTask;
        this.value = null;
        this.cancelToken = cancelToken;
    }

    internal TaskData(Task task, CancelToken cancelToken)
    {
        this.kind = TaskDataKind.Task;
        this.value = task.Required();
        this.valueTask = default;
        this.cancelToken = cancelToken;
    }

    internal TaskData(Action factory, CancelToken cancelToken)
    {
        this.kind = TaskDataKind.Action;
        this.value = factory.Required();
        this.valueTask = default;
        this.cancelToken = cancelToken;
    }

    internal TaskData(Action<CancelToken> factory, CancelToken cancelToken)
    {
        this.kind = TaskDataKind.ActionWithToken;
        this.value = factory.Required();
        this.valueTask = default;
        this.cancelToken = cancelToken;
    }

    internal TaskData(ExceptionDispatchInfo exception)
    {
        this.kind = TaskDataKind.Exception;
        this.value = exception.Required();
        this.valueTask = default;
        this.cancelToken = default;
    }

    public bool HasValue => this.kind != TaskDataKind.None;

    public readonly void ThrowIf() => this.cancelToken.ThrowIf();

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.kind == TaskDataKind.None || this.cancelToken.IsRequested)
                return true;

            return this.kind switch
            {
                TaskDataKind.HeapTask => ((HeapTask)this.value!).IsCompleted,
                TaskDataKind.ValueTask => this.valueTask.IsCompleted,
                TaskDataKind.Task => ((Task)this.value!).IsCompleted,
                _ => true,
            };
        }
    }

    public bool IsCompletedSuccessfully
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.cancelToken.IsRequested)
                return false;

            if (this.kind == TaskDataKind.None)
                return true;

            return this.kind switch
            {
                TaskDataKind.HeapTask => ((HeapTask)this.value!).IsCompletedSuccessfully,
                TaskDataKind.ValueTask => this.valueTask.IsCompletedSuccessfully,
                TaskDataKind.Task => ((Task)this.value!).IsCompletedSuccessfully,
                TaskDataKind.Exception => false,
                _ => true,
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void GetResult()
    {
        this.cancelToken.ThrowIf();

        switch (this.kind)
        {
            case TaskDataKind.HeapTask:
                ((HeapTask)this.value!).GetAwaiter().GetResult();

                break;
            case TaskDataKind.ValueTask:
                this.valueTask.ConfigureAwait(false).GetAwaiter().GetResult();

                break;
            case TaskDataKind.Task:
                ((Task)this.value!).ConfigureAwait(false).GetAwaiter().GetResult();

                break;
            case TaskDataKind.Action:
                ((Action)this.value!)();

                break;
            case TaskDataKind.ActionWithToken:
                ((Action<CancelToken>)this.value!)(this.cancelToken);

                break;
            case TaskDataKind.Exception:
                ((ExceptionDispatchInfo)this.value!).Throw();

                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        continuation.Required();
        this.cancelToken.ThrowIf();

        switch (this.kind)
        {
            case TaskDataKind.HeapTask:
                ((HeapTask)this.value!).GetAwaiter().OnCompleted(continuation);

                break;
            case TaskDataKind.ValueTask:
                this.valueTask.GetAwaiter().OnCompleted(continuation);

                break;
            case TaskDataKind.Task:
                ((Task)this.value!).GetAwaiter().OnCompleted(continuation);

                break;
            case TaskDataKind.Exception:
                ((ExceptionDispatchInfo)this.value!).Throw();

                break;
            default:
                continuation();

                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation)
    {
        continuation.Required();
        this.cancelToken.ThrowIf();

        switch (this.kind)
        {
            case TaskDataKind.HeapTask:
                ((HeapTask)this.value!).GetAwaiter().UnsafeOnCompleted(continuation);

                break;
            case TaskDataKind.ValueTask:
                this.valueTask.GetAwaiter().UnsafeOnCompleted(continuation);

                break;
            case TaskDataKind.Task:
                ((Task)this.value!).GetAwaiter().UnsafeOnCompleted(continuation);

                break;
            case TaskDataKind.Exception:
                ((ExceptionDispatchInfo)this.value!).Throw();

                break;
            default:
                continuation();

                break;
        }
    }

    private enum TaskDataKind : byte
    {
        None = 0,
        HeapTask = 1,
        ValueTask = 2,
        Task = 3,
        Action = 4,
        ActionWithToken = 5,
        Exception = 6,
    }
}

internal readonly struct TaskData<TResult>
{
    private readonly TaskDataResultKind kind;
    private readonly object? value;
    private readonly ValueTask<TResult> valueTask;
    private readonly TResult result;
    private readonly CancelToken cancelToken;

    internal TaskData(in ValueTask<TResult> task, CancelToken cancelToken)
    {
        this.kind = TaskDataResultKind.ValueTask;
        this.valueTask = task;
        this.value = null;
        this.result = default!;
        this.cancelToken = cancelToken;
    }

    internal TaskData(in Task<TResult> task, CancelToken cancelToken)
    {
        this.kind = TaskDataResultKind.Task;
        this.value = task.Required();
        this.valueTask = default;
        this.result = default!;
        this.cancelToken = cancelToken;
    }

    internal TaskData(HeapTask<TResult> task, CancelToken cancelToken)
    {
        this.kind = TaskDataResultKind.HeapTask;
        this.value = task.Required();
        this.valueTask = default;
        this.result = default!;
        this.cancelToken = cancelToken;
    }

    internal TaskData(in TResult result)
    {
        this.kind = TaskDataResultKind.Result;
        this.result = result;
        this.valueTask = default;
        this.value = null;
        this.cancelToken = default;
    }

    internal TaskData(in Func<TResult> factory)
    {
        this.kind = TaskDataResultKind.Factory;
        this.value = factory.Required();
        this.valueTask = default;
        this.result = default!;
        this.cancelToken = default;
    }

    internal TaskData(in Func<CancelToken, TResult> factory, CancelToken cancelToken)
    {
        this.kind = TaskDataResultKind.FactoryWithToken;
        this.value = factory.Required();
        this.valueTask = default;
        this.result = default!;
        this.cancelToken = cancelToken;
    }

    internal TaskData(ExceptionDispatchInfo exception)
    {
        this.kind = TaskDataResultKind.Exception;
        this.value = exception.Required();
        this.valueTask = default;
        this.result = default!;
        this.cancelToken = default;
    }

    internal TaskData(in Func<StackTask<TResult>> factory)
    {
        this.kind = TaskDataResultKind.TaskFactory;
        this.value = factory.Required();
        this.valueTask = default;
        this.result = default!;
        this.cancelToken = default;
    }

    internal TaskData(in Func<CancelToken, StackTask<TResult>> factory, CancelToken cancelToken)
    {
        this.kind = TaskDataResultKind.TaskFactoryWithToken;
        this.value = factory.Required();
        this.valueTask = default;
        this.result = default!;
        this.cancelToken = cancelToken;
    }

    public bool HasValue => this.kind != TaskDataResultKind.None;

    public readonly void ThrowIf() => this.cancelToken.ThrowIf();

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.kind == TaskDataResultKind.None || this.cancelToken.IsRequested)
                return true;

            return this.kind switch
            {
                TaskDataResultKind.ValueTask => this.valueTask.IsCompleted,
                TaskDataResultKind.Task => ((Task<TResult>)this.value!).IsCompleted,
                TaskDataResultKind.HeapTask => ((HeapTask<TResult>)this.value!).IsCompleted,
                _ => true,
            };
        }
    }

    public bool IsCompletedSuccessfully
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.cancelToken.IsRequested)
                return false;

            if (this.kind == TaskDataResultKind.None)
                return true;

            return this.kind switch
            {
                TaskDataResultKind.ValueTask => this.valueTask.IsCompletedSuccessfully,
                TaskDataResultKind.Task => ((Task<TResult>)this.value!).IsCompletedSuccessfully,
                TaskDataResultKind.Exception => false,
                TaskDataResultKind.HeapTask => ((HeapTask<TResult>)this.value!).IsCompletedSuccessfully,
                _ => true,
            };
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult GetResult()
    {
        this.cancelToken.ThrowIf();

        if (this.kind == TaskDataResultKind.Exception)
            ((ExceptionDispatchInfo)this.value!).Throw();

        return this.kind switch
        {
            TaskDataResultKind.ValueTask => this.valueTask.ConfigureAwait(false).GetAwaiter().GetResult(),
            TaskDataResultKind.Task => ((Task<TResult>)this.value!).ConfigureAwait(false).GetAwaiter().GetResult(),
            TaskDataResultKind.Result => this.result,
            TaskDataResultKind.Factory => ((Func<TResult>)this.value!)(),
            TaskDataResultKind.FactoryWithToken => ((Func<CancelToken, TResult>)this.value!)(this.cancelToken),
            TaskDataResultKind.TaskFactory => ((Func<StackTask<TResult>>)this.value!)().GetAwaiter().GetResult(),
            TaskDataResultKind.TaskFactoryWithToken =>
                ((Func<CancelToken, StackTask<TResult>>)this.value!)(this.cancelToken).GetAwaiter().GetResult(),
            TaskDataResultKind.HeapTask => ((HeapTask<TResult>)this.value!).GetAwaiter().GetResult(),
            _ => default!,
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        continuation.Required();
        this.cancelToken.ThrowIf();

        switch (this.kind)
        {
            case TaskDataResultKind.ValueTask:
                this.valueTask.GetAwaiter().OnCompleted(continuation);

                break;
            case TaskDataResultKind.Task:
                ((Task<TResult>)this.value!).GetAwaiter().OnCompleted(continuation);

                break;
            case TaskDataResultKind.Exception:
                ((ExceptionDispatchInfo)this.value!).Throw();

                break;
            case TaskDataResultKind.HeapTask:
                ((HeapTask<TResult>)this.value!).GetAwaiter().OnCompleted(continuation);

                break;
            default:
                continuation();

                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation)
    {
        continuation.Required();
        this.cancelToken.ThrowIf();

        switch (this.kind)
        {
            case TaskDataResultKind.ValueTask:
                this.valueTask.GetAwaiter().UnsafeOnCompleted(continuation);

                break;
            case TaskDataResultKind.Task:
                ((Task<TResult>)this.value!).GetAwaiter().UnsafeOnCompleted(continuation);

                break;
            case TaskDataResultKind.Exception:
                ((ExceptionDispatchInfo)this.value!).Throw();

                break;
            case TaskDataResultKind.HeapTask:
                ((HeapTask<TResult>)this.value!).GetAwaiter().UnsafeOnCompleted(continuation);

                break;
            default:
                continuation();

                break;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetTaskFactory(out Func<StackTask<TResult>> factory)
    {
        if (this.kind == TaskDataResultKind.TaskFactory)
        {
            factory = (Func<StackTask<TResult>>)this.value!;

            return true;
        }

        factory = null!;

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetTaskFactory(out Func<CancelToken, StackTask<TResult>> factory, out CancelToken cancelToken)
    {
        if (this.kind == TaskDataResultKind.TaskFactoryWithToken)
        {
            factory = (Func<CancelToken, StackTask<TResult>>)this.value!;
            cancelToken = this.cancelToken;

            return true;
        }

        factory = null!;
        cancelToken = default;

        return false;
    }

    public Task<TResult> AsTask()
    {
        try
        {
            this.cancelToken.ThrowIf();

            return this.kind switch
            {
                TaskDataResultKind.None => Task.FromResult(default(TResult)!),
                TaskDataResultKind.ValueTask => this.valueTask.AsTask(),
                TaskDataResultKind.Task => (Task<TResult>)this.value!,
                TaskDataResultKind.Result => Task.FromResult(this.result),
                TaskDataResultKind.Factory => Task.FromResult(((Func<TResult>)this.value!)()),
                TaskDataResultKind.FactoryWithToken => Task.FromResult(((Func<CancelToken, TResult>)this.value!)(this.cancelToken)),
                TaskDataResultKind.Exception => Task.FromException<TResult>(((ExceptionDispatchInfo)this.value!).SourceException),
                TaskDataResultKind.TaskFactory => ((Func<StackTask<TResult>>)this.value!)().AsTask(),
                TaskDataResultKind.TaskFactoryWithToken => ((Func<CancelToken, StackTask<TResult>>)this.value!)(this.cancelToken).AsTask(),
                TaskDataResultKind.HeapTask => ((HeapTask<TResult>)this.value!).AsTask(),
                _ => Task.FromResult(default(TResult)!),
            };
        }
        catch (Exception exception)
        {
            return Task.FromException<TResult>(exception);
        }
    }

    private enum TaskDataResultKind : byte
    {
        None = 0,
        ValueTask = 1,
        Task = 2,
        Result = 3,
        Factory = 4,
        FactoryWithToken = 5,
        Exception = 6,
        TaskFactory = 7,
        TaskFactoryWithToken = 8,
        HeapTask = 9,
    }
}
