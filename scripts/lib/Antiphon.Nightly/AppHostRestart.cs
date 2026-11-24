// CARD-0011, CARD-0075, CARD-0273, CARD-0310, CARD-0716, CARD-1527, CARD-1533, CARD-1680, CARD-1806: kill and restart the
// whole desktop Aspire stack (AppHost, server, client, dashboard), then wait for it to come back healthy with the intended
// build loaded (unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced the body of
// scripts/restart-apphost.ps1, which stays a thin launcher because scripts/cutover-postgres-server2.ps1 and
// scripts/deploy-local.ps1 start it by path). The CLI runs it as
//
//   antiphon-scripts restart-apphost [--no-build] [--timeout-sec 150] [--max-wait-sec 600] [--lock-max-age-minutes 15]
//     [--allow-worktree] [--expected-server-sha <sha>] [--graceful-stop-timeout-sec 20] [--skip-graceful-stop] [--stop-only]
//     [--what-if]
//
// dev-aspire launches a new AppHost but does not stop an old one, so this command tears down first and then starts the
// dev-aspire command detached (this CLI's run copy; unit 37 ported dev-aspire.ps1). The session runner on 17204 is preserved
// (it survives AppHost restarts; the new AppHost adopts it). It is the only actor that kills: it takes
// logs/apphost.restart.lock for the whole run and refuses (exit 3, nothing killed) while that lock or logs/apphost.launch.lock
// is held (CARD-0075). A no-progress timeout (exit 1) or a DCP timeout (exit 4)
// leaves the restart lock on disk for LockMaxAgeMinutes, because the spawned launch may still be coming up (CARD-0310);
// exit 0, a failed build and a dead launch remove it.
//
// Exit codes: 0 restarted, dashboard up, /health 200 and /api/version naming the intended HEAD (also --stop-only finished);
// 1 failed (build failed, launcher or AppHost exited, no progress for --timeout-sec, or --max-wait-sec reached); 3 refused (a
// linked or unverifiable worktree, a phone-home configuration, SHA admission, a launch or restart in flight, --what-if
// without --stop-only; nothing was killed); 4 Aspire's DCP dependency check timed out; 5 server build unverified (health
// succeeded but /api/version did not name the intended SHA; the lock is kept and the child left running). Usage 2.
//
// Every observation of the machine is IAppHostPlatform's; the clock, the sleep and git are the host's. Plain C#: no
// PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The restart-apphost command's options (restart-apphost.ps1's parameters in kebab case).</summary>
    public sealed class AppHostRestartOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "restart-apphost [--no-build] [--timeout-sec 150] [--max-wait-sec 600] [--lock-max-age-minutes 15] [--allow-worktree] "
            + "[--expected-server-sha <sha>] [--graceful-stop-timeout-sec 20] [--skip-graceful-stop] [--stop-only] [--what-if]";

        private static readonly string[] Valued = { "--timeout-sec", "--max-wait-sec", "--lock-max-age-minutes", "--expected-server-sha", "--graceful-stop-timeout-sec" };
        private static readonly string[] Switches = { "--no-build", "--allow-worktree", "--skip-graceful-stop", "--stop-only", "--what-if" };

        /// <summary>Pass --no-build to dev-aspire (skip restore and npm).</summary>
        public bool NoBuild { get; init; }

        /// <summary>The no-progress window of the health wait, in seconds (CARD-1680).</summary>
        public int TimeoutSec { get; init; } = 150;

        /// <summary>The hard cap of the whole health wait (never below TimeoutSec).</summary>
        public int MaxWaitSec { get; init; } = 600;

        /// <summary>A lock older than this is ignored.</summary>
        public int LockMaxAgeMinutes { get; init; } = AppHost.LockMaxAgeMinutes;

        /// <summary>Allow a linked worktree to control the shared local stack.</summary>
        public bool AllowWorktree { get; init; }

        /// <summary>The full SHA that must equal the checkout's HEAD; empty: that HEAD.</summary>
        public string ExpectedServerSha { get; init; } = "";

        /// <summary>Seconds to wait for the server pid after a 202 from POST /api/operator/shutdown.</summary>
        public int GracefulStopTimeoutSec { get; init; } = 20;

        /// <summary>Skip the graceful POST and go straight to the force-kill.</summary>
        public bool SkipGracefulStop { get; init; }

        /// <summary>Stop and free the AppHost ports, then exit before dev-aspire (Postgres and 17204 untouched).</summary>
        public bool StopOnly { get; init; }

        /// <summary>With StopOnly: print the stop and change nothing. Without it: refuse.</summary>
        public bool WhatIf { get; init; }

        /// <summary>The checkout whose stack restarts (its logs, its AppHost and HEAD).</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static AppHostRestartOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, Switches);
            return new AppHostRestartOptions
            {
                NoBuild = flags.Switch("--no-build"),
                TimeoutSec = flags.Int("--timeout-sec", 150),
                MaxWaitSec = flags.Int("--max-wait-sec", 600),
                LockMaxAgeMinutes = flags.Int("--lock-max-age-minutes", AppHost.LockMaxAgeMinutes),
                AllowWorktree = flags.Switch("--allow-worktree"),
                ExpectedServerSha = flags.Value("--expected-server-sha", "") ?? "",
                GracefulStopTimeoutSec = flags.Int("--graceful-stop-timeout-sec", 20),
                SkipGracefulStop = flags.Switch("--skip-graceful-stop"),
                StopOnly = flags.Switch("--stop-only"),
                WhatIf = flags.Switch("--what-if"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The desktop AppHost restart (restart-apphost.ps1's body).</summary>
    public static class AppHostRestart
    {
        private static readonly Regex Url = new Regex("^https?://", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>Restarts the stack; the exit code (see the file comment). An unexpected failure is one ERROR line and exit 1.</summary>
        public static int Run(AppHostRestartOptions options, NightlyHost host, IAppHostPlatform platform)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            var restartBegan = host.UtcNow;
            void Say(string line) => host.Out.WriteLine(line);

            var worktree = AppHostWorktree.Classify(host, files, root);
            if (!worktree.Verified || (!worktree.IsMainWorktree && !options.AllowWorktree))
            {
                foreach (var line in worktree.GuardMessage(false)) Say(line);
                return 3;
            }
            if (!worktree.IsMainWorktree) foreach (var line in worktree.GuardMessage(true)) Say(line);

            if (!options.StopOnly)
            {
                var refusal = WindowsPhoneHome.LaunchRefusal(files.File.Exists(platform.PhoneHomeConfigPath), platform.PhoneHomeConfigPath);
                if (refusal != null)
                {
                    Say(refusal);
                    return 3;
                }
            }

            var logDir = AppHost.LogDir(files, root);
            var pidFile = files.Path.Combine(logDir, "apphost.pid");
            var urlFile = files.Path.Combine(logDir, "apphost-dashboard-url.txt");
            var logFile = files.Path.Combine(logDir, "apphost.log");
            var launchLock = files.Path.Combine(logDir, "apphost.launch.lock");
            var restartLock = files.Path.Combine(logDir, "apphost.restart.lock");
            var watchdogLog = files.Path.Combine(logDir, "watchdog-apphost.log");
            var portList = string.Join(",", AppHost.OwnedPorts.Select(port => port.ToString(CultureInfo.InvariantCulture)));

            if (options.WhatIf && !options.StopOnly)
            {
                Say("REFUSED: --what-if is only valid with --stop-only. A restart was not started.");
                Say("  Nothing was killed.");
                return 3;
            }
            if (options.StopOnly && options.WhatIf)
            {
                string launch = null, restart = null;
                if (files.Directory.Exists(logDir))
                {
                    launch = AppHostLocks.Active(platform, launchLock, options.LockMaxAgeMinutes, "launch lock", host.UtcNow);
                    restart = AppHostLocks.Active(platform, restartLock, options.LockMaxAgeMinutes, "restart lock", host.UtcNow);
                }
                if (launch != null || restart != null)
                {
                    Say("REFUSED: a launch or restart is in flight - " + (launch ?? restart));
                    Say("  Nothing was killed.");
                    return 3;
                }
                Say("STOP-ONLY WHATIF: would POST /api/operator/shutdown, free AppHost ports, and exit before dev-aspire.");
                Say("  ports: " + portList);
                Say("  preserving session-runner port " + AppHost.SessionRunnerPort.ToString(CultureInfo.InvariantCulture) + " and antiphon-postgres.");
                Say("  Nothing was stopped. Nothing was launched.");
                return 0;
            }

            Say("Restarting Antiphon AppHost...");
            // Refuse before any kill when anything else is launching or restarting: a refusal leaves the stack as it was.
            files.Directory.CreateDirectory(logDir);
            var launchInFlight = AppHostLocks.Active(platform, launchLock, options.LockMaxAgeMinutes, "launch lock", host.UtcNow);
            if (launchInFlight != null)
            {
                Say("REFUSED: a launch is in flight - " + launchInFlight);
                Say("  dev-aspire is still bringing the stack up. Killing it now would time out its");
                Say("  DCP dependency check (CARD-0075). Wait for it, then re-run if it did not come up.");
                Say("  Nothing was killed.");
                return 3;
            }

            var held = AppHostLocks.TakeRestart(platform, host, restartLock, root, options.LockMaxAgeMinutes);
            if (!held.Acquired)
            {
                Say("REFUSED: another restart is in flight - " + held.Reason);
                Say("  A second teardown over the top of the first is exactly what times out the DCP");
                Say("  dependency check (CARD-0075). Wait for it to finish, then check http://localhost:17202/health.");
                Say("  Nothing was killed. A dead holder is not a reason to delete " + restartLock + ".");
                Say("  Inspect both locks, launcher output, owned processes, listeners and health.");
                Say("  Early recovery needs the same lock identity on two watchdog fires within five minutes,");
                Say("  holder state Dead, launcher activity Absent, connection-refused health, and no launch lock.");
                Say("  Otherwise wait for LockMaxAgeMinutes. Unknown, live, slow, or replacement evidence does not qualify.");
                return 3;
            }

            // CARD-0310: keep the stamp on disk after this run when the spawned launch may still be launching; cleared on a
            // healthy exit 0 and on a failed build or dead launch (which also stop the child).
            var state = new RestartState();
            try
            {
                return Restart(options, host, platform, state, new RestartPaths(root, logDir, pidFile, urlFile, logFile, launchLock, restartLock, watchdogLog), restartBegan);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception
                || e is PlatformNotSupportedException || e is ArgumentException)
            {
                host.Error.WriteLine("ERROR: restart-apphost failed: " + e.Message);
                return 1;
            }
            finally
            {
                held.Release(state.KeepLock);
            }
        }

        private sealed class RestartState
        {
            public bool KeepLock;
        }

        private sealed record RestartPaths(string Root, string LogDir, string PidFile, string UrlFile, string LogFile, string LaunchLock,
            string RestartLock, string WatchdogLog);

        private static int Restart(AppHostRestartOptions options, NightlyHost host, IAppHostPlatform platform, RestartState state, RestartPaths paths, DateTime restartBegan)
        {
            var files = platform.Files;
            void Say(string line) => host.Out.WriteLine(line);
            var root = paths.Root;
            var expectedGiven = !string.IsNullOrWhiteSpace(options.ExpectedServerSha);

            var sourceHead = AppHostProbes.SourceHead(host, root);
            if (sourceHead == null)
            {
                Say("REFUSED: could not read a full HEAD SHA from source root " + root);
                if (expectedGiven) Say("  supplied --expected-server-sha: " + options.ExpectedServerSha);
                Say("  Update the canonical checkout, then re-run.");
                Say("  Nothing was killed.");
                return 3;
            }
            var expectedSha = sourceHead;
            if (expectedGiven)
            {
                if (!AppHostProbes.IsSha(options.ExpectedServerSha))
                {
                    Say("REFUSED: --expected-server-sha is not a full 40- or 64-character SHA: " + options.ExpectedServerSha);
                    Say("  source root: " + root);
                    Say("  HEAD: " + sourceHead);
                    Say("  Update the canonical checkout, then re-run.");
                    Say("  Nothing was killed.");
                    return 3;
                }
                if (!string.Equals(options.ExpectedServerSha.ToLowerInvariant(), sourceHead, StringComparison.Ordinal))
                {
                    Say("REFUSED: --expected-server-sha " + options.ExpectedServerSha + " does not match source-root HEAD " + sourceHead);
                    Say("  source root: " + root);
                    Say("  Update the canonical checkout, then re-run.");
                    Say("  Nothing was killed.");
                    return 3;
                }
                expectedSha = options.ExpectedServerSha.ToLowerInvariant();
            }
            if (AppHostProbes.TrackedEdits(host, root))
                Say("NOTE: source checkout has tracked edits; SHA equality does not prove uncommitted behavior is loaded. Probe the changed feature directly.");
            var indexLock = AppHostGitIndexLock.Read(platform, host, root);
            if (indexLock != null) Say(indexLock.Note());

            // Which process owns the session runner port, so it is never killed.
            var runnerPid = platform.PortOwners(AppHost.SessionRunnerPort).FirstOrDefault();
            if (runnerPid != 0) Say("  preserving session-runner (PID " + runnerPid.ToString(CultureInfo.InvariantCulture) + " on " + AppHost.SessionRunnerPort.ToString(CultureInfo.InvariantCulture) + ")");
            platform.CheckDaemonBuild(host);

            // Ask the server to drain and stop. Refusals never reach this; the wait is not charged to the health window. The
            // force-kill below still runs.
            if (!options.SkipGracefulStop)
            {
                var graceful = platform.GracefulStop("restart-apphost", 5);
                if (graceful.Class == "accepted" && graceful.Pid.HasValue)
                {
                    var exited = WaitForExit(host, platform, graceful.Pid.Value, options.GracefulStopTimeoutSec);
                    var pid = graceful.Pid.Value.ToString(CultureInfo.InvariantCulture);
                    Say(exited.HasValue
                        ? "  graceful stop accepted; server PID " + pid + " exited after " + exited.Value.ToString(CultureInfo.InvariantCulture) + "s"
                        : "  graceful stop accepted; server PID " + pid + " still alive after " + options.GracefulStopTimeoutSec.ToString(CultureInfo.InvariantCulture) + "s; forcing");
                }
                else
                {
                    Say("  graceful stop " + graceful.Class + ": " + graceful.Detail + "; forcing");
                }
            }

            // 1) The AppHost wrapper and the dotnet AppHost tree.
            var appHostPid = ReadPid(files, paths.PidFile);
            if (appHostPid.HasValue)
            {
                platform.StopTree(appHostPid.Value);
                Say("  killed AppHost wrapper tree (PID " + appHostPid.Value.ToString(CultureInfo.InvariantCulture) + ")");
            }

            // 2) Every AppHost-owned port (DCP children often escape the wrapper tree); never the session runner.
            host.Sleep(TimeSpan.FromSeconds(1));
            foreach (var port in AppHost.OwnedPorts)
                foreach (var owner in platform.PortOwners(port))
                {
                    if (owner == runnerPid) continue;
                    platform.StopProcess(owner);
                    Say("  freed port " + port.ToString(CultureInfo.InvariantCulture) + " (PID " + owner.ToString(CultureInfo.InvariantCulture) + ")");
                }

            // 3) Stale DCP and dashboard processes an unclean exit left behind.
            foreach (var stray in platform.Processes(new[] { "dcpctrl", "Aspire.Dashboard" }))
            {
                if (stray.Pid == runnerPid) continue;
                platform.StopProcess(stray.Pid);
                Say("  killed stale " + stray.Name + " (PID " + stray.Pid.ToString(CultureInfo.InvariantCulture) + ")");
            }

            var indexLockAfter = AppHostGitIndexLock.Read(platform, host, root);
            if (indexLockAfter != null && indexLockAfter.LastWriteTimeUtc > restartBegan)
                Say("NOTE: git index lock appeared during this restart's kill (mtime after the restart began); probably orphaned by this restart's kill. "
                    + indexLockAfter.Path + ". Remove it with: Remove-Item '" + indexLockAfter.Path + "'. Never remove a lock while a git process older than it is running.");

            if (options.StopOnly)
            {
                Say("STOP-ONLY: AppHost stopped. Relaunch skipped.");
                Say("  preserving session-runner port " + AppHost.SessionRunnerPort.ToString(CultureInfo.InvariantCulture) + " and antiphon-postgres.");
                return 0;
            }

            // 4) Reset the launch signals so the wait is not fooled by stale state (CARD-1680: the log is rotated, not emptied).
            try { files.File.Delete(paths.UrlFile); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            AppHostLaunchLog.Rotate(files, paths.LogFile);

            // 5) Relaunch detached; dev-aspire backgrounds the AppHost.
            Say("  launching dev-aspire" + (options.NoBuild ? " --no-build" : "") + "...");
            var devArguments = new List<string> { "dev-aspire" };
            if (options.NoBuild) devArguments.Add("--no-build");
            if (options.AllowWorktree) devArguments.Add("--allow-worktree");
            var tag = Guid.NewGuid().ToString("N");
            var launcherOut = files.Path.Combine(paths.LogDir, "apphost-launcher-" + tag + ".out");
            var launcherErr = files.Path.Combine(paths.LogDir, "apphost-launcher-" + tag + ".err");
            DateTime? stalePidStamp = files.File.Exists(paths.PidFile) ? files.File.GetLastWriteTimeUtc(paths.PidFile) : null;
            var child = platform.StartCli(devArguments, launcherOut, launcherErr);
            state.KeepLock = true;

            return Wait(options, host, platform, state, paths, child, expectedSha, launcherOut, launcherErr, stalePidStamp);
        }

        // 6) The dashboard URL, backend health and the intended loaded SHA. CARD-1680: TimeoutSec is a no-progress window; log or
        // launcher output growth, the URL file, the server port opening, the dashboard's first answer (CARD-1806) or a non-200
        // /health answer restart it, up to MaxWaitSec from the launch. Progress is not credited once /health answers 200: the SHA
        // check then has the rest of the current window.
        private static int Wait(AppHostRestartOptions options, NightlyHost host, IAppHostPlatform platform, RestartState state, RestartPaths paths,
            IAppHostChild child, string expectedSha, string launcherOut, string launcherErr, DateTime? stalePidStamp)
        {
            var files = platform.Files;
            void Say(string line) => host.Out.WriteLine(line);
            var waitStarted = host.UtcNow;
            var hardCap = Math.Max(options.MaxWaitSec, options.TimeoutSec);
            var hardDeadline = waitStarted.AddSeconds(hardCap);
            var deadline = waitStarted.AddSeconds(options.TimeoutSec);
            var lastProgressAt = waitStarted;
            var lastProgress = "launch started";
            var progressFiles = new[] { paths.LogFile, launcherOut, launcherErr, paths.UrlFile };
            var snapshot = AppHostLaunchLog.Snapshot(platform, progressFiles);
            var waitEnd = "no-progress";
            string dashUrl = null;
            var backendOk = false;
            var verified = false;
            string observed = null;
            var reason = "unavailable";
            string freshHead = null;
            var timing = new LiveStartupPhase(host, "health-wait");
            try
            {
                while (true)
                {
                    var now = host.UtcNow;
                    if (now >= hardDeadline) { waitEnd = "hard-cap"; break; }
                    if (now >= deadline) { waitEnd = "no-progress"; break; }
                    host.Sleep(TimeSpan.FromSeconds(3));
                    if (dashUrl == null && files.File.Exists(paths.UrlFile))
                    {
                        string text = null;
                        try { text = files.File.ReadAllText(paths.UrlFile).Trim(); }
                        catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                        if (text != null && Url.IsMatch(text)) dashUrl = text;
                    }
                    // CARD-1806: dev-aspire gives up on the URL file 90 s after the launch, build included; ask the pinned port.
                    string dashFound = null;
                    if (dashUrl == null)
                    {
                        var dashboard = "http://localhost:" + AppHost.DashboardPort.ToString(CultureInfo.InvariantCulture);
                        var probe = platform.Get(dashboard + "/", 3);
                        if (probe.StatusCode.HasValue) { dashUrl = dashboard; dashFound = dashboard; }
                    }
                    var verdict = AppHostLaunchLog.Verdict(files, paths.LogFile);
                    if (verdict.Kind == "BuildFailed")
                    {
                        state.KeepLock = false;
                        child.Stop();
                        Say("AppHost build FAILED - check: Get-Content '" + paths.LogFile + "' -Tail 40");
                        return 1;
                    }
                    if (verdict.Kind == "DcpDependencyTimeout")
                    {
                        // The exception names podman because podman's probe had returned when the deadline hit; say what docker
                        // reported. The lock stays: the spawned launch may still be in flight.
                        foreach (var line in AppHostLaunchLog.DcpTimeoutLines(paths.LogFile, verdict, AppHostLaunchLog.Docker(platform, host, 10), paths.LaunchLock,
                            paths.RestartLock, paths.WatchdogLog))
                            Say(line);
                        return 4;
                    }
                    if (!backendOk)
                    {
                        // CARD-1680: a dead launch ends the wait at once instead of at the window.
                        if (LauncherFailure(child).HasValue) { waitEnd = "launcher-exited"; break; }
                        var launched = LaunchedPid(files, paths.PidFile, stalePidStamp);
                        if (launched.HasValue && platform.Holder(launched.Value) != AppHostHolder.Alive && platform.PortOwners(AppHost.ServerPort).Count == 0)
                        {
                            state.KeepLock = false;
                            child.Stop();
                            Say("AppHost process " + launched.Value.ToString(CultureInfo.InvariantCulture)
                                + " exited before health was confirmed and nothing listens on 17202; removing this restart's lock.");
                            Say("  check: Get-Content '" + paths.LogFile + "' -Tail 40; launcher output: " + launcherOut + "; stderr: " + launcherErr);
                            return 1;
                        }
                    }
                    var previous = snapshot;
                    snapshot = AppHostLaunchLog.Snapshot(platform, progressFiles);
                    var progress = AppHostLaunchLog.Progress(previous, snapshot);
                    if (progress == null && dashFound != null) progress = "dashboard answered on " + dashFound;
                    AppHostHttpAnswer health = null;
                    if (dashUrl != null) health = platform.Get("http://localhost:" + AppHost.ServerPort.ToString(CultureInfo.InvariantCulture) + "/health", 5);
                    var healthOk = health != null && health.StatusCode == 200;
                    if (progress == null && health != null && health.StatusCode.HasValue && !healthOk)
                        progress = "/health answered " + health.StatusCode.Value.ToString(CultureInfo.InvariantCulture);
                    if (progress != null && !backendOk && !healthOk)
                    {
                        lastProgressAt = host.UtcNow;
                        lastProgress = progress;
                        deadline = lastProgressAt.AddSeconds(options.TimeoutSec);
                    }
                    if (!healthOk) continue;
                    backendOk = true;
                    var identity = AppHostProbes.LoadedIdentity(platform.Get("http://localhost:" + AppHost.ServerPort.ToString(CultureInfo.InvariantCulture) + "/api/version", 5));
                    freshHead = AppHostProbes.SourceHead(host, paths.Root);
                    var moved = freshHead == null || freshHead != expectedSha;
                    var shaOk = identity.Ok && identity.Sha.ToLowerInvariant() == expectedSha;
                    if (shaOk && !moved)
                    {
                        verified = true;
                        observed = identity.Sha;
                        break;
                    }
                    if (moved) { reason = "checkout moved"; observed = freshHead; }
                    else if (identity.Ok) { reason = identity.Sha; observed = identity.Sha; }
                    else { reason = identity.Reason; observed = identity.Sha; }
                }
            }
            finally
            {
                timing.Stop(verified);
            }

            var waited = (int)(host.UtcNow - waitStarted).TotalSeconds;
            var progressAge = (int)(host.UtcNow - lastProgressAt).TotalSeconds;
            var summary = "  health wait ended after " + waited.ToString(CultureInfo.InvariantCulture) + "s: "
                + (waitEnd == "hard-cap"
                    ? "hit the hard cap of " + hardCap.ToString(CultureInfo.InvariantCulture) + "s"
                    : "no launch progress for " + options.TimeoutSec.ToString(CultureInfo.InvariantCulture) + "s (hard cap " + hardCap.ToString(CultureInfo.InvariantCulture) + "s)")
                + "; last progress: " + lastProgress + ", " + progressAge.ToString(CultureInfo.InvariantCulture) + "s ago";
            var launcherFailure = LauncherFailure(child);
            if (!backendOk && launcherFailure.HasValue)
            {
                state.KeepLock = false;
                Say("AppHost launcher exited " + launcherFailure.Value.ToString(CultureInfo.InvariantCulture) + " before health was confirmed; removing this restart's lock.");
                Say("  launcher output: " + launcherOut + "; stderr: " + launcherErr);
                return 1;
            }
            if (dashUrl != null && backendOk && verified)
            {
                state.KeepLock = false;
                Say("AppHost restarted - dashboard " + dashUrl + ", backend healthy, server " + observed + ".");
                return 0;
            }
            if (dashUrl != null && backendOk)
            {
                Say(summary);
                Say("REFUSED: server build unverified (exit 5).");
                Say("  expected SHA: " + expectedSha);
                Say("  observed SHA: " + (string.IsNullOrWhiteSpace(observed) ? reason : observed));
                Say("  reason: " + reason);
                if (reason == "checkout moved") Say("  checkout moved: expected " + expectedSha + ", HEAD now " + freshHead);
                Say("  source root: " + paths.Root);
                Say("  lock: " + paths.RestartLock);
                Say("  inspect: Get-Content '" + paths.LogFile + "' -Tail 40; Get-Content '" + paths.RestartLock + "'");
                Say("  The launched child was left running; the restart lock is retained.");
                return 5;
            }
            if (dashUrl != null)
            {
                Say("Dashboard up (" + dashUrl + ") but backend health not confirmed in " + waited.ToString(CultureInfo.InvariantCulture) + "s.");
                Say(summary);
                Say("  Leaving " + paths.RestartLock + " so a watchdog fire treats this as still in flight.");
                return 1;
            }
            Say("AppHost did not come up within " + waited.ToString(CultureInfo.InvariantCulture) + "s - check '" + paths.LogFile + "'.");
            Say(summary);
            Say("  NOTE: the dev-aspire this command spawned may STILL be launching (its own");
            Say("  budget is longer than " + waited.ToString(CultureInfo.InvariantCulture) + "s). Do not re-run blind - re-running kills a");
            Say("  DCP that is still coming up (CARD-0075). Check " + paths.LaunchLock + " first.");
            Say("  Leaving " + paths.RestartLock + " for LockMaxAgeMinutes. Do not delete it to force a retry.");
            Say("  Prefer inspection, guarded dead-launch admission, or ordinary expiry.");
            return 1;
        }

        /// <summary>Wait-AppHostProcessExit: whole seconds until the process is gone, or null after the timeout (polled each second).</summary>
        public static int? WaitForExit(NightlyHost host, IAppHostPlatform platform, int pid, int timeoutSec)
        {
            var started = host.UtcNow;
            while (true)
            {
                if (platform.Holder(pid) != AppHostHolder.Alive) return Math.Max(0, (int)Math.Floor((host.UtcNow - started).TotalSeconds));
                if ((host.UtcNow - started).TotalSeconds >= timeoutSec) return null;
                host.Sleep(TimeSpan.FromSeconds(1));
            }
        }

        /// <summary>Get-AppHostLauncherFailure: the launcher's non-zero exit code once it has exited, else null.</summary>
        public static int? LauncherFailure(IAppHostChild child)
        {
            if (child == null || !child.HasExited) return null;
            var code = child.ExitCode;
            return code.HasValue && code.Value != 0 ? code : null;
        }

        /// <summary>
        /// Get-AppHostLaunchedPid (CARD-1680): the AppHost wrapper pid dev-aspire wrote for this launch. A pid file still
        /// carrying the write time it had before the launch belongs to the AppHost the restart killed.
        /// </summary>
        public static int? LaunchedPid(System.IO.Abstractions.IFileSystem files, string pidFile, DateTime? staleStampUtc)
        {
            if (!files.File.Exists(pidFile)) return null;
            if (staleStampUtc.HasValue && files.File.GetLastWriteTimeUtc(pidFile) == staleStampUtc.Value) return null;
            return ReadPid(files, pidFile);
        }

        private static int? ReadPid(System.IO.Abstractions.IFileSystem files, string path)
        {
            if (!files.File.Exists(path)) return null;
            string text;
            try { text = files.File.ReadAllText(path).Trim(); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return null; }
            return Regex.IsMatch(text, @"^\d+$") && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var pid) ? pid : null;
        }
    }
}
