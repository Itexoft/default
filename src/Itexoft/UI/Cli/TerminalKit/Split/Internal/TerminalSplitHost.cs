// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading;

namespace Itexoft.UI.Cli.TerminalKit.Split.Internal;

internal sealed class TerminalSplitHost : IDisposable
{
    private readonly ITerminalConsoleDriver driver;
    private readonly Lock gate = new();
    private readonly ConsoleColor originalBackgroundColor;
    private readonly ConsoleColor originalForegroundColor;
    private readonly TerminalResizeWatcher? resizeWatcher;
    private readonly TerminalSplitSurface[] sections;
    private readonly TerminalSurfaceState[] sectionStates;
    private readonly TextWriter writer;
    private Disposed disposed = new();
    private int renderedManagedHeight;
    private int unmanagedHeight;
    private int unmanagedTop;
    private int windowHeight;
    private int windowWidth;

    public TerminalSplitHost(int sectionCount, int consolePercent) : this(sectionCount, consolePercent, new TerminalSystemConsoleDriver(), true) { }

    internal TerminalSplitHost(int sectionCount, int consolePercent, ITerminalConsoleDriver driver, bool enableResizeWatcher)
    {
        this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        this.writer = this.driver.Writer ?? throw new InvalidOperationException("Terminal split requires a physical console writer.");
        this.SectionCount = sectionCount.RequiredPositive();

        if (consolePercent is < 1 or > 99)
            throw new ArgumentOutOfRangeException(nameof(consolePercent), consolePercent, "Console percent must be in range 1..99.");

        this.ConsolePercent = consolePercent;
        this.originalForegroundColor = NormalizeColor(this.driver.GetForegroundColorOrDefault(), ConsoleColor.Gray);
        this.originalBackgroundColor = NormalizeColor(this.driver.GetBackgroundColorOrDefault(), ConsoleColor.Black);
        this.sectionStates = new TerminalSurfaceState[this.SectionCount];
        this.sections = new TerminalSplitSurface[this.SectionCount];

        for (var i = 0; i < this.SectionCount; i++)
        {
            this.sectionStates[i] = new(this.originalForegroundColor, this.originalBackgroundColor);
            this.sections[i] = new(this, this.sectionStates[i]);
        }

        this.Sections = new(this);

        lock (this.gate)
        {
            this.ApplyLayoutLocked(this.driver.GetWindowWidthOrDefault(), this.driver.GetWindowHeightOrDefault());
            this.RenderLocked();
        }

        if (enableResizeWatcher)
            this.resizeWatcher = new(this.driver, this.HandleResize);
    }

    public int SectionCount { get; }

    public int ConsolePercent { get; }

    public bool IsDisposed => this.disposed;

    public TerminalSectionCollection Sections { get; }

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        this.resizeWatcher?.Dispose();

        lock (this.gate)
        {
            try
            {
                this.writer.Flush();
            }
            finally
            {
                GC.SuppressFinalize(this);
            }
        }
    }

    public int GetSectionCount()
    {
        this.ThrowIfDisposed();

        return this.sections.Length;
    }

    public TerminalSplitSurface GetSection(int index)
    {
        this.ThrowIfDisposed();

        if ((uint)index >= (uint)this.sections.Length)
            throw new ArgumentOutOfRangeException(nameof(index), index, "Section index is out of range.");

        return this.sections[index];
    }

    internal int GetUnmanagedTop()
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();

            return this.unmanagedTop;
        }
    }

    internal int GetUnmanagedHeight()
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();

            return this.unmanagedHeight;
        }
    }

    public int GetWidth(TerminalSurfaceState surface)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();

            return surface.Required().Width;
        }
    }

    public int GetHeight(TerminalSurfaceState surface)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();

            return surface.Required().Height;
        }
    }

    public int GetCursorLeft(TerminalSurfaceState surface)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();

            return surface.Required().CursorLeft;
        }
    }

    public int GetCursorTop(TerminalSurfaceState surface)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();

            return surface.Required().CursorTop;
        }
    }

    public ConsoleColor GetForegroundColor(TerminalSurfaceState surface)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();

            return surface.Required().ForegroundColor;
        }
    }

    public ConsoleColor GetBackgroundColor(TerminalSurfaceState surface)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();

            return surface.Required().BackgroundColor;
        }
    }

    public void SetCursorLeft(TerminalSurfaceState surface, int value)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();
            surface.Required().SetCursorLeft(value);
        }
    }

    public void SetCursorTop(TerminalSurfaceState surface, int value)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();
            surface.Required().SetCursorTop(value);
        }
    }

    public void SetForegroundColor(TerminalSurfaceState surface, ConsoleColor value)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            surface.Required().SetForegroundColor(value);
        }
    }

    public void SetBackgroundColor(TerminalSurfaceState surface, ConsoleColor value)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            surface.Required().SetBackgroundColor(value);
        }
    }

    public void Clear(TerminalSurfaceState surface)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked();
            this.EnsureLayoutLocked();
            surface.Required().Clear();
            this.RenderLocked();
        }
    }

    public void Write(TerminalSurfaceState surface, ReadOnlySpan<char> buffer, bool appendNewLine, CancelToken cancelToken)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked(cancelToken);
            this.EnsureLayoutLocked();
            surface.Required().Write(buffer, appendNewLine);
            this.RenderLocked();
        }
    }

    public void Flush(CancelToken cancelToken = default)
    {
        lock (this.gate)
        {
            this.ThrowIfDisposedLocked(cancelToken);
            this.EnsureLayoutLocked();
            this.RenderLocked();
        }
    }

    private void HandleResize(int width, int height)
    {
        lock (this.gate)
        {
            if (this.disposed)
                return;

            this.ApplyLayoutLocked(width, height);
            this.RenderLocked();
        }
    }

    private void EnsureLayoutLocked()
    {
        var width = this.driver.GetWindowWidthOrDefault(this.windowWidth > 0 ? this.windowWidth : TerminalConsoleRuntime.DefaultWidth);
        var height = this.driver.GetWindowHeightOrDefault(this.windowHeight > 0 ? this.windowHeight : TerminalConsoleRuntime.DefaultHeight);

        if (width == this.windowWidth && height == this.windowHeight)
            return;

        this.ApplyLayoutLocked(width, height);
    }

    private void ApplyLayoutLocked(int width, int height)
    {
        if (width <= 0)
            width = this.windowWidth > 0 ? this.windowWidth : TerminalConsoleRuntime.DefaultWidth;

        if (height <= 0)
            height = this.windowHeight > 0 ? this.windowHeight : TerminalConsoleRuntime.DefaultHeight;

        this.windowWidth = width;
        this.windowHeight = height;
        this.driver.SyncBufferSize(width, height);

        var unmanagedHeight = Math.Max(1, height * this.ConsolePercent / 100);

        if (unmanagedHeight > height)
            unmanagedHeight = height;

        var managedHeight = Math.Max(0, height - unmanagedHeight);
        var baseSectionHeight = managedHeight / this.SectionCount;
        var extraRows = managedHeight % this.SectionCount;
        var currentTop = 0;

        for (var i = 0; i < this.SectionCount; i++)
        {
            var sectionHeight = baseSectionHeight + (i < extraRows ? 1 : 0);
            this.sectionStates[i].Resize(width, sectionHeight, currentTop);
            currentTop += sectionHeight;
        }

        this.unmanagedTop = managedHeight;
        this.unmanagedHeight = unmanagedHeight;
    }

    private void RenderLocked()
    {
        this.EnsureLayoutLocked();
        var restoreLeft = this.driver.GetCursorLeftOrDefault();
        var restoreTop = this.driver.GetCursorTopOrDefault();
        var restoreForeground = NormalizeColor(this.driver.GetForegroundColorOrDefault(this.originalForegroundColor), this.originalForegroundColor);
        var restoreBackground = NormalizeColor(this.driver.GetBackgroundColorOrDefault(this.originalBackgroundColor), this.originalBackgroundColor);
        var restoreCursorVisible = this.driver.GetCursorVisibleOrDefault();
        var rowBuffer = this.windowWidth == 0 ? [] : new char[this.windowWidth];

        if (this.renderedManagedHeight > this.unmanagedTop)
            this.ClearRowsLocked(this.unmanagedTop, this.renderedManagedHeight, rowBuffer);

        for (var i = 0; i < this.sectionStates.Length; i++)
            this.RenderSurfaceLocked(this.sectionStates[i], rowBuffer);

        this.renderedManagedHeight = this.unmanagedTop;
        this.driver.SetColors(restoreForeground, restoreBackground);
        this.driver.SetCursorPosition(this.ClampRestoreLeft(restoreLeft), this.ClampRestoreTop(restoreTop));
        this.driver.SetCursorVisible(restoreCursorVisible);
        this.writer.Flush();
    }

    private void ClearRowsLocked(int startRow, int endRowExclusive, char[] rowBuffer)
    {
        if (startRow >= endRowExclusive || this.windowWidth == 0)
            return;

        Array.Fill(rowBuffer, ' ');
        this.driver.SetColors(this.originalForegroundColor, this.originalBackgroundColor);

        for (var row = startRow; row < endRowExclusive; row++)
        {
            this.driver.SetCursorPosition(0, row);
            this.writer.Write(rowBuffer, 0, rowBuffer.Length);
        }
    }

    private void RenderSurfaceLocked(TerminalSurfaceState surface, char[] rowBuffer)
    {
        if (surface.Height == 0 || surface.Width == 0)
            return;

        for (var row = 0; row < surface.Height; row++)
        {
            var cells = surface.GetRow(row);

            for (var i = 0; i < cells.Length; i++)
                rowBuffer[i] = cells[i].Value;

            this.driver.SetCursorPosition(0, surface.Top + row);
            var start = 0;

            while (start < cells.Length)
            {
                var foreground = cells[start].Foreground;
                var background = cells[start].Background;
                var end = start + 1;

                while (end < cells.Length && cells[end].Foreground == foreground && cells[end].Background == background)
                    end++;

                this.driver.SetColors(foreground, background);
                this.writer.Write(rowBuffer, start, end - start);
                start = end;
            }
        }
    }

    private int ClampRestoreLeft(int value)
    {
        if (this.windowWidth <= 0)
            return 0;

        if (value < 0)
            return 0;

        return value >= this.windowWidth ? this.windowWidth - 1 : value;
    }

    private int ClampRestoreTop(int value)
    {
        if (this.windowHeight <= 0)
            return 0;

        var minimum = this.unmanagedTop < this.windowHeight ? this.unmanagedTop : this.windowHeight - 1;

        if (value < minimum)
            return minimum;

        return value >= this.windowHeight ? this.windowHeight - 1 : value;
    }

    private void ThrowIfDisposed()
    {
        if (this.disposed)
            throw new ObjectDisposedException(nameof(TerminalSplitHost), "Terminal split host is disposed.");
    }

    private void ThrowIfDisposedLocked(CancelToken cancelToken = default)
    {
        if (this.disposed)
            throw new ObjectDisposedException(nameof(TerminalSplitHost), "Terminal split host is disposed.");

        cancelToken.ThrowIf();
    }

    private static ConsoleColor NormalizeColor(ConsoleColor value, ConsoleColor fallback) =>
        (uint)value <= 15u ? value : fallback;
}
