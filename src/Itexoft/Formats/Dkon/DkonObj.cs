// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Diagnostics.CodeAnalysis;
using Itexoft.Extensions;

namespace Itexoft.Formats.Dkon;

public readonly partial struct DkonObj : IEquatable<DkonObj>, IEnumerable<DkonObj>
{
    private readonly EqualityComparer<string> comparer = EqualityComparer<string>.Default;
    private readonly DkonNode value;
    public bool IsEmpty => this.value.IsEmpty;
    public bool IsEmptyValue => this.value.IsEmptyValue;

    public DkonObj() => this.value = new();

    public DkonObj(string value) => this.value = new DkonNode(value);

    internal DkonObj(DkonNode value) => this.value = value.Required();

    public ref DkonPadding Padding => ref this.value.Padding;

    public string Value
    {
        get => this.value.Value ?? string.Empty;
        set => this.value.Value = value;
    }

    public DkonObj? this[string key]
    {
        get
        {
            if (this.TryGetObj(key, out var obj))
                return obj;

            return null;
        }
    }

    public bool TryGetObj(string key, [NotNullWhen(true)] out DkonObj? value)
    {
        foreach (var (k, v) in this.AsPairs())
        {
            if (!this.comparer.Equals(k, key))
                continue;

            value = v;

            return true;
        }

        value = null;

        return false;
    }

    public bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        if (this.TryGetObj(key, out var obj))
            value = obj.Value;

        value = null;

        return false;
    }

    public IEnumerable<string> GetObjs(string key)
    {
        foreach (var (k, v) in this.AsPairs())
        {
            if (!this.comparer.Equals(k, key))
                yield return v;
        }
    }

    public IEnumerable<string> GetValues(string key)
    {
        foreach (var (k, v) in this.AsPairs())
        {
            if (!this.comparer.Equals(k, key))
                yield return v.value;
        }
    }

    public bool Equals(DkonObj other)
    {
        if (!ReferenceEquals(other.value, this.value))
            return false;

        return other.value.Value == this.value?.Value;
    }

    public override string ToString() => this.value.ToString();

    public string Serialize() => DkonFormat.SerializeObj(this)!;

    public string BeautifySerialize()
    {
        DkonFormatters.Beautify(this);

        return DkonFormat.SerializeObj(this)!;
    }

    public override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is not DkonObj other)
            return false;

        return this.Equals(other);
    }

    public override int GetHashCode() => this.value is null ? 0 : this.value.Value.GetHashCode();

    public static bool operator ==(DkonObj left, DkonObj right) => left.Equals(right);

    public static bool operator !=(DkonObj left, DkonObj right) => !left.Equals(right);

    public static bool operator ==(string left, DkonObj right) => right.comparer.Equals(left, right.value);

    public static bool operator !=(string left, DkonObj right) => !right.comparer.Equals(left, right.value);

    public static bool operator ==(DkonObj left, string right) => left.comparer.Equals(left.Value, right);

    public static bool operator !=(DkonObj left, string right) => !left.comparer.Equals(left.Value, right);

    public DkonObj Add(DkonObj key, DkonObj value)
    {
        this.Add(key);
        key.value.Ref = value.value;

        return value;
    }

    public DkonObj Add(DkonObj key)
    {
        if (ReferenceEquals(key.value, this.value))
            throw new ArgumentException("Node is already linked.", nameof(key));

        AppendAxis(this.value, key.value);

        return key;
    }

    private static void AppendAxis(DkonNode root, DkonNode item)
    {
        if (root.Alt is null)
        {
            root.Alt = item;

            return;
        }

        AppendNext(root.Alt, item);
    }

    private static void AppendNext(DkonNode start, DkonNode item)
    {
        var tail = start;

        while (tail.Next is not null)
            tail = tail.Next;

        tail.Next = item;
    }

    public static implicit operator string(DkonObj obj) => obj.value.Value;
    public static implicit operator DkonObj(string obj) => new(obj);

    internal DkonNode Node => this.value;
}
