// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.UI.Web.EmbeddedWeb;

internal static class EmbeddedWebResourceTicksInjector
{
    private static readonly UTF8Encoding strictUtf8 = new(false, true);

    public static bool TryInject(ReadOnlyMemory<byte> html, long ticks, out ReadOnlyMemory<byte> injected)
    {
        injected = ReadOnlyMemory<byte>.Empty;

        if (html.Length == 0)
            return false;

        if (ticks < 0)
            throw new ArgumentOutOfRangeException(nameof(ticks));

        string text;

        try
        {
            text = strictUtf8.GetString(html.Span);
        }
        catch (DecoderFallbackException)
        {
            return false;
        }

        if (!TryRewrite(text, ticks, out var rewritten))
            return false;

        injected = strictUtf8.GetBytes(rewritten);

        return true;
    }

    private static bool TryRewrite(string html, long ticks, out string rewritten)
    {
        var builder = new StringBuilder(html.Length + 32);
        var i = 0;

        while (i < html.Length)
        {
            var lt = html.IndexOf('<', i);

            if (lt < 0)
            {
                builder.Append(html.AsSpan(i));
                rewritten = builder.ToString();

                return true;
            }

            builder.Append(html.AsSpan(i, lt - i));
            i = lt;

            if (StartsWith(html, i, "<!--"))
            {
                var end = html.IndexOf("-->", i + 4, StringComparison.Ordinal);

                if (end < 0)
                {
                    rewritten = string.Empty;

                    return false;
                }

                builder.Append(html.AsSpan(i, end + 3 - i));
                i = end + 3;

                continue;
            }

            if (i + 1 >= html.Length)
            {
                rewritten = string.Empty;

                return false;
            }

            var tagStart = i + 1;
            var tagChar = html[tagStart];

            if (tagChar == '/' || tagChar == '!' || tagChar == '?')
            {
                var end = html.IndexOf('>', tagStart);

                if (end < 0)
                {
                    rewritten = string.Empty;

                    return false;
                }

                builder.Append(html.AsSpan(i, end + 1 - i));
                i = end + 1;

                continue;
            }

            var nameStart = tagStart;
            var nameEnd = nameStart;

            while (nameEnd < html.Length && IsTagNameChar(html[nameEnd]))
                nameEnd++;

            if (nameEnd == nameStart)
            {
                rewritten = string.Empty;

                return false;
            }

            var tagName = html.AsSpan(nameStart, nameEnd - nameStart);
            builder.Append('<');
            builder.Append(tagName);
            i = nameEnd;

            while (i < html.Length)
            {
                var ch = html[i];

                if (ch == '>')
                {
                    builder.Append('>');
                    i++;

                    break;
                }

                if (ch == '/' && i + 1 < html.Length && html[i + 1] == '>')
                {
                    builder.Append("/>");
                    i += 2;

                    break;
                }

                if (char.IsWhiteSpace(ch))
                {
                    builder.Append(ch);
                    i++;

                    continue;
                }

                var attrStart = i;
                var attrEnd = attrStart;

                while (attrEnd < html.Length && IsAttributeNameChar(html[attrEnd]))
                    attrEnd++;

                if (attrEnd == attrStart)
                {
                    builder.Append(html[i]);
                    i++;

                    continue;
                }

                var attrName = html.AsSpan(attrStart, attrEnd - attrStart);
                builder.Append(attrName);
                i = attrEnd;

                var wsStart = i;

                while (i < html.Length && char.IsWhiteSpace(html[i]))
                    i++;

                builder.Append(html.AsSpan(wsStart, i - wsStart));

                if (i >= html.Length || html[i] != '=')
                    continue;

                builder.Append('=');
                i++;

                var ws2Start = i;

                while (i < html.Length && char.IsWhiteSpace(html[i]))
                    i++;

                builder.Append(html.AsSpan(ws2Start, i - ws2Start));

                if (i >= html.Length)
                {
                    rewritten = string.Empty;

                    return false;
                }

                var quote = html[i];

                if (quote == '\'' || quote == '"')
                {
                    builder.Append(quote);
                    i++;
                    var valStart = i;
                    var valEnd = html.IndexOf(quote, valStart);

                    if (valEnd < 0)
                    {
                        rewritten = string.Empty;

                        return false;
                    }

                    var value = html.AsSpan(valStart, valEnd - valStart);
                    builder.Append(RewriteValue(tagName, attrName, value, ticks));
                    builder.Append(quote);
                    i = valEnd + 1;

                    continue;
                }

                var unquotedStart = i;

                while (i < html.Length && !char.IsWhiteSpace(html[i]) && html[i] != '>')
                    i++;

                var unquotedValue = html.AsSpan(unquotedStart, i - unquotedStart);
                builder.Append(RewriteValue(tagName, attrName, unquotedValue, ticks));
            }
        }

        rewritten = builder.ToString();

        return true;
    }

    private static string RewriteValue(ReadOnlySpan<char> tagName, ReadOnlySpan<char> attrName, ReadOnlySpan<char> value, long ticks)
    {
        if (!ShouldVersion(tagName, attrName))
            return value.ToString();

        if (value.IsEmpty)
            return string.Empty;

        if (IsExternalOrFragment(value))
            return value.ToString();

        var url = value.ToString();
        var fragmentIndex = url.IndexOf('#');
        var fragment = string.Empty;

        if (fragmentIndex >= 0)
        {
            fragment = url[fragmentIndex..];
            url = url[..fragmentIndex];
        }

        var separator = url.Contains('?') ? "&" : "?";

        return url + separator + "t=" + ticks + fragment;
    }

    private static bool ShouldVersion(ReadOnlySpan<char> tagName, ReadOnlySpan<char> attrName)
    {
        if (attrName.Equals("src", StringComparison.OrdinalIgnoreCase))
            return IsSrcTag(tagName);

        if (attrName.Equals("href", StringComparison.OrdinalIgnoreCase))
            return tagName.Equals("link", StringComparison.OrdinalIgnoreCase);

        if (attrName.Equals("data", StringComparison.OrdinalIgnoreCase))
            return tagName.Equals("object", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static bool IsSrcTag(ReadOnlySpan<char> tagName)
    {
        if (tagName.Equals("script", StringComparison.OrdinalIgnoreCase))
            return true;

        if (tagName.Equals("img", StringComparison.OrdinalIgnoreCase))
            return true;

        if (tagName.Equals("source", StringComparison.OrdinalIgnoreCase))
            return true;

        if (tagName.Equals("video", StringComparison.OrdinalIgnoreCase))
            return true;

        if (tagName.Equals("audio", StringComparison.OrdinalIgnoreCase))
            return true;

        if (tagName.Equals("track", StringComparison.OrdinalIgnoreCase))
            return true;

        if (tagName.Equals("iframe", StringComparison.OrdinalIgnoreCase))
            return true;

        if (tagName.Equals("embed", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsExternalOrFragment(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return true;

        if (value[0] == '#')
            return true;

        if (value.StartsWith("//", StringComparison.Ordinal))
            return true;

        if (HasScheme(value))
            return true;

        return false;
    }

    private static bool HasScheme(ReadOnlySpan<char> value)
    {
        var colonIndex = value.IndexOf(':');

        if (colonIndex <= 0)
            return false;

        for (var i = 0; i < colonIndex; i++)
        {
            var ch = value[i];

            if (!char.IsLetterOrDigit(ch) && ch != '+' && ch != '-' && ch != '.')
                return false;
        }

        return true;
    }

    private static bool IsTagNameChar(char ch) => char.IsLetterOrDigit(ch) || ch == '-' || ch == ':' || ch == '_';

    private static bool IsAttributeNameChar(char ch) => char.IsLetterOrDigit(ch) || ch == '-' || ch == ':' || ch == '_';

    private static bool StartsWith(string text, int start, string prefix)
    {
        if (start + prefix.Length > text.Length)
            return false;

        return text.AsSpan(start, prefix.Length).Equals(prefix, StringComparison.Ordinal);
    }
}
