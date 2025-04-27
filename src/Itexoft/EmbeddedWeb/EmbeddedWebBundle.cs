// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;
using Itexoft.Threading.Tasks;

namespace Itexoft.EmbeddedWeb;

internal sealed class EmbeddedWebBundle(string bundleId, Assembly assembly, string resourcePrefix)
{
    private readonly Assembly assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
    private AtomicLock initializationLock = new AtomicLock();
    private readonly string resourcePrefix = NormalizeResourcePrefix(resourcePrefix);

    public string BundleId { get; } = bundleId ?? throw new ArgumentNullException(nameof(bundleId));

    public EmbeddedWebContent? Content { get; private set; }

    public EmbeddedWebContent GetContent(CancelToken cancelToken)
    {
        cancelToken.ThrowIf();
        if (this.Content is not null)
            return this.Content;

        using (initializationLock.Enter())
        {

            if (this.Content is not null)
                return this.Content;

            this.Content = this.LoadContent();

            return this.Content;
        }

    }

    public EmbeddedWebStaticFile? TryGetFile(string relativePath, CancelToken cancelToken)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return null;

        var content = this.GetContent(cancelToken);

        return content.TryGetFile(relativePath, out var file) ? file : null;
    }

    private EmbeddedWebContent LoadContent()
    {
        var files = new Dictionary<string, EmbeddedWebStaticFile>(StringComparer.OrdinalIgnoreCase);

        foreach (var resourceName in this.assembly.GetManifestResourceNames())
        {
            var normalizedName = NormalizeResourceName(resourceName);

            if (!normalizedName.StartsWith(this.resourcePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (normalizedName.Length == this.resourcePrefix.Length)
                throw new InvalidOperationException($"Resource '{resourceName}' has no relative path for bundle '{this.BundleId}'.");

            var relativePath = NormalizeResourcePath(normalizedName[this.resourcePrefix.Length..]);

            if (string.IsNullOrWhiteSpace(relativePath))
                throw new InvalidOperationException($"Resource '{resourceName}' has no relative path for bundle '{this.BundleId}'.");

            if (files.ContainsKey(relativePath))
                throw new InvalidOperationException($"Duplicate resource path '{relativePath}' detected for bundle '{this.BundleId}'.");

            files[relativePath] = new EmbeddedWebStaticFile(this.ReadResourceBytes(resourceName), EmbeddedWebContent.DefaultLastModified);
        }

        if (files.Count == 0)
            throw new InvalidOperationException($"No embedded resources found for bundle '{this.BundleId}' using prefix '{this.resourcePrefix}'.");

        return new EmbeddedWebContent(files);
    }

    private byte[] ReadResourceBytes(string resourceName)
    {
        using var stream = this.assembly.GetManifestResourceStream(resourceName)
                           ?? throw new InvalidOperationException($"Resource '{resourceName}' not found in assembly '{this.assembly.FullName}'.");

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);

        return buffer.ToArray();
    }

    private static string NormalizeResourceName(string resourceName) => resourceName.Replace('\\', '/');

    private static string NormalizeResourcePath(string path)
    {
        path = path.Replace('\\', '/');
        path = path.Trim('/');

        return path;
    }

    private static string NormalizeResourcePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new ArgumentException("Resource prefix cannot be empty.", nameof(prefix));

        prefix = prefix.Replace('\\', '/');

        if (!prefix.EndsWith('/'))
            throw new InvalidOperationException("Resource prefix must end with '/'.");

        return prefix;
    }
}
