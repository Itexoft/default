// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Formats.Dkon.Internal;

[Flags]
internal enum DkonSyntaxFeature
{
    None = 0,
    Multiline = 1 << 0,

    // Scalars
    Scalar = 1 << 1,
    ScalarMultiline = 1 << 2,

    // Assignments
    Assignment = 1 << 3,
    AssignmentChain = 1 << 4,
    AssignmentInArray = 1 << 5,

    // Arrays
    Array = 1 << 6,
    ArrayNested = 1 << 7,
    ArrayNamed = 1 << 8,

    // Graph links
    ReferenceGraph = 1 << 9,
}

public enum DkonSyntaxLevel
{
    None = 0,
    Scalars = 1,
    Assignments = 2,
    Arrays = 3,
    AssignmentsChains = 4,
    ArrayAssignments = 5,
    ArrayNamed = 6,
    References = 7,
}
