// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Vectors;

public static partial class VectorMath
{
    /// <summary>
    /// Writes a non-negative vector normalized to unit total mass into the destination span and returns the original total mass.
    /// Use it when raw counts or weights should become a probability distribution.
    /// </summary>
    public static float NormalizeProbability(ReadOnlySpan<float> source, Span<float> destination, string argumentName = "source")
    {
        ValidateDense(source);

        if (source.Length != destination.Length)
            throw new ArgumentException("Source and destination lengths must match.", nameof(destination));

        var total = 0d;

        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] < 0f)
                throw new ArgumentException("Vector values must be non-negative.", argumentName);

            total += source[i];
        }

        if (total <= 0d)
            throw new ArgumentException("Probability mass must be positive.", argumentName);

        var scale = 1d / total;

        for (var i = 0; i < source.Length; i++)
            destination[i] = (float)(source[i] * scale);

        return (float)total;
    }

    /// <summary>
    /// Returns bounded overlap agreement for aligned non-negative mass vectors as <c>1 - 0.5 * L1</c>.
    /// Use it when two vectors already share the same support and you want a similarity score instead of a smaller-is-better total-variation-style distance.
    /// </summary>
    public static double AgreementTotalVariation(ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        _ = ValidateNonNegativePair(p, q);
        var totalVariation = 0d;

        for (var i = 0; i < p.Length; i++)
            totalVariation += Math.Abs(p[i] - q[i]);

        return ClampUnitInterval(1d - totalVariation / 2d);
    }

    /// <summary>
    /// Returns Kullback-Leibler divergence from <paramref name="p" /> to <paramref name="q" />.
    /// Use it when you want to know how well one probability distribution explains another and direction matters.
    /// </summary>
    public static double DivergenceKl(ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        var (sumP, sumQ) = ValidateProbabilityPair(p, q);
        var divergence = 0d;

        for (var i = 0; i < p.Length; i++)
        {
            var pi = p[i] / sumP;

            if (pi == 0d)
                continue;

            var qi = q[i] / sumQ;

            if (qi == 0d)
                return double.PositiveInfinity;

            divergence += pi * Math.Log(pi / qi);
        }

        return ProtectNonNegative(divergence);
    }

    /// <summary>
    /// Returns Jensen-Shannon divergence.
    /// Use it as a safer symmetric alternative to KL divergence when you compare probability vectors with real-world zeros and noise.
    /// </summary>
    public static double DivergenceJensenShannon(ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        var (sumP, sumQ) = ValidateProbabilityPair(p, q);
        var divergence = 0d;

        for (var i = 0; i < p.Length; i++)
        {
            var pi = p[i] / sumP;
            var qi = q[i] / sumQ;
            var midpoint = (pi + qi) / 2d;

            if (pi != 0d)
                divergence += 0.5d * pi * Math.Log(pi / midpoint);

            if (qi != 0d)
                divergence += 0.5d * qi * Math.Log(qi / midpoint);
        }

        return ProtectNonNegative(divergence);
    }

    /// <summary>
    /// Returns Jensen-Shannon distance, which is the square root of Jensen-Shannon divergence.
    /// Use it when you want a symmetric, bounded probability distance that is easier to interpret than raw KL-style values.
    /// </summary>
    public static double DistanceJensenShannon(ReadOnlySpan<float> p, ReadOnlySpan<float> q) =>
        Math.Sqrt(ProtectNonNegative(DivergenceJensenShannon(p, q)));

    /// <summary>
    /// Returns Hellinger distance between probability vectors.
    /// Use it when you want a stable metric on distributions and do not want the harsher behavior of KL divergence.
    /// </summary>
    public static double DistanceHellinger(ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        var (sumP, sumQ) = ValidateProbabilityPair(p, q);
        var sum = 0d;

        for (var i = 0; i < p.Length; i++)
        {
            var delta = Math.Sqrt(p[i] / sumP) - Math.Sqrt(q[i] / sumQ);
            sum += delta * delta;
        }

        return Math.Sqrt(sum / 2d);
    }

    /// <summary>
    /// Returns the Bhattacharyya coefficient, which measures how much two probability distributions overlap.
    /// Use it when you care about shared mass and class overlap more than about a raw distance scale.
    /// </summary>
    public static double Bhattacharyya(ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        var (sumP, sumQ) = ValidateProbabilityPair(p, q);
        var coefficient = 0d;

        for (var i = 0; i < p.Length; i++)
            coefficient += Math.Sqrt(p[i] / sumP * (q[i] / sumQ));

        return ClampUnitInterval(coefficient);
    }

    /// <summary>
    /// Returns Bhattacharyya distance, the logarithmic form of the Bhattacharyya coefficient.
    /// Use it when overlap is the right idea but you want a distance-like value that grows as overlap disappears.
    /// </summary>
    public static double DistanceBhattacharyya(ReadOnlySpan<float> p, ReadOnlySpan<float> q)
    {
        var coefficient = Bhattacharyya(p, q);

        return coefficient == 0d ? double.PositiveInfinity : -Math.Log(coefficient);
    }

    /// <summary>
    /// Returns Fisher-Rao distance in radians for categorical distributions.
    /// Use it when you want a geometry-aware probability distance instead of a purely heuristic divergence.
    /// </summary>
    public static double DistanceFisherRao(ReadOnlySpan<float> p, ReadOnlySpan<float> q) =>
        2d * Math.Acos(ClampUnitInterval(Bhattacharyya(p, q)));
}
