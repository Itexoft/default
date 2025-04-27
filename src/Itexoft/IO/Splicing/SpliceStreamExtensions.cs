// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Splicing;

public static class SpliceStreamExtensions
{
    extension<T>(Clip<T>) where T : unmanaged
    {
        public static Clip<T> operator +(Clip<T> a, Clip<T> b)
        {
            SpliceKernel.Concat(a, b, out var c).ThrowIf();
            
            return c;
        }

        public static Clip<T> operator ~(Clip<T> x)
        {
            SpliceKernel.Freeze(x, out var c).ThrowIf();

            return c;
        }
    }

    extension<T>(ref Clip<T> p) where T : unmanaged
    {
        public void operator += (ReadOnlySpan<T> chunk) => SpliceKernel.Append(ref p, chunk).ThrowIf();
        public void operator += (T item) => SpliceKernel.Append(ref p, in item).ThrowIf();
        public void operator += (Clip<T> piece) => SpliceKernel.Append(ref p, piece).ThrowIf();
        public void Complete() => SpliceKernel.Complete(ref p).ThrowIf();
    }

    private static void ThrowIf(this SpliceNativeStatus status)
    {
        if (status == SpliceNativeStatus.Disposed)
            throw new ObjectDisposedException(nameof(SpliceStream));

        if (status != SpliceNativeStatus.Ok)
            throw new SpliceNativeException(status);
    }
}
