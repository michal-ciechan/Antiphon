// The offline CARD-0727 fixtures of the rolling deploy (unit 68 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md,
// W10 part 3): the C# port of scripts/fixtures/c727-fake-http.ps1 (the runner and census HTTP) and c727-fake-verify.ps1 (the host
// cases, the host jq prerequisite, the rollout lock, the recycle discovery and the two temp censuses), on the same state shapes: the
// generic CARD-0727 scenario flags (tempDeployed, oldDraining, faultRunner, ...) and the CARD-1008 statuses/tasks shape. Unit 65 wrote
// this port for the in-process tests (Server2RollingWorld, checked against every driver scenario captured with the PowerShell fakes);
// unit 68 moved it here so the W10 node harnesses' C727_TEST_* seams (Server2RollingStubOs) and the c727-fake-verify CLI verb answer
// from it in process, where they started pwsh -File <fake>.ps1.
//
// Each answer is what the PowerShell fake printed: its values read and written back as ConvertFrom-Json and ConvertTo-Json did, a
// save at the fake's depth (SavedText holds the last save's text, which a file-backed caller writes back as Set-Content did), and
// each call writes the fake's trace row to Trace. Plain C#: no PowerShell types. ASCII only.
#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;

namespace Antiphon.Nightly
{
    /// <summary>The CARD-0727 fakes of the rolling deploy's runner HTTP and host cases, on one JSON state.</summary>
    public sealed class C727Fakes
    {
        /// <summary>The fake's state (the PowerShell fake's C727_TEST_STATE file, read at the start of each call).</summary>
        public JsonObject State { get; set; } = new JsonObject();

        /// <summary>Each trace row the fake appended to C727_TEST_TRACE, in order.</summary>
        public Action<JsonObject> Trace { get; set; } = _ => { };

        /// <summary>The text of the last save (ConvertTo-Json -Compress at the fake's depth); null while nothing was saved.</summary>
        public string? SavedText { get; private set; }

        /// <summary>Sees each runner request's and census read's full URL.</summary>
        public Action<string>? UrlObserver { get; set; }

        /// <summary>
        /// The cold marker reader the verify fake ran for a markerPath state (scripts/fixtures/c973-marker-reader.sh over
        /// c590-remote.sh, through bash or WSL): its exit code. Null: it passes.
        /// </summary>
        public Func<string, JsonObject, int>? MarkerReader { get; set; }

        /// <summary>How long the rollout lock waits for a held rolloutLockFile (the seam's 70 s for the C1008_LOCKED line).</summary>
        public TimeSpan LockWait { get; set; } = TimeSpan.FromSeconds(70);

        // ---- the fakes' reading and writing ----

        private static bool Truthy(JsonNode? node) => RollingPs.True(Ps(node));

        // PowerShell kept Int64 arithmetic for whole values.
        private static JsonNode Whole(double value) => value == Math.Floor(value) ? JsonValue.Create((long)value) : JsonValue.Create(value);

        private static double Num(JsonNode? node) => node == null ? 0 : Convert.ToDouble(Ps(node), System.Globalization.CultureInfo.InvariantCulture);

        private static string Text(JsonNode? node) => RollingPs.Str(Ps(node));

        private static object? Ps(JsonNode? node) => node == null ? null : RollingPs.Parse(node.ToJsonString());

        // ConvertTo-Json -Compress -Depth depth of a value the fake read from its state.
        private static string Json(JsonNode? node, int depth = 2) => RollingPs.Json(Ps(node), depth);

        // The fake's save at its ConvertTo-Json depth: deeper values became their [string] text, dates their round-trip text.
        private void Save(int depth)
        {
            SavedText = RollingPs.Json(Ps(State), depth);
            State = JsonNode.Parse(SavedText)!.AsObject();
        }

        private void Write(JsonObject row) => Trace(row);

        private static bool Has(JsonNode? node, string name, bool exact = false) =>
            node is JsonObject o && o.Any(p => string.Equals(p.Key, name, exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

        private static JsonNode? Get(JsonNode? node, string name) =>
            node is JsonObject o ? o.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value : null;

        private sealed class FakeExit : Exception
        {
            public FakeExit(int code) : base("fake exit " + code) => Code = code;

            public int Code { get; }
        }

        /// <summary>The state's clockMs as a time (the temp container retirement's fixture clock); null without one.</summary>
        public DateTime? ClockNow() => Has(State, "clockMs") ? DateTime.UnixEpoch.AddMilliseconds(Num(Get(State, "clockMs"))) : null;

        /// <summary>The retirement poll advances the state's clockMs (saved at depth 30); nothing without one.</summary>
        public void AdvanceClock(int milliseconds)
        {
            if (!Has(State, "clockMs")) return;
            State["clockMs"] = Whole(Num(Get(State, "clockMs")) + milliseconds);
            Save(30);
        }

        // ---- c727-fake-http.ps1 ----

        /// <summary>
        /// One runner request (-Method, -RunnerId, -Suffix, -BodyJson from the URL and body): the fake's printed answer (JSON, __404__
        /// or __503__), or null where it exited non-zero. Writes the http trace row first.
        /// </summary>
        public string? RunnerText(string method, string url, string? body)
        {
            var path = new Uri(url).AbsolutePath;
            var rest = path.Substring("/api/session-runners/".Length);
            var slash = rest.IndexOf('/');
            var runnerId = Uri.UnescapeDataString(slash < 0 ? rest : rest.Substring(0, slash));
            var suffix = slash < 0 ? "" : rest.Substring(slash);
            UrlObserver?.Invoke(url);
            var bodyNode = string.IsNullOrEmpty(body) ? null : JsonNode.Parse(body);
            Write(new JsonObject { ["kind"] = "http", ["method"] = method, ["runnerId"] = runnerId, ["suffix"] = suffix, ["path"] = "", ["body"] = bodyNode?.DeepClone() });
            return Runner(method, runnerId, suffix, bodyNode);
        }

        /// <summary>One runner request as an HTTP answer: __404__ is 404, __503__ 503; no answer throws (the request failed).</summary>
        public Server2RollingHttpAnswer RunnerHttp(string method, string url, string? body)
        {
            var text = RunnerText(method, url, body);
            if (text == null) throw new HttpRequestException("RunnerApiUnavailable fixture");
            if (text == "__404__") return new Server2RollingHttpAnswer(404, "");
            if (text == "__503__") return new Server2RollingHttpAnswer(503, "");
            return new Server2RollingHttpAnswer(200, text);
        }

        // The fake's answer text, null where it exited non-zero.
        private string? Runner(string method, string runnerId, string suffix, JsonNode? body)
        {
            var state = State;
            if (Text(state["scenario"]) == "c1008")
            {
                var statuses = Get(state, "statuses");
                if (method == "POST" && Truthy(state["allowClear"]) && suffix == "/drain/clear")
                {
                    var slot = Get(statuses, runnerId)!.AsObject();
                    // A retired temp placeholder is reactivated, not opened for dispatch.
                    if (runnerId == "server2-temp" && Truthy(slot["retiredAt"]))
                    {
                        slot["retiredAt"] = null;
                        if (slot["runnerSessions"] == null) slot["runnerSessions"] = 0;
                    }
                    else
                    {
                        slot["draining"] = false;
                        slot["acceptingNewWork"] = true;
                    }
                    Save(20);
                    return "{}";
                }
                if (method == "POST" && Truthy(state["allowClear"]) && suffix == "/drain" && runnerId == "server2-temp")
                {
                    var slot = Get(statuses, runnerId)!.AsObject();
                    slot["draining"] = true;
                    slot["redirectTo"] = body != null && Truthy(Get(body, "redirectTo")) ? Text(Get(body, "redirectTo")) : "server2";
                    slot["retireWhenIdle"] = body != null && RollingPs.True(Ps(Get(body, "retireWhenIdle")));
                    slot["acceptingNewWork"] = false;
                    if (slot["runnerSessions"] == null) slot["runnerSessions"] = 0;
                    Save(20);
                    return "{}";
                }
                if (method != "GET") return null;
                if (suffix == "/status" && Has(statuses, runnerId))
                {
                    if (runnerId == "server2-temp" && state["statusSequence"] is JsonArray sequence && sequence.Count > 0)
                    {
                        var observation = sequence[0]!;
                        state["clockMs"] = observation["clockMs"]?.DeepClone();
                        statuses!["server2-temp"] = observation["status"]?.DeepClone();
                        state["statusSequence"] = new JsonArray(sequence.Skip(1).Select(x => x?.DeepClone()).ToArray());
                        Save(30);
                        state = State;
                    }
                    return Json(Get(Get(state, "statuses"), runnerId), 20);
                }
                return null;
            }
            if (method == "POST")
            {
                if (!(suffix is "/drain" or "/drain/clear") || !(runnerId is "server2" or "server2-temp")) return null;
                if (runnerId == "server2" && suffix == "/drain") state["oldDraining"] = true;
                if (runnerId == "server2" && suffix == "/drain/clear") state["oldDraining"] = false;
                if (runnerId == "server2-temp" && suffix == "/drain")
                {
                    if (Text(state["scenario"]) == "hold-post-fails") return "__503__";
                    if (Text(state["scenario"]) == "retire-race") state["tempRetiredAt"] = "2026-09-27T10:00:00Z";
                    state["tempDraining"] = true;
                    state["tempRedirectTo"] = Get(body, "redirectTo")?.DeepClone();
                    state["tempRetireWhenIdle"] = RollingPs.True(Ps(Get(body, "retireWhenIdle")));
                    if (Text(Get(body, "reason")) == "CARD-0727 rolling upgrade complete")
                    {
                        state["tempRetiredAt"] = "2026-09-27T10:00:00Z";
                        state["tempOffline"] = true;
                    }
                }
                if (runnerId == "server2-temp" && suffix == "/drain/clear")
                {
                    state["tempDraining"] = false;
                    state["tempRedirectTo"] = null;
                    state["tempRetireWhenIdle"] = false;
                    state["tempRetiredAt"] = null;
                }
                Save(2);
                return "{}";
            }
            if (suffix != "/status") return null;
            if (Text(state["faultRunner"]) == runnerId && Text(state["faultKind"]) == "503") return "__503__";
            if (runnerId == "server2-temp")
            {
                if (Text(state["scenario"]) == "missing") return "__404__";
                var offline = !Truthy(state["tempContainer"]) || Truthy(state["tempOffline"]) || (Truthy(state["tempRetiredAt"]) && !Truthy(state["tempRunning"]));
                var recovering = Text(state["scenario"]) == "recovering" && !Has(state, "recoveryObserved");
                if (recovering) { state["recoveryObserved"] = true; Save(2); state = State; }
                var accepting = Text(state["scenario"]) != "ineligible" && !Truthy(state["tempDraining"]) && !offline;
                var status = new PlainObject(
                    ("available", !offline), ("acceptingNewWork", accepting), ("dispatchEligible", Text(state["scenario"]) != "ineligible" && !offline),
                    ("buildVersion", Truthy(state["tempDeployed"]) ? Ps(state["sha"]) : "old"), ("draining", Truthy(state["tempDraining"])),
                    ("redirectTo", Truthy(state["tempDraining"]) ? Ps(state["tempRedirectTo"]) : null), ("retireWhenIdle", Truthy(state["tempRetireWhenIdle"])),
                    ("retiredAt", Ps(state["tempRetiredAt"])), ("sessions", 0), ("runnerSessions", offline ? null : 0), ("queuedTasks", 0));
                Fault(state, runnerId, status);
                if (recovering) { status["dispatchEligible"] = false; status["acceptingNewWork"] = false; status["runnerSessions"] = null; }
                return RollingPs.Json(status);
            }
            if (runnerId == "server2")
            {
                var sessions = Text(state["scenario"]) == "busy" ? 1 : 0;
                var version = Truthy(state["oldDeployed"]) || Text(state["scenario"]) == "rerun" ? Ps(state["sha"]) : "old";
                var status = new PlainObject(
                    ("available", true), ("acceptingNewWork", !Truthy(state["oldDraining"])), ("dispatchEligible", true), ("buildVersion", version),
                    ("draining", Truthy(state["oldDraining"])), ("redirectTo", Truthy(state["oldDraining"]) ? "server2-temp" : null), ("retireWhenIdle", false),
                    ("retiredAt", null), ("sessions", sessions), ("runnerSessions", 0), ("queuedTasks", 0));
                Fault(state, runnerId, status);
                return RollingPs.Json(status);
            }
            return null;
        }

        private static void Fault(JsonObject state, string runnerId, PlainObject status)
        {
            if (Text(state["faultRunner"]) != runnerId || !Truthy(state["faultField"])) return;
            var field = Text(state["faultField"]);
            if (Text(state["faultKind"]) == "omitted") status.Remove(field);
            else status[field] = Ps(state["faultValue"]);
        }

        /// <summary>
        /// One census read (-Method GET -Path): the fake's printed answer (JSON, __TIMEOUT__, __NNN__, empty), or null where it exited
        /// non-zero. A typed fault (reset, a WebException status, typed-timeout) is the exception the fake threw. Writes the trace row
        /// and shows the URL first.
        /// </summary>
        public string? ReadText(string path, string url)
        {
            ReadRow(path, url);
            return ReadAnswer(path);
        }

        /// <summary>A census read's trace row, then its URL to the observer (ReadText's first half).</summary>
        public void ReadRow(string path, string url)
        {
            Write(new JsonObject { ["kind"] = "http", ["method"] = "GET", ["runnerId"] = "", ["suffix"] = "", ["path"] = path, ["body"] = null });
            UrlObserver?.Invoke(url);
        }

        /// <summary>The read after its trace row (ReadText's second half); a fake exit is null.</summary>
        public string? ReadAnswer(string path)
        {
            try { return Read(path); }
            catch (FakeExit) { return null; }
        }

        /// <summary>A census read as an HTTP answer: no answer throws, __TIMEOUT__ is a timeout and __NNN__ that status.</summary>
        public static Server2RollingHttpAnswer ReadAnswerHttp(string? text)
        {
            if (text == null) throw new InvalidOperationException("fixture transport");
            if (text == "__TIMEOUT__") throw new TimeoutException("fixture timeout");
            var status = Regex.Match(text, "^__(\\d{3})__$");
            if (status.Success) return new Server2RollingHttpAnswer(int.Parse(status.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), "");
            return new Server2RollingHttpAnswer(200, text);
        }

        private string? Read(string path)
        {
            var state = State;
            if (Get(state, "readFaults") is JsonObject faults)
            {
                foreach (var (name, faultValue) in faults.ToArray())
                {
                    var isArray = faultValue is JsonArray;
                    if (!((isArray && path == name) || (!isArray && path.Contains(name, StringComparison.Ordinal)))) continue;
                    var value = faultValue;
                    if (faultValue is JsonArray list)
                    {
                        value = list.Count > 0 ? list[0] : null;
                        if (list.Count > 1)
                        {
                            faults[name] = new JsonArray(list.Skip(1).Select(x => x?.DeepClone()).ToArray());
                            Save(30);
                            state = State;
                        }
                    }
                    var text = Text(value);
                    if (string.Equals(text, "ok", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(text, "timeout", StringComparison.OrdinalIgnoreCase)) return "__TIMEOUT__";
                    if (Regex.IsMatch(text, "^\\d{3}$")) return "__" + text + "__";
                    if (string.Equals(text, "reset", StringComparison.OrdinalIgnoreCase))
                        throw new HttpRequestException("SENTINEL_RESET", new SocketException((int)SocketError.ConnectionReset));
                    foreach (var web in new[] { "ConnectionClosed", "ReceiveFailure", "SendFailure", "NameResolutionFailure", "TrustFailure" })
                        if (string.Equals(text, web, StringComparison.OrdinalIgnoreCase))
#pragma warning disable SYSLIB0014
                            throw new WebException("SENTINEL_WEB", Enum.Parse<WebExceptionStatus>(web));
#pragma warning restore SYSLIB0014
                    if (string.Equals(text, "typed-timeout", StringComparison.OrdinalIgnoreCase)) throw new TimeoutException("SENTINEL_TIMEOUT");
                    if (string.Equals(text, "transport", StringComparison.OrdinalIgnoreCase)) return null;
                    if (string.Equals(text, "empty", StringComparison.OrdinalIgnoreCase)) return "";
                    if (string.Equals(text, "malformed", StringComparison.OrdinalIgnoreCase)) return "{SENTINEL_C1087_BODY";
                    return null;
                }
            }
            if (path == "/api/projects?includeArchived=true")
            {
                if (Has(state, "projects")) return RollingPs.Json(Ps(Get(state, "projects")) ?? Array.Empty<object>(), 20);
                return "[{\"id\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1\",\"name\":\"Antiphon\",\"gitRepositoryUrl\":\"https://github.com/michal-ciechan/Antiphon.git\",\"localRepositoryPath\":null,\"archivedAt\":null}]";
            }
            if (path.StartsWith("/api/projects/", StringComparison.Ordinal))
            {
                var id = path.Substring("/api/projects/".Length);
                var details = Get(state, "projectDetail");
                return Truthy(details) && Has(details, id) ? RollingPs.Json(Ps(Get(details, id)), 20) : "__404__";
            }
            if (Truthy(Get(state, "taskError"))) return null;
            if (path.StartsWith("/api/agent-tasks?", StringComparison.Ordinal))
            {
                var scopeId = Regex.Match(path, "projectId=([^&]+)").Groups[1].Value;
                JsonNode? envelope;
                if (Get(state, "taskQueries") is JsonObject queries && queries.Any(q => q.Key == path))
                {
                    envelope = queries[path];
                    // Sequence entries are consumed per exact path, permitting pass-two drift.
                    if (envelope is JsonArray sequence)
                    {
                        var first = sequence.Count > 0 ? sequence[0] : null;
                        if (sequence.Count > 1)
                        {
                            queries[path] = new JsonArray(sequence.Skip(1).Select(x => x?.DeepClone()).ToArray());
                            Save(30);
                        }
                        envelope = first;
                    }
                    return RollingPs.Json(Ps(envelope), 20);
                }
                var scopes = Get(state, "taskScopes");
                envelope = Truthy(scopes) && Has(scopes, scopeId) ? Get(scopes, scopeId)
                    : Truthy(Get(state, "tasks")) ? Get(state, "tasks")
                    : JsonNode.Parse("{\"items\":[],\"scope\":{\"projectId\":\"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaa1\",\"unscoped\":\"include\"},\"excluded\":{\"total\":0,\"unscoped\":0,\"byProject\":[]}}");
                var value = Ps(envelope);
                // Preserve malformed envelopes so fixtures can still prove fail-closed checks.
                if (value is PlainObject o && o["items"] is object[] items)
                {
                    if (path.Contains("&status=", StringComparison.Ordinal))
                    {
                        var statuses = Regex.Match(path, "status=([^&]+)").Groups[1].Value.Split(',');
                        o["items"] = items.Where(i => RollingPs.CIn(RollingPs.Prop(i, "status"), statuses)).ToArray();
                    }
                    else if (path.Contains("&landPending=true", StringComparison.Ordinal))
                        o["items"] = items.Where(i => RollingPs.Prop(i, "landRequestedAt") != null || RollingPs.Prop(i, "landStartedAt") != null).ToArray();
                }
                return RollingPs.Json(value, 20);
            }
            if (path.StartsWith("/api/agent-tasks/", StringComparison.OrdinalIgnoreCase))
            {
                var id = path.Substring("/api/agent-tasks/".Length);
                var details = Get(state, "details");
                if (Truthy(details) && Has(details, id))
                {
                    var detail = Get(details, id);
                    // A per-id array is consumed one observation at a time so a later pass can disagree with the first.
                    if (detail is JsonArray list)
                    {
                        var chosen = list.Count > 0 ? list[0] : null;
                        if (list.Count > 1)
                        {
                            ((JsonObject)details!)[id] = new JsonArray(list.Skip(1).Select(x => x?.DeepClone()).ToArray());
                            Save(30);
                        }
                        return RollingPs.Json(Ps(chosen), 20);
                    }
                    return detail == null ? "null" : RollingPs.Json(Ps(detail), 20);
                }
                var summary = (Get(Get(state, "tasks"), "items") as JsonArray)?.FirstOrDefault(i => RollingPs.Eq(Ps(Get(i, "id")), id));
                object? summaryValue = summary != null ? Ps(summary) : new PlainObject(("id", id));
                return RollingPs.Json(new PlainObject(("summary", summaryValue), ("landRequest", null)), 20);
            }
            return null;
        }

        // ---- c727-fake-verify.ps1 ----

        private sealed class FakeLock : IServer2RollingLock
        {
            private readonly FileStream? _file;

            public FakeLock(FileStream? file) => _file = file;

            public void Release() => _file?.Dispose();
        }

        /// <summary>
        /// The rollout-lock case: the lock-request row, then the state's rolloutLockFile held exclusively (retried every 20 ms) until
        /// Release. A lock still held after LockWait is RecycleRolloutBusy, as the seam's wait for C1008_LOCKED was.
        /// </summary>
        public IServer2RollingLock EnterRolloutLock()
        {
            Write(new JsonObject { ["kind"] = "lock-request" });
            FileStream? file = null;
            if (Truthy(Get(State, "rolloutLockFile")))
            {
                var lockPath = Text(Get(State, "rolloutLockFile"));
                var waited = Stopwatch.StartNew();
                while (file == null)
                {
                    try { file = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                    catch (IOException)
                    {
                        if (waited.Elapsed >= LockWait) throw new Server2RollingRefusal("RecycleRolloutBusy");
                        Thread.Sleep(20);
                    }
                }
            }
            return new FakeLock(file);
        }

        /// <summary>The recycle-discover case: an incomplete receipt's id when the state names one; exit 2 for discoveryError.</summary>
        public Server2RollingChild RecycleDiscover()
        {
            var stdout = Truthy(Get(State, "incompleteRecycle")) ? "c100800000000000000000000000000000001" + Environment.NewLine : "";
            if (Truthy(Get(State, "discoveryError"))) return new Server2RollingChild(2, stdout, "");
            return new Server2RollingChild(0, stdout, "");
        }

        // The manifest check every case after the lock and the discovery made: this SHA and a c727 run id.
        private bool RequestValid(object? request) =>
            RollingPs.Ceq(RollingPs.Prop(request, "sourceSha"), Ps(Get(State, "sha"))) && RollingPs.Match(RollingPs.Prop(request, "runId"), "^c727[0-9a-f]{12}$");

        /// <summary>The host-jq-prerequisite case on the call's request: the proof, or the fault the state's hostJqSequence names.</summary>
        public Server2RollingChild HostJq(Server2RollingCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            var request = new PlainObject(("sourceSha", call.Sha), ("runId", call.RunId), ("selectedPhase", call.SelectedPhase), ("phase", call.Phase),
                ("mode", call.Mode), ("evidenceRoot", call.EvidenceRoot));
            if (!RequestValid(request)) return new Server2RollingChild(2, "", "");
            if (!RollingPs.In(call.Mode, "check", "provision")
                || !RollingPs.In(call.Phase, "deploy-temp", "drain-old", "redeploy-old", "drain-temp", "retire-temp", "check-host-jq", "provision-host-jq"))
                return new Server2RollingChild(2, "", "");
            var outcome = "healthy";
            if (Get(State, "hostJqSequence") is JsonArray sequence && sequence.Count > 0)
            {
                outcome = Text(sequence[0]);
                State["hostJqSequence"] = new JsonArray(sequence.Skip(1).Select(x => x?.DeepClone()).ToArray());
                Save(30);
            }
            Write(new JsonObject
            {
                ["kind"] = "prerequisite", ["name"] = "host-jq-prerequisite", ["mode"] = call.Mode, ["phase"] = call.Phase, ["selectedPhase"] = call.SelectedPhase,
                ["sourceSha"] = call.Sha, ["runId"] = call.RunId, ["evidenceRoot"] = call.EvidenceRoot, ["outcome"] = outcome,
            });
            switch (outcome)
            {
                case "missing": return new Server2RollingChild(2, "", "HostJqMissing");
                case "invalid": return new Server2RollingChild(2, "", "HostJqInvalid");
                case "ssh-failed": return new Server2RollingChild(255, "", "");
                case "malformed-proof": return new Server2RollingChild(0, "{", "");
                case "receipt-write-failed": Directory.CreateDirectory(Path.Combine(call.EvidenceRoot, "host-jq-" + call.Phase + ".json")); break;
                case "healthy": break;
                default: return new Server2RollingChild(2, "", "");
            }
            var proof = new PlainObject(("schema", 1), ("lane", "host"), ("mode", call.Mode), ("lookupPath", "/usr/local/bin/jq"), ("path", "/usr/local/bin/jq"),
                ("version", "jq-1.7.1"), ("digest", Server2RollingDeploy.HostJqDigest), ("uid", 0), ("gid", 0), ("permissions", "755"), ("trueExit", 0),
                ("falseExit", 1), ("installed", false), ("outcome", "existing"));
            return new Server2RollingChild(0, RollingPs.Json(proof), "");
        }

        /// <summary>The temp-project-absent census: the fixture container's line while the state has one.</summary>
        public Server2RollingChild TempProjectCensus(Server2RollingCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (!RequestValid(new PlainObject(("sourceSha", call.Sha), ("runId", call.RunId)))) return new Server2RollingChild(2, "", "");
            Write(new JsonObject { ["kind"] = "census", ["name"] = "temp-project-absent" });
            if (Text(Get(State, "scenario")) == "census-failed" || Truthy(Get(State, "censusError"))) return new Server2RollingChild(2, "", "");
            return new Server2RollingChild(0, Truthy(Get(State, "tempContainer")) ? "fixture-temp-container" + Environment.NewLine : "", "");
        }

        /// <summary>The retirement-proof-read (call.Operation set) or temp-retirement-clearable census.</summary>
        public Server2RollingChild TempRetirementRead(Server2RollingCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            return string.IsNullOrEmpty(call.Operation) ? ClearableCensus(call) : ProofRead(call);
        }

        /// <summary>The retirement-proof-read case: a Ready scripted proof for the call's operation.</summary>
        public Server2RollingChild ProofRead(Server2RollingCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (!RequestValid(new PlainObject(("sourceSha", call.Sha), ("runId", call.RunId)))) return new Server2RollingChild(2, "", "");
            Write(new JsonObject { ["kind"] = "proof-read" });
            if (Truthy(Get(State, "retirementProofError"))) return new Server2RollingChild(2, "", "");
            var proof = new PlainObject(("operationId", call.Operation ?? ""), ("digest", new string('a', 64)), ("state", "Ready"), ("mode", "scripted"));
            return new Server2RollingChild(0, RollingPs.Json(proof) + Environment.NewLine, "");
        }

        /// <summary>The temp-retirement-clearable census: the state's census, or none.</summary>
        public Server2RollingChild ClearableCensus(Server2RollingCall call)
        {
            if (call == null) throw new ArgumentNullException(nameof(call));
            if (!RequestValid(new PlainObject(("sourceSha", call.Sha), ("runId", call.RunId)))) return new Server2RollingChild(2, "", "");
            Write(new JsonObject { ["kind"] = "census", ["name"] = "temp-retirement-clearable" });
            if (Truthy(Get(State, "tempRetirementCensusError"))) return new Server2RollingChild(2, "", "");
            var census = Has(State, "tempRetirementCensus") && Get(State, "tempRetirementCensus") != null
                ? RollingPs.Json(Ps(Get(State, "tempRetirementCensus")), 8) : "{\"volumes\":[],\"journals\":[]}";
            if (Truthy(Get(State, "tempRetirementCensusAfterRead")))
            {
                State["tempRetirementCensus"] = Get(State, "tempRetirementCensusAfterRead")!.DeepClone();
                State.Remove("tempRetirementCensusAfterRead");
                Save(30);
            }
            return new Server2RollingChild(0, census + Environment.NewLine, "");
        }

        /// <summary>A host case on its manifest (deploy-parent, verify-runner-caches, retire-temp-containers, ...): its exit code.</summary>
        public int HostCase(string hostCase, string manifestPath)
        {
            try { return Case(hostCase, manifestPath); }
            catch (FakeExit e) { return e.Code; }
        }

        private int Case(string hostCase, string manifestPath)
        {
            var request = RollingPs.ParseEnumerated(File.ReadAllText(manifestPath));
            if (!RequestValid(request)) return 2;
            object? R(string name) => RollingPs.Prop(request, name);
            var c1008 = Text(Get(State, "scenario")) == "c1008";
            if (hostCase == "verify-runner-caches" && !RollingPs.In(R("runnerId"), "server2", "server2-temp")) return 2;
            var savedDonor = RollingPs.Has(request, "savedDonor") ? RollingPs.Str(R("savedDonor")) : "";
            var entry = new PlainObject(("kind", "case"), ("name", hostCase), ("runnerId", R("runnerId")), ("sourceSha", R("sourceSha")), ("savedDonor", savedDonor));
            var recycle = R("recycle");
            if (RollingPs.Has(request, "recycle") && recycle != null && RollingPs.Has(recycle, "dryRun"))
                entry["dryRun"] = RollingPs.True(RollingPs.Prop(recycle, "dryRun"));
            if (c1008) { entry["recycle"] = recycle; entry["tempRetiredAt"] = R("tempRetiredAt"); }
            var evidenceRoot = RollingPs.Str(R("evidenceRoot"));
            if (hostCase == "retire-temp-containers")
            {
                var context = R("tempContainerCleanup");
                entry["cleanup"] = context;
                entry["clockMs"] = Ps(Get(State, "clockMs"));
                Write(JsonNode.Parse(RollingPs.Json(entry, 20))!.AsObject());
                var dest = Path.Combine(evidenceRoot, hostCase);
                Directory.CreateDirectory(dest);
                var running = Truthy(Get(State, "tempRunning")) || (Truthy(Get(State, "tempContainer")) && c1008 && !Truthy(Get(State, "tempExited")));
                if (Truthy(Get(State, "cleanupElapsedMs"))) State["clockMs"] = Whole(Num(Get(State, "clockMs")) + Num(Get(State, "cleanupElapsedMs")));
                if (Get(State, "cleanupSequence") is JsonArray cleanupSequence && cleanupSequence.Count > 0)
                {
                    running = Truthy(cleanupSequence[0]);
                    State["cleanupSequence"] = new JsonArray(cleanupSequence.Skip(1).Select(x => x?.DeepClone()).ToArray());
                }
                if (running || Truthy(Get(State, "cleanupFail")))
                {
                    var diagnosis = running ? "TempContainerStillRunning" : "TempContainerRemoveFailed";
                    File.WriteAllText(Path.Combine(dest, "c590-result.json"),
                        RollingPs.JsonIndented(new PlainObject(("accepted", false), ("diagnosis", diagnosis), ("exit", 2))) + Environment.NewLine);
                    Save(30);
                    return 2;
                }
                object[] candidates = Truthy(Get(State, "tempContainer")) ? new object[] { new PlainObject(("Id", new string('1', 64)), ("service", "session-runner")) } : Array.Empty<object>();
                var dryRun = RollingPs.Prop(context, "dryRun");
                var receipt = new PlainObject(("schema", 1), ("sourceSha", R("sourceSha")), ("runId", R("runId")), ("operationId", RollingPs.Prop(context, "operationId")),
                    ("project", RollingPs.Prop(context, "project")), ("projectId", RollingPs.Prop(context, "projectId")), ("retiredAt", RollingPs.Prop(context, "retiredAt")),
                    ("dryRun", dryRun), ("candidates", candidates), ("removals", Array.Empty<object>()), ("finalCensus", Array.Empty<object>()),
                    ("outcome", RollingPs.True(dryRun) ? "preview" : "completed"), ("image", "sha256:" + new string('a', 64)));
                if (Truthy(Get(State, "receiptMismatch"))) receipt["sourceSha"] = new string('b', 40);
                if (!Truthy(Get(State, "receiptMissing")))
                {
                    var receiptPath = Path.Combine(dest, "temp-containers.json");
                    File.WriteAllText(receiptPath, RollingPs.Json(receipt, 30) + Environment.NewLine, new UTF8Encoding(false));
                    File.WriteAllText(Path.Combine(dest, "temp-containers.sha256"), Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(receiptPath))) + Environment.NewLine);
                }
                if (!RollingPs.True(dryRun)) State["tempContainer"] = false;
                if (Truthy(Get(State, "driftAfterCleanup"))) Get(Get(State, "statuses"), "server2-temp")!["sessions"] = 1;
                Save(30);
                if (Truthy(Get(State, "receiptCopyFail"))) return 2;
                return 0;
            }
            Write(JsonNode.Parse(RollingPs.Json(entry, 20))!.AsObject());
            if (hostCase == "retire-temp-runner" && RollingPs.True(RollingPs.Prop(recycle, "dryRun")) && Truthy(Get(State, "failRetirementPreview")))
            {
                var dest = Path.Combine(evidenceRoot, hostCase);
                Directory.CreateDirectory(dest);
                File.WriteAllText(Path.Combine(dest, "c590-result.json"),
                    RollingPs.JsonIndented(new PlainObject(("accepted", false), ("diagnosis", "RecycleGitAuditUnknown"), ("exit", 2))) + Environment.NewLine);
                return 2;
            }
            if (hostCase == "verify-runner-caches" && RollingPs.Eq(Ps(Get(State, "failVerify")), R("runnerId"))) return 1;
            if (c1008 && hostCase == "deploy-parent")
            {
                if (Truthy(Get(State, "failDeploy"))) return 2;
                var main = Get(Get(State, "statuses"), "server2")!;
                main["available"] = true;
                main["dispatchEligible"] = true;
                main["runnerSessions"] = 0;
                main["buildVersion"] = Get(State, "sha")?.DeepClone();
                if (Truthy(Get(State, "wrongDeploySha"))) main["buildVersion"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            }
            if (hostCase == "runner-cache-seed" && Truthy(Get(State, "tempContainer")) && Truthy(Get(State, "tempOffline"))) return 1;
            if (Has(State, "markerPath") && (hostCase is "deploy-parent" or "verify-runner-caches"))
            {
                var code = MarkerReader?.Invoke(hostCase, State) ?? 0;
                Write(new JsonObject { ["kind"] = "marker", ["name"] = hostCase, ["seedImageAvailable"] = Truthy(Get(State, "seedImageAvailable")), ["markerKind"] = "cold", ["exit"] = code });
                if (code != 0) return code;
            }
            if (hostCase == "deploy-temp-runner")
            {
                // Register refuses a retired id. An idle draining runner with retireWhenIdle can also be retired by the service while the
                // new container connects.
                if (!Truthy(Get(State, "tempRetiredAt")) && Truthy(Get(State, "tempDraining")) && Truthy(Get(State, "tempRetireWhenIdle")))
                    State["tempRetiredAt"] = "2026-09-27T10:00:00Z";
                if (Truthy(Get(State, "tempRetiredAt")))
                {
                    Write(new JsonObject { ["kind"] = "registration", ["result"] = "RunnerRetired" });
                    Save(30);
                    return 1;
                }
                if (Has(State, "tempDeployed")) State["tempDeployed"] = true;
                if (Has(State, "tempOffline")) State["tempOffline"] = false;
                if (Has(State, "tempContainer")) State["tempContainer"] = true;
                if (c1008 && Truthy(Get(Get(State, "statuses"), "server2-temp")))
                {
                    var temp = Get(Get(State, "statuses"), "server2-temp")!;
                    temp["buildVersion"] = Get(State, "sha")?.DeepClone();
                    temp["available"] = true;
                    temp["dispatchEligible"] = true;
                    if (temp["runnerSessions"] == null) temp["runnerSessions"] = 0;
                }
            }
            else if (hostCase == "deploy-parent")
            {
                State["oldDeployed"] = true;
                if (Has(State, "seedImageAvailable")) State["seedImageAvailable"] = false;
            }
            else if (!(hostCase is "runner-cache-seed" or "verify-runner-caches" or "retire-temp-runner")) return 2;
            // c1008 keeps the runner slot under statuses. The default depth drops that object.
            Save(c1008 ? 30 : 2);
            return 0;
        }

        /// <summary>
        /// The verify fake's own marker reader for a checkout: bash scripts/fixtures/c973-marker-reader.sh over scripts/c590-remote.sh
        /// with the state's markerPath, seedImageAvailable and markerVariant (through WSL on Windows, the paths converted as the fake's
        /// wslpath did); its exit code. Its output is written to output when it fails, as the fake wrote $result.
        /// </summary>
        public static Func<string, JsonObject, int> CheckoutMarkerReader(string checkout, TextWriter output) => (hostCase, state) =>
        {
            var reader = hostCase == "deploy-parent" ? "case_deploy_parent" : "case_verify_runner_caches";
            var paths = new[]
            {
                Path.Combine(checkout, "scripts", "fixtures", "c973-marker-reader.sh"), Path.Combine(checkout, "scripts", "c590-remote.sh"),
                RollingPs.Str(Ps(Get(state, "markerPath"))),
            };
            var windows = OperatingSystem.IsWindows();
            var start = new ProcessStartInfo(windows ? "wsl" : "bash") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = false };
            if (windows)
            {
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add("bash");
                paths = paths.Select(LinuxPath).ToArray();
            }
            foreach (var argument in new[] { paths[0], paths[1], reader, paths[2], Truthy(Get(state, "seedImageAvailable")) ? "1" : "0", "1", Text(Get(state, "markerVariant")) })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0 && text.Length > 0) output.Write(text);
            return process.ExitCode;
        };

        /// <summary>A case manifest's request fields as a call (sourceSha, runId, selectedPhase, phase, mode, evidenceRoot, operationId).</summary>
        public static Server2RollingCall CallOf(object? request) => new Server2RollingCall
        {
            Sha = RollingPs.Str(RollingPs.Prop(request, "sourceSha")), RunId = RollingPs.Str(RollingPs.Prop(request, "runId")),
            SelectedPhase = RollingPs.Str(RollingPs.Prop(request, "selectedPhase")), Phase = RollingPs.Str(RollingPs.Prop(request, "phase")),
            Mode = RollingPs.Str(RollingPs.Prop(request, "mode")), EvidenceRoot = RollingPs.Str(RollingPs.Prop(request, "evidenceRoot")),
            Operation = RollingPs.Str(RollingPs.Prop(request, "operationId")),
        };

        private static string LinuxPath(string path)
        {
            var start = new ProcessStartInfo("wsl") { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (var argument in new[] { "-e", "wslpath", "-u", path }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var text = process.StandardOutput.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException("C973 WSL fixture path conversion failed");
            return text.Trim();
        }
    }

    /// <summary>The c727-fake-verify verb's arguments: --case and, for every case but rollout-lock and recycle-discover, --manifest.</summary>
    public sealed class C727FakeVerifyOptions
    {
        /// <summary>The CLI's usage line.</summary>
        public const string Usage = "c727-fake-verify --case <case> [--manifest <path>]";

        /// <summary>The host case.</summary>
        public string Case { get; init; } = "";

        /// <summary>The case's manifest (null: none).</summary>
        public string? Manifest { get; init; }

        /// <summary>The arguments after the verb.</summary>
        public static C727FakeVerifyOptions Parse(IReadOnlyList<string> arguments)
        {
            var flags = NightlyArgs.Parse(arguments, new[] { "--case", "--manifest" }, Array.Empty<string>());
            return new C727FakeVerifyOptions { Case = flags.Required("--case"), Manifest = flags.Value("--manifest") };
        }
    }

    /// <summary>
    /// The c727-fake-verify verb (unit 68): one case of the verifier fake (scripts/fixtures/c727-fake-verify.ps1 until unit 68) on
    /// C727_TEST_STATE and C727_TEST_TRACE, for a W10 node harness adapter's cases it does not answer itself, as the adapter's
    /// fall-through to the PowerShell fake was. The case's stdout and stderr and its exit code; rollout-lock prints C1008_LOCKED, holds
    /// the state's rolloutLockFile until a line on the input and exits 0. A case that throws exits 1 with its message on stderr.
    /// </summary>
    public static class C727FakeVerify
    {
        /// <summary>Runs one case.</summary>
        public static int Run(C727FakeVerifyOptions options, Func<string, string> variables, string checkout, TextReader input, TextWriter output, TextWriter error)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (variables == null) throw new ArgumentNullException(nameof(variables));
            if (input == null || output == null || error == null) throw new ArgumentNullException(nameof(output));
            var stub = new Server2RollingStubOs(name => name == "C727_TEST_VERIFY_STUB" ? Server2RollingStubOs.FakeVerify : name == "C727_TEST_HTTP_STUB" ? null : variables(name),
                new NoWorld(), checkout);
            try
            {
                Server2RollingCall Call()
                {
                    if (string.IsNullOrEmpty(options.Manifest)) throw new InvalidOperationException("the " + options.Case + " case needs --manifest");
                    return C727Fakes.CallOf(RollingPs.ParseEnumerated(File.ReadAllText(options.Manifest)));
                }
                Server2RollingChild child;
                switch (options.Case)
                {
                    case "rollout-lock":
                        var held = stub.EnterRolloutLock();
                        output.WriteLine("C1008_LOCKED");
                        output.Flush();
                        input.ReadLine();
                        held.Release();
                        return 0;
                    case "recycle-discover": child = stub.RecycleDiscover(new Server2RollingCall()); break;
                    case "host-jq-prerequisite": child = stub.FakeOnly(f => f.HostJq(Call())); break;
                    case "temp-project-absent": child = stub.FakeOnly(f => f.TempProjectCensus(Call())); break;
                    case "retirement-proof-read": child = stub.FakeOnly(f => f.ProofRead(Call())); break;
                    case "temp-retirement-clearable": child = stub.FakeOnly(f => f.ClearableCensus(Call())); break;
                    default:
                        if (string.IsNullOrEmpty(options.Manifest)) throw new InvalidOperationException("the " + options.Case + " case needs --manifest");
                        return stub.HostCase(options.Case, options.Manifest, output, error);
                }
                // The fake printed each answer as a line.
                if (child.Stdout.Length > 0) output.Write(child.Stdout.EndsWith("\n", StringComparison.Ordinal) ? child.Stdout : child.Stdout + Environment.NewLine);
                if (child.Stderr.Length > 0) error.WriteLine(child.Stderr.TrimEnd('\n'));
                return child.Exit;
            }
            catch (Server2RollingRefusal e)
            {
                error.WriteLine(e.Message);
                return 1;
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException || e is System.Text.Json.JsonException)
            {
                error.WriteLine(e.Message);
                return 1;
            }
        }

        // The verb answers every case from the fake; nothing reaches a production world.
        private sealed class NoWorld : IServer2RollingWorld
        {
            private static Exception None() => new InvalidOperationException("c727-fake-verify reaches no production world");

            public Server2RollingChild HostJq(Server2RollingCall call) => throw None();

            public IServer2RollingLock EnterRolloutLock() => throw None();

            public Server2RollingHttpAnswer RunnerHttp(string method, string url, IReadOnlyDictionary<string, string> headers, string body) => throw None();

            public Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers) => throw None();

            public Server2RollingChild RecycleDiscover(Server2RollingCall call) => throw None();

            public Server2RollingChild TempRetirementRead(Server2RollingCall call) => throw None();

            public Server2RollingChild TempProjectCensus(Server2RollingCall call) => throw None();

            public int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error) => throw None();

            public Server2RollingChild Git(string repo, IReadOnlyList<string> arguments) => throw None();
        }
    }
}
