// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats;

public sealed class YamlException(
    YamlException.Diagnostic primaryDiagnostic,
    IReadOnlyList<YamlException.Diagnostic> diagnostics,
    Exception? innerException = null) : FormatException(primaryDiagnostic.Message, innerException)
{
    public enum Phase
    {
        Decode,
        Scan,
        Parse,
        SerializeCompose,
        RepresentCompose,
        Bind,
        Emit,
    }

    public Diagnostic PrimaryDiagnostic { get; } = primaryDiagnostic;

    public IReadOnlyList<Diagnostic> Diagnostics { get; } = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));

    public readonly record struct SourceSpan(int Start, int Length, int Line, int Column);

    public sealed record class Diagnostic(string Code, Phase Phase, string Message, SourceSpan? SourceSpan = null, string? StablePath = null);
}
