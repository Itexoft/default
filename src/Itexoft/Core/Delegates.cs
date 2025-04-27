// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

namespace Itexoft.Core;

public delegate void InAction<T>(in T arg);

public delegate void RefAction<T>(ref T arg);

public delegate ref T RefFunc<T>() where T : allows ref struct;

public delegate ref T RefFunc<T1, T>(in T1 t1) where T : allows ref struct where T1 : allows ref struct;
