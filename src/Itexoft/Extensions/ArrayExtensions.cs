// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Core;

public static class ArrayExtensions
{
    extension<T>(T[][] source)
    {
        public T[,] To2DArray()
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (source.Length == 0)
                return new T[0, 0];

            var rows = source.Length;
            var cols = source[0]?.Length ?? throw new ArgumentException("Row 0 is null.", nameof(source));

            for (var i = 0; i < rows; i++)
            {
                if (source[i] == null)
                    throw new ArgumentException($"Row {i} is null.", nameof(source));

                if (source[i].Length != cols)
                    throw new ArgumentException("Jagged array must be rectangular.", nameof(source));
            }

            var result = new T[rows, cols];

            for (var i = 0; i < rows; i++)
            for (var j = 0; j < cols; j++)
                result[i, j] = source[i][j];

            return result;
        }
    }
}
