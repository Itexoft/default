// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Core;
using Itexoft.Extensions;
using Itexoft.Threading.Atomics;
using Itexoft.Threading.SysTimerInternal;

namespace Itexoft.Threading.Tasks;

public readonly struct PromiseAwaiter : ICriticalNotifyCompletion, IEquatable<PromiseAwaiter>
{
    private readonly object? state = null;

    public PromiseAwaiter() : this((object?)null) { }
    private PromiseAwaiter(object? state) => this.state = state;

    internal static PromiseAwaiter Uncompleted() => new(new State(false));
    internal static PromiseAwaiter Completed() => new();
    internal static PromiseAwaiter CompletedException(Exception exception) => new(exception.Required());
    internal static PromiseAwaiter UncompletedAction(Action action, bool promise) => new(new State(action.Required(), promise));
    internal static PromiseAwaiter UncompletedTimer(int milliseconds) => new(new State(milliseconds));

    internal static PromiseAwaiter FromState(object? state) => new(state);

    internal static object? ToState(in PromiseAwaiter awaiter) => awaiter.state;

    public bool IsCompletedSuccessfully => this.IsCompleted && !this.IsFaulted;

    public bool IsCompleted
    {
        get
        {
            if (this.state is null)
                return true;

            if (this.state is Exception)
                return true;

            return ((State)this.state!).IsCompleted;
        }
    }

    internal bool IsNull => this.state is null;
    public bool IsTimerFinished => this.state is State state && state.IsTimerFinished;
    
    public bool IsFaulted => this.Exception is not null;
    public Exception? Exception => this.state as Exception ?? (this.state as State)?.AsException();

    public void WaitResult(CancelToken cancelToken)
    {
        if (this.state is State state)
            state.WaitResult(cancelToken);
    }

    public void GetResult()
    {
        this.Exception?.Rethrow();
        this.WaitResult(default);
        this.Exception?.Rethrow();
    }

    public void OnCompleted(Action continuation)
    {
        if (this.state is State state)
            state.OnCompleted(continuation);
        else
            continuation();
    }

    public void UnsafeOnCompleted(Action continuation) => this.OnCompleted(continuation);

    internal bool Complete()
    {
        if (this.state is State state)
            return state.TryComplete();

        return false;
    }
    
    internal Action? CompleteAction()
    {
        if (this.state is State state)
            return state.Complete;
        return null;
    }

    internal bool Complete(Exception exception)
    {
        if (this.state is State state)
            return state.TryComplete(exception);

        return false;
    }

    public bool Equals(PromiseAwaiter other) => this.IsNull && other.IsNull || ReferenceEquals(this.state, other.state);

    public override bool Equals(object? obj) => obj is PromiseAwaiter other && this.Equals(other);

    public override int GetHashCode() => (this.state != null ? this.state.GetHashCode() : 0);

    public static bool operator ==(in PromiseAwaiter left, in PromiseAwaiter right) => left.Equals(right);

    public static bool operator !=(in PromiseAwaiter left, in PromiseAwaiter right) => !left.Equals(right);
}

public readonly struct PromiseAwaiter<TResult> : ICriticalNotifyCompletion
{
    private readonly TResult result = default!;
    private readonly PromiseAwaiter awaiter;

    internal static PromiseAwaiter<TResult> Uncompleted(in TResult result) => new(PromiseAwaiter.FromState(new State<TResult>(in result, true)));
    internal static PromiseAwaiter<TResult> Uncompleted() => new(PromiseAwaiter.FromState(new State<TResult>(default(TResult)!, true)));
    internal static PromiseAwaiter<TResult> Completed(in TResult result = default!) => new(PromiseAwaiter.Completed(), in result);
    internal static PromiseAwaiter<TResult> CompletedException(Exception exception) => new(PromiseAwaiter.FromState(exception.Required()));
    internal static PromiseAwaiter<TResult> FromState(in PromiseAwaiter awaiter, in TResult result) => new(in awaiter, in result);
    internal static PromiseAwaiter<TResult> UncompletedFunc(Func<TResult> func, bool promise) => new(PromiseAwaiter.FromState(new State<TResult>(func.Required(), promise)));

    private PromiseAwaiter(in PromiseAwaiter awaiter, in TResult result = default!)
    {
        this.awaiter = awaiter;
        this.result = result;
    }

    public void OnCompleted(Action continuation) => this.awaiter.OnCompleted(continuation);
    public void UnsafeOnCompleted(Action continuation) => this.awaiter.OnCompleted(continuation);

    public bool IsCompletedSuccessfully => this.awaiter.IsCompletedSuccessfully;
    public bool IsCompleted => this.awaiter.IsCompleted;
    public bool IsFaulted => this.awaiter.IsFaulted;
    public Exception? Exception => this.awaiter.Exception;

    public void WaitResult(CancelToken cancelToken)
    {
        if (PromiseAwaiter.ToState(this.awaiter) is State state)
            state.WaitResult(cancelToken);
    }

    public TResult GetResult()
    {
        this.Exception?.Rethrow();

        if (PromiseAwaiter.ToState(this.awaiter) is State<TResult> state)
        {
            this.WaitResult(default);
            this.Exception?.Rethrow();

            return state.GetResult();
        }

        return this.result;
    }

    internal bool Complete(in TResult result)
    {
        if (PromiseAwaiter.ToState(in this.awaiter) is State<TResult> state)
            return state.TryCompleteValue(in result);

        return this.awaiter.Complete();
    }

    internal void Complete(Exception exception) => this.awaiter.Complete(exception);

    public static implicit operator PromiseAwaiter(in PromiseAwaiter<TResult> awaiter) => awaiter.awaiter;
    public static implicit operator PromiseAwaiter<TResult>(in PromiseAwaiter awaiter) => new(in awaiter);
    
    internal Action? CompleteAction()
    {
        if (PromiseAwaiter.ToState(in this.awaiter) is State state)
            return state.Complete;
        return null;
    }

    internal Action<Exception>? CompleteExceptionAction()
    {
        if (PromiseAwaiter.ToState(in this.awaiter) is State state)
            return state.Complete;
        return null;
    }

    
    internal InAction<TResult>? CompleteInAction()
    {
        if (PromiseAwaiter.ToState(in this.awaiter) is State<TResult> state)
            return state.CompleteValue;
        return null;
    }

}

file class State
{
    private readonly bool promise;
    private AtomicLane32 astate = new();
    private protected object? state;

    public State(bool completed)
    {
        if (completed)
            this.astate.TrySetFlag(Astate.Invoked | Astate.Completed);
        else
            this.promise = true;
    }

    public State(int milliseconds)
    {
        this.promise = true;
        var timer = SysTimer.New(milliseconds.RequiredPositive(), false, this.Complete);
        this.state = timer;
        timer.Start();
    }
    
    public State(Delegate completion, bool promise)
    {
        this.promise = promise;
        this.state = completion.Required();
    }

    public bool IsCompleted => this.astate.HasFlags(Astate.Completed);
    
    public bool IsTimerFinished => this.state is SysTimer timer && timer.IsFinished;
    
    private event Action continuations = null!;

    public Exception? AsException() => this.state as Exception;

    public void Complete() => this.TryComplete();
    public void Complete(Exception exception) => this.TryComplete(exception.Required());

    public bool TryComplete(Exception? exception = null)
    {
        if (!this.TrySetInvoked())
            return false;

        if (exception is null)
        {
            try
            {
                this.Invoke();
                return this.TrySetCompleted();
            }
            catch (Exception ex)
            {
                exception = ex;
            }
        }

        this.state = exception;

        return this.TrySetCompleted();
    }

    public void WaitResult(CancelToken cancelToken)
    {
        if (this.promise || !this.TryComplete())
        {
            for(var i = 0; !cancelToken.IsRequested && !this.astate.HasFlags(Astate.Completed);)
            {
                Spin.Wait(ref i);
            }
        }
    }

    public void OnCompleted(Action continuation)
    {
        if (this.promise)
        {
            this.continuations += continuation;
        }
        else
        {
            this.TryComplete();
            continuation();
        }
    }

    private protected virtual bool Invoke()
    {
        if (this.state is Action action)
        {
            action();

            return true;
        }

        if (this.state is SysTimer)
        {
            return true;
        }

        return false;
    }

    private protected bool TrySetCompleted()
    {
        if (!this.astate.TrySetFlag(Astate.Completed))
            return false;

        if (Atomic.NullOut(ref this.continuations!, out var continuation))
            continuation();

        return true;
    }

    private protected bool TrySetInvoked() => this.astate.TrySetFlag(Astate.Invoked);

    [Flags]
    private protected enum Astate
    {
        Invoked = 1,
        Completed = 2,
    }
}

file sealed class State<TResult> : State
{
    private TResult value = default!;
    public State(in TResult result, bool promise) : base(!promise) => this.value = result;
    public State(Func<TResult> func, bool promise) : base(func, promise) { }

    public ref TResult GetResult() => ref this.value;

    public void CompleteValue(in TResult value) => this.TryCompleteValue(in value);
    
    public bool TryCompleteValue(in TResult value)
    {
        if (!this.TrySetInvoked())
            return false;

        this.value = value;

        return this.TrySetCompleted();
    }

    private protected override bool Invoke()
    {
        if (base.Invoke())
            return true;

        if (this.state is Func<TResult> func)
        {
            this.value = func.Invoke();

            return true;
        }

        return false;
    }
}
