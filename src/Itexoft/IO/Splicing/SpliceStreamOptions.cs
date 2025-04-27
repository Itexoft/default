// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.InteropServices;
using Itexoft.Extensions;

namespace Itexoft.IO.Splicing;

public record struct SpliceStreamOptions
{
    private const int defaultMaxPromiseLength = 32 * 1024 * 1024;
    private const int defaultSegmentBytes = 4096;

    public SpliceStreamOptions()
    {
        this = default;
        this.MaxPromiseLength = defaultMaxPromiseLength;
        this.SegmentBytes = defaultSegmentBytes;
        this.UnboundCue =  UnboundCuePolicy.Throw;
        this.Backpressure =  BackpressureMode.Block;
    }

    public int MaxPromiseLength
    {
        get;
        init => field = value.RequiredPositive();
    }

    public UnboundCuePolicy UnboundCue { get; init; }

    public BackpressureMode Backpressure { get; init; }

    internal int SegmentBytes
    {
        get;
        init => field = value.RequiredPositive();
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct SpliceStreamOptionsNative(SpliceStreamOptions options, int elementSize)
{
    public readonly int SegmentBytes = options.SegmentBytes.RequiredPositive();
    public readonly int MaxPromiseLength = options.MaxPromiseLength;
    public readonly UnboundCuePolicy UnboundCue = options.UnboundCue;
    public readonly BackpressureMode Backpressure = options.Backpressure;
    public readonly int ElementSize = elementSize;
}

public enum UnboundCuePolicy : byte
{
    Empty,
    Throw,
}

public enum BackpressureMode : byte
{
    Block,
    Throw,
}
