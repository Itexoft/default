// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Core;
using Itexoft.UI.Cli.TerminalKit.Split.Internal;

namespace Itexoft.UI.Cli.TerminalKit.Split;

public sealed class TerminalSplitScope : IDisposable
{
    private readonly Action<TerminalSplitHost> clearActiveHost;
    private readonly TerminalSplitHost host;
    private Disposed disposed = new();

    internal TerminalSplitScope(TerminalSplitHost host, Action<TerminalSplitHost> clearActiveHost)
    {
        this.host = host ?? throw new ArgumentNullException(nameof(host));
        this.clearActiveHost = clearActiveHost ?? throw new ArgumentNullException(nameof(clearActiveHost));
    }

    public int SectionCount => this.host.SectionCount;

    public int ConsolePercent => this.host.ConsolePercent;

    public void Dispose()
    {
        if (this.disposed.Enter())
            return;

        try
        {
            this.host.Dispose();
        }
        finally
        {
            this.clearActiveHost(this.host);
            GC.SuppressFinalize(this);
        }
    }
}
