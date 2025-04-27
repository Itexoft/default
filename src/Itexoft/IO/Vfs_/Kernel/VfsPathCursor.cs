// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Vfs;

internal ref struct VfsPathCursor(ReadOnlySpan<char> path)
{
    private readonly ReadOnlySpan<char> path = path;
    private int nextIndex;
    private int segmentStart;
    private int segmentLength;

    public ReadOnlySpan<char> Segment => this.path.Slice(this.segmentStart, this.segmentLength);

    public int SegmentStart => this.segmentStart;

    public int SegmentEnd => this.segmentStart + this.segmentLength;

    public bool MoveNext()
    {
        while (true)
        {
            while (this.nextIndex < this.path.Length && IsSeparator(this.path[this.nextIndex]))
                this.nextIndex++;

            if (this.nextIndex >= this.path.Length)
            {
                this.segmentStart = 0;
                this.segmentLength = 0;

                return false;
            }

            var start = this.nextIndex;

            while (this.nextIndex < this.path.Length && !IsSeparator(this.path[this.nextIndex]))
                this.nextIndex++;

            var end = this.nextIndex;

            while (start < end && char.IsWhiteSpace(this.path[start]))
                start++;

            while (end > start && char.IsWhiteSpace(this.path[end - 1]))
                end--;

            if (start == end)
                continue;

            this.segmentStart = start;
            this.segmentLength = end - start;

            return true;
        }
    }

    private static bool IsSeparator(char value) => value is '/' or '\\';
}
