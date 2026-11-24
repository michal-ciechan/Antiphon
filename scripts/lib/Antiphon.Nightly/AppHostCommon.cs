// CARD-0011, CARD-0075, CARD-0152, CARD-0273, CARD-0310, CARD-0543, CARD-0716, CARD-1527, CARD-1680, CARD-1806: the shared
// pieces of the desktop AppHost commands (restart-apphost, watchdog-apphost and verify-dev-stack; unit 31 of
// docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md). They replaced scripts/apphost-common.ps1's lock, probe, log
// and launch functions (the file keeps only the worktree guard that dev-aspire.ps1 and deploy-local.ps1 still dot-source).
//
// LOCK FORMAT (CARD-0011, extended by CARD-0075): one line "<pid> <utc-iso-8601-roundtrip>", which is what dev-aspire.ps1
// writes into logs/apphost.launch.lock. Two locks use it: logs/apphost.launch.lock (dev-aspire.ps1 while it launches) and
// logs/apphost.restart.lock (restart-apphost while it tears the stack down and waits for health). A lock is ignored (stale)
// when its stamp is older than LockMaxAgeMinutes (15, one number shared with the watchdog cooldown). A dead holder with a
// stamp still inside that window is in flight: the writer exited and the child it spawned may still be launching
// (CARD-0310). Lock stamps are UTC: an offset-less stamp is UTC by the format, never local (CARD-0152).
//
// Every observation of the machine is IAppHostPlatform's (AppHostOs on this machine, a fake in tests); git is the host's.
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
using System.Text.RegularExpressions;

namespace Antiphon.Nightly
{
    /// <summary>The AppHost's fixed numbers and names.</summary>
    public static class AppHost
    {
        /// <summary>CARD-0310: one number for lock freshness, leftover-lock keep, and the watchdog cooldown.</summary>
        public const int LockMaxAgeMinutes = 15;

        /// <summary>CARD-0543: the same 300 s threshold as Git:IndexLockStaleAfterSeconds. Nothing here deletes a git lock.</summary>
        public const int GitIndexLockStaleAfterSeconds = 300;

        /// <summary>The server port.</summary>
        public const int ServerPort = 17202;

        /// <summary>The always-on session runner's port, which no AppHost command touches.</summary>
        public const int SessionRunnerPort = 17204;

        /// <summary>The dashboard's pinned port (AppHost launchSettings.json).</summary>
        public const int DashboardPort = 17205;

        /// <summary>The logon task scripts/install-autostart.ps1 registers for the AppHost.</summary>
        public const string LogonTaskName = "Antiphon AppHost";

        /// <summary>The watchdog task scripts/install-autostart.ps1 registers.</summary>
        public const string WatchdogTaskName = "Antiphon AppHost Watchdog";

        /// <summary>Everything the AppHost owns. 17204 (session runner) is deliberately excluded; 17209 is Storybook.</summary>
        public static readonly IReadOnlyList<int> OwnedPorts = new[] { 17200, 17202, 17203, 17205, 17206, 17207, 17209 };

        /// <summary>The ports whose listener means an AppHost is present (CARD-1527 launch activity).</summary>
        public static readonly IReadOnlyList<int> PresencePorts = new[] { 17200, 17202, 17203, 17205, 17206, 17207 };

        /// <summary>The checkout's logs directory.</summary>
        public static string LogDir(IFileSystem files, string root) => files.Path.Combine(root, "logs");

        /// <summary>A file under the checkout's logs directory.</summary>
        public static string LogFile(IFileSystem files, string root, string name) => files.Path.Combine(root, "logs", name);

        /// <summary>Invariant "{0:N1}".</summary>
        public static string N1(double value) => value.ToString("N1", CultureInfo.InvariantCulture);

        /// <summary>Round-trip UTC text.</summary>
        public static string Round(DateTime value) => value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
    }

    /// <summary>ConvertTo-UtcStamp and Get-UtcAgeMinutes: lock and state stamps are UTC.</summary>
    public static class AppHostStamp
    {
        private static readonly Regex Offset = new Regex(@"(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// The stamp as a UTC DateTime, or null when it is blank or does not parse. A Z or +hh:mm suffix keeps its offset;
        /// offset-less text is UTC by the lock format (CARD-0152: never the local clock face).
        /// </summary>
        public static DateTime? ToUtc(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                if (Offset.IsMatch(text)) return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture).UtcDateTime;
                return DateTime.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            }
            catch (FormatException) { return null; }
            catch (ArgumentException) { return null; }
        }

        /// <summary>A JSON value read as a stamp: a string is parsed, anything else is no stamp.</summary>
        public static DateTime? ToUtc(JsonNode node) =>
            node is JsonValue value && value.TryGetValue<string>(out var text) ? ToUtc(text) : null;

        /// <summary>The age in minutes of a UTC stamp at now (an unspecified kind is UTC).</summary>
        public static double AgeMinutes(DateTime stampUtc, DateTime nowUtc) => (Utc(nowUtc) - Utc(stampUtc)).TotalMinutes;

        private static DateTime Utc(DateTime value) =>
            value.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(value, DateTimeKind.Utc) : value.ToUniversalTime();
    }

    /// <summary>Whether a lock holder's process is running: Alive, Dead (no process with the id) or Unknown (the read failed).</summary>
    public enum AppHostHolder
    {
        /// <summary>The process runs.</summary>
        Alive,

        /// <summary>No process has the id.</summary>
        Dead,

        /// <summary>The read failed: treated as alive.</summary>
        Unknown,
    }

    /// <summary>Get-AppHostLock: one lock file read without judging it.</summary>
    public sealed class AppHostLockInfo
    {
        /// <summary>The lock file.</summary>
        public string Path { get; init; }

        /// <summary>The file exists.</summary>
        public bool Exists { get; init; }

        /// <summary>The line is "&lt;pid&gt; &lt;stamp&gt;".</summary>
        public bool Readable { get; init; }

        /// <summary>The holder's id (null when unreadable).</summary>
        public int? ProcessId { get; init; }

        /// <summary>The stamp text as written.</summary>
        public string StampRaw { get; init; }

        /// <summary>The stamp, else the file's last write time (UTC).</summary>
        public DateTime? StampUtc { get; init; }

        /// <summary>The stamp's age at the read.</summary>
        public double? AgeMinutes { get; init; }

        /// <summary>The holder may be running (an unreadable pid counts as alive: freshness alone decides).</summary>
        public bool HolderAlive { get; init; }

        /// <summary>Alive, Dead or Unknown.</summary>
        public string HolderState { get; init; } = "Unknown";

        /// <summary>Format-AppHostLockStamp: the raw stamp, else the UTC stamp, else "stamp missing".</summary>
        public string StampText => !string.IsNullOrEmpty(StampRaw) ? StampRaw : StampUtc.HasValue ? AppHost.Round(StampUtc.Value) : "stamp missing";

        /// <summary>Format-AppHostLockAge: "12.3 min old", else "age unknown".</summary>
        public string AgeText => AgeMinutes.HasValue ? AppHost.N1(AgeMinutes.Value) + " min old" : "age unknown";

        /// <summary>Get-AppHostLockIdentity: "&lt;pid&gt; &lt;stamp&gt;" for a readable lock with a parseable stamp, else null.</summary>
        public string Identity => Readable && AppHostStamp.ToUtc(StampRaw).HasValue
            ? (ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "") + " " + StampRaw
            : null;
    }

    /// <summary>A lock this process took (New-AppHostLock): the held stream and the line written; Reason when it was refused.</summary>
    public sealed class AppHostLockHandle
    {
        /// <summary>The lock is held.</summary>
        public bool Acquired { get; init; }

        /// <summary>The lock file.</summary>
        public string Path { get; init; }

        /// <summary>Why the lock was not taken (operator-facing).</summary>
        public string Reason { get; init; }

        /// <summary>The line this process wrote.</summary>
        public string Identity { get; init; }

        internal Stream Stream { get; set; }

        internal IFileSystem Files { get; init; }

        /// <summary>
        /// Remove-AppHostLock: closes the held stream; keepFile leaves the stamp on disk so a later check still sees an in-flight
        /// restart after this process has exited (CARD-0310). Otherwise the file is removed when it still holds this lock's line.
        /// </summary>
        public void Release(bool keepFile)
        {
            if (!Acquired) return;
            try { Stream?.Dispose(); }
            catch (IOException) { }
            Stream = null;
            if (keepFile) return;
            try
            {
                if (!string.IsNullOrEmpty(Identity) && string.Equals(Files.File.ReadAllText(Path).Trim(), Identity, StringComparison.Ordinal))
                    Files.File.Delete(Path);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>The launch and restart locks (Get-AppHostLock, Test-AppHostLockActive, New-AppHostLock, New-AppHostRestartLock).</summary>
    public static class AppHostLocks
    {
        private static readonly Regex Line = new Regex(@"^(\d+)\s+(\S+)", RegexOptions.CultureInvariant);

        /// <summary>Reads a lock file. Never throws: a half-written lock is unreadable and its file time gives the age.</summary>
        public static AppHostLockInfo Read(IAppHostPlatform platform, string path, DateTime nowUtc)
        {
            var files = platform.Files;
            if (!files.File.Exists(path)) return new AppHostLockInfo { Path = path };
            string raw = null;
            // A live holder keeps its stream open for write: read with read-write sharing, as Get-Content does.
            try { raw = AppHostLaunchLog.ReadShared(files, path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            raw = raw?.Trim();
            var readable = false;
            int? pid = null;
            string stampRaw = null;
            DateTime? stamp = null;
            var match = string.IsNullOrEmpty(raw) ? Match.Empty : Line.Match(raw);
            if (match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
            {
                readable = true;
                pid = parsed;
                stampRaw = match.Groups[2].Value;
                stamp = AppHostStamp.ToUtc(stampRaw);
            }
            if (!stamp.HasValue)
            {
                try { stamp = files.File.GetLastWriteTimeUtc(path); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            double? age = stamp.HasValue ? AppHostStamp.AgeMinutes(stamp.Value, nowUtc) : null;
            // An unparseable pid counts as alive: freshness alone decides (do not kill what might be launching).
            var alive = true;
            var state = "Unknown";
            if (pid.HasValue && pid.Value != 0)
            {
                switch (platform.Holder(pid.Value))
                {
                    case AppHostHolder.Alive: state = "Alive"; break;
                    case AppHostHolder.Dead: alive = false; state = "Dead"; break;
                }
            }
            return new AppHostLockInfo
            {
                Path = path, Exists = true, Readable = readable, ProcessId = pid, StampRaw = stampRaw, StampUtc = stamp, AgeMinutes = age,
                HolderAlive = alive, HolderState = state,
            };
        }

        /// <summary>
        /// Test-AppHostLockActive: null when the lock may be ignored (absent, or its stamp maxAgeMinutes or older); otherwise the
        /// operator-facing reason, with the pid, the stamp and the age. A dead holder with a fresh stamp is still in flight.
        /// </summary>
        public static string Active(IAppHostPlatform platform, string path, int maxAgeMinutes, string label, DateTime nowUtc)
        {
            if (string.IsNullOrEmpty(label)) label = platform.Files.Path.GetFileName(path);
            var info = Read(platform, path, nowUtc);
            if (!info.Exists) return null;
            if (info.AgeMinutes.HasValue && info.AgeMinutes.Value >= maxAgeMinutes) return null;
            var pid = info.ProcessId.GetValueOrDefault();
            if (pid != 0 && !info.HolderAlive)
                return label + " stamp " + info.StampText + " is " + info.AgeText + "; holder PID " + Num(pid) + " exited; child may still be launching (" + path + ")";
            if (pid != 0) return label + " held by PID " + Num(pid) + " (stamp " + info.StampText + ", " + info.AgeText + "; " + path + ")";
            return label + " present but unreadable (stamp " + info.StampText + ", " + info.AgeText + "; " + path + ")";
        }

        /// <summary>
        /// New-AppHostLock: takes the lock with create-new (two racers cannot both see "no lock"), writes "&lt;pid&gt; &lt;now&gt;" and
        /// keeps the stream open (share read) until Release, so nothing can delete a live lock. A stale leftover is removed
        /// once and the create retried; a live or fresh one refuses with Active's reason.
        /// </summary>
        public static AppHostLockHandle Take(IAppHostPlatform platform, string path, int maxAgeMinutes, string label, DateTime nowUtc)
        {
            var files = platform.Files;
            if (string.IsNullOrEmpty(label)) label = files.Path.GetFileName(path);
            var directory = files.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) files.Directory.CreateDirectory(directory);
            for (var attempt = 1; attempt <= 2; attempt++)
            {
                Stream stream;
                try
                {
                    stream = files.FileStream.New(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    var held = Active(platform, path, maxAgeMinutes, label, nowUtc);
                    if (held != null) return new AppHostLockHandle { Path = path, Reason = held, Files = files };
                    if (attempt == 1)
                    {
                        try { files.File.Delete(path); }
                        catch (Exception d) when (d is IOException || d is UnauthorizedAccessException)
                        {
                            return new AppHostLockHandle { Path = path, Files = files, Reason = label + " exists and could not be cleared (" + d.Message + "; " + path + ")" };
                        }
                        continue;
                    }
                    return new AppHostLockHandle { Path = path, Files = files, Reason = label + " could not be taken (" + e.Message + "; " + path + ")" };
                }
                var text = Num(platform.CurrentPid) + " " + AppHost.Round(nowUtc);
                try
                {
                    var bytes = Encoding.ASCII.GetBytes(text);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    try { stream.Dispose(); } catch (IOException) { }
                    try { files.File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    return new AppHostLockHandle { Path = path, Files = files, Reason = label + " could not be written (" + e.Message + "; " + path + ")" };
                }
                return new AppHostLockHandle { Acquired = true, Path = path, Identity = text, Stream = stream, Files = files };
            }
            return new AppHostLockHandle { Path = path, Files = files, Reason = label + " could not be taken (" + path + ")" };
        }

        /// <summary>
        /// New-AppHostRestartLock: the ordinary restart lock, or (CARD-1527) the same generation reclaimed in place when the
        /// watchdog proved it dead on two fires (logs/apphost-watchdog.state deadLaunch Count 2) and this call's own
        /// observation revalidates it: the file is opened for exclusive write without an unlink gap (a held restart stream
        /// denies the open), still carries the proven identity, and AppHostDeadLaunch.Resolve with revalidate reclaims it.
        /// Anything else returns the ordinary refusal.
        /// </summary>
        public static AppHostLockHandle TakeRestart(IAppHostPlatform platform, NightlyHost host, string path, string root, int maxAgeMinutes)
        {
            var ordinary = Take(platform, path, maxAgeMinutes, "restart lock", host.UtcNow);
            if (ordinary.Acquired) return ordinary;
            var files = platform.Files;
            Stream stream = null;
            try
            {
                var state = JsonNode.Parse(files.File.ReadAllText(AppHost.LogFile(files, root, "apphost-watchdog.state"))) as JsonObject;
                var proof = AppHostDeadLaunchProof.From(state?["deadLaunch"]);
                if (proof == null || proof.Count < 2) return ordinary;
                stream = files.FileStream.New(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
                string raw;
                using (var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, leaveOpen: true)) raw = reader.ReadToEnd().Trim();
                var current = Read(platform, path, host.UtcNow);
                if (!string.Equals(raw, current.Identity, StringComparison.Ordinal)) return ordinary;
                var observation = AppHostDeadLaunch.Observe(platform, root, current, host.UtcNow);
                var decision = AppHostDeadLaunch.Resolve(current, observation.Activity, observation.HealthDead, observation.LaunchBlocked, proof, proof.FireId,
                    host.UtcNow, revalidate: true);
                if (!decision.Reclaim) return ordinary;
                var text = Num(platform.CurrentPid) + " " + AppHost.Round(host.UtcNow);
                var bytes = Encoding.ASCII.GetBytes(text);
                stream.Position = 0;
                stream.SetLength(0);
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush();
                var result = new AppHostLockHandle { Acquired = true, Path = path, Identity = text, Stream = stream, Files = files };
                stream = null;
                return result;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException || e is InvalidOperationException || e is FormatException)
            {
                return ordinary;
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private static string Num(int value) => value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>The watchdog's dead-launch proof (logs/apphost-watchdog.state deadLaunch): the lock identity, the fire count, the fire and when.</summary>
    public sealed class AppHostDeadLaunchProof
    {
        /// <summary>The proven lock identity (null when not eligible).</summary>
        public string Identity { get; init; }

        /// <summary>Distinct fires that saw it, at most 2.</summary>
        public int Count { get; init; }

        /// <summary>The fire that wrote it.</summary>
        public string FireId { get; init; }

        /// <summary>When it was observed (round-trip UTC text).</summary>
        public string ObservedUtc { get; init; }

        /// <summary>The state file's form: {"Identity","Count","FireId","ObservedUtc"}.</summary>
        public JsonObject ToJson() => new JsonObject { ["Identity"] = Identity, ["Count"] = Count, ["FireId"] = FireId, ["ObservedUtc"] = ObservedUtc };

        /// <summary>Reads the state file's form; null for a missing or non-object value. A non-integer Count reads as 0.</summary>
        public static AppHostDeadLaunchProof From(JsonNode node)
        {
            if (node is not JsonObject proof) return null;
            var count = 0;
            if (proof["Count"] is JsonValue value)
            {
                if (value.TryGetValue<int>(out var number)) count = number;
                else if (value.TryGetValue<double>(out var real)) count = (int)real;
            }
            return new AppHostDeadLaunchProof { Identity = Text(proof["Identity"]), Count = count, FireId = Text(proof["FireId"]), ObservedUtc = Text(proof["ObservedUtc"]) };
        }

        private static string Text(JsonNode node) => node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
    }

    /// <summary>One dead-launch decision: reclaim, and the proof to record.</summary>
    public sealed record AppHostDeadLaunchDecision(bool Reclaim, AppHostDeadLaunchProof Proof);

    /// <summary>What a dead-launch admission observes: launcher activity, dead health (connection refused only) and a held launch lock.</summary>
    public sealed record AppHostDeadLaunchObservation(string Activity, bool HealthDead, bool LaunchBlocked);

    /// <summary>CARD-1527: observation and admission are separate; absence is never inferred from a failed query.</summary>
    public static class AppHostDeadLaunch
    {
        private static readonly Regex Wrapped = new Regex(@"^(pwsh|powershell|dotnet|dcp|dcpctrl|Antiphon\.AppHost|Antiphon\.Server|Aspire\.Dashboard)(\.exe)?$",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex Dcp = new Regex(@"^(dcp|dcpctrl|Aspire\.Dashboard)(\.exe)?$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex Encoded = new Regex(@"(?i)-(?:EncodedCommand|enc)\s+([A-Za-z0-9+/=]+)", RegexOptions.CultureInvariant);

        /// <summary>
        /// Resolve-AppHostDeadLaunch: a lock generation is eligible when its identity is readable, its age known and not
        /// negative, its holder Dead, launcher activity Absent, health dead and no launch lock blocks. The same identity seen
        /// by a different fire within five minutes counts once more (at most 2); revalidate observes without counting. Two
        /// counts reclaim.
        /// </summary>
        public static AppHostDeadLaunchDecision Resolve(AppHostLockInfo lockInfo, string activity, bool healthDead, bool launchBlocked,
            AppHostDeadLaunchProof previous, string fireId, DateTime nowUtc, bool revalidate)
        {
            var identity = lockInfo?.Identity;
            var age = lockInfo?.AgeMinutes;
            var eligible = identity != null && age.HasValue && age.Value >= 0 && lockInfo.HolderState == "Dead" && activity == "Absent" && healthDead && !launchBlocked;
            var count = 0;
            var previousTime = AppHostStamp.ToUtc(previous?.ObservedUtc);
            var same = eligible && string.Equals(previous?.Identity, identity, StringComparison.Ordinal) && previousTime.HasValue
                && previousTime.Value <= nowUtc && (nowUtc - previousTime.Value).TotalMinutes <= 5;
            if (same) count = previous.Count;
            if (eligible && !revalidate && !string.Equals(previous?.FireId, fireId, StringComparison.OrdinalIgnoreCase)) count = Math.Min(2, count + 1);
            if (!eligible) { identity = null; count = 0; }
            return new AppHostDeadLaunchDecision(eligible && count >= 2,
                new AppHostDeadLaunchProof { Identity = identity, Count = count, FireId = fireId, ObservedUtc = AppHost.Round(nowUtc) });
        }

        /// <summary>Get-AppHostDeadLaunchObservation: launch activity, health (only connection refusal is dead) and the launch lock.</summary>
        public static AppHostDeadLaunchObservation Observe(IAppHostPlatform platform, string root, AppHostLockInfo lockInfo, DateTime nowUtc)
        {
            var health = platform.Get("http://localhost:" + AppHost.ServerPort.ToString(CultureInfo.InvariantCulture) + "/health", 5);
            var launch = AppHostLocks.Active(platform, AppHost.LogFile(platform.Files, root, "apphost.launch.lock"), AppHost.LockMaxAgeMinutes, null, nowUtc);
            return new AppHostDeadLaunchObservation(Activity(platform, root, lockInfo),
                health.StatusCode != 200 && AppHostProbes.IsConnectionRefused(health.Error), launch != null);
        }

        /// <summary>
        /// Get-AppHostLaunchActivity: Present, Absent or Unknown. A full successful census is required (the processes with their
        /// command lines, the listeners, the AppHost logon task); any failed read is Unknown. Present: the logon task Running, a
        /// listener on an AppHost port, a wrapper or child whose command line (an encoded command decoded) names this checkout,
        /// or the recorded wrapper pid when it started before the lock's stamp. An unowned DCP or dashboard is Unknown. This
        /// process and the processes that started it are not a launch.
        /// </summary>
        public static string Activity(IAppHostPlatform platform, string root, AppHostLockInfo lockInfo)
        {
            try
            {
                var processes = platform.Census();
                var listening = platform.ListeningPorts();
                var running = platform.TaskRunning(AppHost.LogonTaskName);
                if (running == null) return "Unknown";
                if (running.Value) return "Present";
                if (listening.Any(port => AppHost.PresencePorts.Contains(port))) return "Present";
                var files = platform.Files;
                var rootText = files.Path.GetFullPath(root).TrimEnd('\\', '/').Replace('/', '\\') + "\\";
                var wrapperPid = 0;
                var pidPath = AppHost.LogFile(files, root, "apphost.pid");
                if (files.File.Exists(pidPath))
                {
                    var pidText = files.File.ReadAllText(pidPath);
                    if (!int.TryParse(pidText.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out wrapperPid)) return "Unknown";
                }
                var stamp = lockInfo?.StampUtc;
                var own = new HashSet<int>(platform.OwnProcessChain());
                foreach (var process in processes)
                {
                    if (own.Contains(process.Pid)) continue;
                    var name = process.Name ?? "";
                    if (!Wrapped.IsMatch(name)) continue;
                    var command = process.CommandLine;
                    if (string.IsNullOrWhiteSpace(command)) return "Unknown";
                    var encoded = Encoded.Match(command);
                    if (encoded.Success)
                    {
                        try { command += " " + Encoding.Unicode.GetString(Convert.FromBase64String(encoded.Groups[1].Value)); }
                        catch (FormatException) { return "Unknown"; }
                    }
                    if (command.Replace('/', '\\').IndexOf(rootText, StringComparison.OrdinalIgnoreCase) >= 0) return "Present";
                    // A matching pid without creation evidence cannot establish reuse; an older wrapper or an unowned DCP vetoes.
                    if (wrapperPid != 0 && process.Pid == wrapperPid)
                    {
                        if (!stamp.HasValue || !process.CreatedUtc.HasValue) return "Unknown";
                        if (process.CreatedUtc.Value <= stamp.Value) return "Present";
                    }
                    if (Dcp.IsMatch(name)) return "Unknown";
                }
                return "Absent";
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is System.ComponentModel.Win32Exception
                || e is NotSupportedException || e is System.Runtime.InteropServices.COMException)
            {
                return "Unknown";
            }
        }
    }

    /// <summary>Get-AppHostWorktreeClassification: the command's checkout and the main worktree, from git rooted at the checkout.</summary>
    public sealed class AppHostWorktree
    {
        /// <summary>Both roots were read.</summary>
        public bool Verified { get; init; }

        /// <summary>The checkout is the main worktree.</summary>
        public bool IsMainWorktree { get; init; }

        /// <summary>The checkout's worktree root (canonical).</summary>
        public string ScriptWorktreeRoot { get; init; }

        /// <summary>The main worktree's root (canonical).</summary>
        public string MainWorktreeRoot { get; init; }

        /// <summary>The failed git step, when not verified.</summary>
        public string Failure { get; init; }

        private static readonly Regex WorktreeRecord = new Regex(@"^worktree\s+(.+)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        /// <summary>
        /// Read-only: git -C root rev-parse --show-toplevel, then worktree list --porcelain; never the current directory. A failure
        /// is structured so a destructive command refuses before it touches a lock, a log, a process, Docker or a launch.
        /// </summary>
        public static AppHostWorktree Classify(NightlyHost host, IFileSystem files, string root)
        {
            NightlyGitResult top;
            try { top = host.RunGit(root, "rev-parse", "--show-toplevel"); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return new AppHostWorktree { Failure = "git -C \"" + root + "\" rev-parse --show-toplevel failed: " + e.Message };
            }
            var topLines = Lines(top.Output);
            var scriptRootText = topLines.FirstOrDefault();
            if (top.ExitCode != 0 || scriptRootText == null)
            {
                var detail = string.Join(" ", topLines);
                return new AppHostWorktree { Failure = "git -C \"" + root + "\" rev-parse --show-toplevel failed (exit " + top.ExitCode.ToString(CultureInfo.InvariantCulture) + ")" + (detail.Length > 0 ? ": " + detail : "") };
            }
            NightlyGitResult list;
            try { list = host.RunGit(scriptRootText, "worktree", "list", "--porcelain"); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException)
            {
                return new AppHostWorktree { Failure = "git -C \"" + scriptRootText + "\" worktree list --porcelain failed: " + e.Message };
            }
            if (list.ExitCode != 0)
            {
                var detail = string.Join(" ", Lines(list.Output));
                return new AppHostWorktree { Failure = "git -C \"" + scriptRootText + "\" worktree list --porcelain failed (exit " + list.ExitCode.ToString(CultureInfo.InvariantCulture) + ")" + (detail.Length > 0 ? ": " + detail : "") };
            }
            var record = (list.Output ?? "").Split('\n').Select(line => line.TrimEnd('\r')).Select(line => WorktreeRecord.Match(line)).FirstOrDefault(m => m.Success);
            if (record == null)
                return new AppHostWorktree { Failure = "git -C \"" + scriptRootText + "\" worktree list --porcelain returned no first worktree path record" };
            string scriptRoot, mainRoot;
            try
            {
                scriptRoot = Canonical(files, scriptRootText);
                mainRoot = Canonical(files, record.Groups[1].Value.Trim());
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is PathTooLongException)
            {
                return new AppHostWorktree { Failure = "could not canonicalize Git worktree paths: " + e.Message };
            }
            return new AppHostWorktree
            {
                Verified = true, ScriptWorktreeRoot = scriptRoot, MainWorktreeRoot = mainRoot,
                IsMainWorktree = string.Equals(scriptRoot, mainRoot, StringComparison.OrdinalIgnoreCase),
            };
        }

        /// <summary>ConvertTo-AppHostCanonicalPath: separators normalised, full path, no trailing separator below the root.</summary>
        public static string Canonical(IFileSystem files, string path)
        {
            var normalised = path.Replace(files.Path.AltDirectorySeparatorChar, files.Path.DirectorySeparatorChar);
            var full = files.Path.GetFullPath(normalised);
            var rootPart = files.Path.GetPathRoot(full) ?? "";
            if (full.Length > rootPart.Length) full = full.TrimEnd(files.Path.DirectorySeparatorChar);
            return full;
        }

        /// <summary>
        /// Format-AppHostWorktreeGuardMessage: the refusal for an unverified root or a linked worktree, or (allowWorktree) the
        /// warning that --allow-worktree lets a linked worktree affect the shared stack. allowFlag is how the caller spells it.
        /// </summary>
        public IReadOnlyList<string> GuardMessage(bool allowWorktree, string allowFlag = "--allow-worktree")
        {
            if (!Verified)
                return new[]
                {
                    "REFUSED: this AppHost command could not verify its Git worktree root.",
                    "  Failed Git step: " + Failure,
                    "  This command controls the shared local ports and can replace the canonical AppHost stack.",
                    "  No teardown or startup occurred.",
                };
            if (allowWorktree)
                return new[]
                {
                    "WARNING: this AppHost command is rooted in a linked Git worktree.",
                    "  Script worktree: " + ScriptWorktreeRoot,
                    "  Main worktree:   " + MainWorktreeRoot,
                    "  The shared local ports are not isolated; this command can replace the canonical AppHost stack.",
                    "  " + allowFlag + " explicitly permits this worktree to affect the shared stack.",
                };
            return new[]
            {
                "REFUSED: this AppHost command is rooted in a linked Git worktree.",
                "  Script worktree: " + ScriptWorktreeRoot,
                "  Main worktree:   " + MainWorktreeRoot,
                "  This command controls the shared local ports and can replace the canonical AppHost stack.",
                "  Re-run it from " + MainWorktreeRoot + ".",
                "  To intentionally exercise this worktree against the shared stack, re-run with " + allowFlag + ".",
                "  Nothing was killed or started.",
            };
        }

        internal static IReadOnlyList<string> Lines(string output) =>
            (output ?? "").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
    }

    /// <summary>Get-AppHostGitIndexLock: a present index.lock of the checkout, its age and an older git process that may hold it.</summary>
    public sealed class AppHostGitIndexLock
    {
        /// <summary>The lock path as git names it.</summary>
        public string Path { get; init; }

        /// <summary>Always true for a returned lock.</summary>
        public bool Present { get; init; } = true;

        /// <summary>The file's last write time (UTC).</summary>
        public DateTime LastWriteTimeUtc { get; init; }

        /// <summary>The file's length.</summary>
        public long Length { get; init; }

        /// <summary>The age at the read.</summary>
        public double AgeMinutes { get; init; }

        /// <summary>Older than 300 s with no git process started before it (an orphan of an interrupted write).</summary>
        public bool Stale { get; init; }

        /// <summary>A git process started before the lock (or whose start cannot be read).</summary>
        public int? HolderPid { get; init; }

        /// <summary>That process's start (null when unreadable).</summary>
        public DateTime? HolderStartUtc { get; init; }

        /// <summary>The checkout's index.lock, or null when git cannot name it or it is absent. Never deletes anything.</summary>
        public static AppHostGitIndexLock Read(IAppHostPlatform platform, NightlyHost host, string root)
        {
            NightlyGitResult result;
            try { result = host.RunGit(root, "rev-parse", "--path-format=absolute", "--git-path", "index.lock"); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException) { return null; }
            if (result.ExitCode != 0) return null;
            var path = AppHostWorktree.Lines(result.Output).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(path)) return null;
            var files = platform.Files;
            if (!files.File.Exists(path)) return null;
            var info = files.FileInfo.New(path);
            var mtime = info.LastWriteTimeUtc;
            var age = host.UtcNow - mtime;
            int? holder = null;
            DateTime? holderStart = null;
            foreach (var process in platform.Processes(new[] { "git" }))
            {
                if (!process.CreatedUtc.HasValue || process.CreatedUtc.Value <= mtime.AddSeconds(2))
                {
                    holder = process.Pid;
                    holderStart = process.CreatedUtc;
                    break;
                }
            }
            return new AppHostGitIndexLock
            {
                Path = path, LastWriteTimeUtc = mtime, Length = info.Length, AgeMinutes = age.TotalMinutes, HolderPid = holder, HolderStartUtc = holderStart,
                Stale = age >= TimeSpan.FromSeconds(AppHost.GitIndexLockStaleAfterSeconds) && holder == null,
            };
        }

        /// <summary>Format-AppHostGitIndexLockNote: the operator note naming the lock, its age, its liveness and the removal command.</summary>
        public string Note()
        {
            var age = TimeSpan.FromMinutes(AgeMinutes);
            var hours = (int)Math.Floor(age.TotalHours);
            string liveness, kind;
            if (Stale)
            {
                liveness = "No git process older than the lock is running; it is an orphan from an interrupted git write";
                kind = "stale";
            }
            else if (HolderPid.HasValue)
            {
                var started = HolderStartUtc.HasValue ? AppHost.Round(HolderStartUtc.Value) : "unreadable";
                liveness = "A git process started before the lock is still running (PID " + HolderPid.Value.ToString(CultureInfo.InvariantCulture) + ", started " + started + ")";
                kind = "held";
            }
            else
            {
                liveness = "The lock is younger than " + AppHost.GitIndexLockStaleAfterSeconds.ToString(CultureInfo.InvariantCulture) + " s";
                kind = "held";
            }
            return "NOTE: git index lock (" + kind + ") present at " + Path + " (age " + hours.ToString(CultureInfo.InvariantCulture) + "h " + age.Minutes.ToString(CultureInfo.InvariantCulture)
                + "m " + age.Seconds.ToString(CultureInfo.InvariantCulture) + "s, " + Length.ToString(CultureInfo.InvariantCulture) + " bytes). " + liveness
                + ". Remove it with: Remove-Item '" + Path + "'. Never remove a lock while a git process older than it is running.";
        }
    }

    /// <summary>One probe round's classification: Up, Slow or Down, whether a restart is warranted, and the summary.</summary>
    public sealed record AppHostProbeClass(string Kind, bool RestartWorthy, string Summary);

    /// <summary>A loaded server identity read from /api/version: Ok with the SHA, or the reason (unavailable, malformed).</summary>
    public sealed record AppHostLoadedIdentity(bool Ok, string Sha, string Reason);

    /// <summary>The probe classifiers and the build identity checks (pure functions of their inputs).</summary>
    public static class AppHostProbes
    {
        private static readonly Regex Refused = new Regex("actively refused|Unable to connect|connection.*refused|No connection could be made", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Timeout = new Regex("timeout|timed out|canceled|cancelled", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly Regex Sha = new Regex("^[0-9a-fA-F]{40}$|^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);

        /// <summary>Test-AppHostProbeErrorIsConnectionRefused.</summary>
        public static bool IsConnectionRefused(string error) => !string.IsNullOrWhiteSpace(error) && Refused.IsMatch(error);

        /// <summary>Test-AppHostProbeErrorIsTimeout: a timeout that is not a refusal.</summary>
        public static bool IsTimeout(string error) => !string.IsNullOrWhiteSpace(error) && !IsConnectionRefused(error) && Timeout.IsMatch(error);

        /// <summary>
        /// Get-AppHostProbeClassification (CARD-0310): both up is Up; a /health timeout while the client answers is Slow (a loaded
        /// or coming-up stack, not a corpse); anything else is Down and restart-worthy.
        /// </summary>
        public static AppHostProbeClass Classify(bool healthOk, string healthError, int? healthCode, bool clientOk, string clientError, int? clientCode)
        {
            var health = healthOk || (healthCode ?? 0) != 0 ? "health=" + Code(healthCode) : "health=FAIL " + healthError;
            var client = clientOk || (clientCode ?? 0) != 0 ? "client=" + Code(clientCode) : "client=FAIL " + clientError;
            var summary = health + " " + client;
            if (healthOk && clientOk) return new AppHostProbeClass("Up", false, summary);
            if (clientOk && !healthOk && IsTimeout(healthError)) return new AppHostProbeClass("Slow", false, summary);
            return new AppHostProbeClass("Down", true, summary);
        }

        /// <summary>Format-AppHostRestartExitName: the watchdog log's name for a restart-apphost exit code.</summary>
        public static string ExitName(int exitCode) => exitCode switch
        {
            0 => "0=healthy",
            1 => "1=timeout/build",
            3 => "3=refused (already unstamped)",
            4 => "4=DCP dependency timeout",
            5 => "5=server build unverified",
            _ => exitCode.ToString(CultureInfo.InvariantCulture),
        };

        /// <summary>Test-AppHostBuildShaFormat: a full 40- or 64-character hex SHA.</summary>
        public static bool IsSha(string value) => !string.IsNullOrWhiteSpace(value) && Sha.IsMatch(value);

        /// <summary>Resolve-AppHostLoadedIdentity: the /api/version answer's version SHA.</summary>
        public static AppHostLoadedIdentity LoadedIdentity(AppHostHttpAnswer probe)
        {
            if (probe == null || probe.StatusCode != 200 || string.IsNullOrWhiteSpace(probe.Body)) return new AppHostLoadedIdentity(false, null, "unavailable");
            JsonNode parsed;
            try { parsed = JsonNode.Parse(probe.Body); }
            catch (JsonException) { return new AppHostLoadedIdentity(false, null, "malformed"); }
            string sha = null;
            if (parsed is JsonObject body && body["version"] is JsonNode version)
                sha = version is JsonValue value && value.TryGetValue<string>(out var text) ? text : version.ToJsonString();
            if (!IsSha(sha)) return new AppHostLoadedIdentity(false, sha, "malformed");
            return new AppHostLoadedIdentity(true, sha, null);
        }

        /// <summary>Get-AppHostSourceHead: the checkout's HEAD SHA in lower case, or null.</summary>
        public static string SourceHead(NightlyHost host, string root)
        {
            try
            {
                var result = host.RunGit(root, "rev-parse", "HEAD");
                if (result.ExitCode != 0) return null;
                var text = AppHostWorktree.Lines(result.Output).FirstOrDefault();
                return IsSha(text) ? text.ToLowerInvariant() : null;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException) { return null; }
        }

        /// <summary>Test-AppHostTrackedEdits: git status --porcelain --untracked-files=no printed a row.</summary>
        public static bool TrackedEdits(NightlyHost host, string root)
        {
            try
            {
                var result = host.RunGit(root, "status", "--porcelain", "--untracked-files=no");
                return result.ExitCode == 0 && AppHostWorktree.Lines(result.Output).Count > 0;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is IOException) { return false; }
        }

        private static string Code(int? code) => code.HasValue ? code.Value.ToString(CultureInfo.InvariantCulture) : "";
    }

    /// <summary>Invoke-AppHostGracefulStop's answer: accepted (with the server pid), unsupported, refused, unreachable or error, and why.</summary>
    public sealed record AppHostGracefulStopResult(string Class, int? Pid, string Detail);

    /// <summary>
    /// CARD-0716: POST /api/operator/shutdown with the operator token (X-Antiphon-Operator-Token), before the force-kill. The
    /// token is read from the file and never printed: a detail that could carry it is redacted.
    /// </summary>
    public static class AppHostGracefulStop
    {
        private static readonly Regex Unreachable = new Regex("timed out|timeout|task was canceled|task was cancelled|operation was canceled|operation was cancelled|connection refused|actively refused|unable to connect|no connection|name or service not known|no such host|network is unreachable|connection reset",
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex TimedOut = new Regex("timed out|timeout|canceled|cancelled", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        private static readonly Regex StatusInMessage = new Regex(@"\b([45]\d{2})\b", RegexOptions.CultureInvariant);

        /// <summary>The server's default base address.</summary>
        public const string DefaultBaseUrl = "http://localhost:17202";

        /// <summary>
        /// One shutdown request. post sends (uri, headers, body, timeout seconds) and answers the status and body; a transport
        /// failure throws. 202 with {"pid":n} is accepted; 404 unsupported; 403 refused; other statuses error.
        /// </summary>
        public static AppHostGracefulStopResult Run(IFileSystem files, string tokenPath, string baseUrl, string reason, int timeoutSec,
            Func<string, IReadOnlyDictionary<string, string>, string, int, NightlyReportHttpResponse> post)
        {
            if (string.IsNullOrWhiteSpace(tokenPath) || !files.File.Exists(tokenPath)) return new AppHostGracefulStopResult("refused", null, "token file missing");
            string token;
            try { token = files.File.ReadAllText(tokenPath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return new AppHostGracefulStopResult("refused", null, "token file unreadable"); }
            token = token?.Trim();
            if (string.IsNullOrEmpty(token)) return new AppHostGracefulStopResult("refused", null, "token file empty");
            var body = new JsonObject { ["reason"] = reason }.ToJsonString();
            var root = string.IsNullOrWhiteSpace(baseUrl) ? DefaultBaseUrl : baseUrl;
            var uri = root.TrimEnd('/') + "/api/operator/shutdown";
            NightlyReportHttpResponse answer;
            try
            {
                answer = post(uri, new Dictionary<string, string> { ["X-Antiphon-Operator-Token"] = token }, body, timeoutSec);
            }
            catch (Exception e) when (e is System.Net.Http.HttpRequestException || e is OperationCanceledException || e is IOException || e is InvalidOperationException
                || e is System.Net.Sockets.SocketException)
            {
                var message = e.Message ?? "";
                var inMessage = StatusInMessage.Match(message);
                if (inMessage.Success) return ByStatus(int.Parse(inMessage.Groups[1].Value, CultureInfo.InvariantCulture));
                if (string.IsNullOrWhiteSpace(message) || Unreachable.IsMatch(message))
                    return new AppHostGracefulStopResult("unreachable", null, TimedOut.IsMatch(message) ? "timeout" : "connection failed");
                return new AppHostGracefulStopResult("error", null, SafeDetail(message, token));
            }
            if (answer.Status == 202)
            {
                JsonNode parsed;
                try { parsed = JsonNode.Parse(answer.Body ?? ""); }
                catch (JsonException) { return new AppHostGracefulStopResult("error", null, "shutdown body was not json"); }
                int? pid = null;
                if (parsed is JsonObject obj && obj["pid"] is JsonValue value)
                {
                    if (value.TryGetValue<int>(out var number)) pid = number;
                    else if (value.TryGetValue<string>(out var text) && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var fromText)) pid = fromText;
                }
                return pid.HasValue ? new AppHostGracefulStopResult("accepted", pid, null) : new AppHostGracefulStopResult("error", null, "shutdown body had no pid");
            }
            return ByStatus(answer.Status);
        }

        private static AppHostGracefulStopResult ByStatus(int status) => status switch
        {
            404 => new AppHostGracefulStopResult("unsupported", null, "http 404"),
            403 => new AppHostGracefulStopResult("refused", null, "http 403"),
            _ => new AppHostGracefulStopResult("error", null, "http " + status.ToString(CultureInfo.InvariantCulture)),
        };

        /// <summary>ConvertTo-AppHostSafeDetail: the token redacted, one line, at most 180 characters.</summary>
        public static string SafeDetail(string text, string token)
        {
            if (string.IsNullOrWhiteSpace(text)) return "request failed";
            var safe = string.IsNullOrEmpty(token) ? text : text.Replace(token, "[redacted]");
            safe = Regex.Replace(safe, "[\r\n]+", " ").Trim();
            return safe.Length > 180 ? safe.Substring(0, 180) : safe;
        }
    }

    /// <summary>Get-AppHostLogVerdict: a failed launch read from logs/apphost.log (BuildFailed, DcpDependencyTimeout, or none).</summary>
    public sealed record AppHostLogVerdict(string Kind, string Evidence, bool PodmanNoise);

    /// <summary>The docker answer the DCP verdict prints (Get-DockerVerdict): healthy, a summary line and up to six detail lines.</summary>
    public sealed record AppHostDockerVerdict(bool Healthy, string Summary, IReadOnlyList<string> Detail);

    /// <summary>The launch log, docker and progress readers of a restart (CARD-0075, CARD-1680).</summary>
    public static class AppHostLaunchLog
    {
        private static readonly Regex BuildFailed = new Regex("(?m)^.*The build failed.*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        private static readonly string[] DcpPatterns = { "dependency check returned an error", "DcpDependencyCheck", "EnsureDcpContainerRuntimeAsync" };

        /// <summary>
        /// The verdict of the AppHost log. DcpDependencyTimeout is the CARD-0075 shape: Aspire splices dcp info's stderr into the
        /// exception text, and a timeout truncates it after the podman probe (which returns at once on every run) and before the
        /// docker line (the one that stalled). Evidence is cut to 400 characters.
        /// </summary>
        public static AppHostLogVerdict Verdict(IFileSystem files, string logPath)
        {
            if (!files.File.Exists(logPath)) return new AppHostLogVerdict(null, null, false);
            string text;
            try { text = ReadShared(files, logPath); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return new AppHostLogVerdict(null, null, false); }
            if (string.IsNullOrWhiteSpace(text)) return new AppHostLogVerdict(null, null, false);
            var build = BuildFailed.Match(text);
            if (build.Success) return new AppHostLogVerdict("BuildFailed", build.Value.Trim(), false);
            foreach (var pattern in DcpPatterns)
            {
                var match = Regex.Match(text, "(?m)^.*" + pattern + ".*$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                var evidence = match.Value.Trim();
                if (evidence.Length > 400) evidence = evidence.Substring(0, 400) + " ...";
                return new AppHostLogVerdict("DcpDependencyTimeout", evidence, text.IndexOf("podman", StringComparison.OrdinalIgnoreCase) >= 0);
            }
            return new AppHostLogVerdict(null, null, false);
        }

        /// <summary>
        /// Get-DockerVerdict: what docker actually said, the fact the Aspire exception omits. A slow or absent answer is the
        /// positive evidence that the DCP probe stalled on docker.
        /// </summary>
        public static AppHostDockerVerdict Docker(IAppHostPlatform platform, NightlyHost host, int timeoutSec)
        {
            var started = host.UtcNow;
            var result = platform.Run("docker", new[] { "ps", "--format", "{{.Names}} ({{.Status}})" }, timeoutSec);
            var elapsed = AppHost.N1((host.UtcNow - started).TotalSeconds) + "s";
            if (result.TimedOut)
                return new AppHostDockerVerdict(false, "docker did not answer within " + timeoutSec.ToString(CultureInfo.InvariantCulture)
                    + "s - the dependency check most likely stalled on DOCKER, not podman", Array.Empty<string>());
            var lines = AppHostWorktree.Lines(result.Output);
            if (result.ExitCode != 0)
                return new AppHostDockerVerdict(false, "docker ps FAILED with exit " + result.ExitCode.ToString(CultureInfo.InvariantCulture) + " after " + elapsed, lines.Take(5).ToArray());
            return new AppHostDockerVerdict(true, "docker answered in " + elapsed + ": " + lines.Count.ToString(CultureInfo.InvariantCulture) + " container(s) running", lines.Take(6).ToArray());
        }

        /// <summary>Show-DcpTimeoutVerdict: the operator message that replaces "podman: executable file not found".</summary>
        public static IReadOnlyList<string> DcpTimeoutLines(string logPath, AppHostLogVerdict verdict, AppHostDockerVerdict docker, string launchLock, string restartLock, string watchdogLog)
        {
            var lines = new List<string> { "", "AppHost launch FAILED: Aspire's DCP dependency check TIMED OUT." };
            if (!string.IsNullOrEmpty(verdict.Evidence)) lines.Add("  from " + logPath + ": " + verdict.Evidence);
            lines.Add("  docker: " + docker.Summary);
            foreach (var detail in docker.Detail) if (!string.IsNullOrEmpty(detail)) lines.Add("    " + detail);
            if (verdict.PodmanNoise)
            {
                lines.Add("  IGNORE the podman text in that message. 'dcp info' probes podman FIRST on every");
                lines.Add("  invocation, healthy ones included (podman +0ms, docker +382ms, result +648ms), and");
                lines.Add("  Aspire splices its stderr into the exception. The timeout truncates that stderr");
                lines.Add("  after the runtime that answered instantly and before the one that stalled.");
                lines.Add("  Podman is not installed here, is not needed, and is not the failure.");
            }
            if (docker.Healthy)
            {
                lines.Add("  Docker is healthy, so the likely cause is a CONCURRENT restart or launch killing");
                lines.Add("  DCP mid-startup. Check, in order:");
            }
            else
            {
                lines.Add("  Docker did not answer cleanly - fix Docker Desktop first, then check:");
            }
            foreach (var file in new[] { restartLock, launchLock, watchdogLog }) if (!string.IsNullOrEmpty(file)) lines.Add("    " + file);
            lines.Add("  Wait for any in-flight launch to finish before re-running this command.");
            lines.Add("");
            return lines;
        }

        /// <summary>
        /// Get-AppHostLaunchSnapshot (CARD-1680): one progress signal per file (its length and write time, or absent) and the
        /// server port's listener (True, False or unknown). A changed value between two polls is progress.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<string, string>> Snapshot(IAppHostPlatform platform, IEnumerable<string> paths, int serverPort = AppHost.ServerPort)
        {
            var files = platform.Files;
            var signals = new List<KeyValuePair<string, string>>();
            foreach (var path in paths)
            {
                var leaf = files.Path.GetFileName(path);
                string value;
                try
                {
                    var info = files.FileInfo.New(path);
                    value = info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "absent";
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { value = "absent"; }
                signals.Add(new KeyValuePair<string, string>(leaf, value));
            }
            string listening;
            try { listening = platform.PortOwners(serverPort).Count > 0 ? "True" : "False"; }
            catch (Exception e) when (e is InvalidOperationException || e is System.ComponentModel.Win32Exception || e is IOException) { listening = "unknown"; }
            signals.Add(new KeyValuePair<string, string>("port " + serverPort.ToString(CultureInfo.InvariantCulture), listening));
            return signals;
        }

        /// <summary>Get-AppHostLaunchProgress: the first signal that changed between two snapshots, or null.</summary>
        public static string Progress(IReadOnlyList<KeyValuePair<string, string>> before, IReadOnlyList<KeyValuePair<string, string>> after)
        {
            foreach (var signal in after)
            {
                string old = null;
                if (before != null)
                    foreach (var earlier in before)
                        if (earlier.Key == signal.Key) { old = earlier.Value; break; }
                if (string.Equals(old, signal.Value, StringComparison.OrdinalIgnoreCase)) continue;
                if (signal.Key.StartsWith("port ", StringComparison.OrdinalIgnoreCase)) return "server " + signal.Key + " listening=" + signal.Value;
                if (old == null || old == "absent") return signal.Key + " appeared";
                return signal.Key + " grew";
            }
            return null;
        }

        /// <summary>
        /// Move-AppHostLogHistory (CARD-1680): apphost.log to apphost.log.1 .. .&lt;keep&gt; instead of emptying it, so earlier
        /// launches' LIVE_TIMING lines survive; an empty apphost.log is left. A rotation that cannot move a file empties it.
        /// </summary>
        public static void Rotate(IFileSystem files, string logPath, int keep = 10)
        {
            try
            {
                for (var i = keep - 1; i >= 1; i--)
                {
                    var older = logPath + "." + i.ToString(CultureInfo.InvariantCulture);
                    if (files.File.Exists(older)) files.File.Move(older, logPath + "." + (i + 1).ToString(CultureInfo.InvariantCulture), overwrite: true);
                }
                if (files.File.Exists(logPath)) files.File.Move(logPath, logPath + ".1", overwrite: true);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            try { files.File.WriteAllText(logPath, Environment.NewLine); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>A file another process may be writing: read with read-write sharing.</summary>
        public static string ReadShared(IFileSystem files, string path)
        {
            using var stream = files.FileStream.New(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return reader.ReadToEnd();
        }
    }

    /// <summary>
    /// scripts/lib/live-startup-timing.ps1's line for the phases the C# commands time (CARD-1564): "LIVE_TIMING " and one
    /// compact JSON row with fixed labels only (no payload, no exception text).
    /// </summary>
    public sealed class LiveStartupPhase
    {
        private static readonly string RunId = Guid.NewGuid().ToString("N");

        private readonly NightlyHost _host;
        private readonly string _operation;
        private readonly DateTime _started;
        private bool _closed;

        /// <summary>Starts a phase: docker-info, docker-network, container-start, restore, dependencies, build or health-wait.</summary>
        public LiveStartupPhase(NightlyHost host, string operation)
        {
            _host = host;
            _operation = operation;
            _started = host.UtcNow;
        }

        /// <summary>The phase vocabulary of an operation.</summary>
        public static string PhaseOf(string operation) => operation switch
        {
            "health-wait" => "network-loopback",
            "build" or "restore" or "dependencies" => "process-wait",
            _ => "fixture-setup",
        };

        /// <summary>Writes the phase's line once (to the host's output); later calls do nothing.</summary>
        public void Stop(bool succeeded)
        {
            if (_closed) return;
            _closed = true;
            var row = new JsonObject
            {
                ["version"] = 1, ["runId"] = RunId, ["producerPid"] = _host.Processes.CurrentPid, ["phase"] = PhaseOf(_operation), ["operation"] = _operation,
                ["elapsedMs"] = (_host.UtcNow - _started).TotalMilliseconds, ["outcome"] = succeeded ? "completed" : "failed-or-canceled",
            };
            _host.Out.WriteLine("LIVE_TIMING " + row.ToJsonString());
        }
    }
}
