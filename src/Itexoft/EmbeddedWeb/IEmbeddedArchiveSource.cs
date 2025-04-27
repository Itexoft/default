// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.EmbeddedWeb;

/// <summary>
/// Abstraction of a static file archive source. Returned streams must support seeking.
/// </summary>
public interface IEmbeddedArchiveSource
{
    StackTask<IStreamRals> OpenArchiveAsync(CancelToken cancelToken);
}

/// <summary>
/// Factory helpers for archive sources.
/// </summary>
public static class EmbeddedArchiveSource
{
    public static IEmbeddedArchiveSource FromFactory(Func<CancelToken, StackTask<IStreamRals>> factory) => new DelegateArchiveSource(factory);

    public static IEmbeddedArchiveSource FromStream(Stream stream, bool leaveOpen = false)
    {
        stream.Required();
        var memory = new MemoryStream();
        stream.CopyTo(memory);

        if (!leaveOpen)
            stream.Dispose();

        memory.Seek(0, SeekOrigin.Begin);
        var buffer = memory.ToArray();

        return new DelegateArchiveSource(_ => new(new MemoryStream(buffer, false).AsStreamRals()));
    }

    public static IEmbeddedArchiveSource FromResource(Assembly assembly, string resourceName)
    {
        assembly.Required();
        resourceName.Required();

        return new DelegateArchiveSource(async ct =>
        {
            await Task.Yield();

            var stream = assembly.GetManifestResourceStream(resourceName)
                         ?? throw new InvalidOperationException($"Resource '{resourceName}' not found in assembly '{assembly.FullName}'.");

            return await EnsureSeekableAsync(stream.AsStreamRals(), ct);
        });
    }

    public static IEmbeddedArchiveSource FromFile(string filePath)
    {
        filePath.Required();

        return new DelegateArchiveSource(async ct =>
        {
            var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read).AsStreamRals();

            return await EnsureSeekableAsync(stream, ct);
        });
    }

    private static async StackTask<IStreamRals> EnsureSeekableAsync(IStreamRals stream, CancelToken cancelToken)
    {
        if (IsSeekable(stream, out var streamS))
        {
            if (streamS.Position != 0)
                streamS.Seek(0, SeekOrigin.Begin);

            return stream;
        }

        var buffer = new RamAsyncStream();
        await stream.CopyToAsync(buffer, cancelToken);
        await stream.DisposeAsync();
        buffer.Seek(0, SeekOrigin.Begin);

        return buffer;
    }

    private static bool IsSeekable(IStreamRals stream, out IStreamS streamS)
    {
        streamS = (IStreamS)stream;

        if (stream is IStreamBcl bcl && !bcl.CanSeek)
            return false;

        return true;
    }

    private sealed class DelegateArchiveSource(Func<CancelToken, StackTask<IStreamRals>> factory) : IEmbeddedArchiveSource
    {
        private readonly Func<CancelToken, StackTask<IStreamRals>> factory = factory ?? throw new ArgumentNullException(nameof(factory));

        public async StackTask<IStreamRals> OpenArchiveAsync(CancelToken cancelToken)
        {
            var stream = await this.factory(cancelToken);

            if (!IsSeekable(stream, out var streamS))
                stream = await EnsureSeekableAsync(stream, cancelToken);
            else if (streamS.Position != 0)
                streamS.Seek(0, SeekOrigin.Begin);

            return stream;
        }
    }
}
