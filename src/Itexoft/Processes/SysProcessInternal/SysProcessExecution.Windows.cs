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

internal sealed class WindowsSysProcessExecution : SysProcessExecution
{
    private const int startfUseStdHandles = 0x00000100;
    private const int startfUseShowWindow = 0x00000001;

    private const int createUnicodeEnvironment = 0x00000400;
    private const int createNoWindow = 0x08000000;

    private const int handleFlagInherit = 0x00000001;

    private const int processTerminate = 0x0001;
    private const int processQueryLimitedInformation = 0x1000;
    private const int synchronize = 0x00100000;

    private const uint stdInputHandle = 0xFFFFFFF6;
    private const uint stdOutputHandle = 0xFFFFFFF5;
    private const uint stdErrorHandle = 0xFFFFFFF4;

    private const uint waitObject0 = 0x00000000;
    private const uint waitTimeout = 0x00000102;
    private const uint waitFailed = 0xFFFFFFFF;
    private const uint stillActive = 259;
    private const uint invalidSessionId = 0xFFFFFFFF;

    private const uint th32CsSnapProcess = 0x00000002;

    private const int errorInvalidParameter = 87;
    private const int errorAccessDenied = 5;
    private const int errorNoMoreFiles = 18;
    private const int errorInsufficientBuffer = 122;

    private const int tokenAssignPrimary = 0x0001;
    private const int tokenDuplicate = 0x0002;
    private const int tokenQuery = 0x0008;
    private const int tokenAdjustPrivileges = 0x0020;
    private const int tokenAdjustDefault = 0x0080;
    private const int tokenAdjustSessionId = 0x0100;

    private static readonly nint invalidHandleValue = new(-1);

    private readonly SafeKernelHandle processHandle;

    private WindowsSysProcessExecution(int processId, SafeKernelHandle processHandle, CharStreamBw stdIn, CharStreamBr stdOut, CharStreamBr stdErr) :
        base(processId, stdIn, stdOut, stdErr) => this.processHandle = processHandle;

    public static SysProcessExecution StartCore(SysProcessStartRequest request)
    {
        ValidateWindowsRequest(request);

        if (request.RunAsUserName is { Length: > 0 } runAsUserName)
        {
            using var primaryToken = ResolvePrimaryTokenForUser(runAsUserName);

            return StartProcess(request, primaryToken);
        }

        return StartProcess(request);
    }

    private static SysProcessExecution StartProcess(SysProcessStartRequest request, SafeFileHandle? primaryToken = null)
    {
        var parentInput = nint.Zero;
        var childInput = nint.Zero;
        var parentOutput = nint.Zero;
        var childOutput = nint.Zero;
        var parentError = nint.Zero;
        var childError = nint.Zero;

        var environmentBlock = nint.Zero;
        var runAs = primaryToken is not null;

        try
        {
            var anyRedirect = request.RedirectStdIn || request.RedirectStdOut || request.RedirectStdError;

            if (request.RedirectStdIn)
                CreateRedirectPipe(true, out parentInput, out childInput);

            if (request.RedirectStdOut)
                CreateRedirectPipe(false, out parentOutput, out childOutput);

            if (request.RedirectStdError)
                CreateRedirectPipe(false, out parentError, out childError);

            var startupInfo = new StartupInfo
            {
                cb = Marshal.SizeOf<StartupInfo>(),
            };

            if (anyRedirect)
            {
                startupInfo.dwFlags |= startfUseStdHandles;
                startupInfo.hStdInput = request.RedirectStdIn ? childInput : GetStdHandle(stdInputHandle);
                startupInfo.hStdOutput = request.RedirectStdOut ? childOutput : GetStdHandle(stdOutputHandle);
                startupInfo.hStdError = request.RedirectStdError ? childError : GetStdHandle(stdErrorHandle);
            }

            startupInfo.dwFlags |= startfUseShowWindow;
            startupInfo.wShowWindow = 0;

            var creationFlags = createUnicodeEnvironment | createNoWindow;

            var commandLine = new StringBuilder(BuildCommandLine(request));
            environmentBlock = runAs ? BuildRunAsEnvironmentBlock(primaryToken!, request.Environment) : BuildEnvironmentBlock(request.Environment);

            var currentDirectory = string.IsNullOrWhiteSpace(request.WorkingDirectory) ? null : request.WorkingDirectory;

            var processInfo = runAs
                ? StartWindowsRunAsProcess(
                    primaryToken!,
                    request.ExecutablePath,
                    commandLine,
                    creationFlags,
                    environmentBlock,
                    currentDirectory,
                    anyRedirect,
                    ref startupInfo)
                : StartWindowsCurrentUserProcess(
                    request.ExecutablePath,
                    commandLine,
                    creationFlags,
                    environmentBlock,
                    currentDirectory,
                    anyRedirect,
                    ref startupInfo);

            CloseHandleIfValid(ref childInput);
            CloseHandleIfValid(ref childOutput);
            CloseHandleIfValid(ref childError);

            if (processInfo.hThread != nint.Zero && processInfo.hThread != invalidHandleValue)
                CloseHandle(processInfo.hThread);

            var processHandle = new SafeKernelHandle(processInfo.hProcess, true);

            var stdIn = request.RedirectStdIn ? new CharStreamBw(OpenWriteStream(ref parentInput).AsAstreamW(), Encoding.Default) : default;
            var stdOut = request.RedirectStdOut ? new CharStreamBr(OpenReadStream(ref parentOutput).AsAstreamR(), Encoding.Default) : default;
            var stdErr = request.RedirectStdError ? new CharStreamBr(OpenReadStream(ref parentError).AsAstreamR(), Encoding.Default) : default;

            return new WindowsSysProcessExecution((int)processInfo.dwProcessId, processHandle, stdIn, stdOut, stdErr);
        }
        catch
        {
            CloseHandleIfValid(ref parentInput);
            CloseHandleIfValid(ref childInput);
            CloseHandleIfValid(ref parentOutput);
            CloseHandleIfValid(ref childOutput);
            CloseHandleIfValid(ref parentError);
            CloseHandleIfValid(ref childError);

            throw;
        }
        finally
        {
            if (environmentBlock != nint.Zero)
                Marshal.FreeHGlobal(environmentBlock);
        }
    }

    public static void KillByIdCore(int processId) => TerminateProcessById(processId);

    protected override void PollExitCore()
    {
        if (this.IsExitStateFinal())
            return;

        var waitResult = WaitForSingleObject(this.processHandle.DangerousGetHandle(), 0);

        if (waitResult == waitTimeout)
            return;

        if (waitResult == waitFailed)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        if (waitResult != waitObject0)
            throw new InvalidOperationException($"Unexpected wait result: {waitResult}");

        if (!GetExitCodeProcess(this.processHandle.DangerousGetHandle(), out var exitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        this.MarkExited(unchecked((int)exitCode));
    }

    protected override void KillCore(bool tree)
    {
        if (!tree)
        {
            TerminateProcessById(this.Id);

            return;
        }

        var orderedPids = BuildProcessTreePostOrder(this.Id);

        foreach (var pid in orderedPids)
            TerminateProcessById(pid);
    }

    private static ProcessInformation StartWindowsRunAsProcess(
        SafeFileHandle token,
        string fileName,
        StringBuilder commandLine,
        int creationFlags,
        nint environment,
        string? currentDirectory,
        bool inheritHandles,
        ref StartupInfo startupInfo)
    {
        EnablePrivilege("SeAssignPrimaryTokenPrivilege");
        EnablePrivilege("SeIncreaseQuotaPrivilege");

        if (!CreateProcessAsUserW(
                token.DangerousGetHandle(),
                fileName,
                commandLine,
                nint.Zero,
                nint.Zero,
                inheritHandles,
                creationFlags,
                environment,
                currentDirectory,
                ref startupInfo,
                out var processInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return processInfo;
    }

    private static ProcessInformation StartWindowsCurrentUserProcess(
        string fileName,
        StringBuilder commandLine,
        int creationFlags,
        nint environment,
        string? currentDirectory,
        bool inheritHandles,
        ref StartupInfo startupInfo)
    {
        if (!CreateProcessW(
                fileName,
                commandLine,
                nint.Zero,
                nint.Zero,
                inheritHandles,
                creationFlags,
                environment,
                currentDirectory,
                ref startupInfo,
                out var processInfo))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return processInfo;
    }

    private static SafeFileHandle ResolvePrimaryTokenForUser(string runAsUser)
    {
        using var targetSid = ResolveUserSid(runAsUser);
        var sessionId = WTSGetActiveConsoleSessionId();

        if (sessionId == invalidSessionId)
            throw new InvalidOperationException("Active console session is unavailable.");

        EnablePrivilege("SeTcbPrivilege");

        if (!WTSQueryUserToken(sessionId, out var impersonationTokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        using var impersonationToken = new SafeFileHandle(impersonationTokenHandle, true);

        if (!TokenMatchesSid(impersonationToken, targetSid))
            throw new InvalidOperationException("Run-as user has no active console token.");

        if (!DuplicateTokenEx(
                impersonationToken.DangerousGetHandle(),
                tokenAssignPrimary | tokenDuplicate | tokenQuery | tokenAdjustDefault | tokenAdjustSessionId,
                nint.Zero,
                (int)SecurityImpersonationLevel.SecurityImpersonation,
                (int)TokenType.Primary,
                out var primaryTokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        return new SafeFileHandle(primaryTokenHandle, true);
    }

    private static bool TokenMatchesSid(SafeFileHandle token, SafeHGlobalHandle targetSid)
    {
        if (!GetTokenInformation(token.DangerousGetHandle(), TokenInformationClass.TokenUser, nint.Zero, 0, out var length))
        {
            var error = Marshal.GetLastWin32Error();

            if (error != errorInsufficientBuffer)
                throw new Win32Exception(error);
        }

        var buffer = Marshal.AllocHGlobal(length);

        try
        {
            if (!GetTokenInformation(token.DangerousGetHandle(), TokenInformationClass.TokenUser, buffer, length, out _))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var user = Marshal.PtrToStructure<TokenUser>(buffer);

            return EqualSid(user.User.Sid, targetSid.DangerousGetHandle());
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static SafeHGlobalHandle ResolveUserSid(string runAsUser)
    {
        uint sidSize = 0;
        uint domainSize = 0;
        var use = SidNameUse.User;

        LookupAccountName(null, runAsUser, nint.Zero, ref sidSize, null, ref domainSize, ref use);

        var error = Marshal.GetLastWin32Error();

        if (error != errorInsufficientBuffer)
            throw new Win32Exception(error);

        var sid = Marshal.AllocHGlobal((int)sidSize);
        var domain = new StringBuilder((int)domainSize);

        if (!LookupAccountName(null, runAsUser, sid, ref sidSize, domain, ref domainSize, ref use))
        {
            var lookupError = Marshal.GetLastWin32Error();
            Marshal.FreeHGlobal(sid);

            throw new Win32Exception(lookupError);
        }

        return new SafeHGlobalHandle(sid);
    }

    private static void EnablePrivilege(string privilege)
    {
        if (!OpenProcessToken(GetCurrentProcess(), tokenAdjustPrivileges | tokenQuery, out var tokenHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        using var token = new SafeFileHandle(tokenHandle, true);

        if (!LookupPrivilegeValue(null, privilege, out var luid))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var state = new TokenPrivileges
        {
            PrivilegeCount = 1,
            Privileges = new LuidAndAttributes
            {
                Luid = luid,
                Attributes = 0x00000002,
            },
        };

        if (!AdjustTokenPrivileges(token.DangerousGetHandle(), false, ref state, 0, nint.Zero, nint.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var error = Marshal.GetLastWin32Error();

        if (error != 0)
            throw new Win32Exception(error);
    }

    private static void ValidateWindowsRequest(SysProcessStartRequest request)
    {
        if (request.UseShellExecute)
            throw new NotSupportedException("UseShellExecute is not implemented for SysProcessInternal Windows backend.");
    }

    private static string BuildCommandLine(SysProcessStartRequest request)
    {
        var commandLine = new StringBuilder();
        commandLine.Append(QuoteWindowsArgument(request.ExecutablePath));

        if (request.Arguments is null)
            return commandLine.ToString();

        foreach (var argument in request.Arguments)
        {
            commandLine.Append(' ');
            commandLine.Append(QuoteWindowsArgument(argument));
        }

        return commandLine.ToString();
    }

    private static string QuoteWindowsArgument(string argument)
    {
        if (argument.Length == 0)
            return "\"\"";

        var needsQuotes = false;

        foreach (var ch in argument)
        {
            if (char.IsWhiteSpace(ch) || ch == '"')
            {
                needsQuotes = true;

                break;
            }
        }

        if (!needsQuotes)
            return argument;

        var builder = new StringBuilder();
        builder.Append('"');

        var slashCount = 0;

        foreach (var ch in argument)
        {
            if (ch == '\\')
            {
                slashCount++;

                continue;
            }

            if (ch == '"')
            {
                builder.Append('\\', slashCount * 2 + 1);
                builder.Append('"');
                slashCount = 0;

                continue;
            }

            if (slashCount > 0)
            {
                builder.Append('\\', slashCount);
                slashCount = 0;
            }

            builder.Append(ch);
        }

        if (slashCount > 0)
            builder.Append('\\', slashCount * 2);

        builder.Append('"');

        return builder.ToString();
    }

    private static nint BuildEnvironmentBlock(IReadOnlyDictionary<string, string?>? overrides) =>
        BuildEnvironmentBlock(MergeEnvironment(overrides, StringComparer.OrdinalIgnoreCase));

    private static nint BuildRunAsEnvironmentBlock(SafeFileHandle token, IReadOnlyDictionary<string, string?>? overrides)
    {
        if (!CreateEnvironmentBlock(out var environmentBlock, token.DangerousGetHandle(), false))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        try
        {
            var env = ParseEnvironmentBlock(environmentBlock);
            ApplyEnvironmentOverrides(env, overrides);

            return BuildEnvironmentBlock(env);
        }
        finally
        {
            DestroyEnvironmentBlock(environmentBlock);
        }
    }

    private static nint BuildEnvironmentBlock(Dictionary<string, string> env)
    {
        var keys = new string[env.Count];

        env.Keys.CopyTo(keys, 0);
        Array.Sort(keys, StringComparer.OrdinalIgnoreCase);

        var builder = new StringBuilder();

        foreach (var key in keys)
            builder.Append(key).Append('=').Append(env[key]).Append('\0');

        builder.Append('\0');

        return Marshal.StringToHGlobalUni(builder.ToString());
    }

    private static Dictionary<string, string> ParseEnvironmentBlock(nint environmentBlock)
    {
        var env = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var offset = 0;

        while (true)
        {
            var entry = Marshal.PtrToStringUni(environmentBlock + offset);

            if (string.IsNullOrEmpty(entry))
                return env;

            var separatorIndex = entry.IndexOf('=', 1);

            if (separatorIndex < 0)
                throw new InvalidOperationException("Environment block contains an invalid entry.");

            env[entry[..separatorIndex]] = entry[(separatorIndex + 1)..];
            offset += (entry.Length + 1) * sizeof(char);
        }
    }

    private static void CreateRedirectPipe(bool parentWrites, out nint parentHandle, out nint childHandle)
    {
        var security = new SecurityAttributes
        {
            nLength = Marshal.SizeOf<SecurityAttributes>(),
            bInheritHandle = true,
            lpSecurityDescriptor = nint.Zero,
        };

        if (!CreatePipe(out var readHandle, out var writeHandle, ref security, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        parentHandle = parentWrites ? writeHandle : readHandle;
        childHandle = parentWrites ? readHandle : writeHandle;

        if (!SetHandleInformation(parentHandle, handleFlagInherit, 0))
        {
            CloseHandle(parentHandle);
            CloseHandle(childHandle);

            throw new Win32Exception(Marshal.GetLastWin32Error());
        }
    }

    private static List<int> BuildProcessTreePostOrder(int rootPid)
    {
        var snapshot = CreateToolhelp32Snapshot(th32CsSnapProcess, 0);

        if (snapshot == nint.Zero || snapshot == invalidHandleValue)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var childrenByParent = new Dictionary<int, List<int>>();

        try
        {
            var entry = new ProcessEntry32
            {
                dwSize = (uint)Marshal.SizeOf<ProcessEntry32>(),
            };

            if (Process32FirstW(snapshot, ref entry))
            {
                do
                {
                    var parentId = unchecked((int)entry.th32ParentProcessID);
                    var processId = unchecked((int)entry.th32ProcessID);

                    if (!childrenByParent.TryGetValue(parentId, out var children))
                    {
                        children = [];
                        childrenByParent[parentId] = children;
                    }

                    children.Add(processId);
                    entry.dwSize = (uint)Marshal.SizeOf<ProcessEntry32>();
                }
                while (Process32NextW(snapshot, ref entry));
            }
            else
            {
                var error = Marshal.GetLastWin32Error();

                if (error != errorInvalidParameter && error != errorNoMoreFiles)
                    throw new Win32Exception(error);
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var result = new List<int>();
        var visited = new HashSet<int>();

        AppendPostOrder(rootPid, childrenByParent, visited, result);

        return result;
    }

    private static void AppendPostOrder(int processId, Dictionary<int, List<int>> childrenByParent, HashSet<int> visited, List<int> result)
    {
        if (!visited.Add(processId))
            return;

        if (childrenByParent.TryGetValue(processId, out var children))
        {
            foreach (var child in children)
                AppendPostOrder(child, childrenByParent, visited, result);
        }

        result.Add(processId);
    }

    private static void TerminateProcessById(int processId)
    {
        var handle = OpenProcess(processTerminate | processQueryLimitedInformation | synchronize, false, (uint)processId);

        if (handle == nint.Zero || handle == invalidHandleValue)
        {
            var openError = Marshal.GetLastWin32Error();

            if (openError == errorInvalidParameter)
                return;

            throw new Win32Exception(openError);
        }

        try
        {
            if (TerminateProcess(handle, unchecked((uint)-1)))
                return;

            var terminateError = Marshal.GetLastWin32Error();

            if (terminateError == errorAccessDenied)
            {
                if (GetExitCodeProcess(handle, out var code) && code != stillActive)
                    return;
            }

            throw new Win32Exception(terminateError);
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    private static FileStream OpenReadStream(ref nint handle)
    {
        var current = handle;

        if (current == nint.Zero || current == invalidHandleValue)
            throw new InvalidOperationException("Handle is closed.");

        handle = nint.Zero;

        return new FileStream(new SafeFileHandle(current, true), FileAccess.Read, 4096, false);
    }

    private static FileStream OpenWriteStream(ref nint handle)
    {
        var current = handle;

        if (current == nint.Zero || current == invalidHandleValue)
            throw new InvalidOperationException("Handle is closed.");

        handle = nint.Zero;

        return new FileStream(new SafeFileHandle(current, true), FileAccess.Write, 4096, false);
    }

    private static void CloseHandleIfValid(ref nint handle)
    {
        if (handle == nint.Zero || handle == invalidHandleValue)
            return;

        CloseHandle(handle);
        handle = nint.Zero;
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        nint processAttributes,
        nint threadAttributes,
        bool inheritHandles,
        int creationFlags,
        nint environment,
        string? currentDirectory,
        ref StartupInfo startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcessAsUserW(
        nint hToken,
        string lpApplicationName,
        StringBuilder lpCommandLine,
        nint lpProcessAttributes,
        nint lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        nint lpEnvironment,
        string? lpCurrentDirectory,
        ref StartupInfo lpStartupInfo,
        out ProcessInformation lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out nint readPipe, out nint writePipe, ref SecurityAttributes pipeAttributes, int size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(nint handle, int mask, int flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint GetStdHandle(uint stdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint handle, uint milliseconds);

    [DllImport("kernel32.dll")]
    private static extern uint WTSGetActiveConsoleSessionId();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(nint processHandle, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint OpenProcess(int desiredAccess, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(nint processHandle, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(nint snapshot, ref ProcessEntry32 entry);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(nint processHandle, int desiredAccess, out nint tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool DuplicateTokenEx(
        nint existingTokenHandle,
        int desiredAccess,
        nint tokenAttributes,
        int impersonationLevel,
        int tokenType,
        out nint newTokenHandle);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupAccountName(
        string? systemName,
        string accountName,
        nint sid,
        ref uint sidSize,
        StringBuilder? referencedDomainName,
        ref uint referencedDomainNameSize,
        ref SidNameUse use);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool EqualSid(nint pSid1, nint pSid2);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(
        nint tokenHandle,
        TokenInformationClass tokenInformationClass,
        nint tokenInformation,
        int tokenInformationLength,
        out int returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? lpSystemName, string lpName, out Luid lpLuid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(
        nint tokenHandle,
        bool disableAllPrivileges,
        ref TokenPrivileges newState,
        int bufferLength,
        nint previousState,
        nint returnLength);

    [DllImport("wtsapi32.dll", SetLastError = true)]
    private static extern bool WTSQueryUserToken(uint sessionId, out nint tokenHandle);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool CreateEnvironmentBlock(out nint environment, nint token, bool inherit);

    [DllImport("userenv.dll", SetLastError = true)]
    private static extern bool DestroyEnvironmentBlock(nint environment);

    private sealed class SafeHGlobalHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeHGlobalHandle(nint handle) : base(true) => this.SetHandle(handle);

        protected override bool ReleaseHandle()
        {
            Marshal.FreeHGlobal(this.handle);

            return true;
        }
    }

    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle(nint handle, bool ownsHandle) : base(ownsHandle) => this.SetHandle(handle);

        protected override bool ReleaseHandle() => CloseHandle(this.handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SecurityAttributes
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public bool bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint PrivilegeCount;
        public LuidAndAttributes Privileges;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SidAndAttributes
    {
        public IntPtr Sid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenUser
    {
        public SidAndAttributes User;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public UIntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    private enum TokenInformationClass
    {
        TokenUser = 1,
    }

    private enum SidNameUse
    {
        User = 1,
    }

    private enum TokenType
    {
        Primary = 1,
    }

    private enum SecurityImpersonationLevel
    {
        SecurityImpersonation = 2,
    }
}
