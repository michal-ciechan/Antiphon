// W2-nightly S01: the launcher seam of the nightly C# (plan 2026-11-21-w2-nightly-port-slices.md, section 2). A
// NightlyLaunch is one child process; NightlyHost.Launcher runs it. WatchedLauncher is the production launcher for
// Watched custody (the hops), the twin of Start-NightlyProcess (nightly-common.ps1) as Invoke-NightlyWatchedCommand
// calls it: Start-Process -NoNewWindow -Wait -PassThru, so the child shares this console, stdin, stdout and stderr, and
// the call returns its exit code once it has exited. W2-nightly S04: OwnedLauncher runs Owned custody (a private job,
// the log and the drain of NativeProcessOwner), the twin of Invoke-NightlyOwnedProcess over Start-NightlyNativeOwner.
// Tests record launches with RecordingLauncher (tests/Antiphon.Tests/Scripts/Nightly). Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>Who answers for a launched child.</summary>
    public enum NightlyCustody
    {
        /// <summary>A hop: the child is started and waited for; its descendants are not tracked.</summary>
        Watched,

        /// <summary>A test or build step under native job custody: every descendant is joined and the output is logged and drained.</summary>
        Owned,
    }

    /// <summary>
    /// One child process. Environment null: the child inherits this process's environment. Otherwise the child's
    /// environment is exactly Environment, and a name whose value is null or empty is left out (Start-NightlyProcess's
    /// swap). TimeoutMilliseconds 0 or less: wait for the exit, as every hop does today (Owned custody needs a positive
    /// one). LogPath is Owned custody's output log; a Watched child writes to this process's own streams. Hooks are Owned
    /// custody's NativeProcessHooks (the TRX and diagnostic of the completion grace, the hang capture path, the test
    /// barriers); null takes the defaults. A Watched launch ignores them. CaptureOutput (Watched custody, unit 10 of
    /// 2026-11-22-remaining-ps1-to-csharp.md): the child's stdout and stderr are read into the result's LogText instead
    /// of shared with this process (a PowerShell "&amp; tool 2&gt;&amp;1" capture), so the caller decides what is printed.
    /// </summary>
    public sealed record NightlyLaunch(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        int TimeoutMilliseconds = 0,
        IReadOnlyDictionary<string, string> Environment = null,
        string LogPath = null,
        NightlyCustody Custody = NightlyCustody.Watched,
        NativeProcessHooks Hooks = null,
        bool CaptureOutput = false)
    {
        /// <summary>FileName and the arguments, space separated: the text of the exec trace line (Invoke-NightlyWatchedCommand's Write-NightlyTrace).</summary>
        public string CommandText => FileName + " " + string.Join(" ", Arguments ?? Array.Empty<string>());
    }

    /// <summary>What a launch observed. Owned custody fills ChildrenExited, CleanupComplete and Owner (a fake fills LogText); a Watched launch leaves them unset.</summary>
    public sealed class NightlyLaunchResult
    {
        /// <summary>The child's exit code; 1 when it timed out.</summary>
        public int ExitCode { get; set; } = 1;

        /// <summary>True when the launch's timeout ended the child (its tree was killed).</summary>
        public bool TimedOut { get; set; }

        /// <summary>The child's process id; 0 when it did not start.</summary>
        public int Pid { get; set; }

        /// <summary>Owned custody: every descendant was observed to exit.</summary>
        public bool ChildrenExited { get; set; }

        /// <summary>
        /// Owned custody: the child's logged output (a fake launcher's stand-in for the log file). A Watched launch with
        /// CaptureOutput: its stdout and stderr lines in arrival order, each followed by "\n".
        /// </summary>
        public string LogText { get; set; } = "";

        /// <summary>
        /// A Watched launch with CaptureOutput: its stdout lines alone, each followed by "\n" (unit 11 of
        /// 2026-11-22-remaining-ps1-to-csharp.md: the land-graph commands read a tool's stdout apart from its stderr).
        /// </summary>
        public string StandardOutputText { get; set; } = "";

        /// <summary>A Watched launch with CaptureOutput: its stderr lines alone, each followed by "\n".</summary>
        public string StandardErrorText { get; set; } = "";

        /// <summary>
        /// Owned custody: every descendant exited and the output drained (NativeProcessResult.CleanupComplete); null when
        /// the launcher did not observe it (a fake standing in for the child, as the controlled-I/O seam did).
        /// </summary>
        public bool? CleanupComplete { get; set; }

        /// <summary>Owned custody under the native owner: its whole observation (timings, descendants, hang capture); null otherwise.</summary>
        public NativeProcessResult Owner { get; set; }

        /// <summary>
        /// W2-nightly S12: the object a delegate launcher answered with (a fixture's StartProcess record), whose fields are
        /// read by name as the chunk runner read them; null for the native owner and for a launcher answering with this.
        /// </summary>
        public object Record { get; set; }

        /// <summary>
        /// What a launch observed, as a reader of it sees it: the native owner's observation, else the launcher's record,
        /// else this result itself (the object Invoke-NightlyOwnedProcess returned to its caller).
        /// </summary>
        public object Observed => (object)Owner ?? Record ?? this;

        /// <summary>
        /// A launcher's record read as a result (Invoke-NightlyOwnedProcess over the StartProcess seam): ExitCode (absent:
        /// 1), TimedOut, Pid, ChildrenExited, LogText and CleanupComplete (absent: null) by name, the record kept as Record.
        /// </summary>
        public static NightlyLaunchResult FromRecord(object record)
        {
            if (record is NightlyLaunchResult result) return result;
            var exitCode = PsValue.Prop(record, "ExitCode");
            var pid = PsValue.Prop(record, "Pid");
            var cleanup = PsValue.Prop(record, "CleanupComplete");
            return new NightlyLaunchResult
            {
                ExitCode = exitCode == null ? 1 : PsOps.ToInt(exitCode),
                TimedOut = PsValue.IsTrue(PsValue.Prop(record, "TimedOut")),
                Pid = pid == null ? 0 : PsOps.ToInt(pid),
                ChildrenExited = PsValue.IsTrue(PsValue.Prop(record, "ChildrenExited")),
                LogText = PsValue.Str(PsValue.Prop(record, "LogText")),
                CleanupComplete = cleanup == null ? (bool?)null : PsValue.IsTrue(cleanup),
                Record = record,
            };
        }
    }

    /// <summary>Starts children for a run.</summary>
    public interface INightlyLauncher
    {
        /// <summary>Starts the launch and waits for its end (exit or timeout).</summary>
        NightlyLaunchResult Run(NightlyLaunch launch);

        /// <summary>Wait-NightlyOwnedCleanup: true once nothing the launch started is left to clean up.</summary>
        bool WaitCleanup(NightlyLaunchResult result);

        /// <summary>Get-NightlyNativeActiveCount: the owned launches still running.</summary>
        int NativeActiveCount { get; }
    }

    /// <summary>The production launcher for Watched custody: Start-NightlyProcess with -NoNewWindow -Wait -PassThru.</summary>
    public sealed class WatchedLauncher : INightlyLauncher
    {
        private readonly Action _beforeStart;

        /// <summary>
        /// beforeStart runs before every start (null: nothing). Start-NightlyProcess first lands the queued evidence
        /// writes, because a child reads the disk; NightlyHost.Production passes that flush.
        /// </summary>
        public WatchedLauncher(Action beforeStart = null)
        {
            _beforeStart = beforeStart;
        }

        /// <summary>
        /// Starts FileName (resolved on PATH when it has no directory) with the arguments as separate argv entries in
        /// WorkingDirectory (empty: this process's), with no new window, and waits for it. Without CaptureOutput nothing is
        /// redirected; with it stdout and stderr are read as UTF-8 into LogText (stdin stays this process's). A positive
        /// TimeoutMilliseconds kills the child's tree when it expires (TimedOut, exit 1). Refuses Owned custody and a
        /// LogPath, which this launcher cannot honour. A child that cannot start throws, as Start-Process does.
        /// </summary>
        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            if (launch.Custody != NightlyCustody.Watched)
                throw new NotSupportedException("WatchedLauncher runs Watched custody only; Owned custody runs on OwnedLauncher.");
            if (!string.IsNullOrEmpty(launch.LogPath))
                throw new NotSupportedException("A Watched child writes to this process's own streams; LogPath is Owned custody's.");
            _beforeStart?.Invoke();
            var start = new ProcessStartInfo(launch.FileName)
            {
                UseShellExecute = false,
                WorkingDirectory = launch.WorkingDirectory ?? "",
            };
            foreach (var argument in launch.Arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument ?? "");
            ApplyEnvironment(start, launch.Environment);
            var lines = new List<string>();
            var outputLines = new List<string>();
            var errorLines = new List<string>();
            if (launch.CaptureOutput)
            {
                start.RedirectStandardOutput = true;
                start.RedirectStandardError = true;
                start.StandardOutputEncoding = Utf8NoBom;
                start.StandardErrorEncoding = Utf8NoBom;
            }
            using var process = new Process { StartInfo = start };
            if (launch.CaptureOutput)
            {
                void Line(List<string> stream, string data)
                {
                    if (data == null) return;
                    lock (lines)
                    {
                        lines.Add(data);
                        stream.Add(data);
                    }
                }
                process.OutputDataReceived += (sender, e) => Line(outputLines, e.Data);
                process.ErrorDataReceived += (sender, e) => Line(errorLines, e.Data);
            }
            if (!process.Start()) throw new InvalidOperationException("the child did not start: " + launch.FileName);
            if (launch.CaptureOutput)
            {
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            var result = new NightlyLaunchResult { Pid = process.Id };
            if (launch.TimeoutMilliseconds > 0 && !process.WaitForExit(launch.TimeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                process.WaitForExit();
                result.TimedOut = true;
                result.ExitCode = 1;
                Captured(result, lines, outputLines, errorLines);
                return result;
            }
            // Without a timeout WaitForExit also waits for redirected streams to reach their end.
            process.WaitForExit();
            result.ExitCode = process.ExitCode;
            Captured(result, lines, outputLines, errorLines);
            return result;
        }

        private static readonly System.Text.UTF8Encoding Utf8NoBom = new System.Text.UTF8Encoding(false);

        private static void Captured(NightlyLaunchResult result, List<string> lines, List<string> outputLines, List<string> errorLines)
        {
            static string Text(List<string> stream) => stream.Count == 0 ? "" : string.Join("\n", stream) + "\n";
            lock (lines)
            {
                result.LogText = Text(lines);
                result.StandardOutputText = Text(outputLines);
                result.StandardErrorText = Text(errorLines);
            }
        }

        /// <summary>A watched child has been waited for; its descendants are not tracked, so nothing is left to wait on.</summary>
        public bool WaitCleanup(NightlyLaunchResult result) => true;

        /// <summary>Always 0: this launcher owns no native launches.</summary>
        public int NativeActiveCount => 0;

        /// <summary>
        /// Start-NightlyProcess's swap, applied to the child alone: with a map, the child's environment is exactly the
        /// map's non-empty values; without one it is this process's environment. This process's own block never changes.
        /// </summary>
        public static void ApplyEnvironment(ProcessStartInfo start, IReadOnlyDictionary<string, string> environment)
        {
            if (start == null) throw new ArgumentNullException(nameof(start));
            if (environment == null) return;
            start.Environment.Clear();
            foreach (var pair in environment)
            {
                if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value)) continue;
                start.Environment[pair.Key] = pair.Value;
            }
        }
    }

    /// <summary>
    /// The production launcher for Owned custody: Invoke-NightlyOwnedProcess (nightly-tests-impl.ps1) over
    /// Start-NightlyNativeOwner and New-NightlyChildStartInfo (nightly-owned-process.ps1, deleted in W2 slice S21), without their seams. The child
    /// runs under NativeProcessOwner: created suspended, assigned to a private kill-on-close job that forbids breakaway, then
    /// resumed; its stdout and stderr drain into LogPath; the call returns once the root and every descendant have exited
    /// (or the timeout or the completion grace killed the job) and the output has drained. Windows only.
    /// </summary>
    public sealed class OwnedLauncher : INightlyLauncher
    {
        private static readonly Regex WindowsApps = new Regex(@"(?i)[\\/]WindowsApps[\\/]");
        private readonly Action _beforeStart;
        private readonly Action<ProcessStartInfo> _prepareChild;
        private int _active;

        /// <summary>
        /// beforeStart runs before every start (Sync-NightlyDeferredEvidenceWrites: a child reads the disk). prepareChild
        /// adjusts the child's start information before its environment block is taken (Set-AntiphonBuildDriverEnvironment;
        /// the caller supplies BuildDriverEnvironment, which is not part of the core). Null: nothing.
        /// </summary>
        public OwnedLauncher(Action beforeStart = null, Action<ProcessStartInfo> prepareChild = null)
        {
            _beforeStart = beforeStart;
            _prepareChild = prepareChild;
        }

        /// <summary>
        /// Invoke-NightlyOwnedProcess: Start, then wait for the owner's verdict. The result carries the whole observation
        /// as Owner and its CleanupComplete (every descendant exited and the output drained). A positive
        /// TimeoutMilliseconds is required (NativeProcessOwner refuses another).
        /// </summary>
        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            Interlocked.Increment(ref _active);
            try
            {
                var observation = Start(launch).Completion.GetAwaiter().GetResult();
                return new NightlyLaunchResult
                {
                    ExitCode = observation.ExitCode,
                    TimedOut = observation.TimedOut,
                    Pid = observation.Pid,
                    ChildrenExited = observation.ChildrenExited,
                    CleanupComplete = observation.CleanupComplete,
                    Owner = observation,
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        /// <summary>
        /// Start-NightlyNativeOwner: refuses Watched custody and a host other than Windows, runs beforeStart, resolves
        /// FileName as an application (ResolveExecutable), builds the child's start information (ChildStartInfo), lets
        /// prepareChild adjust it, and starts the native owner with that environment block and launch.Hooks (null: the
        /// defaults). Returns the running owner; its Completion is the verdict.
        /// </summary>
        public NativeProcessOwner Start(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            if (launch.Custody != NightlyCustody.Owned)
                throw new NotSupportedException("OwnedLauncher runs Owned custody only; a Watched hop runs on WatchedLauncher.");
            _beforeStart?.Invoke();
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Nightly native custody requires Windows.");
            var executable = ResolveExecutable(launch.FileName);
            var arguments = (launch.Arguments ?? Array.Empty<string>()).Select(argument => argument ?? "").ToArray();
            var start = ChildStartInfo(executable, arguments, launch.WorkingDirectory, launch.Environment);
            _prepareChild?.Invoke(start);
            var entries = start.Environment.Select(pair => pair.Key + "=" + pair.Value).ToArray();
            return NativeProcessOwner.Start(executable, arguments, launch.WorkingDirectory, launch.TimeoutMilliseconds, launch.LogPath, entries, launch.Hooks);
        }

        /// <summary>Wait-NightlyOwnedCleanup without seams: the owner observed every descendant exit and the output drain.</summary>
        public bool WaitCleanup(NightlyLaunchResult result) => result?.CleanupComplete == true;

        /// <summary>The Run calls of this launcher still waiting on their owner.</summary>
        public int NativeActiveCount => Volatile.Read(ref _active);

        /// <summary>
        /// New-NightlyChildStartInfo: FileName and the arguments as separate argv entries, WorkingDirectory when it is not
        /// blank; with an environment map the child's environment is exactly the map (a null value is empty, which the
        /// native owner leaves out of the block), without one this process's.
        /// </summary>
        public static ProcessStartInfo ChildStartInfo(string fileName, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string> environment)
        {
            var start = new ProcessStartInfo();
            if (!string.IsNullOrWhiteSpace(fileName)) start.FileName = fileName;
            foreach (var argument in arguments ?? Array.Empty<string>()) start.ArgumentList.Add(argument ?? "");
            if (!string.IsNullOrWhiteSpace(workingDirectory)) start.WorkingDirectory = workingDirectory;
            if (environment != null)
            {
                start.Environment.Clear();
                foreach (var pair in environment) start.Environment[pair.Key] = pair.Value ?? "";
            }
            return start;
        }

        /// <summary>
        /// Start-NightlyNativeOwner's executable: the application FindApplication names; a packaged PowerShell (pwsh.exe
        /// under a WindowsApps directory, which can launch unpackaged children outside the private job) becomes Program
        /// Files\PowerShell\7\pwsh.exe, which must exist.
        /// </summary>
        public static string ResolveExecutable(string fileName)
        {
            var executable = FindApplication(fileName) ?? throw new FileNotFoundException("The term '" + fileName + "' is not recognized as the name of an application.", fileName);
            if (string.Equals(Path.GetFileName(executable), "pwsh.exe", StringComparison.OrdinalIgnoreCase) && WindowsApps.IsMatch(executable))
            {
                executable = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), "PowerShell", "7", "pwsh.exe");
                if (!File.Exists(executable)) throw new InvalidOperationException("Native process custody requires unpackaged PowerShell 7 in Program Files\\PowerShell\\7.");
            }
            return executable;
        }

        /// <summary>
        /// Get-Command -CommandType Application's first answer: a name with a directory is that file (or that file with a
        /// PATHEXT extension); a bare name is looked up in each PATH directory in order, as given and then with each
        /// PATHEXT extension. Null when there is none.
        /// </summary>
        public static string FindApplication(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            var extensions = (System.Environment.GetEnvironmentVariable("PATHEXT") ?? ".COM;.EXE;.BAT;.CMD")
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            IEnumerable<string> Candidates(string path)
            {
                if (Path.HasExtension(path)) yield return path;
                foreach (var extension in extensions) yield return path + extension;
            }
            if (Path.IsPathRooted(name) || name.IndexOfAny(new[] { '\\', '/' }) >= 0)
                return Candidates(Path.GetFullPath(name)).FirstOrDefault(File.Exists);
            foreach (var directory in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                var trimmed = directory.Trim().Trim('"');
                if (trimmed.Length == 0) continue;
                string root;
                try { root = Path.GetFullPath(trimmed); }
                catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException) { continue; }
                var found = Candidates(Path.Combine(root, name)).FirstOrDefault(File.Exists);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>
    /// W2-nightly S17: one launcher for both custodies, as a production run needs them: a Watched launch (a hop) runs on
    /// the watched launcher, an Owned one (a build, a test host, a discovery) on the owned launcher. Cleanup is waited for
    /// on the owned launcher when the result carries the native owner's observation, else on the watched one.
    /// </summary>
    public sealed class CustodyLauncher : INightlyLauncher
    {
        private readonly INightlyLauncher _watched;
        private readonly INightlyLauncher _owned;

        /// <summary>watched runs Watched custody, owned runs Owned custody; both are required.</summary>
        public CustodyLauncher(INightlyLauncher watched, INightlyLauncher owned)
        {
            _watched = watched ?? throw new ArgumentNullException(nameof(watched));
            _owned = owned ?? throw new ArgumentNullException(nameof(owned));
        }

        /// <inheritdoc />
        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            return launch.Custody == NightlyCustody.Owned ? _owned.Run(launch) : _watched.Run(launch);
        }

        /// <inheritdoc />
        public bool WaitCleanup(NightlyLaunchResult result) =>
            result != null && result.Owner != null ? _owned.WaitCleanup(result) : _watched.WaitCleanup(result);

        /// <inheritdoc />
        public int NativeActiveCount => _owned.NativeActiveCount;
    }
}
