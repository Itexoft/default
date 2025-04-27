// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Returns normalized longest-common-subsequence similarity.
    /// Use it when sequence identity matters and you want a bounded overlap score that tolerates insertions without collapsing the whole match.
    /// </summary>
    public static double SimilarityLongestCommonSubsequence<T>(ReadOnlySpan<T> left, ReadOnlySpan<T> right, IEqualityComparer<T>? comparer = null)
    {
        if (left.Length == 0)
            throw new ArgumentException("Sequence must be non-empty.", nameof(left));

        if (right.Length == 0)
            throw new ArgumentException("Sequence must be non-empty.", nameof(right));

        comparer ??= EqualityComparer<T>.Default;
        var previous = new int[right.Length + 1];
        var current = new int[right.Length + 1];

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            current[0] = 0;

            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                current[rightIndex] = comparer.Equals(left[leftIndex - 1], right[rightIndex - 1])
                    ? previous[rightIndex - 1] + 1
                    : Math.Max(previous[rightIndex], current[rightIndex - 1]);
            }

            (previous, current) = (current, previous);
        }

        return (double)previous[right.Length] / Math.Max(left.Length, right.Length);
    }

    /// <summary>
    /// Returns dynamic time warping distance for two sequences of vectors.
    /// Use it when two sequences have the same general shape but may be stretched, compressed, or shifted in time.
    /// </summary>
    public static double DistanceDynamicTimeWarping(
        IReadOnlyList<ReadOnlyMemory<float>> xs,
        IReadOnlyList<ReadOnlyMemory<float>> ys,
        VectorStepCost localCost)
    {
        xs.Required();
        ys.Required();
        localCost.Required();

        if (xs.Count == 0)
            throw new ArgumentException("Sequence must be non-empty.", nameof(xs));

        if (ys.Count == 0)
            throw new ArgumentException("Sequence must be non-empty.", nameof(ys));

        var previous = new double[ys.Count + 1];
        var current = new double[ys.Count + 1];
        Array.Fill(previous, double.PositiveInfinity);
        previous[0] = 0d;

        for (var xIndex = 1; xIndex <= xs.Count; xIndex++)
        {
            current[0] = double.PositiveInfinity;

            for (var yIndex = 1; yIndex <= ys.Count; yIndex++)
            {
                var cost = localCost(xs[xIndex - 1].Span, ys[yIndex - 1].Span);
                var bestPrefix = Math.Min(previous[yIndex], Math.Min(current[yIndex - 1], previous[yIndex - 1]));
                current[yIndex] = cost + bestPrefix;
            }

            (previous, current) = (current, previous);
        }

        return previous[ys.Count];
    }
}
