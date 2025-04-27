// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Itexoft.Extensions;

public static class CommonExtensions
{
    extension(Index index)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public nuint GetOffset(nuint length)
        {
            var offset = checked((nuint)index.Value);

            if (index.IsFromEnd)
                offset += length + 1;

            return offset;
        }
    }

    extension(Range range)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public (nuint Start, nuint Length) GetOffsetAndLength(nuint length)
        {
            var start = range.Start.GetOffset(length);
            var end = range.End.GetOffset(length);

            if (end > length || start > end)
                throw new IndexOutOfRangeException();

            return (start, end - start);
        }
    }

    extension(TimeSpan timeSpan)
    {
        public bool IsInfinite => timeSpan == Timeout.InfiniteTimeSpan;
        public bool IsZero => timeSpan == TimeSpan.Zero;

        public int TimeoutMilliseconds => timeSpan == TimeSpan.Zero ? 0 :
            timeSpan == Timeout.InfiniteTimeSpan ? Timeout.Infinite : checked((int)Math.Ceiling(timeSpan.TotalMilliseconds));
    }

    extension(IDisposable disposable)
    {
        public bool TryDispose()
        {
            if (disposable is null)
                return false;

            try
            {
                disposable.Dispose();

                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    extension(IAsyncDisposable disposable)
    {
        public async ValueTask<bool> TryDisposeAsync(bool continueOnCapturedContext = false)
        {
            if (disposable is null)
                return false;

            try
            {
                await disposable.DisposeAsync().ConfigureAwait(continueOnCapturedContext);

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
    
    extension(Exception? exception)
    {
        public bool IsCancel => exception is OperationCanceledException;
        public bool IsNotCancel => exception is not OperationCanceledException;

        /// <summary>
        /// Rethrows an exception while preserving the original stack trace.
        /// </summary>
        public Exception Rethrow()
        {
            if (exception is not null)
                ExceptionDispatchInfo.Capture(exception).Throw();

            return exception!;
        }
    }
}
