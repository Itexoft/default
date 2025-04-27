// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Itexoft.Threading.Tasks;

[AsyncMethodBuilder(typeof(PromiseMethodBuilder)), DebuggerNonUserCode, DebuggerStepThrough]
public sealed partial class Promise : IPromise
{
    private PromiseAwaiter awaiter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Promise(bool completed) => this.awaiter = completed ? PromiseAwaiter.Completed() : PromiseAwaiter.Uncompleted();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Promise(Exception exception) => this.awaiter = PromiseAwaiter.CompletedException(exception);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Promise(PromiseAwaiter awaiter) => this.awaiter = awaiter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Promise(Action factory, bool promise) => this.awaiter = PromiseAwaiter.UncompletedAction(factory, promise);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Promise(Action factory) => this.awaiter = PromiseAwaiter.UncompletedAction(factory, false);

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

    public static Promise Completed { get; } = new(true);

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this.awaiter.IsCompleted;
    }

    static TPromise IPromise.FromException<TPromise>(Exception exception) => (TPromise)(IPromise)new Promise(exception);
    static TPromise IPromise.FromAwaiter<TPromise>(in PromiseAwaiter awaiter) => (TPromise)(IPromise)new Promise(awaiter);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PromiseAwaiter GetAwaiter() => this.awaiter;

    public static implicit operator Promise(ValueTask task) => AwaitTask(task.AsTask());
    public static implicit operator Promise(Task task) => AwaitTask(task);

    private static async Promise AwaitTask(Task task) => await task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Promise FromException(Exception exception) => new(exception);

    internal void Complete() => this.awaiter.Complete();

    public void Wait(CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        this.awaiter.GetResult();
        cancelToken.ThrowIf();
    }
}
