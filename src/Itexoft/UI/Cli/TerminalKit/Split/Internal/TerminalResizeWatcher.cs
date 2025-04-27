// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Cli.TerminalKit.Split.Internal;

internal sealed class TerminalResizeWatcher : IDisposable
{
    private readonly ITerminalConsoleDriver driver;
    private readonly Lock gate = new();
    private readonly Action<int, int> onResize;
    private readonly Timer timer;
    private bool disposed;
    private int lastHeight;
    private int lastWidth;

    public TerminalResizeWatcher(ITerminalConsoleDriver driver, Action<int, int> onResize, TimeSpan? interval = null)
    {
        this.driver = driver ?? throw new ArgumentNullException(nameof(driver));
        this.onResize = onResize ?? throw new ArgumentNullException(nameof(onResize));
        this.lastWidth = this.driver.GetWindowWidthOrDefault();
        this.lastHeight = this.driver.GetWindowHeightOrDefault();
        this.timer = new(this.Tick, null, interval ?? TimeSpan.FromMilliseconds(120), interval ?? TimeSpan.FromMilliseconds(120));
    }

    public void Dispose()
    {
        lock (this.gate)
        {
            if (this.disposed)
                return;

            this.disposed = true;
        }

        this.timer.Dispose();
    }

    private void Tick(object? _)
    {
        var width = this.driver.GetWindowWidthOrDefault(this.lastWidth);
        var height = this.driver.GetWindowHeightOrDefault(this.lastHeight);

        lock (this.gate)
        {
            if (this.disposed || (width == this.lastWidth && height == this.lastHeight))
                return;

            this.lastWidth = width;
            this.lastHeight = height;
        }

        this.onResize(width, height);
    }
}
