// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using Itexoft.Extensions;
using Itexoft.IO.Streams.Chars;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Processes.SysProcessInternal;

internal sealed class SysProcessStartRequest
{
    public required string ExecutablePath { get; init; }
    public IReadOnlyList<string>? Arguments { get; init; }

    public string? WorkingDirectory { get; init; }
    public IReadOnlyDictionary<string, string?>? Environment { get; init; }
    public string? RunAsUserName { get; init; }

    public bool UseShellExecute { get; init; }
    public bool RedirectStdIn { get; init; }
    public bool RedirectStdOut { get; init; }
    public bool RedirectStdError { get; init; }
}

internal abstract class SysProcessExecution
{
    private readonly Lock stateSync = new();
    private int exitCode;
    private bool hasExitCode;

    private bool hasExited;

    protected SysProcessExecution(int processId, CharStreamBw stdIn, CharStreamBr stdOut, CharStreamBr stdErr)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        this.Id = processId;
        this.StdIn = stdIn;
        this.StdOut = stdOut;
        this.StdErr = stdErr;
    }

    public int Id { get; }

    public CharStreamBw StdIn { get; }
    public CharStreamBr StdOut { get; }
    public CharStreamBr StdErr { get; }

    public bool HasExited
    {
        get
        {
            this.PollExitCore();

            lock (this.stateSync)
                return this.hasExited;
        }
    }

    public async Promise<int> WaitAsync(CancelToken cancelToken = default)
    {
        while (true)
        {
            this.PollExitCore();

            lock (this.stateSync)
            {
                if (this.hasExited)
                {
                    if (!this.hasExitCode)
                        throw new NotSupportedException("Exit code is unavailable for this process instance.");

                    return this.exitCode;
                }
            }

            await Promise.Delay(16, cancelToken);
        }
    }

    public void Kill(bool tree = false) => this.KillCore(tree);

    protected abstract void PollExitCore();

    protected abstract void KillCore(bool tree);

    protected bool IsExitStateFinal()
    {
        lock (this.stateSync)
            return this.hasExited;
    }

    protected void MarkExited(int? code)
    {
        lock (this.stateSync)
        {
            if (this.hasExited)
                return;

            this.hasExited = true;

            if (code is int value)
            {
                this.hasExitCode = true;
                this.exitCode = value;
            }
        }
    }

    public static SysProcessExecution Start(SysProcessStartRequest request)
    {
        request.Required();
        ValidateRequest(request);

        if (OperatingSystem.IsWindows())
            return WindowsSysProcessExecution.StartCore(request);

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
            return UnixSysProcessExecution.StartCore(request);

        throw new PlatformNotSupportedException("Current platform is not supported by SysProcessInternal.");
    }

    public static void KillById(int processId)
    {
        if (processId <= 0)
            throw new ArgumentOutOfRangeException(nameof(processId));

        if (OperatingSystem.IsWindows())
        {
            WindowsSysProcessExecution.KillByIdCore(processId);

            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            UnixSysProcessExecution.KillByIdCore(processId);

            return;
        }

        throw new PlatformNotSupportedException("Current platform is not supported by SysProcessInternal.");
    }

    protected static void ValidateRequest(SysProcessStartRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ExecutablePath))
            throw new InvalidOperationException("FileNameMissing");

        if (request.RunAsUserName is not null && string.IsNullOrWhiteSpace(request.RunAsUserName))
            throw new InvalidOperationException("RunAsUserNameEmpty");

        if (request.Arguments is { Count: > 0 })
        {
            foreach (var argument in request.Arguments)
            {
                if (argument is null)
                    throw new ArgumentNullException(nameof(request), "ArgumentListMayNotContainNull");
            }
        }

        if (request.UseShellExecute && (request.RedirectStdIn || request.RedirectStdOut || request.RedirectStdError))
            throw new InvalidOperationException("CantRedirectStreams");
    }

    protected static List<string> BuildArgumentValues(SysProcessStartRequest request)
    {
        var values = new List<string>();

        if (request.Arguments is not null)
        {
            foreach (var argument in request.Arguments)
                values.Add(argument!);
        }

        return values;
    }

    protected static Dictionary<string, string> MergeEnvironment(IReadOnlyDictionary<string, string?>? overrides, StringComparer comparer)
    {
        var env = new Dictionary<string, string>(comparer);

        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = entry.Key?.ToString() ?? string.Empty;

            if (key.Length == 0)
                continue;

            env[key] = entry.Value?.ToString() ?? string.Empty;
        }

        if (overrides is null)
            return env;

        ApplyEnvironmentOverrides(env, overrides);

        return env;
    }

    protected static void ApplyEnvironmentOverrides(Dictionary<string, string> env, IReadOnlyDictionary<string, string?>? overrides)
    {
        if (overrides is null)
            return;

        foreach (var (key, value) in overrides)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Environment key cannot be null or whitespace.", nameof(overrides));

            if (value is null)
                env.Remove(key);
            else
                env[key] = value;
        }
    }
}
