// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.Tasks;

internal abstract class TaskCore
{
    private Latch completed = new();
    private Action? continuation;
    private ExceptionDispatchInfo? exceptionDispatchInfo;

    public bool IsCompleted => this.completed;
    public bool IsCompletedSuccessfully => this.IsCompleted && this.exceptionDispatchInfo is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult() => this.Complete();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception)
    {
        exception.Required();
        this.exceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
        this.Complete();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        continuation.Required();

        if (Interlocked.CompareExchange(ref this.continuation, continuation, null) is not null)
            throw new InvalidOperationException("Multiple awaiters are not supported.");

        if (this.completed)
            ((Action?)Interlocked.Exchange(ref this.continuation, null))?.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation) => this.OnCompleted(continuation);

    public void GetResult()
    {
        if (!this.completed)
        {
            var sw = new SpinWait();

            while (!this.completed)
                sw.SpinOnce();
        }

        var exception = this.exceptionDispatchInfo;

        this.Release();

        exception?.Throw();
    }

    protected void ResetCore()
    {
        this.exceptionDispatchInfo = null;
        this.continuation = null;
        this.completed.Reset();
    }

    private void Complete()
    {
        if (!this.completed.Try())
            return;

        Interlocked.Exchange(ref this.continuation, null)?.Invoke();
    }

    protected abstract void Release();
}

internal sealed class TaskStateMachineBox<TStateMachine> : TaskCore where TStateMachine : IAsyncStateMachine
{
    internal readonly Action moveNext;
    internal TaskStateMachineBox<TStateMachine>? next;
    internal TStateMachine? stateMachine;

    internal TaskStateMachineBox() => this.moveNext = this.MoveNext;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Init(ref TStateMachine stateMachine) => this.stateMachine = stateMachine;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void MoveNext() => this.stateMachine!.MoveNext();

    protected override void Release()
    {
        this.ResetCore();
        this.stateMachine = default;
        TaskStateMachineBoxPool<TStateMachine>.Return(this);
    }
}

internal static class TaskStateMachineBoxPool<TStateMachine> where TStateMachine : IAsyncStateMachine
{
    private static TaskStateMachineBox<TStateMachine>? head;

    internal static TaskStateMachineBox<TStateMachine> Rent()
    {
        while (true)
        {
            var current = Volatile.Read(ref head);

            if (current is null)
                return new TaskStateMachineBox<TStateMachine>();

            var next = current.next;

            if (Interlocked.CompareExchange(ref head, next, current) == current)
            {
                current.next = null;

                return current;
            }
        }
    }

    internal static void Return(TaskStateMachineBox<TStateMachine> box)
    {
        while (true)
        {
            var current = Volatile.Read(ref head);
            box.next = current;

            if (Interlocked.CompareExchange(ref head, box, current) == current)
                return;
        }
    }
}

internal abstract class TaskCore<TResult>
{
    private Latch completed = new();
    private Action? continuation;
    private ExceptionDispatchInfo? exceptionDispatchInfo;
    private TResult result = default!;

    public bool IsCompleted => this.completed;
    public bool IsCompletedSuccessfully => this.IsCompleted && this.exceptionDispatchInfo is null;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult(TResult result)
    {
        this.result = result;
        this.Complete();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception)
    {
        exception.Required();
        this.exceptionDispatchInfo = ExceptionDispatchInfo.Capture(exception);
        this.Complete();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnCompleted(Action continuation)
    {
        continuation.Required();

        if (Interlocked.CompareExchange(ref this.continuation, continuation, null) is not null)
            throw new InvalidOperationException("Multiple awaiters are not supported.");

        if (this.completed)
            ((Action?)Interlocked.Exchange(ref this.continuation, null))?.Invoke();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void UnsafeOnCompleted(Action continuation) => this.OnCompleted(continuation);

    public TResult GetResult()
    {
        if (!this.completed)
        {
            var sw = new SpinWait();

            while (!this.completed)
                sw.SpinOnce();
        }

        var exception = this.exceptionDispatchInfo;
        var value = this.result;

        this.Release();

        exception?.Throw();

        return value;
    }

    protected void ResetCore()
    {
        this.exceptionDispatchInfo = null;
        this.continuation = null;
        this.result = default!;
        this.completed.Reset();
    }

    private void Complete()
    {
        if (!this.completed.Try())
            return;

        Interlocked.Exchange(ref this.continuation, null)?.Invoke();
    }

    protected abstract void Release();
}

internal sealed class TaskStateMachineBox<TResult, TStateMachine> : TaskCore<TResult> where TStateMachine : IAsyncStateMachine
{
    internal readonly Action moveNext;
    internal TaskStateMachineBox<TResult, TStateMachine>? next;
    internal TStateMachine? stateMachine;

    internal TaskStateMachineBox() => this.moveNext = this.MoveNext;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Init(ref TStateMachine stateMachine) => this.stateMachine = stateMachine;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void MoveNext() => this.stateMachine!.MoveNext();

    protected override void Release()
    {
        this.ResetCore();
        this.stateMachine = default;
        TaskStateMachineBoxPool<TResult, TStateMachine>.Return(this);
    }
}

internal static class TaskStateMachineBoxPool<TResult, TStateMachine> where TStateMachine : IAsyncStateMachine
{
    private static TaskStateMachineBox<TResult, TStateMachine>? head;

    internal static TaskStateMachineBox<TResult, TStateMachine> Rent()
    {
        while (true)
        {
            var current = Volatile.Read(ref head);

            if (current is null)
                return new TaskStateMachineBox<TResult, TStateMachine>();

            var next = current.next;

            if (Interlocked.CompareExchange(ref head, next, current) == current)
            {
                current.next = null;

                return current;
            }
        }
    }

    internal static void Return(TaskStateMachineBox<TResult, TStateMachine> box)
    {
        while (true)
        {
            var current = Volatile.Read(ref head);
            box.next = current;

            if (Interlocked.CompareExchange(ref head, box, current) == current)
                return;
        }
    }
}
