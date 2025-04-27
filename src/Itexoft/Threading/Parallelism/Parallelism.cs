// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.Parallelism;

public static class Parallelism
{
    public static bool Any<T>(IEnumerable<T> source, Func<T, CancelToken, bool> predicate, out T result, CancelToken cancelToken = default)
    {
        cancelToken.ThrowIf();
        cancelToken = cancelToken.Branch();

        var winner = new Latch();
        var enumerationCompleted = new Latch();
        Exception? exception = null;
        T newResult = default!;
        long total = 0;
        var state = 0;

        try
        {
            using var enumerator = source.GetEnumerator();

            while (Atomic.Read(ref state) == 0 && enumerator.MoveNext())
            {
                Interlocked.Increment(ref total);

                try
                {
                    ThreadPool.UnsafeQueueUserWorkItem(callback, enumerator.Current, false);
                }
                catch
                {
                    Interlocked.Decrement(ref total);

                    throw;
                }
            }

            if (Atomic.Read(ref state) == 0)
                cancelToken.ThrowIf();
        }
        catch (Exception ex)
        {
            publishFailure(ex);
        }
        finally
        {
            enumerationCompleted.Try();
        }

        for (var i = 0; Atomic.Read(ref state) == 0;)
        {
            if (enumerationCompleted && Atomic.Read(ref total) == 0)
                Atomic.Write(ref state, 3);
            else
                Spin.Wait(ref i);
        }

        result = newResult;
        var resultException = exception;
        cancelToken.Cancel();

        if (Atomic.Read(ref state) == 1)
            return true;

        if (Atomic.Read(ref state) == 2)
        {
            resultException?.Rethrow();

            return false;
        }

        if (Atomic.Read(ref state) == 3)
            return false;

        throw new InvalidOperationException();

        void callback(T item)
        {
            try
            {
                cancelToken.ThrowIf();

                if (Atomic.Read(ref state) == 0 && predicate(item, cancelToken))
                {
                    cancelToken.ThrowIf();

                    if (winner.Try())
                    {
                        newResult = item;
                        Atomic.Write(ref state, 1);
                    }
                }
            }
            catch (Exception ex)
            {
                publishFailure(ex);
            }
            finally
            {
                Interlocked.Decrement(ref total);
            }
        }

        void publishFailure(Exception ex)
        {
            if (winner.Try())
            {
                exception = ex;
                Atomic.Write(ref state, 2);
            }
        }
    }
}
