// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Threading.Tasks;

namespace Itexoft.Core;

public delegate TValue GetFunc<TValue>(Func<TValue> func);

public delegate void SetAction<out TValue>(Action<TValue> action);

public delegate StackTask<TValue> GetValueTaskFunc<TValue>(Func<StackTask<TValue>> func);

public delegate StackTask SetValueTaskAction<out TValue>(Func<TValue, StackTask> action);

public delegate StackTask<TValue> GetTaskFunc<TValue>(Func<StackTask<TValue>> func);

public delegate StackTask SetTaskAction<out TValue>(Func<TValue, StackTask> action);

public delegate bool TryGetFunc<TValue>(Func<bool> func, out TValue value);

public delegate bool TrySetAction<out TValue>(Action<TValue> action);

public delegate StackTask<bool> TrySetValueTaskAction<out TValue>(Func<TValue, StackTask<bool>> action);

public delegate TValue GetFunc<TValue, TArg1>(Func<TArg1, TValue> func, TArg1 arg1);

public delegate void SetAction<out TValue, TArg1>(Action<TArg1, TValue> action, TArg1 arg1);

public delegate StackTask<TValue> GetValueTaskFunc<TValue, TArg1>(Func<TArg1, StackTask<TValue>> func, TArg1 arg1);

public delegate StackTask SetValueTaskAction<out TValue, TArg1>(Func<TArg1, TValue, StackTask> action, TArg1 arg1);

public delegate StackTask<TValue> GetTaskFunc<TValue, TArg1>(Func<TArg1, StackTask<TValue>> func, TArg1 arg1);

public delegate StackTask SetTaskAction<out TValue, TArg1>(Func<TArg1, TValue, StackTask> action, TArg1 arg1);

public delegate TValue GetFunc<TValue, TArg1, TArg2>(Func<TArg1, TArg2, TValue> func, TArg1 arg1, TArg2 arg2);

public delegate void SetAction<out TValue, TArg1, TArg2>(Action<TArg1, TArg2, TValue> action, TArg1 arg1, TArg2 arg2);

public delegate StackTask<TValue> GetValueTaskFunc<TValue, TArg1, TArg2>(Func<TArg1, TArg2, StackTask<TValue>> func, TArg1 arg1, TArg2 arg2);

public delegate StackTask SetValueTaskAction<out TValue, TArg1, TArg2>(Func<TArg1, TArg2, TValue, StackTask> action, TArg1 arg1, TArg2 arg2);

public delegate StackTask<TValue> GetTaskFunc<TValue, TArg1, TArg2>(Func<TArg1, TArg2, StackTask<TValue>> func, TArg1 arg1, TArg2 arg2);

public delegate StackTask SetTaskAction<out TValue, TArg1, TArg2>(Func<TArg1, TArg2, TValue, StackTask> action, TArg1 arg1, TArg2 arg2);
