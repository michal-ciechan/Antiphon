// W2-nightly S01: the launcher seam of the nightly C# (plan 2026-11-21-w2-nightly-port-slices.md, section 2). A
// NightlyLaunch is one child process; NightlyHost.Launcher runs it. WatchedLauncher is the production launcher for
// Watched custody (the hops), the twin of Start-NightlyProcess (nightly-common.ps1) as Invoke-NightlyWatchedCommand
// calls it: Start-Process -NoNewWindow -Wait -PassThru, so the child shares this console, stdin, stdout and stderr, and
// the call returns its exit code once it has exited. Owned custody (a private job, the log and the drain of
// nightly-owned-process.cs) is another launcher. Tests record launches with RecordingLauncher
// (tests/Antiphon.Tests/Scripts/Nightly). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;

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
    /// swap). TimeoutMilliseconds 0 or less: wait for the exit, as every hop does today. LogPath is Owned custody's
    /// output log; a Watched child writes to this process's own streams.
    /// </summary>
    public sealed record NightlyLaunch(
        string FileName,
        IReadOnlyList<string> Arguments,
        string WorkingDirectory,
        int TimeoutMilliseconds = 0,
        IReadOnlyDictionary<string, string> Environment = null,
        string LogPath = null,
        NightlyCustody Custody = NightlyCustody.Watched)
    {
        /// <summary>FileName and the arguments, space separated: the text of the exec trace line (Invoke-NightlyWatchedCommand's Write-NightlyTrace).</summary>
        public string CommandText => FileName + " " + string.Join(" ", Arguments ?? Array.Empty<string>());
    }

    /// <summary>What a launch observed. Owned custody fills ChildrenExited and LogText; a Watched launch leaves them unset.</summary>
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

        /// <summary>Owned custody: the child's logged output (a fake launcher's stand-in for the log file).</summary>
        public string LogText { get; set; } = "";
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
        /// WorkingDirectory (empty: this process's), with no new window and no redirection, and waits for it. A positive
        /// TimeoutMilliseconds kills the child's tree when it expires (TimedOut, exit 1). Refuses Owned custody and a
        /// LogPath, which this launcher cannot honour. A child that cannot start throws, as Start-Process does.
        /// </summary>
        public NightlyLaunchResult Run(NightlyLaunch launch)
        {
            if (launch == null) throw new ArgumentNullException(nameof(launch));
            if (launch.Custody != NightlyCustody.Watched)
                throw new NotSupportedException("WatchedLauncher runs Watched custody only; Owned custody needs the native owner launcher.");
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
            using var process = Process.Start(start) ?? throw new InvalidOperationException("the child did not start: " + launch.FileName);
            var result = new NightlyLaunchResult { Pid = process.Id };
            if (launch.TimeoutMilliseconds > 0 && !process.WaitForExit(launch.TimeoutMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                process.WaitForExit();
                result.TimedOut = true;
                result.ExitCode = 1;
                return result;
            }
            process.WaitForExit();
            result.ExitCode = process.ExitCode;
            return result;
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
}
