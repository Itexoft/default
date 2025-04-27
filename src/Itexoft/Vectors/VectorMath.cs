// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;

namespace Itexoft.Vectors;

/// <summary>
/// Deterministic low-level vector primitives shared by ranking/indexing components.
/// This type intentionally contains only math operations and excludes any domain decisions.
/// </summary>
public static class VectorMath
{
    /// <summary>
    /// Computes component-wise mean vector for a non-empty set of equal-length vectors.
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
                    throw new ArgumentException("Vector contains non-finite values.");

                result[col] += component;
            }
        }

        var scale = 1f / rows;

        for (var col = 0; col < result.Length; col++)
            result[col] *= scale;

        return result;
    }

    /// <summary>
    /// Computes L2 norm and writes normalized values into <paramref name="destination" />.
    /// The method keeps normalization explicit and side-effect free for callers that need deterministic preprocessing.
    /// </summary>
    public static float NormalizeL2(ReadOnlySpan<float> source, Span<float> destination, string argumentName = "source")
    {
        if (source.Length == 0)
            throw new ArgumentException("Vector must be non-empty.", argumentName);

        if (source.Length != destination.Length)
            throw new ArgumentException("Source and destination lengths must match.", nameof(destination));

        var norm = NormL2(source, argumentName);

        if (norm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.", argumentName);

        var scale = 1f / norm;

        for (var i = 0; i < source.Length; i++)
            destination[i] = source[i] * scale;

        return norm;
    }

    /// <summary>
    /// Computes deterministic cosine similarity for two vectors with strict shape and finite-value checks.
    /// </summary>
    public static float Cosine(
        ReadOnlySpan<float> left,
        ReadOnlySpan<float> right,
        string leftArgumentName = "left",
        string rightArgumentName = "right")
    {
        if (left.Length == 0)
            throw new ArgumentException("Vector must be non-empty.", leftArgumentName);

        if (right.Length == 0)
            throw new ArgumentException("Vector must be non-empty.", rightArgumentName);

        if (left.Length != right.Length)
            throw new ArgumentException("Vector shape mismatch.");

        var leftNorm = NormL2(left, leftArgumentName);
        var rightNorm = NormL2(right, rightArgumentName);

        if (leftNorm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.", leftArgumentName);

        if (rightNorm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.", rightArgumentName);

        return Dot(left, right) / (leftNorm * rightNorm);
    }

    /// <summary>
    /// Computes deterministic dot product for vectors with equal shape.
    /// </summary>
    public static float Dot(ReadOnlySpan<float> left, ReadOnlySpan<float> right)
    {
        if (left.Length == 0 || right.Length == 0 || left.Length != right.Length)
            throw new ArgumentException("Vector shape mismatch.");

        var sum = 0d;

        for (var i = 0; i < left.Length; i++)
            sum += left[i] * right[i];

        return (float)sum;
    }

    /// <summary>
    /// Computes L2 norm and validates that all components are finite.
    /// </summary>
    public static float NormL2(ReadOnlySpan<float> vector, string argumentName = "vector")
    {
        if (vector.Length == 0)
            throw new ArgumentException("Vector must be non-empty.", argumentName);

        var sum = 0d;

        foreach (var component in vector)
        {
            if (!float.IsFinite(component))
                throw new ArgumentException("Vector contains non-finite values.", argumentName);

            sum += component * component;
        }

        return (float)Math.Sqrt(sum);
    }

    /// <summary>
    /// Selects the best and second-best cosine matches in deterministic index order for equal scores.
    /// This is useful for margin-based confidence checks without introducing domain-specific policies.
    /// </summary>
    public static bool TrySelectTop2ByCosine(
        ReadOnlySpan<float> query,
        IReadOnlyList<ReadOnlyMemory<float>> candidates,
        out int bestIndex,
        out float bestScore,
        out int secondIndex,
        out float secondScore,
        bool includeExactMatch = true)
    {
        if (query.Length == 0)
            throw new ArgumentException("Vector must be non-empty.", nameof(query));

        if (candidates is null)
            throw new ArgumentNullException(nameof(candidates));

        bestIndex = -1;
        secondIndex = -1;
        bestScore = float.NegativeInfinity;
        secondScore = float.NegativeInfinity;
        var queryNorm = NormL2(query, nameof(query));

        if (queryNorm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.", nameof(query));

        for (var i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i].Span;

            if (candidate.Length != query.Length)
                throw new ArgumentException($"Vector length mismatch. Expected {query.Length}, got {candidate.Length}.", nameof(candidates));

            if (!includeExactMatch && query.SequenceEqual(candidate))
                continue;

            var candidateNorm = NormL2(candidate, nameof(candidates));

            if (candidateNorm == 0f)
                throw new ArgumentException("Vector norm must be non-zero.", nameof(candidates));

            var score = Dot(query, candidate) / (queryNorm * candidateNorm);

            if (bestIndex < 0 || score > bestScore || (score == bestScore && i < bestIndex))
            {
                secondIndex = bestIndex;
                secondScore = bestScore;
                bestIndex = i;
                bestScore = score;

                continue;
            }

            if (secondIndex < 0 || score > secondScore || (score == secondScore && i < secondIndex))
            {
                secondIndex = i;
                secondScore = score;
            }
        }

        return bestIndex >= 0;
    }
}
