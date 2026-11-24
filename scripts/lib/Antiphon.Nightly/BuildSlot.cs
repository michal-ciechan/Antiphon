// CARD-0589 D-6, CARD-0800, CARD-0845, CARD-1048, CARD-1627, CARD-1981: one build or test command under a host build slot
// (unit 34 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). It replaced the body of scripts/build-slot.ps1
// and the wrapper half of scripts/lib/build-slot.ps1. build-slot.ps1 stays as a thin launcher because agents, the delegate
// bundle and other checkouts on this machine run it by path; it passes its own PID as --holder-pid, so the lease keeps
// naming the process its caller started. The CLI runs it as
//
//   antiphon-scripts build-slot --label <what> [--slot-wait-minutes 45] [--no-slot] [--holder-pid <pid>] -- <command...>
//
// It takes a lease from the session runner's /build-slots broker (BuildSlotBroker: FIFO, a BUILD SLOT waiting line when the
// reason changes and once a minute), adds the grant's -maxcpucount:N to a dotnet build|test|publish|pack|msbuild command
// that names none (never to dotnet run, which hands the switch to the program; a note says so), runs the command, releases
// the lease and answers with the command's exit code. A non-dotnet command cannot be capped: it gets the grant as
// ANTIPHON_BUILD_SLOT_MAXCPUCOUNT and a BUILD SLOT warning line (CARD-1627). Exit 4 when no slot came within the wait
// (nothing ran); 1 when the command cannot be resolved or started; 130 when interrupted (the child's tree is killed, then
// the lease is released). An unreachable runner (no answer, or an old runner without /build-slots) is not a failure: after
// the grace (60 s) the command runs unleased at -maxcpucount:4 and a BUILD SLOT unleased line says so. --no-slot is for an
// operator shell only.
//
// The command starts with literal argv in the caller's directory (CARD-0800): a native executable directly, a .ps1 under
// pwsh -NoProfile -NonInteractive -File (its named parameters bind by name, its process exit code returns; CARD-0845), a
// .cmd or .bat through the system's batch launch. A bare name is looked up on PATH as PowerShell did (in each directory
// <name>.ps1, then PATHEXT on Windows, then the name). Windows PowerShell (powershell.exe) starts without PSModulePath, so it
// rebuilds its own module path (CARD-1048). A dotnet command on Linux gets a private TMPDIR for the lease (CARD-1981, the
// platform's LeaseTemp). Every child is prepared by the platform's PrepareChild (BuildDriverEnvironment, CARD-1506) with the
// grant. In-process callers (run-daemon's build, qualification-launch, the post-land lane) run it through a BuildSlotRunner.
//
// The host carries every outside observation: HTTP (NightlyReportHttp; production 10 s, no proxy), the clock and sleep,
// the process table (the holder's start time), the environment (ANTIPHON_BUILD_SLOTS_URL, else the runner on this host:
// Windows http://localhost:17204/build-slots, Linux http://127.0.0.1:8080/build-slots; ANTIPHON_SESSION_ID and
// ANTIPHON_TASK_ID for the request; PATH and PATHEXT; the offline seams C589_SLOT_WAIT_SECONDS, C589_SLOT_GRACE_SECONDS and
// C589_SLOT_RETRY_MS), the file system and the children. Plain C#: no PowerShell types. Owner: docs/testing-and-build.md
// "Build slots (CARD-0589)". ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The build-slot command's options (build-slot.ps1 -Label -SlotWaitMinutes -NoSlot -- command).</summary>
    public sealed class BuildSlotOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "build-slot --label <what> [--slot-wait-minutes 45] [--no-slot] [--holder-pid <pid>] -- <command...>";

        /// <summary>What the lease is for; blank: the command's first two tokens.</summary>
        public string Label { get; init; } = "";

        /// <summary>How long to wait for a slot while the broker answers busy or below its memory floor.</summary>
        public double WaitMinutes { get; init; } = 45;

        /// <summary>Run without asking the broker (an operator shell only).</summary>
        public bool NoSlot { get; init; }

        /// <summary>The process the lease belongs to; null: this process. The launcher passes its own PID.</summary>
        public int? HolderPid { get; init; }

        /// <summary>The command, each element one argv token.</summary>
        public IReadOnlyList<string> Command { get; init; } = Array.Empty<string>();

        /// <summary>Where the command runs and relative paths resolve; empty: this process's directory.</summary>
        public string WorkingDirectory { get; init; } = "";

        /// <summary>Variables added to the command's environment (an in-process caller's); null: none.</summary>
        public IReadOnlyDictionary<string, string> Environment { get; init; }

        /// <summary>
        /// The arguments after the command name: --label, --slot-wait-minutes, --no-slot and --holder-pid, then "--" and the
        /// command verbatim (the first argument that is not a flag also starts it). A missing command, an unknown or repeated
        /// flag, a valueless flag, a negative or unreadable wait and a holder PID below 1 are NightlyUsageExceptions.
        /// </summary>
        public static BuildSlotOptions Parse(IReadOnlyList<string> arguments, string workingDirectory)
        {
            var label = "";
            var wait = 45.0;
            var noSlot = false;
            int? holder = null;
            var command = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var count = arguments?.Count ?? 0;
            string Value(ref int index, string flag)
            {
                if (!seen.Add(flag)) throw new NightlyUsageException("repeated flag " + flag);
                if (index + 1 >= count) throw new NightlyUsageException("missing value for " + flag);
                return arguments[++index] ?? "";
            }
            for (var i = 0; i < count; i++)
            {
                var token = arguments[i] ?? "";
                if (token == "--")
                {
                    for (var j = i + 1; j < count; j++) command.Add(arguments[j] ?? "");
                    break;
                }
                if (token == "--label")
                {
                    label = Value(ref i, token);
                }
                else if (token == "--slot-wait-minutes")
                {
                    var text = Value(ref i, token);
                    if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out wait) || double.IsNaN(wait) || double.IsInfinity(wait) || wait < 0)
                        throw new NightlyUsageException("--slot-wait-minutes expects a number of minutes, not '" + text + "'");
                }
                else if (token == "--holder-pid")
                {
                    var text = Value(ref i, token);
                    if (!int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) || pid < 1)
                        throw new NightlyUsageException("--holder-pid expects a process id above 0, not '" + text + "'");
                    holder = pid;
                }
                else if (token == "--no-slot")
                {
                    if (!seen.Add(token)) throw new NightlyUsageException("repeated flag " + token);
                    noSlot = true;
                }
                else if (token.StartsWith("-", StringComparison.Ordinal))
                {
                    throw new NightlyUsageException("unknown flag " + token + " (the command follows --)");
                }
                else
                {
                    for (var j = i; j < count; j++) command.Add(arguments[j] ?? "");
                    break;
                }
            }
            if (command.Count == 0) throw new NightlyUsageException("missing command: build-slot --label <what> -- <command...>");
            return new BuildSlotOptions
            {
                Label = label, WaitMinutes = wait, NoSlot = noSlot, HolderPid = holder, Command = command, WorkingDirectory = workingDirectory ?? "",
            };
        }
    }

    /// <summary>What the broker answered a lease request with.</summary>
    public enum BuildSlotOutcome
    {
        /// <summary>A lease is held; release it with BuildSlotBroker.Exit.</summary>
        Granted,

        /// <summary>The broker is disabled: nothing is held, MaxCpuCount is its configured count.</summary>
        Unlimited,

        /// <summary>The runner did not answer for the grace (or has no /build-slots): run anyway at -maxcpucount 4.</summary>
        Unleased,

        /// <summary>Busy or below the memory floor for the whole wait: run nothing (exit 4).</summary>
        Timeout,

        /// <summary>Interrupted while waiting: run nothing.</summary>
        Cancelled,
    }

    /// <summary>One lease request's result (Enter-AntiphonBuildSlot's record).</summary>
    public sealed class BuildSlotLease
    {
        /// <summary>The broker's answer.</summary>
        public BuildSlotOutcome Outcome { get; init; }

        /// <summary>The lease to release; null unless granted.</summary>
        public string LeaseId { get; init; }

        /// <summary>The node count the command runs with (the grant's, the broker's for unlimited, 4 unleased, 0 on timeout).</summary>
        public int MaxCpuCount { get; init; }

        /// <summary>Whole seconds waited.</summary>
        public int WaitedSeconds { get; init; }

        /// <summary>The broker's URL.</summary>
        public string Endpoint { get; init; } = "";

        /// <summary>The last queue position seen.</summary>
        public int Position { get; init; }

        internal long GrantedAt { get; init; }

        internal BuildSlotRenewal Renewal { get; init; }
    }

    /// <summary>A private temporary directory for one lease (LeaseTempDirectory's handle): its path, removed on Dispose.</summary>
    public sealed record BuildSlotTemp(string DirectoryPath, IDisposable Handle);

    /// <summary>
    /// The parts of a child's start that live outside the core: PrepareChild (BuildDriverEnvironment.Apply(start, grant),
    /// CARD-1506) and LeaseTemp (LeaseTempDirectory.TryCreate, CARD-1981; null on Windows). The CLI binds both.
    /// </summary>
    public sealed class BuildSlotPlatform
    {
        /// <summary>Adjusts a child's start information with the grant (0 without a slot) before it starts; null: nothing.</summary>
        public Action<ProcessStartInfo, int> PrepareChild { get; init; }

        /// <summary>A private TMPDIR for a dotnet command, or null; null: none.</summary>
        public Func<BuildSlotTemp> LeaseTemp { get; init; }
    }

    /// <summary>One command the slot runs: the resolved file, its literal argv, where, the variables changed (a null value removes one) and the grant.</summary>
    public sealed record BuildSlotChild(string FileName, IReadOnlyList<string> Arguments, string WorkingDirectory,
        IReadOnlyDictionary<string, string> Environment, int CompilerGrant);

    /// <summary>Runs the command under the slot.</summary>
    public interface IBuildSlotChildren
    {
        /// <summary>Starts the child, waits for it and returns its exit code; throws Win32Exception when it cannot start.</summary>
        int Run(BuildSlotChild child);
    }

    /// <summary>
    /// One in-process caller's build slot: runs options' command under a slot. capture null: the BUILD SLOT lines go to the
    /// runner's output and the command shares this process's console; otherwise both, and the command's stdout and stderr,
    /// go to capture.
    /// </summary>
    public delegate int BuildSlotRunner(BuildSlotOptions options, TextWriter capture);

    /// <summary>The build slot's host. Every member that observes the machine is required, so a test cannot reach it by omission.</summary>
    public sealed class BuildSlotHost
    {
        /// <summary>The BUILD SLOT lines.</summary>
        public required TextWriter Out { get; init; }

        /// <summary>A command that cannot be resolved or started.</summary>
        public required TextWriter Error { get; init; }

        /// <summary>The wait's clock.</summary>
        public required TimeProvider Clock { get; init; }

        /// <summary>Waits between lease requests.</summary>
        public required Action<TimeSpan> Sleep { get; init; }

        /// <summary>The broker's HTTP (method, uri, headers, body); a failure throws.</summary>
        public required NightlyReportHttp Http { get; init; }

        /// <summary>Runs the command.</summary>
        public required IBuildSlotChildren Children { get; init; }

        /// <summary>This process's id and the holder's start time.</summary>
        public required INightlyProcessTable Processes { get; init; }

        /// <summary>The environment as the slot reads it: a variable's value, null when unset.</summary>
        public required Func<string, string> Variables { get; init; }

        /// <summary>Command lookup (File.Exists).</summary>
        public required IFileSystem Files { get; init; }

        /// <summary>True on Windows (PATHEXT, batch files, powershell.exe, the default endpoint).</summary>
        public required bool Windows { get; init; }

        /// <summary>A private TMPDIR for a dotnet command (CARD-1981); null answers none.</summary>
        public Func<BuildSlotTemp> LeaseTemp { get; init; }

        /// <summary>The renewal thread's wait: false once cancelled. A test answers quickly instead of waiting.</summary>
        public Func<TimeSpan, CancellationToken, bool> RenewWait { get; init; } = (span, token) => !token.WaitHandle.WaitOne(span);

        /// <summary>Ctrl+C or a termination signal: stops the wait or the command.</summary>
        public CancellationToken Cancellation { get; init; }

        /// <summary>
        /// The production host: the system clock, a sleep that ends early when cancelled, HTTP without a proxy and a 10 s
        /// deadline per request, the real process table, file system and environment, and children started by
        /// BuildSlotProcesses with the platform's PrepareChild (capture: the children's output goes there).
        /// </summary>
        public static BuildSlotHost Production(TextWriter output, TextWriter error, BuildSlotPlatform platform, CancellationToken cancellation,
            TextWriter capture = null)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (error == null) throw new ArgumentNullException(nameof(error));
            return new BuildSlotHost
            {
                Out = output,
                Error = error,
                Clock = TimeProvider.System,
                Sleep = span => cancellation.WaitHandle.WaitOne(span),
                Http = NightlyReport.HttpTransport(new SocketsHttpHandler { UseProxy = false }, BuildSlotBroker.RequestTimeout),
                Children = new BuildSlotProcesses(platform?.PrepareChild, capture, cancellation),
                Processes = new NightlyProcessTable(),
                Variables = System.Environment.GetEnvironmentVariable,
                Files = new FileSystem(),
                Windows = OperatingSystem.IsWindows(),
                LeaseTemp = platform?.LeaseTemp,
                Cancellation = cancellation,
            };
        }
    }

    /// <summary>The dotnet command-line rules of the slot (Add-AntiphonMaxCpuCount and its neighbours).</summary>
    public static class BuildSlotCommandLine
    {
        private static readonly Regex NodeSwitch = new Regex("^(-{1,2}|/)(m|maxcpucount)(:.*)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly string[] CappedVerbs = { "build", "test", "publish", "pack", "msbuild" };

        /// <summary>True when the token names dotnet or dotnet.exe (a path's leaf, ignoring case).</summary>
        public static bool IsDotnet(string token)
        {
            if (string.IsNullOrWhiteSpace(token)) return false;
            var leaf = Leaf(token);
            return string.Equals(leaf, "dotnet", StringComparison.OrdinalIgnoreCase) || string.Equals(leaf, "dotnet.exe", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Inserts -maxcpucount:N (before any "--") for dotnet build|test|publish|pack|msbuild, unless the command already names
        /// -m or -maxcpucount (in -, --, or / form, with or without a value) before "--". Never for dotnet run: measured on SDK
        /// 10.0.401, dotnet run hands -maxcpucount:N to the program as an argument rather than to its build.
        /// </summary>
        public static IReadOnlyList<string> AddMaxCpuCount(IReadOnlyList<string> command, int maxCpuCount)
        {
            var tokens = (command ?? Array.Empty<string>()).ToList();
            if (tokens.Count < 2 || maxCpuCount < 1 || !IsDotnet(tokens[0])) return tokens;
            var verb = (tokens[1] ?? "").ToLowerInvariant();
            if (!CappedVerbs.Contains(verb, StringComparer.Ordinal)) return tokens;
            var end = tokens.IndexOf("--");
            if (end < 0) end = tokens.Count;
            for (var i = 2; i < end; i++)
                if (NodeSwitch.IsMatch(tokens[i] ?? "")) return tokens;
            tokens.Insert(end, "-maxcpucount:" + maxCpuCount.ToString(CultureInfo.InvariantCulture));
            return tokens;
        }

        /// <summary>dotnet run without --no-build (before "--"): its implicit build runs at the default node count.</summary>
        public static bool IsDotnetRunWithBuild(IReadOnlyList<string> command)
        {
            var tokens = command ?? Array.Empty<string>();
            if (tokens.Count < 2 || !IsDotnet(tokens[0]) || !string.Equals(tokens[1], "run", StringComparison.OrdinalIgnoreCase)) return false;
            for (var i = 2; i < tokens.Count && tokens[i] != "--"; i++)
                if (string.Equals(tokens[i], "--no-build", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>The note printed for a leased dotnet run with its implicit build.</summary>
        public static string DotnetRunNote(int maxCpuCount) =>
            "BUILD SLOT note: dotnet run hands -maxcpucount to the program, so its implicit build runs at the default node count; build first and use dotnet run --no-build to apply maxcpucount="
            + maxCpuCount.ToString(CultureInfo.InvariantCulture);

        /// <summary>
        /// CARD-1627: -maxcpucount can only be put on a dotnet command line. MSBuild (SDK 10.0.403) honours no environment
        /// variable for its node count, and a Directory.Build.rsp would reach every build in the tree, so a wrapped non-dotnet
        /// command (a bash loop.sh, a script) is told the grant through ANTIPHON_BUILD_SLOT_MAXCPUCOUNT and this line says the
        /// cap is not enforced. Null for a dotnet command, an empty one, or no grant.
        /// </summary>
        public static string CapWarning(IReadOnlyList<string> command, int maxCpuCount)
        {
            var tokens = command ?? Array.Empty<string>();
            if (tokens.Count < 1 || maxCpuCount < 1 || IsDotnet(tokens[0])) return null;
            return "BUILD SLOT warning: maxcpucount=" + maxCpuCount.ToString(CultureInfo.InvariantCulture) + " is not applied to " + Leaf(tokens[0] ?? "")
                + ": a dotnet build inside it runs at the default node count unless it passes -maxcpucount:$ANTIPHON_BUILD_SLOT_MAXCPUCOUNT;"
                + " lease each build separately, never one lease around a loop";
        }

        /// <summary>The token's last path segment ('/' or '\' separated).</summary>
        public static string Leaf(string token)
        {
            var text = token ?? "";
            var cut = text.LastIndexOfAny(new[] { '/', '\\' });
            return cut < 0 ? text : text.Substring(cut + 1);
        }
    }

    /// <summary>How a resolved command starts.</summary>
    public enum BuildSlotTargetKind
    {
        /// <summary>A native executable, started directly.</summary>
        Application,

        /// <summary>A PowerShell script, started under pwsh -NoProfile -NonInteractive -File.</summary>
        Script,

        /// <summary>A Windows .cmd or .bat file, started directly (the system runs it under cmd.exe).</summary>
        Batch,
    }

    /// <summary>The command as it starts: the file, its literal argv and how it was resolved.</summary>
    public sealed record BuildSlotTarget(string FileName, IReadOnlyList<string> Arguments, BuildSlotTargetKind Kind, string Resolved)
    {
        /// <summary>
        /// Resolves command[0] as PowerShell's Get-Command did for build-slot.ps1: a token with a directory part is a path
        /// against workingDirectory (on Windows a missing extension tries .ps1 and PATHEXT); a bare name is looked up on PATH
        /// (in each directory: an extension it already has, else &lt;name&gt;.ps1, PATHEXT on Windows, the bare name elsewhere).
        /// A .ps1 starts under pwsh (resolved the same way) with -NoProfile -NonInteractive -File &lt;script&gt;; .cmd and .bat
        /// on Windows start directly; anything else is an application. Null when nothing is found.
        /// </summary>
        public static BuildSlotTarget Resolve(IReadOnlyList<string> command, string workingDirectory, Func<string, string> variables, bool windows, IFileSystem files)
        {
            if (command == null || command.Count == 0) return null;
            var found = Find(command[0] ?? "", workingDirectory, variables, windows, files);
            if (found == null) return null;
            var rest = command.Skip(1).Select(token => token ?? "").ToList();
            var extension = System.IO.Path.GetExtension(found);
            if (string.Equals(extension, ".ps1", StringComparison.OrdinalIgnoreCase))
            {
                var pwsh = Find("pwsh", workingDirectory, variables, windows, files, scripts: false) ?? "pwsh";
                return new BuildSlotTarget(pwsh, new[] { "-NoProfile", "-NonInteractive", "-File", found }.Concat(rest).ToList(), BuildSlotTargetKind.Script, found);
            }
            if (windows && (string.Equals(extension, ".cmd", StringComparison.OrdinalIgnoreCase) || string.Equals(extension, ".bat", StringComparison.OrdinalIgnoreCase)))
                return new BuildSlotTarget(found, rest, BuildSlotTargetKind.Batch, found);
            return new BuildSlotTarget(found, rest, BuildSlotTargetKind.Application, found);
        }

        private static string Find(string token, string workingDirectory, Func<string, string> variables, bool windows, IFileSystem files, bool scripts = true)
        {
            if (string.IsNullOrWhiteSpace(token)) return null;
            var extensions = Extensions(variables, windows, scripts);
            var hasExtension = System.IO.Path.HasExtension(token);
            if (token.IndexOfAny(new[] { '/', '\\' }) >= 0 || files.Path.IsPathRooted(token))
            {
                string full;
                try
                {
                    full = files.Path.GetFullPath(string.IsNullOrEmpty(workingDirectory) ? token : files.Path.Combine(workingDirectory, token));
                }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
                {
                    return null;
                }
                if (files.File.Exists(full)) return full;
                if (hasExtension || !windows) return null;
                return extensions.Select(extension => full + extension).FirstOrDefault(files.File.Exists);
            }
            foreach (var directory in (variables("PATH") ?? "").Split(System.IO.Path.PathSeparator))
            {
                var root = directory.Trim().Trim('"');
                if (root.Length == 0 || !files.Path.IsPathRooted(root)) continue;
                if (hasExtension)
                {
                    var candidate = files.Path.Combine(root, token);
                    if (files.File.Exists(candidate)) return candidate;
                    continue;
                }
                foreach (var extension in extensions)
                {
                    var candidate = files.Path.Combine(root, token + extension);
                    if (files.File.Exists(candidate)) return candidate;
                }
            }
            return null;
        }

        // In each directory: <name>.ps1 (scripts only), then PATHEXT (Windows) or the bare name.
        private static IReadOnlyList<string> Extensions(Func<string, string> variables, bool windows, bool scripts)
        {
            var list = new List<string>();
            if (scripts) list.Add(".ps1");
            if (windows)
            {
                var pathExt = variables("PATHEXT");
                if (string.IsNullOrWhiteSpace(pathExt)) pathExt = ".COM;.EXE;.BAT;.CMD";
                foreach (var extension in pathExt.Split(';'))
                {
                    // Lower case: the file system ignores it, and the started file reads as dotnet.exe, not dotnet.EXE.
                    var value = extension.Trim().ToLowerInvariant();
                    if (value.StartsWith(".", StringComparison.Ordinal) && !list.Contains(value, StringComparer.OrdinalIgnoreCase)) list.Add(value);
                }
            }
            else
            {
                list.Add("");
            }
            return list;
        }
    }

    /// <summary>The production children: ProcessStartInfo with literal argv, the caller's directory and the slot's variables.</summary>
    public sealed class BuildSlotProcesses : IBuildSlotChildren
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);
        private readonly Action<ProcessStartInfo, int> _prepare;
        private readonly TextWriter _capture;
        private readonly CancellationToken _cancellation;

        /// <summary>
        /// prepare adjusts each start with the grant (BuildDriverEnvironment; null: nothing). capture null: the child shares
        /// this process's console; otherwise its stdout and stderr lines are written there. cancellation kills the child's tree.
        /// </summary>
        public BuildSlotProcesses(Action<ProcessStartInfo, int> prepare, TextWriter capture, CancellationToken cancellation)
        {
            _prepare = prepare;
            _capture = capture;
            _cancellation = cancellation;
        }

        /// <summary>The child's start information before PrepareChild: the file, literal argv, the directory and the changed variables.</summary>
        public static ProcessStartInfo StartInfo(BuildSlotChild child)
        {
            if (child == null) throw new ArgumentNullException(nameof(child));
            var start = new ProcessStartInfo(child.FileName) { UseShellExecute = false, WorkingDirectory = child.WorkingDirectory ?? "" };
            foreach (var argument in child.Arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument ?? "");
            foreach (var pair in child.Environment ?? new Dictionary<string, string>())
            {
                if (pair.Value == null) start.Environment.Remove(pair.Key);
                else start.Environment[pair.Key] = pair.Value;
            }
            return start;
        }

        /// <summary>
        /// Starts the child and polls for its exit, so an interruption is seen: then its tree is killed and the answer is
        /// BuildSlot.Interrupted. A child that cannot start throws.
        /// </summary>
        public int Run(BuildSlotChild child)
        {
            var start = StartInfo(child);
            _prepare?.Invoke(start, child.CompilerGrant);
            var gate = new object();
            if (_capture != null)
            {
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = Utf8NoBom;
                start.StandardErrorEncoding = Utf8NoBom;
            }
            using var process = new Process { StartInfo = start };
            if (_capture != null)
            {
                void Line(string data)
                {
                    if (data == null) return;
                    lock (gate) _capture.WriteLine(data);
                }
                process.OutputDataReceived += (_, e) => Line(e.Data);
                process.ErrorDataReceived += (_, e) => Line(e.Data);
            }
            if (!process.Start()) throw new InvalidOperationException("the command did not start: " + child.FileName);
            if (_capture != null)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            try
            {
                while (!process.WaitForExit(250))
                {
                    if (!_cancellation.IsCancellationRequested) continue;
                    Kill(process);
                    return BuildSlot.Interrupted;
                }
                process.WaitForExit();
                return process.ExitCode;
            }
            finally
            {
                Kill(process);
            }
        }

        private static void Kill(Process process)
        {
            try
            {
                if (process.HasExited) return;
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
            catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is NotSupportedException)
            {
                // Already gone, or not ours to stop: the lease is released either way.
            }
        }
    }

    /// <summary>The /build-slots broker client (Enter-AntiphonBuildSlot and Exit-AntiphonBuildSlot).</summary>
    public static class BuildSlotBroker
    {
        /// <summary>The node count of an unleased run.</summary>
        public const int UnleasedMaxCpuCount = 4;

        /// <summary>Each request's deadline.</summary>
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        /// <summary>ANTIPHON_BUILD_SLOTS_URL (trimmed, no trailing slash), else the runner on this host.</summary>
        public static string Endpoint(Func<string, string> variables, bool windows)
        {
            var configured = variables("ANTIPHON_BUILD_SLOTS_URL");
            if (!string.IsNullOrWhiteSpace(configured)) return configured.Trim().TrimEnd('/');
            return windows ? "http://localhost:17204/build-slots" : "http://127.0.0.1:8080/build-slots";
        }

        /// <summary>Format-AntiphonBuildSlotSpan: under a minute "Ns", else whole minutes "Nm".</summary>
        public static string Span(double seconds) =>
            seconds < 60
                ? ((int)Math.Floor(seconds)).ToString(CultureInfo.InvariantCulture) + "s"
                : ((int)Math.Floor(seconds / 60)).ToString(CultureInfo.InvariantCulture) + "m";

        /// <summary>
        /// Waits in FIFO order for a slot, printing BUILD SLOT lines. The holder is options.HolderPid (else this process) with
        /// its start time; the request names the label and ANTIPHON_SESSION_ID / ANTIPHON_TASK_ID. A 200 grant (or unlimited)
        /// answers at once; a 409 busy or memory-floor answer waits (the broker's retry hint, 250 ms to 60 s) until the wait
        /// ends (Timeout); anything else (no answer, an old runner's 404) runs unleased once it has lasted the grace, which
        /// starts at the first failed answer, even past the wait's deadline. A renew-mode grant (renewEverySeconds) is renewed
        /// by a background thread until Exit.
        /// </summary>
        public static BuildSlotLease Enter(string label, BuildSlotOptions options, BuildSlotHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            var endpoint = Endpoint(host.Variables, host.Windows);
            var waitSeconds = Override(host.Variables("C589_SLOT_WAIT_SECONDS")) ?? options.WaitMinutes * 60;
            var graceSeconds = Override(host.Variables("C589_SLOT_GRACE_SECONDS")) ?? 60;
            var retryOverride = (int)(Override(host.Variables("C589_SLOT_RETRY_MS")) ?? 0);
            var holder = options.HolderPid ?? host.Processes.CurrentPid;
            var body = RequestBody(holder, host.Processes.StartTimeUtc(holder), label, Blank(host.Variables("ANTIPHON_SESSION_ID")), Blank(host.Variables("ANTIPHON_TASK_ID")));

            var started = host.Clock.GetTimestamp();
            double Elapsed() => host.Clock.GetElapsedTime(started).TotalSeconds;
            var unreachableSince = -1.0;
            var position = 0;
            var lastState = "";
            var lastPrinted = -1000.0;
            while (true)
            {
                var answer = Send(host, "POST", endpoint, body);
                var json = Parse(answer.Body);
                var type = Text(json, "type");
                var elapsed = Elapsed();
                var sleepMs = 5000;

                if (answer.Status == 200 && json.HasValue)
                {
                    var cpu = Int(json, "maxCpuCount");
                    if (json.Value.TryGetProperty("unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True)
                    {
                        host.Out.WriteLine("BUILD SLOT unlimited maxcpucount=" + Number(cpu));
                        return new BuildSlotLease { Outcome = BuildSlotOutcome.Unlimited, MaxCpuCount = cpu, WaitedSeconds = Whole(elapsed), Endpoint = endpoint };
                    }
                    var leaseId = Text(json, "leaseId");
                    host.Out.WriteLine("BUILD SLOT granted lease=" + leaseId + " waited=" + Number(Whole(elapsed)) + "s maxcpucount=" + Number(cpu));
                    var every = Int(json, "renewEverySeconds");
                    return new BuildSlotLease
                    {
                        Outcome = BuildSlotOutcome.Granted, LeaseId = leaseId, MaxCpuCount = cpu, WaitedSeconds = Whole(elapsed), Endpoint = endpoint,
                        GrantedAt = host.Clock.GetTimestamp(), Renewal = every > 0 ? BuildSlotRenewal.Start(host, endpoint + "/" + leaseId + "/renew", every) : null,
                    };
                }

                if (answer.Status == 409 && (type == "build_slot_busy" || type == "build_slot_memory_floor"))
                {
                    unreachableSince = -1;
                    position = Int(json, "queuePosition");
                    if (elapsed >= waitSeconds)
                    {
                        host.Out.WriteLine("BUILD SLOT timeout after " + Span(waitSeconds) + " position=" + Number(position));
                        return new BuildSlotLease { Outcome = BuildSlotOutcome.Timeout, WaitedSeconds = Whole(elapsed), Endpoint = endpoint, Position = position };
                    }
                    sleepMs = retryOverride > 0 ? retryOverride : Math.Min(Math.Max(Int(json, "retryAfterMs"), 250), 60000);
                    // Once a minute (and whenever the reason changes) the wait is visible in the transcript.
                    if (type != lastState || elapsed - lastPrinted >= 60)
                    {
                        var minutes = Number((int)Math.Floor(elapsed / 60));
                        host.Out.WriteLine(type == "build_slot_busy"
                            ? "BUILD SLOT waiting label=" + label + " position=" + Number(position) + " occupied=" + Number(Int(json, "occupied")) + "/" + Number(Int(json, "budget"))
                                + " elapsed=" + minutes + "m"
                            : "BUILD SLOT waiting label=" + label + " reason=memory_floor available=" + Long(json, "availableMb").ToString(CultureInfo.InvariantCulture)
                                + "MB floor=" + Long(json, "floorMb").ToString(CultureInfo.InvariantCulture) + "MB position=" + Number(position) + " elapsed=" + minutes + "m");
                        lastState = type;
                        lastPrinted = elapsed;
                    }
                }
                else
                {
                    // No answer, an old runner without /build-slots (404), or anything else unexpected.
                    var last = answer.Status == 0 ? "no answer" : "http " + Number(answer.Status);
                    if (unreachableSince < 0) unreachableSince = elapsed;
                    if (retryOverride > 0) sleepMs = retryOverride;
                    if (elapsed - unreachableSince >= graceSeconds)
                    {
                        host.Out.WriteLine("BUILD SLOT unleased reason=runner_unreachable maxcpucount=" + Number(UnleasedMaxCpuCount) + " last=" + last);
                        return new BuildSlotLease { Outcome = BuildSlotOutcome.Unleased, MaxCpuCount = UnleasedMaxCpuCount, WaitedSeconds = Whole(elapsed), Endpoint = endpoint };
                    }
                }

                // A reachable refusal uses the wait deadline; an unreachable runner gets its own full grace even if the first
                // failed answer arrives after that deadline.
                var remaining = unreachableSince < 0 ? waitSeconds - Elapsed() : graceSeconds - (Elapsed() - unreachableSince);
                var remainingMs = (int)Math.Min(int.MaxValue, Math.Ceiling(remaining * 1000));
                host.Sleep(TimeSpan.FromMilliseconds(Math.Max(1, Math.Min(sleepMs, remainingMs))));
                if (host.Cancellation.IsCancellationRequested)
                    return new BuildSlotLease { Outcome = BuildSlotOutcome.Cancelled, WaitedSeconds = Whole(Elapsed()), Endpoint = endpoint, Position = position };
            }
        }

        /// <summary>Releases a granted lease (a no-op otherwise): stops its renewal, then DELETE; a failure is a line, never an error.</summary>
        public static void Exit(BuildSlotLease lease, BuildSlotHost host)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (lease == null || lease.Outcome != BuildSlotOutcome.Granted || string.IsNullOrWhiteSpace(lease.LeaseId)) return;
            lease.Renewal?.Stop();
            var held = Number(Whole(host.Clock.GetElapsedTime(lease.GrantedAt).TotalSeconds));
            var answer = Send(host, "DELETE", lease.Endpoint + "/" + lease.LeaseId, null);
            host.Out.WriteLine(answer.Status == 204
                ? "BUILD SLOT released lease=" + lease.LeaseId + " held=" + held + "s"
                : "BUILD SLOT release failed lease=" + lease.LeaseId + " held=" + held + "s status=" + Number(answer.Status) + "; the runner reaps it when this process exits");
        }

        /// <summary>The lease request: {"pid":N,"processStartUtc":"...","label":"...","sessionId":...,"taskId":...} (nulls kept).</summary>
        public static string RequestBody(int pid, DateTime? processStartUtc, string label, string sessionId, string taskId)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
            {
                writer.WriteStartObject();
                writer.WriteNumber("pid", pid);
                if (processStartUtc.HasValue) writer.WriteString("processStartUtc", processStartUtc.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture));
                else writer.WriteNull("processStartUtc");
                writer.WriteString("label", label ?? "");
                if (sessionId == null) writer.WriteNull("sessionId");
                else writer.WriteString("sessionId", sessionId);
                if (taskId == null) writer.WriteNull("taskId");
                else writer.WriteString("taskId", taskId);
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        /// <summary>One request; a transport failure is status 0 with no body (Invoke-AntiphonBuildSlotHttp).</summary>
        internal static NightlyReportHttpResponse Send(BuildSlotHost host, string method, string uri, string body)
        {
            try
            {
                return host.Http(method, uri, new Dictionary<string, string>(), body) ?? new NightlyReportHttpResponse(0, "");
            }
            catch (Exception e) when (e is HttpRequestException || e is OperationCanceledException || e is IOException || e is InvalidOperationException
                || e is UriFormatException || e is ArgumentException || e is System.Net.Sockets.SocketException)
            {
                return new NightlyReportHttpResponse(0, "");
            }
        }

        private static double? Override(string text) =>
            !string.IsNullOrWhiteSpace(text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && !double.IsNaN(value) && !double.IsInfinity(value)
                ? value
                : null;

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value;

        // PowerShell's [int] of a double: the nearest whole number, halves to even.
        private static int Whole(double seconds) => (int)Math.Round(seconds, MidpointRounding.ToEven);

        private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

        private static JsonElement? Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var document = JsonDocument.Parse(text);
                return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : (JsonElement?)null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string Text(JsonElement? json, string name)
        {
            if (!json.HasValue || !json.Value.TryGetProperty(name, out var value)) return "";
            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? "",
                JsonValueKind.Null or JsonValueKind.Undefined => "",
                _ => value.GetRawText(),
            };
        }

        private static long Long(JsonElement? json, string name)
        {
            if (!json.HasValue || !json.Value.TryGetProperty(name, out var value)) return 0;
            if (value.ValueKind == JsonValueKind.Number)
            {
                if (value.TryGetInt64(out var whole)) return whole;
                if (value.TryGetDouble(out var real)) return (long)Math.Round(real, MidpointRounding.ToEven);
            }
            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)) return parsed;
            return 0;
        }

        private static int Int(JsonElement? json, string name) => (int)Math.Max(int.MinValue, Math.Min(int.MaxValue, Long(json, name)));
    }

    /// <summary>A renew-mode lease's renewal: POST &lt;lease&gt;/renew every interval until stopped or the broker answers 404.</summary>
    internal sealed class BuildSlotRenewal
    {
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private readonly Thread _thread;

        private BuildSlotRenewal(BuildSlotHost host, string uri, int everySeconds)
        {
            var token = _stop.Token;
            _thread = new Thread(() =>
            {
                try
                {
                    while (host.RenewWait(TimeSpan.FromSeconds(everySeconds), token))
                    {
                        if (BuildSlotBroker.Send(host, "POST", uri, null).Status == 404) break;
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Stopped while a renewal was in flight: the release follows.
                }
            })
            { IsBackground = true, Name = "build-slot renewal" };
        }

        internal static BuildSlotRenewal Start(BuildSlotHost host, string uri, int everySeconds)
        {
            var renewal = new BuildSlotRenewal(host, uri, everySeconds);
            renewal._thread.Start();
            return renewal;
        }

        internal void Stop()
        {
            _stop.Cancel();
            // A renewal still in flight (10 s at most) keeps its token: dispose only a finished thread's.
            if (_thread.Join(TimeSpan.FromSeconds(15))) _stop.Dispose();
        }
    }

    /// <summary>The build-slot command (build-slot.ps1's body).</summary>
    public static class BuildSlot
    {
        /// <summary>No slot came within the wait; nothing ran.</summary>
        public const int SlotTimeout = 4;

        /// <summary>Interrupted (Ctrl+C or a termination signal): the child's tree was killed, the lease released.</summary>
        public const int Interrupted = 130;

        /// <summary>
        /// Runs the command under a slot (see the file header) and answers with its exit code; 4 when no slot came, 1 when the
        /// command cannot be resolved or started, 130 when interrupted. A missing command is a NightlyUsageException.
        /// </summary>
        public static int Run(BuildSlotOptions options, BuildSlotHost host)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            IReadOnlyList<string> command = (options.Command ?? Array.Empty<string>()).Select(token => token ?? "").ToList();
            if (command.Count == 0) throw new NightlyUsageException("missing command: build-slot --label <what> -- <command...>");
            var label = string.IsNullOrWhiteSpace(options.Label) ? string.Join(" ", command.Take(2)) : options.Label;

            BuildSlotLease lease = null;
            string exported = null;
            if (options.NoSlot)
            {
                host.Out.WriteLine("BUILD SLOT skipped by -NoSlot");
            }
            else
            {
                lease = BuildSlotBroker.Enter(label, options, host);
                if (lease.Outcome == BuildSlotOutcome.Timeout) return SlotTimeout;
                if (lease.Outcome == BuildSlotOutcome.Cancelled) return Interrupted;
                command = BuildSlotCommandLine.AddMaxCpuCount(command, lease.MaxCpuCount);
                if (BuildSlotCommandLine.IsDotnetRunWithBuild(command)) host.Out.WriteLine(BuildSlotCommandLine.DotnetRunNote(lease.MaxCpuCount));
                var warning = BuildSlotCommandLine.CapWarning(command, lease.MaxCpuCount);
                if (warning != null)
                {
                    host.Out.WriteLine(warning);
                    exported = lease.MaxCpuCount.ToString(CultureInfo.InvariantCulture);
                }
            }

            BuildSlotTemp temp = null;
            try
            {
                if (string.Equals(command[0], "dotnet", StringComparison.OrdinalIgnoreCase)) temp = host.LeaseTemp?.Invoke();
                var environment = new Dictionary<string, string>(host.Windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
                foreach (var pair in options.Environment ?? new Dictionary<string, string>()) environment[pair.Key] = pair.Value;
                // CARD-1627: the grant for a non-dotnet command.
                if (exported != null) environment["ANTIPHON_BUILD_SLOT_MAXCPUCOUNT"] = exported;
                // CARD-1981: the SDK's scratch directory lands under the lease's own TMPDIR and goes with it.
                if (temp != null) environment["TMPDIR"] = temp.DirectoryPath;
                var target = BuildSlotTarget.Resolve(command, options.WorkingDirectory, host.Variables, host.Windows, host.Files);
                if (target == null)
                {
                    host.Error.WriteLine("build-slot: '" + command[0] + "' is not a command, script or executable on PATH");
                    return 1;
                }
                // CARD-1048: Windows PowerShell rebuilds its own module path instead of inheriting PowerShell 7's.
                if (host.Windows && string.Equals(BuildSlotCommandLine.Leaf(target.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase))
                    environment["PSModulePath"] = null;
                var child = new BuildSlotChild(target.FileName, target.Arguments, options.WorkingDirectory ?? "", environment, lease?.MaxCpuCount ?? 0);
                try
                {
                    return host.Children.Run(child);
                }
                catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException)
                {
                    host.Error.WriteLine("build-slot: cannot start " + target.FileName + ": " + e.Message);
                    return 1;
                }
            }
            finally
            {
                if (temp?.Handle != null)
                {
                    try { temp.Handle.Dispose(); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
                BuildSlotBroker.Exit(lease, host);
            }
        }

        /// <summary>
        /// The production runner for in-process callers: each call runs on a production host (BuildSlotHost.Production) with
        /// the platform; capture null writes to output and error and shares the console, otherwise everything goes to capture.
        /// </summary>
        public static BuildSlotRunner Runner(BuildSlotPlatform platform, TextWriter output, TextWriter error, CancellationToken cancellation = default)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (error == null) throw new ArgumentNullException(nameof(error));
            return (options, capture) =>
            {
                var sink = capture == null ? null : TextWriter.Synchronized(capture);
                return Run(options, BuildSlotHost.Production(sink ?? output, sink ?? error, platform, cancellation, sink));
            };
        }
    }
}
