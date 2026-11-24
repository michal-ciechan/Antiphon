// CARD-0011, CARD-0273, CARD-1564, CARD-1680, CARD-1744, CARD-1806: start the desktop Aspire AppHost in dashboard mode (unit 37
// of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced the body of the repository root's dev-aspire.ps1,
// which stays a thin launcher so the runbooks' `.\dev-aspire.ps1` still works, and scripts/lib/dev-postgres-compose.ps1,
// scripts/lib/live-startup-timing.ps1 and the launcher scripts/check-daemon-build.ps1, which only it loaded or ran). The CLI
// runs it as
//
//   antiphon-scripts dev-aspire [--no-build] [--no-browser] [--allow-worktree]
//   antiphon-scripts run-apphost --source-revision <40-hex sha>
//
// dev-aspire refuses (exit 3, nothing touched) a linked or unverifiable worktree (CARD-0273; --allow-worktree permits a
// linked one) and a phone-home configuration (CARD-1744, WindowsPhoneHome.LaunchRefusal). Then, holding logs/apphost.launch.lock
// ("<pid> <utc round-trip>", CARD-0011: a watchdog fire sees a launch in flight; removed on every exit), it checks
// server/appsettings.json and `docker info`, times a `docker network create` (8 s; a hang is HNS trouble: a warning), brings
// up the dev compose services (runbook G-1: ANTIPHON_SKIP_DEV_POSTGRES=1 leaves the desktop Postgres stopped; DevCompose),
// runs the advisory daemon build check, removes aspire.* directories older than two hours from %TEMP%, creates logs/,
// server/logs/, server/logs/sessions/, backups/ and C:\Antiphon\worktrees, and (without --no-build) restores the AppHost under a
// build slot and runs npm install in client/. It empties logs/apphost.log and starts run-apphost detached (this CLI from its
// run copy under bin-launcher, so the long-lived wrapper never holds the build output): the wrapper builds the AppHost under a
// build slot stamped with HEAD (-p:SourceRevisionId; HEAD unreadable refuses), then runs it (dotnet run --no-build), both
// outputs and the build's LIVE_TIMING line in logs/apphost.log. Its pid goes to logs/apphost.pid. dev-aspire waits up to 90 s
// for the dashboard URL (the log's "Login to the dashboard at" line, else the Aspire.Dashboard process's listener), writes it
// to logs/apphost-dashboard-url.txt and opens it (not with --no-browser), checks the antiphon-postgres container for 45 s and
// exits; the AppHost runs on. Each timed phase prints one LIVE_TIMING line (CARD-1564).
//
// Exit codes: 0 launched (a dashboard or Postgres that did not show is a warning); 1 failed (settings, Docker, restore, npm,
// HEAD or the wrapper's start; one stderr line); 3 refused; usage 2. run-apphost answers with the build's exit code when it
// fails, else the AppHost's. Every observation of the machine is IAppHostPlatform's or IDevStackSeams'; git, the clock, the
// sleep and the variables (ANTIPHON_SKIP_DEV_POSTGRES, TEMP, PATH, ComSpec) are the host's. Plain C#: no PowerShell types.
// ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The dev-aspire command's options (dev-aspire.ps1 -NoBuild -NoBrowser -AllowWorktree).</summary>
    public sealed class DevAspireOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "dev-aspire [--no-build] [--no-browser] [--allow-worktree]";

        /// <summary>Skip the AppHost restore and npm install.</summary>
        public bool NoBuild { get; init; }

        /// <summary>Do not open the dashboard (the logon auto-start).</summary>
        public bool NoBrowser { get; init; }

        /// <summary>Allow a linked worktree to control the shared local stack.</summary>
        public bool AllowWorktree { get; init; }

        /// <summary>The checkout whose AppHost starts.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>Where the git worktrees live; created when missing.</summary>
        public string WorktreeRoot { get; init; } = DevStack.WorktreeRoot;

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static DevAspireOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--no-build", "--no-browser", "--allow-worktree" });
            return new DevAspireOptions
            {
                NoBuild = flags.Switch("--no-build"),
                NoBrowser = flags.Switch("--no-browser"),
                AllowWorktree = flags.Switch("--allow-worktree"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The run-apphost command's options: the HEAD the build is stamped with.</summary>
    public sealed class AppHostRunOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "run-apphost --source-revision <40-hex sha>";

        private static readonly Regex Sha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);

        /// <summary>The 40-hex HEAD passed as -p:SourceRevisionId.</summary>
        public string SourceRevision { get; init; } = "";

        /// <summary>The checkout whose AppHost builds and runs.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; a missing or malformed SHA is a usage error.</summary>
        public static AppHostRunOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--source-revision" }, Array.Empty<string>());
            var sha = flags.Required("--source-revision");
            if (!Sha.IsMatch(sha)) throw new NightlyUsageException("--source-revision expects 40 lower-case hex characters, not '" + sha + "'");
            return new AppHostRunOptions { SourceRevision = sha, RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>Runs dev-aspire on a host (an in-process caller's seam: the logon auto-start).</summary>
    public delegate int DevAspireRunner(DevAspireOptions options, NightlyHost host);

    /// <summary>The desktop AppHost start (dev-aspire.ps1's body) and its detached wrapper.</summary>
    public static class DevAspire
    {
        /// <summary>How long the dashboard URL is looked for after the wrapper starts (its build included).</summary>
        public const int DashboardWaitSec = 90;

        /// <summary>How long the antiphon-postgres container is checked after the launch.</summary>
        public const int PostgresWaitSec = 45;

        /// <summary>The OTLP port, which is never the dashboard's.</summary>
        public const int OtlpPort = 17206;

        private static readonly Regex Login = new Regex(@"Login to the dashboard at (https?://\S+)", RegexOptions.CultureInvariant);
        private static readonly Regex LoginPath = new Regex("/login.*", RegexOptions.CultureInvariant);

        /// <summary>Starts the AppHost (see the file header) and returns the exit code.</summary>
        public static int Run(DevAspireOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);

            var worktree = AppHostWorktree.Classify(host, files, root);
            if (!worktree.Verified || (!worktree.IsMainWorktree && !options.AllowWorktree))
            {
                foreach (var line in worktree.GuardMessage(false)) Say(line);
                return 3;
            }
            if (!worktree.IsMainWorktree) foreach (var line in worktree.GuardMessage(true)) Say(line);

            var refusal = WindowsPhoneHome.LaunchRefusal(files.File.Exists(platform.PhoneHomeConfigPath), platform.PhoneHomeConfigPath);
            if (refusal != null)
            {
                Say(refusal);
                return 3;
            }

            // CARD-0011: a watchdog fire sees this launch in flight; the lock goes on every exit.
            var logDir = AppHost.LogDir(files, root);
            var launchLock = files.Path.Combine(logDir, "apphost.launch.lock");
            files.Directory.CreateDirectory(logDir);
            files.File.WriteAllText(launchLock, platform.CurrentPid.ToString(CultureInfo.InvariantCulture) + " " + AppHost.Round(host.UtcNow) + Environment.NewLine);
            try
            {
                return Launch(options, host, platform, seams);
            }
            finally
            {
                try { files.File.Delete(launchLock); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        private static int Launch(DevAspireOptions options, NightlyHost host, IAppHostPlatform platform, IDevStackSeams seams)
        {
            var files = platform.Files;
            var root = options.RepoRoot;
            void Say(string line) => host.Out.WriteLine(line);
            var appHostDir = files.Path.Combine(root, "Antiphon.AppHost");
            var logDir = AppHost.LogDir(files, root);

            if (!files.File.Exists(files.Path.Combine(root, "server", "appsettings.json")))
            {
                host.Error.WriteLine("server\\appsettings.json not found. Copy appsettings.json.example first.");
                return 1;
            }

            var dockerTiming = new LiveStartupPhase(host, "docker-info");
            var docker = platform.Run("docker", new[] { "info" }, 0);
            dockerTiming.Stop(docker.ExitCode == 0);
            if (docker.ExitCode != 0)
            {
                host.Error.WriteLine("Docker Desktop is not running. Start it, wait for the tray icon, then re-run.");
                return 1;
            }

            // Windows HNS can wedge so that `docker network create` never returns; Postgres then never starts.
            Say("");
            Say("> Testing Docker network health...");
            var networkHealthy = false;
            var networkTiming = new LiveStartupPhase(host, "docker-network");
            try
            {
                var name = "aspire-preflight-" + Guid.NewGuid().ToString("N").Substring(0, 12);
                if (!platform.Run("docker", new[] { "network", "create", name }, 8).TimedOut)
                {
                    networkHealthy = true;
                    platform.Run("docker", new[] { "network", "rm", name }, 5);
                    Say("  Docker networking: OK");
                }
                else
                {
                    Say("");
                    Say("  WARNING: Docker network creation is unresponsive (>8s).");
                    Say("  Postgres will NOT start. Restart Docker Desktop, then re-run dev-aspire.");
                    Say("");
                }
            }
            finally
            {
                networkTiming.Stop(networkHealthy);
            }

            // Runbook G-1: Postgres is an external always-on container (docker-compose.dev.yml), not Aspire-managed.
            var skipValue = host.Variables(DevCompose.SkipVariable);
            var skipped = DevCompose.Skipped(skipValue);
            Say("");
            Say(skipped ? "> Ensuring dev compose services except Postgres are up..." : "> Ensuring Postgres container 'antiphon-postgres' is up...");
            DevComposeUp compose = null;
            var containerTiming = new LiveStartupPhase(host, "container-start");
            try
            {
                compose = DevCompose.Up(platform, DevStack.ComposeFile(files, root), skipValue);
            }
            finally
            {
                containerTiming.Stop(compose != null && compose.Exit == 0);
            }
            if (skipped) Say("  " + compose.Message);
            if (compose.Exit != 0) Say("  WARNING: could not start the dev compose services via docker compose.");
            else if (!skipped) Say("  Postgres container ensured");

            // Advisory: AppHost adoption keeps long-lived daemons, so name a relevant source-build difference without stopping them.
            platform.CheckDaemonBuild(host);

            CleanAspireTemp(host, files);
            foreach (var directory in new[] { logDir, files.Path.Combine(root, "server", "logs"), files.Path.Combine(root, "server", "logs", "sessions"), files.Path.Combine(root, "backups"), options.WorktreeRoot })
                files.Directory.CreateDirectory(directory);

            if (!options.NoBuild)
            {
                Say("");
                Say("> Restoring AppHost dependencies...");
                var restore = 1;
                var restoreTiming = new LiveStartupPhase(host, "restore");
                try
                {
                    restore = seams.BuildSlot(new BuildSlotOptions { Label = "apphost-restore", Command = new[] { "dotnet", "restore", appHostDir }, WorkingDirectory = root }, host.Out);
                }
                finally
                {
                    restoreTiming.Stop(restore == 0);
                }
                if (restore != 0)
                {
                    host.Error.WriteLine("dotnet restore failed.");
                    return 1;
                }

                Say("");
                Say("> Installing client npm packages...");
                var npm = 1;
                var dependencyTiming = new LiveStartupPhase(host, "dependencies");
                try
                {
                    npm = DevStack.Npm(host, seams, files.Path.Combine(root, "client"), Say, "install");
                }
                finally
                {
                    dependencyTiming.Stop(npm == 0);
                }
                if (npm != 0)
                {
                    host.Error.WriteLine("npm install failed.");
                    return 1;
                }
            }

            var logFile = files.Path.Combine(logDir, "apphost.log");
            var pidFile = files.Path.Combine(logDir, "apphost.pid");
            Say("");
            Say("> Starting Aspire AppHost (background)...");
            Say("  OTLP    : http://localhost:" + OtlpPort.ToString(CultureInfo.InvariantCulture));
            Say("  Log     : " + logFile);
            Say("");
            // An emptied log: the wait below must never match an earlier launch's lines.
            files.File.WriteAllText(logFile, "");

            // Only a deployed build is stamped (Directory.Build.props keeps "unknown" otherwise); restart-apphost compares
            // /api/version with this HEAD and exits 5 on "unknown".
            var head = DevStack.Head(host, root);
            if (head == null)
            {
                host.Error.WriteLine("git rev-parse HEAD failed in " + root + "; refusing an unstamped AppHost build.");
                return 1;
            }
            IAppHostChild wrapper;
            try
            {
                wrapper = platform.StartCli(new[] { "run-apphost", "--source-revision", head }, files.Path.Combine(logDir, "apphost-run.out"), files.Path.Combine(logDir, "apphost-run.err"));
            }
            catch (Exception e) when (e is Win32Exception || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine("the AppHost did not start: " + e.Message);
                return 1;
            }
            var pid = wrapper.Pid.ToString(CultureInfo.InvariantCulture);
            files.File.WriteAllText(pidFile, pid + Environment.NewLine);
            Say("  AppHost PID : " + pid);
            Say("  Waiting for dashboard URL in log (up to " + DashboardWaitSec.ToString(CultureInfo.InvariantCulture) + "s)...");
            Say("");

            var dashboardUrl = WaitForDashboard(host, platform, logFile);
            if (dashboardUrl != null)
            {
                Say("");
                Say("  Dashboard : " + dashboardUrl);
                files.File.WriteAllText(files.Path.Combine(logDir, "apphost-dashboard-url.txt"), dashboardUrl + Environment.NewLine);
                if (!options.NoBrowser) DevStack.Browse(host, seams, dashboardUrl);
            }
            else
            {
                Say("");
                Say("  Dashboard URL not found after " + DashboardWaitSec.ToString(CultureInfo.InvariantCulture) + "s.");
                Say("  Check log: Get-Content '" + logFile + "' -Tail 30");
            }

            Say("");
            Say("> Checking Postgres...");
            var postgresUp = false;
            if (skipped)
            {
                // The desktop container stays stopped on purpose; the AppHost secret names the server2 database.
                postgresUp = true;
                Say("  Postgres: desktop container not checked (ANTIPHON_SKIP_DEV_POSTGRES=1)");
            }
            else
            {
                var deadline = host.UtcNow.AddSeconds(PostgresWaitSec);
                while (host.UtcNow < deadline)
                {
                    host.Sleep(TimeSpan.FromSeconds(5));
                    var status = platform.Run("docker", new[] { "ps", "--filter", "name=antiphon-postgres", "--format", "{{.Status}}" }, 0);
                    if (AppHostWorktree.Lines(status.Output).Any(line => line.StartsWith("Up", StringComparison.OrdinalIgnoreCase)))
                    {
                        postgresUp = true;
                        Say("  Postgres: Up");
                        break;
                    }
                }
            }
            if (!postgresUp)
            {
                Say("");
                Say("  WARNING: Postgres ('antiphon-postgres') is not up after " + PostgresWaitSec.ToString(CultureInfo.InvariantCulture) + "s.");
                var state = platform.Run("docker", new[] { "ps", "-a", "--filter", "name=antiphon-postgres", "--format", "{{.Names}}\t{{.Status}}" }, 0).Output.Trim();
                if (state.Length > 0) Say("  Container state: " + state);
                Say("  Fix: docker compose -f docker-compose.dev.yml up -d   (restart Docker Desktop if it hangs).");
            }

            Say("");
            Say("AppHost running in background (PID " + pid + ").");
            host.Sleep(TimeSpan.FromSeconds(3));
            return 0;
        }

        /// <summary>
        /// The dashboard URL: Aspire's "Login to the dashboard at" line in the log (its /login path dropped), else the first
        /// listener of an Aspire.Dashboard process that is not the OTLP port; polled every 3 s, null after DashboardWaitSec.
        /// </summary>
        public static string WaitForDashboard(NightlyHost host, IAppHostPlatform platform, string logFile)
        {
            var files = platform.Files;
            var elapsed = 0;
            while (elapsed < DashboardWaitSec)
            {
                host.Sleep(TimeSpan.FromSeconds(3));
                elapsed += 3;
                if (files.File.Exists(logFile))
                {
                    string text = null;
                    try { text = AppHostLaunchLog.ReadShared(files, logFile); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                    var match = text == null ? Match.Empty : Login.Match(text);
                    if (match.Success) return LoginPath.Replace(match.Groups[1].Value, "");
                    var dashboard = platform.Processes(new[] { "Aspire.Dashboard" }).FirstOrDefault();
                    if (dashboard != null)
                    {
                        var port = 0;
                        try { port = platform.ListeningPorts().Where(candidate => candidate != OtlpPort).FirstOrDefault(candidate => platform.PortOwners(candidate).Contains(dashboard.Pid)); }
                        catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is IOException || e is NotSupportedException) { }
                        if (port != 0) return "http://localhost:" + port.ToString(CultureInfo.InvariantCulture);
                    }
                }
                host.Out.WriteLine("  ...(" + elapsed.ToString(CultureInfo.InvariantCulture) + " s)");
            }
            return null;
        }

        /// <summary>Removes the aspire.* directories under %TEMP% that are older than two hours (stale DCP state); best effort.</summary>
        public static void CleanAspireTemp(NightlyHost host, IFileSystem files)
        {
            var temp = host.Variables("TEMP");
            if (string.IsNullOrWhiteSpace(temp) || !files.Directory.Exists(temp)) return;
            var cutoff = host.UtcNow.AddHours(-2);
            var removed = 0;
            foreach (var directory in files.Directory.EnumerateDirectories(temp, "aspire.*"))
            {
                try
                {
                    if (files.Directory.GetLastWriteTimeUtc(directory) >= cutoff) continue;
                    DevStack.DeleteTree(files, directory);
                    removed++;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { removed++; }
            }
            if (removed > 0) host.Out.WriteLine("  Cleaned " + removed.ToString(CultureInfo.InvariantCulture) + " old DCP temp dir(s)");
        }

        /// <summary>
        /// The detached wrapper (run-apphost): logs/apphost.log is created; the AppHost's build under a build slot, stamped, and
        /// the build's LIVE_TIMING line go to it, then (when the build passed) `dotnet run --no-build`'s output until the AppHost
        /// exits. Returns the build's code when it failed, else the AppHost's.
        /// </summary>
        public static int RunAppHost(AppHostRunOptions options, NightlyHost host, IFileSystem files, IDevStackSeams seams)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var root = options.RepoRoot;
            var appHostDir = files.Path.Combine(root, "Antiphon.AppHost");
            var logDir = AppHost.LogDir(files, root);
            files.Directory.CreateDirectory(logDir);
            using var stream = files.FileStream.New(files.Path.Combine(logDir, "apphost.log"), FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            var log = TextWriter.Synchronized(writer);
            var logHost = DevStack.WithOutput(host, log);
            var built = 1;
            var timing = new LiveStartupPhase(logHost, "build");
            try
            {
                built = seams.BuildSlot(new BuildSlotOptions
                {
                    Label = "apphost-build",
                    Command = new[] { "dotnet", "build", "-p:SourceRevisionId=" + options.SourceRevision },
                    WorkingDirectory = appHostDir,
                }, log);
            }
            finally
            {
                timing.Stop(built == 0);
            }
            if (built != 0) return built;
            return DevStack.Run(seams, new DevStackLaunch("dotnet", new[] { "run", "--no-build" }, appHostDir), log.WriteLine);
        }
    }

    /// <summary>
    /// This CLI's run copy for a detached child (dev-aspire, run-apphost): bin-launcher/run-&lt;hash&gt; of the checkout, the copy
    /// scripts/lib/antiphon-cli.ps1 makes (the SHA-256 of the output's relative names and bytes, 24 hex characters). A child
    /// that runs for days must never hold the build output that the next `scripts/antiphon` build writes.
    /// </summary>
    public static class AppHostCli
    {
        /// <summary>The argv that starts the run copy: cli with its last element (the dll or the executable) moved into the copy, made when missing.</summary>
        public static IReadOnlyList<string> RunCopy(string root, IReadOnlyList<string> cli)
        {
            if (cli == null || cli.Count == 0) throw new ArgumentException("the CLI argv is empty", nameof(cli));
            var entry = Path.GetFullPath(cli[cli.Count - 1]);
            var source = Path.GetDirectoryName(entry);
            var launcher = Path.Combine(root, "bin-launcher");
            var run = Path.Combine(launcher, "run-" + OutputHash(source));
            var target = Path.Combine(run, Path.GetFileName(entry));
            if (!string.Equals(Trim(source), Trim(run), StringComparison.OrdinalIgnoreCase) && !File.Exists(target))
            {
                // A whole copy appears under its final name at once; when another launcher wins the rename, its copy is used.
                var temporary = Path.Combine(launcher, "tmp-" + Guid.NewGuid().ToString("N"));
                Copy(source, temporary);
                try { Directory.Move(temporary, run); }
                catch (IOException)
                {
                    try { Directory.Delete(temporary, true); }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                }
                if (!File.Exists(target)) throw new IOException("the run copy " + run + " has no " + Path.GetFileName(entry));
            }
            // In use now: antiphon-cli.ps1's 7-day sweep goes by this time.
            try { Directory.SetLastWriteTimeUtc(run, DateTime.UtcNow); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            var argv = cli.ToList();
            argv[argv.Count - 1] = target;
            return argv;
        }

        /// <summary>Get-AntiphonCliOutputHash: SHA-256 over the relative names ('/' separators, ordinal order) and bytes, 24 lower-case hex characters.</summary>
        public static string OutputHash(string directory)
        {
            var rootPath = directory.TrimEnd('\\', '/');
            var names = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories)
                .Select(file => file.Substring(rootPath.Length + 1).Replace('\\', '/')).OrderBy(name => name, StringComparer.Ordinal).ToList();
            using var sha = SHA256.Create();
            using var buffer = new MemoryStream();
            foreach (var name in names)
            {
                var header = Encoding.UTF8.GetBytes(name + "\n");
                buffer.Write(header, 0, header.Length);
                var bytes = File.ReadAllBytes(Path.Combine(rootPath, name));
                buffer.Write(bytes, 0, bytes.Length);
            }
            return Convert.ToHexString(sha.ComputeHash(buffer.ToArray())).Substring(0, 24).ToLowerInvariant();
        }

        private static string Trim(string path) => Path.GetFullPath(path).TrimEnd('\\', '/');

        private static void Copy(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(target, directory.Substring(source.Length + 1)));
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
                File.Copy(file, Path.Combine(target, file.Substring(source.Length + 1)), true);
        }
    }
}
