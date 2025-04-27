// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Itexoft.Core;

[StructLayout(LayoutKind.Explicit, Pack = 0, Size = 1)]
public readonly struct TriBool : IComparable<TriBool>, IEquatable<TriBool>
{
    public readonly override int GetHashCode() => Unsafe.BitCast<TriBool, sbyte>(this);

    public static implicit operator TriBool(bool value)
    {
        Unsafe.SkipInit(out TriBool v);
        Unsafe.As<TriBool, sbyte>(ref v) = value ? sbyte.MaxValue : sbyte.MinValue;

        return v;
    }

    public static implicit operator TriBool(bool? value)
    {
        Unsafe.SkipInit(out TriBool v);

        if (value.HasValue)
            Unsafe.As<TriBool, sbyte>(ref v) = value == false ? sbyte.MinValue : sbyte.MaxValue;

        return v;
    }

    public int CompareTo(TriBool other) => this == other ? 0 : 1;

    public bool Equals(TriBool other) => other == this;

    public override bool Equals(object? obj) => obj is TriBool triBool && triBool == this;

    public static bool operator ==(TriBool left, TriBool right) => Unsafe.AreSame(ref left, ref right);

    public static bool operator !=(TriBool left, TriBool right) => !Unsafe.AreSame(ref left, ref right);
}
