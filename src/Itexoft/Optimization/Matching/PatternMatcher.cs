// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.IO;

namespace Itexoft.Optimization.Matching;

public static class PatternMatcher
{
    public static IStreamRs<T> Match<T>(IStreamRs<T> stream, Span<T> pattern) where T : unmanaged => throw new NotImplementedException();
}
