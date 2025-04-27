// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;

namespace Itexoft.Threading.Parallelism;

public static class Parallelism
{
    public static TResult[] SelectOrdered<T, TResult>(
        IReadOnlyList<T> source,
        Func<T, CancelToken, TResult> selector,
        int maxDegreeOfParallelism = 0,
        CancelToken cancelToken = default)
    {
        source.Required();
        selector.Required();
        cancelToken.ThrowIf();

        if (source.Count == 0)
            return [];

        maxDegreeOfParallelism = maxDegreeOfParallelism <= 0 ? Environment.ProcessorCount : maxDegreeOfParallelism;
        maxDegreeOfParallelism = Math.Max(1, Math.Min(source.Count, maxDegreeOfParallelism));

        if (maxDegreeOfParallelism == 1)
        {
            var sequential = new TResult[source.Count];

            for (var i = 0; i < source.Count; i++)
                sequential[i] = selector(source[i], cancelToken);

            return sequential;
        }

        cancelToken = cancelToken.Branch();
        var results = new TResult[source.Count];
        var workersRemaining = maxDegreeOfParallelism;
        var nextIndex = -1;
        var completion = new Latch();
        var failure = new Latch();
        Exception? exception = null;

        for (var i = 0; i < maxDegreeOfParallelism; i++)
            ThreadPool.UnsafeQueueUserWorkItem(callback, (object?)null, false);

        completion.Wait();
        cancelToken.Cancel();
        exception?.Rethrow();

        return results;

        void callback(object? _)
        {
            try
            {
                while (!failure)
                {
                    cancelToken.ThrowIf();
                    var index = Interlocked.Increment(ref nextIndex);

                    if (index >= source.Count)
                        break;

                    results[index] = selector(source[index], cancelToken);
                }
            }
            catch (Exception ex)
            {
                if (failure.Try())
                    exception = ex;
            }
            finally
            {
                if (Interlocked.Decrement(ref workersRemaining) == 0)
                    completion.Try();
            }
        }
    }

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
