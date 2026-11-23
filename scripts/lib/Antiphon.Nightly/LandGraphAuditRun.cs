// CARD-1483 S5 (with CARD-1385, 1390, 1400, 1409, 1528, 1540, 1613 and 1753): the land verification build graph's
// author-time audit and its emulation pre-check as antiphon-scripts commands:
//   land-graph-audit [--check | --update] [--repo <dir>] [--tool-path <dll>] [--evaluation-budget-seconds <n>] [--land-refresh]
//   land-graph-precheck [--check | --update] [--repo <dir>] [--manifest-path <file>] [--tool-path <dll>]
// They replaced scripts/audit-land-verification-graph.ps1 and scripts/audit-land-verification-precheck.ps1 (deleted
// 2026-11-23, unit 11 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). The owner documents are the
// land verification build graph section of docs/orchestration-loop.md and docs/testing-and-build.md.
//
// The audit itself is compiled C# in the tool (tools/Antiphon.LandGraphAudit.Core/LandGraphAuditGate.cs: --update
// requires the pre-check and the real MSBuild evaluation, --check verifies the pre-check, the manifest and the
// digest/pins record and always evaluates; the refusals, the records and the evaluation budget are the gate's). These
// commands resolve the built tool (LandAuditTool, rebuilding a stale default DLL) and run it as a child process
// (dotnet <dll> audit | precheck) on the host's launcher, exactly as the scripts did:
// - land-graph-audit: when the tool is current, the tool runs the whole audit, writing to this process's own streams
//   (nothing captured). Otherwise this command first runs the audit's opening checks itself (the switches, HEAD, and
//   the source check), because the audit always ran them before resolving the tool: a dirty tree refuses before any
//   rebuild, and a missing tool is reported only after them. It then passes the HEAD it read to the tool, which starts
//   at the pre-check. The tool's exit 0 is the only approval passed on; exit 1 is passed on as is; any other exit (a
//   build without 'audit' exits 64) and a tool that does not start refuse with exit 1. The children get the caller's
//   environment without MSBuild* names, and the tool ends when this process does (--parent-pid). --land-refresh is
//   only for the land-graph-land-refresh command, and only together with --update; a bare --update still refuses
//   with "commit source and pins before auditing" while the pins differ from HEAD.
// - land-graph-precheck: the tool's 'precheck' command; its stdout (trimmed) and stderr are passed through, and its exit
//   0, 1 or 2; a missing tool, a tool that does not start, or any other exit refuses with exit 1. Not an approval.
// Exit 2 is also the CLI's usage code. Plain C#: no PowerShell types; the core reads no environment (the CLI passes it
// in). ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;

namespace Antiphon.Nightly
{
    /// <summary>A step of the audit's opening checks refused (the text follows "audit refused: ").</summary>
    public sealed class LandGraphRefusal : Exception
    {
        /// <summary>A refusal with its reason.</summary>
        public LandGraphRefusal(string message) : base(message) { }
    }

    /// <summary>The land-graph-audit command's options.</summary>
    public sealed class LandGraphAuditRunOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "land-graph-audit [--check | --update] [--repo <dir>] [--tool-path <dll>] [--evaluation-budget-seconds <n>] [--land-refresh]";

        /// <summary>The valued flags.</summary>
        public static readonly IReadOnlyList<string> Valued = Array.AsReadOnly(new[] { "--repo", "--tool-path", "--evaluation-budget-seconds" });

        /// <summary>The switches.</summary>
        public static readonly IReadOnlyList<string> Switches = Array.AsReadOnly(new[] { "--check", "--update", "--land-refresh" });

        /// <summary>The checkout whose graph is audited.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>--check: verify the records (also what no mode switch does).</summary>
        public bool Check { get; init; }

        /// <summary>--update: re-record the manifest and the evaluation record.</summary>
        public bool Update { get; init; }

        /// <summary>--land-refresh: only the land refresh, only with --update: pins may differ from HEAD.</summary>
        public bool LandRefresh { get; init; }

        /// <summary>--tool-path: the tool DLL, used as given (never rebuilt); empty: the default DLL.</summary>
        public string ToolPath { get; init; } = "";

        /// <summary>--evaluation-budget-seconds: the evaluator's exact wait; 0 waits for the evaluator's ceiling.</summary>
        public int EvaluationBudgetSeconds { get; init; }

        /// <summary>The caller's environment (the CLI's); the children get it without MSBuild* names. Null: they inherit this process's.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; init; }

        /// <summary>
        /// The land refresh's hop: the tool's output is captured and then written to host.Out (stdout, then stderr), and
        /// a positive TimeoutMilliseconds bounds the tool (its tree is killed; the result says TimedOut). Not CLI flags.
        /// </summary>
        public bool CaptureOutput { get; init; }

        /// <summary>The tool's wait in milliseconds; 0 or less waits for its exit (the CLI).</summary>
        public int TimeoutMilliseconds { get; init; }

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line.</summary>
        public static LandGraphAuditRunOptions Parse(IReadOnlyList<string> arguments, string defaultRepoRoot, IReadOnlyDictionary<string, string> environment)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new LandGraphAuditRunOptions
            {
                RepoRoot = flags.Value("--repo", "") is { Length: > 0 } repo ? repo : defaultRepoRoot ?? "",
                Check = flags.Switch("--check"),
                Update = flags.Switch("--update"),
                LandRefresh = flags.Switch("--land-refresh"),
                ToolPath = flags.Value("--tool-path", ""),
                EvaluationBudgetSeconds = flags.Int("--evaluation-budget-seconds", 0),
                Environment = environment,
            };
        }
    }

    /// <summary>What an audit run did: its exit code, and whether the tool was killed at TimeoutMilliseconds.</summary>
    public sealed record LandGraphAuditResult(int ExitCode, bool TimedOut);

    /// <summary>The land-graph-precheck command's options.</summary>
    public sealed class LandGraphPrecheckRunOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "land-graph-precheck [--check | --update] [--repo <dir>] [--manifest-path <file>] [--tool-path <dll>]";

        /// <summary>The valued flags.</summary>
        public static readonly IReadOnlyList<string> Valued = Array.AsReadOnly(new[] { "--repo", "--manifest-path", "--tool-path" });

        /// <summary>The switches.</summary>
        public static readonly IReadOnlyList<string> Switches = Array.AsReadOnly(new[] { "--check", "--update" });

        /// <summary>The checkout whose definitions are checked.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>--check (the default): print digest=&lt;HEX&gt; count=&lt;N&gt; and exit 0 only when both manifest lines match.</summary>
        public bool Check { get; init; }

        /// <summary>--update: rewrite the manifest (or --manifest-path) from HEAD.</summary>
        public bool Update { get; init; }

        /// <summary>--manifest-path: the manifest --update writes; a relative path is the tool's, resolved against this process's directory.</summary>
        public string ManifestPath { get; init; } = "";

        /// <summary>--tool-path: the tool DLL, used as given; empty: the default DLL.</summary>
        public string ToolPath { get; init; } = "";

        /// <summary>The arguments after the command name; NightlyUsageException for a malformed line.</summary>
        public static LandGraphPrecheckRunOptions Parse(IReadOnlyList<string> arguments, string defaultRepoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new LandGraphPrecheckRunOptions
            {
                RepoRoot = flags.Value("--repo", "") is { Length: > 0 } repo ? repo : defaultRepoRoot ?? "",
                Check = flags.Switch("--check"),
                Update = flags.Switch("--update"),
                ManifestPath = flags.Value("--manifest-path", ""),
                ToolPath = flags.Value("--tool-path", ""),
            };
        }
    }

    /// <summary>The land-graph-audit and land-graph-precheck commands.</summary>
    public static class LandGraphAuditRun
    {
        /// <summary>The records the source check lets differ from HEAD (the generated ones), and the pins a land refresh stages.</summary>
        public const string ManifestRelative = "server/Application/Services/LandVerificationBuildGraph.manifest.txt";

        /// <summary>The evaluation record.</summary>
        public const string EvaluationRelative = "server/Application/Services/LandVerificationBuildGraph.evaluation.txt";

        /// <summary>The target pins.</summary>
        public const string PinsRelative = "server/Application/Services/LandVerificationBuildGraph.pins.txt";

        /// <summary>The opening checks' git wait (the gate's own child wait).</summary>
        public const int GitTimeoutMilliseconds = 300000;

        private const int Approved = 0;
        private const int Refused = 1;

        /// <summary>
        /// The audit: the tool when it is current, else the opening checks, the resolution (rebuilding) and the tool from the
        /// pre-check on. The tool's own lines go to this process's streams (host.Out with CaptureOutput); this command's
        /// refusals go to host.Error. 0 approved, 1 refused.
        /// </summary>
        public static LandGraphAuditResult Run(LandGraphAuditRunOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            var repo = files.Path.GetFullPath(string.IsNullOrEmpty(options.RepoRoot) ? "." : options.RepoRoot).TrimEnd('\\', '/');
            var environment = LandAuditTool.WithoutMsBuild(options.Environment);

            LandAuditToolResolution tool;
            try
            {
                tool = LandAuditTool.Resolve(repo, options.ToolPath, noRebuild: true, host, files);
            }
            catch (Exception e) when (Expected(e))
            {
                tool = null;
            }
            NightlyLaunchResult ran;
            if (tool?.Path != null)
            {
                ran = RunTool(options, host, repo, environment, tool.Path, "");
            }
            else
            {
                string head;
                try
                {
                    if (options.Check && options.Update) throw new LandGraphRefusal("Specify -Check or -Update, not both.");
                    if (options.LandRefresh && !options.Update) throw new LandGraphRefusal("-LandRefresh requires -Update.");
                    head = GitValue(host, repo, environment, "rev-parse", "HEAD");
                    AssertHeadSource(host, repo, environment, head, options.Update && options.LandRefresh);
                    tool = LandAuditTool.Resolve(repo, options.ToolPath, noRebuild: false, host, files);
                    if (tool.Error != null)
                    {
                        host.Error.WriteLine(tool.Error);
                        throw new LandGraphRefusal("land graph pre-check refused");
                    }
                }
                catch (Exception e) when (e is LandGraphRefusal || Expected(e))
                {
                    host.Error.WriteLine("audit refused: " + e.Message);
                    return new LandGraphAuditResult(Refused, false);
                }
                ran = RunTool(options, host, repo, environment, tool.Path, head);
            }
            if (ran == null) return new LandGraphAuditResult(Refused, false);
            if (ran.TimedOut)
            {
                host.Error.WriteLine("audit refused: land graph audit tool exceeded " + (options.TimeoutMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) + " s");
                return new LandGraphAuditResult(Refused, true);
            }
            if (ran.ExitCode != 0)
            {
                if (ran.ExitCode != 1) host.Error.WriteLine("audit refused: land graph audit tool failed (exit " + ran.ExitCode.ToString(CultureInfo.InvariantCulture) + ")");
                return new LandGraphAuditResult(Refused, false);
            }
            // The tool's exit 0 is the only approval.
            return new LandGraphAuditResult(Approved, false);
        }

        /// <summary>
        /// The tool's audit command line: the DLL (the resolved full path), audit, --repo, --tool-path, the budget and this process's id,
        /// then the switches given and --head when this command already read HEAD.
        /// </summary>
        public static IReadOnlyList<string> AuditArguments(LandGraphAuditRunOptions options, string repo, string tool, int parentPid, string head)
        {
            var arguments = new List<string>
            {
                tool, "audit", "--repo", repo, "--tool-path", tool,
                "--evaluation-budget-seconds", options.EvaluationBudgetSeconds.ToString(CultureInfo.InvariantCulture),
                "--parent-pid", parentPid.ToString(CultureInfo.InvariantCulture),
            };
            if (options.Check) arguments.Add("--check");
            if (options.Update) arguments.Add("--update");
            if (options.LandRefresh) arguments.Add("--land-refresh");
            if (!string.IsNullOrEmpty(head)) arguments.AddRange(new[] { "--head", head });
            return arguments;
        }

        /// <summary>
        /// The pre-check: the tool's precheck command (--update, else --check; --manifest-path when given), run in this
        /// process's directory with its output captured, then its stdout (trimmed, one line) to host.Out and its stderr to
        /// host.Error. 0 approved or matched, 1 mismatch, refusal or a tool that failed, 2 a git failure.
        /// </summary>
        public static int Precheck(LandGraphPrecheckRunOptions options, NightlyHost host, IFileSystem files)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (options.Check && options.Update)
            {
                host.Error.WriteLine("Specify -Check or -Update, not both.");
                return Refused;
            }
            var repo = files.Path.GetFullPath(string.IsNullOrEmpty(options.RepoRoot) ? "." : options.RepoRoot).TrimEnd('\\', '/');
            LandAuditToolResolution tool;
            try
            {
                tool = LandAuditTool.Resolve(repo, options.ToolPath, noRebuild: false, host, files);
            }
            catch (Exception e) when (Expected(e))
            {
                host.Error.WriteLine("land graph pre-check tool did not resolve: " + e.Message);
                return Refused;
            }
            if (tool.Error != null)
            {
                host.Error.WriteLine(tool.Error);
                return Refused;
            }
            var arguments = new List<string> { tool.Path, "precheck", "--repo", repo, options.Update ? "--update" : "--check" };
            if (!string.IsNullOrEmpty(options.ManifestPath)) arguments.AddRange(new[] { "--manifest-path", options.ManifestPath });
            NightlyLaunchResult ran;
            try
            {
                ran = host.Launch(new NightlyLaunch("dotnet", arguments, "", CaptureOutput: true));
            }
            catch (Exception e) when (Expected(e))
            {
                host.Error.WriteLine("land graph pre-check tool did not start: " + e.Message);
                return Refused;
            }
            var text = (ran.StandardOutputText ?? "").TrimEnd();
            if (text.Length > 0) host.Out.WriteLine(text);
            host.Error.Write(ran.StandardErrorText ?? "");
            if (ran.ExitCode != 0 && ran.ExitCode != 1 && ran.ExitCode != 2)
            {
                host.Error.WriteLine("land graph pre-check tool failed (exit " + ran.ExitCode.ToString(CultureInfo.InvariantCulture) + ")");
                return Refused;
            }
            return ran.ExitCode;
        }

        // The tool's audit, writing to this process's streams (or captured, then written, for the land refresh). Null: it
        // did not start (already reported).
        private static NightlyLaunchResult RunTool(LandGraphAuditRunOptions options, NightlyHost host, string repo, IReadOnlyDictionary<string, string> environment,
            string tool, string head)
        {
            NightlyLaunchResult ran;
            try
            {
                ran = host.Launch(new NightlyLaunch("dotnet", AuditArguments(options, repo, tool, host.Processes.CurrentPid, head), repo,
                    TimeoutMilliseconds: options.TimeoutMilliseconds, Environment: environment, CaptureOutput: options.CaptureOutput));
            }
            catch (Exception e) when (Expected(e))
            {
                host.Error.WriteLine("audit refused: land graph audit tool did not start: " + e.Message);
                return null;
            }
            if (options.CaptureOutput)
            {
                host.Out.Write(ran.StandardOutputText ?? "");
                host.Out.Write(ran.StandardErrorText ?? "");
            }
            return ran;
        }

        // Assert-HeadSource: HEAD has not moved, and the working tree matches it apart from the generated records (and the
        // pins a land refresh stages). Evaluation reads disk, while the pre-check hashes HEAD: this binds the two.
        private static void AssertHeadSource(NightlyHost host, string repo, IReadOnlyDictionary<string, string> environment, string head, bool allowPendingPins)
        {
            if (!string.Equals(GitValue(host, repo, environment, "rev-parse", "HEAD"), head, StringComparison.Ordinal)) throw new LandGraphRefusal("HEAD changed during audit");
            var arguments = new List<string> { "diff", "--quiet", "HEAD", "--", ".", ":(exclude)" + ManifestRelative, ":(exclude)" + EvaluationRelative };
            // Pending pins are allowed only for the land-graph-land-refresh command.
            if (allowPendingPins) arguments.Add(":(exclude)" + PinsRelative);
            if (Git(host, repo, environment, arguments.ToArray()).ExitCode != 0) throw new LandGraphRefusal("source differs from HEAD; commit source and pins before auditing");
        }

        private static string GitValue(NightlyHost host, string repo, IReadOnlyDictionary<string, string> environment, params string[] arguments)
        {
            var run = Git(host, repo, environment, arguments);
            var text = (run.StandardOutputText ?? "") + (run.StandardErrorText ?? "");
            if (run.ExitCode != 0) throw new LandGraphRefusal("git failed: " + text.Trim());
            return text.Trim();
        }

        private static NightlyLaunchResult Git(NightlyHost host, string repo, IReadOnlyDictionary<string, string> environment, params string[] arguments)
        {
            var run = host.Launch(new NightlyLaunch("git", arguments, repo, TimeoutMilliseconds: GitTimeoutMilliseconds, Environment: environment, CaptureOutput: true));
            if (run.TimedOut) throw new LandGraphRefusal("subprocess exceeded " + (GitTimeoutMilliseconds / 1000).ToString(CultureInfo.InvariantCulture) + " s: git");
            return run;
        }

        // The failures a step reports as a refusal: files, processes and paths (a programming error still throws).
        internal static bool Expected(Exception e) =>
            e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is Win32Exception || e is ArgumentException
            || e is NotSupportedException || e is PlatformNotSupportedException;
    }
}
