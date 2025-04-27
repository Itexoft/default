// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Counts how many positions differ between two byte sequences.
    /// Use it for binary codes, compact fingerprints, or any representation where each position is a discrete symbol.
    /// </summary>
    public static int DistanceHamming(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        ValidateBinaryPair(left, right);
        var count = 0;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] != right[i])
                count++;
        }

        return count;
    }

    /// <summary>
    /// Returns Jaccard similarity for binary vectors.
    /// Use it when a vector really means “which features are present” and shared zeros should not count as evidence of similarity.
    /// </summary>
    public static double Jaccard(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        ValidateBinaryPair(left, right);
        ValidateBinary(left);
        ValidateBinary(right);
        var intersection = 0;
        var union = 0;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] == 1 && right[i] == 1)
                intersection++;

            if (left[i] == 1 || right[i] == 1)
                union++;
        }

        return union == 0 ? 1d : (double)intersection / union;
    }

    /// <summary>
    /// Returns Jaccard distance, which is <c>1 - Jaccard similarity</c>.
    /// Use it when set overlap is the right notion of similarity but you want a distance value instead of a score.
    /// </summary>
    public static double DistanceJaccard(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) =>
        1d - Jaccard(left, right);

    /// <summary>
    /// Returns Tanimoto similarity for non-negative vectors.
    /// Use it when you want a weighted overlap measure that is more expressive than binary Jaccard.
    /// </summary>
    public static double Tanimoto(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var dot = 0d;
        var leftNormSquared = 0d;
        var rightNormSquared = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] < 0f)
                throw new ArgumentException("Vector values must be non-negative.");

            if (right[i] < 0f)
                throw new ArgumentException("Vector values must be non-negative.");

            dot += left[i] * right[i];
            leftNormSquared += left[i] * left[i];
            rightNormSquared += right[i] * right[i];
        }

        var denominator = leftNormSquared + rightNormSquared - dot;

        if (denominator == 0d)
            return 1d;

        return dot / denominator;
    }

    /// <summary>
    /// Returns Tanimoto distance, which is <c>1 - Tanimoto similarity</c>.
    /// Use it when weighted overlap is the right idea but your caller expects a smaller-is-better distance.
    /// </summary>
    public static double DistanceTanimoto(ReadOnlySpan<float> left, ReadOnlySpan<float> right) =>
        1d - Tanimoto(left, right);
}
