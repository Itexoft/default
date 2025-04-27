// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Vectors;

/// <summary>
/// Represents the local per-step cost used by sequence comparison methods such as dynamic time warping.
/// Use it when your sequence elements are vectors and you want to decide what “one step is far from another” means.
/// </summary>
public delegate double VectorStepCost(ReadOnlySpan<float> x, ReadOnlySpan<float> y);
