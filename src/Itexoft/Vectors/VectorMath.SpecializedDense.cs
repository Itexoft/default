// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Returns Euclidean distance with per-coordinate weights.
    /// Use it when some features should matter more than others and you want to encode that importance directly in the distance.
    /// </summary>
    public static double DistanceWeightedL2(ReadOnlySpan<float> left, ReadOnlySpan<float> right, ReadOnlySpan<float> weights)
    {
        var length = ValidateDensePair(left, right);
        ValidateWeighted(weights, length);
        var sum = 0d;

        for (var i = 0; i < length; i++)
        {
            var delta = left[i] - right[i];
            sum += weights[i] * delta * delta;
        }

        return Math.Sqrt(sum);
    }

    /// <summary>
    /// Returns Euclidean distance after dividing each squared difference by a supplied variance.
    /// Use it when coordinates live on very different scales and large-variance features should not dominate the result.
    /// </summary>
    public static double DistanceStandardizedL2(ReadOnlySpan<float> left, ReadOnlySpan<float> right, ReadOnlySpan<float> variances)
    {
        var length = ValidateDensePair(left, right);
        ValidatePositiveScales(variances, length);
        var sum = 0d;

        for (var i = 0; i < length; i++)
        {
            var delta = left[i] - right[i];
            sum += delta * delta / variances[i];
        }

        return Math.Sqrt(sum);
    }

    /// <summary>
    /// Returns Mahalanobis distance using a supplied inverse covariance matrix.
    /// Use it when features are correlated and plain Euclidean distance does not reflect the real geometry of the space.
    /// </summary>
    public static double DistanceMahalanobis(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right,
        ReadOnlySpan<float> inverseCovariance,
        int dimension)
    {
        if (dimension <= 0)
            throw new ArgumentOutOfRangeException(nameof(dimension), dimension, "Value must be positive.");

        ValidateDensePair(left, right);

        if (left.Length != dimension)
            throw new ArgumentException("Vector shape mismatch.");

        _ = ComputeCholesky(inverseCovariance, dimension);
        var sum = 0d;

        for (var row = 0; row < dimension; row++)
        {
            var rowValue = 0d;
            var deltaRow = left[row] - right[row];

            for (var col = 0; col < dimension; col++)
                rowValue += inverseCovariance[GetRowMajorIndex(row, col, dimension)] * (left[col] - right[col]);

            sum += deltaRow * rowValue;
        }

        return Math.Sqrt(ProtectNonNegative(sum));
    }

    /// <summary>
    /// Returns Bray-Curtis distance, which compares difference relative to total mass.
    /// Use it for non-negative count-like or abundance-like vectors when composition matters more than raw scale.
    /// </summary>
    public static double DistanceBrayCurtis(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var numerator = 0d;
        var denominator = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] < 0f)
                throw new ArgumentException("Vector values must be non-negative.");

            if (right[i] < 0f)
                throw new ArgumentException("Vector values must be non-negative.");

            numerator += Math.Abs(left[i] - right[i]);
            denominator += left[i] + right[i];
        }

        if (denominator == 0d)
            throw new ArgumentException("Total mass must be positive.");

        return numerator / denominator;
    }

    /// <summary>
    /// Returns Canberra distance, which strongly emphasizes differences near zero.
    /// Use it for sparse or low-magnitude signals where small values still carry important meaning.
    /// </summary>
    public static double DistanceCanberra(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var sum = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            var numerator = Math.Abs(left[i] - right[i]);
            var denominator = Math.Abs(left[i]) + Math.Abs(right[i]);

            if (denominator == 0d)
                continue;

            sum += numerator / denominator;
        }

        return sum;
    }
}
