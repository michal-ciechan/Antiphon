// W2-nightly S01: the git seam of the nightly C# (plan 2026-11-21-w2-nightly-port-slices.md, section 2). NightlyHost.Git is
// a NightlyGit; production uses NightlyNativeGit.Run, the twin of Invoke-NightlyGit (nightly-common.ps1): the same -c
// options before -C and the caller's arguments, the inherited environment unchanged, and one Output text of stdout and
// stderr together (its 2>&1 | Out-String). Tests use FakeGit (tests/Antiphon.Tests/Scripts/Nightly).
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace Antiphon.Nightly
{
    /// <summary>One git run: its exit code and its output, stdout and stderr lines together in arrival order.</summary>
    public sealed class NightlyGitResult
    {
        /// <summary>A result; a null output reads as empty.</summary>
        public NightlyGitResult(int exitCode, string output)
        {
            ExitCode = exitCode;
            Output = output ?? "";
        }

        /// <summary>git's exit code.</summary>
        public int ExitCode { get; }

        /// <summary>Every stdout and stderr line, each followed by Environment.NewLine (Out-String's text); empty when git printed nothing.</summary>
        public string Output { get; }
    }

    /// <summary>Runs git with these arguments in this working directory: NightlyNativeGit.Run in production, FakeGit in tests.</summary>
    public delegate NightlyGitResult NightlyGit(string workingDirectory, IReadOnlyList<string> arguments);

    /// <summary>The production NightlyGit: Invoke-NightlyGit's command line, run without a shell.</summary>
    public static class NightlyNativeGit
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        /// <summary>
        /// The options before -C: core.fsmonitor=false overrides a system core.fsmonitor=true without writing repository
        /// configuration, and no command starts automatic gc or maintenance. Object ids, refs and the caller's arguments
        /// are unchanged.
        /// </summary>
        public static IReadOnlyList<string> Options { get; } =
            Array.AsReadOnly(new[] { "-c", "core.fsmonitor=false", "-c", "gc.auto=0", "-c", "maintenance.auto=false" });

        /// <summary>git's arguments for one call: Options, then -C and the working directory (empty stays empty: git then uses its own directory), then the caller's arguments.</summary>
        public static IReadOnlyList<string> CommandLine(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var line = new List<string>(Options) { "-C", workingDirectory ?? "" };
            if (arguments != null) line.AddRange(arguments);
            return line;
        }

        /// <summary>
        /// Runs git (found on PATH) with CommandLine(workingDirectory, arguments) and waits for it, with no timeout, as
        /// Invoke-NightlyGit does. The environment is this process's own. Both streams are read as UTF-8. A git that cannot
        /// start throws (Invoke-NightlyGit's call operator throws too).
        /// </summary>
        public static NightlyGitResult Run(string workingDirectory, IReadOnlyList<string> arguments)
        {
            var start = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };
            foreach (var argument in CommandLine(workingDirectory, arguments)) start.ArgumentList.Add(argument);
            var lines = new List<string>();
            void Line(object sender, DataReceivedEventArgs e)
            {
                if (e.Data == null) return;
                lock (lines) lines.Add(e.Data);
            }
            using var process = new Process { StartInfo = start };
            process.OutputDataReceived += Line;
            process.ErrorDataReceived += Line;
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            // Without a timeout WaitForExit also waits for both streams to reach their end.
            process.WaitForExit();
            var output = new StringBuilder();
            lock (lines)
                foreach (var line in lines) output.Append(line).Append(Environment.NewLine);
            return new NightlyGitResult(process.ExitCode, output.ToString());
        }
    }
}
