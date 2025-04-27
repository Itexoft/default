// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.UI.Cli.TerminalKit.Split.Internal;

namespace Itexoft.UI.Cli.TerminalKit.Split;

public sealed class TerminalSectionCollection
{
    private readonly TerminalSplitHost host;

    internal TerminalSectionCollection(TerminalSplitHost host) => this.host = host ?? throw new ArgumentNullException(nameof(host));

    public int Count => this.host.GetSectionCount();

    public TerminalSplitSurface this[int index] => this.host.GetSection(index);
}
