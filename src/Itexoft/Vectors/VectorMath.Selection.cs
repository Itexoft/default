// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Tries to pick the best and second-best cosine matches from a candidate list.
    /// Use it when you need not only the top hit, but also the runner-up to judge confidence or ambiguity.
    /// </summary>
    public static bool TrySelectTop2ByCosine(
        ReadOnlySpan<float> query,
        IEnumerable<ReadOnlyMemory<float>> candidates,
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
        var queryNorm = NormL2(query);

        if (queryNorm == 0f)
            throw new ArgumentException("Vector norm must be non-zero.", nameof(query));

        var index = 0;

        foreach (var vector in candidates)
        {
            var candidate = vector.Span;
            var i = index++;

            if (candidate.Length != query.Length)
                throw new ArgumentException($"Vector length mismatch. Expected {query.Length}, got {candidate.Length}.", nameof(candidates));

            if (!includeExactMatch && query.SequenceEqual(candidate))
                continue;

            var candidateNorm = NormL2(candidate);

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
