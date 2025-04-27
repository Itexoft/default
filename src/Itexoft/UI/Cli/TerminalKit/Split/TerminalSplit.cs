// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.UI.Cli.TerminalKit.Split.Internal;

namespace Itexoft.UI.Cli.TerminalKit.Split;

public static class TerminalSplit
{
    private static readonly Lock gate = new();
    private static TerminalSplitHost? activeHost;

    public static bool IsActive
    {
        get
        {
            lock (gate)
                return activeHost is not null && !activeHost.IsDisposed;
        }
    }

    public static TerminalSectionCollection Sections => GetActiveHost().Sections;

    public static TerminalSplitScope Use(int sectionCount, int consolePercent)
    {
        lock (gate)
        {
            if (activeHost is not null && !activeHost.IsDisposed)
                throw new InvalidOperationException("Terminal split is already active.");

            var host = new TerminalSplitHost(sectionCount, consolePercent);
            var scope = new TerminalSplitScope(host, ClearActiveHost);
            activeHost = host;

            return scope;
        }
    }

    private static TerminalSplitHost GetActiveHost()
    {
        lock (gate)
        {
            if (activeHost is null || activeHost.IsDisposed)
                throw new InvalidOperationException("Terminal split is not active.");

            return activeHost;
        }
    }

    private static void ClearActiveHost(TerminalSplitHost host)
    {
        lock (gate)
        {
            if (!ReferenceEquals(activeHost, host))
                return;

            activeHost = null;
        }
    }
}
