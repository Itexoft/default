// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Threading.Tasks;

public readonly struct ValuePromise
{
    public static ValuePromise Completed { get; } = new(true);
    public static ValuePromise<TResult> FromResult<TResult>(in TResult result) => PromiseAwaiter<TResult>.Completed(in result);

    internal ValuePromise(in PromiseAwaiter awaiter) => this.awaiter = awaiter;
    private readonly PromiseAwaiter awaiter;
    public PromiseAwaiter GetAwaiter() => this.awaiter;

    public bool IsCompleted => this.awaiter.IsCompleted;

    public ValuePromise(bool completed) => this.awaiter = completed ? PromiseAwaiter.Completed() : PromiseAwaiter.Uncompleted();

    public static implicit operator ValuePromise(in PromiseAwaiter awaiter) => new(awaiter);

    public Action? Complete => this.awaiter.CompleteAction();
}

public readonly struct ValuePromise<TResult>
{
    public static ValuePromise<TResult> Completed { get; } = new(PromiseAwaiter<TResult>.Completed(default!));

    public static ValuePromise<TResult> FromResult(in TResult result) => PromiseAwaiter<TResult>.Completed(in result);
    internal ValuePromise(in PromiseAwaiter<TResult> awaiter) => this.awaiter = awaiter;
    private readonly PromiseAwaiter<TResult> awaiter;

    public PromiseAwaiter<TResult> GetAwaiter() => this.awaiter;

    public bool IsCompleted => this.awaiter.IsCompleted;

    public ValuePromise(Func<TResult> factory) => this.awaiter = PromiseAwaiter<TResult>.UncompletedFunc(factory, true);

    public static implicit operator ValuePromise<TResult>(in PromiseAwaiter<TResult> awaiter) => new(awaiter);
    public static implicit operator ValuePromise<TResult>(in TResult result) => PromiseAwaiter<TResult>.Completed(in result);

    public Action? Complete => this.awaiter.CompleteAction();
}
