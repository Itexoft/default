// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Globalization;

namespace Itexoft.Formats.Yaml.Internal.Binding;

internal sealed class DefaultScalarCodecProvider : IScalarCodecProvider
{
    private static readonly DateTimeCodec dateTimeCodec = new();
    private static readonly DateTimeOffsetCodec dateTimeOffsetCodec = new();
    private static readonly DateOnlyCodec dateOnlyCodec = new();
    private static readonly TimeOnlyCodec timeOnlyCodec = new();
    private static readonly EnumCodec enumCodec = new();

    public bool TryGetCodec(Type type, out IScalarCodec? codec)
    {
        var effectiveType = Nullable.GetUnderlyingType(type) ?? type;

        if (effectiveType.IsEnum)
        {
            codec = enumCodec;

            return true;
        }

        if (effectiveType == typeof(DateTime))
        {
            codec = dateTimeCodec;

            return true;
        }

        if (effectiveType == typeof(DateTimeOffset))
        {
            codec = dateTimeOffsetCodec;

            return true;
        }

        if (effectiveType == typeof(DateOnly))
        {
            codec = dateOnlyCodec;

            return true;
        }

        if (effectiveType == typeof(TimeOnly))
        {
            codec = timeOnlyCodec;

            return true;
        }

        codec = null;

        return false;
    }
}

internal sealed class DateTimeCodec : IScalarCodec
{
    public object Read(string text, Type targetType) => DateTime.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public string Write(object? value, Type targetType) => ((DateTime)value!).ToString("O", CultureInfo.InvariantCulture);
}

internal sealed class DateTimeOffsetCodec : IScalarCodec
{
    public object Read(string text, Type targetType) =>
        DateTimeOffset.ParseExact(text, "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    public string Write(object? value, Type targetType) => ((DateTimeOffset)value!).ToString("O", CultureInfo.InvariantCulture);
}

internal sealed class DateOnlyCodec : IScalarCodec
{
    public object Read(string text, Type targetType) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    public string Write(object? value, Type targetType) => ((DateOnly)value!).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

internal sealed class TimeOnlyCodec : IScalarCodec
{
    public object Read(string text, Type targetType) => TimeOnly.ParseExact(text, "HH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    public string Write(object? value, Type targetType) => ((TimeOnly)value!).ToString("HH:mm:ss.fffffff", CultureInfo.InvariantCulture);
}

internal sealed class EnumCodec : IScalarCodec
{
    public object Read(string text, Type targetType)
    {
        if (Enum.GetNames(targetType).Any(x => x == text))
            return Enum.Parse(targetType, text, false);

        var underlying = Enum.GetUnderlyingType(targetType);
        var parsed = Convert.ChangeType(text, underlying, CultureInfo.InvariantCulture);

        return Enum.ToObject(targetType, parsed!);
    }

    public string Write(object? value, Type targetType)
    {
        var name = Enum.GetName(targetType, value!);

        if (name is not null)
            return name;

        var underlying = Convert.ChangeType(value!, Enum.GetUnderlyingType(targetType), CultureInfo.InvariantCulture);

        return Convert.ToString(underlying, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
