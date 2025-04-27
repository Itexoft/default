// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Cli.TerminalKit.Split.Internal;

internal sealed class TerminalSystemConsoleDriver : ITerminalConsoleDriver
{
    public TextWriter Writer => Console.Out;

    public int GetWindowWidthOrDefault(int fallback = TerminalConsoleRuntime.DefaultWidth) =>
        TerminalConsoleRuntime.GetWindowWidthOrDefault(fallback);

    public int GetWindowHeightOrDefault(int fallback = TerminalConsoleRuntime.DefaultHeight) =>
        TerminalConsoleRuntime.GetWindowHeightOrDefault(fallback);

    public void SyncBufferSize(int width, int height) => TerminalConsoleRuntime.SyncBufferSize(width, height);

    public bool GetCursorVisibleOrDefault(bool fallback = true) => TerminalConsoleRuntime.GetCursorVisibleOrDefault(fallback);

    public void SetCursorVisible(bool value) => TerminalConsoleRuntime.SetCursorVisible(value);

    public ConsoleColor GetForegroundColorOrDefault(ConsoleColor fallback = ConsoleColor.Gray) =>
        TerminalConsoleRuntime.GetForegroundColorOrDefault(fallback);

    public ConsoleColor GetBackgroundColorOrDefault(ConsoleColor fallback = ConsoleColor.Black) =>
        TerminalConsoleRuntime.GetBackgroundColorOrDefault(fallback);

    public int GetCursorLeftOrDefault(int fallback = 0) => TerminalConsoleRuntime.GetCursorLeftOrDefault(fallback);

    public int GetCursorTopOrDefault(int fallback = 0) => TerminalConsoleRuntime.GetCursorTopOrDefault(fallback);

    public void SetCursorPosition(int left, int top) => TerminalConsoleRuntime.SetCursorPosition(left, top);

    public void SetColors(ConsoleColor foreground, ConsoleColor background) => TerminalConsoleRuntime.SetColors(foreground, background);
}
