// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Cli.TerminalKit.Split.Internal;

internal sealed class TerminalSurfaceState
{
    private TerminalCell[] cells = [];

    public TerminalSurfaceState(ConsoleColor foregroundColor, ConsoleColor backgroundColor)
    {
        ValidateColor(foregroundColor, nameof(foregroundColor));
        ValidateColor(backgroundColor, nameof(backgroundColor));
        this.ForegroundColor = foregroundColor;
        this.BackgroundColor = backgroundColor;
    }

    public int Width { get; private set; }

    public int Height { get; private set; }

    public int Top { get; private set; }

    public int CursorLeft { get; private set; }

    public int CursorTop { get; private set; }

    public ConsoleColor ForegroundColor { get; private set; }

    public ConsoleColor BackgroundColor { get; private set; }

    public void Resize(int width, int height, int top)
    {
        if (width < 0)
            throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be zero or positive.");

        if (height < 0)
            throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be zero or positive.");

        if (top < 0)
            throw new ArgumentOutOfRangeException(nameof(top), top, "Top must be zero or positive.");

        var resized = width == 0 || height == 0 ? [] : new TerminalCell[width * height];
        Fill(resized, width, height, this.CreateBlankCell());

        var copyWidth = Math.Min(this.Width, width);
        var copyHeight = Math.Min(this.Height, height);

        for (var row = 0; row < copyHeight; row++)
            Array.Copy(this.cells, row * this.Width, resized, row * width, copyWidth);

        this.cells = resized;
        this.Width = width;
        this.Height = height;
        this.Top = top;
        this.CursorLeft = ClampCursor(this.CursorLeft, width);
        this.CursorTop = ClampCursor(this.CursorTop, height);
    }

    public void Clear()
    {
        Fill(this.cells, this.Width, this.Height, this.CreateBlankCell());
        this.CursorLeft = 0;
        this.CursorTop = 0;
    }

    public void SetCursorLeft(int value) => this.CursorLeft = ValidateCursor(value, this.Width, nameof(value));

    public void SetCursorTop(int value) => this.CursorTop = ValidateCursor(value, this.Height, nameof(value));

    public void SetForegroundColor(ConsoleColor value)
    {
        ValidateColor(value, nameof(value));
        this.ForegroundColor = value;
    }

    public void SetBackgroundColor(ConsoleColor value)
    {
        ValidateColor(value, nameof(value));
        this.BackgroundColor = value;
    }

    public void Write(ReadOnlySpan<char> buffer, bool appendNewLine)
    {
        foreach (var value in buffer)
            this.WriteChar(value);

        if (appendNewLine)
            this.WriteChar('\n');
    }

    public ReadOnlySpan<TerminalCell> GetRow(int row)
    {
        if ((uint)row >= (uint)this.Height)
            throw new ArgumentOutOfRangeException(nameof(row), row, "Row is out of range.");

        return this.Width == 0 ? ReadOnlySpan<TerminalCell>.Empty : this.cells.AsSpan(row * this.Width, this.Width);
    }

    private void WriteChar(char value)
    {
        if (value == '\r')
        {
            this.CursorLeft = 0;

            return;
        }

        if (value == '\n')
        {
            this.CursorLeft = 0;
            this.MoveToNextLine();

            return;
        }

        if (char.IsControl(value))
            value = ' ';

        if (this.Width == 0 || this.Height == 0)
            return;

        this.cells[this.CursorTop * this.Width + this.CursorLeft] = new(value, this.ForegroundColor, this.BackgroundColor);
        this.CursorLeft++;

        if (this.CursorLeft < this.Width)
            return;

        this.CursorLeft = 0;
        this.MoveToNextLine();
    }

    private void MoveToNextLine()
    {
        if (this.Height == 0)
        {
            this.CursorTop = 0;

            return;
        }

        this.CursorTop++;

        if (this.CursorTop < this.Height)
            return;

        this.Scroll();
        this.CursorTop = this.Height - 1;
    }

    private void Scroll()
    {
        if (this.Width == 0 || this.Height == 0)
            return;

        if (this.Height > 1)
            Array.Copy(this.cells, this.Width, this.cells, 0, this.Width * (this.Height - 1));

        this.FillRow(this.Height - 1, this.CreateBlankCell());
    }

    private void FillRow(int row, TerminalCell value)
    {
        if (this.Width == 0 || this.Height == 0)
            return;

        var offset = row * this.Width;

        for (var i = 0; i < this.Width; i++)
            this.cells[offset + i] = value;
    }

    private TerminalCell CreateBlankCell() => new(' ', this.ForegroundColor, this.BackgroundColor);

    private static void Fill(TerminalCell[] cells, int width, int height, TerminalCell value)
    {
        if (cells.Length == 0 || width == 0 || height == 0)
            return;

        Array.Fill(cells, value);
    }

    private static int ValidateCursor(int value, int size, string parameterName)
    {
        if (size == 0)
        {
            if (value == 0)
                return 0;

            throw new ArgumentOutOfRangeException(parameterName, value, "Cursor must be zero for an empty surface.");
        }

        if ((uint)value >= (uint)size)
            throw new ArgumentOutOfRangeException(parameterName, value, "Cursor is out of range.");

        return value;
    }

    private static int ClampCursor(int value, int size)
    {
        if (size == 0)
            return 0;

        if (value < 0)
            return 0;

        return value >= size ? size - 1 : value;
    }

    private static void ValidateColor(ConsoleColor value, string parameterName)
    {
        if ((uint)value > 15u)
            throw new ArgumentOutOfRangeException(parameterName, value, "Console color is out of range.");
    }
}
