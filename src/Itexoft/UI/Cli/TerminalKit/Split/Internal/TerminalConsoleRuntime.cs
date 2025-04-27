// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Cli.TerminalKit.Split.Internal;

internal static class TerminalConsoleRuntime
{
    public const int DefaultWidth = 120;
    public const int DefaultHeight = 30;

    public static int GetWindowWidthOrDefault(int fallback = DefaultWidth)
    {
        try
        {
            var width = Console.WindowWidth;

            return width > 0 ? width : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static int GetWindowHeightOrDefault(int fallback = DefaultHeight)
    {
        try
        {
            var height = Console.WindowHeight;

            return height > 0 ? height : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    public static void SyncBufferSize(int width, int height)
    {
        try
        {
#pragma warning disable CA1416
            if (width > 0 && Console.BufferWidth != width)
                Console.BufferWidth = width;

            if (height > 0 && Console.BufferHeight != height)
                Console.BufferHeight = height;
#pragma warning restore CA1416
        }
        catch { }
    }

    public static bool GetCursorVisibleOrDefault(bool fallback = true)
    {
        try
        {
#pragma warning disable CA1416
            return Console.CursorVisible;
#pragma warning restore CA1416
        }
        catch
        {
            return fallback;
        }
    }

    public static void SetCursorVisible(bool value)
    {
        try
        {
#pragma warning disable CA1416
            Console.CursorVisible = value;
#pragma warning restore CA1416
        }
        catch { }
    }

    public static ConsoleColor GetForegroundColorOrDefault(ConsoleColor fallback = ConsoleColor.Gray)
    {
        try
        {
            return Console.ForegroundColor;
        }
        catch
        {
            return fallback;
        }
    }

    public static ConsoleColor GetBackgroundColorOrDefault(ConsoleColor fallback = ConsoleColor.Black)
    {
        try
        {
            return Console.BackgroundColor;
        }
        catch
        {
            return fallback;
        }
    }

    public static int GetCursorLeftOrDefault(int fallback = 0)
    {
        try
        {
            return Console.CursorLeft;
        }
        catch
        {
            return fallback;
        }
    }

    public static int GetCursorTopOrDefault(int fallback = 0)
    {
        try
        {
            return Console.CursorTop;
        }
        catch
        {
            return fallback;
        }
    }

    public static void SetCursorPosition(int left, int top) => Console.SetCursorPosition(left, top);

    public static void SetColors(ConsoleColor foreground, ConsoleColor background)
    {
        Console.ForegroundColor = foreground;
        Console.BackgroundColor = background;
    }
}
