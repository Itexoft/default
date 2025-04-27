// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Returns the residual distance after the best orthogonal alignment between two matrices.
    /// Use it when two vector spaces may be the same up to rotation and you want to compare them after that best rigid alignment.
    /// </summary>
    public static double DistanceOrthogonalProcrustes(ReadOnlySpan<float> a, ReadOnlySpan<float> b, int rows, int cols)
    {
        ValidateMatrix(a, rows, cols);
        ValidateMatrix(b, rows, cols);
        var cross = CreateCrossProduct(a, b, rows, cols);
        var singularValues = ComputeSingularValues(cross, cols);
        var nuclearNorm = 0d;
        var normA = 0d;
        var normB = 0d;

        foreach (var singularValue in singularValues)
            nuclearNorm += singularValue;

        foreach (var value in a)
            normA += value * value;

        foreach (var value in b)
            normB += value * value;

        return Math.Sqrt(ProtectNonNegative(normA + normB - 2d * nuclearNorm));
    }

    /// <summary>
    /// Returns geodesic distance between two subspaces through their principal angles.
    /// Use it when the object you compare is not a single vector but a basis or subspace, such as PCA directions or local linear models.
    /// </summary>
    public static double DistanceGrassmannGeodesic(ReadOnlySpan<float> basisA, ReadOnlySpan<float> basisB, int rows, int cols)
    {
        ValidateMatrix(basisA, rows, cols);
        ValidateMatrix(basisB, rows, cols);

        if (cols > rows)
            throw new ArgumentException("Basis width cannot exceed row count.", nameof(cols));

        var orthonormalA = BuildOrthonormalBasis(basisA, rows, cols);
        var orthonormalB = BuildOrthonormalBasis(basisB, rows, cols);
        var cross = CreateCrossProduct(orthonormalA, orthonormalB, rows, cols);
        var singularValues = ComputeSingularValues(cross, cols);
        var sum = 0d;

        foreach (var singularValue in singularValues)
        {
            var angle = Math.Acos(ClampUnitInterval(singularValue));
            sum += angle * angle;
        }

        return Math.Sqrt(sum);
    }

    private static double[] BuildOrthonormalBasis(ReadOnlySpan<float> matrix, int rows, int cols)
    {
        var orthonormal = new double[matrix.Length];
        var workspace = new double[rows];

        for (var col = 0; col < cols; col++)
        {
            for (var row = 0; row < rows; row++)
                workspace[row] = matrix[GetRowMajorIndex(row, col, cols)];

            for (var previousCol = 0; previousCol < col; previousCol++)
            {
                var projection = 0d;

                for (var row = 0; row < rows; row++)
                    projection += orthonormal[GetRowMajorIndex(row, previousCol, cols)] * workspace[row];

                for (var row = 0; row < rows; row++)
                    workspace[row] -= projection * orthonormal[GetRowMajorIndex(row, previousCol, cols)];
            }

            var normSquared = 0d;

            for (var row = 0; row < rows; row++)
                normSquared += workspace[row] * workspace[row];

            if (normSquared == 0d)
                throw new ArgumentException("Basis must have full column rank.");

            var scale = 1d / Math.Sqrt(normSquared);

            for (var row = 0; row < rows; row++)
                orthonormal[GetRowMajorIndex(row, col, cols)] = workspace[row] * scale;
        }

        return orthonormal;
    }

    private static double[] CreateCrossProduct(ReadOnlySpan<float> left, ReadOnlySpan<float> right, int rows, int cols)
    {
        var product = new double[cols * cols];

        for (var leftCol = 0; leftCol < cols; leftCol++)
        {
            for (var rightCol = 0; rightCol < cols; rightCol++)
            {
                var sum = 0d;

                for (var row = 0; row < rows; row++)
                    sum += left[GetRowMajorIndex(row, leftCol, cols)] * right[GetRowMajorIndex(row, rightCol, cols)];

                product[GetRowMajorIndex(leftCol, rightCol, cols)] = sum;
            }
        }

        return product;
    }

    private static double[] CreateCrossProduct(double[] left, double[] right, int rows, int cols)
    {
        var product = new double[cols * cols];

        for (var leftCol = 0; leftCol < cols; leftCol++)
        {
            for (var rightCol = 0; rightCol < cols; rightCol++)
            {
                var sum = 0d;

                for (var row = 0; row < rows; row++)
                    sum += left[GetRowMajorIndex(row, leftCol, cols)] * right[GetRowMajorIndex(row, rightCol, cols)];

                product[GetRowMajorIndex(leftCol, rightCol, cols)] = sum;
            }
        }

        return product;
    }

    private static double[] ComputeSingularValues(double[] squareMatrix, int dimension)
    {
        var gram = new double[squareMatrix.Length];

        for (var row = 0; row < dimension; row++)
        {
            for (var col = 0; col < dimension; col++)
            {
                var sum = 0d;

                for (var k = 0; k < dimension; k++)
                    sum += squareMatrix[GetRowMajorIndex(k, row, dimension)] * squareMatrix[GetRowMajorIndex(k, col, dimension)];

                gram[GetRowMajorIndex(row, col, dimension)] = sum;
            }
        }

        var eigenvalues = ComputeSymmetricEigenvalues(gram, dimension);
        var singularValues = new double[eigenvalues.Length];

        for (var i = 0; i < eigenvalues.Length; i++)
            singularValues[i] = Math.Sqrt(ProtectNonNegative(eigenvalues[i]));

        return singularValues;
    }
}
