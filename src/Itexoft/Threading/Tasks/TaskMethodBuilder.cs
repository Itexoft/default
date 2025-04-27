// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Itexoft.Threading.Tasks;

public struct TaskMethodBuilder
{
    private TaskCore? core;
    private ExceptionDispatchInfo? exceptionDispatchInfo;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TaskMethodBuilder Create() => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult()
    {
        var core = this.core;

        if (core is not null)
        {
            core.SetResult();

            return;
        }

        this.exceptionDispatchInfo = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception)
    {
        var core = this.core;

        if (core is not null)
        {
            core.SetException(exception);

            return;
        }

        this.exceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
    }

    public StackTask Task
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            var core = this.core;

            if (core is not null)
                return new StackTask(core);

            var exceptionDispatchInfo = this.exceptionDispatchInfo;

            if (exceptionDispatchInfo is not null)
                return new StackTask(exceptionDispatchInfo);

            return default;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var box = this.GetOrCreateBox(ref stateMachine);
        var continuation = box.moveNext;
        var syncContext = SynchronizationContext.Current;

        if (syncContext is null || syncContext.GetType() == typeof(SynchronizationContext))
        {
            awaiter.OnCompleted(continuation);

            return;
        }

        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            awaiter.OnCompleted(continuation);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var box = this.GetOrCreateBox(ref stateMachine);
        var continuation = box.moveNext;
        var syncContext = SynchronizationContext.Current;

        if (syncContext is null || syncContext.GetType() == typeof(SynchronizationContext))
        {
            awaiter.UnsafeOnCompleted(continuation);

            return;
        }

        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            awaiter.UnsafeOnCompleted(continuation);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TaskStateMachineBox<TStateMachine> GetOrCreateBox<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
    {
        if (this.core is TaskStateMachineBox<TStateMachine> existing)
            return existing;

        var box = TaskStateMachineBoxPool<TStateMachine>.Rent();
        this.core = box;
        box.Init(ref stateMachine);

        return box;
    }
}

public struct TaskMethodBuilder<TResult>
{
    private TaskCore<TResult>? core;
    private ExceptionDispatchInfo? exceptionDispatchInfo;
    private TResult result;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static TaskMethodBuilder<TResult> Create() => default;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine =>
        stateMachine.MoveNext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult(TResult result)
    {
        if (this.core is not null)
        {
            this.core.SetResult(result);

            return;
        }

        this.result = result;
        this.exceptionDispatchInfo = null;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception)
    {
        if (this.core is not null)
        {
            this.core.SetException(exception);

            return;
        }

        this.exceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
    }

    public StackTask<TResult> Task
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.core is not null)
                return new StackTask<TResult>(this.core);

            if (this.exceptionDispatchInfo is not null)
                return StackTask<TResult>.FromException(this.exceptionDispatchInfo);

            return new StackTask<TResult>(this.result);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var box = this.GetOrCreateBox(ref stateMachine);
        var syncContext = SynchronizationContext.Current;

        if (syncContext is null || syncContext.GetType() == typeof(SynchronizationContext))
        {
            awaiter.OnCompleted(box.moveNext);

            return;
        }

        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            awaiter.OnCompleted(box.moveNext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
    {
        var box = this.GetOrCreateBox(ref stateMachine);
        var syncContext = SynchronizationContext.Current;

        if (syncContext is null || syncContext.GetType() == typeof(SynchronizationContext))
        {
            awaiter.UnsafeOnCompleted(box.moveNext);

            return;
        }

        SynchronizationContext.SetSynchronizationContext(null);

        try
        {
            awaiter.UnsafeOnCompleted(box.moveNext);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(syncContext);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private TaskStateMachineBox<TResult, TStateMachine> GetOrCreateBox<TStateMachine>(ref TStateMachine stateMachine)
        where TStateMachine : IAsyncStateMachine
    {
        if (this.core is TaskStateMachineBox<TResult, TStateMachine> existing)
            return existing;

        var box = TaskStateMachineBoxPool<TResult, TStateMachine>.Rent();
        this.core = box;
        box.Init(ref stateMachine);

        return box;
    }
}
