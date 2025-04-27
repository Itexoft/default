// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net;
using Itexoft.Net.Http;
using Itexoft.Threading;

namespace Itexoft.UI.Web.EmbeddedWeb;

public static class EmbeddedWebServer
{
    private const string resourceMarker = "EmbeddedWeb.";
    private static readonly ConcurrentDictionary<string, EmbeddedWebBundle> bundles = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterBundle(string bundleId, Assembly assembly, bool overwrite = false) =>
        RegisterBundle(bundleId, assembly, BuildDefaultResourcePrefix(bundleId), overwrite);

    public static void RegisterBundle(string bundleId, Assembly assembly, string resourcePrefix, bool overwrite = false)
    {
        if (string.IsNullOrWhiteSpace(bundleId))
            throw new ArgumentException("Bundle identifier cannot be empty.", nameof(bundleId));

        assembly.Required();

        resourcePrefix = NormalizeResourcePrefix(resourcePrefix);
        var bundle = new EmbeddedWebBundle(bundleId, assembly, resourcePrefix);

        if (!overwrite)
        {
            if (!bundles.TryAdd(bundleId, bundle))
                throw new InvalidOperationException($"Bundle '{bundleId}' is already registered.");
        }
        else
            bundles[bundleId] = bundle;
    }

    public static void RegisterBundlesFromAssembly(Assembly assembly)
    {
        assembly.Required();

        var prefixes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!TryExtractBundleId(resource, out var bundleId, out var prefix))
                continue;

            if (!prefixes.TryGetValue(bundleId, out var existing))
                prefixes[bundleId] = prefix;
            else if (!existing.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Bundle '{bundleId}' has conflicting resource prefixes: '{existing}' and '{prefix}'.");
        }

        foreach (var pair in prefixes)
        {
            if (bundles.ContainsKey(pair.Key))
                continue;

            RegisterBundle(pair.Key, assembly, pair.Value);
        }
    }

    internal static bool TryGetBundle(string bundleId, out EmbeddedWebBundle bundle) => bundles.TryGetValue(bundleId, out bundle!);

    public static EmbeddedWebHandle Start(
        string bundleId,
        NetIpEndpoint endpoint,
        Action<EmbeddedWebOptions>? configureOptions = null,
        CancelToken cancelToken = default)
    {
        if (!bundles.TryGetValue(bundleId, out var bundle))
            throw new InvalidOperationException($"Bundle '{bundleId}' is not registered.");

        var options = CreateOptions(configureOptions);
        var rpc = CreateRpcDispatcher(options);
        var server = new NetHttpServer(endpoint);

        var runToken = CancelToken.New();

        if (!cancelToken.IsNone)
            cancelToken.Register(() => runToken.Cancel());

        server.Ws((request, ct) => ServeWebSocket(request, options, rpc, ct));
        server.Http((request, ct) => ServeRequest(request, bundle, options, rpc, ct));
        var completion = server.Start(runToken);

        return new EmbeddedWebHandle(bundleId, server, runToken, completion);
    }

    private static EmbeddedWebOptions CreateOptions(Action<EmbeddedWebOptions>? configure)
    {
        var options = new EmbeddedWebOptions();
        configure?.Invoke(options);

        foreach (var name in options.DefaultFileNames)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidOperationException("Default file names cannot be empty.");
        }

        if (options.EnableSpaFallback && string.IsNullOrWhiteSpace(options.SpaFallbackFile))
            options.SpaFallbackFile = options.DefaultFileNames.FirstOrDefault();

        if (options.StaticFilesCacheDuration.HasValue && options.StaticFilesCacheDuration.Value < TimeSpan.Zero)
            throw new InvalidOperationException("Static file cache duration cannot be negative.");

        if (options.RpcHandler is not null || options.EnableRpcClientScriptInjection)
            ValidateRpcPrefix(options.RpcPathPrefix);

        if (options.EnableRpcClientScriptInjection && options.RpcHandler is null)
            throw new InvalidOperationException("RPC handler must be provided to enable client script injection.");

        if (options.EnableDebugResourceTicks)
        {
            if (!options.DebugResourceTicks.HasValue)
                throw new InvalidOperationException("Debug resource ticks must be provided when debug ticks are enabled.");

            if (options.DebugResourceTicks.Value < 0)
                throw new InvalidOperationException("Debug resource ticks must be non-negative.");
        }

        return options;
    }

    private static NetHttpResponse ServeRequest(
        NetHttpRequest request,
        EmbeddedWebBundle bundle,
        EmbeddedWebOptions options,
        EmbeddedWebRpcDispatcher? rpc,
        CancelToken cancelToken)
    {
        if (rpc is not null && EmbeddedWebRpcClientScript.IsScriptRequest(request.PathAndQuery.Path, options.RpcPathPrefix))
            return EmbeddedWebRpcClientScript.CreateResponse(request.Method);

        if (rpc is not null && TryGetRpcMethod(request.PathAndQuery.Path, options.RpcPathPrefix, out var rpcMethod))
            return rpc.Dispatch(rpcMethod, request, cancelToken);

        if (options.RequestHandler is EmbeddedWebRequestHandler requestHandler && requestHandler(request, cancelToken) is NetHttpResponse handled)
            return handled;

        if (!IsGetOrHead(request.Method))
            return new NetHttpResponse(NetHttpVersion.Version11, NetHttpStatus.MethodNotAllowed);

        var content = bundle.GetContent(cancelToken);
        var relativePath = NormalizePath(request.PathAndQuery.Path);

        if (TryResolveFile(content, relativePath, options, out var resolvedPath, out var file))
            return CreateFileResponse(request.Method, resolvedPath, file, options);

        if (options.EnableDirectoryBrowsing && TryBuildDirectoryListing(content, relativePath, out var listing))
            return listing;

        if (options.EnableSpaFallback && !string.IsNullOrWhiteSpace(options.SpaFallbackFile))
        {
            var spaPath = NormalizePath(options.SpaFallbackFile!);

            if (content.TryGetFile(spaPath, out var spaFile))
                return CreateFileResponse(request.Method, spaPath, spaFile, options);
        }

        return new NetHttpResponse(NetHttpStatus.NotFound);
    }

    private static NetHttpWebSocketSessionHandler? ServeWebSocket(
        NetHttpRequest request,
        EmbeddedWebOptions options,
        EmbeddedWebRpcDispatcher? rpc,
        CancelToken cancelToken)
    {
        if (rpc is not null && EmbeddedWebRpcClientScript.IsScriptRequest(request.PathAndQuery.Path, options.RpcPathPrefix))
            return null;

        if (rpc is not null && TryGetRpcMethod(request.PathAndQuery.Path, options.RpcPathPrefix, out _))
            return null;

        return options.WebSocketHandler?.Invoke(request, cancelToken);
    }

    private static EmbeddedWebRpcDispatcher? CreateRpcDispatcher(EmbeddedWebOptions options)
    {
        if (options.RpcHandler is null)
            return null;

        return new EmbeddedWebRpcDispatcher(options.RpcHandler);
    }

    private static void ValidateRpcPrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new InvalidOperationException("RPC path prefix cannot be empty.");

        if (!prefix.StartsWith('/'))
            throw new InvalidOperationException("RPC path prefix must start with '/'.");

        if (prefix.Length > 1 && prefix.EndsWith('/'))
            throw new InvalidOperationException("RPC path prefix must not end with '/'.");
    }

    private static bool TryGetRpcMethod(string path, string prefix, out string methodName)
    {
        methodName = string.Empty;

        if (string.IsNullOrEmpty(path))
            return false;

        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Length == prefix.Length)
            return true;

        if (path[prefix.Length] != '/')
            return false;

        var remainder = path[(prefix.Length + 1)..];

        if (string.IsNullOrWhiteSpace(remainder))
            return true;

        if (remainder.Contains('/'))
            return true;

        methodName = remainder;

        return true;
    }

    private static bool TryResolveFile(
        EmbeddedWebContent content,
        string relativePath,
        EmbeddedWebOptions options,
        out string resolvedPath,
        [NotNullWhen(true)] out EmbeddedWebStaticFile? file)
    {
        resolvedPath = string.Empty;
        file = null;

        if (!string.IsNullOrEmpty(relativePath) && content.TryGetFile(relativePath, out var resolved))
        {
            resolvedPath = relativePath;
            file = resolved;

            return true;
        }

        if (TryResolveDefaultFile(content, relativePath, options, out resolvedPath, out file))
            return true;

        return false;
    }

    private static bool TryResolveDefaultFile(
        EmbeddedWebContent content,
        string relativePath,
        EmbeddedWebOptions options,
        out string resolvedPath,
        [NotNullWhen(true)] out EmbeddedWebStaticFile? file)
    {
        resolvedPath = string.Empty;
        file = null;

        var directory = relativePath.Trim('/');

        if (!string.IsNullOrEmpty(directory) && !HasDirectory(content, directory))
            return false;

        foreach (var name in options.DefaultFileNames)
        {
            var candidate = string.IsNullOrEmpty(directory) ? name : $"{directory}/{name}";

            if (!content.TryGetFile(candidate, out var resolved))
                continue;

            resolvedPath = candidate;
            file = resolved;

            return true;
        }

        return false;
    }

    private static bool HasDirectory(EmbeddedWebContent content, string prefix)
    {
        foreach (var path in content.Paths)
        {
            if (path.Length <= prefix.Length)
                continue;

            if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            if (path[prefix.Length] == '/')
                return true;
        }

        return string.IsNullOrEmpty(prefix) && content.Paths.Count > 0;
    }

    private static NetHttpResponse CreateFileResponse(
        NetHttpMethod method,
        string relativePath,
        EmbeddedWebStaticFile file,
        EmbeddedWebOptions options)
    {
        var headers = new NetHttpHeaders
        {
            ContentType = GetContentType(relativePath),
        };

        headers["Last-Modified"] = file.LastModified.ToUniversalTime().ToString("R");

        if (options.StaticFilesCacheDuration.HasValue)
        {
            var seconds = (long)options.StaticFilesCacheDuration.Value.TotalSeconds;

            if (seconds < 0)
                throw new InvalidOperationException("Static file cache duration cannot be negative.");

            headers.CacheControl = $"public, max-age={seconds}";
        }

        options.OnPrepareResponse?.Invoke(new EmbeddedWebResponseContext(file, relativePath, method, headers));

        ReadOnlyMemory<byte> body = file.Content;

        if (options.EnableRpcClientScriptInjection && IsHtmlContent(headers.ContentType))
        {
            if (!EmbeddedWebRpcClientScript.TryInject(body, options.RpcPathPrefix, out var injected))
                return new NetHttpResponse(NetHttpStatus.InternalServerError);

            body = injected;
        }

        if (options.EnableDebugResourceTicks && IsHtmlContent(headers.ContentType))
        {
            var ticks = options.DebugResourceTicks!.Value;

            if (!EmbeddedWebResourceTicksInjector.TryInject(body, ticks, out var injected))
                return new NetHttpResponse(NetHttpStatus.InternalServerError);

            body = injected;
        }

        return new NetHttpResponse(NetHttpStatus.Ok, headers, new StreamTrs<byte>(body));
    }

    private static bool TryBuildDirectoryListing(EmbeddedWebContent content, string relativePath, out NetHttpResponse response)
    {
        response = default;
        var entries = CollectDirectoryEntries(content, relativePath);

        if (entries.Count == 0)
            return false;

        entries.Sort(static (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Name, y.Name));

        var prefix = string.IsNullOrEmpty(relativePath) ? "/" : $"/{relativePath.Trim('/')}/";
        var html = new StringBuilder();

        html.Append("<!doctype html><html><body><ul>");

        foreach (var entry in entries)
        {
            var display = entry.IsDirectory ? entry.Name + "/" : entry.Name;
            var href = prefix + entry.Name + (entry.IsDirectory ? "/" : string.Empty);

            html.Append("<li><a href=\"");
            html.Append(EscapeHtml(href));
            html.Append("\">");
            html.Append(EscapeHtml(display));
            html.Append("</a></li>");
        }

        html.Append("</ul></body></html>");

        var headers = new NetHttpHeaders
        {
            ContentType = "text/html; charset=utf-8",
        };

        response = new NetHttpResponse(NetHttpStatus.Ok, headers, new StreamTrs<byte>(Encoding.UTF8.GetBytes(html.ToString())));

        return true;
    }

    private static List<DirectoryEntry> CollectDirectoryEntries(EmbeddedWebContent content, string relativePath)
    {
        var prefix = relativePath.Trim('/');
        var entries = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var fullPath in content.Paths)
        {
            if (!TryMatchDirectoryEntry(fullPath, prefix, out var name, out var isDirectory))
                continue;

            if (!entries.TryGetValue(name, out var existing))
                entries[name] = isDirectory;
            else if (isDirectory && !existing)
                entries[name] = true;
        }

        var list = new List<DirectoryEntry>(entries.Count);

        foreach (var pair in entries)
            list.Add(new DirectoryEntry(pair.Key, pair.Value));

        return list;
    }

    private static bool TryMatchDirectoryEntry(string fullPath, string prefix, out string name, out bool isDirectory)
    {
        name = string.Empty;
        isDirectory = false;

        if (string.IsNullOrEmpty(prefix))
        {
            var firstSeparator = fullPath.IndexOf('/');

            if (firstSeparator < 0)
            {
                name = fullPath;

                return true;
            }

            name = fullPath[..firstSeparator];
            isDirectory = true;

            return true;
        }

        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        if (fullPath.Length == prefix.Length)
        {
            name = GetLastSegment(fullPath);

            return true;
        }

        if (fullPath.Length <= prefix.Length || fullPath[prefix.Length] != '/')
            return false;

        var remainder = fullPath[(prefix.Length + 1)..];
        var childSeparator = remainder.IndexOf('/');

        if (childSeparator >= 0)
        {
            name = remainder[..childSeparator];
            isDirectory = true;

            return true;
        }

        name = remainder;

        return true;
    }

    private static string GetLastSegment(string path)
    {
        var index = path.LastIndexOf('/');

        return index < 0 ? path : path[(index + 1)..];
    }

    private static string EscapeHtml(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);

        foreach (var ch in value)
        {
            switch (ch)
            {
                case '&':
                    builder.Append("&amp;");

                    break;
                case '<':
                    builder.Append("&lt;");

                    break;
                case '>':
                    builder.Append("&gt;");

                    break;
                case '"':
                    builder.Append("&quot;");

                    break;
                case '\'':
                    builder.Append("&#39;");

                    break;
                default:
                    builder.Append(ch);

                    break;
            }
        }

        return builder.ToString();
    }

    private static NetHttpContentType GetContentType(string path)
    {
        var ext = Path.GetExtension(path);

        if (ext.Length == 0)
            return NetHttpContentType.ApplicationOctetStream;

        if (ext.Equals(".html", StringComparison.OrdinalIgnoreCase) || ext.Equals(".htm", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.TextHtml;

        if (ext.Equals(".css", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.TextCss;

        if (ext.Equals(".js", StringComparison.OrdinalIgnoreCase) || ext.Equals(".mjs", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.ApplicationJavascript;

        if (ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.ApplicationJson;

        if (ext.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.TextPlain;

        if (ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.ImagePng;

        if (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.ImageJpeg;

        if (ext.Equals(".gif", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.ImageGif;

        if (ext.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.ImageSvg;

        if (ext.Equals(".webp", StringComparison.OrdinalIgnoreCase))
            return NetHttpContentType.ImageWebp;

        if (ext.Equals(".ico", StringComparison.OrdinalIgnoreCase))
            return "image/x-icon";

        return NetHttpContentType.ApplicationOctetStream;
    }

    private static bool IsHtmlContent(NetHttpContentType contentType) =>
        contentType.MediaType.Equals(NetHttpContentType.TextHtml, StringComparison.OrdinalIgnoreCase);

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return string.Empty;

        path = path.Replace('\\', '/');
        path = path.Trim('/');

        return path;
    }

    private static bool IsGetOrHead(NetHttpMethod method)
    {
        var value = method.Value.ToString();

        return value.Equals("GET", StringComparison.OrdinalIgnoreCase) || value.Equals("HEAD", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractBundleId(string resourceName, out string bundleId, out string resourcePrefix)
    {
        var normalized = NormalizeResourceName(resourceName);
        var index = FindResourceMarker(normalized);

        if (index < 0)
        {
            bundleId = string.Empty;
            resourcePrefix = string.Empty;

            return false;
        }

        var start = index + resourceMarker.Length;
        var slashIndex = normalized.IndexOf('/', start);

        if (slashIndex < 0)
        {
            throw new InvalidOperationException(
                $"Embedded web resource '{resourceName}' must use '/' after the bundle id. Expected 'EmbeddedWeb.<bundleId>/<path>'.");
        }

        bundleId = normalized[start..slashIndex];

        if (string.IsNullOrWhiteSpace(bundleId))
            throw new InvalidOperationException($"Embedded web resource '{resourceName}' has empty bundle id.");

        resourcePrefix = normalized[..(slashIndex + 1)];

        return true;
    }

    private static int FindResourceMarker(string resourceName)
    {
        var index = resourceName.IndexOf(resourceMarker, StringComparison.OrdinalIgnoreCase);

        while (index >= 0)
        {
            if (index == 0 || resourceName[index - 1] == '.')
                return index;

            index = resourceName.IndexOf(resourceMarker, index + resourceMarker.Length, StringComparison.OrdinalIgnoreCase);
        }

        return -1;
    }

    private static string NormalizeResourcePrefix(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
            throw new InvalidOperationException("Resource prefix cannot be empty.");

        prefix = prefix.Replace('\\', '/');

        if (!prefix.EndsWith('/'))
            throw new InvalidOperationException("Resource prefix must end with '/'.");

        return prefix;
    }

    private static string BuildDefaultResourcePrefix(string bundleId) => $"{resourceMarker}{bundleId}/";

    private static string NormalizeResourceName(string resourceName) => resourceName.Replace('\\', '/');

    private readonly record struct DirectoryEntry(string Name, bool IsDirectory);
}
