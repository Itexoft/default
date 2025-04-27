// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;
using System.Text.Json;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Net;
using Itexoft.Net.Http;
using Itexoft.Threading;
using Itexoft.Threading.Atomics;
using Itexoft.UI.Web.EmbeddedWeb;
using Itexoft.UI.Web.Monitoring.BrowserEventMonitor.Storage;
using Itexoft.UI.Web.Monitoring.BrowserEventMonitor.Transport;

namespace Itexoft.UI.Web.Monitoring.BrowserEventMonitor;

public sealed class BrowserEventMonitor
{
    private const string bundleId = "browser-event-monitor";
    private static AtomicLock bundleRegistrationLock = new();
    private static Latch bundleRegistered = new();
    private readonly Dictionary<string, BrowserEventMonitorCategoryState> categories = new(StringComparer.Ordinal);
    private readonly BrowserEventMonitorHttpHandler httpHandler;
    private readonly Dictionary<string, long> tombstones = new(StringComparer.Ordinal);
    private long globalRevision;
    private long nextCreationOrder;

    private AtomicLock stateLock = new();

    public BrowserEventMonitor()
    {
        this.ServerInstanceId = Guid.NewGuid().ToString("D", CultureInfo.InvariantCulture);
        this.httpHandler = new BrowserEventMonitorHttpHandler(this);
    }

    public long GlobalRevision => Interlocked.Read(ref this.globalRevision);

    public string ServerInstanceId { get; }

    public BrowserEventMonitorWriteResult AddEvent(string key, BrowserEventMonitorEvent eventData)
    {
        key = key.RequiredNotWhiteSpace();
        EnsureFinite(eventData.Value, nameof(eventData.Value));
        EnsureTimestamp(eventData.TimestampUtcMs);
        var normalized = new BrowserEventMonitorStoredEvent(eventData.TimestampUtcMs, eventData.Value, NormalizeText(eventData.Text));

        using (this.stateLock.Enter())
        {
            if (!this.categories.TryGetValue(key, out var category))
            {
                category = new BrowserEventMonitorCategoryState(key, this.nextCreationOrder++, CreateSeriesStream());
                this.categories.Add(key, category);
                this.tombstones.Remove(key);
                AppendEvent(category, normalized);
                UpdateCategoryMetadataOnAppend(category, normalized);
                category.SeriesRevision = 1;
                category.AppendBaseRevision = 1;
                category.AppendBaseCount = 1;
                category.Count = 1;
                category.LastChangedGlobalRevision = ++this.globalRevision;

                return new BrowserEventMonitorWriteResult(this.globalRevision, category.SeriesRevision);
            }

            if (TryAppendAtTail(category, normalized))
            {
                UpdateCategoryMetadataOnAppend(category, normalized);
                category.SeriesRevision++;
                category.Count++;
                category.LastChangedGlobalRevision = ++this.globalRevision;

                return new BrowserEventMonitorWriteResult(this.globalRevision, category.SeriesRevision);
            }

            RewriteCategory(category, normalized);
            category.SeriesRevision++;
            category.Count++;
            category.AppendBaseRevision = category.SeriesRevision;
            category.AppendBaseCount = category.Count;
            category.LastChangedGlobalRevision = ++this.globalRevision;

            return new BrowserEventMonitorWriteResult(this.globalRevision, category.SeriesRevision);
        }
    }

    public BrowserEventMonitorDeleteResult DeleteCategory(string key)
    {
        key = key.RequiredNotWhiteSpace();

        using (this.stateLock.Enter())
        {
            var deleted = this.categories.Remove(key, out var category);

            if (deleted)
            {
                category!.EventStream.Dispose();
                this.tombstones[key] = this.globalRevision + 1;
            }

            this.globalRevision++;

            return new BrowserEventMonitorDeleteResult(this.globalRevision, deleted);
        }
    }

    public EmbeddedWebRequestHandler CreateRequestHandler() => this.httpHandler.Handle;

    public EmbeddedWebHandle StartServer(NetIpEndpoint endpoint, Action<EmbeddedWebOptions>? configure = null, CancelToken cancelToken = default)
    {
        endpoint.Required();
        EnsureBundleRegistered();

        return EmbeddedWebServer.Start(
            bundleId,
            endpoint,
            options =>
            {
                options.EnableSpaFallback = true;
                options.SpaFallbackFile = "index.html";
                var handler = this.CreateRequestHandler();
                options.RequestHandler = handler;
                configure?.Invoke(options);

                if (options.RequestHandler is not null && !ReferenceEquals(options.RequestHandler, handler))
                {
                    var userHandler = options.RequestHandler;
                    options.RequestHandler = (request, ct) => handler(request, ct) ?? userHandler(request, ct);
                }
                else
                    options.RequestHandler = handler;
            },
            cancelToken);
    }

    internal NetHttpResponse CreateModelResponse(long since)
    {
        if (since < 0)
            throw new ArgumentOutOfRangeException(nameof(since));

        using (this.stateLock.Enter())
        {
            var mode = since > this.globalRevision ? "reset" : "delta";
            var ordered = this.CollectOrderedCategories(mode == "reset", since);
            var deletedKeys = mode == "reset" ? [] : this.CollectDeletedKeys(since);

            return CreateJsonResponse(
                NetHttpStatus.Ok,
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("serverInstanceId", this.ServerInstanceId);
                    writer.WriteNumber("globalRevision", this.globalRevision);
                    writer.WriteString("mode", mode);
                    writer.WritePropertyName("categories");
                    writer.WriteStartArray();

                    foreach (var category in ordered)
                        WriteCategoryMetadata(writer, category);

                    writer.WriteEndArray();
                    writer.WritePropertyName("deletedKeys");
                    writer.WriteStartArray();

                    foreach (var deletedKey in deletedKeys)
                        writer.WriteStringValue(deletedKey);

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                });
        }
    }

    internal NetHttpResponse CreateSeriesResponse(string key, long knownRevision, int knownCount)
    {
        if (knownRevision < 0)
            throw new ArgumentOutOfRangeException(nameof(knownRevision));

        if (knownCount < 0)
            throw new ArgumentOutOfRangeException(nameof(knownCount));

        using (this.stateLock.Enter())
        {
            if (!this.categories.TryGetValue(key, out var category))
            {
                return CreateJsonResponse(
                    NetHttpStatus.NotFound,
                    writer =>
                    {
                        writer.WriteStartObject();
                        writer.WriteString("key", key);
                        writer.WriteString("mode", "absent");
                        writer.WriteEndObject();
                    });
            }

            var append = CanAppend(category, knownRevision, knownCount);

            return CreateJsonResponse(
                NetHttpStatus.Ok,
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteString("key", category.Key);
                    writer.WriteNumber("seriesRevision", category.SeriesRevision);
                    writer.WriteString("mode", append ? "append" : "replace");
                    writer.WriteNumber("count", category.Count);
                    writer.WritePropertyName("events");
                    writer.WriteStartArray();

                    category.EventStream.Position = 0;
                    var index = 0;

                    while (BrowserEventMonitorEventCodec.TryRead(category.EventStream, out var current))
                    {
                        if (append && index < knownCount)
                        {
                            index++;

                            continue;
                        }

                        WriteEvent(writer, current);
                        index++;
                    }

                    writer.WriteEndArray();
                    writer.WriteEndObject();
                });
        }
    }

    internal NetHttpResponse CreateSseResponse(long since)
    {
        if (since < 0)
            throw new ArgumentOutOfRangeException(nameof(since));

        var headers = new NetHttpHeaders
        {
            ContentType = "text/event-stream; charset=utf-8",
            CacheControl = "no-cache",
        };

        headers["X-Accel-Buffering"] = "no";

        return new NetHttpResponse(NetHttpStatus.Ok, headers, new BrowserEventMonitorSseStream(this, since));
    }

    internal static NetHttpResponse CreateJsonResponse(BrowserEventMonitorWriteResult result) =>
        CreateJsonResponse(
            NetHttpStatus.Ok,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("globalRevision", result.GlobalRevision);
                writer.WriteNumber("seriesRevision", result.SeriesRevision);
                writer.WriteEndObject();
            });

    internal static NetHttpResponse CreateJsonResponse(BrowserEventMonitorDeleteResult result) =>
        CreateJsonResponse(
            NetHttpStatus.Ok,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteNumber("globalRevision", result.GlobalRevision);
                writer.WriteEndObject();
            });

    internal static NetHttpResponse CreateErrorResponse(NetHttpStatus status, string error) =>
        CreateJsonResponse(
            status,
            writer =>
            {
                writer.WriteStartObject();
                writer.WriteString("error", error);
                writer.WriteEndObject();
            });

    internal static NetHttpResponse CreateJsonResponse(NetHttpStatus status, Action<Utf8JsonWriter> write)
    {
        var stream = new MemoryStream(512);

        using (var writer = new Utf8JsonWriter(stream))
            write(writer);

        stream.Position = 0;

        return new NetHttpResponse(
            status,
            new NetHttpHeaders
            {
                ContentType = "application/json; charset=utf-8",
                CacheControl = "no-store",
            },
            stream.AsStreamRs());
    }

    private static void EnsureBundleRegistered()
    {
        if (bundleRegistered)
            return;

        using (bundleRegistrationLock.Enter())
        {
            if (bundleRegistered)
                return;

            EmbeddedWebServer.RegisterBundle(bundleId, typeof(BrowserEventMonitor).Assembly);
            _ = bundleRegistered.Try();
        }
    }

    private static IStreamRwsl<byte> CreateSeriesStream() => new MemoryStream(1024).AsStreamRwsl();

    private static void EnsureTimestamp(long timestampUtcMs)
    {
        try
        {
            _ = DateTimeOffset.FromUnixTimeMilliseconds(timestampUtcMs);
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ArgumentOutOfRangeException(nameof(timestampUtcMs), timestampUtcMs, exception.Message);
        }
    }

    private static void EnsureFinite(double value, string name)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(name, value, "Value must be finite.");
    }

    private static string? NormalizeText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        return text;
    }

    private static void AppendEvent(BrowserEventMonitorCategoryState category, in BrowserEventMonitorStoredEvent value)
    {
        category.EventStream.Position = category.EventStream.Length;
        BrowserEventMonitorEventCodec.Write(category.EventStream, value);
    }

    private static bool TryAppendAtTail(BrowserEventMonitorCategoryState category, in BrowserEventMonitorStoredEvent value)
    {
        if (category.Count == 0)
            return true;

        category.EventStream.Position = 0;
        var last = default(BrowserEventMonitorStoredEvent);

        while (BrowserEventMonitorEventCodec.TryRead(category.EventStream, out var current))
            last = current;

        if (value.TimestampUtcMs < last.TimestampUtcMs)
            return false;

        AppendEvent(category, value);

        return true;
    }

    private static void UpdateCategoryMetadataOnAppend(BrowserEventMonitorCategoryState category, in BrowserEventMonitorStoredEvent value)
    {
        var summary = new BrowserEventMonitorCategorySummary();

        if (category.Count > 0)
        {
            summary.Seed(
                category.TimeMinUtcMs,
                category.TimeMaxUtcMs,
                category.ValueMin,
                category.ValueMax,
                category.HasText,
                category.AllValuesPositive);
        }

        summary.Add(value);
        summary.ApplyTo(category);
    }

    private static void RewriteCategory(BrowserEventMonitorCategoryState category, in BrowserEventMonitorStoredEvent inserted)
    {
        var current = category.EventStream;
        var next = CreateSeriesStream();
        var summary = new BrowserEventMonitorCategorySummary();
        var insertedFlag = false;
        current.Position = 0;

        while (BrowserEventMonitorEventCodec.TryRead(current, out var existing))
        {
            if (!insertedFlag && inserted.TimestampUtcMs < existing.TimestampUtcMs)
            {
                BrowserEventMonitorEventCodec.Write(next, inserted);
                summary.Add(inserted);
                insertedFlag = true;
            }

            BrowserEventMonitorEventCodec.Write(next, existing);
            summary.Add(existing);
        }

        if (!insertedFlag)
        {
            BrowserEventMonitorEventCodec.Write(next, inserted);
            summary.Add(inserted);
        }

        current.Dispose();
        category.EventStream = next;
        summary.ApplyTo(category);
    }

    private static bool CanAppend(BrowserEventMonitorCategoryState category, long knownRevision, int knownCount)
    {
        if (knownCount > category.Count || knownRevision > category.SeriesRevision)
            return false;

        if (knownRevision == category.SeriesRevision)
            return knownCount == category.Count;

        if (knownRevision < category.AppendBaseRevision || knownCount < category.AppendBaseCount)
            return false;

        return knownRevision - category.AppendBaseRevision == knownCount - category.AppendBaseCount;
    }

    private static void WriteCategoryMetadata(Utf8JsonWriter writer, BrowserEventMonitorCategoryState category)
    {
        writer.WriteStartObject();
        writer.WriteString("key", category.Key);
        writer.WriteNumber("seriesRevision", category.SeriesRevision);
        writer.WriteNumber("count", category.Count);
        writer.WriteNumber("timeMinUtcMs", category.TimeMinUtcMs);
        writer.WriteNumber("timeMaxUtcMs", category.TimeMaxUtcMs);
        writer.WriteNumber("valueMin", category.ValueMin);
        writer.WriteNumber("valueMax", category.ValueMax);
        writer.WriteBoolean("hasText", category.HasText);
        writer.WriteBoolean("allValuesPositive", category.AllValuesPositive);
        writer.WriteEndObject();
    }

    private static void WriteEvent(Utf8JsonWriter writer, in BrowserEventMonitorStoredEvent value)
    {
        writer.WriteStartObject();
        writer.WriteNumber("timestampUtcMs", value.TimestampUtcMs);
        writer.WriteNumber("value", value.Value);

        if (value.Text is null)
            writer.WriteNull("text");
        else
            writer.WriteString("text", value.Text);

        writer.WriteEndObject();
    }

    private List<BrowserEventMonitorCategoryState> CollectOrderedCategories(bool includeAll, long since)
    {
        var ordered = new List<BrowserEventMonitorCategoryState>(this.categories.Count);

        foreach (var category in this.categories.Values)
        {
            if (!includeAll && category.LastChangedGlobalRevision <= since)
                continue;

            ordered.Add(category);
        }

        ordered.Sort(static (left, right) => left.CreationOrder.CompareTo(right.CreationOrder));

        return ordered;
    }

    private string[] CollectDeletedKeys(long since)
    {
        var deleted = new List<KeyValuePair<string, long>>();

        foreach (var pair in this.tombstones)
        {
            if (pair.Value > since)
                deleted.Add(pair);
        }

        deleted.Sort(static (left, right) => left.Value.CompareTo(right.Value));

        if (deleted.Count == 0)
            return [];

        var values = new string[deleted.Count];

        for (var i = 0; i < deleted.Count; i++)
            values[i] = deleted[i].Key;

        return values;
    }
}
