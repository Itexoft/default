// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Itexoft.Native;

namespace Itexoft.IO.Splicing;

internal static unsafe partial class SpliceStreamRuntimeImports
{
    static SpliceStreamRuntimeImports() => NativeLibraryLoader.RegisterResolver();

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(GetRuntime)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus GetRuntime(nint token, out nint runtime);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Cue)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Cue(nint runtime, out nint node);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Port)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Port(nint runtime, out nint node);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(In)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus In(nint runtime, out nint node);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(EnsureOwner)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus EnsureOwner(nint runtime, nint token);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(ScopePush)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus ScopePush(nint runtime);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(ScopePop)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus ScopePop(nint runtime);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Destroy)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Destroy(nint runtime);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Create)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Create(SpliceStreamHandle* callbacks, SpliceStreamOptionsNative* options);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(GetOptions)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus GetOptions(nint runtime, out SpliceStreamOptions* options);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Insert)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Insert(nint runtime, nint data, int length, out nint token);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Render)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Render(nint runtime, nint src, out nint token);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Cat)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Cat(nint runtime, nint* parts, int length, out nint token);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Concat)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Concat(nint runtime, nint left, nint right, out nint token);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Freeze)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Freeze(nint runtime, nint token, out nint frozen);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(AppendChunk)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus AppendChunk(nint runtime, nint promise, nint data, int length);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(AppendStream)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus AppendStream(nint runtime, nint promise, nint piece);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Bind)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Bind(nint runtime, nint hole, nint src);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Complete)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Complete(nint runtime, nint promise);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(Read)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus Read(nint runtime, nint token, nint dst, int length, out int read);

    [LibraryImport(NativeLibraryLoader.Name, EntryPoint = nameof(SpliceStream<>) + nameof(AppendItem)), UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial SpliceNativeStatus AppendItem(nint runtime, nint promiseToken, nint item);

    extension(nint handle)
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal nint SpliceStreamRuntimeHandle() => handle;
    }
}
