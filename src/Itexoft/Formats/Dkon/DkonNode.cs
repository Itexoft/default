// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Formats.Dkon.Internal;

namespace Itexoft.Formats.Dkon;

public sealed class DkonNode
{
    private DkonBracing bracing;
    private DkonBracing minBracing;
    private DkonPadding padding;
    private string value;

    public DkonNode(string value)
    {
        this.value = value;
        this.minBracing = DkonSerializer.ChooseBracing(this.value.AsSpan(), DkonBracing.Bare);
    }

    public DkonNode()
    {
        this.value = string.Empty;
        this.bracing = DkonBracing.Bare;
    }

    public DkonBracing Bracing
    {
        get => this.bracing;
        set
        {
            var newValue = Math.Max((int)this.minBracing, (int)value);
            this.bracing = (DkonBracing)Math.Clamp(newValue, (int)DkonBracing.Bare, (int)DkonBracing.Multiline);
        }
    }

    public ref DkonPadding Padding => ref this.padding;

    public DkonNode? Ref { get; set; }
    public DkonNode? Next { get; set; }
    public DkonNode? Alt { get; set; }

    public string Value
    {
        get => this.value;
        set
        {
            this.value = value ?? string.Empty;
            this.minBracing = DkonSerializer.ChooseBracing(this.value.AsSpan(), this.bracing);
        }
    }

    public bool IsEmpty => this.IsEmptyValue && this.Ref is null && this.Next is null && this.Alt is null;

    public bool IsEmptyValue => string.IsNullOrEmpty(this.value);

    public void Beautify() => DkonFormatters.Beautify(this);

    public override string ToString() => this.Value;

    public static implicit operator string(DkonNode? value) => value?.ToString()!;
}
