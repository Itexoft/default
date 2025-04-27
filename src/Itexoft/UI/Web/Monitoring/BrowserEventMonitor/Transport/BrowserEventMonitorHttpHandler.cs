// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Itexoft.IO;
using Itexoft.Net.Http;
using Itexoft.Threading;

namespace Itexoft.UI.Web.Monitoring.BrowserEventMonitor.Transport;

internal sealed class BrowserEventMonitorHttpHandler(BrowserEventMonitor monitor)
{
    private const string apiPrefix = "/api";
    private const string modelPath = "/api/model";
    private const string eventsPath = "/api/events";
    private const string seriesPrefix = "/api/series/";
    private static readonly Encoding strictUtf8 = new UTF8Encoding(false, true);
    private readonly BrowserEventMonitor monitor = monitor;

    public NetHttpResponse? Handle(NetHttpRequest request, CancelToken cancelToken)
    {
        var path = request.PathAndQuery.Path;

        if (!path.StartsWith(apiPrefix, StringComparison.Ordinal))
            return null;

        if (path.Equals(modelPath, StringComparison.Ordinal))
            return this.HandleModel(request);

        if (path.Equals(eventsPath, StringComparison.Ordinal))
            return this.HandleEvents(request);

        if (!path.StartsWith(seriesPrefix, StringComparison.Ordinal))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.NotFound, "Unknown API route.");

        var remainder = path[seriesPrefix.Length..];

        if (remainder.Length == 0)
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.NotFound, "Series key is missing.");

        var slash = remainder.IndexOf('/');

        if (slash < 0)
        {
            if (!TryDecodePathSegment(remainder, out var key))
                return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "Series key is invalid.");

            return this.HandleSeries(key, request);
        }

        var keySegment = remainder[..slash];

        if (!TryDecodePathSegment(keySegment, out var eventKey))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "Series key is invalid.");

        if (!remainder[(slash + 1)..].Equals("events", StringComparison.Ordinal))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.NotFound, "Unknown API route.");

        return this.HandleAddEvent(eventKey, request, cancelToken);
    }

    private NetHttpResponse HandleModel(NetHttpRequest request)
    {
        if (!IsMethod(request.Method, "GET"))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.MethodNotAllowed, "Model endpoint supports GET only.");

        if (!TryGetRequiredLong(request, "since", out var since, out var response))
            return response;

        return this.monitor.CreateModelResponse(since);
    }

    private NetHttpResponse HandleEvents(NetHttpRequest request)
    {
        if (!IsMethod(request.Method, "GET"))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.MethodNotAllowed, "Events endpoint supports GET only.");

        if (!TryGetRequiredLong(request, "since", out var since, out var response))
            return response;

        return this.monitor.CreateSseResponse(since);
    }

    private NetHttpResponse HandleSeries(string key, NetHttpRequest request)
    {
        if (IsMethod(request.Method, "DELETE"))
            return BrowserEventMonitor.CreateJsonResponse(this.monitor.DeleteCategory(key));

        if (!IsMethod(request.Method, "GET"))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.MethodNotAllowed, "Series endpoint supports GET or DELETE only.");

        if (!TryGetRequiredLong(request, "knownRevision", out var knownRevision, out var revisionResponse))
            return revisionResponse;

        if (!TryGetRequiredInt(request, "knownCount", out var knownCount, out var countResponse))
            return countResponse;

        return this.monitor.CreateSeriesResponse(key, knownRevision, knownCount);
    }

    private NetHttpResponse HandleAddEvent(string key, NetHttpRequest request, CancelToken cancelToken)
    {
        if (!IsMethod(request.Method, "POST"))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.MethodNotAllowed, "Add-event endpoint supports POST only.");

        if (request.Length <= 0 || request.Content is null)
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "Request body is required.");

        if (!request.Headers.ContentType.MediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase))
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "Content-Type must be application/json.");

        try
        {
            using var document = JsonDocument.Parse(request.Content.AsStream());

            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "Request body must be a JSON object.");

            if (!document.RootElement.TryGetProperty("timestampUtcMs", out var timestampProp) || !timestampProp.TryGetInt64(out var timestampUtcMs))
                return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "timestampUtcMs must be an integer.");

            if (!document.RootElement.TryGetProperty("value", out var valueProp) || valueProp.ValueKind is not JsonValueKind.Number)
                return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "value must be a number.");

            var value = valueProp.GetDouble();
            string? text = null;

            if (document.RootElement.TryGetProperty("text", out var textProp))
            {
                if (textProp.ValueKind is JsonValueKind.Null)
                    text = null;
                else if (textProp.ValueKind is JsonValueKind.String)
                    text = textProp.GetString();
                else
                    return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "text must be a string or null.");
            }

            var result = this.monitor.AddEvent(key, new(timestampUtcMs, value, text));

            return BrowserEventMonitor.CreateJsonResponse(result);
        }
        catch (JsonException)
        {
            cancelToken.ThrowIf();

            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, "Request body is not valid JSON.");
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.UnprocessableEntity, exception.Message);
        }
        catch (ArgumentException exception)
        {
            return BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.UnprocessableEntity, exception.Message);
        }
    }

    private static bool TryGetRequiredLong(NetHttpRequest request, string name, out long value, out NetHttpResponse response)
    {
        foreach (var query in request.PathAndQuery.Query)
        {
            if (!query.Name.Equals(name, StringComparison.Ordinal))
                continue;

            if (long.TryParse(query.Value, out value) && value >= 0)
            {
                response = default;

                return true;
            }

            break;
        }

        value = 0;
        response = BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, $"Query parameter '{name}' must be a non-negative integer.");

        return false;
    }

    private static bool TryGetRequiredInt(NetHttpRequest request, string name, out int value, out NetHttpResponse response)
    {
        foreach (var query in request.PathAndQuery.Query)
        {
            if (!query.Name.Equals(name, StringComparison.Ordinal))
                continue;

            if (int.TryParse(query.Value, out value) && value >= 0)
            {
                response = default;

                return true;
            }

            break;
        }

        value = 0;
        response = BrowserEventMonitor.CreateErrorResponse(NetHttpStatus.BadRequest, $"Query parameter '{name}' must be a non-negative integer.");

        return false;
    }

    private static bool TryDecodePathSegment(ReadOnlySpan<char> value, out string decoded)
    {
        if (value.IsEmpty)
        {
            decoded = string.Empty;

            return false;
        }

        try
        {
            var chars = new char[value.Length];
            var bytes = new List<byte>(value.Length);
            var charIndex = 0;

            for (var i = 0; i < value.Length; i++)
            {
                var current = value[i];

                if (current == '%')
                {
                    if (i + 2 >= value.Length || !TryParseHex(value[i + 1], out var hi) || !TryParseHex(value[i + 2], out var lo))
                    {
                        decoded = string.Empty;

                        return false;
                    }

                    bytes.Add((byte)((hi << 4) | lo));
                    i += 2;

                    continue;
                }

                FlushBytes();
                chars[charIndex++] = current;
            }

            FlushBytes();
            decoded = new(chars, 0, charIndex);

            return !string.IsNullOrWhiteSpace(decoded);

            void FlushBytes()
            {
                if (bytes.Count == 0)
                    return;

                charIndex += strictUtf8.GetChars(CollectionsMarshal.AsSpan(bytes), chars.AsSpan(charIndex));
                bytes.Clear();
            }
        }
        catch (DecoderFallbackException)
        {
            decoded = string.Empty;

            return false;
        }
    }

    private static bool TryParseHex(char value, out int result)
    {
        result = value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' => value - 'a' + 10,
            >= 'A' and <= 'F' => value - 'A' + 10,
            _ => -1,
        };

        return result >= 0;
    }

    private static bool IsMethod(NetHttpMethod method, string expected) =>
        method.Value.ToString().Equals(expected, StringComparison.OrdinalIgnoreCase);
}
