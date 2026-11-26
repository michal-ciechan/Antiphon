using System.Diagnostics;
using System.Text.Json.Nodes;
using Antiphon.LiveLane;
using Antiphon.Tests.TestHelpers;
using Antiphon.TestSupport.LiveClaude;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1545 S2a-2: the live-lane environment attestation is exact, fails closed and is shadow-only (V-8..V-13 of the
// S2a/S2b test design). W2 slice S22: the cases of the deleted scripts/test-live-lane-attestation.ps1 run in process
// against the C# consumer (LiveLaneAttestation); each method produces its documents from the real S1 objects
// (LiveLaneAttestationExport) and forges schema faults here. The status front door is LiveLaneStatus in process.
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LiveLaneAttestationTests
{
    private const string Lane = "fixture-lane";
    private const string Sentinel = "SENTINEL-c1545-credential";

    private static readonly string[] Roster = LiveLaneLaneFixture.Roster;

    [Test]
    public void CliVersionOrBytesInvalidates() => Run(nameof(CliVersionOrBytesInvalidates), w =>
    {
        var fixture = LaneFixture(w);
        var plain = Fp(fixture, null);
        var withA = Fp(fixture, w.Doc("base"));
        var withB = Fp(fixture, w.Doc("image-b"));
        var @base = w.Att("base");
        w.Cases.Check("same-version-new-bytes", Differs(w.Att("image-b"), @base, "executable") && withA.Complete && withA.Components!["environment"] is string env
            && !plain.Components!.Contains("environment") && env == @base.Value!.Digest && withA.Digest != withB.Digest && withA.Digest != plain.Digest, Show(@base));
        w.Cases.Check("version", Differs(w.Att("version-2"), @base, "executable"));
        w.Cases.Check("installed-target", Differs(w.Att("installed-target"), @base, "executable"));
        var drift = w.Att("drift");
        var fpDrift = Fp(fixture, w.Doc("drift"));
        w.Cases.Check("pre-post-drift", Unknown(drift, "executable-drift:" + w.Uids[^1]) && fpDrift.Status == "unknown" && fpDrift.UnknownReasons.Contains("environment-unknown"), Show(drift));
        var first = w.Json("base");
        var second = w.Json("base-again");
        w.Cases.Check("equal", Same(@base, w.Att("base-again")) && Str(first, "rows", 0, "attested", "path") != Str(second, "rows", 0, "attested", "path")
            && Str(first, "rows", 0, "observed", "session") != Str(second, "rows", 0, "observed", "session"), Show(@base));
    }, "same-version-new-bytes", "version", "installed-target", "pre-post-drift", "equal");

    [Test]
    public void WrapperPayloadInvalidates() => Run(nameof(WrapperPayloadInvalidates), w =>
    {
        w.Cases.Check("chain-payload", Differs(w.Att("chain-1"), w.Att("chain-1b"), "executable"));
        w.Cases.Check("chain-shape", Differs(w.Att("chain-1"), w.Att("base"), "executable") && Differs(w.Att("chain-2"), w.Att("chain-1"), "executable"));
        var nulled = w.Att(w.Forge("chain-1", d => { foreach (var side in new[] { "attested", "observed" }) d["rows"]![0]![side]!["chain"]![0]!["sha256"] = null; }));
        w.Cases.Check("chain-unresolved", Unknown(nulled, "chain-unresolved:" + w.Uids[0]), Show(nulled));
        var foreign = w.Att(w.Forge("chain-1", d => d["rows"]![0]!["observed"]!["chain"]![0]!["sha256"] = new string('f', 64)));
        w.Cases.Check("observed-chain", Unknown(foreign, "executable-mismatch:" + w.Uids[0]), Show(foreign));
    }, "chain-payload", "chain-shape", "chain-unresolved", "observed-chain");

    [Test]
    public void AttestedPathMismatchRefuses() => Run(nameof(AttestedPathMismatchRefuses), w =>
    {
        var path = w.Att(w.Forge("base", d => d["rows"]![0]!["observed"]!["path"] = (string)d["rows"]![0]!["observed"]!["path"]! + ".other"));
        w.Cases.Check("launched-path", Unknown(path, "executable-mismatch:" + w.Uids[0]), Show(path));
        var hash = w.Att(w.Forge("base", d => d["rows"]![0]!["observed"]!["sha256"] = new string('e', 64)));
        w.Cases.Check("launched-hash", Unknown(hash, "executable-mismatch:" + w.Uids[0]), Show(hash));
        var route = w.Att(w.Forge("base", d => d["rows"]![0]!["attested"]!["route"] = "path-search"));
        var leaf = w.Att("leaf-cl");
        w.Cases.Check("path-route", Unknown(route, "route-not-owned:" + w.Uids[0]) && Unknown(leaf, "route-not-owned:" + w.Uids[0]), Show(route) + " / " + Show(leaf));
        var missing = w.Att(w.Forge("base", d => ((JsonObject)d["rows"]![0]!).Remove("observed")));
        w.Cases.Check("missing-observation", Unknown(missing, "observation-missing:" + w.Uids[0]), Show(missing));
        var last = w.Att(w.Forge("base", d => d["rows"]![5]!["observed"]!["path"] = (string)d["rows"]![5]!["observed"]!["path"]! + ".other"));
        w.Cases.Check("any-row", w.Uids.Length == 6 && Unknown(last, "executable-mismatch:" + w.Uids[5]) && !last.Value!.Reasons.Any(r => r.EndsWith(":" + w.Uids[0], StringComparison.Ordinal)), Show(last));
    }, "launched-path", "launched-hash", "path-route", "missing-observation", "any-row");

    [Test]
    public void ConfigurationInvalidates() => Run(nameof(ConfigurationInvalidates), w =>
    {
        var @base = w.Att("base");
        w.Cases.Check("user", Differs(w.Att("user"), @base, "settings"));
        w.Cases.Check("project-local", Differs(w.Att("project"), @base, "settings") && Differs(w.Att("local"), @base, "settings") && Differs(w.Att("local"), w.Att("project")));
        w.Cases.Check("managed", Differs(w.Att("managed"), @base, "settings"));
        w.Cases.Check("global-state", Differs(w.Att("global"), @base, "settings"));
        w.Cases.Check("overlay", Differs(w.Att("overlay-a"), w.Att("overlay-b"), "settings"));
        w.Cases.Check("model", Differs(w.Att("model-haiku"), w.Att("model-other"), "settings"));
        var absent = w.Att("env-absent");
        var empty = w.Att("env-empty");
        w.Cases.Check("env-tristate", Differs(absent, empty, "settings") && Differs(empty, @base, "settings") && Differs(absent, @base, "settings"));
        w.Cases.Check("runtime-identity", Differs(w.Att("runtime-dotnet"), @base, "runtime") && Differs(w.Att("runtime-packages"), @base, "runtime"));
        w.Cases.Check("volatile-only", Same(w.Att("volatile"), @base));
        var text = File.ReadAllText(w.Doc("credential"));
        var credential = w.Att("credential");
        var output = LiveLaneData.Canonical(credential.Value?.ToJson());
        var forged = w.Status(w.Forge("base", d => d["environment"]!["ANTHROPIC_API_KEY"] = "value:" + Sentinel));
        w.Cases.Check("credential-free", Same(credential, @base) && !text.Contains(Sentinel) && !output.Contains(Sentinel) && Refused(forged, "credential-field-present")
            && !forged.Text.Contains(Sentinel), Show(credential) + " status=" + forged.Exit + " " + forged.Text);
        // A-5: a credential name in any letter case, in a nested object or inside a list item, refuses by name (never
        // through the generic schema fault) and never echoes the value.
        var cased = w.Att(w.Forge("base", d => d["environment"]!["AccessToken"] = "value:" + Sentinel));
        var listed = w.Att(w.Forge("base", d => d["rows"]![0]!["observed"]!["APIKEY"] = Sentinel));
        var upper = w.Att(w.Forge("base", d => d["OAUTHACCOUNT"] = new JsonObject { ["emailAddress"] = Sentinel }));
        w.Cases.Check("credential-case", new[] { cased, listed, upper }.All(a => a.Error == "credential-field-present")
            && !(cased.Error + listed.Error + upper.Error).Contains(Sentinel), cased.Error + " / " + listed.Error + " / " + upper.Error);
    }, "user", "project-local", "managed", "global-state", "overlay", "model", "env-tristate", "runtime-identity", "volatile-only", "credential-free", "credential-case");

    [Test]
    public void UnknownSettingsRefuseReuse() => Run(nameof(UnknownSettingsRefuseReuse), w =>
    {
        var att = w.Att("unknown-key");
        w.Cases.Check("unknown-key", Unknown(att, "settings-not-reusable"), Show(att));
        att = w.Att("managed-unverified");
        w.Cases.Check("managed-unverified", Unknown(att, "source-unverified:managed"), Show(att));
        att = w.Att("unreadable");
        w.Cases.Check("unreadable", Unknown(att, "source-unreadable:user"), Show(att));
        att = w.Att(w.Forge("base", d =>
        {
            var sources = d["settings"]!["sources"]!.AsArray();
            foreach (var source in sources.Where(s => (string)s!["kind"]! == "global").ToList()) sources.Remove(source);
        }));
        w.Cases.Check("missing-source-entry", Unknown(att, "source-missing:global"), Show(att));
        att = w.Att("additional-source");
        w.Cases.Check("additional-source", Unknown(att, "source-unresolved"), Show(att));
        att = w.Att(w.Forge("unreadable", d => d["settings"]!["reuseEligible"] = true));
        w.Cases.Check("forged-eligible", Unknown(att, "source-unreadable:user") && !att.Value!.Reasons.Contains("settings-not-reusable"), Show(att));
        att = w.Att("runtime-missing");
        w.Cases.Check("runtime-missing", Unknown(att, "runtime-missing:restoredPackagesDigest"), Show(att));
        var status = w.Status(w.Forge("base", d => d["settings"]!["reuseEligible"] = "true"));
        w.Cases.Check("string-boolean", Refused(status, "attestation-invalid"), status.Text);
        var extra = w.Status(w.Forge("base", d => d["extra"] = 1));
        var version = w.Status(w.Forge("base", d => d["schemaVersion"] = 2));
        w.Cases.Check("schema", Refused(extra, "attestation-invalid") && Refused(version, "attestation-invalid"), extra.Text + " / " + version.Text);
        // A-2: a linked document (file link, or a linked directory) is refused, and so is a document over the size cap.
        var linkedDirectory = Path.Combine(w.Results, "linked-attestations");
        DirectoryLink(linkedDirectory, w.Documents);
        Attested viaDirectory;
        try { viaDirectory = w.Att(Path.Combine(linkedDirectory, "base.json")); }
        finally { Directory.Delete(linkedDirectory); }
        var viaFile = new Attested(null, "attestation-link");
        if (!OperatingSystem.IsWindows())
        {
            var linkedFile = Path.Combine(w.Results, "linked-base.json");
            File.CreateSymbolicLink(linkedFile, w.Doc("base"));
            try { viaFile = w.Att(linkedFile); }
            finally { File.Delete(linkedFile); }
        }
        var copied = Path.Combine(w.Results, "copied-base.json");
        File.Copy(w.Doc("base"), copied);
        w.Cases.Check("linked-document", viaDirectory.Error == "attestation-link" && viaFile.Error == "attestation-link" && w.Att(copied).Value?.Status == "complete",
            Show(viaDirectory) + " / " + Show(viaFile));
        var baseText = File.ReadAllText(w.Doc("base"));
        var bytes = System.Text.Encoding.UTF8.GetByteCount(baseText);
        var large = Path.Combine(w.Results, "large-base.json");
        File.WriteAllText(large, baseText + new string(' ', LiveLaneAttestation.DocumentCap - bytes + 1));
        var atCap = Path.Combine(w.Results, "cap-base.json");
        File.WriteAllText(atCap, baseText + new string(' ', LiveLaneAttestation.DocumentCap - bytes));
        var over = w.Att(large);
        var equal = w.Att(atCap);
        w.Cases.Check("size-cap", over.Error == "attestation-too-large" && equal.Value?.Status == "complete", Show(over) + " / " + Show(equal));
        // A-3: rows bind to the lane roster. A short document, a foreign uid and an unbound call are unknown; a foreign uid
        // is never echoed.
        var @short = w.Att(w.Forge("base", d => d["rows"] = new JsonArray(d["rows"]![0]!.DeepClone())));
        var foreign = w.Att(w.Forge("base", d =>
        {
            d["rows"]![0]!["uid"] = "sk-ant-" + Sentinel;
            d["rows"]![0]!["attested"]!["route"] = "path-search";
        }));
        var foreignText = LiveLaneData.Canonical(foreign.Value?.ToJson());
        var unbound = w.Att("base", bound: false);
        w.Cases.Check("roster-bound", Unknown(@short, "roster-missing:" + w.Uids[1]) && Unknown(@short, "roster-missing:" + w.Uids[5])
            && Unknown(foreign, "roster-unexpected") && Unknown(foreign, "roster-missing:" + w.Uids[0]) && !foreignText.Contains(Sentinel)
            && Unknown(unbound, "roster-unbound") && w.Att("base").Value?.Status == "complete", Show(@short) + " / " + Show(foreign) + " / " + Show(unbound));
    }, "unknown-key", "managed-unverified", "unreadable", "missing-source-entry", "additional-source", "forged-eligible", "runtime-missing", "string-boolean", "schema",
        "linked-document", "size-cap", "roster-bound");

    [Test]
    public void ExternalEpochIsNotInferred() => Run(nameof(ExternalEpochIsNotInferred), w =>
    {
        var now = DateTimeOffset.Parse("2026-11-02T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        var epoch = Str(w.Json("base"), "external", "epoch");
        LiveLaneMap Probe(string kind, int age, string? nonce = "nonce-A", string? host = "host-A", string? account = "account-A", string result = "Ok") =>
            new(("kind", kind), ("nonce", nonce), ("host", host), ("account", account), ("atUtc", now.AddSeconds(-age)), ("result", result));
        var fresh = new List<object> { Probe("auth", 30), Probe("control", 30) };
        Fresh Run(string? policy = LiveLaneAttestation.Bounded24h, DateTimeOffset? executedAt = null, LiveLaneBinding? candidate = null, LiveLaneBinding? executed = null,
            List<object>? probes = null, List<object>? revocations = null, bool noCandidate = false)
        {
            try
            {
                return new Fresh(LiveLaneAttestation.Freshness(policy, executedAt ?? now.AddHours(-1), now,
                    noCandidate ? null! : candidate ?? new LiveLaneBinding { Nonce = "nonce-A", Host = "host-A", Account = "account-A" },
                    executed ?? new LiveLaneBinding { Epoch = epoch, Host = "host-A", Account = "account-A", ReceiptId = "receipt-A" }, probes ?? fresh, revocations ?? []), null);
            }
            catch (LiveLaneRefusal refusal) { return new Fresh(null, refusal.Message); }
        }
        var r = Run(policy: null);
        w.Cases.Check("always-rerun-default", r.Refuses("policy-always-rerun"), r.Detail);
        r = Run(executed: new LiveLaneBinding { Epoch = null, Host = "host-A", Account = "account-A", ReceiptId = "receipt-A" }, probes: []);
        w.Cases.Check("no-epoch", r.Refuses("external-epoch-unknown"), r.Detail);
        r = Run(executedAt: now.AddSeconds(-(24 * 3600 - 1)));
        w.Cases.Check("bounded-fresh", r.Value is { ReuseEligible: true } && r.Value.Reasons.Contains("d6-bounded-exception") && !r.Value.ExternalProof, r.Detail);
        var a = Run(executedAt: now.AddHours(-24));
        var b = Run(executedAt: now.AddSeconds(-(24 * 3600 + 1)));
        w.Cases.Check("expired", a.Refuses("reuse-expired") && b.Refuses("reuse-expired"), a.Detail + " / " + b.Detail);
        a = Run(executedAt: now.AddSeconds(121));
        b = Run(executedAt: now.AddSeconds(119));
        w.Cases.Check("future-skew", a.Refuses("clock-skew") && b.Value is { ReuseEligible: true }, a.Detail + " / " + b.Detail);
        r = Run(probes: [Probe("auth", 30), Probe("control", 61)]);
        w.Cases.Check("probe-stale", r.Refuses("probe-stale:control"), r.Detail);
        r = Run(probes: [Probe("auth", 30, "nonce-B"), Probe("control", 30, "nonce-B")]);
        w.Cases.Check("probe-foreign-nonce", r.Refuses("probe-foreign"), r.Detail);
        r = Run(probes: [Probe("auth", 30, result: "AuthExpired"), Probe("control", 30)]);
        w.Cases.Check("probe-failed", r.Refuses("probe-failed:auth"), r.Detail);
        // The horizon is fixed: the rule has no parameter for it (the script refused -MaxAgeHours horizon-not-configurable).
        var parameters = typeof(LiveLaneAttestation).GetMethod(nameof(LiveLaneAttestation.Freshness))!.GetParameters().Select(p => p.Name!).ToArray();
        w.Cases.Check("horizon-fixed", !parameters.Any(p => p.Contains("age", StringComparison.OrdinalIgnoreCase) || p.Contains("horizon", StringComparison.OrdinalIgnoreCase))
            && LiveLaneAttestation.ReuseHorizon == TimeSpan.FromHours(24), string.Join(",", parameters));
        a = Run(probes: [Probe("auth", 30, host: "host-B"), Probe("control", 30, host: "host-B")]);
        b = Run(probes: [Probe("auth", 30, account: "account-B"), Probe("control", 30, account: "account-B")]);
        w.Cases.Check("host-account", a.Refuses("host-mismatch") && b.Refuses("account-mismatch") && !a.Value!.Reasons.Contains("account-mismatch"), a.Detail + " / " + b.Detail);
        a = Run(revocations: [new LiveLaneMap(("epoch", epoch))]);
        b = Run(revocations: [new LiveLaneMap(("receiptId", "receipt-A"))]);
        var c = Run(revocations: [new LiveLaneMap(("receiptId", "receipt-Z"))]);
        w.Cases.Check("incident-revoked", a.Refuses("revoked") && b.Refuses("revoked") && c.Value is { ReuseEligible: true }, a.Detail + " / " + b.Detail + " / " + c.Detail);
        // A-1: a missing nonce, host or account is unknown, never equal (missing == missing is not a binding).
        a = Run(candidate: new LiveLaneBinding(), executed: new LiveLaneBinding { Epoch = epoch }, probes: [Probe("auth", 30, null, null, null), Probe("control", 30, null, null, null)]);
        b = Run(executed: new LiveLaneBinding { Epoch = epoch, ReceiptId = "receipt-A" });
        c = Run(probes: [Probe("auth", 30, host: null), Probe("control", 30)]);
        var d = Run(noCandidate: true);
        w.Cases.Check("binding-missing", a.Refuses("candidate-binding-missing") && a.Refuses("executed-binding-missing") && a.Refuses("probe-binding-missing:auth")
            && b.Refuses("executed-binding-missing") && !b.Value!.Reasons.Contains("candidate-binding-missing")
            && c.Refuses("probe-binding-missing:auth") && !c.Value!.Reasons.Contains("probe-binding-missing:control") && d.Refuses("candidate-binding-missing"),
            a.Detail + " / " + b.Detail + " / " + c.Detail + " / " + d.Detail);
        // A-4: a malformed probe time refuses under a fixed token and never echoes the value.
        var bad = Probe("auth", 30);
        bad["atUtc"] = "not-a-time-" + Sentinel;
        r = Run(probes: [bad, Probe("control", 30)]);
        var missing = Probe("control", 30);
        missing["atUtc"] = null;
        var m = Run(probes: [Probe("auth", 30), missing]);
        w.Cases.Check("probe-time-token", r.Refuses("probe-time-invalid:auth") && !r.Detail.Contains(Sentinel) && m.Refuses("probe-time-invalid:control"), r.Detail + " / " + m.Detail);
        // A-6: the executed host and account are compared on their own, with candidate and probes equal.
        a = Run(executed: new LiveLaneBinding { Epoch = epoch, Host = "host-B", Account = "account-A", ReceiptId = "receipt-A" });
        b = Run(executed: new LiveLaneBinding { Epoch = epoch, Host = "host-A", Account = "account-B", ReceiptId = "receipt-A" });
        w.Cases.Check("executed-host-account", a.Refuses("host-mismatch") && !a.Value!.Reasons.Contains("account-mismatch")
            && b.Refuses("account-mismatch") && !b.Value!.Reasons.Contains("host-mismatch"), a.Detail + " / " + b.Detail);
    }, "always-rerun-default", "no-epoch", "bounded-fresh", "expired", "future-skew", "probe-stale", "probe-foreign-nonce", "probe-failed", "horizon-fixed", "host-account",
        "incident-revoked", "binding-missing", "probe-time-token", "executed-host-account");

    internal sealed record Attested(LiveLaneEnvironment? Value, string? Error);


    private sealed record Fresh(LiveLaneFreshness? Value, string? Error)
    {
        public bool Refuses(string reason) => Value is { ReuseEligible: false } && Value.Reasons.Contains(reason);
        public string Detail => "eligible=" + Value?.ReuseEligible + " reasons=" + string.Join(",", Value?.Reasons ?? []) + " error=" + Error;
    }

    internal sealed record StatusAnswer(int Exit, LiveLaneMap Document, string Text);

    /// <summary>One method's documents (exported from the real S1 objects), results directory and case ids.</summary>
    private sealed class World(string method, string documents, string results)
    {
        public C1545Cases Cases { get; } = new(method);
        public string Documents { get; } = documents;
        public string Results { get; } = results;
        public string[] Uids { get; } = JsonNode.Parse(File.ReadAllText(Path.Combine(documents, "base.json")))!["rows"]!.AsArray().Select(r => (string)r!["uid"]!).ToArray();

        public string Doc(string name) => Path.Combine(Documents, name + ".json");
        public JsonNode Json(string name) => JsonNode.Parse(File.ReadAllText(Doc(name)))!;

        public string Forge(string name, Action<JsonNode> edit)
        {
            var document = Json(name);
            edit(document);
            var path = Path.Combine(Results, "forged-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(path, document.ToJsonString());
            return path;
        }

        public Attested Att(string nameOrPath, bool bound = true)
        {
            var path = Path.IsPathRooted(nameOrPath) ? nameOrPath : Doc(nameOrPath);
            try { return new Attested(LiveLaneAttestation.Environment(path, Lane, bound ? Roster : null), null); }
            catch (LiveLaneRefusal refusal) { return new Attested(null, refusal.Message); }
        }

        /// <summary>The status front door (live-lane-status) on a root that is no repository, with this attestation.</summary>
        public StatusAnswer Status(string attestation)
        {
            var result = LiveLaneStatus.Lane(LiveLaneTestSources.Create(), LiveLaneStatusOptions.Of(Results, Lane, new string('0', 40), attestation));
            return new StatusAnswer(result.ExitCode, result.Document, LiveLaneData.Canonical(result.Document));
        }
    }

    private static void Run(string method, Action<World> body, params string[] expected)
    {
        var documents = LiveClaudeProfileFixture.CleanTempSubdirectory("c1545-attestation-");
        using var scratch = new LaneScratch("c1545-attestation-results-");
        var results = scratch.Root;
        try
        {
            LiveLaneAttestationExport.WriteAll(documents);
            var world = new World(method, documents, results);
            body(world);
            world.Cases.Complete(expected);
        }
        finally
        {
            Directory.Delete(documents, true);
        }
    }

    internal static bool Refused(StatusAnswer status, string reason) =>
        status.Exit == 2 && LiveLaneData.Same(status.Document["error"], reason) && status.Document["gateCredit"] is false;

    private static bool Differs(Attested a, Attested b, params string[] components)
    {
        if (a.Value?.Status != "complete" || b.Value?.Status != "complete" || a.Value.Digest is not { Length: 64 } || a.Value.Digest == b.Value.Digest) return false;
        return components.All(name => a.Value.Components?[name] is string mine && mine != b.Value.Components?[name] as string);
    }

    private static bool Same(Attested a, Attested b) => a.Value?.Status == "complete" && a.Value.Digest is { Length: 64 } && a.Value.Digest == b.Value?.Digest;

    // Unknown means the whole document: no digest and no component survives a refused row.
    private static bool Unknown(Attested att, string reason) =>
        att.Value is { Status: "unknown" } r && r.Reasons.Contains(reason) && r.Digest == null && r.Components == null;

    private static string Show(Attested att) => "status=" + att.Value?.Status + " reasons=" + string.Join(",", att.Value?.Reasons ?? []) + " error=" + att.Error;

    private static string? Str(JsonNode node, params object[] path)
    {
        JsonNode? current = node;
        foreach (var step in path) current = step is int i ? current?[i] : current?[(string)step];
        return (string?)current;
    }

    /// <summary>A minimal fingerprint fixture, so the environment component is witnessed through the lane fingerprint.</summary>
    private static (string Root, string Sha) LaneFixture(World w)
    {
        var repo = new LiveLaneRepo(Path.Combine(w.Results, "lane repository"));
        repo.Put("tests/rc-project-categories.json", "{\"schemaVersion\":1,\"projects\":{\"tests/Pty.Tests/Pty.Tests.csproj\":[\"fixture\"]},\"batches\":[\"tests/Pty.Tests/Pty.Tests.csproj\"]}");
        repo.Put("tests/Pty.Tests/Pty.Tests.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"TUnit\" Version=\"1.0\" /></ItemGroup></Project>");
        repo.Put("tests/Pty.Tests/T.cs", "namespace Antiphon.Agents.Pty.Tests;\n");
        repo.Put("scripts/lib/live-lane-contract.ps1", "# contract\n");
        repo.Put("CLAUDE.md", "# fixture\n");
        repo.Put("tests/live-lanes.json", LiveLaneData.Canonical(LiveLaneLaneFixture.Manifest(Roster)));
        repo.Put("tests/live-lane-inputs.json", "{\"format\":\"antiphon-live-lane-inputs-v1\",\"lanes\":{\"fixture-lane\":{\"manifest\":\"tests/live-lanes.json\","
            + "\"contractFiles\":[\"tests/live-lanes.json\",\"tests/live-lane-inputs.json\"],\"minimumRoots\":[\"tests/Pty.Tests/\"],\"runtimeRoots\":[\"scripts/\"],"
            + "\"namespaceRoots\":[\"Antiphon.Agents.Pty\"],\"instructionPatterns\":[\"CLAUDE.md\"]}}}");
        return (repo.Root, repo.Commit());
    }

    private static LiveLaneFingerprintResult Fp((string Root, string Sha) fixture, string? attestation) =>
        LiveLaneFingerprint.Compute(LiveLaneTestSources.Create(), fixture.Root, fixture.Sha, Lane, attestation);

    // A directory link: a junction on Windows (no privilege needed), a symbolic link elsewhere.
    internal static void DirectoryLink(string link, string target)
    {
        if (!OperatingSystem.IsWindows())
        {
            Directory.CreateSymbolicLink(link, target);
            return;
        }
        var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var argument in new[] { "/d", "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(link)) throw new InvalidOperationException("mklink /J failed: " + process.StandardError.ReadToEnd());
    }
}
