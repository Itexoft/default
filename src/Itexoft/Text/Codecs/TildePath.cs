// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections.Immutable;
using System.Text;
using Itexoft.Collections;

namespace Itexoft.Text.Codecs;

public readonly struct TildePath
{
    private const char separator = '/';
    public ImmutableArray<TildeSeg> Segments { get; }

    private TildePath(params TildeSeg[] segments) : this(segments.ToImmutableArray()) { }

    private TildePath(ImmutableArray<TildeSeg> segments) => this.Segments = segments.IsDefault ? [] : segments;

    public TildePath(IIterable<TildeSeg> segments) : this((segments ?? throw new ArgumentNullException(nameof(segments))).ToImmutableArray()) { }

    public override string ToString()
    {
        if (this.Segments.IsDefaultOrEmpty)
            return separator.ToString();

        var builder = new StringBuilder(this.Segments.Length * 2 + 1);
        builder.Append(separator);

        for (var i = 0; i < this.Segments.Length; i++)
        {
            if (i != 0)
                builder.Append(separator);

            builder.Append(this.Segments[i].Encoded ?? throw new InvalidOperationException("Path contains an uninitialized segment."));
        }

        return builder.ToString();
    }

    public static TildePath Combine(TildePath path, TildeSeg segment)
    {
        if (path.Segments.IsDefaultOrEmpty)
            return new(segment);

        var builder = ImmutableArray.CreateBuilder<TildeSeg>(path.Segments.Length + 1);
        builder.AddRange(path.Segments);
        builder.Add(segment);

        return new(builder.MoveToImmutable());
    }

    public static TildePath Combine(TildePath left, TildePath right)
    {
        if (left.Segments.IsDefaultOrEmpty)
            return new(right.Segments);

        if (right.Segments.IsDefaultOrEmpty)
            return new(left.Segments);

        var builder = ImmutableArray.CreateBuilder<TildeSeg>(left.Segments.Length + right.Segments.Length);
        builder.AddRange(left.Segments);
        builder.AddRange(right.Segments);

        return new(builder.MoveToImmutable());
    }

    public static implicit operator string(TildePath path) => path.ToString();

    public static implicit operator TildePath(TildeSeg segment) => new(segment);

    public static TildePath operator /(TildePath path, TildeSeg segment) => Combine(path, segment);

    public static TildePath operator /(TildePath left, TildePath right) => Combine(left, right);
}
