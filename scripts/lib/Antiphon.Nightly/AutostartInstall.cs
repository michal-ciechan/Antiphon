// The always-on desktop backend's installers (unit 36 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; they
// replaced scripts/install-autostart.ps1 and scripts/install-cleanup-task.ps1, which nothing started by path). The CLI runs them as
//
//   antiphon-scripts install-autostart [--uninstall] [--no-apphost] [--no-watchdog] [--no-watchdog-state-observer] [--apphost-only]
//       [--task-name "Antiphon Session Runner"] [--apphost-task-name "Antiphon AppHost"] [--watchdog-task-name "Antiphon AppHost Watchdog"]
//       [--watchdog-state-observer-task-name "Antiphon AppHost Watchdog State Observer"] [--apphost-delay PT1M] [--watchdog-delay PT15M]
//   antiphon-scripts install-cleanup-task [--uninstall]
//
// install-autostart brings up the docker-compose.dev.yml services (restart: unless-stopped; runbook G-1:
// ANTIPHON_SKIP_DEV_POSTGRES=1 brings up every service but postgres) and registers up to four per-user Scheduled Tasks, each
// running a kept launcher by path under the resolved PowerShell host (-NonInteractive -NoProfile -ExecutionPolicy Bypass
// -WindowStyle Hidden -File "<checkout>\scripts\<launcher>.ps1", working directory the checkout), so a task registered before
// or after this port starts the same file:
//
//   "Antiphon Session Runner"   at logon; no time limit, 3 restarts a minute apart      autostart-session-runner.ps1
//   "Antiphon AppHost"          logon + --apphost-delay; 1 h limit, 2 restarts 5 min apart autostart-apphost.ps1
//   "Antiphon AppHost Watchdog" logon + --watchdog-delay, then every 2 min, and every 2 min from 2 min after the install
//                               (the logon trigger alone leaves NextRunTime blank while the user is logged on); 30 min limit
//                                                                                        watchdog-apphost.ps1
//   "Antiphon AppHost Watchdog State Observer" the watchdog's triggers; 5 min limit     apphost-watchdog-state-observer.ps1
//
// Re-registering is delete then register, which TERMINATES a running instance: --apphost-only leaves the session runner's task
// (and its live supervisor) alone. --no-watchdog does not skip the observer; --no-apphost skips the AppHost, the watchdog and the
// observer. --uninstall removes the same selection and leaves the Postgres container and its data. The PowerShell host is pwsh 7
// from Program Files, Program Files (x86) or the per-user WindowsApps alias (version-independent), else pwsh on PATH, never a
// version-pinned MSIX package path (it vanishes on the next PowerShell update and the task silently stops), else
// powershell.exe. install-cleanup-task registers "Antiphon Build Junk Cleanup" (cleanup-build-junk.ps1, weekly Monday 09:00
// and at logon, 2 h limit) under pwsh on PATH, else powershell.exe.
//
// Exit 0 done; 1 a missing launcher or compose file, a failed compose bring-up, a task the Task Scheduler refused, or not
// Windows (one line). Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The install-autostart command's options (install-autostart.ps1's parameters).</summary>
    public sealed class AutostartInstallOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "install-autostart [--uninstall] [--no-apphost] [--no-watchdog] [--no-watchdog-state-observer] [--apphost-only] [--task-name <name>]"
            + " [--apphost-task-name <name>] [--watchdog-task-name <name>] [--watchdog-state-observer-task-name <name>] [--apphost-delay PT1M] [--watchdog-delay PT15M]";

        /// <summary>The session runner's logon task.</summary>
        public const string SessionRunnerTask = "Antiphon Session Runner";

        /// <summary>The AppHost's logon task.</summary>
        public const string AppHostTask = "Antiphon AppHost";

        /// <summary>The AppHost watchdog's task.</summary>
        public const string WatchdogTask = "Antiphon AppHost Watchdog";

        /// <summary>The watchdog-state observer's task.</summary>
        public const string ObserverTask = "Antiphon AppHost Watchdog State Observer";

        /// <summary>Remove the tasks instead of registering them.</summary>
        public bool Uninstall { get; init; }

        /// <summary>Skip the AppHost, the watchdog and the observer.</summary>
        public bool NoAppHost { get; init; }

        /// <summary>Skip the watchdog (not the observer).</summary>
        public bool NoWatchdog { get; init; }

        /// <summary>Skip the observer.</summary>
        public bool NoWatchdogStateObserver { get; init; }

        /// <summary>Leave the session runner's task alone.</summary>
        public bool AppHostOnly { get; init; }

        /// <summary>The session runner's task name.</summary>
        public string TaskName { get; init; } = SessionRunnerTask;

        /// <summary>The AppHost's task name.</summary>
        public string AppHostTaskName { get; init; } = AppHostTask;

        /// <summary>The watchdog's task name.</summary>
        public string WatchdogTaskName { get; init; } = WatchdogTask;

        /// <summary>The observer's task name.</summary>
        public string WatchdogStateObserverTaskName { get; init; } = ObserverTask;

        /// <summary>The AppHost task's delay after logon (ISO 8601).</summary>
        public string AppHostDelay { get; init; } = "PT1M";

        /// <summary>The watchdog's and the observer's delay after logon (ISO 8601): the logon AppHost launch can hold 17202 dead for minutes.</summary>
        public string WatchdogDelay { get; init; } = "PT15M";

        /// <summary>The checkout whose launchers the tasks run.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static AutostartInstallOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments,
                new[] { "--task-name", "--apphost-task-name", "--watchdog-task-name", "--watchdog-state-observer-task-name", "--apphost-delay", "--watchdog-delay" },
                new[] { "--uninstall", "--no-apphost", "--no-watchdog", "--no-watchdog-state-observer", "--apphost-only" });
            return new AutostartInstallOptions
            {
                Uninstall = flags.Switch("--uninstall"),
                NoAppHost = flags.Switch("--no-apphost"),
                NoWatchdog = flags.Switch("--no-watchdog"),
                NoWatchdogStateObserver = flags.Switch("--no-watchdog-state-observer"),
                AppHostOnly = flags.Switch("--apphost-only"),
                TaskName = flags.Value("--task-name", SessionRunnerTask),
                AppHostTaskName = flags.Value("--apphost-task-name", AppHostTask),
                WatchdogTaskName = flags.Value("--watchdog-task-name", WatchdogTask),
                WatchdogStateObserverTaskName = flags.Value("--watchdog-state-observer-task-name", ObserverTask),
                AppHostDelay = flags.Value("--apphost-delay", "PT1M"),
                WatchdogDelay = flags.Value("--watchdog-delay", "PT15M"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>The install-cleanup-task command's options.</summary>
    public sealed class CleanupTaskInstallOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "install-cleanup-task [--uninstall]";

        /// <summary>The task.</summary>
        public const string TaskName = "Antiphon Build Junk Cleanup";

        /// <summary>Remove the task instead of registering it.</summary>
        public bool Uninstall { get; init; }

        /// <summary>The checkout whose cleanup-build-junk.ps1 the task runs.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static CleanupTaskInstallOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Array.Empty<string>(), new[] { "--uninstall" });
            return new CleanupTaskInstallOptions { Uninstall = flags.Switch("--uninstall"), RepoRoot = repoRoot ?? "" };
        }
    }

    /// <summary>
    /// The machine the installers read and change: the file system, the Task Scheduler, the variables the PowerShell host and
    /// the task user come from (ProgramFiles, ProgramFiles(x86), LOCALAPPDATA, APPDATA, USERDOMAIN, USERNAME and
    /// ANTIPHON_SKIP_DEV_POSTGRES), Get-Command (a name's first match on PATH, null when none) and docker (its exit code; capture:
    /// its output read and dropped).
    /// </summary>
    public sealed record AutostartInstallMachine(IFileSystem Files, IScheduledTaskRegistry Tasks, Func<string, string> Variables, Func<string, string> FindApplication,
        Func<IReadOnlyList<string>, bool, int> Docker);

    /// <summary>The installers (install-autostart.ps1's and install-cleanup-task.ps1's bodies).</summary>
    public static class AutostartInstall
    {
        /// <summary>A task action's argument prefix: the launcher file follows, quoted.</summary>
        public const string HiddenFile = "-NonInteractive -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File ";

        private static readonly Regex PinnedMsix = new Regex(@"WindowsApps\\Microsoft\.PowerShell_", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private sealed class Failure : Exception
        {
            public Failure(string message) : base(message) { }
        }

        /// <summary>Registers (or removes) the always-on tasks; 0 done, 1 after one error line.</summary>
        public static int Run(AutostartInstallOptions options, NightlyHost host, AutostartInstallMachine machine)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            try
            {
                Install(options, host, machine);
                return 0;
            }
            catch (Exception e) when (e is Failure || e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine("install-autostart: " + e.Message);
                return 1;
            }
        }

        /// <summary>Registers (or removes) the build junk cleanup task; 0 done, 1 after one error line.</summary>
        public static int RunCleanupTask(CleanupTaskInstallOptions options, NightlyHost host, AutostartInstallMachine machine)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            var name = CleanupTaskInstallOptions.TaskName;
            if (options.Uninstall)
            {
                // Unregister-ScheduledTask -ErrorAction SilentlyContinue: an absent task is not an error.
                try
                {
                    if (machine.Tasks.Exists(name)) machine.Tasks.Delete(name);
                }
                catch (Exception e) when (e is InvalidOperationException || e is UnauthorizedAccessException) { }
                host.Out.WriteLine("Removed task '" + name + "' (if it existed).");
                return 0;
            }
            try
            {
                var launcher = machine.Files.Path.Combine(options.RepoRoot, "scripts", "cleanup-build-junk.ps1");
                var shell = machine.FindApplication("pwsh") ?? "powershell.exe";
                var user = User(machine);
                var local = host.Clock.GetLocalNow();
                var nine = new DateTimeOffset(local.Year, local.Month, local.Day, 9, 0, 0, local.Offset);
                var definition = new ScheduledTaskDefinition(name,
                    "Deletes regenerable alternate build outputs (bin-verify, bin-ptyhost, bin-profile*) so they cannot accumulate and slow builds.",
                    new ScheduledTaskAction(shell, HiddenFile + "\"" + launcher + "\"", null),
                    new[]
                    {
                        new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Weekly, StartBoundary: Boundary(nine), DaysOfWeek: 2, WeeksInterval: 1),
                        new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: user),
                    },
                    user,
                    new ScheduledTaskSettings(false, false, true, "PT2H"));
                if (machine.Tasks.Exists(name)) machine.Tasks.Delete(name);
                machine.Tasks.Register(definition);
                host.Out.WriteLine("Registered task '" + name + "' (weekly Mon 09:00 + at logon) running " + launcher);
                return 0;
            }
            catch (Exception e) when (e is InvalidOperationException || e is IOException || e is UnauthorizedAccessException)
            {
                host.Error.WriteLine("install-cleanup-task: " + e.Message);
                return 1;
            }
        }

        /// <summary>An ISO 8601 duration as the cmdlets wrote a TimeSpan (PT2M, P3650D, PT0S).</summary>
        public static string Duration(TimeSpan span) => System.Xml.XmlConvert.ToString(span);

        /// <summary>A Once or Weekly start boundary as the cmdlets wrote it: the local time in UTC, to the second, with Z.</summary>
        public static string Boundary(DateTimeOffset local) =>
            local.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        /// <summary>
        /// The task action's PowerShell host: the first existing pwsh 7 of Program Files, Program Files (x86), the per-user
        /// WindowsApps alias and PATH that is not a version-pinned MSIX package path; else powershell.exe on PATH. Null when none.
        /// </summary>
        public static string PowerShellHost(AutostartInstallMachine machine)
        {
            if (machine == null) throw new ArgumentNullException(nameof(machine));
            string Under(string variable, params string[] parts)
            {
                var root = machine.Variables(variable);
                return string.IsNullOrWhiteSpace(root) ? null : machine.Files.Path.Combine(new[] { root }.Concat(parts).ToArray());
            }
            var candidates = new[]
            {
                Under("ProgramFiles", "PowerShell", "7", "pwsh.exe"),
                Under("ProgramFiles(x86)", "PowerShell", "7", "pwsh.exe"),
                Under("LOCALAPPDATA", "Microsoft", "WindowsApps", "pwsh.exe"),
                machine.FindApplication("pwsh.exe"),
            };
            return candidates.FirstOrDefault(path => !string.IsNullOrEmpty(path) && machine.Files.File.Exists(path) && !PinnedMsix.IsMatch(path))
                ?? machine.FindApplication("powershell.exe");
        }

        private static string User(AutostartInstallMachine machine) => (machine.Variables("USERDOMAIN") ?? "") + "\\" + (machine.Variables("USERNAME") ?? "");

        private static void Install(AutostartInstallOptions options, NightlyHost host, AutostartInstallMachine machine)
        {
            var output = host.Out;
            void Step(string message)
            {
                output.WriteLine();
                output.WriteLine("> " + message);
            }
            void Note(string message) => output.WriteLine("  " + message);
            var files = machine.Files;
            var root = options.RepoRoot;
            string Launcher(string name) => files.Path.Combine(root, "scripts", name);
            var composeFile = files.Path.Combine(root, "docker-compose.dev.yml");
            var appHost = !options.NoAppHost;
            var watchdog = appHost && !options.NoWatchdog;
            var observer = appHost && !options.NoWatchdogStateObserver;

            if (options.Uninstall)
            {
                var remove = new List<string>();
                if (!options.AppHostOnly) remove.Add(options.TaskName);
                if (appHost) remove.Add(options.AppHostTaskName);
                if (watchdog) remove.Add(options.WatchdogTaskName);
                if (observer) remove.Add(options.WatchdogStateObserverTaskName);
                foreach (var name in remove)
                {
                    Step("Removing Scheduled Task '" + name + "'...");
                    if (machine.Tasks.Exists(name))
                    {
                        machine.Tasks.Delete(name);
                        Note("Task removed.");
                    }
                    else
                    {
                        Note("No such task - nothing to do.");
                    }
                }
                Note("Postgres container left running. To stop and remove it (data kept in the 'antiphon_pgdata' volume):");
                Note("  docker compose -f \"" + composeFile + "\" down");
                Note("To also delete the data volume:  docker compose -f \"" + composeFile + "\" down -v");
                return;
            }

            // Pre-flight: every launcher a task will run, and the compose file.
            var runnerLauncher = Launcher("autostart-session-runner.ps1");
            var appHostLauncher = Launcher("autostart-apphost.ps1");
            var watchdogLauncher = Launcher("watchdog-apphost.ps1");
            var observerLauncher = Launcher("apphost-watchdog-state-observer.ps1");
            if (!files.File.Exists(runnerLauncher)) throw new Failure("Missing " + runnerLauncher);
            if (!files.File.Exists(composeFile)) throw new Failure("Missing " + composeFile);
            if (appHost && !files.File.Exists(appHostLauncher)) throw new Failure("Missing " + appHostLauncher);
            if (watchdog && !files.File.Exists(watchdogLauncher)) throw new Failure("Missing " + watchdogLauncher);
            if (observer && !files.File.Exists(observerLauncher)) throw new Failure("Missing " + observerLauncher);

            var shell = PowerShellHost(machine) ?? throw new Failure("No PowerShell host: neither pwsh 7 nor powershell.exe was found.");
            Note("PowerShell host for task: " + shell);
            var user = User(machine);
            ScheduledTaskAction Action(string launcher) => new ScheduledTaskAction(shell, HiddenFile + "\"" + launcher + "\"", root);

            // 1. The dev compose services (Postgres unless runbook G-1 skips it).
            Step("Bringing up always-on PostgreSQL (docker-compose.dev.yml)...");
            if (machine.Docker(new[] { "info" }, true) != 0)
            {
                output.WriteLine("  Docker Desktop is not running - start it, then re-run this command.");
                output.WriteLine("  (Postgres setup skipped; the Scheduled Task will still be registered.)");
            }
            else
            {
                var skip = DevCompose.Skipped(machine.Variables(DevCompose.SkipVariable));
                var text = skip ? files.File.ReadAllText(composeFile) : "";
                var arguments = DevCompose.UpArguments(composeFile, text, skip);
                if (skip) Note(DevCompose.SkipVariable + "=1: the desktop Postgres is not brought up (database moved off-host)"
                    + (arguments.Count > 0 ? "; compose up -d --no-deps " + string.Join(", ", arguments.Skip(6)) : "") + ".");
                if (arguments.Count > 0 && machine.Docker(arguments, false) != 0) throw new Failure("docker compose up failed.");
                if (!skip) output.WriteLine("  Postgres container 'antiphon-postgres' is up (restart: unless-stopped).");
                if (DockerAutoStart(machine) == true)
                {
                    output.WriteLine("  Docker Desktop AutoStart is ON - Postgres will return on login.");
                }
                else
                {
                    output.WriteLine("  Docker Desktop 'Start Docker Desktop when you log in' appears OFF.");
                    output.WriteLine("  Enable it (Settings -> General) so Postgres auto-starts on boot.");
                }
            }

            // 2. The session runner. Re-registering a RUNNING task terminates its live supervisor (the daemon is then
            // unsupervised until the next logon): --apphost-only leaves it alone.
            if (options.AppHostOnly)
            {
                Step("Skipping session-runner task (--apphost-only).");
                Note("Existing '" + options.TaskName + "' left untouched.");
            }
            else
            {
                Step("Registering logon Scheduled Task '" + options.TaskName + "'...");
                Replace(machine, new ScheduledTaskDefinition(options.TaskName,
                    "Starts the Antiphon session-runner daemon (port 17204) at logon so agents can run.",
                    Action(runnerLauncher),
                    new[] { new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: user) },
                    user,
                    // No time limit (a long-lived supervisor); restarts as the backstop when it dies.
                    new ScheduledTaskSettings(true, true, true, "PT0S", 3, "PT1M")));
                output.WriteLine("  Task registered (runs at logon as " + machine.Variables("USERNAME") + ").");
            }

            // 3. The AppHost: a one-shot launcher (it exits once the backend is healthy), delayed so Docker Desktop gets a head start.
            if (appHost)
            {
                Step("Registering logon Scheduled Task '" + options.AppHostTaskName + "'...");
                Replace(machine, new ScheduledTaskDefinition(options.AppHostTaskName,
                    "Starts the Antiphon Aspire AppHost (server 17202 / client 17203 / dashboard 17205) at logon, after waiting for Docker Desktop.",
                    Action(appHostLauncher),
                    new[] { new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: user, Delay: options.AppHostDelay) },
                    user,
                    new ScheduledTaskSettings(true, true, true, "PT1H", 2, "PT5M")));
                output.WriteLine("  Task registered (runs " + options.AppHostDelay + " after logon as " + machine.Variables("USERNAME") + ").");
            }
            else
            {
                Step("Skipping AppHost logon task (--no-apphost).");
                Note("Start the app by hand with:  .\\dev-aspire.ps1");
            }

            // 4. The watchdog (the logon AppHost task is a one-shot): logon + delay, repeating, and a repeating once trigger.
            var interval = Duration(TimeSpan.FromMinutes(2));
            var duration = Duration(TimeSpan.FromDays(3650));
            var now = host.Clock.GetLocalNow();
            IReadOnlyList<ScheduledTaskTrigger> Repeating(string delay) => new[]
            {
                new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Logon, UserId: user, Delay: delay, RepetitionInterval: interval, RepetitionDuration: duration),
                new ScheduledTaskTrigger(ScheduledTaskTriggerKind.Once, StartBoundary: Boundary(now.AddMinutes(2)), RepetitionInterval: interval, RepetitionDuration: duration),
            };
            if (watchdog)
            {
                Step("Registering watchdog Scheduled Task '" + options.WatchdogTaskName + "'...");
                Replace(machine, new ScheduledTaskDefinition(options.WatchdogTaskName,
                    "Probes Antiphon AppHost HTTP health (17202/health and 17203) every 2 minutes; restarts via restart-apphost.ps1 if both stay down. Never touches the session-runner on 17204.",
                    Action(watchdogLauncher), Repeating(options.WatchdogDelay), user,
                    new ScheduledTaskSettings(true, true, true, "PT30M")));
                output.WriteLine("  Task registered (logon+" + options.WatchdogDelay + " then every 2 min as " + machine.Variables("USERNAME") + ").");
            }
            else
            {
                Step("Skipping AppHost watchdog task.");
            }

            // 5. The watchdog-state observer: independent of the watchdog (disabling the watchdog must never disable it).
            // Detection only: it writes logs/apphost-watchdog-state.json and never restarts or re-enables anything.
            if (observer)
            {
                Step("Registering watchdog-state observer Scheduled Task '" + options.WatchdogStateObserverTaskName + "'...");
                Replace(machine, new ScheduledTaskDefinition(options.WatchdogStateObserverTaskName,
                    "Samples Antiphon AppHost Watchdog Scheduled Task state every 2 minutes into logs/apphost-watchdog-state.json. Detection only; never restarts or re-enables the watchdog.",
                    Action(observerLauncher), Repeating(options.WatchdogDelay), user,
                    new ScheduledTaskSettings(true, true, true, "PT5M")));
                output.WriteLine("  Task registered (logon+" + options.WatchdogDelay + " then every 2 min as " + machine.Variables("USERNAME") + ").");
            }
            else
            {
                Step("Skipping watchdog-state observer task.");
            }

            output.WriteLine();
            output.WriteLine("Always-on backend configured:");
            Note("  Postgres       : docker container 'antiphon-postgres'  (localhost:17280)");
            Note("  Session-runner : Scheduled Task '" + options.TaskName + "'            (http://localhost:17204)");
            if (appHost)
            {
                Note("  AppHost        : Scheduled Task '" + options.AppHostTaskName + "'  (server :17202, client :17203, dashboard :17205)");
                if (!options.NoWatchdog) Note("  AppHost watchdog: Scheduled Task '" + options.WatchdogTaskName + "'  (HTTP probe every 2 min -> restart-apphost)");
                if (!options.NoWatchdogStateObserver)
                    Note("  Watchdog observer: Scheduled Task '" + options.WatchdogStateObserverTaskName + "'  (state sample every 2 min -> logs/apphost-watchdog-state.json)");
            }
            else
            {
                Note("  The rest (server/client/dashboard): run  .\\dev-aspire.ps1");
            }
            output.WriteLine();
            Note("Start the session-runner now without logging out:  Start-ScheduledTask -TaskName \"" + options.TaskName + "\"");
            if (appHost)
            {
                Note("Start the AppHost now without logging out:          Start-ScheduledTask -TaskName \"" + options.AppHostTaskName + "\"");
                Note("AppHost auto-start log:                             " + files.Path.Combine(root, "logs", "autostart-apphost.log"));
                if (!options.NoWatchdog)
                {
                    Note("Watchdog log:                                       " + files.Path.Combine(root, "logs", "watchdog-apphost.log"));
                    Note("Leave the stack down on purpose:                    scripts\\antiphon.cmd set-apphost-maintenance");
                    Note("Leave maintenance (re-enable watchdog):             scripts\\antiphon.cmd set-apphost-maintenance --clear");
                }
                if (!options.NoWatchdogStateObserver)
                {
                    Note("Watchdog-state document:                            " + files.Path.Combine(root, "logs", "apphost-watchdog-state.json"));
                    Note("Watchdog-state observer log:                        " + files.Path.Combine(root, "logs", "apphost-watchdog-state-observer.log"));
                }
            }
            Note("Remove auto-start later:                            scripts\\antiphon.cmd install-autostart --uninstall");
        }

        // Idempotent: an existing task of the name is removed first (which stops a running instance), then the task is registered.
        private static void Replace(AutostartInstallMachine machine, ScheduledTaskDefinition definition)
        {
            if (machine.Tasks.Exists(definition.Name)) machine.Tasks.Delete(definition.Name);
            machine.Tasks.Register(definition);
        }

        // Docker Desktop's "Start when you log in": AutoStart of %APPDATA%\Docker\settings-store.json, else settings.json; null
        // when neither names it (an unreadable file is skipped).
        private static bool? DockerAutoStart(AutostartInstallMachine machine)
        {
            var appData = machine.Variables("APPDATA");
            if (string.IsNullOrWhiteSpace(appData)) return null;
            foreach (var name in new[] { "settings-store.json", "settings.json" })
            {
                var path = machine.Files.Path.Combine(appData, "Docker", name);
                if (!machine.Files.File.Exists(path)) continue;
                try
                {
                    using var document = JsonDocument.Parse(machine.Files.File.ReadAllText(path));
                    if (document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty("AutoStart", out var value)
                        && value.ValueKind != JsonValueKind.Null)
                        return value.ValueKind == JsonValueKind.True;
                }
                catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException) { }
            }
            return null;
        }
    }
}
