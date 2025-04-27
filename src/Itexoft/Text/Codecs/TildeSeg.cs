// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Codecs;

public readonly struct TildeSeg
{
    public TildeSeg(string decoded) : this(decoded, true) { }

    public TildeSeg(string decoded, bool encode)
    {
        if (encode)
        {
            this.Decoded = decoded;
            this.Encoded = TildeSegCodec.Encode(decoded);
        }
        else
        {
            this.Decoded = TildeSegCodec.Decode(decoded);
            this.Encoded = decoded;
        }
    }

    public string Decoded { get; }
    public string Encoded { get; }

    public override string ToString() => this.Decoded;
    public static implicit operator TildeSeg(string key) => new(key, true);
    public static implicit operator TildeSeg(long key) => new(key.ToString(), true);

    public static TildePath operator /(TildeSeg path, TildeSeg segment) => TildePath.Combine(path, segment);
    public static TildePath operator /(string path, TildeSeg segment) => TildePath.Combine(new TildeSeg(path), segment);
    public static TildePath operator /(TildeSeg path, string segment) => TildePath.Combine(path, segment);
}
