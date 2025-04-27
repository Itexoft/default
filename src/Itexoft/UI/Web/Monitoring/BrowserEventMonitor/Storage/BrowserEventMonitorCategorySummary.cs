// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.UI.Web.Monitoring.BrowserEventMonitor.Storage;

internal struct BrowserEventMonitorCategorySummary
{
    private bool hasValue;

    public bool HasValue => this.hasValue;

    public long TimeMinUtcMs { get; private set; }

    public long TimeMaxUtcMs { get; private set; }

    public double ValueMin { get; private set; }

    public double ValueMax { get; private set; }

    public bool HasText { get; private set; }

    public bool AllValuesPositive { get; private set; }

    public void Seed(long timeMinUtcMs, long timeMaxUtcMs, double valueMin, double valueMax, bool hasText, bool allValuesPositive)
    {
        this.hasValue = true;
        this.TimeMinUtcMs = timeMinUtcMs;
        this.TimeMaxUtcMs = timeMaxUtcMs;
        this.ValueMin = valueMin;
        this.ValueMax = valueMax;
        this.HasText = hasText;
        this.AllValuesPositive = allValuesPositive;
    }

    public void Add(in BrowserEventMonitorStoredEvent value)
    {
        if (!this.hasValue)
        {
            this.hasValue = true;
            this.TimeMinUtcMs = value.TimestampUtcMs;
            this.TimeMaxUtcMs = value.TimestampUtcMs;
            this.ValueMin = value.Value;
            this.ValueMax = value.Value;
            this.HasText = value.Text is not null;
            this.AllValuesPositive = value.Value > 0;

            return;
        }

        if (value.TimestampUtcMs < this.TimeMinUtcMs)
            this.TimeMinUtcMs = value.TimestampUtcMs;

        if (value.TimestampUtcMs > this.TimeMaxUtcMs)
            this.TimeMaxUtcMs = value.TimestampUtcMs;

        if (value.Value < this.ValueMin)
            this.ValueMin = value.Value;

        if (value.Value > this.ValueMax)
            this.ValueMax = value.Value;

        this.HasText |= value.Text is not null;
        this.AllValuesPositive &= value.Value > 0;
    }

    public void ApplyTo(BrowserEventMonitorCategoryState category)
    {
        if (!this.hasValue)
        {
            category.TimeMinUtcMs = 0;
            category.TimeMaxUtcMs = 0;
            category.ValueMin = 0;
            category.ValueMax = 0;
            category.HasText = false;
            category.AllValuesPositive = true;

            return;
        }

        category.TimeMinUtcMs = this.TimeMinUtcMs;
        category.TimeMaxUtcMs = this.TimeMaxUtcMs;
        category.ValueMin = this.ValueMin;
        category.ValueMax = this.ValueMax;
        category.HasText = this.HasText;
        category.AllValuesPositive = this.AllValuesPositive;
    }
}
