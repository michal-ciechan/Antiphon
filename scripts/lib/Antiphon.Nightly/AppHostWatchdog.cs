// CARD-0075, CARD-0310, CARD-0543, CARD-1527: the health-triggered desktop AppHost watchdog (unit 31 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md; it replaced the body of scripts/watchdog-apphost.ps1, which
// stays a thin launcher because the per-user Scheduled Task "Antiphon AppHost Watchdog" (the install-autostart command) starts
// it by path every two minutes). The CLI runs it as
//
//   antiphon-scripts watchdog-apphost [--probe-only] [--cooldown-minutes 15] [--max-restarts-per-window 3] [--interval-seconds 15]
//     [--lock-max-age-minutes 15] [--probe-timeout-sec 5] [--health-url <url>] [--client-url <url>]
//
// One fire, in order: (1) observe API health and the outage's duration before any recovery guard, and emit the independent
// Event Log, marker (logs/apphost-down.alert) and optional webhook (ANTIPHON_WATCHDOG_ALERT_WEBHOOK, https only) signals at
// 3 minutes; then skip during intentional maintenance (logs/apphost.down-on-purpose) or while a launch or restart is in
// flight (either lock younger than 15 minutes, even with an exited holder, or the "Antiphon AppHost" logon task Running). A
// proven-dead restart generation may be reclaimed after two distinct fires (CARD-1527). (2) GET /health (expect 200) and the
// client (2xx/3xx); a restart-worthy failure is re-probed twice at --interval-seconds; a /health timeout while the client
// answers is not a failed round (CARD-0310). (3) Docker not answering skips without spending the flap budget. (4) A restart
// stamped under --cooldown-minutes ago skips. (5) --max-restarts-per-window restarts in the last 60 minutes skip with ERROR.
// (6) The locks are re-checked (CARD-0075: the first check is up to a minute old). (7) restart-apphost runs in this process;
// its last 40 lines and named exit go to logs/watchdog-apphost.log, and logs/apphost-watchdog.state is stamped unless it
// refused (exit 3: nothing killed, no flap budget spent).
//
// --probe-only probes and logs and never restarts. Overlapping fires are serialised by logs/apphost-watchdog.fire.lock. An
// unreadable state file stops the fire (exit 1) so the outage and restart history are kept for repair. Exit 0 otherwise.
// Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Antiphon.Nightly
{
    /// <summary>The watchdog-apphost command's options (watchdog-apphost.ps1's parameters in kebab case).</summary>
    public sealed class AppHostWatchdogOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "watchdog-apphost [--probe-only] [--cooldown-minutes 15] [--max-restarts-per-window 3] [--interval-seconds 15] "
            + "[--lock-max-age-minutes 15] [--probe-timeout-sec 5] [--health-url <url>] [--client-url <url>]";

        private static readonly string[] Valued = { "--cooldown-minutes", "--max-restarts-per-window", "--interval-seconds", "--lock-max-age-minutes", "--probe-timeout-sec", "--health-url", "--client-url" };

        /// <summary>Probe and log; never restart.</summary>
        public bool ProbeOnly { get; init; }

        /// <summary>Skip a restart within this many minutes of the last one.</summary>
        public int CooldownMinutes { get; init; } = AppHost.LockMaxAgeMinutes;

        /// <summary>The flap cap within the 60-minute window.</summary>
        public int MaxRestartsPerWindow { get; init; } = 3;

        /// <summary>Seconds between probe rounds.</summary>
        public int IntervalSeconds { get; init; } = 15;

        /// <summary>A lock older than this is ignored.</summary>
        public int LockMaxAgeMinutes { get; init; } = AppHost.LockMaxAgeMinutes;

        /// <summary>Each probe's timeout.</summary>
        public int ProbeTimeoutSec { get; init; } = 5;

        /// <summary>The health endpoint.</summary>
        public string HealthUrl { get; init; } = "http://localhost:17202/health";

        /// <summary>The client endpoint.</summary>
        public string ClientUrl { get; init; } = "http://localhost:17203/";

        /// <summary>The checkout whose stack is watched.</summary>
        public string RepoRoot { get; init; } = "";

        /// <summary>The flags after the command name; the checkout comes from the CLI.</summary>
        public static AppHostWatchdogOptions Parse(IReadOnlyList<string> arguments, string repoRoot)
        {
            var flags = NightlyArgs.Parse(arguments, Valued, new[] { "--probe-only" });
            return new AppHostWatchdogOptions
            {
                ProbeOnly = flags.Switch("--probe-only"),
                CooldownMinutes = flags.Int("--cooldown-minutes", AppHost.LockMaxAgeMinutes),
                MaxRestartsPerWindow = flags.Int("--max-restarts-per-window", 3),
                IntervalSeconds = flags.Int("--interval-seconds", 15),
                LockMaxAgeMinutes = flags.Int("--lock-max-age-minutes", AppHost.LockMaxAgeMinutes),
                ProbeTimeoutSec = flags.Int("--probe-timeout-sec", 5),
                HealthUrl = flags.Value("--health-url", "http://localhost:17202/health"),
                ClientUrl = flags.Value("--client-url", "http://localhost:17203/"),
                RepoRoot = repoRoot ?? "",
            };
        }
    }

    /// <summary>A restart-apphost run captured for the watchdog log: its exit, the exit's name and its last lines.</summary>
    public sealed record AppHostRestartCapture(int ExitCode, string ExitName, IReadOnlyList<string> Tail);

    /// <summary>The desktop AppHost watchdog (watchdog-apphost.ps1's body).</summary>
    public static class AppHostWatchdog
    {
        private const int RoundsNeeded = 3;
        private const int FlapWindowMinutes = 60;
        private static readonly int[] ClientCodes = { 200, 201, 202, 203, 204, 301, 302, 303, 307, 308 };
        private static readonly string[] StateKeys = { "lastOkUtc", "restartsUtc", "deadLaunch", "downSinceUtc", "outageId", "outageAlerted", "pendingAlerts" };

        /// <summary>One probe round.</summary>
        private sealed record Round(bool HealthOk, bool HealthDead, bool Ok, bool RestartWorthy, string Kind, string Summary);

        /// <summary>The state file could not be read: the fire stops and the file is kept for repair.</summary>
        public sealed class StateUnreadableException : Exception
        {
            /// <summary>The fixed message.</summary>
            public StateUnreadableException() : base("Watchdog state unreadable; preserving outage and restart history for repair.") { }
        }

        /// <summary>
        /// One fire. restart runs restart-apphost and captures it (production: in this process); exit 0, or 1 for an unreadable
        /// state file.
        /// </summary>
        public static int Run(AppHostWatchdogOptions options, NightlyHost host, IAppHostPlatform platform, Func<AppHostRestartCapture> restart)
        {
            var fire = new Fire(options, host, platform, restart);
            return fire.Run();
        }

        /// <summary>
        /// Invoke-AppHostRestartCaptured: runs restart-apphost (its default options) on a host whose output is captured, and
        /// returns the exit, its name and the last tailLines non-blank lines (stdout, then stderr).
        /// </summary>
        public static AppHostRestartCapture CaptureRestart(string root, NightlyHost host, IAppHostPlatform platform, int tailLines = 40)
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var error = new StringWriter(CultureInfo.InvariantCulture);
            var captured = new NightlyHost
            {
                Clock = host.Clock, Git = host.Git, Launcher = host.Launcher, Processes = host.Processes, Probe = host.Probe, Out = output, Error = error,
                Trace = host.Trace, Variables = host.Variables, Sleep = host.Sleep, Qualification = host.Qualification,
            };
            int code;
            try { code = AppHostRestart.Run(new AppHostRestartOptions { RepoRoot = root }, captured, platform); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                return new AppHostRestartCapture(-1, "-1", new[] { e.Message });
            }
            var lines = (output.ToString() + "\n" + error.ToString()).Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Trim().Length > 0).ToArray();
            return new AppHostRestartCapture(code, AppHostProbes.ExitName(code), lines.Skip(Math.Max(0, lines.Length - tailLines)).ToArray());
        }

        /// <summary>
        /// Update-AppHostOutage: maintenance ends observation without a recovery; health clears the outage (queuing a recovery
        /// event when the outage had alerted); otherwise the outage starts (or continues) and a down event is queued once it is
        /// 180 s old.
        /// </summary>
        public static void UpdateOutage(JsonObject state, bool healthOk, bool maintenance, DateTime nowUtc)
        {
            var down = AppHostStamp.ToUtc(state["downSinceUtc"]);
            var episode = Text(state["outageId"]);
            var alerted = Bool(state["outageAlerted"]);
            var pending = state["pendingAlerts"] is JsonArray existing ? existing.Select(node => node?.DeepClone()).ToList() : new List<JsonNode>();
            if (maintenance)
            {
                down = null; episode = null; alerted = false; pending.Clear();
            }
            else if (healthOk)
            {
                if (alerted && !string.IsNullOrEmpty(episode) && down.HasValue) pending.Add(Event(episode + ":recovery", "recovery", down.Value, nowUtc));
                down = null; episode = null; alerted = false;
            }
            else
            {
                if (!down.HasValue || down.Value > nowUtc) down = nowUtc;
                if (string.IsNullOrEmpty(episode)) episode = "apphost:" + Guid.NewGuid().ToString("N");
                if (!alerted && (nowUtc - down.Value).TotalSeconds >= 180)
                {
                    pending.Add(Event(episode + ":down", "down", down.Value, nowUtc));
                    alerted = true;
                }
            }
            state["downSinceUtc"] = down.HasValue ? AppHost.Round(down.Value) : null;
            state["outageId"] = episode;
            state["outageAlerted"] = alerted;
            state["pendingAlerts"] = new JsonArray(pending.ToArray());
        }

        private static JsonObject Event(string id, string kind, DateTime down, DateTime now) =>
            new JsonObject { ["Id"] = id, ["Kind"] = kind, ["DownSinceUtc"] = AppHost.Round(down), ["ObservedUtc"] = AppHost.Round(now) };

        /// <summary>
        /// Update-AppHostAlertMarker: logs/apphost-down.alert carries the current alerted outage (its start, minutes down, the last
        /// restart attempt) and is removed otherwise; written through a temporary file. False when it cannot be updated.
        /// </summary>
        public static bool UpdateMarker(IFileSystem files, string root, JsonObject state, DateTime nowUtc)
        {
            string temp = null;
            try
            {
                var path = AppHost.LogFile(files, root, "apphost-down.alert");
                var down = AppHostStamp.ToUtc(state["downSinceUtc"]);
                if (!down.HasValue || !Bool(state["outageAlerted"]))
                {
                    // The current observation, not a queued event: an old down webhook retry never recreates the marker.
                    if (files.File.Exists(path)) files.File.Delete(path);
                    return true;
                }
                DateTime? last = null;
                if (state["restartsUtc"] is JsonArray restarts)
                    foreach (var stamp in restarts)
                    {
                        var parsed = AppHostStamp.ToUtc(stamp);
                        if (parsed.HasValue && (!last.HasValue || parsed.Value > last.Value)) last = parsed;
                    }
                var payload = new JsonObject
                {
                    ["downSinceUtc"] = AppHost.Round(down.Value),
                    ["minutesDown"] = Math.Round(Math.Max(0, (nowUtc - down.Value).TotalMinutes), 2),
                    ["lastRestartAttemptUtc"] = last.HasValue ? AppHost.Round(last.Value) : null,
                };
                temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                files.File.WriteAllText(temp, payload.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.ASCII);
                files.File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return false;
            }
            finally
            {
                try { if (temp != null && files.File.Exists(temp)) files.File.Delete(temp); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }

        /// <summary>
        /// Send-AppHostOutageEvent: the three sinks are independent. The marker is refreshed even with no event; an event's Event
        /// Log entry is attempted once (its attempt persisted first, so a lost process does not repeat it); the webhook is sent
        /// until it succeeds (deferred while an earlier one is pending). True when the event is delivered as far as configured.
        /// </summary>
        public static bool SendEvent(IAppHostPlatform platform, NightlyHost host, JsonObject outageEvent, string root, JsonObject state, DateTime nowUtc, bool deferWebhook,
            Action persist)
        {
            var markerOk = UpdateMarker(platform.Files, root, state, nowUtc);
            if (outageEvent == null) return markerOk;
            outageEvent["MarkerUpdated"] = markerOk;
            if (!Bool(outageEvent["EventLogAttempted"]))
            {
                outageEvent["EventLogAttempted"] = true;
                persist?.Invoke();
                var message = "AppHost " + Text(outageEvent["Kind"]) + "; outage began " + Text(outageEvent["DownSinceUtc"]) + "; observed " + Text(outageEvent["ObservedUtc"])
                    + "; event " + Text(outageEvent["Id"]);
                outageEvent["EventLogWritten"] = platform.WriteEvent(message, Text(outageEvent["Kind"]) == "recovery");
            }
            if (Bool(outageEvent["WebhookComplete"])) return true;
            var endpoint = host.Variables("ANTIPHON_WATCHDOG_ALERT_WEBHOOK");
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                outageEvent["WebhookConfigured"] = false;
                outageEvent["WebhookComplete"] = true;
                return true;
            }
            outageEvent["WebhookConfigured"] = true;
            if (deferWebhook) return false;
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme != "https") return false;
            // Only the notification is sent, never the local delivery bookkeeping; neither the URL nor the answer is logged.
            var body = new JsonObject
            {
                ["Id"] = Text(outageEvent["Id"]), ["Kind"] = Text(outageEvent["Kind"]), ["DownSinceUtc"] = Text(outageEvent["DownSinceUtc"]), ["ObservedUtc"] = Text(outageEvent["ObservedUtc"]),
            };
            if (!platform.PostWebhook(uri, Text(outageEvent["Id"]), body.ToJsonString(), 5)) return false;
            outageEvent["WebhookComplete"] = true;
            return true;
        }

        internal static string Text(JsonNode node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

        internal static bool Bool(JsonNode node) => node is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

        private sealed class Fire
        {
            private readonly AppHostWatchdogOptions _options;
            private readonly NightlyHost _host;
            private readonly IAppHostPlatform _platform;
            private readonly Func<AppHostRestartCapture> _restart;
            private readonly IFileSystem _files;
            private readonly string _root;
            private readonly string _logFile;
            private readonly string _launchLock;
            private readonly string _restartLock;
            private readonly string _stateFile;
            private readonly string _fireId = Guid.NewGuid().ToString("N");
            private JsonObject _state;
            private bool _maintenance;
            private Round _first;

            public Fire(AppHostWatchdogOptions options, NightlyHost host, IAppHostPlatform platform, Func<AppHostRestartCapture> restart)
            {
                _options = options;
                _host = host;
                _platform = platform;
                _restart = restart;
                _files = platform.Files;
                _root = options.RepoRoot;
                _logFile = AppHost.LogFile(_files, _root, "watchdog-apphost.log");
                _launchLock = AppHost.LogFile(_files, _root, "apphost.launch.lock");
                _restartLock = AppHost.LogFile(_files, _root, "apphost.restart.lock");
                _stateFile = AppHost.LogFile(_files, _root, "apphost-watchdog.state");
            }

            public int Run()
            {
                var logDir = AppHost.LogDir(_files, _root);
                _files.Directory.CreateDirectory(logDir);
                // Overlapping scheduler fires for this checkout are serialised; the lease's path is the mutex.
                Stream lease;
                try { lease = _files.FileStream.New(_files.Path.Combine(logDir, "apphost-watchdog.fire.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    Log("INFO", "skip: another watchdog fire owns observation");
                    return 0;
                }
                using (lease)
                {
                    try
                    {
                        return Observe();
                    }
                    catch (StateUnreadableException e)
                    {
                        _host.Error.WriteLine(e.Message);
                        return 1;
                    }
                }
            }

            private int Observe()
            {
                _state = ReadState();
                _maintenance = _files.File.Exists(AppHost.LogFile(_files, _root, "apphost.down-on-purpose"));
                _first = Probe();
                ObserveOutage(_first);
                if (_maintenance)
                {
                    _state["deadLaunch"] = null;
                    WriteState();
                    Log("INFO", "skip: intentional AppHost maintenance");
                    return 0;
                }
                Log("INFO", "fire beginning (PID " + _platform.CurrentPid.ToString(CultureInfo.InvariantCulture) + (_options.ProbeOnly ? "; ProbeOnly" : "") + ")");
                var indexLock = AppHostGitIndexLock.Read(_platform, _host, _root);
                if (indexLock != null && indexLock.Stale) Log("WARN", indexLock.Note());

                var inFlight = LaunchInFlight(false);
                if (inFlight != null)
                {
                    Log("INFO", "skip: launch in flight - " + inFlight);
                    return 0;
                }

                var downRounds = 0;
                var summary = "";
                for (var i = 1; i <= RoundsNeeded; i++)
                {
                    Round round;
                    if (i == 1) round = _first;
                    else
                    {
                        round = Probe();
                        ObserveOutage(round);
                    }
                    summary = round.Summary;
                    if (round.Ok)
                    {
                        if (i > 1) Log("INFO", "probe round " + i.ToString(CultureInfo.InvariantCulture) + "/" + RoundsNeeded.ToString(CultureInfo.InvariantCulture) + " recovered: " + summary);
                        _state = ReadState();
                        var lastOk = AppHostStamp.ToUtc(_state["lastOkUtc"]);
                        var now = _host.UtcNow;
                        if (_options.ProbeOnly || !lastOk.HasValue || (now - lastOk.Value).TotalHours >= 1)
                        {
                            Log("INFO", "OK " + summary);
                            _state["lastOkUtc"] = AppHost.Round(now);
                            WriteState();
                        }
                        return 0;
                    }
                    if (!round.RestartWorthy)
                    {
                        Log("WARN", "health slow, client up - not counting as down (" + summary + ")");
                        if (i < RoundsNeeded) _host.Sleep(TimeSpan.FromSeconds(_options.IntervalSeconds));
                        continue;
                    }
                    downRounds++;
                    Log("WARN", "probe round " + i.ToString(CultureInfo.InvariantCulture) + "/" + RoundsNeeded.ToString(CultureInfo.InvariantCulture) + " failed: " + summary);
                    if (i < RoundsNeeded) _host.Sleep(TimeSpan.FromSeconds(_options.IntervalSeconds));
                }
                if (downRounds < RoundsNeeded)
                {
                    Log("WARN", "health slow, client up - not counting as down (" + summary + ")");
                    return 0;
                }
                Log("WARN", "all " + RoundsNeeded.ToString(CultureInfo.InvariantCulture) + " probe rounds failed (" + summary + ")");

                // Docker down is not a reason to bounce the AppHost: the launch would fail anyway.
                if (_platform.Run("docker", new[] { "info" }, 0).ExitCode != 0)
                {
                    Log("WARN", "skip: Docker is not responsive (not counting against flap budget)");
                    return 0;
                }

                _state = ReadState();
                var fireNow = _host.UtcNow;
                var recent = Restarts().Where(stamp => (fireNow - stamp).TotalMinutes < FlapWindowMinutes).OrderBy(stamp => stamp).ToList();
                if (recent.Count > 0)
                {
                    var minutesAgo = (fireNow - recent[recent.Count - 1]).TotalMinutes;
                    if (minutesAgo < _options.CooldownMinutes)
                    {
                        Log("INFO", "skip: cooldown (" + AppHost.N1(minutesAgo) + " min since last restart, need " + _options.CooldownMinutes.ToString(CultureInfo.InvariantCulture) + ")");
                        return 0;
                    }
                }
                if (recent.Count >= _options.MaxRestartsPerWindow)
                {
                    Log("ERROR", "flap cap: " + recent.Count.ToString(CultureInfo.InvariantCulture) + " restarts in the last " + FlapWindowMinutes.ToString(CultureInfo.InvariantCulture)
                        + " min - not restarting until the window clears");
                    return 0;
                }
                if (_options.ProbeOnly)
                {
                    Log("INFO", "ProbeOnly: would restart via restart-apphost");
                    return 0;
                }
                // CARD-0075: the first check is up to a minute old (three rounds and docker info); re-check right before acting.
                inFlight = LaunchInFlight(true);
                if (inFlight != null)
                {
                    Log("INFO", "skip: launch in flight at restart time - " + inFlight);
                    return 0;
                }

                Log("WARN", "restarting AppHost via restart-apphost");
                var captured = _restart();
                foreach (var line in captured.Tail) Log("INFO", "restart-apphost: " + line);
                // Exit 3 refused and killed nothing: stamping it would spend the cooldown on a restart that never happened.
                if (captured.ExitCode == 3)
                {
                    Log("INFO", "restart-apphost REFUSED (" + captured.ExitName + ", nothing killed) - not stamping a restart");
                    return 0;
                }
                _state = ReadState();
                var kept = Restarts().Where(stamp => (fireNow - stamp).TotalMinutes < FlapWindowMinutes).Select(stamp => (JsonNode)JsonValue.Create(AppHost.Round(stamp))).ToList();
                kept.Add(JsonValue.Create(AppHost.Round(_host.UtcNow)));
                _state["restartsUtc"] = new JsonArray(kept.ToArray());
                SendEvent(_platform, _host, null, _root, _state, _host.UtcNow, false, null);
                WriteState();
                Log("INFO", "restart-apphost exited " + captured.ExitName + "; stamped restart in " + _stateFile);
                return 0;
            }

            private IEnumerable<DateTime> Restarts()
            {
                if (_state["restartsUtc"] is not JsonArray restarts) yield break;
                foreach (var stamp in restarts)
                {
                    var parsed = AppHostStamp.ToUtc(stamp);
                    if (parsed.HasValue) yield return parsed.Value;
                }
            }

            private Round Probe()
            {
                var health = Http(_options.HealthUrl, new[] { 200 });
                var client = Http(_options.ClientUrl, ClientCodes);
                var kind = AppHostProbes.Classify(health.Ok, health.Error, health.Code, client.Ok, client.Error, client.Code);
                return new Round(health.Ok, !health.Ok && AppHostProbes.IsConnectionRefused(health.Error), kind.Kind == "Up", kind.RestartWorthy, kind.Kind, kind.Summary);
            }

            // Test-HttpOk: ok when the answer's status is one of the codes.
            private (bool Ok, int? Code, string Error) Http(string url, int[] codes)
            {
                var answer = _platform.Get(url, _options.ProbeTimeoutSec);
                if (answer.StatusCode.HasValue && codes.Contains(answer.StatusCode.Value)) return (true, answer.StatusCode, null);
                return (false, answer.StatusCode, answer.Error);
            }

            private void ObserveOutage(Round round)
            {
                UpdateOutage(_state, round.HealthOk, _maintenance, _host.UtcNow);
                // Intent precedes transport: a sent event goes to the local journal; a pending one keeps its id.
                WriteState();
                if (_options.ProbeOnly) return;
                if (!SendEvent(_platform, _host, null, _root, _state, _host.UtcNow, false, null))
                    Log("WARN", "outage marker could not be updated; other alert sinks remain independent");
                if (_maintenance) return;
                var blocked = false;
                var pending = _state["pendingAlerts"] is JsonArray alerts ? alerts.OfType<JsonObject>().ToList() : new List<JsonObject>();
                foreach (var outageEvent in pending)
                {
                    var complete = SendEvent(_platform, _host, outageEvent, _root, _state, _host.UtcNow, blocked, WriteState);
                    WriteState();
                    var id = Text(outageEvent["Id"]);
                    if (!complete)
                    {
                        Log("WARN", "optional outage webhook pending (" + id + "); local sinks were attempted independently");
                        blocked = true;
                        continue;
                    }
                    _files.File.AppendAllText(AppHost.LogFile(_files, _root, "apphost-outage-events.jsonl"), outageEvent.ToJsonString() + Environment.NewLine, new UTF8Encoding(false));
                    // In place: the events still to send are these same nodes.
                    if (_state["pendingAlerts"] is JsonArray current)
                        foreach (var sent in current.Where(node => node is JsonObject other && Text(other["Id"]) == id).ToList()) current.Remove(sent);
                    WriteState();
                }
            }

            private string LaunchInFlight(bool revalidate)
            {
                var restart = AppHostLocks.Read(_platform, _restartLock, _host.UtcNow);
                var launchHeld = AppHostLocks.Active(_platform, _launchLock, _options.LockMaxAgeMinutes, "launch lock", _host.UtcNow);
                var activity = AppHostDeadLaunch.Activity(_platform, _root, restart);
                var observed = revalidate ? Probe() : _first;
                var decision = AppHostDeadLaunch.Resolve(restart, activity, observed.HealthDead, launchHeld != null, AppHostDeadLaunchProof.From(_state["deadLaunch"]),
                    _fireId, _host.UtcNow, revalidate || _options.ProbeOnly);
                _state["deadLaunch"] = decision.Proof.ToJson();
                WriteState();
                if (launchHeld != null) return launchHeld;
                var restartHeld = AppHostLocks.Active(_platform, _restartLock, _options.LockMaxAgeMinutes, "restart lock", _host.UtcNow);
                if (restartHeld != null && !decision.Reclaim) return restartHeld;
                if (decision.Reclaim) Log("WARN", "dead launch proven on two fires; restart admission must revalidate");
                // The logon task's protection also applies when no restart stamp exists.
                var running = _platform.TaskRunning(AppHost.LogonTaskName);
                if (running == null) return "AppHost logon task inventory unknown";
                if (running.Value) return "AppHost logon task is Running";
                return null;
            }

            private JsonObject ReadState()
            {
                JsonObject state;
                if (_files.File.Exists(_stateFile))
                {
                    try { state = JsonNode.Parse(_files.File.ReadAllText(_stateFile)) as JsonObject; }
                    catch (Exception e) when (e is JsonException || e is IOException || e is UnauthorizedAccessException) { throw new StateUnreadableException(); }
                    if (state == null) throw new StateUnreadableException();
                }
                else
                {
                    state = new JsonObject();
                }
                foreach (var key in StateKeys)
                    if (!state.ContainsKey(key)) state[key] = key == "restartsUtc" || key == "pendingAlerts" ? new JsonArray() : null;
                return state;
            }

            private void WriteState()
            {
                var temp = _stateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    _files.File.WriteAllText(temp, _state.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
                    _files.File.Move(temp, _stateFile, overwrite: true);
                }
                finally
                {
                    if (_files.File.Exists(temp)) _files.File.Delete(temp);
                }
            }

            private void Log(string level, string message)
            {
                var line = _host.Clock.GetLocalNow().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " [watchdog-apphost] [" + level + "] " + message;
                _host.Out.WriteLine(line);
                try { _files.File.AppendAllText(_logFile, line + Environment.NewLine, new UTF8Encoding(false)); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            }
        }
    }
}
