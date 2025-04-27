// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;
using Itexoft.Threading;
using Itexoft.UI.Cli.TerminalKit.Split.Internal;

namespace Itexoft.UI.Cli.TerminalKit.Split;

public sealed class TerminalSplitSurface : IStreamW<char>
{
    private readonly TerminalSplitHost host;
    private readonly TerminalSurfaceState state;

    internal TerminalSplitSurface(TerminalSplitHost host, TerminalSurfaceState state)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.state = state ?? throw new ArgumentNullException(nameof(state));
    }

    internal TerminalSurfaceState State => this.state;

    public int Width => this.host.GetWidth(this.state);

    public int Height => this.host.GetHeight(this.state);

    public int CursorLeft
    {
        get => this.host.GetCursorLeft(this.state);
        set => this.host.SetCursorLeft(this.state, value);
    }

    public int CursorTop
    {
        get => this.host.GetCursorTop(this.state);
        set => this.host.SetCursorTop(this.state, value);
    }

    public ConsoleColor ForegroundColor
    {
        get => this.host.GetForegroundColor(this.state);
        set => this.host.SetForegroundColor(this.state, value);
    }

    public ConsoleColor BackgroundColor
    {
        get => this.host.GetBackgroundColor(this.state);
        set => this.host.SetBackgroundColor(this.state, value);
    }

    public void Write(ReadOnlySpan<char> buffer, CancelToken cancelToken = default) => this.host.Write(this.state, buffer, false, cancelToken);

    public void Flush(CancelToken cancelToken = default) => this.host.Flush(cancelToken);

    public void Dispose() { }

    public void Clear() => this.host.Clear(this.state);

    public void WriteLine(ReadOnlySpan<char> buffer, CancelToken cancelToken = default) => this.host.Write(this.state, buffer, true, cancelToken);

    public void WriteLine(string? value, CancelToken cancelToken = default)
    {
        if (!string.IsNullOrEmpty(value))
            this.host.Write(this.state, value.AsSpan(), true, cancelToken);
        else
            this.host.Write(this.state, ReadOnlySpan<char>.Empty, true, cancelToken);
    }

    public void WriteLine(CancelToken cancelToken = default) => this.host.Write(this.state, ReadOnlySpan<char>.Empty, true, cancelToken);
}
