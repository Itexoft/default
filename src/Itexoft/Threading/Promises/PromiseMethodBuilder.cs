// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Tasks;

public readonly struct PromiseMethodBuilder()
{
    public Promise Task { get; } = new(false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PromiseMethodBuilder Create() => new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult() => this.Task.GetAwaiter().Complete();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception) => this.Task.GetAwaiter().Complete(exception);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : struct, INotifyCompletion where TStateMachine : IAsyncStateMachine => awaiter.OnCompleted(stateMachine.MoveNext);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : struct, ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine =>
        awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
}

public readonly struct PromiseMethodBuilder<TResult>()
{
    public Promise<TResult> Task { get; } = new(false);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static PromiseMethodBuilder<TResult> Create() => new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetStateMachine(IAsyncStateMachine stateMachine) { }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetResult(TResult result) => this.Task.GetAwaiter().Complete(result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void SetException(Exception exception) => this.Task.GetAwaiter().Complete(exception);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : struct, INotifyCompletion where TStateMachine : IAsyncStateMachine => awaiter.OnCompleted(stateMachine.MoveNext);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
        where TAwaiter : struct, ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine =>
        awaiter.UnsafeOnCompleted(stateMachine.MoveNext);
}
