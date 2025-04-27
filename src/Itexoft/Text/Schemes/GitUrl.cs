// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Text.Schemes;

public sealed class GitUrl : TransportUrl, IEquatable<GitUrl>
{
    public enum SyntaxKind : byte
    {
        Url,
        ScpLike,
        LocalPath,
        Helper,
    }

    public enum TransportKind : byte
    {
        Ssh,
        Git,
        Http,
        Https,
        Ftp,
        Ftps,
        File,
        Local,
        Helper,
    }

    private readonly int helperLength;

    private readonly int helperStart;

    private GitUrl(
        string text,
        TransportKind transport,
        SyntaxKind syntax,
        int userStart,
        int userLength,
        int hostStart,
        int hostLength,
        int targetStart,
        int targetLength,
        int helperStart,
        int helperLength,
        ushort port,
        bool hasPort) : base(text, userStart, userLength, hostStart, hostLength, targetStart, targetLength, port, hasPort)
    {
        this.Transport = transport;
        this.Syntax = syntax;
        this.helperStart = helperStart;
        this.helperLength = helperLength;
    }

    public TransportKind Transport { get; }

    public SyntaxKind Syntax { get; }

    public bool IsLocal => this.Transport is TransportKind.Local or TransportKind.File;

    public bool IsHelper => this.Syntax == SyntaxKind.Helper;

    public string Path => this.Syntax != SyntaxKind.Helper ? this.Target : throw new InvalidOperationException();

    public string HostPath
    {
        get
        {
            if (!this.HasHost)
                throw new InvalidOperationException();

            if (this.Syntax == SyntaxKind.ScpLike)
                return this.BuildHostTarget(':');

            if (this.Syntax == SyntaxKind.Url)
            {
                if (this.HasPort)
                    return this.BuildHostPortTarget();

                return this.BuildHostTarget();
            }

            throw new InvalidOperationException();
        }
    }

    public bool HasHelper => this.helperLength != 0;

    public string Helper => this.helperLength != 0
        ? new string(this.Text.AsSpan(this.helperStart, this.helperLength))
        : throw new InvalidOperationException();

    public string HelperAddress => this.Syntax == SyntaxKind.Helper ? this.Target : throw new InvalidOperationException();

    public bool Equals(GitUrl? other) =>
        other is not null && string.Equals(this.Text, other.Text, StringComparison.Ordinal);

    public bool TryGetPath(out string? path)
    {
        if (this.Syntax == SyntaxKind.Helper)
        {
            path = null;

            return false;
        }

        path = new(this.TargetSpan);

        return true;
    }

    public bool TryGetHelper(out string? helper)
    {
        if (this.helperLength == 0)
        {
            helper = null;

            return false;
        }

        helper = new(this.Text.AsSpan(this.helperStart, this.helperLength));

        return true;
    }

    public bool TryGetHelperAddress(out string? helperAddress)
    {
        if (this.Syntax != SyntaxKind.Helper)
        {
            helperAddress = null;

            return false;
        }

        helperAddress = new(this.TargetSpan);

        return true;
    }

    public override bool Equals(object? obj) => obj is GitUrl other && this.Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(this.Text);

    public static GitUrl Parse(string text) =>
        TryParseCore(text.RequiredNotWhiteSpace(), text.AsSpan(), out var value) ? value! : throw new FormatException();

    public static bool TryParse(string? text, out GitUrl? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;

            return false;
        }

        return TryParseCore(text, text.AsSpan(), out value);
    }

    private static bool TryParseCore(string source, ReadOnlySpan<char> text, out GitUrl? value)
    {
        if (text.IsEmpty || HasWhitespace(text))
        {
            value = null;

            return false;
        }

        var helperSep = text.IndexOf("::".AsSpan(), StringComparison.Ordinal);

        if (helperSep >= 0 && !HasSlashBefore(text, helperSep))
            return TryParseHelper(source, text, helperSep, out value);

        var schemeSep = text.IndexOf("://".AsSpan(), StringComparison.Ordinal);

        if (schemeSep >= 0)
            return TryParseUrl(source, text, schemeSep, out value);

        var colon = text.IndexOf(':');

        if (colon >= 0 && !HasSlashBefore(text, colon))
            return TryParseScp(source, text, colon, out value);

        return TryParseLocal(source, text, out value);
    }

    private static bool TryParseHelper(string source, ReadOnlySpan<char> text, int helperSep, out GitUrl? value)
    {
        if (helperSep == 0 || helperSep + 2 >= text.Length)
        {
            value = null;

            return false;
        }

        var helperName = text[..helperSep];

        if (!IsValidHelperName(helperName))
        {
            value = null;

            return false;
        }

        var addressStart = helperSep + 2;
        var addressLength = text.Length - addressStart;

        if (!HasNonSlash(text[addressStart..]))
        {
            value = null;

            return false;
        }

        value = new GitUrl(source, TransportKind.Helper, SyntaxKind.Helper, 0, 0, 0, 0, addressStart, addressLength, 0, helperSep, 0, false);

        return true;
    }

    private static bool TryParseUrl(string source, ReadOnlySpan<char> text, int schemeSep, out GitUrl? value)
    {
        if (!TryMapScheme(text[..schemeSep], out var transport))
        {
            value = null;

            return false;
        }

        var authorityStart = schemeSep + 3;

        if (authorityStart >= text.Length)
        {
            value = null;

            return false;
        }

        var pathRel = text[authorityStart..].IndexOf('/');

        if (pathRel < 0)
        {
            value = null;

            return false;
        }

        var pathStart = authorityStart + pathRel;
        var path = text[pathStart..];

        if (!IsValidUrlPath(path))
        {
            value = null;

            return false;
        }

        var authority = text.Slice(authorityStart, pathStart - authorityStart);

        if (transport == TransportKind.File)
        {
            if (authority.Length != 0)
            {
                value = null;

                return false;
            }

            value = new GitUrl(source, transport, SyntaxKind.Url, 0, 0, 0, 0, pathStart, path.Length, 0, 0, 0, false);

            return true;
        }

        if (authority.Length == 0)
        {
            value = null;

            return false;
        }

        var userStart = 0;
        var userLength = 0;
        var hostStart = authorityStart;
        var hostPort = authority;

        if (transport == TransportKind.Ssh)
        {
            var at = authority.IndexOf('@');

            if (at >= 0)
            {
                if (at == 0 || at == authority.Length - 1 || authority[(at + 1)..].IndexOf('@') >= 0)
                {
                    value = null;

                    return false;
                }

                userStart = authorityStart;
                userLength = at;

                if (!IsValidUser(text.Slice(userStart, userLength)))
                {
                    value = null;

                    return false;
                }

                hostStart = authorityStart + at + 1;
                hostPort = authority[(at + 1)..];
            }
        }
        else if (authority.IndexOf('@') >= 0)
        {
            value = null;

            return false;
        }

        if (hostPort.Length == 0)
        {
            value = null;

            return false;
        }

        ushort port;
        var hasPort = false;
        var hostLength = hostPort.Length;
        var colon = hostPort.IndexOf(':');

        if (colon >= 0)
        {
            if (colon == 0 || colon == hostPort.Length - 1 || hostPort[(colon + 1)..].IndexOf(':') >= 0)
            {
                value = null;

                return false;
            }

            if (!TryParsePort(hostPort[(colon + 1)..], out port))
            {
                value = null;

                return false;
            }

            hasPort = true;
            hostLength = colon;
        }
        else
            port = 0;

        if (!IsValidHost(text.Slice(hostStart, hostLength)))
        {
            value = null;

            return false;
        }

        value = new GitUrl(
            source,
            transport,
            SyntaxKind.Url,
            userStart,
            userLength,
            hostStart,
            hostLength,
            pathStart,
            path.Length,
            0,
            0,
            port,
            hasPort);

        return true;
    }

    private static bool TryParseScp(string source, ReadOnlySpan<char> text, int colon, out GitUrl? value)
    {
        if (colon == 0 || colon == text.Length - 1)
        {
            value = null;

            return false;
        }

        var left = text[..colon];
        var userStart = 0;
        var userLength = 0;
        var hostStart = 0;
        var host = left;
        var at = left.IndexOf('@');

        if (at >= 0)
        {
            if (at == 0 || at == left.Length - 1 || left[(at + 1)..].IndexOf('@') >= 0)
            {
                value = null;

                return false;
            }

            userStart = 0;
            userLength = at;

            if (!IsValidUser(text.Slice(userStart, userLength)))
            {
                value = null;

                return false;
            }

            hostStart = at + 1;
            host = left[(at + 1)..];
        }

        if (!IsValidHost(host))
        {
            value = null;

            return false;
        }

        var pathStart = colon + 1;
        var pathLength = text.Length - pathStart;

        if (!HasNonSlash(text[pathStart..]))
        {
            value = null;

            return false;
        }

        value = new GitUrl(
            source,
            TransportKind.Ssh,
            SyntaxKind.ScpLike,
            userStart,
            userLength,
            hostStart,
            host.Length,
            pathStart,
            pathLength,
            0,
            0,
            0,
            false);

        return true;
    }

    private static bool TryParseLocal(string source, ReadOnlySpan<char> text, out GitUrl? value)
    {
        if (!HasNonSlash(text))
        {
            value = null;

            return false;
        }

        value = new GitUrl(source, TransportKind.Local, SyntaxKind.LocalPath, 0, 0, 0, 0, 0, text.Length, 0, 0, 0, false);

        return true;
    }

    private static bool TryMapScheme(ReadOnlySpan<char> scheme, out TransportKind transport)
    {
        if (EqualsIgnoreCaseAscii(scheme, "ssh"))
        {
            transport = TransportKind.Ssh;

            return true;
        }

        if (EqualsIgnoreCaseAscii(scheme, "git"))
        {
            transport = TransportKind.Git;

            return true;
        }

        if (EqualsIgnoreCaseAscii(scheme, "http"))
        {
            transport = TransportKind.Http;

            return true;
        }

        if (EqualsIgnoreCaseAscii(scheme, "https"))
        {
            transport = TransportKind.Https;

            return true;
        }

        if (EqualsIgnoreCaseAscii(scheme, "ftp"))
        {
            transport = TransportKind.Ftp;

            return true;
        }

        if (EqualsIgnoreCaseAscii(scheme, "ftps"))
        {
            transport = TransportKind.Ftps;

            return true;
        }

        if (EqualsIgnoreCaseAscii(scheme, "file"))
        {
            transport = TransportKind.File;

            return true;
        }

        transport = default;

        return false;
    }

    private static bool IsValidUser(ReadOnlySpan<char> span) =>
        IsValidToken(span, TokenExtras.Dot | TokenExtras.Dash | TokenExtras.Underscore);

    private static bool IsValidHost(ReadOnlySpan<char> span) =>
        IsValidToken(span, TokenExtras.Dot | TokenExtras.Dash);

    private static bool IsValidHelperName(ReadOnlySpan<char> span) =>
        IsValidToken(span, TokenExtras.Dot | TokenExtras.Dash | TokenExtras.Underscore | TokenExtras.Plus);

    private static bool IsValidUrlPath(ReadOnlySpan<char> path) =>
        path.Length > 1 && HasNonSlash(path[1..]);
}
