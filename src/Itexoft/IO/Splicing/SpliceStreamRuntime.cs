// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Splicing;

internal static unsafe class SpliceStreamRuntime
{
    public static void ScopePop(nint runtime) => SpliceStreamRuntimeImports.ScopePop(runtime);
    public static SpliceNativeStatus ScopePush(nint runtime) => SpliceStreamRuntimeImports.ScopePush(runtime);

    public static void Destroy(nint runtime) => SpliceStreamRuntimeImports.Destroy(runtime);

    public static nint Create(SpliceStreamHandle* input, SpliceStreamOptionsNative* options) => SpliceStreamRuntimeImports.Create(input, options);

    public static SpliceNativeStatus GetRuntime(nint token, out nint runtime) => SpliceStreamRuntimeImports.GetRuntime(token, out runtime);

    public static SpliceNativeStatus AppendChunk<T>(nint runtime, nint promiseToken, nint chunkPtr, int chunkLength) where T : unmanaged =>
        SpliceStreamRuntimeImports.AppendChunk(runtime, promiseToken, chunkPtr, chunkLength);

    public static SpliceNativeStatus AppendItem(nint runtime, nint promiseToken, nint item) =>
        SpliceStreamRuntimeImports.AppendItem(runtime, promiseToken, item);

    public static SpliceNativeStatus AppendStream(nint runtime, nint promiseToken, nint pieceToken) =>
        SpliceStreamRuntimeImports.AppendStream(runtime, promiseToken, pieceToken);

    public static SpliceNativeStatus Bind(nint runtime, nint holeToken, nint srcToken) =>
        SpliceStreamRuntimeImports.Bind(runtime, holeToken, srcToken);

    public static SpliceNativeStatus Read(nint runtime, nint token, nint dstPtr, int dstLength, out int result) =>
        SpliceStreamRuntimeImports.Read(runtime, token, dstPtr, dstLength, out result);

    public static SpliceNativeStatus Complete(nint runtime, nint promiseToken) => SpliceStreamRuntimeImports.Complete(runtime, promiseToken);

    public static SpliceNativeStatus Freeze(nint runtime, nint xToken, out nint frozen) =>
        SpliceStreamRuntimeImports.Freeze(runtime, xToken, out frozen);

    public static SpliceNativeStatus Concat(nint runtime, nint aToken, nint bToken, out nint token) =>
        SpliceStreamRuntimeImports.Concat(runtime, aToken, bToken, out token);

    public static SpliceNativeStatus Cat(nint runtime, nint* partsPtr, int partsLength, out nint token) =>
        SpliceStreamRuntimeImports.Cat(runtime, partsPtr, partsLength, out token);

    public static SpliceNativeStatus EnsureOwner(nint runtime, nint token) => SpliceStreamRuntimeImports.EnsureOwner(runtime, token);

    public static SpliceNativeStatus Insert(nint runtime, nint dataPtr, int dataLength, out nint node) =>
        SpliceStreamRuntimeImports.Insert(runtime, dataPtr, dataLength, out node);

    public static SpliceNativeStatus Cue(nint runtime, out nint token) => SpliceStreamRuntimeImports.Cue(runtime, out token);

    public static SpliceNativeStatus Port(nint runtime, out nint o) => SpliceStreamRuntimeImports.Port(runtime, out o);

    public static SpliceNativeStatus In(nint runtime, out nint node) => SpliceStreamRuntimeImports.In(runtime, out node);
}
