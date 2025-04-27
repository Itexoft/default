// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.Extensions;

public static class StringExtensions
{
    public static string ToKebabCase(this string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var sb = new StringBuilder(value.Length * 2);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];

            if (char.IsUpper(c))
            {
                if (i > 0)
                    sb.Append('-');

                sb.Append(char.ToLowerInvariant(c));
            }
            else
                sb.Append(c);
        }

        return sb.ToString();
    }

    public static string EmptyIfNull(this string? value) => string.IsNullOrEmpty(value) ? string.Empty : value;

    public static string Clip(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        return value.Length <= maxLength ? value : value[..maxLength];
    }

    public static string PadLines(this string value, int count = 1) => value.PadLines(count, ' ');

    public static string PadLines(this string value, int count, char symbol)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), count, "Value must be positive.");

        var prefix = new string(symbol, count);
        var sb = new StringBuilder(value.Length + prefix.Length);

        sb.Append(prefix);

        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            sb.Append(c);

            if (c == '\n' && i + 1 < value.Length)
                sb.Append(prefix);
        }

        return sb.ToString();
    }
}
