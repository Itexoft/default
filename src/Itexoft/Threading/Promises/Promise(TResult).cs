// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Tasks;

[AsyncMethodBuilder(typeof(PromiseMethodBuilder<>)), DebuggerNonUserCode, DebuggerStepThrough]
public sealed class Promise<TResult> : IPromise<TResult>
{
    private PromiseAwaiter<TResult> awaiter;

    private Promise(Exception exception) => this.awaiter = PromiseAwaiter<TResult>.CompletedException(exception);
    public Promise(Func<TResult> func) => this.awaiter = PromiseAwaiter<TResult>.UncompletedFunc(func, false);
    internal Promise(Func<TResult> func, bool promise) => this.awaiter = PromiseAwaiter<TResult>.UncompletedFunc(func, promise);
    internal Promise(in PromiseAwaiter<TResult> awaiter) => this.awaiter = awaiter;

    internal Promise(bool completed)
    {
        if (completed)
            this.awaiter = PromiseAwaiter<TResult>.Completed();
        else
            this.awaiter = PromiseAwaiter<TResult>.Uncompleted();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Promise(in TResult value) => this.awaiter = PromiseAwaiter<TResult>.Completed(in value);

    public bool IsCompletedSuccessfully
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.awaiter.IsCompletedSuccessfully;
    }

    public bool IsFaulted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.awaiter.IsFaulted;
    }

    public Exception? Exception
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.awaiter.Exception;
    }

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.awaiter.IsCompleted;
    }

    static TPromise IPromise.FromException<TPromise>(Exception exception) => (TPromise)(IPromise)new Promise<TResult>(exception);
    static TPromise IPromise.FromAwaiter<TPromise>(in PromiseAwaiter awaiter) => (TPromise)(IPromise)new Promise<TResult>(awaiter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PromiseAwaiter<TResult> GetAwaiter() => this.awaiter;

    public static implicit operator Promise<TResult>(in TResult result) => new(in result);
    public static implicit operator Promise<TResult>(ValueTask<TResult> result) => AwaitTask(result.AsTask());
    public static implicit operator Promise<TResult>(Task<TResult> result) => AwaitTask(result);
    private static async Promise<TResult> AwaitTask(Task<TResult> task) => await task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref PromiseAwaiter<TResult> GetAwaiterRef() => ref this.awaiter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Promise<TResult> FromException(Exception exception) => new(exception);
}
