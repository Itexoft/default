// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace Itexoft.Threading.Tasks;

[AsyncMethodBuilder(typeof(TaskMethodBuilder))]
public sealed class HeapTask
{
    private readonly TaskCore? core;
    private readonly TaskData data;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Task task)
    {
        this.core = null;
        this.data = new TaskData(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(ValueTask task)
    {
        this.core = null;
        this.data = new TaskData(task, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Action factory)
    {
        this.core = null;
        this.data = new TaskData(factory, default);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask(Action<CancelToken> factory, CancelToken cancelToken)
    {
        this.core = null;
        this.data = new TaskData(factory, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HeapTask(TaskCore? core, in TaskData data)
    {
        this.core = core;
        this.data = data;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal HeapTask(TaskCore? core)
    {
        this.core = core;
        this.data = default;
    }

    internal HeapTask(ExceptionDispatchInfo exceptionDispatchInfo)
    {
        this.core = null;
        this.data = new TaskData(exceptionDispatchInfo);
    }

    public bool IsCompletedSuccessfully
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.core is not null)
                return this.core.IsCompletedSuccessfully;

            return !this.data.HasValue || this.data.IsCompletedSuccessfully;
        }
    }

    public bool IsCompleted
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (this.core is not null)
                return this.core.IsCompleted;

            return this.data.IsCompleted;
        }
    }

    public static HeapTask CompletedTask { get; } = new(null!, new TaskData());

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator HeapTask(in ValueTask task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator HeapTask(in Task task) => new(task);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator HeapTask(in StackTask task) => task.AsHeapTask();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal StackTask AsStackTask() => new(this.core, in this.data);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TaskAwaiter GetAwaiter() => new(this.core, in this.data);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static HeapTask FromException(Exception exception) => new(ExceptionDispatchInfo.Capture(exception));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public HeapTask WaitAsync(CancelToken cancelToken)
    {
        if (this.core is TaskCore core && !core.IsCompleted)
        {
            var data = new TaskData(this, cancelToken);
            return new(core, in data);
        }
        
        return new(this.core, in this.data);
    }
}
