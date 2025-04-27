// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Itexoft.Extensions;

namespace Itexoft.Core;

public readonly struct Index64 : IEquatable<Index64>
{
    private readonly long value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Index64(long value, bool fromEnd = false)
    {
        if (fromEnd)
            this.value = ~value.RequiredPositiveOrZero();
        else
            this.value = value.RequiredPositiveOrZero();
    }

    private Index64(long value) => this.value = value;

    public static Index64 Start => new(0);

    public static Index64 End => new(~0);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Index64 FromStart(long value) => new(value.RequiredPositiveOrZero());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Index64 FromEnd(long value) => new(~value.RequiredPositiveOrZero());

    public long Value
    {
        get
        {
            if (this.value < 0)
                return ~this.value;

            return this.value;
        }
    }

    public bool IsFromEnd => this.value < 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public long GetOffset(long length)
    {
        var offset = this.value;

        if (this.IsFromEnd)
            offset += length + 1;

        return offset;
    }

    public override bool Equals([NotNullWhen(true)] object? value) => value is Index64 index64 && this.value == index64.value;

    public bool Equals(Index64 other) => this.value == other.value;

    public override int GetHashCode() => this.value.GetHashCode();

    public static implicit operator Index64(long value) => FromStart(value);

    public override string ToString()
    {
        if (this.IsFromEnd)
        {
            Span<char> span = stackalloc char[30];
            ((uint)this.Value).TryFormat(span[1..], out var charsWritten);
            span[0] = '^';

            return new string(span[..(charsWritten + 1)]);
        }

        return ((uint)this.Value).ToString();
    }

    public static bool operator ==(Index64 left, Index64 right) => left.Equals(right);

    public static bool operator !=(Index64 left, Index64 right) => !left.Equals(right);
}
