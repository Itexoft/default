// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Threading.Tasks;

public static class PromiseExtensions
{
    extension<TPromise>(TPromise promise) where TPromise : IPromise
    {
        public bool Wait(TimeSpan completionTimeout, CancelToken cancelToken = default)
        {
            cancelToken.ThrowIf();

            if (completionTimeout == TimeSpan.Zero)
                return false;

            cancelToken.ThrowIf();

            if (completionTimeout <= Timeout.InfiniteTimeSpan)
            {
                cancelToken.ThrowIf();
                promise.GetAwaiter().GetResult();

                return true;
            }

            var timeout = cancelToken.Branch(completionTimeout);
            Promise.WhenAny(promise, TPromise.FromAwaiter<TPromise>(timeout), cancelToken).GetAwaiter().GetResult();

            return !timeout.IsTimedOut;
        }

        public TPromise WaitAsync(TimeSpan completionTimeout, CancelToken cancelToken = default)
        {
            cancelToken.ThrowIf();

            if (completionTimeout == TimeSpan.Zero)
                return TPromise.FromException<TPromise>(new TimeoutException());

            if (completionTimeout <= Timeout.InfiniteTimeSpan)
                return promise;

            var timeout = cancelToken.Branch(completionTimeout);
            Promise.WhenAny(promise, TPromise.FromAwaiter<TPromise>(timeout), cancelToken).GetAwaiter().WaitResult(cancelToken);

            if (timeout.IsTimedOut)
                return TPromise.FromException<TPromise>(new TimeoutException());

            return promise;
        }
    }
}
