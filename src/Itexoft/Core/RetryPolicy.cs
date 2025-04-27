// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Runtime.CompilerServices;
using Itexoft.Extensions;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;
using ExceptionHandler = System.Func<System.Exception, ulong, Itexoft.Threading.Tasks.StackTask>;

namespace Itexoft.Core;

file static class RetryPolicyCore
{
    internal static async StackTask Retry(
        Func<CancelToken, StackTask> operation,
        ExceptionHandler? onError,
        ulong maxAttempts,
        CancelToken cancelToken)
    {
        for (ulong attempt = 0;; attempt++)
        {
            cancelToken.ThrowIf();

            try
            {
                await operation(cancelToken);

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (maxAttempts > 0 && attempt + 1 >= maxAttempts)
                    throw;

                if (onError is not null)
                    await onError(ex, attempt);
            }
        }
    }

    internal static async StackTask<T> Retry<T>(
        Func<CancelToken, StackTask<T>> operation,
        ExceptionHandler? onError,
        ulong maxAttempts,
        CancelToken cancelToken)
    {
        for (ulong attempt = 0;; attempt++)
        {
            cancelToken.ThrowIf();

            try
            {
                return await operation(cancelToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                if (maxAttempts > 0 && attempt + 1 >= maxAttempts)
                    throw;

                if (onError is not null)
                    await onError(ex, attempt);
            }
        }
    }

    internal static async StackTask Ignore(Func<CancelToken, StackTask> operation, CancelToken cancelToken)
    {
        while (true)
        {
            cancelToken.ThrowIf();

            try
            {
                await operation(cancelToken);

                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // swallow and retry
            }
        }
    }

    internal static async StackTask<T> Ignore<T>(Func<CancelToken, StackTask<T>> operation, CancelToken cancelToken)
    {
        while (true)
        {
            cancelToken.ThrowIf();

            try
            {
                return await operation(cancelToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                // swallow and retry
            }
        }
    }
}

public readonly struct RetryPolicy
{
    private readonly ulong attempts;
    private readonly ExceptionHandler? onError;
    private readonly TimeSpan timeout;

    public RetryPolicy() : this(null, TimeSpan.Zero, 0) { }
    public RetryPolicy(ExceptionHandler? onError, TimeSpan timeout = default) : this(onError, timeout, 0) { }

    private RetryPolicy(ExceptionHandler? onError, TimeSpan timeout, ulong attempts)
    {
        this.onError = onError;
        this.timeout = timeout;
        this.attempts = attempts;
    }

    private static readonly ExceptionHandler ignoreDelegate = static (_, _) => default;
    private bool IsIgnore => ReferenceEquals(this.onError, ignoreDelegate);

    public static RetryPolicy None { get; } = new();

    public static RetryPolicy Ignore { get; } = new(ignoreDelegate);

    public static RetryPolicy Limit(TimeSpan timeout) => new(static (_, _) => default, timeout);

    public static RetryPolicy Limit(ulong attempts) => new(null, TimeSpan.Zero, attempts);

    public static RetryPolicy Limit(ulong attempts, TimeSpan timeout) => new(null, timeout, attempts);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RetryPolicy WithTimeout(TimeSpan timeout) => new(this.onError, timeout, this.attempts);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask Run(Func<CancelToken, StackTask> operation, CancelToken cancelToken = default)
    {
        operation.Required();

        return this.RunCore(operation, cancelToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask<T> Run<T>(Func<CancelToken, StackTask<T>> operation, CancelToken cancelToken = default)
    {
        operation.Required();

        return this.RunCore(operation, cancelToken);
    }

    public static implicit operator RetryPolicy(ExceptionHandler? onError) => new(onError);
    

    private StackTask RunCore(Func<CancelToken, StackTask> operation, CancelToken cancelToken) =>
        this.Dispatch(operation, this.PrepareToken(cancelToken));

    private StackTask<T> RunCore<T>(Func<CancelToken, StackTask<T>> operation, CancelToken cancelToken) =>
        this.Dispatch(operation, this.PrepareToken(cancelToken));

    private CancelToken PrepareToken(CancelToken cancelToken)
    {
        if (this.timeout.IsZero)
            return cancelToken;

        cancelToken.ThrowIf();

        return cancelToken.Branch(this.timeout);
    }

    private StackTask Dispatch(Func<CancelToken, StackTask> operation, CancelToken cancelToken)
    {
        if (this.IsIgnore)
            return RetryPolicyCore.Ignore(operation, cancelToken);

        if (this.onError is null && this.attempts == 0)
            return operation(cancelToken);

        return RetryPolicyCore.Retry(operation, this.onError, this.attempts, cancelToken);
    }

    private StackTask<T> Dispatch<T>(Func<CancelToken, StackTask<T>> operation, CancelToken cancelToken)
    {
        if (this.IsIgnore)
            return RetryPolicyCore.Ignore(operation, cancelToken);

        if (this.onError is null && this.attempts == 0)
            return operation(cancelToken);

        return RetryPolicyCore.Retry(operation, this.onError, this.attempts, cancelToken);
    }
}

public readonly struct RetryPolicy<T>
{
    public static RetryPolicy<T> None { get; } = new(RetryPolicy.None);

    public static RetryPolicy<T> Ignore { get; } = new(RetryPolicy.Ignore);

    public static RetryPolicy<T> Limit(TimeSpan timeout) => new(RetryPolicy.Limit(timeout));

    public static RetryPolicy<T> Limit(ulong attempts) => new(RetryPolicy.Limit(attempts));

    public static RetryPolicy<T> Limit(ulong attempts, TimeSpan timeout) => new(RetryPolicy.Limit(attempts, timeout));

    private readonly RetryPolicy policy;

    public RetryPolicy(ExceptionHandler? onError) : this(new RetryPolicy(onError)) { }

    private RetryPolicy(RetryPolicy policy) => this.policy = policy;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public StackTask<T> Run(Func<CancelToken, StackTask<T>> operation, CancelToken cancelToken = default) =>
        this.policy.Run(operation, cancelToken);
    
    public static implicit operator RetryPolicy<T>(ExceptionHandler? onError) => new(onError);
}
