// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Text;

namespace Itexoft.UI.Cli.TerminalKit.Split.Internal;

internal sealed class TerminalSurfaceTextWriter(TerminalSplitSurface splitSurface, Encoding encoding) : TextWriter
{
    private readonly Encoding encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
    private readonly TerminalSplitSurface splitSurface = splitSurface ?? throw new ArgumentNullException(nameof(splitSurface));

    public override Encoding Encoding => this.encoding;

    public override void Write(char value) => this.splitSurface.Write(stackalloc char[] { value });

    public override void Write(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            this.splitSurface.Write(value.AsSpan());
    }

    public override void Write(ReadOnlySpan<char> buffer)
    {
        if (!buffer.IsEmpty)
            this.splitSurface.Write(buffer);
    }

    public override void Write(char[] buffer, int index, int count)
    {
        if (buffer is null)
            throw new ArgumentNullException(nameof(buffer));

        if (index < 0 || index > buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (count < 0 || count > buffer.Length - index)
            throw new ArgumentOutOfRangeException(nameof(count));

        this.Write(buffer.AsSpan(index, count));
    }

    public override void WriteLine() => this.splitSurface.WriteLine();

    public override void WriteLine(string? value) => this.splitSurface.WriteLine(value);

    public override void Flush() => this.splitSurface.Flush();
}
