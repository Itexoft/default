// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;

namespace Itexoft.UI.Web.Monitoring.BrowserEventMonitor.Storage;

internal sealed class BrowserEventMonitorCategoryState(string key, long creationOrder, IStreamRwsl<byte> eventStream)
{
    public string Key { get; } = key;

    public long CreationOrder { get; } = creationOrder;

    public IStreamRwsl<byte> EventStream { get; set; } = eventStream;

    public long SeriesRevision { get; set; }

    public long LastChangedGlobalRevision { get; set; }

    public int Count { get; set; }

    public long AppendBaseRevision { get; set; }

    public int AppendBaseCount { get; set; }

    public long TimeMinUtcMs { get; set; }

    public long TimeMaxUtcMs { get; set; }

    public double ValueMin { get; set; }

    public double ValueMax { get; set; }

    public bool HasText { get; set; }

    public bool AllValuesPositive { get; set; } = true;
}
