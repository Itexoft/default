// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Returns one-dimensional Wasserstein distance for aligned histogram bins on a shared ordered axis.
    /// Use it when both vectors already live on the same ordered support and you want transport over mass, not over raw point samples.
    /// </summary>
    public static double DistanceWassersteinHistogram1D(ReadOnlySpan<float> binsA, ReadOnlySpan<float> binsB)
    {
        _ = ValidateNonNegativePair(binsA, binsB);
        var cumulative = 0d;
        var distance = 0d;

        for (var i = 0; i < binsA.Length; i++)
        {
            cumulative += binsA[i] - binsB[i];
            distance += Math.Abs(cumulative);
        }

        return distance;
    }

    /// <summary>
    /// Returns one-dimensional Wasserstein distance for sorted scalar samples.
    /// Use it when values live on an ordered line and moving mass along that line is the right notion of difference.
    /// </summary>
    public static double DistanceWasserstein1D(ReadOnlySpan<float> sortedA, ReadOnlySpan<float> sortedB)
    {
        ValidateSorted(sortedA);
        ValidateSorted(sortedB);
        var weightA = 1d / sortedA.Length;
        var weightB = 1d / sortedB.Length;
        var indexA = 0;
        var indexB = 0;
        var cdfA = 0d;
        var cdfB = 0d;
        var previous = (double)Math.Min(sortedA[0], sortedB[0]);
        var distance = 0d;

        while (indexA < sortedA.Length || indexB < sortedB.Length)
        {
            var nextA = indexA < sortedA.Length ? sortedA[indexA] : double.PositiveInfinity;
            var nextB = indexB < sortedB.Length ? sortedB[indexB] : double.PositiveInfinity;
            var next = Math.Min(nextA, nextB);
            distance += Math.Abs(cdfA - cdfB) * (next - previous);

            while (indexA < sortedA.Length && sortedA[indexA] == next)
            {
                cdfA += weightA;
                indexA++;
            }

            while (indexB < sortedB.Length && sortedB[indexB] == next)
            {
                cdfB += weightB;
                indexB++;
            }

            previous = next;
        }

        return distance;
    }

    /// <summary>
    /// Returns entropic Sinkhorn transport distance for two point clouds.
    /// Use it when you want optimal-transport-style geometry on samples but need something cheaper and more practical than exact transport.
    /// </summary>
    public static double DistanceSinkhorn(
        ReadOnlySpan<float> pointsA,
        int countA,
        ReadOnlySpan<float> pointsB,
        int countB,
        int dimension,
        double epsilon,
        int maxIterations)
    {
        ValidatePointCloud(pointsA, countA, dimension);
        ValidatePointCloud(pointsB, countB, dimension);

        if (!double.IsFinite(epsilon) || epsilon <= 0d)
            throw new ArgumentOutOfRangeException(nameof(epsilon), epsilon, "Value must be finite and positive.");

        maxIterations.RequiredPositive();
        var costs = new double[countA * countB];

        for (var row = 0; row < countA; row++)
        {
            for (var col = 0; col < countB; col++)
                costs[GetRowMajorIndex(row, col, countB)] = DistancePointL2(pointsA, row, pointsB, col, dimension);
        }

        var kernel = new double[costs.Length];

        for (var i = 0; i < costs.Length; i++)
            kernel[i] = Math.Exp(-costs[i] / epsilon);

        var u = new double[countA];
        var v = new double[countB];
        Array.Fill(u, 1d);
        Array.Fill(v, 1d);
        var weightA = 1d / countA;
        var weightB = 1d / countB;

        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            for (var row = 0; row < countA; row++)
            {
                var denominator = 0d;

                for (var col = 0; col < countB; col++)
                    denominator += kernel[GetRowMajorIndex(row, col, countB)] * v[col];

                if (denominator == 0d)
                    throw new ArgumentException("Epsilon is too small for the supplied geometry.", nameof(epsilon));

                u[row] = weightA / denominator;
            }

            for (var col = 0; col < countB; col++)
            {
                var denominator = 0d;

                for (var row = 0; row < countA; row++)
                    denominator += kernel[GetRowMajorIndex(row, col, countB)] * u[row];

                if (denominator == 0d)
                    throw new ArgumentException("Epsilon is too small for the supplied geometry.", nameof(epsilon));

                v[col] = weightB / denominator;
            }
        }

        var distance = 0d;

        for (var row = 0; row < countA; row++)
        {
            for (var col = 0; col < countB; col++)
            {
                var plan = u[row] * kernel[GetRowMajorIndex(row, col, countB)] * v[col];
                distance += plan * costs[GetRowMajorIndex(row, col, countB)];
            }
        }

        return distance;
    }

    /// <summary>
    /// Returns sliced Wasserstein distance by projecting point clouds onto caller-supplied directions.
    /// Use it when full transport is too expensive but you still want distribution geometry, not just pointwise statistics.
    /// </summary>
    public static double DistanceSlicedWasserstein(
        ReadOnlySpan<float> pointsA,
        int countA,
        ReadOnlySpan<float> pointsB,
        int countB,
        int dimension,
        ReadOnlySpan<float> directions)
    {
        ValidatePointCloud(pointsA, countA, dimension);
        ValidatePointCloud(pointsB, countB, dimension);
        ValidateDense(directions);

        if (directions.Length % dimension != 0)
            throw new ArgumentException("Directions layout mismatch.", nameof(directions));

        var directionCount = directions.Length / dimension;

        if (directionCount == 0)
            throw new ArgumentException("Directions must be non-empty.", nameof(directions));

        var projectedA = new float[countA];
        var projectedB = new float[countB];
        var direction = new double[dimension];
        var sum = 0d;

        for (var directionIndex = 0; directionIndex < directionCount; directionIndex++)
        {
            var offset = directionIndex * dimension;
            var normSquared = 0d;

            for (var component = 0; component < dimension; component++)
            {
                direction[component] = directions[offset + component];
                normSquared += direction[component] * direction[component];
            }

            if (normSquared == 0d)
                throw new ArgumentException("Direction norm must be non-zero.", nameof(directions));

            var scale = 1d / Math.Sqrt(normSquared);

            for (var component = 0; component < dimension; component++)
                direction[component] *= scale;

            ProjectPointCloud(pointsA, countA, dimension, direction, projectedA);
            ProjectPointCloud(pointsB, countB, dimension, direction, projectedB);
            Array.Sort(projectedA);
            Array.Sort(projectedB);
            sum += DistanceWasserstein1D(projectedA, projectedB);
        }

        return sum / directionCount;
    }

    /// <summary>
    /// Returns maximum mean discrepancy with an RBF kernel.
    /// Use it when you want to compare whole sample distributions and answer “are these two clouds drawn from the same shape of data?”
    /// </summary>
    public static double MaximumMeanDiscrepancyRbf(
        ReadOnlySpan<float> pointsA,
        int countA,
        ReadOnlySpan<float> pointsB,
        int countB,
        int dimension,
        double gamma)
    {
        ValidatePointCloud(pointsA, countA, dimension);
        ValidatePointCloud(pointsB, countB, dimension);

        if (!double.IsFinite(gamma) || gamma <= 0d)
            throw new ArgumentOutOfRangeException(nameof(gamma), gamma, "Value must be finite and positive.");

        var meanAa = 0d;
        var meanBb = 0d;
        var meanAb = 0d;

        for (var left = 0; left < countA; left++)
        {
            for (var right = 0; right < countA; right++)
                meanAa += ComputeRbfKernel(pointsA, left, pointsA, right, dimension, gamma);
        }

        for (var left = 0; left < countB; left++)
        {
            for (var right = 0; right < countB; right++)
                meanBb += ComputeRbfKernel(pointsB, left, pointsB, right, dimension, gamma);
        }

        for (var left = 0; left < countA; left++)
        {
            for (var right = 0; right < countB; right++)
                meanAb += ComputeRbfKernel(pointsA, left, pointsB, right, dimension, gamma);
        }

        meanAa /= (double)countA * countA;
        meanBb /= (double)countB * countB;
        meanAb /= (double)countA * countB;
        var squared = ProtectNonNegative(meanAa + meanBb - 2d * meanAb);

        return Math.Sqrt(squared);
    }

    private static void ProjectPointCloud(ReadOnlySpan<float> points, int count, int dimension, double[] direction, Span<float> destination)
    {
        for (var pointIndex = 0; pointIndex < count; pointIndex++)
        {
            var projection = 0d;

            for (var component = 0; component < dimension; component++)
                projection += points[GetRowMajorIndex(pointIndex, component, dimension)] * direction[component];

            destination[pointIndex] = (float)projection;
        }
    }

    private static double DistancePointL2(ReadOnlySpan<float> left, int leftIndex, ReadOnlySpan<float> right, int rightIndex, int dimension)
    {
        var sum = 0d;

        for (var component = 0; component < dimension; component++)
        {
            var delta = left[GetRowMajorIndex(leftIndex, component, dimension)] - right[GetRowMajorIndex(rightIndex, component, dimension)];
            sum += delta * delta;
        }

        return Math.Sqrt(sum);
    }

    private static double ComputeRbfKernel(
        ReadOnlySpan<float> left,
        int leftIndex,
        ReadOnlySpan<float> right,
        int rightIndex,
        int dimension,
        double gamma)
    {
        var squaredDistance = 0d;

        for (var component = 0; component < dimension; component++)
        {
            var delta = left[GetRowMajorIndex(leftIndex, component, dimension)] - right[GetRowMajorIndex(rightIndex, component, dimension)];
            squaredDistance += delta * delta;
        }

        return Math.Exp(-gamma * squaredDistance);
    }
}
