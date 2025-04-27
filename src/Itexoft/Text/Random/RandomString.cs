// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Text.Random;

public static class RandomString
{
    private const string english = "abcdefghijklmnopqrstuvwxyz";
    private const uint seedOffset = 0x9E3779B9u;
    private const uint zeroSeed = 0xA341316Cu;

    public static ReadOnlySpan<char> English => english;

    public static string Create(int seed, int length, ReadOnlySpan<char> chars)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length));

        if (length == 0)
            return string.Empty;

        return CreateCore(seed, length, chars.IsEmpty ? english : chars.ToString());
    }

    public static string Create(int seed, int length) => Create(seed, length, default);

    public static string Create(int length, ReadOnlySpan<char> chars) => Create(System.Random.Shared.Next(), length, chars);

    public static string Create(int length) => Create(System.Random.Shared.Next(), length);

    private static string CreateCore(int seed, int length, string chars)
    {
        var state = unchecked((uint)seed + seedOffset);

        if (state == 0)
            state = zeroSeed;

        return string.Create(
            length,
            (State: state, Chars: chars),
            static (span, state) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    state.State = Next(state.State);
                    span[i] = state.Chars[(int)(state.State % (uint)state.Chars.Length)];
                }
            });
    }

    private static uint Next(uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;

        return state;
    }
}
