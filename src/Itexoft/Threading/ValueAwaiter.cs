// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;

namespace Itexoft.Threading;

public struct ValueAwaiter<T>()
{
    internal Latch latch = new();
    internal T value = default!;

    public static implicit operator bool(ValueAwaiter<T> awaiter) => awaiter.latch;
}

public static class ValueAwaiterExtensions
{
    extension<T>(ref ValueAwaiter<T> awaiter)
    {
        public T Wait()
        {
            awaiter.latch.SleepWait();

            return awaiter.value;
        }

        public void SetResult(T value)
        {
            if (awaiter.latch.Try())
                awaiter.value = value;
        }
    }
}
