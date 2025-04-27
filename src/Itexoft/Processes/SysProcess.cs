// Copyright (c) 2011-2026 Denis Kudelin
// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Collections;
using System.Text;
using Itexoft.Extensions;
using Itexoft.IO.Streams.Chars;
using Itexoft.Processes.SysProcessInternal;
using Itexoft.Threading;
using Itexoft.Threading.Tasks;

namespace Itexoft.Processes;

public sealed class SysProcess
{
    private readonly SysProcessExecution execution;

    private SysProcess(SysProcessOptions options) => this.execution = SysProcessExecution.Start(MapStartRequest(options));

    public int Id => this.execution.Id;

    public bool HasExited => this.execution.HasExited;

    public CharStreamBw StdIn => this.execution.StdIn;
    public CharStreamBr StdOut => this.execution.StdOut;
    public CharStreamBr StdErr => this.execution.StdErr;

    public Promise<int> WaitAsync(CancelToken cancelToken = default) => this.execution.WaitAsync(cancelToken);

    public void Kill(bool tree = false) => this.execution.Kill(tree);

    public static void KillById(int id) => SysProcessExecution.KillById(id.RequiredPositive());

    public static SysProcess Start(SysProcessOptions options) => new(options.Required());
    public static SysProcess Start(string path) => new(new SysProcessOptions(path.RequiredNotWhiteSpace()));

    private static SysProcessStartRequest MapStartRequest(SysProcessOptions options) => new()
    {
        ExecutablePath = options.Path,
        Arguments = options.Arguments,
        WorkingDirectory = options.WorkingDirectory,
        Environment = options.Environment,
        RunAsUserName = options.User?.Name,
        RedirectStdIn = options.RedirectStdIn,
        RedirectStdOut = options.RedirectStdOut,
        RedirectStdError = options.RedirectStdError,
    };
}

public sealed class SysProcessOptions(string path)
{
    public bool RedirectStdError = false;
    public bool RedirectStdIn = false;
    public bool RedirectStdOut = false;

    public SysProcessArguments? Arguments { get; set; }

    public string? WorkingDirectory { get; set; }
    public IReadOnlyDictionary<string, string?>? Environment { get; set; }

    public SysProcessUser? User { get; set; }

    public string Path { get; } = path;
}

public sealed record SysProcessUser(string? Name)
{
    public static implicit operator SysProcessUser(string? name) => new(name);
}

public sealed class SysProcessArguments : IReadOnlyList<string>
{
    private const char quote = '"';
    private const char slash = '\\';

    private readonly List<string> arguments = [];

    public SysProcessArguments(string arguments)
    {
        arguments.Required();

        for (var i = 0; i < arguments.Length; i++)
        {
            while (i < arguments.Length && (arguments[i] == ' ' || arguments[i] == '\t'))
                i++;

            if (i == arguments.Length)
                break;

            this.arguments.Add(GetNextArgument(arguments, ref i));
        }
    }

    public SysProcessArguments(IReadOnlyList<string> arguments)
    {
        foreach (var argument in arguments.Required())
        {
            if (argument is null)
                throw new ArgumentNullException(nameof(arguments), "ArgumentListMayNotContainNull");

            this.arguments.Add(argument);
        }
    }

    public IEnumerator<string> GetEnumerator() => this.arguments.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
    public int Count => this.arguments.Count;

    public string this[int index] => this.arguments[index];

    public static implicit operator SysProcessArguments?(string? args) => args == null ? null : new(args);
    public static implicit operator SysProcessArguments?(string[]? args) => args == null ? null : new(args);
    public static implicit operator SysProcessArguments?(List<string>? args) => args == null ? null : new(args);

    private static string GetNextArgument(string arguments, ref int index)
    {
        var builder = new StringBuilder(arguments.Length);
        var inQuotes = false;

        while (index < arguments.Length)
        {
            var slashCount = 0;

            while (index < arguments.Length && arguments[index] == '\\')
            {
                index++;
                slashCount++;
            }

            if (slashCount > 0)
            {
                if (index >= arguments.Length || arguments[index] != '"')
                    builder.Append('\\', slashCount);
                else
                {
                    builder.Append('\\', slashCount / 2);

                    if (slashCount % 2 != 0)
                    {
                        builder.Append('"');
                        index++;
                    }
                }

                continue;
            }

            var ch = arguments[index];

            if (ch == '"')
            {
                if (inQuotes && index < arguments.Length - 1 && arguments[index + 1] == '"')
                {
                    builder.Append('"');
                    index++;
                }
                else
                    inQuotes = !inQuotes;

                index++;

                continue;
            }

            if ((ch == ' ' || ch == '\t') && !inQuotes)
                break;

            builder.Append(ch);
            index++;
        }

        return builder.ToString();
    }

    public override string ToString()
    {
        if (this.arguments.Count == 0)
            return string.Empty;

        var builder = new StringBuilder(256);

        foreach (var argument in this.arguments)
            AppendArgument(builder, argument);

        return builder.ToString();
    }

    private static void AppendArgument(StringBuilder builder, string argument)
    {
        if (builder.Length != 0)
            builder.Append(' ');

        if (argument.Length != 0 && ContainsNoWhitespaceOrQuotes(argument))
        {
            builder.Append(argument);

            return;
        }

        builder.Append(quote);

        for (var i = 0; i < argument.Length; i++)
        {
            var ch = argument[i];

            if (ch == slash)
            {
                var slashCount = 1;

                while (i + 1 < argument.Length && argument[i + 1] == slash)
                {
                    i++;
                    slashCount++;
                }

                if (i + 1 == argument.Length)
                {
                    builder.Append(slash, slashCount * 2);

                    continue;
                }

                if (argument[i + 1] == quote)
                {
                    builder.Append(slash, slashCount * 2 + 1);
                    builder.Append(quote);
                    i++;

                    continue;
                }

                builder.Append(slash, slashCount);

                continue;
            }

            if (ch == quote)
            {
                builder.Append(slash);
                builder.Append(quote);

                continue;
            }

            builder.Append(ch);
        }

        builder.Append(quote);
    }

    private static bool ContainsNoWhitespaceOrQuotes(string value)
    {
        foreach (var ch in value)
        {
            if (char.IsWhiteSpace(ch) || ch == quote)
                return false;
        }

        return true;
    }
}
