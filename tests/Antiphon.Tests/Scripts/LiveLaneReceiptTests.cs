using Antiphon.LiveLane;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2b: live-lane receipts are exact, typed, lineage-bound and revocable; a reuse is a use record, never an
// execution, and no state carries gate credit (shadow-only; V-14..V-19 of the S2a/S2b test design). W2 slice S22: the cases
// of the deleted scripts/test-live-lane-receipt.ps1 run in process against the C# receipts and state (LiveLaneReceipt);
// receipts, histories and an owned git repository (main X0 -> X -> Y, a divergent D from X0) are built here and ancestry
// is answered by real git. The status front door is LiveLaneStatus in process.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneReceiptTests
{
    private const string Lane = "fixture-lane";
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
    private static readonly string[] Roster = LiveLaneLaneFixture.Roster;
    private static readonly LiveLaneMap Components = new(("environment", new string('e', 64)), ("instructions", new string('1', 64)), ("laneContract", new string('2', 64)),
        ("liveRoots", new string('3', 64)), ("repositoryClosure", new string('4', 64)), ("runtimeInputs", new string('5', 64)), ("testSources", new string('6', 64)));

    [Test]
    public void ExactRosterRequired() => Run(nameof(ExactRosterRequired), w =>
    {
        var r = w.Receipt(w.Write(w.NewReceipt()));
        w.Cases.Check("exact", r.Valid && r.Outcome == "GREEN" && r.Code == 0, Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(), d => { d["rows"] = Rows(d).Take(5).ToList(); d["planned"] = 5L; d["executed"] = 5L; d["passed"] = 5L; }));
        w.Cases.Check("dropped", Invalid(r, "roster-incomplete"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(), d => Rows(d)[5] = new LiveLaneMap(("uid", Row(d, 0)["uid"]), ("outcome", "Passed"))));
        w.Cases.Check("duplicate", Invalid(r, "roster-duplicate") && !r.Reasons.Contains("counts-mismatch"), Show(r));
        var codex = Roster[0].Replace("(claude)", "(codex)");
        r = w.Receipt(w.Write(w.NewReceipt(), d =>
        {
            ((List<object>)d["rows"]!).Add(new LiveLaneMap(("uid", codex), ("outcome", "Passed")));
            d["planned"] = 7L; d["executed"] = 7L; d["passed"] = 7L;
        }));
        w.Cases.Check("extra-expansion", codex != Roster[0] && Invalid(r, "roster-unexpected"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(), d => Rows(d)[2] = new LiveLaneMap(("uid", Roster[2] + "_renamed"), ("outcome", "Passed"))));
        w.Cases.Check("renamed", Invalid(r, "roster-unexpected") && Invalid(r, "roster-incomplete"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(), d => { d["rows"] = Rows(d).Take(5).ToList(); d["executed"] = 5L; d["missing"] = 1L; d["passed"] = 5L; }));
        w.Cases.Check("planned-executed", Invalid(r, "roster-incomplete") && !r.Reasons.Contains("counts-mismatch"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(), d => { Row(d, 3)["outcome"] = "Skipped"; d["passed"] = 5L; d["skipped"] = 1L; }));
        w.Cases.Check("skipped", r.Valid && r.Outcome == "INCONCLUSIVE" && r.Code == 20, Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(), d => { d["rows"] = new List<object>(); d["planned"] = 0L; d["executed"] = 0L; d["passed"] = 0L; }));
        w.Cases.Check("zero", Invalid(r, "roster-empty"), Show(r));
    }, "exact", "dropped", "duplicate", "extra-expansion", "renamed", "planned-executed", "skipped", "zero");

    [Test]
    public void TypesAndCountsFailClosed() => Run(nameof(TypesAndCountsFailClosed), w =>
    {
        var r = w.Receipt(w.Write(w.NewReceipt()));
        w.Cases.Check("valid", r.Valid && r.Outcome == "GREEN" && r.Digest is { Length: 64 }, Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(("sourceClean", "true"))));
        w.Cases.Check("string-bool", Invalid(r, "type:sourceClean"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(("passed", "6"))));
        w.Cases.Check("string-count", Invalid(r, "type:passed"), Show(r));
        var a = w.Receipt(w.Write(w.NewReceipt(("durationMs", -1L))));
        var b = w.Receipt(w.Write(w.NewReceipt(("failed", -1L))));
        w.Cases.Check("negative", Invalid(a, "negative:durationMs") && Invalid(b, "negative:failed"), Show(a) + " / " + Show(b));
        r = w.Receipt(w.Write(w.NewReceipt(), d => Row(d, 5)["outcome"] = "Failed"));
        w.Cases.Check("counts-outcomes", Invalid(r, "counts-mismatch"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(("note", "x"))));
        w.Cases.Check("unknown-field", Invalid(r, "field:note"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(("kind", "live-lane-use"))));
        w.Cases.Check("kind", Invalid(r, "kind"), Show(r));
        // S22: schemaVersion 2 is the receipt with the Claude version, so it needs the claude field; 3 is no schema.
        a = w.Receipt(w.Write(w.NewReceipt(("schemaVersion", 3L))));
        b = w.Receipt(w.Write(w.NewReceipt(("schemaVersion", 2L))));
        w.Cases.Check("schema-version", Invalid(a, "schema") && Invalid(b, "required:claude") && !b.Reasons.Contains("schema"), Show(a) + " / " + Show(b));
        r = w.Receipt(w.Write(w.NewReceipt(), d => ((LiveLaneMap)d["fingerprint"]!)["digest"] = null));
        w.Cases.Check("null-required", Invalid(r, "required:fingerprint.digest"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(), d => Row(d, 0)["outcome"] = "Pass"));
        w.Cases.Check("outcome-enum", Invalid(r, "outcome-enum"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(("classifierVerdict", 20L), ("finalExit", 0L))));
        w.Cases.Check("classifier-exit", Invalid(r, "classifier-mismatch"), Show(r));
        var path = w.Write(w.NewReceipt());
        File.AppendAllText(Path.Combine(Path.GetDirectoryName(path)!, "console.log"), "x");
        r = w.Receipt(path);
        w.Cases.Check("artifact-digest", Invalid(r, "artifact-digest:console.log"), Show(r));
        r = w.Receipt(w.Write(w.NewReceipt(("startedUtc", LiveLaneReceipt.Iso(Now.AddMinutes(-50))), ("endedUtc", LiveLaneReceipt.Iso(Now.AddMinutes(-55))))));
        w.Cases.Check("time-order", Invalid(r, "time-order"), Show(r));
    }, "valid", "string-bool", "string-count", "negative", "counts-outcomes", "unknown-field", "kind", "schema-version", "null-required", "outcome-enum", "classifier-exit",
        "artifact-digest", "time-order");

    [Test]
    public void LineageAndHostBound() => Run(nameof(LineageAndHostBound), w =>
    {
        var s = w.State([w.Write(w.NewReceipt())]);
        w.Cases.Check("same-line-ancestor", Satisfied(s) && LiveLaneData.Same(s.Value!.Selected?["fingerprintDigest"], NewFp()["digest"]) && w.X != w.Y, Show(s));
        (bool, string) Refused(string reason, params (string, object?)[] over)
        {
            var state = w.State([w.Write(w.NewReceipt(over))]);
            return (Due(state, reason) && state.Value!.Freshness == null && state.Value.Selected == null, Show(state));
        }
        var r = Refused("foreign-repository", ("repository", "other/antiphon"));
        w.Cases.Check("foreign-repo", r.Item1, r.Item2);
        r = Refused("foreign-ref", ("ref", "rc/fixture-0"));
        w.Cases.Check("other-line", r.Item1, r.Item2);
        r = Refused("not-ancestor", ("sourceSha", w.D));
        w.Cases.Check("not-ancestor", r.Item1, r.Item2);
        r = Refused("source-missing", ("sourceSha", new string('d', 40)));
        w.Cases.Check("missing-object", r.Item1, r.Item2);
        r = Refused("host-mismatch", ("host", new LiveLaneMap(("enrollmentId", "host-B"))));
        w.Cases.Check("host", r.Item1, r.Item2);
        r = Refused("account-mismatch", ("account", new LiveLaneMap(("enrollmentId", "account-B"))));
        w.Cases.Check("account", r.Item1, r.Item2);
        // A lane id is part of the fingerprint domain, so the foreign receipt recomputes under its own lane.
        var foreign = w.NewReceipt(("laneId", "other-lane"));
        var fingerprint = (LiveLaneMap)foreign["fingerprint"]!;
        fingerprint["digest"] = LiveLaneFingerprint.Digest("other-lane", fingerprint["components"]);
        s = w.State([w.Write(foreign)]);
        w.Cases.Check("lane", Due(s, "foreign-lane") && s.Value!.Selected == null, Show(s));
        r = Refused("source-dirty", ("sourceClean", false));
        w.Cases.Check("dirty", r.Item1, r.Item2);
        var trusted = w.Write(w.NewReceipt(("authority", "trusted")));
        var check = w.Receipt(trusted);
        s = w.State([trusted]);
        w.Cases.Check("issuer", Invalid(check, "issuer-unqualified") && Due(s, "history-invalid") && s.Value!.Selected == null, Show(check) + " / " + Show(s));
    }, "same-line-ancestor", "foreign-repo", "other-line", "not-ancestor", "missing-object", "host", "account", "lane", "dirty", "issuer");

    [Test]
    public void NewerRedRevokes() => Run(nameof(NewerRedRevokes), w =>
    {
        void Red(LiveLaneMap d) { Row(d, 1)["outcome"] = "Failed"; d["passed"] = 5L; d["failed"] = 1L; d["classifierVerdict"] = 1L; d["finalExit"] = 1L; }
        var green = w.Write(w.NewReceipt(Now.AddHours(-2)));
        var red = w.Write(w.NewReceipt(Now.AddHours(-1)), Red);
        var inconclusive = w.Write(w.NewReceipt(Now.AddMinutes(-30)), d => { Row(d, 1)["outcome"] = "Skipped"; d["passed"] = 5L; d["skipped"] = 1L; d["classifierVerdict"] = 20L; d["finalExit"] = 20L; });
        var s = w.State([green, red]);
        w.Cases.Check("green-then-red", Due(s, "newer-red"), Show(s));
        s = w.State([green, inconclusive]);
        w.Cases.Check("newer-inconclusive", Due(s, "newer-inconclusive"), Show(s));
        var revocation = w.WriteJson(new LiveLaneMap(("kind", "live-lane-revocation"), ("schemaVersion", 1L), ("receiptDigest", Sha(green)), ("reason", "incident")), "revocation");
        s = w.State([green, revocation]);
        var control = w.State([green]);
        w.Cases.Check("revocation", Due(s, "revoked") && Satisfied(control), Show(s) + " / " + Show(control));
        var olderRed = w.Write(w.NewReceipt(Now.AddHours(-3)), Red);
        s = w.State([olderRed, green]);
        w.Cases.Check("red-then-green", Satisfied(s) && LiveLaneData.Same(s.Value!.Selected?["receiptDigest"], Sha(green)), Show(s));
        var other = (LiveLaneMap)LiveLaneData.Parse(LiveLaneData.Canonical(Components))!;
        other["testSources"] = new string('7', 64);
        var foreignRed = w.Write(w.NewReceipt(Now.AddHours(-1), NewFp(other)), Red);
        s = w.State([green, foreignRed]);
        w.Cases.Check("other-fingerprint-red", Satisfied(s) && LiveLaneData.Same(s.Value!.Selected?["receiptDigest"], Sha(green)), Show(s));
        var use = w.WriteJson(UseRecord(w, Sha(green), 0), "use");
        s = w.State([green, red, inconclusive, use]);
        w.Cases.Check("last-green-only", s.Value?.LastGreen == Sha(green) && Due(s, "newer-inconclusive") && !s.Value!.Reasons.Contains("history-invalid"), Show(s));
        // Recorded time, then sequence: the enumeration order never decides.
        var tie = Now.AddMinutes(-20);
        var first = w.Write(w.NewReceipt(tie, null, ("sequence", 1L)));
        var second = w.Write(w.NewReceipt(tie, null, ("sequence", 2L)), Red);
        var a = w.State([green, red]);
        var b = w.State([red, green]);
        var c = w.State([second, first]);
        var e = w.State([first, second]);
        w.Cases.Check("order-independent", Due(a, "newer-red") && Due(b, "newer-red") && string.Join(",", a.Value!.Reasons) == string.Join(",", b.Value!.Reasons)
            && Due(c, "newer-red") && Due(e, "newer-red"), Show(b) + " / " + Show(c));
        var one = w.Write(w.NewReceipt(Now.AddHours(-2), null, ("runId", "run-shared")));
        var two = w.Write(w.NewReceipt(Now.AddHours(-2), null, ("runId", "run-shared")), Red);
        s = w.State([one, two]);
        var t = w.State([two, one]);
        w.Cases.Check("contradictory-duplicate", s.Error == "contradictory-receipts" && t.Error == "contradictory-receipts", Show(s) + " / " + Show(t));
    }, "green-then-red", "newer-inconclusive", "revocation", "red-then-green", "other-fingerprint-red", "last-green-only", "order-independent", "contradictory-duplicate");

    [Test]
    public void ReuseIsNotExecution() => Run(nameof(ReuseIsNotExecution), w =>
    {
        var receipt = w.Write(w.NewReceipt());
        var before = Sha(receipt);
        var s = w.State([receipt]);
        var usePath = Path.Combine(w.Results, "use-record.json");
        string? refusal = null;
        try { LiveLaneReceipt.WriteUse(s.Value!, usePath, Now); }
        catch (LiveLaneRefusal e) { refusal = e.Message; }
        var written = File.Exists(usePath) ? (LiveLaneMap)LiveLaneData.Parse(File.ReadAllText(usePath))! : new LiveLaneMap();
        var check = LiveLaneReceipt.CheckUse(usePath);
        w.Cases.Check("use-record", Satisfied(s) && LiveLaneData.Same(written["kind"], "live-lane-use") && written["executedNow"] is 0L && written["reused"] is true
            && LiveLaneData.Same(written["receiptDigest"], before) && LiveLaneData.Same(written["runId"], s.Value!.Selected?["runId"]) && LiveLaneData.Same(written["candidateSha"], w.Y)
            && written["executed"] is 0L && written["passed"] is 0L && written["gateCredit"] is false && check.Valid, Show(s) + " record=" + refusal + " " + LiveLaneData.Canonical(written));
        var after = Sha(receipt);
        var again = w.Receipt(receipt);
        w.Cases.Check("receipt-immutable", before == after && again.Valid && again.Digest == before && !File.ReadAllText(receipt).Contains("lastUsed")
            && LiveLaneData.Same(written["receiptDigest"], after), before + " " + after);
        s = w.State([usePath]);
        w.Cases.Check("no-chained-reuse", Due(s, "no-execution-receipt") && s.Value!.Selected == null, Show(s));
        var forged = w.WriteJson(UseRecord(w, before, 6), "forged-use");
        var u = LiveLaneReceipt.CheckUse(forged);
        s = w.State([receipt, forged]);
        w.Cases.Check("forged-executed", !u.Valid && u.Reasons.Contains("use-executed") && Due(s, "history-invalid"), string.Join(",", u.Reasons) + " / " + Show(s));
        var existing = Path.Combine(w.Results, "existing-use.json");
        File.WriteAllText(existing, "existing bytes");
        refusal = null;
        try { LiveLaneReceipt.WriteUse(w.State([receipt]).Value!, existing, Now); }
        catch (LiveLaneRefusal e) { refusal = e.Message; }
        w.Cases.Check("no-overwrite", refusal == "use-record-exists" && File.ReadAllText(existing) == "existing bytes", "error=" + refusal);
        // Through the status front door: real fingerprint and attestation, default policy, then Bounded24h.
        var documents = LiveClaudeProfileFixture.CleanTempSubdirectory("c1545-receipt-");
        try
        {
            LiveLaneAttestationExport.WriteAll(documents);
            var attestation = Path.Combine(documents, "base.json");
            var fingerprint = LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), w.Root, w.Y, Lane, attestation);
            var history = Path.Combine(w.Results, "history");
            Directory.CreateDirectory(history);
            w.Write(w.NewReceipt(Now.AddHours(-1), new LiveLaneMap(("digest", fingerprint.Digest), ("components", fingerprint.Components))), null, history);
            var context = w.WriteJson(new LiveLaneMap(("repository", "fixture/antiphon"), ("ref", "rc/fixture-1"), ("host", "host-A"), ("account", "account-A"), ("nonce", "nonce-A"),
                ("nowUtc", LiveLaneReceipt.Iso(Now)), ("probes", new List<object> { IsoProbe("auth"), IsoProbe("control") }), ("revocations", new List<object>())), "context");
            var defaultUse = Path.Combine(w.Results, "default-use.json");
            var boundedUse = Path.Combine(w.Results, "bounded-use.json");
            var @default = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), LiveLaneStatusOptions.Of(w.Root, Lane, w.Y, attestation, history, context, useRecord: defaultUse));
            var bounded = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), LiveLaneStatusOptions.Of(w.Root, Lane, w.Y, attestation, history, context, "Bounded24h", boundedUse));
            s = w.State([receipt], policy: null);
            w.Cases.Check("policy-default", fingerprint.Complete && @default.ExitCode == 0 && LiveLaneData.Same(@default.Document["state"], "DUE")
                && ((List<object>)@default.Document["reasons"]!).Contains("policy-always-rerun") && LiveLaneData.Same(@default.Document["localCompatibility"], "SATISFIED")
                && !File.Exists(defaultUse) && bounded.ExitCode == 0 && LiveLaneData.Same(bounded.Document["state"], "SATISFIED") && File.Exists(boundedUse)
                && Due(s, "policy-always-rerun"), LiveLaneData.Canonical(@default.Document) + " / " + LiveLaneData.Canonical(bounded.Document));
            w.States.Add(@default.Document);
            w.States.Add(bounded.Document);
        }
        finally { Directory.Delete(documents, true); }
        var states = w.States;
        w.Cases.Check("gate-credit", states.Count >= 7 && states.Count(st => LiveLaneData.Same(st["state"], "SATISFIED")) >= 2 && states.Count(st => LiveLaneData.Same(st["state"], "DUE")) >= 3
            && states.All(st => st["gateCredit"] is false && LiveLaneData.Same(st["effectiveMode"], "Full") && LiveLaneData.Same(st["activation"], "shadow-only")), "states=" + states.Count);
    }, "use-record", "receipt-immutable", "no-chained-reuse", "forged-executed", "no-overwrite", "policy-default", "gate-credit");

    [Test]
    public void InputDriftRefuses() => Run(nameof(InputDriftRefuses), w =>
    {
        var receipt = w.Write(w.NewReceipt());
        var s = w.State([receipt]);
        w.Cases.Check("equal-all", Satisfied(s) && s.Value!.ReuseEligible && s.Value.Freshness!.Reasons.Contains("d6-bounded-exception"), Show(s));
        var changed = (LiveLaneMap)LiveLaneData.Parse(LiveLaneData.Canonical(Components))!;
        changed["testSources"] = new string('9', 64);
        s = w.State([receipt], NewFp(changed));
        w.Cases.Check("fingerprint-unequal", Due(s, "fingerprint-changed:testSources") && !s.Value!.Reasons.Contains("fingerprint-changed:laneContract"), Show(s));
        var tampered = w.Write(w.NewReceipt(), d => ((LiveLaneMap)((LiveLaneMap)d["fingerprint"]!)["components"]!)["testSources"] = new string('9', 64));
        var r = w.Receipt(tampered);
        w.Cases.Check("component-tamper", Invalid(r, "fingerprint-tamper"), Show(r));
        var roster = Roster.Take(5).Append(Roster[5] + "_v2").ToArray();
        s = w.State([receipt], roster: roster);
        w.Cases.Check("roster-changed", Due(s, "roster-changed") && s.Value!.Selected == null, Show(s));
        var unknown = NewFp(Components, "unknown");
        s = w.State([receipt], unknown);
        w.Cases.Check("unknown-input", LiveLaneData.Same(unknown["digest"], NewFp()["digest"]) && Due(s, "unknown-input") && s.Value!.Selected == null && s.Value.LastGreen == null, Show(s));
        var drift = w.Write(w.NewReceipt(("inputAfter", new string('b', 64))));
        r = w.Receipt(drift);
        s = w.State([drift]);
        w.Cases.Check("pre-post", Invalid(r, "input-drift") && s.Value?.State == "DUE", Show(r) + " / " + Show(s));
        s = w.State([receipt], NewFp(Components, "unknown", "unknown"));
        w.Cases.Check("attestation-unknown", Due(s, "environment-unknown"), Show(s));
        var stale = w.Write(w.NewReceipt(Now.AddSeconds(-(24 * 3600 + 1))));
        s = w.State([stale]);
        var fresh = w.State([w.Write(w.NewReceipt(Now.AddSeconds(-(24 * 3600 - 1))))]);
        w.Cases.Check("stale", Due(s, "reuse-expired") && s.Value!.LocalCompatibility == "SATISFIED" && Satisfied(fresh), Show(s) + " / " + Show(fresh));
    }, "equal-all", "fingerprint-unequal", "component-tamper", "roster-changed", "unknown-input", "pre-post", "attestation-unknown", "stale");

    private sealed record StateAnswer(LiveLaneState? Value, string? Error);

    /// <summary>The candidate fingerprint (the driver's New-Fp): status, digest of the components, the attested environment.</summary>
    private static LiveLaneMap NewFp(LiveLaneMap? components = null, string status = "complete", string environment = "complete")
    {
        var copy = (LiveLaneMap)LiveLaneData.Parse(LiveLaneData.Canonical(components ?? Components))!;
        var reasons = new List<object>();
        if (status != "complete") reasons.Add(environment != "complete" ? "environment-unknown" : "declared-root-empty:x/");
        return new LiveLaneMap(("status", status), ("digest", LiveLaneFingerprint.Digest(Lane, copy)), ("components", copy), ("unknownReasons", reasons),
            ("environment", new LiveLaneMap(("status", environment), ("reasons", new List<object>()))));
    }

    private static LiveLaneMap IsoProbe(string kind) =>
        new(("kind", kind), ("nonce", "nonce-A"), ("host", "host-A"), ("account", "account-A"), ("atUtc", LiveLaneReceipt.Iso(Now.AddSeconds(-30))), ("result", "Ok"));

    private static LiveLaneMap UseRecord(World w, string receiptDigest, long executedNow) => new(("kind", "live-lane-use"), ("schemaVersion", 1L), ("laneId", Lane),
        ("candidateSha", w.Y), ("receiptDigest", receiptDigest), ("runId", "run-use"), ("fingerprintDigest", NewFp()["digest"]), ("reused", true), ("executedNow", executedNow),
        ("executed", 0L), ("passed", 0L), ("failed", 0L), ("skipped", 0L), ("gateCredit", false), ("createdUtc", LiveLaneReceipt.Iso(Now.AddMinutes(-1))));

    private static List<object> Rows(LiveLaneMap receipt) => (List<object>)receipt["rows"]!;

    private static LiveLaneMap Row(LiveLaneMap receipt, int index) => (LiveLaneMap)Rows(receipt)[index];

    private static string Sha(string path) => LiveLaneData.Sha256(File.ReadAllBytes(path));

    private static bool Invalid(LiveLaneReceiptCheck r, string reason) => !r.Valid && r.Reasons.Contains(reason) && r.Outcome != "GREEN";
    private static bool Due(StateAnswer s, string reason) => s.Value is { State: "DUE", GateCredit: false } && s.Value.Reasons.Contains(reason);
    private static bool Satisfied(StateAnswer s) => s.Value is { State: "SATISFIED", LocalCompatibility: "SATISFIED", GateCredit: false };
    private static string Show(LiveLaneReceiptCheck r) => "valid=" + r.Valid + " outcome=" + r.Outcome + " reasons=" + string.Join(",", r.Reasons);
    private static string Show(StateAnswer s) => "state=" + s.Value?.State + " reasons=" + string.Join(",", s.Value?.Reasons ?? []) + " error=" + s.Error;

    /// <summary>The owned repository: main X0 -> X -> Y (Y changes only a file outside every lane input), D diverges from X0.</summary>
    private sealed class World
    {
        public World(string method, string results)
        {
            Cases = new C1545Cases(method);
            Results = results;
            Repo = new LiveLaneRepo(Path.Combine(results, "receipt repository"));
            Repo.Put("tests/rc-project-categories.json", "{\"schemaVersion\":1,\"projects\":{\"tests/Pty.Tests/Pty.Tests.csproj\":[\"fixture\"]},\"batches\":[\"tests/Pty.Tests/Pty.Tests.csproj\"]}");
            Repo.Put("tests/Pty.Tests/Pty.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.0\" /></ItemGroup></Project>");
            Repo.Put("tests/Pty.Tests/T.cs", "namespace Antiphon.Agents.Pty.Tests;\n");
            Repo.Put("scripts/lib/live-lane-contract.ps1", "# contract\n");
            Repo.Put("CLAUDE.md", "# fixture\n");
            Repo.Put("tests/live-lanes.json", LiveLaneData.Canonical(LiveLaneLaneFixture.Manifest(Roster)));
            Repo.Put("tests/live-lane-inputs.json", "{\"format\":\"antiphon-live-lane-inputs-v1\",\"lanes\":{\"fixture-lane\":{\"manifest\":\"tests/live-lanes.json\","
                + "\"contractFiles\":[\"tests/live-lanes.json\",\"tests/live-lane-inputs.json\"],\"minimumRoots\":[\"tests/Pty.Tests/\"],\"runtimeRoots\":[\"scripts/\"],"
                + "\"namespaceRoots\":[\"Antiphon.Agents.Pty\"],\"instructionPatterns\":[\"CLAUDE.md\"]}}}");
            string Step(string text)
            {
                Repo.Put("docs/notes.md", text);
                return Repo.Commit(text.Trim());
            }
            X0 = Step("zero\n");
            X = Step("receipt\n");
            Y = Step("candidate\n");
            Repo.Checkout(X0);
            D = Step("divergent\n");
        }

        public C1545Cases Cases { get; }
        public string Results { get; }
        public LiveLaneRepo Repo { get; }
        public string Root => Repo.Root;
        public string X0 { get; }
        public string X { get; }
        public string Y { get; }
        public string D { get; }
        public List<LiveLaneMap> States { get; } = [];

        public LiveLaneMap NewReceipt(params (string Key, object? Value)[] over) => NewReceipt(Now.AddHours(-1), null, over);

        public LiveLaneMap NewReceipt(DateTimeOffset ended, LiveLaneMap? fingerprint = null, params (string Key, object? Value)[] over)
        {
            fingerprint ??= NewFp();
            var rows = Roster.Select(uid => (object)new LiveLaneMap(("uid", uid), ("outcome", "Passed"))).ToList();
            var document = new LiveLaneMap(("kind", "live-lane-execution"), ("schemaVersion", 1L), ("laneId", Lane), ("runId", "run-" + Guid.NewGuid().ToString("N")[..12]),
                ("sequence", 1L), ("authority", "diagnostic"), ("repository", "fixture/antiphon"), ("ref", "rc/fixture-1"), ("sourceSha", X), ("sourceClean", true),
                ("host", new LiveLaneMap(("enrollmentId", "host-A"))), ("account", new LiveLaneMap(("enrollmentId", "account-A"))),
                ("startedUtc", LiveLaneReceipt.Iso(ended.AddMinutes(-5))), ("endedUtc", LiveLaneReceipt.Iso(ended)), ("durationMs", 300000L),
                ("fingerprint", new LiveLaneMap(("digest", fingerprint["digest"]), ("components", fingerprint["components"]), ("rosterDigest", LiveLaneReceipt.RosterDigest(Roster)))),
                ("inputBefore", new string('a', 64)), ("inputAfter", new string('a', 64)), ("external", new LiveLaneMap(("epoch", "epoch-A"))),
                ("planned", 6L), ("executed", 6L), ("missing", 0L), ("passed", 6L), ("failed", 0L), ("skipped", 0L), ("rows", rows), ("classifierVerdict", 0L), ("finalExit", 0L),
                ("artifacts", new List<object>()));
            foreach (var (key, value) in over) document[key] = value;
            return document;
        }

        /// <summary>Each receipt lives in its own directory with its artifact beside it.</summary>
        public string Write(LiveLaneMap document, Action<LiveLaneMap>? edit = null, string? directory = null)
        {
            directory ??= Path.Combine(Results, "r-" + Guid.NewGuid().ToString("N")[..12]);
            Directory.CreateDirectory(directory);
            var artifact = Path.Combine(directory, "console.log");
            if (!File.Exists(artifact)) File.WriteAllText(artifact, "console bytes\n");
            document["artifacts"] = new List<object> { new LiveLaneMap(("name", "console.log"), ("sha256", Sha(artifact))) };
            edit?.Invoke(document);
            var path = Path.Combine(directory, "receipt-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            File.WriteAllText(path, LiveLaneData.Canonical(document));
            return path;
        }

        public string WriteJson(LiveLaneMap value, string name)
        {
            var path = Path.Combine(Results, name + "-" + Guid.NewGuid().ToString("N")[..8] + ".json");
            File.WriteAllText(path, LiveLaneData.Canonical(value));
            return path;
        }

        public LiveLaneReceiptCheck Receipt(string path) => LiveLaneReceipt.Check(path, roster: Roster);

        public StateAnswer State(IReadOnlyList<string> history, LiveLaneMap? fingerprint = null, IReadOnlyList<string>? roster = null, string? policy = "Bounded24h")
        {
            var candidate = new LiveLaneStateCandidate
            {
                LaneId = Lane, Repository = "fixture/antiphon", Ref = "rc/fixture-1", Sha = Y, Host = "host-A", Account = "account-A", Nonce = "nonce-A", Fingerprint = fingerprint ?? NewFp()
            };
            var probes = new List<object>
            {
                new LiveLaneMap(("kind", "auth"), ("nonce", "nonce-A"), ("host", "host-A"), ("account", "account-A"), ("atUtc", Now.AddSeconds(-30)), ("result", "Ok")),
                new LiveLaneMap(("kind", "control"), ("nonce", "nonce-A"), ("host", "host-A"), ("account", "account-A"), ("atUtc", Now.AddSeconds(-30)), ("result", "Ok"))
            };
            try
            {
                var state = LiveLaneReceipt.State(LiveLaneTestSources.Create().Git, candidate, history, roster ?? Roster, Root, policy, Now, probes, new List<object>());
                States.Add(state.ToJson());
                return new StateAnswer(state, null);
            }
            catch (LiveLaneRefusal refusal) { return new StateAnswer(null, refusal.Message); }
        }
    }

    private static void Run(string method, Action<World> body, params string[] expected)
    {
        using var scratch = new LaneScratch("c1545-receipt-");
        var world = new World(method, scratch.Root);
        body(world);
        world.Cases.Complete(expected);
    }
}
