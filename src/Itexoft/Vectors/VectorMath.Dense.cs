// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Returns the component-wise average of a batch of vectors.
    /// Use it when you want one “typical” vector that summarizes many examples, such as a centroid or prototype.
    /// </summary>
    public static ReadOnlySpan<float> Mean(float[,] vectors)
    {
        vectors.RequiredNotEmpty();
        var rows = vectors.GetLength(0);
        var cols = vectors.GetLength(1);

        if (cols == 0)
            throw new ArgumentException("Vector must be non-empty.", nameof(vectors));

        var result = new float[cols];

        for (var row = 0; row < rows; row++)
        {
            for (var col = 0; col < cols; col++)
            {
                var component = vectors[row, col];

                if (!float.IsFinite(component))
                    throw new ArgumentException("Vector contains non-finite values.", nameof(vectors));

                result[col] += component;
            }
        }

        var scale = 1f / rows;

        for (var col = 0; col < result.Length; col++)
            result[col] *= scale;

        return result;
    }

    public static ReadOnlySpan<float> MeanCosine(float[][] vectors)
    {
        if (vectors.Required().Length == 0)
            throw new ArgumentException("Vector batch must be non-empty.", nameof(vectors));

        if (vectors.Length == 1)
            return vectors[0];

        var mean = new float[vectors[0].Length];

        foreach (var vector in vectors)
        {
            var norm = NormL2(vector);

            if (norm == 0f)
                continue;

            for (var i = 0; i < mean.Length; i++)
                mean[i] += vector[i] / norm;
        }

        var meanNorm = NormL2(mean);

        if (meanNorm == 0f)
            return vectors[^1];

        for (var i = 0; i < mean.Length; i++)
            mean[i] /= meanNorm;

        return mean;
    }

    /// <summary>
    /// Returns the mean direction in cosine space by L2-normalizing each input vector, averaging the normalized vectors,
    /// and L2-normalizing the result again.
    /// Use it when you want one semantic prototype from a batch of embeddings where direction matters more than magnitude.
    /// </summary>
    public static ReadOnlySpan<float> MeanCosine(float[,] vectors)
    {
        vectors.RequiredNotEmpty();
        var rows = vectors.GetLength(0);
        var cols = vectors.GetLength(1);

        if (cols == 0)
            throw new ArgumentException("Vector must be non-empty.", nameof(vectors));

        var result = new float[cols];

        for (var row = 0; row < rows; row++)
        {
            var normSquared = 0d;

            for (var col = 0; col < cols; col++)
            {
                var component = vectors[row, col];

                if (!float.IsFinite(component))
                    throw new ArgumentException("Vector contains non-finite values.", nameof(vectors));

                normSquared += component * component;
            }

            if (normSquared == 0d)
                throw new ArgumentException("Vector norm must be non-zero.", nameof(vectors));

            var scale = 1d / Math.Sqrt(normSquared);

            for (var col = 0; col < cols; col++)
                result[col] += (float)(vectors[row, col] * scale);
        }

        var meanScale = 1f / rows;

        for (var col = 0; col < result.Length; col++)
            result[col] *= meanScale;

        NormalizeL2(result, result);

        return result;
    }

    /// <summary>
    /// Writes the L2-normalized version of a vector into the destination span and returns the original vector length.
    /// Use it when direction matters more than magnitude and you want a unit vector for cosine-style comparison.
    /// </summary>
    public static float NormalizeL2(ReadOnlySpan<float> source, Span<float> destination)
    {
        ValidateDense(source);

        if (source.Length != destination.Length)
            throw new ArgumentException("Source and destination lengths must match.", nameof(destination));

        var norm = NormL2(source);

        if (norm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.");

        var scale = 1f / norm;

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i] * scale;

        return norm;
    }

    /// <summary>
    /// Returns cosine similarity, which compares vector direction instead of raw length.
    /// Use it for semantic embeddings or other cases where “pointing the same way” matters more than absolute scale.
    /// </summary>
    public static float Cosine(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var dot = 0d;
        var leftNormSquared = 0d;
        var rightNormSquared = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftNormSquared += left[i] * left[i];
            rightNormSquared += right[i] * right[i];
        }

        if (leftNormSquared == 0d)
            throw new ArgumentException("Vector norm must be non-zero.");

        if (rightNormSquared == 0d)
            throw new ArgumentException("Vector norm must be non-zero.");

        return (float)ClampCosine(dot / (Math.Sqrt(leftNormSquared) * Math.Sqrt(rightNormSquared)));
    }

    /// <summary>
    /// Returns the dot product of two vectors by multiplying matching components and summing the results.
    /// Use it when vector magnitude is meaningful, such as linear scoring, projection, or weighted matching.
    /// </summary>
    public static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var sum = 0d;

        for (var i = 0; i < left.Length; i++)
            sum += left[i] * right[i];

        return (float)sum;
    }

    /// <summary>
    /// Returns the ordinary L2 length of a vector.
    /// Use it when you want to know how large or strong a vector is before normalizing it or comparing magnitudes.
    /// </summary>
    public static float NormL2(ReadOnlySpan<float> vector)
    {
        ValidateDense(vector);
        var sum = 0d;

        foreach (var component in vector)
            sum += component * component;

        return (float)Math.Sqrt(sum);
    }

    /// <summary>
    /// Returns squared Euclidean distance.
    /// Use it when you need standard geometric distance but do not need the final square root, for example in ranking or optimization loops.
    /// </summary>
    public static double DistanceL2Squared(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var sum = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            var delta = left[i] - right[i];
            sum += delta * delta;
        }

        return sum;
    }

    /// <summary>
    /// Returns Euclidean distance, the usual straight-line distance between two vectors.
    /// Use it when absolute geometric difference matters and you want the result in the same scale as the coordinates.
    /// </summary>
    public static double DistanceL2(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right,
        string leftArgumentName = "left",
        string rightArgumentName = "right") =>
        Math.Sqrt(DistanceL2Squared(left, right));

    /// <summary>
    /// Returns Manhattan distance, the sum of absolute per-component differences.
    /// Use it when you want a more outlier-tolerant distance than Euclidean distance.
    /// </summary>
    public static double DistanceL1(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right,
        string leftArgumentName = "left",
        string rightArgumentName = "right")
    {
        ValidateDensePair(left, right);
        var sum = 0d;

        for (var i = 0; i < left.Length; i++)
            sum += Math.Abs(left[i] - right[i]);

        return sum;
    }

    /// <summary>
    /// Returns Chebyshev distance, the largest single-coordinate difference.
    /// Use it when the worst offending component is what matters, such as tolerance checks or hard limits.
    /// </summary>
    public static double DistanceLInf(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var max = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            var delta = Math.Abs(left[i] - right[i]);

            if (delta > max)
                max = delta;
        }

        return max;
    }

    /// <summary>
    /// Returns the Minkowski distance for the supplied <paramref name="p" />.
    /// Use it when you want one family of distances that can smoothly move between L1, L2, and stronger penalty shapes.
    /// </summary>
    public static double DistanceLp(ReadOnlySpan<float> left, ReadOnlySpan<float> right, double p)
    {
        ValidateDensePair(left, right);
        p.RequiredGreaterOrEqual(1d);

        if (p == 1d)
            return DistanceL1(left, right);

        if (p == 2d)
            return DistanceL2(left, right);

        var sum = 0d;

        for (var i = 0; i < left.Length; i++)
            sum += Math.Pow(Math.Abs(left[i] - right[i]), p);

        return Math.Pow(sum, 1d / p);
    }

    /// <summary>
    /// Returns cosine distance, which is <c>1 - cosine similarity</c>.
    /// Use it when you want cosine-style comparison but a smaller-is-better distance value.
    /// </summary>
    public static double DistanceCosine(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right,
        string leftArgumentName = "left",
        string rightArgumentName = "right") =>
        1d - Cosine(left, right);

    /// <summary>
    /// Returns the angular distance in radians.
    /// Use it when you want the actual angle between normalized directions, not just the <c>1 - cosine</c> shortcut.
    /// </summary>
    public static double DistanceAngular(ReadOnlySpan<float> left, ReadOnlySpan<float> right) =>
        Math.Acos(ClampCosine(Cosine(left, right)));

    /// <summary>
    /// Returns Pearson correlation, which compares the shape of two signals after removing their average level.
    /// Use it when two vectors may have different offsets or scales but you still care about whether they rise and fall together.
    /// </summary>
    public static double CorrelationPearson(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var count = 0d;
        var meanLeft = 0d;
        var meanRight = 0d;
        var sumSquaresLeft = 0d;
        var sumSquaresRight = 0d;
        var covariance = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            count++;
            var deltaLeft = left[i] - meanLeft;
            meanLeft += deltaLeft / count;
            var deltaRight = right[i] - meanRight;
            meanRight += deltaRight / count;
            sumSquaresLeft += deltaLeft * (left[i] - meanLeft);
            sumSquaresRight += deltaRight * (right[i] - meanRight);
            covariance += deltaLeft * (right[i] - meanRight);
        }

        if (sumSquaresLeft == 0d)
            throw new ArgumentException("Centered vector variance must be positive.");

        if (sumSquaresRight == 0d)
            throw new ArgumentException("Centered vector variance must be positive.");

        return ClampCosine(covariance / Math.Sqrt(sumSquaresLeft * sumSquaresRight));
    }

    /// <summary>
    /// Returns Pearson distance, which is <c>1 - Pearson correlation</c>.
    /// Use it when you want Pearson-style shape matching but need a smaller-is-better distance form.
    /// </summary>
    public static double DistancePearson(ReadOnlySpan<float> left, ReadOnlySpan<float> right) =>
        1d - CorrelationPearson(left, right);
}
