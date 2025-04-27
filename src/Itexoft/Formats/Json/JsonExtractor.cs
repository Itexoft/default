// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Itexoft.Extensions;
using Itexoft.IO;
using Itexoft.Threading;

namespace Itexoft.Formats.Json;

public static class JsonExtractor
{
    private const char objectOpen = '{';
    private const char objectClose = '}';
    private const char arrayOpen = '[';
    private const char arrayClose = ']';
    private const char stringDelimiter = '"';
    private const char escape = '\\';

    public static IEnumerable<string> Extract(IStreamR<char> source, CancelToken cancelToken = default)
    {
        source.Required();

        while (ReadNextCandidate(source, cancelToken) is { } candidate)
        {
            if (IsValid(candidate))
                yield return candidate;
        }
    }

    public static IEnumerable<T> Extract<T>(IStreamR<char> source, JsonSerializerContext context, CancelToken cancelToken = default) =>
        Extract(source, context, typeof(T), cancelToken).Cast<T>();

    public static IEnumerable<object> Extract(IStreamR<char> source, JsonSerializerContext context, Type type, CancelToken cancelToken = default)
    {
        source.Required();
        context.Required();

        if (context.GetTypeInfo(type) is not JsonTypeInfo typeInfo)
        {
            throw new ArgumentException(
                $"The specified type {type.FullName} is not a known JSON-serializable type in context {context.GetType()}.",
                nameof(context));
        }

        foreach (var json in Extract(source, cancelToken))
        {
            object? value;

            try
            {
                value = JsonSerializer.Deserialize(json, typeInfo);
            }
            catch (JsonException)
            {
                continue;
            }

            if (value is not null)
                yield return value;
        }
    }

    private static string? ReadNextCandidate(IStreamR<char> source, CancelToken cancelToken)
    {
        StringBuilder? builder = null;
        var depth = 0;
        var insideString = false;
        var escaped = false;
        Span<char> one = stackalloc char[1];

        while (TryReadChar(source, one, cancelToken, out var value))
        {
            if (builder is null)
            {
                if (!IsRootStart(value))
                    continue;

                builder = new StringBuilder();
                builder.Append(value);
                depth = 1;

                continue;
            }

            builder.Append(value);

            if (insideString)
            {
                if (escaped)
                {
                    escaped = false;

                    continue;
                }

                if (value == escape)
                {
                    escaped = true;

                    continue;
                }

                if (value == stringDelimiter)
                    insideString = false;

                continue;
            }

            if (value == stringDelimiter)
            {
                insideString = true;

                continue;
            }

            if (IsOpen(value))
            {
                depth++;

                continue;
            }

            if (!IsClose(value))
                continue;

            depth--;

            if (depth == 0)
                return builder.ToString();
        }

        return null;
    }

    private static bool TryReadChar(IStreamR<char> source, Span<char> buffer, CancelToken cancelToken, out char value)
    {
        cancelToken.ThrowIf();

        if (source.Read(buffer, cancelToken) <= 0)
        {
            value = default;

            return false;
        }

        value = buffer[0];

        return true;
    }

    private static bool IsValid(string candidate)
    {
        try
        {
            using var _ = JsonDocument.Parse(candidate);

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsRootStart(char value) => value is objectOpen or arrayOpen;

    private static bool IsOpen(char value) => value is objectOpen or arrayOpen;

    private static bool IsClose(char value) => value is objectClose or arrayClose;
}
