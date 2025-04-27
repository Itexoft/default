// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Threading.Tasks;

public interface IPromise
{
    bool IsCompleted { get; }
    PromiseAwaiter GetAwaiter();
    internal static abstract TPromise FromException<TPromise>(Exception exception) where TPromise : IPromise;
    internal static abstract TPromise FromAwaiter<TPromise>(in PromiseAwaiter awaiter) where TPromise : IPromise;
}

public interface IPromise<TResult> : IPromise
{
    PromiseAwaiter IPromise.GetAwaiter() => this.GetAwaiter();
    new PromiseAwaiter<TResult> GetAwaiter();
}
