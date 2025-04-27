// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Text.Schemes;

public abstract class TransportUrl(
    string text,
    int userStart,
    int userLength,
    int hostStart,
    int hostLength,
    int targetStart,
    int targetLength,
    ushort port,
    bool hasPort)
{
    public string Text { get; } = text.RequiredNotWhiteSpace();

    public bool HasUser => userLength != 0;

    public string User => userLength != 0 ? new string(this.UserSpan) : throw new InvalidOperationException();

    public bool HasHost => hostLength != 0;

    public string Host => hostLength != 0 ? new string(this.HostSpan) : throw new InvalidOperationException();

    public bool HasPort { get; } = hasPort;

    public ushort Port => this.HasPort ? port : throw new InvalidOperationException();

    public string Target => new(this.TargetSpan);

    protected ReadOnlySpan<char> UserSpan => this.Text.AsSpan(userStart, userLength);

    protected ReadOnlySpan<char> HostSpan => this.Text.AsSpan(hostStart, hostLength);

    protected ReadOnlySpan<char> TargetSpan => this.Text.AsSpan(targetStart, targetLength);

    public bool TryGetUser(out string? user)
    {
        if (userLength == 0)
        {
            user = null;

            return false;
        }

        user = new(this.UserSpan);

        return true;
    }

    public bool TryGetHost(out string? host)
    {
        if (hostLength == 0)
        {
            host = null;

            return false;
        }

        host = new(this.HostSpan);

        return true;
    }

    public bool TryGetAccessPort(out ushort accessPort)
    {
        if (!this.HasPort)
        {
            accessPort = 0;

            return false;
        }

        accessPort = port;

        return true;
    }

    public override string ToString() => this.Text;

    public static implicit operator string(TransportUrl url) => url.Required().ToString();

    protected string BuildHostTarget()
    {
        if (hostLength == 0)
            throw new InvalidOperationException();

        var totalLength = hostLength + targetLength;

        return string.Create(
            totalLength,
            (this.Text, hostStart, hostLength, targetStart, targetLength),
            static (span, state) =>
            {
                state.Text.AsSpan(state.hostStart, state.hostLength).CopyTo(span);
                state.Text.AsSpan(state.targetStart, state.targetLength).CopyTo(span[state.hostLength..]);
            });
    }

    protected string BuildHostTarget(char separator)
    {
        if (hostLength == 0)
            throw new InvalidOperationException();

        var totalLength = hostLength + 1 + targetLength;

        return string.Create(
            totalLength,
            (this.Text, hostStart, hostLength, targetStart, targetLength, separator),
            static (span, state) =>
            {
                state.Text.AsSpan(state.hostStart, state.hostLength).CopyTo(span);
                span[state.hostLength] = state.separator;
                state.Text.AsSpan(state.targetStart, state.targetLength).CopyTo(span[(state.hostLength + 1)..]);
            });
    }

    protected string BuildHostPortTarget()
    {
        if (hostLength == 0 || !this.HasPort)
            throw new InvalidOperationException();

        var portLength = GetPortLength(port);
        var totalLength = hostLength + 1 + portLength + targetLength;

        return string.Create(
            totalLength,
            (this.Text, hostStart, hostLength, targetStart, targetLength, port, portLength),
            static (span, state) =>
            {
                state.Text.AsSpan(state.hostStart, state.hostLength).CopyTo(span);

                var index = state.hostLength;

                span[index] = ':';
                index++;

                state.port.TryFormat(span.Slice(index, state.portLength), out _);
                index += state.portLength;

                state.Text.AsSpan(state.targetStart, state.targetLength).CopyTo(span[index..]);
            });
    }

    protected static bool IsValidToken(ReadOnlySpan<char> span, TokenExtras extras)
    {
        if (span.IsEmpty)
            return false;

        foreach (var c in span)
        {
            if (IsAsciiLetterOrDigit(c))
                continue;

            if ((extras & TokenExtras.Dot) != 0 && c == '.')
                continue;

            if ((extras & TokenExtras.Dash) != 0 && c == '-')
                continue;

            if ((extras & TokenExtras.Underscore) != 0 && c == '_')
                continue;

            if ((extras & TokenExtras.Plus) != 0 && c == '+')
                continue;

            return false;
        }

        return true;
    }

    protected static bool EqualsIgnoreCaseAscii(ReadOnlySpan<char> span, string value)
    {
        if (span.Length != value.Length)
            return false;

        for (var i = 0; i < span.Length; i++)
        {
            var c = span[i];
            var v = value[i];

            if (c == v)
                continue;

            if ((c | (char)0x20) != v)
                return false;
        }

        return true;
    }

    protected static bool TryParsePort(ReadOnlySpan<char> span, out ushort port)
    {
        if (span.IsEmpty)
        {
            port = 0;

            return false;
        }

        var value = 0;

        foreach (var c in span)
        {
            if ((uint)(c - '0') > 9)
            {
                port = 0;

                return false;
            }

            var digit = c - '0';

            if (value > 6553 || (value == 6553 && digit > 5))
            {
                port = 0;

                return false;
            }

            value = value * 10 + digit;
        }

        if (value == 0)
        {
            port = 0;

            return false;
        }

        port = (ushort)value;

        return true;
    }

    protected static bool HasNonSlash(ReadOnlySpan<char> text)
    {
        foreach (var t in text)
        {
            if (t != '/')
                return true;
        }

        return false;
    }

    protected static bool HasWhitespace(ReadOnlySpan<char> text)
    {
        foreach (var t in text)
        {
            if (char.IsWhiteSpace(t))
                return true;
        }

        return false;
    }

    protected static bool HasSlashBefore(ReadOnlySpan<char> text, int index)
    {
        for (var i = 0; i < index; i++)
        {
            if (text[i] == '/')
                return true;
        }

        return false;
    }

    protected static int GetPortLength(ushort port) =>
        port >= 10000 ? 5 : port >= 1000 ? 4 : port >= 100 ? 3 : port >= 10 ? 2 : 1;

    protected static bool IsAsciiLetterOrDigit(char c) =>
        (uint)(c - '0') <= 9 || (uint)((c | (char)0x20) - 'a') <= 25;

    [Flags]
    protected enum TokenExtras : byte
    {
        None = 0,
        Dot = 1,
        Dash = 2,
        Underscore = 4,
        Plus = 8,
    }
}
