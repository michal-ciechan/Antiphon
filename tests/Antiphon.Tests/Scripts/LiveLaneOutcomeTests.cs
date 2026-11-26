using System.Text.Json.Nodes;
using Antiphon.LiveLane;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1545 synthetic, offline outcome contracts: no provider executable or network operation. W2 slice S21: the cases of
/// the deleted scripts/test-live-lane-outcome.ps1 run in process against Antiphon.LiveLane (LiveLaneOutcome through
/// LiveLaneRun) with fixture adapters, the real shared native-run lock under a scratch coordination root, and a virtual
/// lane clock the probe adapter advances.
/// </summary>
[Category("Integration")]
public sealed class LiveLaneOutcomeTests
{
    private static JsonObject Row(string uid = "fixture.A.0", string state = "Passed") => new()
    {
        ["Uid"] = uid, ["State"] = state, ["Setup"] = "Ready", ["Healthy"] = true, ["Deterministic"] = true,
        ["Account"] = "account-A", ["Endpoint"] = "endpoint-A", ["Dependency"] = "provider-A",
        ["Session"] = "session-A", ["Request"] = "request-A", ["Prompt"] = "fixture whole\nprompt body", ["Transcript"] = new JsonArray()
    };

    private static void Outage(JsonObject row)
    {
        row["State"] = "Failed";
        row["Transcript"] = LiveLaneJsonFixtures.Array(
            new JsonObject { ["Type"] = "UserPrompt", ["Body"] = row["Prompt"]!.DeepClone(), ["Session"] = row["Session"]!.DeepClone(), ["Request"] = row["Request"]!.DeepClone(), ["Sequence"] = 1 },
            new JsonObject { ["Type"] = "ApiError", ["Error"] = "ServiceUnavailable", ["Session"] = row["Session"]!.DeepClone(), ["Request"] = row["Request"]!.DeepClone(), ["Sequence"] = 2 });
    }

    private sealed class Fixture
    {
        public Fixture(C1545Scratch scratch)
        {
            Root = scratch.NewDirectory();
            Request = new LiveLaneRequest
            {
                Mode = "Fixture", Lane = "fixture-live", RunId = "run-A", Attempt = 1, Source = new string('a', 40), Uids = ["fixture.A.0"],
                Executable = "owned-fixture-executable", Profile = "owned-fixture-profile", Commission = "fixture-commission", BudgetSeconds = 120,
                Inherited = new Dictionary<string, string>(), CoordinationRoot = Path.Combine(Root, "coord"), ResultsRoot = Path.Combine(Root, "diagnostic"),
                Account = "account-A", Endpoint = "endpoint-A", Dependency = "provider-A"
            };
            Terminal = new JsonObject
            {
                ["SchemaVersion"] = 1, ["RunId"] = "checkpoint-A", ["Lane"] = "fixture-live", ["Attempt"] = 1, ["Source"] = new string('a', 40),
                ["Build"] = "Succeeded", ["Platform"] = "Fixture", ["Authority"] = "diagnostic", ["Complete"] = true, ["Evidence"] = "Complete",
                ["InputBefore"] = "input-A", ["InputAfter"] = "input-A", ["ExecutableBefore"] = "exe-A", ["ExecutableAfter"] = "exe-A",
                ["Outcome"] = "Passed", ["Rows"] = LiveLaneJsonFixtures.Array(Row())
            };
            Boundary = new LiveLaneBoundary
            {
                Candidate = r => new LiveLaneCandidate { Sha = r.Source, Clean = true },
                Clock = () => Tick,
                Slot = _ => { Slots++; return true; },
                WriteContext = (path, json) => { File.WriteAllText(path, json); return path; },
                Start = (_, _, _) =>
                {
                    Starts++;
                    Prompts++;
                    Trace.Add("start");
                    return new LiveLaneChild { RunId = ChildRunId, Children = ["owned-child"] };
                },
                Wait = id =>
                {
                    Waits++;
                    Trace.Add("wait:" + id);
                    return Waits == 1 ? 75 : Raw;
                },
                ReadTerminal = _ => { Trace.Add("terminal"); return Terminal; },
                Stop = _ => Trace.Add("stop"),
                Join = (_, _) => { Trace.Add("join"); return true; },
                WriteReceipt = (path, result) =>
                {
                    Trace.Add("receipt");
                    if (ReceiptFault == "missing") return;
                    File.WriteAllText(path, result.ToJson());
                    if (ReceiptFault == "partial") throw new InvalidOperationException("synthetic interrupted receipt publication");
                },
                Probe = (purpose, spec) =>
                {
                    ProbeRequests.Add(spec);
                    ProbePrompts += spec.PromptCount;
                    if (spec.Operation != "Availability") throw new InvalidOperationException("forbidden model call");
                    Trace.Add("probe:" + purpose);
                    var status = PostStatus;
                    var delay = PostDelay;
                    if (purpose == "Preflight")
                    {
                        Pre++;
                        status = PreStatus;
                        delay = PreDelay;
                    }
                    else
                    {
                        Post++;
                        if (status == "Throws") throw new InvalidOperationException("synthetic transport failure");
                        if (Post > 1) status = "Unavailable";
                    }
                    // The adapter honours the supplied absolute deadline, in virtual time.
                    var available = Math.Max(0, spec.Deadline - Tick);
                    Tick += Math.Min(delay, available);
                    if (delay > available) status = "Timeout";
                    var reply = new LiveLaneProbeReply { Status = status, Error = "ServiceUnavailable", Account = "account-A", Endpoint = "endpoint-A", Dependency = "provider-A" };
                    if (Foreign == "Account") reply.Account = "foreign";
                    if (Foreign == "Endpoint") reply.Endpoint = "foreign";
                    if (Foreign == "Dependency") reply.Dependency = "foreign";
                    return reply;
                }
            };
        }

        public string Root { get; }
        public double Tick { get; set; }
        public int Starts { get; private set; }
        public int Prompts { get; private set; }
        public int Slots { get; private set; }
        public int Waits { get; private set; }
        public int Pre { get; private set; }
        public int Post { get; private set; }
        public int ProbePrompts { get; private set; }
        public string PreStatus { get; set; } = "Ready";
        public double PreDelay { get; set; }
        public string PostStatus { get; set; } = "Unknown";
        public double PostDelay { get; set; }
        public string Foreign { get; set; } = "";
        public int Raw { get; set; }
        public string ReceiptFault { get; set; } = "";
        public string ChildRunId { get; set; } = "checkpoint-A";
        public JsonObject? Terminal { get; set; }
        public JsonObject Row0 => Terminal!["Rows"]![0]!.AsObject();
        public List<string> Trace { get; } = [];
        public List<LiveLaneProbeRequest> ProbeRequests { get; } = [];
        public LiveLaneRequest Request { get; }
        public LiveLaneBoundary Boundary { get; }
        public LiveLaneNativeLockSeam Lock { get; } = new();

        public LiveLaneRunResult Run() => LiveLaneRun.Run(Request, Boundary, Lock);

        public JsonNode Receipt() => JsonNode.Parse(File.ReadAllText(Path.Combine(Request.ResultsRoot, "diagnostic.json")))!;
    }

    private static bool Is(LiveLaneRunResult r, int code, string reason = "") =>
        r.ExitCode == code && (reason.Length == 0 || r.Reason == reason) && r.DiagnosticOnly && !r.GateCredit;

    [Test]
    public void ProviderOutcomeIsDistinct()
    {
        var cases = new C1545Cases(nameof(ProviderOutcomeIsDistinct));
        using var scratch = new C1545Scratch("c1545-outcome-");
        var q = new Fixture(scratch);
        var r = q.Run();
        var saved = q.Receipt();
        cases.Check("green", Is(r, 0, "all-passed") && r.Classification == "GREEN" && r.TerminalComplete && LiveLaneValues.Text(saved["ExitCode"]) == "0"
            && LiveLaneValues.Text(saved["DiagnosticOnly"]) == "True" && LiveLaneValues.Text(saved["GateCredit"]) == "False"
            && q.Trace.IndexOf("join") < q.Trace.IndexOf("receipt"));
        var ok = true;
        foreach (var raw in new[] { 2, 3, 8 })
        {
            q = new Fixture(scratch) { Raw = raw };
            q.Terminal!["Build"] = "Failed";
            q.Terminal["Outcome"] = "Failed";
            r = q.Run();
            ok = ok && Is(r, 2) && r.RawChildExit == raw && r.RawChildOutcome == "Failed" && q.Waits == 2;
        }
        cases.Check("raw-child", ok);
        foreach (var raw in new[] { 1, 2, 3, 8 })
        {
            q = new Fixture(scratch) { Raw = raw };
            r = q.Run();
            cases.Check("raw-passing-" + raw, Is(r, 2, "checkpoint-inconsistent") && r.RawChildExit == raw && r.RawChildOutcome == "Passed"
                && r.Rows[0].ExitCode == 0 && q.Waits == 2);
        }
        var idNumber = 0;
        foreach (var id in new[] { "../foreign", "foreign/child", "foreign\\child", "", new string('a', 129), "checkpoint-A\n", ".", ".." })
        {
            q = new Fixture(scratch) { ChildRunId = id };
            r = q.Run();
            cases.Check("run-id-invalid-" + ++idNumber, Is(r, 2, "checkpoint-run-invalid") && r.CheckpointRunId == null && q.Waits == 0
                && q.Trace.Contains("stop") && q.Trace.Contains("join"));
        }
        idNumber = 0;
        foreach (var id in new[] { "checkpoint-A", new string('a', 128) })
        {
            q = new Fixture(scratch) { ChildRunId = id };
            q.Terminal!["RunId"] = id;
            r = q.Run();
            cases.Check("run-id-valid-" + ++idNumber, Is(r, 0) && r.CheckpointRunId == id);
        }
        ok = true;
        foreach (var variant in new[] { "missing", "unreadable", "incomplete", "identity", "schema", "receipt-partial", "receipt-missing" })
        {
            q = new Fixture(scratch);
            switch (variant)
            {
                case "missing": q.Terminal = null; break;
                case "unreadable": q.Terminal!["Evidence"] = "Unreadable"; break;
                case "incomplete": q.Terminal!["Complete"] = false; break;
                case "identity": q.Terminal!["RunId"] = "foreign"; break;
                case "schema": q.Terminal!["SchemaVersion"] = 42; break;
                case "receipt-partial": q.ReceiptFault = "partial"; break;
                case "receipt-missing": q.ReceiptFault = "missing"; break;
            }
            r = q.Run();
            var expected = variant is "identity" or "schema" ? 2 : 20;
            ok = ok && Is(r, expected) && q.Starts == 1;
            if (variant.StartsWith("receipt-", StringComparison.Ordinal))
                ok = ok && !r.TerminalComplete && !File.Exists(Path.Combine(q.Request.ResultsRoot, "diagnostic.json"))
                    && !File.Exists(Path.Combine(q.Request.ResultsRoot, "diagnostic.pending.json"));
        }
        cases.Check("evidence", ok);
        ok = true;
        foreach (var kind in new[] { "InputAfter", "ExecutableAfter" })
        {
            q = new Fixture(scratch);
            q.Terminal![kind] = "drift";
            r = q.Run();
            ok = ok && Is(r, 20, "input-drift");
        }
        cases.Check("drift", ok);
        q = new Fixture(scratch) { PreStatus = "Unavailable", PreDelay = 20 };
        r = q.Run();
        cases.Check("pre-retry", Is(r, 20, "provider-outage") && q.Pre == 2 && q.Tick <= 60
            && q.ProbeRequests[1].Deadline - q.ProbeRequests[0].Deadline <= 30 && q.Starts == 0);
        q = new Fixture(scratch) { PreStatus = "QuotaExceeded" };
        r = q.Run();
        cases.Check("quota-retry", Is(r, 20, "quota-exhausted") && q.Pre == 1 && q.Starts == 0 && q.Prompts == 0);
        q = new Fixture(scratch) { Raw = 3 };
        Outage(q.Row0);
        r = q.Run();
        cases.Check("post-submit", Is(r, 20, "provider-outage") && q.Pre == 1 && q.Starts == 1 && q.Prompts == 1 && r.RawChildExit == 3);
        cases.Complete("green", "raw-child", "raw-passing-1", "raw-passing-2", "raw-passing-3", "raw-passing-8",
            "run-id-invalid-1", "run-id-invalid-2", "run-id-invalid-3", "run-id-invalid-4", "run-id-invalid-5", "run-id-invalid-6", "run-id-invalid-7",
            "run-id-invalid-8", "run-id-valid-1", "run-id-valid-2", "evidence", "drift", "pre-retry", "quota-retry", "post-submit");
    }

    [Test]
    public void MidRunOutageAfterReceiptIsInconclusive()
    {
        var cases = new C1545Cases(nameof(MidRunOutageAfterReceiptIsInconclusive));
        using var scratch = new C1545Scratch("c1545-outcome-");
        var ok = true;
        Fixture q;
        LiveLaneRunResult r;
        foreach (var type in new[] { "Overloaded", "ServiceUnavailable", "RateLimited" })
        {
            q = new Fixture(scratch);
            Outage(q.Row0);
            q.Row0["Transcript"]![1]!["Error"] = type;
            r = q.Run();
            ok = ok && Is(r, 20, "provider-outage") && q.Pre == 1 && q.Starts == 1 && r.Rows[0].Reason == "provider-outage";
            var savedRow = q.Receipt()["Rows"]![0]!;
            ok = ok && LiveLaneValues.Text(savedRow["ProviderError"]) == type && LiveLaneValues.Text(savedRow["Phase"]) == "PostSubmission"
                && LiveLaneValues.Text(savedRow["OutageCorrelated"]) == "True";
        }
        // A passing summary cannot hide a correlated typed outage in its own turn.
        q = new Fixture(scratch);
        Outage(q.Row0);
        q.Row0["State"] = "Passed";
        r = q.Run();
        ok = ok && Is(r, 20, "provider-outage") && r.RawChildExit == 0;
        cases.Check("transcript-positive", ok);
        foreach (var variant in new[] { "prompt-partial", "session-foreign", "request-foreign", "error-before-prompt", "untyped-error" })
        {
            q = new Fixture(scratch);
            Outage(q.Row0);
            var t = q.Row0["Transcript"]!.AsArray();
            switch (variant)
            {
                case "prompt-partial": t[0]!["Body"] = "fixture whole"; break;
                case "session-foreign": t[0]!["Session"] = "foreign"; break;
                case "request-foreign": t[1]!["Request"] = "foreign"; break;
                case "error-before-prompt": t[1]!["Sequence"] = 0; break;
                case "untyped-error": t[1]!["Error"] = "overload text only"; break;
            }
            r = q.Run();
            cases.Check(variant, Is(r, 1, "assertion-failed") && q.Starts == 1);
        }
        q = new Fixture(scratch) { PostStatus = "Unavailable" };
        q.Row0["State"] = "Failed";
        r = q.Run();
        cases.Check("probe-positive", Is(r, 20, "provider-outage") && q.Post == 1 && r.Rows[0].ProviderError == "ServiceUnavailable" && r.Rows[0].Phase == "PostFailure");
        foreach (var field in new[] { "Account", "Endpoint", "Dependency" })
        {
            q = new Fixture(scratch) { PostStatus = "Unavailable", Foreign = field };
            q.Row0["State"] = "Failed";
            r = q.Run();
            cases.Check(field.ToLowerInvariant() + "-foreign", Is(r, 1, "assertion-failed") && q.Post == 1);
        }
        q = new Fixture(scratch) { PostStatus = "Unavailable", PostDelay = 16 };
        q.Row0["State"] = "Failed";
        r = q.Run();
        cases.Check("post-probe-bound", Is(r, 1) && q.Tick <= 15 && q.Post == 1);
        q = new Fixture(scratch);
        q.Row0["State"] = "Failed";
        r = q.Run();
        cases.Check("post-probe-count", Is(r, 1) && q.Post == 1);
        cases.Check("post-probe-no-prompt", q.ProbePrompts == 0 && q.Prompts == 1 && q.ProbeRequests.All(s => s.Operation == "Availability" && s.PromptCount == 0));
        cases.Complete("transcript-positive", "prompt-partial", "session-foreign", "request-foreign", "error-before-prompt", "untyped-error",
            "probe-positive", "account-foreign", "endpoint-foreign", "dependency-foreign", "post-probe-bound", "post-probe-count", "post-probe-no-prompt");
    }

    [Test]
    public void HealthyProviderAssertionStaysRed()
    {
        var cases = new C1545Cases(nameof(HealthyProviderAssertionStaysRed));
        using var scratch = new C1545Scratch("c1545-outcome-");
        var ok = true;
        Fixture q;
        LiveLaneRunResult r;
        foreach (var assertion in new[] { "EnterLost", "BridgeDropped" })
        {
            q = new Fixture(scratch) { PostStatus = "Ready" };
            q.Row0["State"] = "Failed";
            q.Row0["Assertion"] = assertion;
            r = q.Run();
            ok = ok && Is(r, 1, "assertion-failed") && r.Classification == "RED";
        }
        cases.Check("healthy-red", ok);
        ok = true;
        foreach (var unproven in new[] { "Unknown", "Timeout", "InternetFailure", "Screenshot", "ModelRefusal", "Throws" })
        {
            q = new Fixture(scratch) { PostStatus = unproven };
            q.Row0["State"] = "Failed";
            r = q.Run();
            ok = ok && Is(r, 1);
        }
        cases.Check("unproven-red", ok);
        ok = true;
        var invalid = true;
        foreach (var reverse in new[] { false, true })
        {
            q = new Fixture(scratch);
            q.Request.Uids = ["fixture.A.0", "fixture.B.0"];
            var a = Row("fixture.A.0", "Failed");
            var b = Row("fixture.B.0");
            Outage(b);
            q.Terminal!["Rows"] = reverse ? LiveLaneJsonFixtures.Array(b, a) : LiveLaneJsonFixtures.Array(a, b);
            r = q.Run();
            ok = ok && Is(r, 1) && r.Rows.Count(row => row.ExitCode == 1) == 1 && r.Rows.Count(row => row.ExitCode == 20) == 1;
            foreach (var field in new[] { "SchemaVersion", "Source", "Build", "Authority" })
            {
                var kept = q.Terminal[field]!.DeepClone();
                q.Terminal[field] = "invalid";
                q.Request.ResultsRoot += "-invalid-" + field;
                r = q.Run();
                invalid = invalid && Is(r, 2) && r.Rows.Count == 2;
                q.Terminal[field] = kept;
            }
        }
        foreach (var reverse in new[] { false, true })
        {
            q = new Fixture(scratch) { PostStatus = "Unavailable" };
            q.Request.Uids = ["fixture.A.0", "fixture.B.0"];
            var a = Row("fixture.A.0", "Failed");
            var b = Row("fixture.B.0", "Failed");
            q.Terminal!["Rows"] = reverse ? LiveLaneJsonFixtures.Array(b, a) : LiveLaneJsonFixtures.Array(a, b);
            r = q.Run();
            var rows = q.Terminal["Rows"]!.AsArray();
            cases.Check("probe-two-failed-" + (reverse ? "True" : "False"), Is(r, 1, "assertion-failed") && q.Post == 1
                && r.Rows[0].Uid == LiveLaneValues.Text(rows[0]!["Uid"]) && r.Rows[0].ExitCode == 20 && r.Rows[0].Proof == "Probe"
                && r.Rows[1].Uid == LiveLaneValues.Text(rows[1]!["Uid"]) && r.Rows[1].ExitCode == 1 && r.Rows[1].Proof == "None");
        }
        cases.Check("mixed-red", ok);
        cases.Check("mixed-invalid", invalid);
        cases.Complete("healthy-red", "unproven-red", "probe-two-failed-False", "probe-two-failed-True", "mixed-red", "mixed-invalid");
    }

    [Test]
    public void TypedSetupOutcomes()
    {
        var cases = new C1545Cases(nameof(TypedSetupOutcomes));
        using var scratch = new C1545Scratch("c1545-outcome-");
        var ok = true;
        Fixture q;
        LiveLaneRunResult r;
        var mapping = new Dictionary<string, string>
        {
            ["AuthMissing"] = "auth-required", ["AuthExpired"] = "auth-required", ["QuotaExceeded"] = "quota-exhausted",
            ["DependencyUnavailable"] = "provider-outage", ["StartupDefaultAbsent"] = "premise-absent"
        };
        foreach (var (status, reason) in mapping)
        {
            q = new Fixture(scratch) { PreStatus = status };
            r = q.Run();
            ok = ok && Is(r, 20, reason) && q.Starts == 0 && q.Prompts == 0 && r.Probes[0].Status == status;
        }
        cases.Check("external-setup", ok);
        ok = true;
        foreach (var status in new[] { "ReadyTimeout", "TrustNotCleared", "BridgeNotEstablished" })
        {
            q = new Fixture(scratch) { PreStatus = status };
            r = q.Run();
            ok = ok && Is(r, 20, "setup-unclassified") && q.Prompts == 0;
        }
        cases.Check("unclassified-setup", ok);
        ok = true;
        foreach (var healthy in new[] { true, false })
        {
            q = new Fixture(scratch);
            q.Row0["Setup"] = "SetupContractViolation";
            q.Row0["State"] = "Skipped";
            q.Row0["Healthy"] = healthy;
            r = q.Run();
            ok = ok && Is(r, healthy ? 1 : 20);
        }
        cases.Check("setup-contract", ok);
        ok = true;
        foreach (var field in new[] { "Executable", "Profile", "Context" })
        {
            q = new Fixture(scratch);
            if (field == "Context") q.Boundary.WriteContext = (_, _) => "";
            else if (field == "Executable") q.Request.Executable = "";
            else q.Request.Profile = "";
            r = q.Run();
            var reason = field switch { "Executable" => "executable-missing", "Profile" => "profile-required", _ => "lane-context-required" };
            ok = ok && Is(r, 20, reason) && q.Prompts == 0;
        }
        cases.Check("missing-prerequisite", ok);
        ok = true;
        foreach (var status in new[] { "LegacySkip", "arbitrary", "" })
        {
            q = new Fixture(scratch);
            q.Row0["State"] = "Skipped";
            q.Row0["Setup"] = status;
            r = q.Run();
            ok = ok && Is(r, 2, "row-invalid");
        }
        cases.Check("unknown-skip", ok);
        q = new Fixture(scratch) { PreStatus = "AuthMissing" };
        r = q.Run();
        cases.Check("setup-no-prompt", Is(r, 20, "auth-required") && q.Prompts == 0 && q.Starts == 0);
        foreach (var variant in new[] { "missing", "empty", "LegacySkip", "ready", "wrong-case-field", "AuthMissing", "AuthMissing-proof" })
        {
            q = new Fixture(scratch);
            var row = q.Row0;
            row["State"] = "Failed";
            row["Setup"] = variant;
            var expected = 2;
            var reason = "row-invalid";
            switch (variant)
            {
                case "missing": row.Remove("Setup"); break;
                case "empty": row["Setup"] = ""; break;
                case "wrong-case-field":
                    // A case-sensitive external reader's dictionary must carry the exact key Setup.
                    var wrong = new JsonObject();
                    foreach (var (key, value) in row)
                        if (key != "Setup") wrong[key] = value?.DeepClone();
                    wrong["setup"] = "Ready";
                    q.Terminal!["Rows"] = LiveLaneJsonFixtures.Array(wrong);
                    break;
                case "AuthMissing": expected = 1; reason = "assertion-failed"; break;
                case "AuthMissing-proof": row["Setup"] = "AuthMissing"; Outage(row); expected = 20; reason = "auth-required"; break;
            }
            r = q.Run();
            cases.Check("failed-setup-" + variant, Is(r, expected, reason) && q.Starts == 1, r.Reason);
        }
        cases.Complete("external-setup", "unclassified-setup", "setup-contract", "missing-prerequisite", "unknown-skip", "setup-no-prompt",
            "failed-setup-missing", "failed-setup-empty", "failed-setup-LegacySkip", "failed-setup-ready", "failed-setup-wrong-case-field",
            "failed-setup-AuthMissing", "failed-setup-AuthMissing-proof");
    }

    [Test]
    public void SkipAndZeroNeverGreen()
    {
        var cases = new C1545Cases(nameof(SkipAndZeroNeverGreen));
        using var scratch = new C1545Scratch("c1545-outcome-");
        var q = new Fixture(scratch);
        q.Request.Uids = ["fixture.A.0", "fixture.B.0"];
        q.Terminal!["Rows"] = LiveLaneJsonFixtures.Array(Row(), Row("fixture.B.0"));
        var r = q.Run();
        var ok = Is(r, 0);
        foreach (var variant in new[] { "zero", "missing", "duplicate", "unexpected", "wrong", "summary" })
        {
            q = new Fixture(scratch);
            q.Request.Uids = ["fixture.A.0", "fixture.B.0"];
            q.Terminal!["Rows"] = variant switch
            {
                "missing" => LiveLaneJsonFixtures.Array(Row()),
                "duplicate" => LiveLaneJsonFixtures.Array(Row(), Row()),
                "unexpected" => LiveLaneJsonFixtures.Array(Row(), Row("fixture.B.0"), Row("unexpected")),
                "wrong" => LiveLaneJsonFixtures.Array(Row(), Row("wrong")),
                _ => new JsonArray()
            };
            q.Terminal["Passed"] = 2;
            r = q.Run();
            ok = ok && Is(r, 2, "roster-incomplete");
        }
        cases.Check("exact-roster", ok);
        ok = true;
        foreach (var setup in new[] { "Ready", "AuthMissing", "LegacySkip" })
        {
            q = new Fixture(scratch);
            q.Request.Uids = ["fixture.A.0", "fixture.B.0"];
            var skip = Row("fixture.B.0", "Skipped");
            skip["Setup"] = setup;
            q.Terminal!["Rows"] = LiveLaneJsonFixtures.Array(Row(), skip);
            r = q.Run();
            ok = ok && Is(r, setup == "LegacySkip" ? 2 : 20);
        }
        // A typed preflight refusal explains why no uid executed.
        q = new Fixture(scratch) { PreStatus = "AuthExpired" };
        q.Terminal!["Rows"] = new JsonArray();
        r = q.Run();
        ok = ok && Is(r, 20, "auth-required");
        cases.Check("skipped", ok);
        ok = true;
        foreach (var field in new[] { "SchemaVersion", "Build", "Platform", "Authority", "Source", "Uid", "State" })
        {
            q = new Fixture(scratch);
            Outage(q.Row0);
            if (field is "Uid" or "State") q.Row0[field] = null; else q.Terminal![field] = null;
            r = q.Run();
            ok = ok && Is(r, 2);
        }
        cases.Check("invalid-schema", ok);
        cases.Complete("exact-roster", "skipped", "invalid-schema");
    }

    [Test]
    public void StaleSharedLockRefuses()
    {
        var cases = new C1545Cases(nameof(StaleSharedLockRefuses));
        using var scratch = new C1545Scratch("c1545-outcome-");
        foreach (var age in new[] { "2000-01-01T00:00:00Z", "2099-01-01T00:00:00Z" })
        {
            var q = new Fixture(scratch);
            var path = LiveLaneNativeLockSeam.LockPath(q.Request.CoordinationRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = new JsonObject
            {
                ["pid"] = 2147483647, ["runId"] = "dead", ["lane"] = "fixture-live", ["processStartedAt"] = "2000-01-01T00:00:00Z", ["acquiredAt"] = age
            }.ToJsonString();
            File.WriteAllText(path, bytes);
            var r = q.Run();
            cases.Check(age.StartsWith("2000", StringComparison.Ordinal) ? "stale" : "age", Is(r, 2, "stale-shared-lock") && r.Classification == "INVALID"
                && q.Starts == 0 && q.Slots == 0 && File.ReadAllText(path) == bytes);
        }
        var busy = new Fixture(scratch);
        var foreign = LiveLaneNativeLockSeam.Foreign();
        var owner = foreign.Enter(busy.Request.CoordinationRoot, "foreign", "fixture-live");
        var held = File.ReadAllText(owner.Path);
        var result = busy.Run();
        cases.Check("busy", Is(result, 21, "deferred-busy") && busy.Starts == 0 && busy.Slots == 0 && File.ReadAllText(owner.Path) == held);
        foreign.Exit(busy.Request.CoordinationRoot, "foreign");
        cases.Complete("stale", "age", "busy");
    }
}
