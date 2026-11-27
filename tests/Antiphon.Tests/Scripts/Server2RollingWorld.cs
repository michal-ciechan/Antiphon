using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.Application;
using Antiphon.Tests.Scripts.Nightly;
using Microsoft.Extensions.Time.Testing;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Unit 65 of docs/superpowers/plans/2026-11-22-remaining-ps1-to-csharp.md: the deploy-server2 command (Antiphon.Nightly's
/// Server2RollingDeploy, which replaced scripts/deploy-server2.ps1) in process. This world is the C# port of the script's two
/// offline fakes, scripts/fixtures/c727-fake-http.ps1 (the runner and census HTTP) and c727-fake-verify.ps1 (the host cases, the
/// host jq prerequisite, the rollout lock, the recycle discovery and the two temp censuses), on the same state shapes: the generic
/// CARD-0727 scenario flags (tempDeployed, oldDraining, faultRunner, ...) and the CARD-1008 statuses/tasks shape. Each answer is
/// what the PowerShell fake printed (its values read and written back as ConvertFrom-Json and ConvertTo-Json did, a save at the
/// fake's depth), and each call appends the fake's trace row (also to TracePath). The checkout is a temporary directory (the
/// evidence root is under it), the clock a fake one that Sleep advances, and the timings the drivers' C727_TEST_* values (100 ms
/// waits, 5 ms polls). Nothing is started and nothing leaves the process.
/// </summary>
internal sealed class Server2RollingWorld : IServer2RollingWorld, IDisposable
{
    public const string Sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string Token = "SENTINEL_C727_OPERATOR_TOKEN_1234567890";

    private readonly List<JsonObject> _trace = [];
    private readonly bool _ownsRoot;

    public Server2RollingWorld(JsonObject? state = null, string? root = null)
    {
        State = state ?? new JsonObject();
        _ownsRoot = root == null;
        Root = root ?? Directory.CreateTempSubdirectory("c727-world-").FullName;
        TokenPath = Path.Combine(Root, "operator-token");
        File.WriteAllText(TokenPath, Token);
        Variables["ANTIPHON_API"] = "http://127.0.0.1:9";
        Variables["ANTIPHON_OPERATOR_TOKEN_FILE"] = TokenPath;
    }

    /// <summary>The driver's generic state for a scenario (Run-C727), with the phase's neighbours.</summary>
    public static JsonObject DriverState(string scenario, string phase = "all", JsonObject? set = null)
    {
        var state = new JsonObject
        {
            ["scenario"] = scenario, ["sha"] = Sha, ["clockMs"] = 0, ["tempDeployed"] = false, ["oldDeployed"] = false,
            ["oldDraining"] = false, ["tempDraining"] = false, ["tempRetiredAt"] = null, ["tempContainer"] = false, ["tempOffline"] = false,
            ["tempRedirectTo"] = "server2", ["tempRetireWhenIdle"] = true,
            ["faultRunner"] = "", ["faultField"] = "", ["faultKind"] = "", ["faultValue"] = null, ["failVerify"] = "",
        };
        set ??= new JsonObject();
        foreach (var (key, value) in set) state[key] = value?.DeepClone();
        // A normal rolling main replacement begins only after temp admission.
        if (phase == "redeploy-old")
        {
            if (!set.ContainsKey("tempContainer")) state["tempContainer"] = true;
            if (!set.ContainsKey("tempDeployed")) state["tempDeployed"] = true;
        }
        if ((Truthy(state["tempDeployed"]) || phase == "retire-temp") && !set.ContainsKey("tempContainer")) state["tempContainer"] = true;
        return state;
    }

    /// <summary>The fake's state (the PowerShell fake's C727_TEST_STATE file).</summary>
    public JsonObject State { get; set; }

    /// <summary>The checkout the command runs in (its .antiphon/rolling-server2 holds the evidence).</summary>
    public string Root { get; }

    public string TokenPath { get; }

    /// <summary>The trace file (C727_TEST_TRACE); null: kept in memory only.</summary>
    public string? TracePath { get; set; }

    /// <summary>The host's environment.</summary>
    public Dictionary<string, string?> Variables { get; } = new(StringComparer.Ordinal);

    /// <summary>The drivers' timings: C727_TEST_WAIT_MS=100, C727_TEST_POLL_MS=5, C727_TEST_LAND_WAIT_MS=100, the HTTP stub set.</summary>
    public Server2RollingTiming Timing { get; set; } = new() { WaitMs = 100, PollMs = 5, LandWaitMs = 100, StubbedReads = true };

    /// <summary>The installed jq path the proof names.</summary>
    public string HostJqDestination { get; set; } = "/usr/local/bin/jq";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 26, 3, 0, 0, TimeSpan.Zero));

    /// <summary>Runs before each host case with its name and manifest path (the observer stubs the drivers wrapped around the fake).</summary>
    public Action<string, string>? OnCase { get; set; }

    /// <summary>Sees each census read's full URL.</summary>
    public Action<string>? UrlObserver { get; set; }

    /// <summary>Answers a host case instead of the fake (its exit), null: the fake answers.</summary>
    public Func<string, string, int?>? CaseOverride { get; set; }

    /// <summary>Answers a census read before the fake does (a test's own HTTP schedule); null: the fake answers.</summary>
    public Func<string, Server2RollingHttpAnswer?>? ReadOverride { get; set; }

    /// <summary>The real transport for host jq and git (the Linux fixtures' fake ssh on PATH); null: the fake answers.</summary>
    public IServer2RollingWorld? Transport { get; set; }

    /// <summary>What the verifier children printed (the host cases' stdout) for the command's output.</summary>
    public string CaseOutput { get; set; } = "";

    /// <summary>Empties the trace (rows and file).</summary>
    public void ClearTrace()
    {
        _trace.Clear();
        if (TracePath != null && File.Exists(TracePath)) File.Delete(TracePath);
    }

    /// <summary>The trace rows of this world, in order.</summary>
    public JsonObject[] Trace => _trace.ToArray();

    private int _guid;

    /// <summary>The ids the command draws, in order (deterministic: 1, 2, 3 ... as 32 hex digits); null: random.</summary>
    public bool DeterministicIds { get; set; }

    private NightlyHost Host(StringWriter output, StringWriter error, Action<TimeSpan>? onSleep = null) => new()
    {
        Clock = Clock,
        Git = (_, _) => throw new InvalidOperationException("deploy-server2 runs git through its world"),
        Launcher = new RecordingLauncher(_ => throw new InvalidOperationException("deploy-server2 starts nothing on the launcher")),
        Processes = new NightlyProcessTable(),
        Probe = new NightlyHostProbe(),
        Out = output,
        Error = error,
        Trace = TextWriter.Null,
        Variables = name => Variables.TryGetValue(name, out var value) ? value : null,
        Sleep = wait => { onSleep?.Invoke(wait); Clock.Advance(wait); },
    };

    private Server2RollingSeams Seams() => new()
    {
        World = this,
        Timing = Timing,
        HostJqDestination = HostJqDestination,
        C994Now = () => Has(State, "clockMs") ? DateTime.UnixEpoch.AddMilliseconds(Num(Get(State, "clockMs"))) : Clock.GetUtcNow().UtcDateTime,
        C994Advance = ms => { if (Has(State, "clockMs")) { State["clockMs"] = Whole(Num(Get(State, "clockMs")) + ms); Save(30); } },
        NewGuid = () => DeterministicIds ? new Guid(Convert.ToHexString(BitConverter.GetBytes(++_guid)).PadLeft(32, '0')) : Guid.NewGuid(),
    };

    public Server2RollingRun Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit;
        try
        {
            exit = Server2RollingDeploy.Run(Server2RollingOptions.Parse(arguments, Root), Host(output, error), new System.IO.Abstractions.FileSystem(), Seams());
        }
        catch (NightlyUsageException e)
        {
            error.WriteLine(e.Message);
            exit = 2;
        }
        return new Server2RollingRun(exit, output.ToString(), error.ToString(), Trace);
    }

    /// <summary>One runner request on this world as a phase makes it (Server2RollingDeploy.ProbeRunnerRequest).</summary>
    public object ProbeRequest(string method, string runnerId, string suffix, PlainObject body, bool holdLift) =>
        Server2RollingDeploy.ProbeRunnerRequest(method, runnerId, suffix, body, holdLift, Sha, Root, Host(new StringWriter(), new StringWriter()),
            new System.IO.Abstractions.FileSystem(), Seams());

    /// <summary>The census alone on this world (the census probes); onSleep sees each wait before the clock advances.</summary>
    public Server2RollingCensusResult Probe(Server2RollingCensusProbe probe, Action<TimeSpan>? onSleep = null) =>
        Server2RollingDeploy.ProbeCensus(probe, Host(new StringWriter(), new StringWriter(), onSleep), new System.IO.Abstractions.FileSystem(), Seams());
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
    private void Save(int depth) => State = JsonNode.Parse(RollingPs.Json(Ps(State), depth))!.AsObject();

    private void Write(JsonObject row)
    {
        _trace.Add(row);
        if (TracePath != null) File.AppendAllText(TracePath, row.ToJsonString() + Environment.NewLine);
    }

    private static bool Has(JsonNode? node, string name, bool exact = false) =>
        node is JsonObject o && o.Any(p => string.Equals(p.Key, name, exact ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));

    private static JsonNode? Get(JsonNode? node, string name) =>
        node is JsonObject o ? o.FirstOrDefault(p => string.Equals(p.Key, name, StringComparison.OrdinalIgnoreCase)).Value : null;

    private static Server2RollingHttpAnswer Ok(string body) => new(200, body);

    private sealed class FakeExit(int code) : Exception("fake exit " + code)
    {
        public int Code { get; } = code;
    }

    // ---- c727-fake-http.ps1 ----

    public Server2RollingHttpAnswer RunnerHttp(string method, string url, IReadOnlyDictionary<string, string> headers, string body)
    {
        var path = new Uri(url).AbsolutePath;
        var rest = path.Substring("/api/session-runners/".Length);
        var slash = rest.IndexOf('/');
        var runnerId = Uri.UnescapeDataString(slash < 0 ? rest : rest.Substring(0, slash));
        var suffix = slash < 0 ? "" : rest.Substring(slash);
        UrlObserver?.Invoke(url);
        var bodyNode = string.IsNullOrEmpty(body) ? null : JsonNode.Parse(body);
        Write(new JsonObject { ["kind"] = "http", ["method"] = method, ["runnerId"] = runnerId, ["suffix"] = suffix, ["path"] = "", ["body"] = bodyNode?.DeepClone() });
        var text = Runner(method, runnerId, suffix, bodyNode);
        if (text == null) throw new HttpRequestException("RunnerApiUnavailable fixture");
        if (text == "__404__") return new Server2RollingHttpAnswer(404, "");
        if (text == "__503__") return new Server2RollingHttpAnswer(503, "");
        return Ok(text);
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

    public Server2RollingHttpAnswer ReadHttp(string path, string url, IReadOnlyDictionary<string, string> headers)
    {
        Write(new JsonObject { ["kind"] = "http", ["method"] = "GET", ["runnerId"] = "", ["suffix"] = "", ["path"] = path, ["body"] = null });
        UrlObserver?.Invoke(url);
        var overridden = ReadOverride?.Invoke(path);
        if (overridden != null) return overridden;
        string? text;
        try { text = Read(path); }
        catch (FakeExit) { throw new InvalidOperationException("fixture transport"); }
        if (text == null) throw new InvalidOperationException("fixture transport");
        if (text == "__TIMEOUT__") throw new TimeoutException("fixture timeout");
        var status = Regex.Match(text, "^__(\\d{3})__$");
        if (status.Success) return new Server2RollingHttpAnswer(int.Parse(status.Groups[1].Value), "");
        return Ok(text);
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

    private sealed class FakeLock(FileStream? file) : IServer2RollingLock
    {
        public void Release() => file?.Dispose();
    }

    public IServer2RollingLock EnterRolloutLock()
    {
        Write(new JsonObject { ["kind"] = "lock-request" });
        FileStream? file = null;
        if (Truthy(Get(State, "rolloutLockFile")))
        {
            var lockPath = Text(Get(State, "rolloutLockFile"));
            while (file == null)
            {
                try { file = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { Thread.Sleep(20); }
            }
        }
        return new FakeLock(file);
    }

    public Server2RollingChild RecycleDiscover(Server2RollingCall call)
    {
        var stdout = Truthy(Get(State, "incompleteRecycle")) ? "c100800000000000000000000000000000001" + Environment.NewLine : "";
        if (Truthy(Get(State, "discoveryError"))) return new Server2RollingChild(2, stdout, "");
        return new Server2RollingChild(0, stdout, "");
    }

    // The manifest check every case after the lock and the discovery made: this SHA and a c727 run id.
    private bool RequestValid(object? request) =>
        RollingPs.Ceq(RollingPs.Prop(request, "sourceSha"), Ps(Get(State, "sha"))) && RollingPs.Match(RollingPs.Prop(request, "runId"), "^c727[0-9a-f]{12}$");

    public Server2RollingChild HostJq(Server2RollingCall call)
    {
        if (Transport != null) return Transport.HostJq(call);
        // The script wrote the request to host-jq-<phase>.manifest.json for the fake; this world reads the call instead.
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

    public Server2RollingChild TempProjectCensus(Server2RollingCall call)
    {
        if (!RequestValid(new PlainObject(("sourceSha", call.Sha), ("runId", call.RunId)))) return new Server2RollingChild(2, "", "");
        Write(new JsonObject { ["kind"] = "census", ["name"] = "temp-project-absent" });
        if (Text(Get(State, "scenario")) == "census-failed" || Truthy(Get(State, "censusError"))) return new Server2RollingChild(2, "", "");
        return new Server2RollingChild(0, Truthy(Get(State, "tempContainer")) ? "fixture-temp-container" + Environment.NewLine : "", "");
    }

    public Server2RollingChild TempRetirementRead(Server2RollingCall call)
    {
        if (!RequestValid(new PlainObject(("sourceSha", call.Sha), ("runId", call.RunId)))) return new Server2RollingChild(2, "", "");
        if (!string.IsNullOrEmpty(call.Operation))
        {
            Write(new JsonObject { ["kind"] = "proof-read" });
            if (Truthy(Get(State, "retirementProofError"))) return new Server2RollingChild(2, "", "");
            var proof = new PlainObject(("operationId", call.Operation), ("digest", new string('a', 64)), ("state", "Ready"), ("mode", "scripted"));
            return new Server2RollingChild(0, RollingPs.Json(proof) + Environment.NewLine, "");
        }
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

    public int HostCase(string hostCase, string manifestPath, TextWriter output, TextWriter error)
    {
        OnCase?.Invoke(hostCase, manifestPath);
        if (CaseOutput.Length > 0) output.Write(CaseOutput);
        if (CaseOverride?.Invoke(hostCase, manifestPath) is int overridden) return overridden;
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
            object[] candidates = Truthy(Get(State, "tempContainer")) ? [new PlainObject(("Id", new string('1', 64)), ("service", "session-runner"))] : [];
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

    /// <summary>The cold marker reader (T-20: c973-marker-reader.sh through bash or WSL); null: it passes.</summary>
    public Func<string, JsonObject, int>? MarkerReader { get; set; }

    public Server2RollingChild Git(string repo, IReadOnlyList<string> arguments)
    {
        if (Transport != null) return Transport.Git(repo, arguments);
        if (GitAnswer != null) return GitAnswer(arguments);
        return new Server2RollingChild(0, "", "");
    }

    /// <summary>git's answer (the capture's git.cmd: STUB_GIT_HEAD and STUB_GIT_EXIT); null: no output, exit 0.</summary>
    public Func<IReadOnlyList<string>, Server2RollingChild>? GitAnswer { get; set; }

    /// <summary>The evidence directories this world's runs wrote.</summary>
    public string EvidenceRoot => Path.Combine(Root, ".antiphon", "rolling-server2");

    public void Dispose()
    {
        if (_ownsRoot && Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }
}

/// <summary>One in-process run: the exit code, both streams and the fake trace.</summary>
internal sealed record Server2RollingRun(int Exit, string Stdout, string Stderr, JsonObject[] Trace)
{
    /// <summary>stdout then stderr, as the drivers read a child's two streams.</summary>
    public string Output => Stdout + Stderr;

    public JsonObject[] Cases => Trace.Where(t => t["kind"]?.GetValue<string>() == "case").ToArray();

    public JsonObject[] Posts => Trace.Where(t => t["kind"]?.GetValue<string>() == "http" && t["method"]?.GetValue<string>() == "POST").ToArray();

    public bool HasCase(string name) => Cases.Any(c => c["name"]?.GetValue<string>() == name);

    public int IndexOf(JsonObject row) => Array.IndexOf(Trace, row);

    /// <summary>The run id (c727 and 12 hex) the output names, '' without one.</summary>
    public string RunId => Regex.Match(Output, "c727[0-9a-f]{12}").Value;

    public override string ToString() => "exit=" + Exit + "\n" + Stdout + "\n--- stderr\n" + Stderr;
}
