// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Cli.TerminalKit.Split.Internal;

internal interface ITerminalConsoleDriver
{
    TextWriter Writer { get; }

    int GetWindowWidthOrDefault(int fallback = TerminalConsoleRuntime.DefaultWidth);

    int GetWindowHeightOrDefault(int fallback = TerminalConsoleRuntime.DefaultHeight);

    void SyncBufferSize(int width, int height);

    bool GetCursorVisibleOrDefault(bool fallback = true);

    void SetCursorVisible(bool value);

    ConsoleColor GetForegroundColorOrDefault(ConsoleColor fallback = ConsoleColor.Gray);

    ConsoleColor GetBackgroundColorOrDefault(ConsoleColor fallback = ConsoleColor.Black);

    int GetCursorLeftOrDefault(int fallback = 0);

    int GetCursorTopOrDefault(int fallback = 0);

    void SetCursorPosition(int left, int top);

    void SetColors(ConsoleColor foreground, ConsoleColor background);
}
