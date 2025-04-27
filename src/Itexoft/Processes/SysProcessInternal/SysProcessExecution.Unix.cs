// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Itexoft.IO;
using Itexoft.IO.Streams.Chars;
using Microsoft.Win32.SafeHandles;

namespace Itexoft.Processes.SysProcessInternal;

internal sealed class UnixSysProcessExecution : SysProcessExecution
{
    private const int stageSetProcessGroup = 1;
    private const int stageDupStdIn = 2;
    private const int stageDupStdOut = 3;
    private const int stageDupStdErr = 4;
    private const int stageWorkingDirectory = 5;
    private const int stageExec = 6;
    private const int stageSetGroups = 7;
    private const int stageSetGid = 8;
    private const int stageSetUid = 9;

    private const int stdInFd = 0;
    private const int stdOutFd = 1;
    private const int stdErrFd = 2;

    private const int wNoHang = 1;

    private const int sigKill = 9;
    private const int fdCloExec = 1;
    private const int fSetFd = 2;

    private const int ePerm = 1;
    private const int eSrch = 3;
    private const int eChild = 10;
    private const int scNGroupsMax = 4;
    private const short posixSpawnSetPGroup = 0x0002;

    private static readonly Lock forkStartSync = new();
    private readonly int processGroupId;
    private int startupErrorFd;

    private UnixSysProcessExecution(
        int processId,
        int processGroupId,
        CharStreamBw stdIn,
        CharStreamBr stdOut,
        CharStreamBr stdErr,
        int startupErrorFd = -1) : base(processId, stdIn, stdOut, stdErr)
    {
        this.processGroupId = processGroupId;
        this.startupErrorFd = startupErrorFd;
    }

    public static SysProcessExecution StartCore(SysProcessStartRequest request)
    {
        ValidateUnixRequest(request);
        var runAsUserName = request.RunAsUserName;
        var runAsEnabled = !string.IsNullOrWhiteSpace(runAsUserName);

        if (!runAsEnabled)
        {
            if (OperatingSystem.IsMacOS())
                return StartSpawnCore(request);

            lock (forkStartSync)
                return StartForkCore(request, null, false);
        }

        if (OperatingSystem.IsMacOS())
        {
            if (!OperatingSystem.IsMacOSVersionAtLeast(10, 15))
                throw new NotSupportedException("run_as on macOS requires macOS 10.15 or newer.");

            return StartSpawnCore(request, runAsUserName);
        }

        lock (forkStartSync)
            return StartForkCore(request, runAsUserName, true);
    }

    private static SysProcessExecution StartForkCore(SysProcessStartRequest request, string? runAsUserName, bool runAsEnabled)
    {
        var runAsUser = runAsEnabled ? ResolvePosixUser(runAsUserName!) : default;

        var executable = ResolveExecutablePath(request.ExecutablePath, request.WorkingDirectory);
        var args = BuildArgumentValues(request);
        var argv = BuildArgv(request.ExecutablePath, args);

        var env = runAsEnabled
            ? BuildRunAsEnvironment(request.Environment, runAsUserName!, runAsUser)
            : MergeEnvironment(request.Environment, StringComparer.Ordinal);

        var envp = BuildEnvp(env);

        var stdinPipe = request.RedirectStdIn ? CreatePipe() : PosixPipePair.Empty;
        var stdoutPipe = request.RedirectStdOut ? CreatePipe() : PosixPipePair.Empty;
        var stderrPipe = request.RedirectStdError ? CreatePipe() : PosixPipePair.Empty;
        var errorPipe = CreatePipe();

        try
        {
            SetCloseOnExec(errorPipe.Write);

            using var nativeExecutable = new NativeUtf8String(executable);
            using var nativeArgv = new NativeUtf8StringArray(argv);
            using var nativeEnvp = new NativeUtf8StringArray(envp);

            using var nativeWorkingDirectory = !string.IsNullOrWhiteSpace(request.WorkingDirectory)
                ? new NativeUtf8String(request.WorkingDirectory!)
                : null;

            // Resolve directory-service state before fork; the child path must stay in direct syscalls.
            using var nativeRunAsGroups = runAsEnabled ? new NativeUInt32Array(runAsUser.SupplementaryGroups) : null;

            var pid = fork();

            if (pid == -1)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            if (pid == 0)
            {
                RunChild(
                    nativeExecutable.Pointer,
                    nativeArgv.Pointer,
                    nativeEnvp.Pointer,
                    nativeWorkingDirectory?.Pointer ?? nint.Zero,
                    !string.IsNullOrWhiteSpace(request.WorkingDirectory),
                    request.RedirectStdIn,
                    request.RedirectStdOut,
                    request.RedirectStdError,
                    runAsEnabled,
                    nativeRunAsGroups?.Pointer ?? nint.Zero,
                    nativeRunAsGroups?.Count ?? 0,
                    runAsUser.Uid,
                    runAsUser.Gid,
                    stdinPipe,
                    stdoutPipe,
                    stderrPipe,
                    errorPipe);
            }

            CloseFd(ref errorPipe.Write);

            CloseUnusedParent(ref stdinPipe.Read, ref stdinPipe.Write, request.RedirectStdIn, true);
            CloseUnusedParent(ref stdoutPipe.Read, ref stdoutPipe.Write, request.RedirectStdOut, false);
            CloseUnusedParent(ref stderrPipe.Read, ref stderrPipe.Write, request.RedirectStdError, false);

            var stdIn = request.RedirectStdIn ? new CharStreamBw(OpenWriteStream(ref stdinPipe.Write).AsAstreamRw(), Encoding.Default) : default;
            var stdOut = request.RedirectStdOut ? new CharStreamBr(OpenReadStream(ref stdoutPipe.Read).AsAstreamR(), Encoding.Default) : default;
            var stdErr = request.RedirectStdError ? new CharStreamBr(OpenReadStream(ref stderrPipe.Read).AsAstreamR(), Encoding.Default) : default;

            return new UnixSysProcessExecution(pid, pid, stdIn, stdOut, stdErr, errorPipe.Read);
        }
        catch
        {
            ClosePipe(ref stdinPipe);
            ClosePipe(ref stdoutPipe);
            ClosePipe(ref stderrPipe);
            ClosePipe(ref errorPipe);

            throw;
        }
    }

    private static SysProcessExecution StartSpawnCore(SysProcessStartRequest request, string? runAsUserName = null)
    {
        var runAsEnabled = !string.IsNullOrWhiteSpace(runAsUserName);
        var runAsUser = runAsEnabled ? ResolvePosixUser(runAsUserName!) : default;
        var executable = ResolveExecutablePath(request.ExecutablePath, request.WorkingDirectory);
        var args = BuildArgumentValues(request);
        var argv = BuildArgv(request.ExecutablePath, args);

        var env = runAsEnabled
            ? BuildRunAsEnvironment(request.Environment, runAsUserName!, runAsUser)
            : MergeEnvironment(request.Environment, StringComparer.Ordinal);

        var envp = BuildEnvp(env);

        var stdinPipe = request.RedirectStdIn ? CreatePipe() : PosixPipePair.Empty;
        var stdoutPipe = request.RedirectStdOut ? CreatePipe() : PosixPipePair.Empty;
        var stderrPipe = request.RedirectStdError ? CreatePipe() : PosixPipePair.Empty;
        var fileActions = nint.Zero;
        var attributes = nint.Zero;
        var hasFileActions = false;
        var hasAttributes = false;

        try
        {
            CheckSpawnResult(posix_spawn_file_actions_init(out fileActions));
            hasFileActions = true;

            if (!string.IsNullOrWhiteSpace(request.WorkingDirectory))
                CheckSpawnResult(posix_spawn_file_actions_addchdir_np(ref fileActions, request.WorkingDirectory!));

            if (request.RedirectStdIn)
                AddSpawnPipeActions(ref fileActions, stdinPipe, stdInFd, true);

            if (request.RedirectStdOut)
                AddSpawnPipeActions(ref fileActions, stdoutPipe, stdOutFd, false);

            if (request.RedirectStdError)
                AddSpawnPipeActions(ref fileActions, stderrPipe, stdErrFd, false);

            CheckSpawnResult(posix_spawnattr_init(out attributes));
            hasAttributes = true;
            CheckSpawnResult(posix_spawnattr_setflags(ref attributes, posixSpawnSetPGroup));
            CheckSpawnResult(posix_spawnattr_setpgroup(ref attributes, 0));

            if (runAsEnabled)
            {
                CheckSpawnResult(posix_spawnattr_set_uid_np(ref attributes, runAsUser.Uid));
                CheckSpawnResult(posix_spawnattr_set_gid_np(ref attributes, runAsUser.Gid));

                CheckSpawnResult(
                    posix_spawnattr_set_groups_np(
                        ref attributes,
                        runAsUser.SupplementaryGroups.Length,
                        runAsUser.SupplementaryGroups,
                        runAsUser.Uid));
            }

            using var nativeExecutable = new NativeUtf8String(executable);
            using var nativeArgv = new NativeUtf8StringArray(argv);
            using var nativeEnvp = new NativeUtf8StringArray(envp);

            var spawnError = posix_spawn(
                out var pid,
                nativeExecutable.Pointer,
                ref fileActions,
                ref attributes,
                nativeArgv.Pointer,
                nativeEnvp.Pointer);

            if (spawnError != 0)
                throw new Win32Exception(spawnError);

            CloseUnusedParent(ref stdinPipe.Read, ref stdinPipe.Write, request.RedirectStdIn, true);
            CloseUnusedParent(ref stdoutPipe.Read, ref stdoutPipe.Write, request.RedirectStdOut, false);
            CloseUnusedParent(ref stderrPipe.Read, ref stderrPipe.Write, request.RedirectStdError, false);

            var stdIn = request.RedirectStdIn ? new CharStreamBw(OpenWriteStream(ref stdinPipe.Write).AsAstreamRw(), Encoding.Default) : default;
            var stdOut = request.RedirectStdOut ? new CharStreamBr(OpenReadStream(ref stdoutPipe.Read).AsAstreamR(), Encoding.Default) : default;
            var stdErr = request.RedirectStdError ? new CharStreamBr(OpenReadStream(ref stderrPipe.Read).AsAstreamR(), Encoding.Default) : default;

            return new UnixSysProcessExecution(pid, pid, stdIn, stdOut, stdErr);
        }
        catch
        {
            ClosePipe(ref stdinPipe);
            ClosePipe(ref stdoutPipe);
            ClosePipe(ref stderrPipe);

            throw;
        }
        finally
        {
            if (hasAttributes)
                posix_spawnattr_destroy(ref attributes);

            if (hasFileActions)
                posix_spawn_file_actions_destroy(ref fileActions);
        }
    }

    public static void KillByIdCore(int processId)
    {
        if (kill(processId, sigKill) == -1)
        {
            var error = Marshal.GetLastWin32Error();

            if (error == eSrch)
                return;

            throw new Win32Exception(error);
        }
    }

    protected override void PollExitCore()
    {
        if (this.IsExitStateFinal())
            return;

        var waitResult = waitpid(this.Id, out var status, wNoHang);

        if (waitResult == this.Id)
        {
            this.MarkExited(ParseWaitStatus(status));
            this.CloseStartupErrorFd();

            return;
        }

        if (waitResult == 0)
            return;

        if (waitResult == -1)
        {
            var error = Marshal.GetLastWin32Error();

            if (error == eChild)
            {
                if (!IsProcessAlive(this.Id))
                {
                    this.MarkExited(null);
                    this.CloseStartupErrorFd();
                }

                return;
            }

            throw new Win32Exception(error);
        }
    }

    protected override void KillCore(bool tree)
    {
        if (tree)
        {
            if (this.processGroupId <= 0)
                throw new NotSupportedException("Tree kill requires a dedicated process group.");

            if (kill(-this.processGroupId, sigKill) == -1)
            {
                var error = Marshal.GetLastWin32Error();

                if (error == eSrch)
                {
                    this.MarkExited(null);
                    this.CloseStartupErrorFd();

                    return;
                }

                throw new Win32Exception(error);
            }

            return;
        }

        if (kill(this.Id, sigKill) == -1)
        {
            var error = Marshal.GetLastWin32Error();

            if (error == eSrch)
            {
                this.MarkExited(null);
                this.CloseStartupErrorFd();

                return;
            }

            throw new Win32Exception(error);
        }
    }

    private static void ValidateUnixRequest(SysProcessStartRequest request)
    {
        if (request.UseShellExecute)
            throw new NotSupportedException("UseShellExecute is not implemented for SysProcessInternal Unix backend.");
    }

    private static string[] BuildArgv(string executablePath, IReadOnlyList<string> args)
    {
        var argv = new string[args.Count + 1];
        argv[0] = executablePath;

        for (var i = 0; i < args.Count; i++)
            argv[i + 1] = args[i];

        return argv;
    }

    private static string[] BuildEnvp(Dictionary<string, string> environment)
    {
        var envp = new string[environment.Count];
        var i = 0;

        foreach (var (key, value) in environment)
        {
            envp[i] = $"{key}={value}";
            i++;
        }

        return envp;
    }

    private static Dictionary<string, string> BuildRunAsEnvironment(
        IReadOnlyDictionary<string, string?>? overrides,
        string runAsUserName,
        PosixUser runAsUser)
    {
        var env = MergeEnvironment(null, StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(runAsUser.Home))
            env["HOME"] = runAsUser.Home!;

        env["USER"] = runAsUserName;
        env["LOGNAME"] = runAsUserName;

        if (!string.IsNullOrWhiteSpace(runAsUser.Shell))
            env["SHELL"] = runAsUser.Shell!;

        ApplyEnvironmentOverrides(env, overrides);

        return env;
    }

    private static PosixUser ResolvePosixUser(string userName)
    {
        var pwdPtr = getpwnam(userName);

        if (pwdPtr == nint.Zero)
            throw new InvalidOperationException("Run-as user was not found.");

        uint uid;
        uint gid;
        string? home;
        string? shell;

        if (OperatingSystem.IsMacOS() || OperatingSystem.IsFreeBSD())
        {
            var pwd = Marshal.PtrToStructure<BsdPasswd>(pwdPtr);
            uid = pwd.pw_uid;
            gid = pwd.pw_gid;
            home = Marshal.PtrToStringAnsi(pwd.pw_dir);
            shell = Marshal.PtrToStringAnsi(pwd.pw_shell);
        }
        else
        {
            var pwd = Marshal.PtrToStructure<LinuxPasswd>(pwdPtr);
            uid = pwd.pw_uid;
            gid = pwd.pw_gid;
            home = Marshal.PtrToStringAnsi(pwd.pw_dir);
            shell = Marshal.PtrToStringAnsi(pwd.pw_shell);
        }

        var groups = ResolveSupplementaryGroups(userName, gid);

        return new PosixUser(uid, gid, home, shell, groups);
    }

    private static uint[] ResolveSupplementaryGroups(string userName, uint baseGid)
    {
        var capacity = ResolveProcessGroupLimit();
        var rawGroups = new int[capacity];
        var count = capacity;
        var result = getgrouplist(userName, unchecked((int)baseGid), rawGroups, ref count);

        if (result != 0)
            count = capacity;

        if (count < 1 || count > capacity)
            throw new InvalidOperationException("getgrouplist returned an invalid group count.");

        var groups = new uint[count];

        for (var i = 0; i < count; i++)
        {
            if (rawGroups[i] < 0)
                throw new InvalidOperationException("getgrouplist returned a negative group id.");

            groups[i] = unchecked((uint)rawGroups[i]);
        }

        return groups;
    }

    private static int ResolveProcessGroupLimit()
    {
        var limit = sysconf(scNGroupsMax);

        if (limit < 1 || limit > int.MaxValue)
            throw new InvalidOperationException("sysconf(_SC_NGROUPS_MAX) returned an invalid value.");

        return (int)limit;
    }

    private static string ResolveExecutablePath(string fileName, string? workingDirectory)
    {
        if (Path.IsPathRooted(fileName))
            return fileName;

        if (fileName.Contains('/'))
        {
            var baseDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? Directory.GetCurrentDirectory() : workingDirectory;

            return Path.GetFullPath(Path.Combine(baseDirectory, fileName));
        }

        var probeDirectories = new List<string>(4);

        if (!string.IsNullOrWhiteSpace(workingDirectory))
            probeDirectories.Add(workingDirectory);

        var processPath = Environment.ProcessPath;

        if (!string.IsNullOrEmpty(processPath))
        {
            var processDirectory = Path.GetDirectoryName(processPath);

            if (!string.IsNullOrWhiteSpace(processDirectory))
                probeDirectories.Add(processDirectory);
        }

        probeDirectories.Add(Directory.GetCurrentDirectory());

        foreach (var directory in probeDirectories)
        {
            var candidate = Path.Combine(directory, fileName);

            if (IsExecutableFile(candidate))
                return candidate;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;

        foreach (var pathSegment in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var candidate = Path.Combine(pathSegment, fileName);

            if (IsExecutableFile(candidate))
                return candidate;
        }

        return fileName;
    }

    private static bool IsExecutableFile(string path)
    {
        if (!File.Exists(path) || Directory.Exists(path))
            return false;

        return access(path, 1) == 0;
    }

    private static int? ParseWaitStatus(int status)
    {
        if ((status & 0x7F) == 0)
            return (status >> 8) & 0xFF;

        if ((status & 0x7F) != 0x7F)
            return 128 + (status & 0x7F);

        return null;
    }

    private static void AddSpawnPipeActions(ref nint fileActions, PosixPipePair pipe, int childFd, bool childReads)
    {
        var childEnd = childReads ? pipe.Read : pipe.Write;
        var otherEnd = childReads ? pipe.Write : pipe.Read;

        CheckSpawnResult(posix_spawn_file_actions_addclose(ref fileActions, otherEnd));
        CheckSpawnResult(posix_spawn_file_actions_adddup2(ref fileActions, childEnd, childFd));
        CheckSpawnResult(posix_spawn_file_actions_addclose(ref fileActions, childEnd));
    }

    private static void CheckSpawnResult(int result)
    {
        if (result != 0)
            throw new Win32Exception(result);
    }

    private static void RunChild(
        nint executablePath,
        nint argv,
        nint envp,
        nint workingDirectory,
        bool hasWorkingDirectory,
        bool redirectStdIn,
        bool redirectStdOut,
        bool redirectStdErr,
        bool runAsEnabled,
        nint runAsGroups,
        int runAsGroupCount,
        uint runAsUid,
        uint runAsGid,
        PosixPipePair stdinPipe,
        PosixPipePair stdoutPipe,
        PosixPipePair stderrPipe,
        PosixPipePair errorPipe)
    {
        CloseFd(ref errorPipe.Read);

        if (setpgid(0, 0) == -1)
            ExitWithError(errorPipe.Write, stageSetProcessGroup);

        if (redirectStdIn)
        {
            CloseFd(ref stdinPipe.Write);

            if (dup2(stdinPipe.Read, stdInFd) == -1)
                ExitWithError(errorPipe.Write, stageDupStdIn);

            CloseFd(ref stdinPipe.Read);
        }

        if (redirectStdOut)
        {
            CloseFd(ref stdoutPipe.Read);

            if (dup2(stdoutPipe.Write, stdOutFd) == -1)
                ExitWithError(errorPipe.Write, stageDupStdOut);

            CloseFd(ref stdoutPipe.Write);
        }

        if (redirectStdErr)
        {
            CloseFd(ref stderrPipe.Read);

            if (dup2(stderrPipe.Write, stdErrFd) == -1)
                ExitWithError(errorPipe.Write, stageDupStdErr);

            CloseFd(ref stderrPipe.Write);
        }

        if (runAsEnabled)
        {
            if (setgroups(runAsGroupCount, runAsGroups) == -1)
                ExitWithError(errorPipe.Write, stageSetGroups);

            if (setgid(runAsGid) == -1)
                ExitWithError(errorPipe.Write, stageSetGid);

            if (setuid(runAsUid) == -1)
                ExitWithError(errorPipe.Write, stageSetUid);
        }

        if (hasWorkingDirectory && chdir(workingDirectory) == -1)
            ExitWithError(errorPipe.Write, stageWorkingDirectory);

        execve(executablePath, argv, envp);

        ExitWithError(errorPipe.Write, stageExec);
    }

    private static void ExitWithError(int errorPipe, int stage)
    {
        var error = Marshal.GetLastWin32Error();
        WriteExecError(errorPipe, error, stage);
        _exit(127);
    }

    private static void WriteExecError(int fd, int error, int stage)
    {
        var firstValue = error;
        var secondValue = stage;
        var firstWrite = write(fd, ref firstValue, sizeof(int));

        if (firstWrite == sizeof(int))
            write(fd, ref secondValue, sizeof(int));
    }

    private static bool IsProcessAlive(int processId)
    {
        if (kill(processId, 0) == 0)
            return true;

        var error = Marshal.GetLastWin32Error();

        return error switch
        {
            ePerm => true,
            eSrch => false,
            _ => throw new Win32Exception(error),
        };
    }

    private static void SetCloseOnExec(int fd)
    {
        if (fcntl(fd, fSetFd, fdCloExec) == -1)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    private static PosixPipePair CreatePipe()
    {
        var fds = new int[2];

        if (pipe(fds) == -1)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            SetCloseOnExec(fds[0]);
            SetCloseOnExec(fds[1]);
        }
        catch
        {
            close(fds[0]);
            close(fds[1]);

            throw;
        }

        return new PosixPipePair(fds[0], fds[1]);
    }

    private static void CloseUnusedParent(ref int read, ref int write, bool redirected, bool parentKeepsWrite)
    {
        if (!redirected)
        {
            CloseFd(ref read);
            CloseFd(ref write);

            return;
        }

        if (parentKeepsWrite)
            CloseFd(ref read);
        else
            CloseFd(ref write);
    }

    private static void ClosePipe(ref PosixPipePair pipe)
    {
        CloseFd(ref pipe.Read);
        CloseFd(ref pipe.Write);
    }

    private static void CloseFd(ref int fd)
    {
        if (fd < 0)
            return;

        close(fd);
        fd = -1;
    }

    private void CloseStartupErrorFd()
    {
        if (this.startupErrorFd < 0)
            return;

        close(this.startupErrorFd);
        this.startupErrorFd = -1;
    }

    private static FileStream OpenReadStream(ref int fd)
    {
        var current = fd;

        if (current < 0)
            throw new InvalidOperationException("File descriptor is closed.");

        fd = -1;

        return new FileStream(new SafeFileHandle((nint)current, true), FileAccess.Read, 4096, false);
    }

    private static FileStream OpenWriteStream(ref int fd)
    {
        var current = fd;

        if (current < 0)
            throw new InvalidOperationException("File descriptor is closed.");

        fd = -1;

        return new FileStream(new SafeFileHandle((nint)current, true), FileAccess.Write, 4096, false);
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int fork();

    [DllImport("libc", SetLastError = true)]
    private static extern int pipe(int[] fds);

    [DllImport("libc", SetLastError = true)]
    private static extern int dup2(int oldfd, int newfd);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern int fcntl(int fd, int cmd, int flags);

    [DllImport("libc", SetLastError = true)]
    private static extern int chdir(nint path);

    [DllImport("libc", SetLastError = true)]
    private static extern int setuid(uint uid);

    [DllImport("libc", SetLastError = true)]
    private static extern int setgid(uint gid);

    [DllImport("libc", SetLastError = true)]
    private static extern int setgroups(int count, nint groups);

    [DllImport("libc", SetLastError = true)]
    private static extern nint getpwnam(string name);

    [DllImport("libc", SetLastError = true)]
    private static extern int getgrouplist(string name, int group, [Out] int[] groups, ref int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int setpgid(int pid, int pgid);

    [DllImport("libc", SetLastError = true)]
    private static extern int execve(nint file, nint argv, nint envp);

    [DllImport("libc", SetLastError = true)]
    private static extern int _exit(int status);

    [DllImport("libc", SetLastError = true)]
    private static extern int write(int fd, ref int value, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int read(int fd, out int value, int count);

    [DllImport("libc", SetLastError = true)]
    private static extern int waitpid(int pid, out int status, int options);

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);

    [DllImport("libc", SetLastError = true)]
    private static extern int access(string path, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern long sysconf(int name);

    [DllImport("libc")]
    private static extern int posix_spawn(out int pid, nint path, ref nint fileActions, ref nint attributes, nint argv, nint envp);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_init(out nint fileActions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_destroy(ref nint fileActions);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_addclose(ref nint fileActions, int fd);

    [DllImport("libc")]
    private static extern int posix_spawn_file_actions_adddup2(ref nint fileActions, int fd, int newFd);

    [DllImport("libc", EntryPoint = "posix_spawn_file_actions_addchdir_np")]
    private static extern int posix_spawn_file_actions_addchdir_np(ref nint fileActions, string path);

    [DllImport("libc")]
    private static extern int posix_spawnattr_init(out nint attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_destroy(ref nint attributes);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setflags(ref nint attributes, short flags);

    [DllImport("libc")]
    private static extern int posix_spawnattr_setpgroup(ref nint attributes, int pgroup);

    [DllImport("libc", EntryPoint = "posix_spawnattr_set_uid_np")]
    private static extern int posix_spawnattr_set_uid_np(ref nint attributes, uint uid);

    [DllImport("libc", EntryPoint = "posix_spawnattr_set_gid_np")]
    private static extern int posix_spawnattr_set_gid_np(ref nint attributes, uint gid);

    [DllImport("libc", EntryPoint = "posix_spawnattr_set_groups_np")]
    private static extern int posix_spawnattr_set_groups_np(ref nint attributes, int count, uint[] groups, uint uid);

    private struct PosixPipePair(int read, int write)
    {
        public static PosixPipePair Empty => new(-1, -1);

        public int Read = read;
        public int Write = write;
    }

    private readonly struct PosixUser(uint uid, uint gid, string? home, string? shell, uint[] supplementaryGroups)
    {
        public uint Uid { get; } = uid;
        public uint Gid { get; } = gid;
        public string? Home { get; } = home;
        public string? Shell { get; } = shell;
        public uint[] SupplementaryGroups { get; } = supplementaryGroups;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LinuxPasswd
    {
        public IntPtr pw_name;
        public IntPtr pw_passwd;
        public uint pw_uid;
        public uint pw_gid;
        public IntPtr pw_gecos;
        public IntPtr pw_dir;
        public IntPtr pw_shell;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BsdPasswd
    {
        public IntPtr pw_name;
        public IntPtr pw_passwd;
        public uint pw_uid;
        public uint pw_gid;
        public long pw_change;
        public IntPtr pw_class;
        public IntPtr pw_gecos;
        public IntPtr pw_dir;
        public IntPtr pw_shell;
        public long pw_expire;
    }

    private sealed class NativeUtf8String : IDisposable
    {
        public NativeUtf8String(string value)
        {
            var bytes = Encoding.UTF8.GetBytes(value);
            this.Pointer = Marshal.AllocHGlobal(bytes.Length + 1);
            Marshal.Copy(bytes, 0, this.Pointer, bytes.Length);
            Marshal.WriteByte(this.Pointer, bytes.Length, 0);
        }

        public nint Pointer { get; }

        public void Dispose()
        {
            if (this.Pointer != nint.Zero)
                Marshal.FreeHGlobal(this.Pointer);
        }
    }

    private sealed class NativeUtf8StringArray : IDisposable
    {
        private readonly nint[] elementPointers;

        public NativeUtf8StringArray(string[] values)
        {
            this.elementPointers = new nint[values.Length];

            for (var i = 0; i < values.Length; i++)
            {
                var bytes = Encoding.UTF8.GetBytes(values[i]);
                var ptr = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
                Marshal.WriteByte(ptr, bytes.Length, 0);
                this.elementPointers[i] = ptr;
            }

            this.Pointer = Marshal.AllocHGlobal((values.Length + 1) * nint.Size);

            for (var i = 0; i < values.Length; i++)
                Marshal.WriteIntPtr(this.Pointer, i * nint.Size, this.elementPointers[i]);

            Marshal.WriteIntPtr(this.Pointer, values.Length * nint.Size, nint.Zero);
        }

        public nint Pointer { get; }

        public void Dispose()
        {
            foreach (var pointer in this.elementPointers)
                Marshal.FreeHGlobal(pointer);

            Marshal.FreeHGlobal(this.Pointer);
        }
    }

    private sealed class NativeUInt32Array : IDisposable
    {
        public NativeUInt32Array(uint[] values)
        {
            this.Count = values.Length;

            if (values.Length == 0)
                return;

            this.Pointer = Marshal.AllocHGlobal(values.Length * sizeof(uint));

            for (var i = 0; i < values.Length; i++)
                Marshal.WriteInt32(this.Pointer, i * sizeof(uint), unchecked((int)values[i]));
        }

        public int Count { get; }
        public nint Pointer { get; }

        public void Dispose()
        {
            if (this.Pointer != nint.Zero)
                Marshal.FreeHGlobal(this.Pointer);
        }
    }
}
