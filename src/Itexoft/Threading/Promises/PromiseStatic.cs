// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading.SysTimerInternal;

namespace Itexoft.Threading.Tasks;

partial class Promise
{
    public static TPromise WhenAny<TPromise>(TPromise promise1, TPromise promise2, CancelToken cancelToken) where TPromise : IPromise
    {
        cancelToken.ThrowIf();

        var awaiter = PromiseAwaiter<TPromise>.Uncompleted();

        ThreadPool.UnsafeQueueUserWorkItem(callback, (promise1, awaiter.CompleteInAction()!, cancelToken), false);
        ThreadPool.UnsafeQueueUserWorkItem(callback, (promise2, awaiter.CompleteInAction()!, cancelToken), false);

        var result = awaiter.GetResult();
        cancelToken.ThrowIf();

        return result;

        static void callback((TPromise promise, InAction<TPromise> completed, CancelToken cancelToken) cp)
        {
            cp.promise.GetAwaiter().WaitResult(cp.cancelToken);
            cp.completed(in cp.promise);
        }
    }

    public static TPromise WhenAny<TPromise>(IEnumerable<TPromise> promises, CancelToken cancelToken) where TPromise : IPromise
    {
        cancelToken.ThrowIf();

        var awaiter = PromiseAwaiter<TPromise>.Uncompleted();

        foreach (var promise in promises)
            ThreadPool.UnsafeQueueUserWorkItem(callback, (promise, awaiter.CompleteInAction()!, cancelToken), false);

        var result = awaiter.GetResult();
        cancelToken.ThrowIf();

        return result;

        static void callback((TPromise promise, InAction<TPromise> completed, CancelToken cancelToken) cp)
        {
            cp.promise.GetAwaiter().WaitResult(cp.cancelToken);
            cp.completed(in cp.promise);
        }
    }

    public static async Promise WhenAll<TPromise>(TPromise promise1, TPromise promise2, CancelToken cancelToken = default) where TPromise : IPromise
    {
        cancelToken.ThrowIf();
        await promise1;
        cancelToken.ThrowIf();
        await promise2;
        cancelToken.ThrowIf();
    }

    public static async Promise WhenAll<TPromise>(IEnumerable<TPromise> promises, CancelToken cancelToken = default) where TPromise : IPromise
    {
        foreach (var promise in promises)
        {
            cancelToken.ThrowIf();
            await promise;
        }
    }

    public static Promise Run(Action func, bool preferLocal = false, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        var source = new Promise(func, true);
        ThreadPool.UnsafeQueueUserWorkItem(static s => s(), source.GetAwaiter().CompleteAction()!, preferLocal);

        return source;
    }

    public static Promise<T> Run<T>(Func<T> func, bool preferLocal = false, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        var source = new Promise<T>(func, true);
        ThreadPool.UnsafeQueueUserWorkItem(static s => s(), source.GetAwaiter().CompleteAction()!, preferLocal);

        return source;
    }

    public static async Promise RunAsync(Func<Promise> func, bool preferLocal = false, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        var source = new ValuePromise<Promise>(func);
        ThreadPool.UnsafeQueueUserWorkItem(static t => t(), source.Complete!, preferLocal);
        await await source;
    }

    public static async Promise<T> RunAsync<T>(Func<Promise<T>> func, bool preferLocal = false, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        var source = new ValuePromise<Promise<T>>(func);
        ThreadPool.UnsafeQueueUserWorkItem(static t => t(), source.Complete!, preferLocal);

        return await await source;
    }

    public static ValuePromise Delay(int millisecondsDelay, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();

        if (millisecondsDelay == 0)
            return ValuePromise.Completed;

        if (millisecondsDelay < 0)
            return cancelToken;

        var source = new ValuePromise(false);
        SysTimer.New(millisecondsDelay, true, source.Complete!).Start();

        return source;
    }


    public static ValuePromise Delay(TimeSpan delay, CancelToken cancelToken = default) => Delay(delay.TimeoutMilliseconds, cancelToken);

    public static ValuePromise Yield()
    {
        var result = new ValuePromise(false);
        ThreadPool.UnsafeQueueUserWorkItem(static s => s(), result.Complete!, false);

        return result;
    }
}
