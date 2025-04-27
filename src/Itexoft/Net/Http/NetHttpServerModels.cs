// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.Net.Http;

public delegate NetHttpResponse NetHttpRequestHandler(NetHttpRequest request, CancelToken cancelToken);

public readonly record struct NetHttpMatch(NetHttpRequest Request, int Offset, bool Matched)
{
    public static NetHttpMatch operator /(NetHttpMatch left, string right)
    {
        if (!left.Matched)
            return new(left.Request, left.Offset, false);

        return TryMatchSegment(left.Request.PathAndQuery.Path, left.Offset, right, out var nextOffset)
            ? new(left.Request, nextOffset, true)
            : new(left.Request, left.Offset, false);
    }

    public static bool operator ==(NetHttpMatch left, string right) =>
        left.Matched && MatchRemainder(left.Request.PathAndQuery.Path, left.Offset, right);

    public static bool operator !=(NetHttpMatch left, string right) => !(left == right);

    public static bool operator true(NetHttpMatch value) => value.Matched;

    public static bool operator false(NetHttpMatch value) => !value.Matched;

    public override int GetHashCode() => HashCode.Combine(RuntimeHelpers.GetHashCode(this.Request), this.Offset, this.Matched);

    private static bool MatchRemainder(string path, int offset, string fragment)
    {
        fragment.Required();

        var pathStart = offset < path.Length ? offset + 1 : path.Length;
        var pathLength = path.Length - pathStart;

        if (fragment.Length == 0)
            return pathLength == 0;

        if (fragment.Length != pathLength)
            return false;

        return path.AsSpan(pathStart).SequenceEqual(fragment.AsSpan());
    }

    private static bool TryMatchSegment(string path, int offset, string segment, out int nextOffset)
    {
        segment.RequiredNotEmpty();

        if (segment.Contains('/'))
            throw new ArgumentException("Segment cannot contain '/'.", nameof(segment));

        var start = offset < path.Length ? offset + 1 : path.Length;

        if (start >= path.Length)
        {
            nextOffset = offset;

            return false;
        }

        var slash = path.AsSpan(start).IndexOf('/');
        var end = slash < 0 ? path.Length : start + slash;
        var length = end - start;

        if (length != segment.Length || !path.AsSpan(start, length).SequenceEqual(segment.AsSpan()))
        {
            nextOffset = offset;

            return false;
        }

        nextOffset = end;

        return true;
    }
}
