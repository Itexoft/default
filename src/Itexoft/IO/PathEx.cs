// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

#if !NativeAOT
#endif

namespace Itexoft.IO;

public static class PathEx
{
    public static string NormalizeHomePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return rawPath;

        var expanded = rawPath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

        if (expanded.StartsWith("~", StringComparison.Ordinal))
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? Environment.CurrentDirectory;

            expanded = Path.Combine(home, expanded.TrimStart('~').TrimStart(Path.DirectorySeparatorChar));
        }

        expanded = Environment.ExpandEnvironmentVariables(expanded);

        return Path.GetFullPath(expanded);
    }

    public static string DenormalizeHomePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
            return rawPath;

        var fullPath = Path.GetFullPath(rawPath);

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrWhiteSpace(home))
            home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(home))
            return fullPath;

        var homeFull = Path.GetFullPath(home).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (fullPath.Equals(homeFull, comparison))
            return "~";

        var prefix = homeFull + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, comparison))
            return fullPath;

        var relative = fullPath[homeFull.Length..];

        if (relative.Length > 0 && (relative[0] == Path.DirectorySeparatorChar || relative[0] == Path.AltDirectorySeparatorChar))
            relative = relative[1..];

        return "~" + Path.DirectorySeparatorChar + relative;
    }
}
