// CARD-1545 S1b admission, ported from scripts/lib/live-lane-run.ps1 and the native mode of scripts/run-live-lane.ps1
// (W2 slice S21; both scripts are deleted). Every result is diagnostic; native and paid execution stay disabled: a
// request that is not the inert fixture roster is native-unqualified (exit 20) before any I/O, so the CLI's
// live-lane-run never takes a lock, starts a process or reaches a provider. The fixture path is driven only by in-process
// tests through LiveLaneBoundary (the old Fixture parameter set) and an ILiveLaneNativeLock bound to the shared native-run
// lock. Plain C#, framework only. ASCII only.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Antiphon.LiveLane
{
    /// <summary>One admission request (the -Request hashtable).</summary>
    public sealed class LiveLaneRequest
    {
        public string Mode { get; set; }
        public string Lane { get; set; }
        public string RunId { get; set; }
        public int Attempt { get; set; }
        public string Source { get; set; }
        public IReadOnlyList<string> Uids { get; set; } = Array.Empty<string>();
        public string Executable { get; set; }
        public string Profile { get; set; }
        public string Commission { get; set; }
        public double BudgetSeconds { get; set; }
        public string CoordinationRoot { get; set; }
        public string ResultsRoot { get; set; }
        /// <summary>The environment the child could inherit; only the allow-list below reaches it.</summary>
        public IReadOnlyDictionary<string, string> Inherited { get; set; } = new Dictionary<string, string>();
        public string Account { get; set; }
        public string Endpoint { get; set; }
        public string Dependency { get; set; }

        /// <summary>The run's own identity for its preflight probe (no uid).</summary>
        public LiveLaneProbeIdentity Identity() => new LiveLaneProbeIdentity(null, Account, Endpoint, Dependency);
    }

    /// <summary>The candidate checkout the boundary reports: its sha and whether it is clean (null: not reported).</summary>
    public sealed class LiveLaneCandidate
    {
        public string Sha { get; set; }
        public bool? Clean { get; set; }
    }

    /// <summary>An owned adapter handle: the checkpoint run id and the owned children it started.</summary>
    public sealed class LiveLaneChild
    {
        public string RunId { get; set; }
        public IReadOnlyList<string> Children { get; set; } = Array.Empty<string>();
    }

    /// <summary>The child start (New-LiveLaneChildStartInfo): never an executable; its environment is an allow-list.</summary>
    public sealed class LiveLaneChildStart
    {
        public string FileName { get; set; }
        public IReadOnlyList<string> Arguments { get; set; } = Array.Empty<string>();
        public string WorkingDirectory { get; set; }
        public IReadOnlyDictionary<string, string> Environment { get; set; }
    }

    /// <summary>
    /// The fixture I/O adapters (the -Boundary hashtable of scriptblocks). Each required one must be set or the run
    /// refuses fixture-boundary-required. Clock is the lane clock in seconds. Probe, Trace and BeforeRelease are optional.
    /// </summary>
    public sealed class LiveLaneBoundary
    {
        public Func<LiveLaneRequest, LiveLaneCandidate> Candidate { get; set; }
        public Func<double> Clock { get; set; }
        public Func<LiveLaneRequest, bool> Slot { get; set; }
        /// <summary>(issued path, context json) -> the path written.</summary>
        public Func<string, string, string> WriteContext { get; set; }
        /// <summary>(context read back, child start, manifest path) -> the owned handle.</summary>
        public Func<JsonNode, LiveLaneChildStart, string, LiveLaneChild> Start { get; set; }
        /// <summary>checkpoint run id -> the child's exit (75: still running).</summary>
        public Func<string, int> Wait { get; set; }
        public Func<string, JsonNode> ReadTerminal { get; set; }
        public Action<string> Stop { get; set; }
        /// <summary>(child, seconds) -> joined.</summary>
        public Func<string, int, bool> Join { get; set; }
        public Action<string, LiveLaneRunResult> WriteReceipt { get; set; }
        public Func<string, LiveLaneProbeRequest, LiveLaneProbeReply> Probe { get; set; }
        public Action<string> Trace { get; set; }
        public Action<LiveLaneRequest> BeforeRelease { get; set; }
        /// <summary>Optional: the process seam the run asks `&lt;executable&gt; --version` through (the Claude version it records).</summary>
        public ILiveLaneProcess Process { get; set; }
    }

    /// <summary>The owner fields of the shared native-run lock record.</summary>
    public sealed class LiveLaneLockOwner
    {
        public LiveLaneLockOwner(int pid, string processStartedAt, string runId, string lane)
        {
            Pid = pid;
            ProcessStartedAt = processStartedAt ?? "";
            RunId = runId ?? "";
            Lane = lane ?? "";
        }

        public int Pid { get; }
        public string ProcessStartedAt { get; }
        public string RunId { get; }
        public string Lane { get; }
    }

    /// <summary>An Enter answer: taken, or the refusal (deferred-busy for a live holder, stale-shared-lock otherwise).</summary>
    public sealed class LiveLaneLockEntry
    {
        public LiveLaneLockEntry(bool ok, string reason, LiveLaneLockOwner owner)
        {
            Ok = ok;
            Reason = reason ?? "";
            Owner = owner;
        }

        public bool Ok { get; }
        public string Reason { get; }
        public LiveLaneLockOwner Owner { get; }
    }

    /// <summary>
    /// The shared native-run lock (CARD-0599 D-4, &lt;coordination root&gt;/native-run.lock) the live lane takes before any slot
    /// or checkpoint admission. Its implementation is the nightly core's ReleaseGateNativeLock, which serialises the master
    /// and RC lanes too; the caller binds it, so this project does not reference the nightly core.
    /// </summary>
    public interface ILiveLaneNativeLock
    {
        LiveLaneLockEntry Enter(string coordinationRoot, string runId, string lane);
        /// <summary>The record in the lock file now; null when there is none or it cannot be read.</summary>
        LiveLaneLockOwner Owner(string coordinationRoot);
        bool IsAlive(int pid);
        void Exit(string coordinationRoot, string runId);
    }

    /// <summary>Invoke-LiveLaneRun's result: always diagnostic, never gate credit.</summary>
    public sealed class LiveLaneRunResult
    {
        public int ExitCode { get; set; } = 20;
        public string Reason { get; set; } = "admission-incomplete";
        public bool DiagnosticOnly => true;
        public bool GateCredit => false;
        public bool TerminalComplete { get; set; }
        public int? RawChildExit { get; set; }
        public string CheckpointRunId { get; set; }
        public string Classification { get; set; } = "INCONCLUSIVE";
        public IReadOnlyList<LiveLaneOutcomeRow> Rows { get; set; } = Array.Empty<LiveLaneOutcomeRow>();
        public string RawChildOutcome { get; set; }
        public List<int> ChildExits { get; } = new List<int>();
        public List<LiveLaneProbeObservation> Probes { get; set; } = new List<LiveLaneProbeObservation>();
        /// <summary>The Claude CLI version of the executable the run targeted (null when the run never asked it).</summary>
        public LiveLaneClaudeVersion Claude { get; set; }

        /// <summary>
        /// The compact JSON record, in the field order of the old pscustomobject: what run-live-lane.ps1 printed with
        /// ConvertTo-Json -Compress, and what live-lane-run prints.
        /// </summary>
        public string ToJson()
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteNumber("ExitCode", ExitCode);
                json.WriteString("Reason", Reason);
                json.WriteBoolean("DiagnosticOnly", DiagnosticOnly);
                json.WriteBoolean("GateCredit", GateCredit);
                json.WriteBoolean("TerminalComplete", TerminalComplete);
                if (RawChildExit.HasValue) json.WriteNumber("RawChildExit", RawChildExit.Value); else json.WriteNull("RawChildExit");
                OptionalString(json, "CheckpointRunId", CheckpointRunId);
                json.WriteString("Classification", Classification);
                json.WriteStartArray("Rows");
                foreach (var row in Rows)
                {
                    json.WriteStartObject();
                    OptionalString(json, "Uid", row.Uid);
                    json.WriteString("State", row.State);
                    json.WriteString("Setup", row.Setup);
                    json.WriteString("Proof", row.Proof);
                    json.WriteNumber("ExitCode", row.ExitCode);
                    json.WriteString("Reason", row.Reason);
                    OptionalString(json, "ProviderError", row.ProviderError);
                    json.WriteString("Phase", row.Phase);
                    json.WriteBoolean("OutageCorrelated", row.OutageCorrelated);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                OptionalString(json, "RawChildOutcome", RawChildOutcome);
                json.WriteStartArray("ChildExits");
                foreach (var code in ChildExits) json.WriteNumberValue(code);
                json.WriteEndArray();
                json.WriteStartArray("Probes");
                foreach (var probe in Probes)
                {
                    json.WriteStartObject();
                    OptionalString(json, "Status", probe.Status);
                    OptionalString(json, "ProviderError", probe.ProviderError);
                    OptionalString(json, "Phase", probe.Phase);
                    json.WriteNumber("Started", probe.Started);
                    json.WriteNumber("Finished", probe.Finished);
                    json.WriteBoolean("Bounded", probe.Bounded);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                // S22: the Claude version the run recorded (null: it never asked; the native mode never does).
                if (Claude == null) json.WriteNull("Claude");
                else
                {
                    json.WriteStartObject("Claude");
                    json.WriteString("Version", Claude.Version);
                    OptionalString(json, "Raw", Claude.Raw);
                    json.WriteEndObject();
                }
                json.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        private static void OptionalString(Utf8JsonWriter json, string name, string value)
        {
            if (value == null) json.WriteNull(name); else json.WriteString(name, value);
        }
    }

    /// <summary>The admission run.</summary>
    public static class LiveLaneRun
    {
        /// <summary>The only lane, executable, profile and uids this slice admits: an inert roster and opaque handles.</summary>
        public const string FixtureLane = "fixture-live";
        public const string FixtureExecutable = "owned-fixture-executable";
        public const string FixtureProfile = "owned-fixture-profile";
        public static readonly IReadOnlyList<string> FixtureUids = new[] { "fixture.A.0", "fixture.B.0" };

        /// <summary>The child's FileName: there is no production native executor.</summary>
        public const string ChildFileName = "fixture-only-no-executable";

        /// <summary>The inherited keys a child may keep, and the selected keys it is given (presence and emptiness kept).</summary>
        public static readonly IReadOnlyList<string> InheritedKeys = new[] { "PATH", "SystemRoot", "WINDIR", "TEMP", "TMP", "TMPDIR", "DOTNET_ROOT" };
        public static readonly IReadOnlyList<string> SelectedKeys = new[]
        {
            "CLAUDE_CONFIG_DIR", "ANTIPHON_LIVE_LANE_CONTEXT", "ANTIPHON_LIVE_CLAUDE_EXE",
            "CLAUDE_CODE_CHILD_SESSION", "CLAUDE_CODE_SESSION_ID", "CLAUDE_CODE_BRIDGE_SESSION_ID", "CLAUDECODE"
        };

        private static readonly Regex Sha = new Regex("^[0-9a-f]{40}$", RegexOptions.CultureInvariant);
        private static readonly Regex CheckpointRunId = new Regex(@"\A[A-Za-z0-9][A-Za-z0-9_-]{0,127}\z", RegexOptions.CultureInvariant);
        private static readonly string[] ContextFields = { "schemaVersion", "lane", "runId", "attempt", "source", "executable", "profile", "issuance" };

        /// <summary>
        /// The build-driver policy of a Windows child (CARD-1506), copied from Antiphon.Build.BuildDriverEnvironment
        /// (scripts/lib/BuildDriverEnvironment.cs) so this project links no shared source; LiveLaneRunnerTests pins the
        /// copy to the original. The opt-out key is never in the allow-list, so a Windows child always gets all six. On
        /// Linux the policy applies only to a dotnet child, which this one never is.
        /// </summary>
        public static IReadOnlyDictionary<string, string> BuildDriverOverrides(bool windows)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!windows) return values;
            values["UseSharedCompilation"] = "false";
            values["DOTNET_gcServer"] = "0";
            values["DOTNET_gcConcurrent"] = "0";
            values["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
            values["MSBUILDDISABLENODEREUSE"] = "1";
            values["DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER"] = "1";
            return values;
        }

        /// <summary>
        /// New-LiveLaneChildStartInfo: never merges an ambient task or provider environment into the child. The inherited
        /// allow-list is looked up ignoring case (a hashtable) and kept under its own spelling; a selected key that is
        /// present stays present, empty or valued; then the build-driver policy of the platform.
        /// </summary>
        public static LiveLaneChildStart ChildStart(IReadOnlyDictionary<string, string> inherited, IReadOnlyDictionary<string, string> selected,
            string workingDirectory, bool windows)
        {
            var environment = new Dictionary<string, string>(windows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (var key in InheritedKeys)
                if (TryGet(inherited, key, out var value)) environment[key] = value ?? "";
            foreach (var key in SelectedKeys)
                if (TryGet(selected, key, out var value)) environment[key] = value ?? "";
            foreach (var pair in BuildDriverOverrides(windows)) environment[pair.Key] = pair.Value;
            return new LiveLaneChildStart
            {
                FileName = ChildFileName, Arguments = Array.Empty<string>(),
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory, Environment = environment
            };
        }

        private static bool TryGet(IReadOnlyDictionary<string, string> map, string key, out string value)
        {
            value = null;
            if (map == null) return false;
            if (map.TryGetValue(key, out value)) return true;
            foreach (var pair in map)
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) { value = pair.Value; return true; }
            return false;
        }

        /// <summary>
        /// Test-LiveLaneOwner: the lock file still names the saved owner (pid, run id, lane, the same process start) and that
        /// process is alive.
        /// </summary>
        public static bool OwnerHolds(ILiveLaneNativeLock nativeLock, LiveLaneLockOwner saved, string coordinationRoot)
        {
            var current = nativeLock.Owner(coordinationRoot);
            if (current == null || saved == null) return false;
            return current.Pid == saved.Pid && LiveLaneValues.Same(current.RunId, saved.RunId) && LiveLaneValues.Same(current.Lane, saved.Lane)
                && LiveLaneValues.SameInstant(current.ProcessStartedAt, saved.ProcessStartedAt) && nativeLock.IsAlive(saved.Pid);
        }

        /// <summary>The context the run issues, in the old [ordered] field order (ConvertTo-Json -Compress).</summary>
        public static string ContextJson(LiveLaneRequest request, LiveLaneLockOwner owner, string issuance)
        {
            var uids = new JsonArray();
            foreach (var uid in request.Uids ?? Array.Empty<string>()) uids.Add(uid);
            var context = new JsonObject
            {
                ["schemaVersion"] = 1, ["lane"] = request.Lane, ["runId"] = request.RunId, ["attempt"] = request.Attempt,
                ["source"] = request.Source, ["uids"] = uids, ["executable"] = request.Executable, ["profile"] = request.Profile,
                ["owner"] = new JsonObject
                {
                    ["pid"] = owner.Pid, ["processStartedAt"] = owner.ProcessStartedAt, ["runId"] = owner.RunId, ["lane"] = owner.Lane
                },
                ["issuance"] = issuance
            };
            return context.ToJsonString();
        }

        /// <summary>
        /// Test-LiveLaneContext: '' when the context read back is the one issued and its owner still holds the lock;
        /// otherwise lane-context-required, context-mismatch (a bound field), context-unissued (another path) or
        /// context-stale (the owner tuple or the lock moved).
        /// </summary>
        public static string ContextRefusal(ILiveLaneNativeLock nativeLock, JsonNode context, JsonObject expected, LiveLaneLockOwner expectedOwner,
            string issuedPath, string actualPath, string coordinationRoot)
        {
            if (context == null) return "lane-context-required";
            foreach (var field in ContextFields)
                if (!LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(context, field)) ?? "", LiveLaneValues.Text(expected[field]) ?? ""))
                    return "context-mismatch";
            var actualUids = LiveLaneValues.Items(LiveLaneValues.Field(context, "uids"));
            var expectedUids = LiveLaneValues.Items(expected["uids"]);
            if (actualUids.Count != expectedUids.Count) return "context-mismatch";
            for (var i = 0; i < actualUids.Count; i++)
                if (LiveLaneValues.AsString(actualUids[i]) == null || !LiveLaneValues.Same(LiveLaneValues.AsString(actualUids[i]), LiveLaneValues.AsString(expectedUids[i])))
                    return "context-mismatch";
            if (!LiveLaneValues.Same(actualPath, issuedPath)) return "context-unissued";
            var owner = LiveLaneValues.Field(context, "owner");
            var issuedOwner = expected["owner"];
            foreach (var field in new[] { "pid", "runId", "lane" })
                if (!LiveLaneValues.Same(LiveLaneValues.Text(LiveLaneValues.Field(owner, field)) ?? "", LiveLaneValues.Text(issuedOwner[field]) ?? ""))
                    return "context-stale";
            if (!LiveLaneValues.SameInstant(LiveLaneValues.Text(LiveLaneValues.Field(owner, "processStartedAt")), expectedOwner.ProcessStartedAt)
                || !OwnerHolds(nativeLock, expectedOwner, coordinationRoot)) return "context-stale";
            return "";
        }

        private static bool JoinChildren(LiveLaneChild child, LiveLaneBoundary boundary, bool stop)
        {
            var ok = true;
            var children = child.Children ?? Array.Empty<string>();
            if (stop)
                foreach (var id in children)
                {
                    try { boundary.Stop(id); }
                    catch (Exception) { ok = false; }
                }
            foreach (var id in children)
            {
                try { if (!boundary.Join(id, 30)) ok = false; }
                catch (Exception) { ok = false; }
            }
            return ok;
        }

        private static bool Refusing(int code) => code == 1 || code == 2;

        /// <summary>
        /// Invoke-LiveLaneRun. Without the inert fixture request and every required adapter the answer is a refusal before
        /// any I/O. Otherwise: the candidate, then the shared native lock (always before slot or checkpoint admission),
        /// the slot, an exclusive results root, the issued context read back and checked, the owner rechecked just before
        /// the handoff, the optional preflight, the owned start, the wait loop on the lane clock, one post-failure probe
        /// for classification only, the owned children stopped and joined, and the diagnostic receipt published by a
        /// same-directory rename. The lock is released only when every owned child is accounted for and the record still
        /// names this run; a lock replaced during teardown is never removed. Exceptions are never serialised.
        /// </summary>
        public static LiveLaneRunResult Run(LiveLaneRequest request, LiveLaneBoundary boundary, ILiveLaneNativeLock nativeLock)
        {
            var result = new LiveLaneRunResult();
            var windows = OperatingSystem.IsWindows();
            // This slice has no production native executor. Fake opt-in alone is insufficient: only this inert
            // roster, opaque executable and profile, and explicit I/O adapters exist.
            if (request == null || request.Mode != "Fixture") { result.Reason = "native-unqualified"; return result; }
            if (string.IsNullOrWhiteSpace(request.Executable)) { result.Reason = "executable-missing"; return result; }
            if (string.IsNullOrWhiteSpace(request.Profile)) { result.Reason = "profile-required"; return result; }
            var uids = request.Uids ?? Array.Empty<string>();
            if (request.Lane != FixtureLane || request.Executable != FixtureExecutable || request.Profile != FixtureProfile || uids.Count == 0
                || uids.Any(uid => !FixtureUids.Contains(uid, StringComparer.Ordinal)))
            {
                result.Reason = "native-unqualified";
                return result;
            }
            if (boundary == null || boundary.Candidate == null || boundary.Clock == null || boundary.Slot == null || boundary.WriteContext == null
                || boundary.Start == null || boundary.Wait == null || boundary.ReadTerminal == null || boundary.Stop == null || boundary.Join == null
                || boundary.WriteReceipt == null || nativeLock == null)
            {
                result.Reason = "fixture-boundary-required";
                return result;
            }
            if (string.IsNullOrWhiteSpace(request.Commission)) { result.Reason = "commission-required"; return result; }
            if (request.BudgetSeconds <= 0) { result.Reason = "budget-exhausted"; return result; }
            if (request.Source == null || !Sha.IsMatch(request.Source) || string.IsNullOrWhiteSpace(request.RunId) || request.Attempt < 1
                || string.IsNullOrWhiteSpace(request.CoordinationRoot) || string.IsNullOrWhiteSpace(request.ResultsRoot))
            {
                result.ExitCode = 2;
                result.Reason = "request-invalid";
                return result;
            }

            var owned = false;
            LiveLaneChild child = null;
            var terminal = false;
            var joined = false;
            var cleanupAttempted = false;
            LiveLaneLockEntry entry = null;
            try
            {
                var candidate = boundary.Candidate(request);
                if (candidate == null || !LiveLaneValues.Same(candidate.Sha, request.Source) || candidate.Clean != true)
                {
                    result.ExitCode = 2;
                    result.Reason = "candidate-invalid";
                    return result;
                }
                // The atomic shared native lock always precedes slot or checkpoint admission.
                entry = nativeLock.Enter(request.CoordinationRoot, request.RunId, request.Lane);
                if (!entry.Ok)
                {
                    result.ExitCode = entry.Reason == "deferred-busy" ? 21 : 2;
                    result.Reason = entry.Reason;
                    return result;
                }
                owned = true;
                boundary.Trace?.Invoke("lock-acquired");
                var started = boundary.Clock();
                var deadline = started + request.BudgetSeconds;
                if (!boundary.Slot(request))
                {
                    result.ExitCode = 4;
                    result.Reason = "slot-timeout";
                    return result;
                }
                // The results root is created exclusively, never reused to resume an old run.
                if (Directory.Exists(request.ResultsRoot) || File.Exists(request.ResultsRoot))
                {
                    result.ExitCode = 2;
                    result.Reason = "results-exist";
                    return result;
                }
                Directory.CreateDirectory(request.ResultsRoot);
                if (!windows) File.SetUnixFileMode(request.ResultsRoot, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                var issuedPath = Path.Combine(request.ResultsRoot, "lane-context.json");
                var expectedJson = ContextJson(request, entry.Owner, Guid.NewGuid().ToString("N"));
                var expected = (JsonObject)JsonNode.Parse(expectedJson);
                var actualPath = boundary.WriteContext(issuedPath, expectedJson);
                if (string.IsNullOrWhiteSpace(actualPath) || !File.Exists(actualPath))
                {
                    result.Reason = "lane-context-required";
                    return result;
                }
                var context = JsonNode.Parse(File.ReadAllText(actualPath));
                var refusal = ContextRefusal(nativeLock, context, expected, entry.Owner, issuedPath, actualPath, request.CoordinationRoot);
                if (refusal.Length > 0)
                {
                    result.ExitCode = 2;
                    result.Reason = refusal;
                    return result;
                }
                var manifest = Path.Combine(request.ResultsRoot, "fixture-checkpoints.yaml");
                // An inert, deliberately non-executable synthetic spec. Only the injected fixture adapter sees it.
                // Native manifest generation belongs to S1c.
                File.WriteAllText(manifest, "schemaVersion: -1545\nformat: admitted-fixture-only\n");
                var selected = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["CLAUDE_CONFIG_DIR"] = request.Profile, ["ANTIPHON_LIVE_CLAUDE_EXE"] = request.Executable, ["ANTIPHON_LIVE_LANE_CONTEXT"] = issuedPath
                };
                var start = ChildStart(request.Inherited, selected, request.ResultsRoot, windows);
                // Recheck current ownership immediately before the adapter handoff.
                if (!OwnerHolds(nativeLock, entry.Owner, request.CoordinationRoot))
                {
                    result.ExitCode = 2;
                    result.Reason = "context-stale";
                    return result;
                }
                if (boundary.Clock() >= deadline)
                {
                    result.Reason = "deadline";
                    return result;
                }
                // S22: the Claude version is asked of the executable the run targets, before anything runs with it, and
                // recorded beside the code digest (a failure to answer records unknown; it never refuses the run).
                if (boundary.Process != null) result.Claude = LiveLaneClaudeVersion.Probe(boundary.Process, request.Executable);
                if (boundary.Probe != null)
                {
                    var preflight = LiveLaneOutcome.Preflight(boundary, request, deadline);
                    result.Probes = preflight.Probes.Select(LiveLaneOutcome.Observation).ToList();
                    if (preflight.Verdict.ExitCode != 0)
                    {
                        result.ExitCode = preflight.Verdict.ExitCode;
                        result.Reason = preflight.Verdict.Reason;
                        return result;
                    }
                }
                child = boundary.Start(context, start, manifest);
                if (child == null || child.Children == null || child.Children.Count == 0) throw new InvalidOperationException("invalid owned adapter handle");
                // A checkpoint identity is an opaque bounded token, never a path. Validate it before it reaches the wait
                // and read calls or the diagnostic receipt.
                if (child.RunId == null || !CheckpointRunId.IsMatch(child.RunId))
                {
                    result.ExitCode = 2;
                    result.Reason = "checkpoint-run-invalid";
                    return result;
                }
                result.CheckpointRunId = child.RunId;
                while (true)
                {
                    if (boundary.Clock() >= deadline) { result.Reason = "deadline"; break; }
                    var code = boundary.Wait(child.RunId);
                    result.RawChildExit = code;
                    result.ChildExits.Add(code);
                    if (code == 75) continue;
                    terminal = true;
                    var evidence = boundary.ReadTerminal(child.RunId);
                    LiveLaneProbe postProbe = null;
                    // One uid-bound zero-prompt probe for classification only. Never restart the checkpoint or submit a
                    // model turn after any UserPrompt receipt.
                    if (boundary.Probe != null && evidence is JsonObject)
                    {
                        var failed = LiveLaneValues.Items(LiveLaneValues.Field(evidence, "Rows")).FirstOrDefault(row =>
                            LiveLaneValues.Text(LiveLaneValues.Field(row, "State")) == "Failed" && !LiveLaneOutcome.TranscriptOutage(row, out _));
                        if (failed != null)
                        {
                            var probeDeadline = Math.Min(deadline, boundary.Clock() + 15);
                            try
                            {
                                postProbe = LiveLaneOutcome.Probe(boundary, "PostFailure", LiveLaneProbeIdentity.Of(failed), probeDeadline);
                                result.Probes.Add(LiveLaneOutcome.Observation(postProbe));
                            }
                            catch (Exception)
                            {
                                // A transport failure is no outage proof. Keep the original assertion and every terminal
                                // fact for the classifier.
                                postProbe = null;
                            }
                        }
                    }
                    var outcome = LiveLaneOutcome.Evaluate(request, evidence, child.RunId, code, postProbe);
                    result.ExitCode = outcome.ExitCode;
                    result.Reason = outcome.Reason;
                    result.Rows = outcome.Rows;
                    result.RawChildOutcome = outcome.RawChildOutcome;
                    break;
                }
                // Stop every still-running owned child before joining any. No foreign pid is ever inferred from a lock
                // file or an ambient process listing.
                cleanupAttempted = true;
                joined = JoinChildren(child, boundary, !terminal);
                if (!joined)
                {
                    if (!Refusing(result.ExitCode)) { result.ExitCode = 20; result.Reason = "cleanup-incomplete"; }
                    return result;
                }
                result.TerminalComplete = terminal;
                result.Classification = LiveLaneOutcome.Classification(result.ExitCode);
                // A writer can fail after creating bytes. Only a successful same-directory rename publishes the final
                // receipt; a partial or missing write stays incomplete.
                var pendingReceipt = Path.Combine(request.ResultsRoot, "diagnostic.pending.json");
                try
                {
                    boundary.WriteReceipt(pendingReceipt, result);
                    File.Move(pendingReceipt, Path.Combine(request.ResultsRoot, "diagnostic.json"));
                }
                finally
                {
                    // An interrupted write cannot leave a pending record claiming GREEN.
                    if (File.Exists(pendingReceipt)) File.Delete(pendingReceipt);
                }
            }
            catch (Exception)
            {
                // Never serialise an arbitrary exception from a provider, path or environment.
                if (!Refusing(result.ExitCode)) { result.ExitCode = 20; result.Reason = "handoff-incomplete"; }
                result.TerminalComplete = false;
            }
            finally
            {
                if (child != null && !cleanupAttempted)
                {
                    joined = JoinChildren(child, boundary, true);
                    if (!joined)
                    {
                        if (!Refusing(result.ExitCode)) { result.ExitCode = 20; result.Reason = "cleanup-incomplete"; }
                        result.TerminalComplete = false;
                    }
                }
                if (owned && (child == null || joined))
                {
                    boundary.BeforeRelease?.Invoke(request);
                    // The release is guarded by pid, start, run and lane: it cannot remove a lock replaced during teardown.
                    if (OwnerHolds(nativeLock, entry.Owner, request.CoordinationRoot))
                    {
                        nativeLock.Exit(request.CoordinationRoot, request.RunId);
                        boundary.Trace?.Invoke("release");
                    }
                }
                result.Classification = LiveLaneOutcome.Classification(result.ExitCode);
            }
            return result;
        }
    }

    /// <summary>
    /// live-lane-run's flags, kept from run-live-lane.ps1's Native parameter set (all optional, all ignored: the run is
    /// native-unqualified whatever they say), and --claude (S22): the Claude executable the run targets (the request's
    /// Executable, passed to the child as ANTIPHON_LIVE_CLAUDE_EXE; the CLI defaults it to that variable). The version a
    /// run records is asked of this executable; the native mode refuses before it asks anything. Kebab case; an unknown,
    /// repeated or valueless flag, or a positional argument, is a usage error.
    /// </summary>
    public sealed class LiveLaneRunOptions
    {
        public const string Usage = "live-lane-run [--lane <id>] [--candidate-ref <ref>] [--expected-sha <sha>] [--manifest <path>] [--coordination-root <dir>] [--results-root <dir>] [--claude <exe>]";
        private static readonly string[] Flags = { "--lane", "--candidate-ref", "--expected-sha", "--manifest", "--coordination-root", "--results-root", "--claude" };

        public string Lane { get; private set; }
        public string CandidateRef { get; private set; }
        public string ExpectedSha { get; private set; }
        public string Manifest { get; private set; }
        public string CoordinationRoot { get; private set; }
        public string ResultsRoot { get; private set; }
        /// <summary>The Claude executable the run targets (null: none named).</summary>
        public string Claude { get; private set; }

        public static LiveLaneRunOptions Parse(IReadOnlyList<string> arguments, string defaultClaude = null)
        {
            var values = LiveLaneArgs.Parse(arguments, Flags, Array.Empty<string>());
            return new LiveLaneRunOptions
            {
                Lane = values.Value("--lane"), CandidateRef = values.Value("--candidate-ref"), ExpectedSha = values.Value("--expected-sha"),
                Manifest = values.Value("--manifest"), CoordinationRoot = values.Value("--coordination-root"), ResultsRoot = values.Value("--results-root"),
                Claude = values.Value("--claude") ?? (string.IsNullOrWhiteSpace(defaultClaude) ? null : defaultClaude)
            };
        }
    }

    /// <summary>
    /// antiphon-scripts live-lane-run: run-live-lane.ps1's native mode. It always answers native-unqualified (exit 20) with
    /// the compact JSON record on stdout, and never takes a lock, starts a process or reaches a provider.
    /// </summary>
    public static class LiveLaneRunCommand
    {
        public const string Name = "live-lane-run";

        public static int Run(LiveLaneRunOptions options, TextWriter output)
        {
            // The flags name a native run; there is no native executor, so they cannot change the answer. The executable is
            // carried so that a run records the version of the Claude it targets; the native mode never asks it.
            var result = LiveLaneRun.Run(new LiveLaneRequest { Mode = "Native", Lane = options?.Lane, Executable = options?.Claude }, null, null);
            output.WriteLine(result.ToJson());
            return result.ExitCode;
        }
    }
}
