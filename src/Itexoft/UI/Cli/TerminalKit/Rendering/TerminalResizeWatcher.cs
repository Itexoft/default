// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.UI.Cli.TerminalKit.Rendering;

/// <summary>
/// Polls console window size and notifies listeners when it changes.
/// </summary>
internal sealed class TerminalResizeWatcher : IDisposable
{
    private readonly CancelToken cancelToken = CancelToken.New();
    private readonly Promise watcher;

    public TerminalResizeWatcher(TimeSpan? pollInterval = null)
    {
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(120);

        this.watcher = Promise.RunAsync(
            async () =>
            {
                var width = TerminalDimensions.GetWindowWidthOrDefault(0);
                var height = TerminalDimensions.GetWindowHeightOrDefault(0);

                while (!this.cancelToken.IsRequested)
                {
                    try
                    {
                        await Promise.Delay(interval, this.cancelToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    var currentWidth = TerminalDimensions.GetWindowWidthOrDefault(width);
                    var currentHeight = TerminalDimensions.GetWindowHeightOrDefault(height);

                    if (width != currentWidth || height != currentHeight)
                    {
                        width = currentWidth;
                        height = currentHeight;
                        this.Resized?.Invoke(this, EventArgs.Empty);
                    }
                }
            },
            true,
            this.cancelToken);
    }

    public void Dispose()
    {
        this.cancelToken.Cancel();

        try
        {
            this.watcher.Wait();
        }
        catch (OperationCanceledException) { }
    }

    public event EventHandler? Resized;
}
