// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    private const int bitsPerByte = 8;
    private const int jacobiSweepFactor = sizeof(double) * bitsPerByte;

    private static void ValidateDense(ReadOnlySpan<float> vector)
    {
        if (vector.Length == 0)
            throw new ArgumentException("Vector must be non-empty.");

        foreach (var component in vector)
        {
            if (!float.IsFinite(component))
                throw new ArgumentException("Vector contains non-finite values.");
        }
    }

    private static int ValidateDensePair(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDense(left);
        ValidateDense(right);

        if (left.Length != right.Length)
            throw new ArgumentException("Vector shape mismatch.");

        return left.Length;
    }

    private static int ValidateBinaryPair(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right)
    {
        if (left.Length == 0)
            throw new ArgumentException("Vector must be non-empty.");

        if (right.Length == 0)
            throw new ArgumentException("Vector must be non-empty.");

        if (left.Length != right.Length)
            throw new ArgumentException("Vector shape mismatch.");

        return left.Length;
    }

    private static void ValidateBinary(ReadOnlySpan<byte> vector)
    {
        foreach (var value in vector)
        {
            if (value is not 0 and not 1)
                throw new ArgumentException("Vector must contain only binary 0/1 values.");
        }
    }

    private static void ValidateWeighted(ReadOnlySpan<float> weights, int length)
    {
        if (weights.Length == 0)
            throw new ArgumentException("Vector must be non-empty.");

        if (weights.Length != length)
            throw new ArgumentException("Vector shape mismatch.");

        foreach (var weight in weights)
        {
            if (!float.IsFinite(weight) || weight < 0f)
                throw new ArgumentException("Weights must be finite and non-negative.");
        }
    }

    private static void ValidatePositiveScales(ReadOnlySpan<float> scales, int length)
    {
        if (scales.Length == 0)
            throw new ArgumentException("Vector must be non-empty.");

        if (scales.Length != length)
            throw new ArgumentException("Vector shape mismatch.");

        foreach (var value in scales)
        {
            if (!float.IsFinite(value) || value <= 0f)
                throw new ArgumentException("Values must be finite and positive.");
        }
    }

    private static (double SumLeft, double SumRight) ValidateNonNegativePair(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        ValidateDensePair(left, right);
        var leftSum = 0d;
        var rightSum = 0d;

        for (var i = 0; i < left.Length; i++)
        {
            if (left[i] < 0f)
                throw new ArgumentException("Vector values must be non-negative.");

            if (right[i] < 0f)
                throw new ArgumentException("Vector values must be non-negative.");

            leftSum += left[i];
            rightSum += right[i];
        }

        return (leftSum, rightSum);
    }

    private static (double SumP, double SumQ) ValidateProbabilityPair(ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        var (sumP, sumQ) = ValidateNonNegativePair(p, q);

        if (sumP <= 0d)
            throw new ArgumentException("Probability mass must be positive.");

        if (sumQ <= 0d)
            throw new ArgumentException("Probability mass must be positive.");

        return (sumP, sumQ);
    }

    private static void ValidateSorted(ReadOnlySpan<float> values)
    {
        ValidateDense(values);

        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] < values[i - 1])
                throw new ArgumentException("Values must be sorted in non-decreasing order.");
        }
    }

    private static void ValidateMatrix(ReadOnlySpan<float> matrix, int rows, int cols)
    {
        rows.RequiredPositive();
        cols.RequiredPositive();

        if (matrix.Length != checked(rows * cols))
            throw new ArgumentException("Matrix shape mismatch.");

        ValidateDense(matrix);
    }

    private static void ValidatePointCloud(ReadOnlySpan<float> points, int count, int dimension)
    {
        count.RequiredPositive();
        dimension.RequiredPositive();

        if (points.Length != checked(count * dimension))
            throw new ArgumentException("Point cloud layout mismatch.");

        ValidateDense(points);
    }

    private static int GetRowMajorIndex(int row, int col, int width) => checked(row * width + col);

    private static double ClampCosine(double value)
    {
        if (value > 1d)
            return 1d;

        return value < -1d ? -1d : value;
    }

    private static double ClampUnitInterval(double value)
    {
        if (value < 0d)
            return 0d;

        return value > 1d ? 1d : value;
    }

    private static double ProtectNonNegative(double value) => value < 0d ? 0d : value;

    private static double[] ComputeCholesky(ReadOnlySpan<float> matrix, int dimension)
    {
        if (matrix.Length != checked(dimension * dimension))
            throw new ArgumentException("Matrix shape mismatch.");

        var lower = new double[matrix.Length];

        for (var row = 0; row < dimension; row++)
        {
            for (var col = 0; col <= row; col++)
            {
                var left = matrix[GetRowMajorIndex(row, col, dimension)];
                var right = matrix[GetRowMajorIndex(col, row, dimension)];

                if (!float.IsFinite(left) || !float.IsFinite(right))
                    throw new ArgumentException("Matrix contains non-finite values.");

                if (left != right)
                    throw new ArgumentException("Matrix must be symmetric positive definite.");

                var sum = (double)left;

                for (var k = 0; k < col; k++)
                    sum -= lower[GetRowMajorIndex(row, k, dimension)] * lower[GetRowMajorIndex(col, k, dimension)];

                if (row == col)
                {
                    if (sum <= 0d)
                        throw new ArgumentException("Matrix must be symmetric positive definite.");

                    lower[GetRowMajorIndex(row, col, dimension)] = Math.Sqrt(sum);

                    continue;
                }

                lower[GetRowMajorIndex(row, col, dimension)] = sum / lower[GetRowMajorIndex(col, col, dimension)];
            }
        }

        return lower;
    }

    private static double[] ComputeSymmetricEigenvalues(double[] symmetricMatrix, int dimension)
    {
        var matrix = (double[])symmetricMatrix.Clone();
        var sweepCount = checked(dimension * dimension * jacobiSweepFactor);

        for (var sweep = 0; sweep < sweepCount; sweep++)
        {
            var rotated = false;

            for (var p = 0; p < dimension - 1; p++)
            {
                for (var q = p + 1; q < dimension; q++)
                {
                    var pqIndex = GetRowMajorIndex(p, q, dimension);
                    var apq = matrix[pqIndex];

                    if (apq == 0d)
                        continue;

                    var appIndex = GetRowMajorIndex(p, p, dimension);
                    var aqqIndex = GetRowMajorIndex(q, q, dimension);
                    var app = matrix[appIndex];
                    var aqq = matrix[aqqIndex];
                    var tau = (aqq - app) / (2d * apq);
                    var t = tau == 0d ? 1d : Math.Sign(tau) / (Math.Abs(tau) + Math.Sqrt(1d + tau * tau));
                    var c = 1d / Math.Sqrt(1d + t * t);
                    var s = t * c;

                    for (var k = 0; k < dimension; k++)
                    {
                        if (k == p || k == q)
                            continue;

                        var pkIndex = GetRowMajorIndex(p, k, dimension);
                        var kpIndex = GetRowMajorIndex(k, p, dimension);
                        var qkIndex = GetRowMajorIndex(q, k, dimension);
                        var kqIndex = GetRowMajorIndex(k, q, dimension);
                        var apk = matrix[pkIndex];
                        var aqk = matrix[qkIndex];

                        matrix[pkIndex] = c * apk - s * aqk;
                        matrix[kpIndex] = matrix[pkIndex];
                        matrix[qkIndex] = s * apk + c * aqk;
                        matrix[kqIndex] = matrix[qkIndex];
                    }

                    matrix[appIndex] = c * c * app - 2d * c * s * apq + s * s * aqq;
                    matrix[aqqIndex] = s * s * app + 2d * c * s * apq + c * c * aqq;
                    matrix[pqIndex] = 0d;
                    matrix[GetRowMajorIndex(q, p, dimension)] = 0d;
                    rotated = true;
                }
            }

            if (!rotated)
                break;
        }

        var eigenvalues = new double[dimension];

        for (var i = 0; i < dimension; i++)
            eigenvalues[i] = matrix[GetRowMajorIndex(i, i, dimension)];

        return eigenvalues;
    }
}
