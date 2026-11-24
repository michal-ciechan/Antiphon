// CARD-0245: the supported way to leave the desktop AppHost down on purpose, and the independent watchdog-state observer
// (unit 31 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; they replaced the bodies of
// scripts/set-apphost-maintenance.ps1 and scripts/apphost-watchdog-state-observer.ps1, which stay thin launchers:
// scripts/cutover-postgres-server2.ps1 starts the first by path, and the per-user Scheduled Task "Antiphon AppHost Watchdog
// State Observer" (scripts/install-autostart.ps1) the second). The CLI runs them as
//
//   antiphon-scripts set-apphost-maintenance [--clear | --enter-if-absent] [--root <dir>] [--watchdog-task-name <name>]
//   antiphon-scripts apphost-watchdog-state-observer [--task-name <name>] [--root <dir>] [--state-file <path>] [--marker-file <path>]
//     [--log-file <path>]
//
// Entering maintenance creates logs/apphost.down-on-purpose first, then disables the watchdog task; leaving re-enables the
// watchdog first, then removes the marker; either then runs the observer, which records maintenance=true so a recovered
// AppHost raises no Critical attention for an intentional outage. --enter-if-absent creates the marker with create-new
// semantics and exits 2 when it already exists (nothing disabled or removed). Neither command starts or stops the AppHost.
// The observer samples the watchdog task (Enabled, Disabled, Missing or Unknown) into logs/apphost-watchdog-state.json, which
// the server reads (AppHostWatchdogStateDocument); an unhealthy state keeps its episode while it lasts. Detection only: it
// never restarts or re-enables anything. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The set-apphost-maintenance command's options (set-apphost-maintenance.ps1 -Clear -EnterIfAbsent -Root -WatchdogTaskName).</summary>
    public sealed class AppHostMaintenanceOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "set-apphost-maintenance [--clear | --enter-if-absent] [--root <dir>] [--watchdog-task-name <name>]";

        /// <summary>Leave maintenance: re-enable the watchdog, then remove the marker.</summary>
        public bool Clear { get; init; }

        /// <summary>Enter only when the marker is absent (create-new); exit 2 when it exists.</summary>
        public bool EnterIfAbsent { get; init; }

        /// <summary>The checkout (its logs directory holds the marker).</summary>
        public string Root { get; init; } = "";

        /// <summary>The watchdog task.</summary>
        public string WatchdogTaskName { get; init; } = AppHost.WatchdogTaskName;

        /// <summary>The flags after the command name; an empty --root is the CLI's checkout.</summary>
        public static AppHostMaintenanceOptions Parse(IReadOnlyList<string> arguments, string defaultRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--root", "--watchdog-task-name" }, new[] { "--clear", "--enter-if-absent" });
            var root = flags.Value("--root", "");
            var task = flags.Value("--watchdog-task-name", "");
            return new AppHostMaintenanceOptions
            {
                Clear = flags.Switch("--clear"),
                EnterIfAbsent = flags.Switch("--enter-if-absent"),
                Root = string.IsNullOrEmpty(root) ? defaultRoot ?? "" : root,
                WatchdogTaskName = string.IsNullOrEmpty(task) ? AppHost.WatchdogTaskName : task,
            };
        }
    }

    /// <summary>The apphost-watchdog-state-observer command's options (the observer script's parameters in kebab case).</summary>
    public sealed class AppHostObserverOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "apphost-watchdog-state-observer [--task-name <name>] [--root <dir>] [--state-file <path>] [--marker-file <path>] [--log-file <path>]";

        /// <summary>The watchdog task sampled.</summary>
        public string TaskName { get; init; } = AppHost.WatchdogTaskName;

        /// <summary>The checkout.</summary>
        public string Root { get; init; } = "";

        /// <summary>The document (default logs/apphost-watchdog-state.json).</summary>
        public string StateFile { get; init; } = "";

        /// <summary>The maintenance marker (default logs/apphost.down-on-purpose).</summary>
        public string MarkerFile { get; init; } = "";

        /// <summary>The observer's log (default logs/apphost-watchdog-state-observer.log).</summary>
        public string LogFile { get; init; } = "";

        /// <summary>The flags after the command name; an empty --root is the CLI's checkout.</summary>
        public static AppHostObserverOptions Parse(IReadOnlyList<string> arguments, string defaultRoot)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--task-name", "--root", "--state-file", "--marker-file", "--log-file" }, Array.Empty<string>());
            var root = flags.Value("--root", "");
            var task = flags.Value("--task-name", "");
            return new AppHostObserverOptions
            {
                TaskName = string.IsNullOrEmpty(task) ? AppHost.WatchdogTaskName : task,
                Root = string.IsNullOrEmpty(root) ? defaultRoot ?? "" : root,
                StateFile = flags.Value("--state-file", "") ?? "",
                MarkerFile = flags.Value("--marker-file", "") ?? "",
                LogFile = flags.Value("--log-file", "") ?? "",
            };
        }
    }

    /// <summary>The maintenance helper (set-apphost-maintenance.ps1's body).</summary>
    public static class AppHostMaintenance
    {
        /// <summary>Enters or leaves maintenance; exit 0, or 2 (--clear with --enter-if-absent, or the marker already present for --enter-if-absent).</summary>
        public static int Run(AppHostMaintenanceOptions options, NightlyHost host, IAppHostPlatform platform)
        {
            var files = platform.Files;
            var logDir = AppHost.LogDir(files, options.Root);
            var marker = files.Path.Combine(logDir, "apphost.down-on-purpose");
            var task = options.WatchdogTaskName;
            void Say(string line) => host.Out.WriteLine(line);
            files.Directory.CreateDirectory(logDir);

            if (options.Clear && options.EnterIfAbsent)
            {
                Say("REFUSED: --clear and --enter-if-absent cannot be combined.");
                return 2;
            }
            if (options.EnterIfAbsent)
            {
                Say("> Entering AppHost maintenance only when the marker is absent.");
                try
                {
                    using var stream = files.FileStream.New(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                    var bytes = Encoding.ASCII.GetBytes("down-on-purpose " + AppHost.Round(host.UtcNow));
                    stream.Write(bytes, 0, bytes.Length);
                }
                catch (IOException)
                {
                    Say("  Marker already present. This run did not enter maintenance.");
                    return 2;
                }
                Say("  Wrote " + marker);
                if (!platform.IsWindows) Say("  Watchdog task cmdlet is not available; marker is in place.");
                else Disable(platform, task, Say);
                Observe(options, host, platform);
                Say("Done. This command does not start or stop the AppHost.");
                return 0;
            }
            if (options.Clear)
            {
                Say("> Leaving AppHost maintenance (re-enable '" + task + "', then remove marker).");
                if (platform.Task(task).Registered)
                {
                    platform.SetTaskEnabled(task, true);
                    Say("  Watchdog task enabled.");
                }
                else
                {
                    Say("  Watchdog task '" + task + "' is not registered; marker will still be removed.");
                }
                if (files.File.Exists(marker))
                {
                    files.File.Delete(marker);
                    Say("  Removed " + marker);
                }
                else
                {
                    Say("  Marker already absent.");
                }
            }
            else
            {
                Say("> Entering AppHost maintenance (create marker, then disable '" + task + "').");
                if (!files.File.Exists(marker))
                {
                    files.File.WriteAllText(marker, "down-on-purpose " + AppHost.Round(host.UtcNow) + Environment.NewLine, Encoding.ASCII);
                    Say("  Wrote " + marker);
                }
                else
                {
                    Say("  Marker already present.");
                }
                Disable(platform, task, Say);
            }
            Observe(options, host, platform);
            Say("Done. This command does not start or stop the AppHost.");
            return 0;
        }

        private static void Disable(IAppHostPlatform platform, string task, Action<string> say)
        {
            if (platform.Task(task).Registered)
            {
                platform.SetTaskEnabled(task, false);
                say("  Watchdog task disabled.");
            }
            else
            {
                say("  Watchdog task '" + task + "' is not registered; marker is in place.");
            }
        }

        private static void Observe(AppHostMaintenanceOptions options, NightlyHost host, IAppHostPlatform platform) =>
            AppHostWatchdogStateObserver.Run(new AppHostObserverOptions { TaskName = options.WatchdogTaskName, Root = options.Root }, host, platform);
    }

    /// <summary>The watchdog-state observer (apphost-watchdog-state-observer.ps1's body).</summary>
    public static class AppHostWatchdogStateObserver
    {
        /// <summary>Samples the task into the state document; exit 0.</summary>
        public static int Run(AppHostObserverOptions options, NightlyHost host, IAppHostPlatform platform)
        {
            var files = platform.Files;
            var logDir = AppHost.LogDir(files, options.Root);
            var stateFile = string.IsNullOrEmpty(options.StateFile) ? files.Path.Combine(logDir, "apphost-watchdog-state.json") : options.StateFile;
            var markerFile = string.IsNullOrEmpty(options.MarkerFile) ? files.Path.Combine(logDir, "apphost.down-on-purpose") : options.MarkerFile;
            var logFile = string.IsNullOrEmpty(options.LogFile) ? files.Path.Combine(logDir, "apphost-watchdog-state-observer.log") : options.LogFile;
            files.Directory.CreateDirectory(logDir);
            void Log(string level, string message)
            {
                var line = host.Clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [watchdog-state] [" + level + "] " + message;
                host.Out.WriteLine(line);
                try { files.File.AppendAllText(logFile, line + Environment.NewLine, new UTF8Encoding(false)); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }

            var observedAt = AppHost.Round(host.UtcNow);
            var info = platform.Task(options.TaskName);
            var state = info.State;
            var healthy = state == "Enabled";
            var maintenance = files.File.Exists(markerFile);

            JsonObject previous = null;
            if (files.File.Exists(stateFile))
            {
                try
                {
                    var raw = files.File.ReadAllText(stateFile);
                    if (!string.IsNullOrWhiteSpace(raw)) previous = JsonNode.Parse(raw) as JsonObject;
                }
                catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException)
                {
                    Log("WARN", "previous state unreadable (" + e.Message + ")");
                }
            }
            string episode = null, disabledSince = null;
            if (!healthy)
            {
                var previousEpisode = AppHostWatchdog.Text(previous?["episodeId"]);
                var previousSince = AppHostWatchdog.Text(previous?["disabledSinceUtc"]);
                var same = previous != null && string.Equals(AppHostWatchdog.Text(previous["state"]), state, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrEmpty(previousEpisode) && !string.IsNullOrEmpty(previousSince);
                if (same)
                {
                    episode = previousEpisode;
                    disabledSince = previousSince;
                }
                else
                {
                    episode = Guid.NewGuid().ToString("D");
                    disabledSince = observedAt;
                }
            }
            var payload = new JsonObject
            {
                ["observedAtUtc"] = observedAt, ["taskName"] = options.TaskName, ["state"] = state, ["healthy"] = healthy, ["maintenance"] = maintenance,
                ["disabledSinceUtc"] = disabledSince, ["episodeId"] = episode, ["detail"] = info.Detail,
            };
            var temp = stateFile + "." + platform.CurrentPid.ToString(CultureInfo.InvariantCulture) + ".tmp";
            try
            {
                files.File.WriteAllText(temp, payload.ToJsonString() + Environment.NewLine, new UTF8Encoding(false));
                files.File.Copy(temp, stateFile, overwrite: true);
            }
            finally
            {
                try { if (files.File.Exists(temp)) files.File.Delete(temp); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
            Log("INFO", "state=" + state + " healthy=" + (healthy ? "True" : "False") + " maintenance=" + (maintenance ? "True" : "False") + " episode=" + episode);
            return 0;
        }
    }
}
