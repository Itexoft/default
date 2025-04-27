// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.IO.Vfs;

public sealed partial class VirtualFileSystem
{
    private static string NormalizePath(string path, bool allowRoot = false)
    {
        ArgumentNullException.ThrowIfNull(path);

        var cursor = new VfsPathCursor(path);
        var segmentCount = 0;
        var totalLength = 0;

        while (cursor.MoveNext())
        {
            var segment = cursor.Segment;

            if (IsRelativeSegment(segment))
                throw new ArgumentException("Relative path segments are not allowed.", nameof(path));

            totalLength += segment.Length;

            if (segmentCount != 0)
                totalLength++;

            segmentCount++;
        }

        if (segmentCount == 0)
        {
            if (allowRoot)
                return string.Empty;

            throw new ArgumentException("Path cannot be empty.", nameof(path));
        }

        return string.Create(totalLength, path, static (destination, source) =>
        {
            var cursor = new VfsPathCursor(source);
            var written = 0;

            while (cursor.MoveNext())
            {
                if (written != 0)
                    destination[written++] = '/';

                cursor.Segment.CopyTo(destination[written..]);
                written += cursor.Segment.Length;
            }
        });
    }

    private static string GetParentPath(string path)
    {
        var index = path.LastIndexOf('/');

        return index < 0 ? string.Empty : path[..index];
    }

    private static bool IsRelativeSegment(ReadOnlySpan<char> segment)
        => segment.Length == 1 && segment[0] == '.'
            || segment.Length == 2 && segment[0] == '.' && segment[1] == '.';
}
