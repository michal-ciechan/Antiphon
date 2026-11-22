// CARD-1784 (moved from scripts/post-land-early-warning.ps1 and the lock and lane of its library): the entry of
// antiphon-scripts post-land-early-warning. --plan prints the decision and does not detach, lock or start dotnet. Run
// mode refuses a non-Linux host before a build slot, checks HEAD and a clean tree, then detaches: the parent starts
// the lane (settings.LaunchChild; the CLI relaunches itself with --child under setsid) and returns once the lane's
// marker exists. The lane takes <receipt-root>/lock.json, builds once through the build slot and runs each shard
// through its own slot with --no-build, then writes the receipt. The lock owner is the process id and the run id, so
// two lanes in one process do not share it. Plain C#: no PowerShell types. ASCII only.
#nullable disable
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    public static partial class PostLandEarlyWarning
    {
        public const string NotLinuxRefusal = "refused=not-linux: run this lane on the Linux host. The desktop does not run it.";
        public const string SlotLabel = "post-land-early-warning";

        private static readonly Regex RunIdPattern = new Regex("^[0-9a-fA-F]{32}$", RegexOptions.CultureInvariant);

        private static string Now() => DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);

        // ---- process identity ------------------------------------------------------------------------------------------

        public static bool IsPidAlive(int processId)
        {
            if (processId <= 0) return false;
            var procfs = "/proc/" + processId.ToString(CultureInfo.InvariantCulture);
            if (Directory.Exists(procfs) || File.Exists(procfs)) return true;
            try
            {
                using var process = Process.GetProcessById(processId);
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception)
            {
                return false;
            }
        }

        private static DateTime? ProcessStartUtc(int processId)
        {
            try
            {
                using var process = Process.GetProcessById(processId);
                return process.StartTime.ToUniversalTime();
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException || e is Win32Exception || e is NotSupportedException)
            {
                return null;
            }
        }

        /// <summary>The raw startedAt text ("startedAt":"...") of a lock or last-start file, without a JSON reader's date parsing.</summary>
        public static string StartedAtToken(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            const string marker = "\"startedAt\":\"";
            var at = raw.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) return null;
            var start = at + marker.Length;
            var end = raw.IndexOf('"', start);
            return end < 0 ? null : raw.Substring(start, end - start);
        }

        /// <summary>An invariant round-trip timestamp as UTC; a time without a zone is taken as UTC.</summary>
        public static DateTime? ToUtc(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)) return null;
            return parsed.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc) : parsed.ToUniversalTime();
        }

        /// <summary>A live pid whose process started more than 2 s after the lock was taken is a reused pid.</summary>
        private static bool IsPidReused(int processId, string startedAtText)
        {
            var lockStart = ToUtc(startedAtText);
            if (lockStart == null) return false;
            var processStart = ProcessStartUtc(processId);
            if (processStart == null) return false;
            return processStart.Value > lockStart.Value.AddSeconds(2);
        }

        // ---- the lock --------------------------------------------------------------------------------------------------

        private static bool TryPid(JsonElement value, out int pid)
        {
            pid = 0;
            switch (value.ValueKind)
            {
                case JsonValueKind.Null: return true;
                case JsonValueKind.Number:
                    if (value.TryGetInt32(out pid)) return true;
                    if (!value.TryGetDouble(out var number) || number < int.MinValue || number > int.MaxValue) return false;
                    pid = (int)Math.Round(number, MidpointRounding.ToEven);
                    return true;
                case JsonValueKind.String:
                    return int.TryParse(value.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out pid);
                default: return false;
            }
        }

        /// <summary>
        /// absent; ours (this process and ownRunId); busy (a live owner, or a dead one with no startedAt); or stale
        /// (unreadable, no pid, a reused pid, or a dead owner with a startedAt).
        /// </summary>
        public static string LockKind(string path, string ownRunId)
        {
            if (!File.Exists(path)) return "absent";
            string raw;
            try { raw = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return "stale"; }
            JsonElement document;
            try
            {
                using var parsed = JsonDocument.Parse(raw, Lenient);
                document = parsed.RootElement.Clone();
            }
            catch (JsonException) { return "stale"; }
            if (document.ValueKind != JsonValueKind.Object || !TryProperty(document, "pid", out var pidValue)) return "stale";
            if (!TryPid(pidValue, out var processId)) return "stale";
            if (processId == Environment.ProcessId && !string.IsNullOrEmpty(ownRunId)
                && TryProperty(document, "runId", out var runId) && string.Equals(Text(runId), ownRunId, StringComparison.Ordinal))
                return "ours";
            if (IsPidAlive(processId)) return IsPidReused(processId, StartedAtToken(raw)) ? "stale" : "busy";
            if (!TryProperty(document, "startedAt", out var startedAt) || string.IsNullOrWhiteSpace(Text(startedAt))) return "busy";
            return "stale";
        }

        // File.Move without overwrite performs a destination check followed by rename on Linux. Serialize
        // changes to the owner record across processes so that two contenders cannot both publish it.
        private static Mutex WaitForLockUpdate(string path)
        {
            var fullPath = Path.GetFullPath(path);
            if (OperatingSystem.IsWindows()) fullPath = fullPath.ToUpperInvariant();
            var name = "Global\\antiphon-post-land-lock-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
            var mutex = new Mutex(false, name);
            try { mutex.WaitOne(); }
            catch (AbandonedMutexException) { /* The caller owns the mutex. Inspect the owner record as usual. */ }
            catch
            {
                mutex.Dispose();
                throw;
            }
            return mutex;
        }

        /// <summary>Takes the lock by publishing a complete owner record. A stale lock can be reclaimed.</summary>
        public static bool EnterLock(string path, string runId, int reclaimDelayMs = 0)
        {
            using var update = WaitForLockUpdate(path);
            try { return EnterLockCore(path, runId, reclaimDelayMs); }
            finally { update.ReleaseMutex(); }
        }

        private static bool EnterLockCore(string path, string runId, int reclaimDelayMs)
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            var pid = Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            for (var attempt = 0; attempt < 3; attempt++)
            {
                var suffix = "." + pid + "." + runId + "." + attempt.ToString(CultureInfo.InvariantCulture);
                var body = "{\"pid\":" + pid + ",\"startedAt\":\"" + Now() + "\",\"runId\":\"" + runId + "\"}\n";
                var temp = path + suffix + ".tmp";
                File.WriteAllText(temp, body);
                try
                {
                    File.Move(temp, path);
                    return true;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    TryDelete(temp);
                }
                var kind = File.Exists(path) ? LockKind(path, runId) : "absent";
                if (kind == "ours") return true;
                if (kind != "stale") return false;
                if (reclaimDelayMs > 0) Thread.Sleep(reclaimDelayMs);
                var claim = path + suffix + ".reclaim";
                try { File.Move(path, claim); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { continue; }
                if (LockKind(claim, runId) != "stale")
                {
                    try { File.Move(claim, path); } catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
                    return false;
                }
                TryDelete(claim);
            }
            return false;
        }

        /// <summary>Removes the lock only while it is this lane's.</summary>
        public static void ExitLock(string path, string runId)
        {
            using var update = WaitForLockUpdate(path);
            try
            {
                if (!File.Exists(path) || LockKind(path, runId) != "ours") return;
                TryDelete(path);
            }
            finally { update.ReleaseMutex(); }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
        }

        /// <summary>lane-&lt;run-id&gt;.json: {"runId":...,"state":...}, written whole (temp file and rename).</summary>
        public static void WriteRunMarker(string path, string runId, string state)
        {
            var parent = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temp, "{\"runId\":\"" + runId + "\",\"state\":\"" + state + "\"}\n");
            File.Move(temp, path, true);
        }

        /// <summary>last-start.json's startedAt is at most `minutes` old (and not in the future).</summary>
        public static bool IsHourly(string path, int minutes)
        {
            if (!File.Exists(path)) return false;
            string raw;
            try { raw = File.ReadAllText(path); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return false; }
            var started = ToUtc(StartedAtToken(raw));
            if (started == null) return false;
            var age = (DateTime.UtcNow - started.Value).TotalMinutes;
            return age >= 0 && age < minutes;
        }

        public static bool IsCompleted(string path)
        {
            if (!File.Exists(path)) return false;
            var read = ReadJson(path);
            return read != null && IsState(read.Value, "completed");
        }

        // ---- the build slot ------------------------------------------------------------------------------------------

        /// <summary>
        /// Runs one command through the build slot: settings.SlotCommand (default pwsh -File scripts/build-slot.ps1)
        /// followed by -Label post-land-early-warning -- command, in the repository, its stdout and stderr appended to
        /// logPath. Returns the exit code (1 when it cannot start). With settings.SlotShim set, nothing is started: the
        /// command line is appended to the shim file and the shim's exit codes and TRX copy stand in.
        /// </summary>
        public static int RunSlot(IReadOnlyList<string> command, string logPath, string repo, PostLandSettings settings, IDictionary<string, string> environment,
            TextWriter stdout, TextWriter stderr)
        {
            if (!string.IsNullOrWhiteSpace(settings.SlotShim)) return RunShim(command, settings);
            var launcher = settings.SlotCommand ?? PostLandSettings.SlotCommandFor(Path.Combine(settings.ProductRoot, "scripts", "build-slot.ps1"));
            var start = new ProcessStartInfo(launcher[0])
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = new UTF8Encoding(false),
                StandardErrorEncoding = new UTF8Encoding(false),
                WorkingDirectory = repo,
            };
            foreach (var argument in launcher.Skip(1).Concat(new[] { "-Label", SlotLabel, "--" }).Concat(command)) start.ArgumentList.Add(argument);
            if (environment != null)
                foreach (var pair in environment) start.Environment[pair.Key] = pair.Value;
            StreamWriter log = null;
            if (!string.IsNullOrWhiteSpace(logPath))
            {
                var parent = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
                log = new StreamWriter(new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false));
            }
            var gate = new object();
            try
            {
                using var process = new Process { StartInfo = start };
                process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (gate) (log ?? stdout).WriteLine(e.Data); };
                process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (gate) (log ?? stderr).WriteLine(e.Data); };
                try { process.Start(); }
                catch (Win32Exception e)
                {
                    stderr.WriteLine(launcher[0] + ": " + e.Message);
                    return 1;
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                return process.ExitCode;
            }
            finally
            {
                log?.Dispose();
            }
        }

        private static int RunShim(IReadOnlyList<string> command, PostLandSettings settings)
        {
            var shim = settings.SlotShim;
            var parent = Path.GetDirectoryName(shim);
            if (!string.IsNullOrWhiteSpace(parent)) Directory.CreateDirectory(parent);
            File.AppendAllText(shim, string.Join(" ", command) + "\n");
            if (settings.SlotDelayMs > 0) Thread.Sleep(settings.SlotDelayMs);
            var shard = false;
            var fileName = "";
            var results = "";
            for (var i = 0; i < command.Count; i++)
            {
                if (Is(command[i], "--no-build")) shard = true;
                if (Is(command[i], "--report-trx-filename") && i + 1 < command.Count) fileName = command[i + 1];
                if (Is(command[i], "--results-directory") && i + 1 < command.Count) results = command[i + 1];
            }
            if (shard && !string.IsNullOrWhiteSpace(settings.SlotTrx) && !string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(results) && File.Exists(settings.SlotTrx))
            {
                Directory.CreateDirectory(results);
                var copied = Path.Combine(results, fileName);
                File.Copy(settings.SlotTrx, copied, true);
                File.SetLastWriteTimeUtc(copied, DateTime.UtcNow);
            }
            var exitText = shard ? settings.SlotShardExit : settings.SlotBuildExit;
            if (string.IsNullOrWhiteSpace(exitText)) return 0;
            var exit = IntOrZero(exitText);
            return exit == 0 && !double.TryParse(exitText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out _) ? 1 : exit;
        }

        // ---- the lane -----------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds once, then runs each planned shard (one per filter) through its own slot with --no-build and a TRX in
        /// &lt;receipt-root&gt;/&lt;tip&gt;. A TRX older than the shard's start is removed; a shard over the budget is slow (R1).
        /// Writes the receipt and prints the summary line. Exit 1 for a failed build, new reds or an unknown shard.
        /// </summary>
        public static int Lane(string repo, string receiptRoot, PostLandPayload payload, PostLandManifest manifest, PostLandSettings settings,
            TextWriter stdout, TextWriter stderr)
        {
            var now = Now();
            payload.StartedAt = now;
            Directory.CreateDirectory(receiptRoot);
            var tipDir = Path.Combine(receiptRoot, payload.TipSha);
            Directory.CreateDirectory(tipDir);
            File.WriteAllText(Path.Combine(receiptRoot, "last-start.json"), "{\"startedAt\":\"" + now + "\"}\n");
            var budget = manifest?.ShardBudgetMinutes ?? 30.0;
            if (settings.ShardBudgetMinutes != null) budget = settings.ShardBudgetMinutes.Value;
            var slowIds = new List<string>();
            var slotLog = Path.Combine(tipDir, "slot.log");
            var build = new[] { "dotnet", "build", "tests/Antiphon.Tests", "--property:UseAppHost=false", "--nologo" };
            var buildExit = RunSlot(build, slotLog, repo, settings, null, stdout, stderr);
            payload.BuildExit = buildExit;
            if (buildExit == 0)
            {
                var launched = new HashSet<string>(StringComparer.Ordinal);
                foreach (var shard in payload.Shards)
                {
                    if (shard == null || !launched.Add(shard.Filter ?? "")) continue;
                    var phase = Path.Combine(tipDir, "phase", shard.Id);
                    Directory.CreateDirectory(phase);
                    var trxPath = Path.Combine(tipDir, shard.Id + ".trx");
                    if (File.Exists(trxPath)) File.Delete(trxPath);
                    var run = new[]
                    {
                        "dotnet", "run", "--project", "tests/Antiphon.Tests", "--no-build", "--property:UseAppHost=false",
                        "--", "--treenode-filter", shard.Filter, "--report-trx", "--report-trx-filename", shard.Id + ".trx",
                        "--results-directory", tipDir,
                    };
                    var started = DateTime.UtcNow;
                    var watch = Stopwatch.StartNew();
                    var shardExit = 1;
                    try
                    {
                        shardExit = RunSlot(run, slotLog, repo, settings, new Dictionary<string, string> { ["ANTIPHON_TEST_PHASE_ROOT"] = phase }, stdout, stderr);
                    }
                    catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
                    {
                        stderr.WriteLine(e.Message);
                    }
                    watch.Stop();
                    shard.Exit = shardExit;
                    var fresh = File.Exists(trxPath) && File.GetLastWriteTimeUtc(trxPath) >= started.AddSeconds(-2);
                    if (!fresh && File.Exists(trxPath)) File.Delete(trxPath);
                    shard.Slow = false;
                    if (watch.Elapsed.TotalMinutes > budget)
                    {
                        shard.Slow = true;
                        slowIds.Add(shard.Id);
                        stderr.WriteLine("R1 debug shard=" + shard.Id);
                    }
                }
            }
            payload.SlowShards = slowIds;
            payload.Slot = true;
            payload.FinishedAt = Now();
            if (buildExit != 0)
            {
                payload.SummaryLine = Summary(payload.TipSha, payload.BaseSha, null, null, payload.OwedWindows.Count, buildExit);
                payload.State = "incomplete";
                WriteReceipt(receiptRoot, payload);
                stdout.WriteLine(payload.SummaryLine);
                return 1;
            }
            CompleteOutcome(payload, repo, receiptRoot);
            var unknown = payload.SummaryLine.Contains("new-reds=unknown", StringComparison.Ordinal);
            payload.State = unknown ? "incomplete" : "completed";
            WriteReceipt(receiptRoot, payload);
            stdout.WriteLine(payload.SummaryLine);
            return payload.NewReds.Count > 0 || unknown ? 1 : 0;
        }

        // ---- the entry ------------------------------------------------------------------------------------------------

        /// <summary>Run mode's host check: the platform seam when set ("linux" admits), else the host itself.</summary>
        public static bool RunModeAllowed(string platform)
        {
            if (!string.IsNullOrEmpty(platform)) return string.Equals(platform, "linux", StringComparison.OrdinalIgnoreCase);
            return OperatingSystem.IsLinux();
        }

        /// <summary>The detached lane's stdout and stderr logs (&lt;receipt-root&gt;/&lt;tip&gt;/lane-&lt;run-id&gt;.log and .err), or null.</summary>
        public static (string Stdout, string Stderr)? LaneLogs(PostLandOptions options)
        {
            if (string.IsNullOrWhiteSpace(options.ReceiptRoot) || string.IsNullOrWhiteSpace(options.RunId)) return null;
            if (!TryResolveRange(options.Range, out _, out var tip) || tip.IndexOfAny(new[] { '/', '\\' }) >= 0) return null;
            var tipDir = Path.Combine(Path.GetFullPath(options.ReceiptRoot), tip);
            return (Path.Combine(tipDir, "lane-" + options.RunId + ".log"), Path.Combine(tipDir, "lane-" + options.RunId + ".err"));
        }

        /// <summary>
        /// The post-land-early-warning command. Exit 0 done (or skipped), 1 new reds or unknown shards (plan) / a lane that
        /// is not green, 2 refused (bad range, head mismatch, dirty tree, bad run id, refused plan, detach), 3 not Linux.
        /// </summary>
        public static int Run(PostLandOptions options, PostLandSettings settings, TextWriter stdout, TextWriter stderr)
        {
            if (!TryResolveRange(options.Range, out var baseRef, out var tipRef))
            {
                stderr.WriteLine("refused=bad-range");
                return 2;
            }
            if (!options.Plan && !RunModeAllowed(settings.Platform))
            {
                stderr.WriteLine(NotLinuxRefusal);
                return 3;
            }
            var repo = Path.GetFullPath(string.IsNullOrWhiteSpace(options.Repo) ? settings.ProductRoot : options.Repo);
            var receiptRoot = Path.GetFullPath(string.IsNullOrWhiteSpace(options.ReceiptRoot) ? Path.Combine(repo, ".antiphon", "post-land-warning") : options.ReceiptRoot);
            var baseSha = ResolveSha(repo, baseRef);
            var tipSha = ResolveSha(repo, tipRef);
            if (string.IsNullOrWhiteSpace(baseSha) || string.IsNullOrWhiteSpace(tipSha))
            {
                stderr.WriteLine("refused=bad-range");
                return 2;
            }
            if (!options.Plan)
            {
                if (!string.Equals(ResolveSha(repo, "HEAD"), tipSha, StringComparison.OrdinalIgnoreCase))
                {
                    stderr.WriteLine("refused=head-mismatch");
                    return 2;
                }
                var dirty = Git(repo, "status", "--porcelain", "--untracked-files=no");
                if (dirty.Exit != 0 || !string.IsNullOrWhiteSpace(dirty.Stdout))
                {
                    stderr.WriteLine("refused=dirty");
                    return 2;
                }
            }
            if (options.Plan)
            {
                var plan = Decide(repo, receiptRoot, baseSha, tipSha, options.FileList, options.Table, options.Force, settings, null, stderr);
                stdout.WriteLine(plan.Payload.ToJson());
                return plan.Exit;
            }
            var runId = string.IsNullOrWhiteSpace(options.RunId) ? Guid.NewGuid().ToString("N") : options.RunId;
            if (!RunIdPattern.IsMatch(runId))
            {
                stderr.WriteLine("refused=bad-run-id");
                return 2;
            }
            var marker = Path.Combine(receiptRoot, "lane-" + runId + ".json");
            var lockPath = Path.Combine(receiptRoot, "lock.json");
            if (options.Child) return RunLane(repo, receiptRoot, baseSha, tipSha, runId, marker, lockPath, options, settings, stdout, stderr);

            var child = new PostLandOptions
            {
                Range = baseSha + ".." + tipSha,
                Repo = repo,
                ReceiptRoot = receiptRoot,
                RunId = runId,
                FileList = string.IsNullOrWhiteSpace(options.FileList) ? null : options.FileList,
                Table = string.IsNullOrWhiteSpace(options.Table) ? null : options.Table,
                Force = options.Force,
                Child = true,
            };
            var logs = LaneLogs(child).Value;
            Directory.CreateDirectory(Path.GetDirectoryName(logs.Stdout));
            try
            {
                if (settings.LaunchChild == null) throw new InvalidOperationException("no lane launcher");
                settings.LaunchChild(new PostLandChildLaunch
                {
                    Options = child,
                    Arguments = child.ToArguments(),
                    WorkingDirectory = repo,
                    StdoutLog = logs.Stdout,
                    StderrLog = logs.Stderr,
                });
            }
            catch (Exception e) when (e is InvalidOperationException || e is Win32Exception || e is IOException || e is UnauthorizedAccessException)
            {
                stderr.WriteLine(e.Message);
                stderr.WriteLine("refused=detach");
                return 2;
            }
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (File.Exists(marker))
                {
                    stdout.WriteLine("runId=" + runId);
                    stdout.WriteLine(File.ReadAllText(marker));
                    return 0;
                }
                Thread.Sleep(200);
            }
            stderr.WriteLine("refused=detach");
            return 2;
        }

        // The detached lane: the lock, the marker, the decision under the lock and the lane itself.
        private static int RunLane(string repo, string receiptRoot, string baseSha, string tipSha, string runId, string marker, string lockPath,
            PostLandOptions options, PostLandSettings settings, TextWriter stdout, TextWriter stderr)
        {
            if (!EnterLock(lockPath, runId, settings.ReclaimDelayMs))
            {
                var busy = Decide(repo, receiptRoot, baseSha, tipSha, options.FileList, options.Table, options.Force, settings, runId, stderr);
                busy.Payload.FinishedAt = Now();
                WriteReceipt(receiptRoot, busy.Payload);
                WriteRunMarker(marker, runId, "skipped-busy");
                stdout.WriteLine(busy.Payload.SummaryLine);
                return 0;
            }
            try
            {
                WriteRunMarker(marker, runId, "started");
                var decision = Decide(repo, receiptRoot, baseSha, tipSha, options.FileList, options.Table, options.Force, settings, runId, stderr);
                if (!string.Equals(decision.Payload.State, "run", StringComparison.OrdinalIgnoreCase))
                {
                    decision.Payload.FinishedAt = Now();
                    WriteReceipt(receiptRoot, decision.Payload);
                    stdout.WriteLine(decision.Payload.SummaryLine);
                    return 0;
                }
                return Lane(repo, receiptRoot, decision.Payload, ReadManifest(ManifestPath(settings)), settings, stdout, stderr);
            }
            finally
            {
                ExitLock(lockPath, runId);
            }
        }
    }
}
