using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S8a: the forced-Full policy, in-process (moved from test-rc-forced-full.ps1). Every input contract field
/// is mandatory and reviewed; unproven baseline, admission chain, history, ownership or shadow evidence forces Full;
/// a Full anchor ages out after seven days and is never renewed by a carry; and only a verified, complete,
/// all-executed Full closure becomes a new anchor, keeping its pre-run reasons as history.
/// </summary>
[Category("Unit")]
[Timeout(60_000)]
public sealed class RcForcedFullTests
{
    // CARD-1836 S3 (CP-3, V-3, G-8..G-11): trust releases holds only on executed clean own tests plus equal B/A contracts.
    [Test]
    public void Trust_requires_executed_own_tests_and_equal_contracts()
    {
        const string Messaging = "src/Messaging/Messaging.csproj";
        string Policy(params object[] entries) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = "antiphon-rc-trusted-projects-v1", ["schema"] = 1, ["entries"] = entries,
        });
        object Entry(string project, bool tests = true, bool contract = true) => new Dictionary<string, object?>
        {
            ["project"] = project, ["configurations"] = new[] { "Release|x64" },
            ["ownedTests"] = tests ? new object[] { new Dictionary<string, object> { ["chunk"] = "MessagingTests", ["cases"] = new[] { "m1", "m2" } } } : Array.Empty<object>(),
            ["downstream"] = new[] { "src/Server/Server.csproj", "src/Runner/Runner.csproj" }, ["contractProducer"] = contract ? "antiphon-contract-v1" : null,
        };
        var trusted = Policy(Entry(Messaging));
        var empty = Policy();
        RcTrustChunk[] chunks =
        [
            new() { Id = "MessagingTests", Project = "tests/Messaging/Messaging.Tests.csproj", Consumes = [Messaging] },
            new() { Id = "ServerTests", Project = "tests/Server/Server.Tests.csproj", Consumes = ["src/Server/Server.csproj", Messaging] },
            new() { Id = "RunnerTests", Project = "tests/Runner/Runner.Tests.csproj", Consumes = ["src/Runner/Runner.csproj", Messaging] },
            new() { Id = "OtherTests", Project = "tests/Other/Other.Tests.csproj", Consumes = ["src/Other/Other.csproj"] },
        ];
        string[] privateChange = ["src/Messaging/Internal/Queue.cs"];
        RcOwnResult Own(string result = "pass", string disposition = "executed", int skipped = 0, bool timedOut = false, string[]? cases = null) => new()
        {
            Chunk = "MessagingTests", Disposition = disposition, Result = result, Skipped = skipped, TimedOut = timedOut, PassedCases = cases ?? ["m1", "m2"],
        };
        RcContractReceipt Contract(string side, string hash) => new() { Project = Messaging, Side = side, ExtractorVersion = "contract-v1", ContractHash = hash };
        RcContractReceipt[] equal = [Contract("B", "h1"), Contract("A", "h1")];
        RcTrustDecision Decide(RcOwnResult[]? own, RcContractReceipt[]? contracts = null, string? policyB = null, string? policyA = null, string[]? paths = null,
            RcTrustChunk[]? with = null) =>
            RcTrustedProjectGate.Decide(policyB ?? trusted, policyA ?? trusted, paths ?? privateChange, with ?? chunks, own, contracts ?? equal);
        string Show(RcTrustDecision d) => string.Join(" ", d.Chunks.Select(c => c.Chunk + "=" + c.Decision + "[" + string.Join(",", c.Reasons) + "]"))
            + " reasons=" + string.Join(",", d.Reasons) + " refused=" + string.Join(",", d.Refused);
        bool Expanded(RcTrustDecision d, string why) => new[] { "ServerTests", "RunnerTests" }.All(id =>
            d.Of(id).Decision == "execute" && d.Of(id).Reasons.Any(r => r == "trust-gate-failed:" + Messaging + ":" + why));
        var rows = new NightlyCaseRows();

        var first = Decide(null);
        rows.Check(first.Of("MessagingTests").Decision == "execute" && first.Of("ServerTests").Decision == "held" && first.Of("RunnerTests").Decision == "held"
            && first.Of("OtherTests").Decision == "carry" && first.Mode == "Scoped", "s3-hold-only-dependents", Show(first));

        var release = Decide([Own()]);
        rows.Check(new[] { "ServerTests", "RunnerTests" }.All(id => release.Of(id).Decision == "carry" && release.Of(id).Reasons.SequenceEqual(["trust-gate-passed:" + Messaging]))
            && release.Of("MessagingTests").Decision == "execute", "s3-release", Show(release));

        var red = Decide([Own(result: "FAIL")]);
        rows.Check(Expanded(red, "own-red"), "s3-own-red", Show(red));
        var skip = Decide([Own(skipped: 1)]);
        rows.Check(Expanded(skip, "own-skip"), "s3-own-skip", Show(skip));
        var timeout = Decide([Own(result: "TIMEOUT", timedOut: true)]);
        rows.Check(Expanded(timeout, "own-timeout"), "s3-own-timeout", Show(timeout));
        var notRun = Decide([Own(disposition: "missing", result: "")]);
        var noRow = Decide([]);
        rows.Check(Expanded(notRun, "own-missing") && Expanded(noRow, "own-missing"), "s3-own-missing", Show(notRun));

        var drift = Decide([Own()], [Contract("B", "h1"), Contract("A", "h2")]);
        rows.Check(Expanded(drift, "contract-drift"), "s3-hash-drift", Show(drift));
        var absentA = Decide([Own()], [Contract("B", "h1")]);
        var absentBoth = Decide([Own()], [Contract("B", ""), Contract("A", "")]);
        rows.Check(Expanded(absentA, "contract-absent") && Expanded(absentBoth, "contract-absent"), "s3-hash-absent", Show(absentBoth));

        var edited = Policy(Entry(Messaging), Entry("src/Server/Server.csproj"));
        var edit = Decide([Own()], policyA: edited);
        rows.Check(edit.Reasons.Contains("trust-policy-changed") && new[] { "ServerTests", "RunnerTests" }.All(id => edit.Of(id).Decision == "execute")
            && edit.PolicyDigestA != edit.PolicyDigestB, "s3-policy-edit", Show(edit));

        var self = Decide([Own()], policyB: empty, policyA: trusted);
        rows.Check(new[] { "ServerTests", "RunnerTests" }.All(id => self.Of(id).Decision == "execute" && !self.Of(id).Reasons.Any(r => r.StartsWith("trust-gate-passed", StringComparison.Ordinal))),
            "s3-self-authorize", Show(self));

        var package = Decide([Own()], paths: [.. privateChange, "Directory.Packages.props"]);
        var sdk = Decide([Own()], paths: ["global.json"]);
        rows.Check(package.Mode == "Full" && package.Reasons.Contains("global-input:Directory.Packages.props") && sdk.Mode == "Full"
            && sdk.Chunks.All(c => c.Decision == "execute"), "s3-package-sdk", Show(package));

        var refusedPolicy = Policy(Entry("src/*/Messaging.csproj"), Entry("src/Server/Server.csproj", tests: false), Entry("Directory.Packages.props"),
            Entry("nuget:Newtonsoft.Json"), Entry("src/Runner/Runner.csproj", contract: false));
        var refusals = new List<string>();
        var admitted = RcTrustedProjectGate.Entries(refusedPolicy, refusals);
        rows.Check(admitted.Length == 0 && refusals.Contains("trust-entry-refused:src/*/Messaging.csproj:wildcard")
            && refusals.Contains("trust-entry-refused:src/Server/Server.csproj:no-owned-tests") && refusals.Contains("trust-entry-refused:Directory.Packages.props:external")
            && refusals.Contains("trust-entry-refused:nuget:Newtonsoft.Json:external") && refusals.Contains("trust-entry-refused:src/Runner/Runner.csproj:no-contract"),
            "s3-policy-refusals", string.Join(",", refusals));

        var shippedPath = Path.Combine(Antiphon.Tests.TestHelpers.C1514Support.RepoRoot(), "scripts", "lib", "rc-trusted-projects.json");
        var shippedRefusals = new List<string>();
        using (var shipped = JsonDocument.Parse(File.ReadAllText(shippedPath)))
            rows.Check(shipped.RootElement.GetProperty("format").GetString() == "antiphon-rc-trusted-projects-v1" && shipped.RootElement.GetProperty("entries").GetArrayLength() == 0
                && RcTrustedProjectGate.Entries(File.ReadAllText(shippedPath), shippedRefusals).Length == 0 && shippedRefusals.Count == 0, "s3-empty-shipped-policy");

        RcTrustChunk[] direct = [.. chunks.Select(c => c.Id == "ServerTests" ? new RcTrustChunk { Id = c.Id, Project = c.Project, Consumes = c.Consumes, DirectHit = true } : c)];
        var retained = Decide([Own()], with: direct);
        rows.Check(retained.Of("ServerTests").Decision == "execute" && retained.Of("ServerTests").Reasons.Contains("direct-hit") && retained.Of("RunnerTests").Decision == "carry",
            "s3-direct-hit-retained", Show(retained));

        // Review r1 D-5(a): only a consumer reached through a declared downstream edge is held; any other consumer executes.
        RcTrustChunk[] withWorker = [.. chunks, new() { Id = "WorkerTests", Project = "tests/Worker/Worker.Tests.csproj", Consumes = ["src/Worker/Worker.csproj", Messaging] }];
        var undeclaredFirst = Decide(null, with: withWorker);
        var undeclared = Decide([Own()], with: withWorker);
        rows.Check(undeclaredFirst.Of("WorkerTests").Decision == "execute" && undeclared.Of("WorkerTests").Decision == "execute"
            && undeclared.Of("WorkerTests").Reasons.Contains("consumer:" + Messaging + ":undeclared-edge")
            && undeclared.Of("ServerTests").Decision == "carry" && undeclared.Of("RunnerTests").Decision == "carry", "s3-undeclared-consumer", Show(undeclared));

        // Review r1 D-5(b): packages live in each .csproj here; a project file or an imported .props/.targets change voids trust.
        // Review r2 A-9 (N11): a voided project keeps no trust entry, so a gated decision that cannot resolve its gate is a failed row, not a crash.
        (RcTrustDecision? Decision, string Crash) Gated(Func<RcTrustDecision> decide)
        {
            try { return (decide(), ""); }
            catch (KeyNotFoundException e) { return (null, e.GetType().Name); }
        }
        var bumpFirst = Decide(null, paths: [.. privateChange, "src/Messaging/Messaging.csproj"]);
        var bump = Gated(() => Decide([Own()], paths: [.. privateChange, "src/Messaging/Messaging.csproj"]));
        var imported = Gated(() => Decide([Own()], paths: [.. privateChange, "build/Common.targets"]));
        bool Voided(RcTrustDecision? d) => d != null && new[] { "ServerTests", "RunnerTests" }.All(id => d.Of(id).Decision == "execute"
            && d.Of(id).Reasons.Any(r => r.StartsWith("consumer:" + Messaging + ":trust-void", StringComparison.Ordinal))
            && !d.Of(id).Reasons.Any(r => r.StartsWith("held:", StringComparison.Ordinal)));
        string ShowGated((RcTrustDecision? Decision, string Crash) g) => g.Decision == null ? "crash:" + g.Crash : Show(g.Decision);
        rows.Check(Voided(bump.Decision) && Voided(imported.Decision) && Voided(bumpFirst)
            && bump.Decision!.Reasons.Contains("trust-void:" + Messaging + ":src/Messaging/Messaging.csproj")
            && imported.Decision!.Reasons.Contains("trust-void:" + Messaging + ":build/Common.targets"), "s3-csproj-package-bump",
            ShowGated(bump) + " || " + ShowGated(imported) + " || first " + Show(bumpFirst));

        // M6: an own-test roster missing a listed case fails the gate.
        var caseMissing = Decide([Own(cases: ["m1"])]);
        rows.Check(Expanded(caseMissing, "own-case-missing"), "s3-own-case-missing", Show(caseMissing));

        rows.ShouldAllPass(17, "s3-hold-only-dependents", "s3-release", "s3-own-red", "s3-own-skip", "s3-own-timeout", "s3-own-missing", "s3-hash-drift",
            "s3-hash-absent", "s3-policy-edit", "s3-self-authorize", "s3-package-sdk", "s3-policy-refusals", "s3-empty-shipped-policy", "s3-direct-hit-retained",
            "s3-undeclared-consumer", "s3-csproj-package-bump", "s3-own-case-missing");
    }

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-13T12:00:00Z", CultureInfo.InvariantCulture);

    private static readonly string[] HashFields =
    [
        "policyHash", "catalogHash", "selectorHash", "skipResolverHash", "discoveryHash", "parserHash", "coverageHash", "launcherHash", "custodyHash",
        "buildHash", "harnessHash", "inputMapHash", "sdkHash", "runtimeHash",
    ];

    private static JsonObject Contract()
    {
        var contract = new JsonObject
        {
            ["format"] = "antiphon-rc-input-contract-v1", ["inputMapVersion"] = "1", ["sdkVersion"] = "10.0.204", ["runtimeVersion"] = "9.0.10",
            ["platform"] = "linux", ["executionImplementation"] = "compiled",
        };
        foreach (var field in HashFields) contract[field] = new string('a', 64);
        return contract;
    }

    private static JsonObject Evidence() => new()
    {
        ["baselineState"] = "admitted-ancestor", ["admissionChainState"] = "verified", ["historyState"] = "complete", ["globalOwnershipState"] = "resolved",
        ["shadowState"] = "clean",
    };

    private static JsonObject Anchor(string when = "2026-11-12T12:00:00Z") => new()
    {
        ["kind"] = "executed-full", ["admissionState"] = "verified", ["completedAtUtc"] = when, ["sourceSha"] = new string('1', 40),
        ["closureDigest"] = new string('a', 64),
    };

    /// <summary>The decision's arguments; a null anchor is no anchor at all.</summary>
    private sealed class Arguments
    {
        public JsonObject CurrentContract { get; } = Contract();
        public JsonObject BaselineContract { get; } = Contract();
        public JsonObject Evidence { get; } = RcForcedFullTests.Evidence();
        public JsonObject? FullAnchor { get; set; } = Anchor();
        public bool ForceFull { get; set; }

        public JsonObject Side(string side) => side == "CurrentContract" ? CurrentContract : BaselineContract;

        public string Text() => new JsonObject
        {
            ["current"] = CurrentContract.DeepClone(), ["baseline"] = BaselineContract.DeepClone(), ["evidence"] = Evidence.DeepClone(),
            ["anchor"] = FullAnchor?.DeepClone(), ["force"] = ForceFull,
        }.ToJsonString();
    }

    private static JsonElement Element(JsonNode node) => JsonSerializer.SerializeToElement(node);

    private static RcForcedFullDecision Decide(Arguments a) => RcForcedFull.Decision(Element(a.CurrentContract), Element(a.BaselineContract), Element(a.Evidence),
        a.FullAnchor == null ? default : Element(a.FullAnchor), Now, a.ForceFull);

    // Order, duplicates, missing and invented reasons are observable contract failures.
    private static void Expect(RcForcedFullDecision result, string[] reasons, string label)
    {
        result.ForceFull.ShouldBe(reasons.Length > 0, label + ": forceFull");
        result.LaunchAll.ShouldBe(reasons.Length > 0, label + ": launchAll");
        string.Join("|", result.Reasons).ShouldBe(string.Join("|", reasons), label + ": reasons=" + string.Join("|", result.Reasons));
    }

    private static JsonNode? Value(object? value) => value switch
    {
        null => null,
        string s => JsonValue.Create(s),
        bool b => JsonValue.Create(b),
        int i => JsonValue.Create(i),
        _ => throw new ArgumentException("fixture value " + value),
    };

    [Test]
    public void ContractOrUnknownForcesFull()
    {
        var arguments = new Arguments();
        Expect(Decide(arguments), [], "complete equal contract");
        var before = arguments.Text();
        // Every contract input is mandatory, even when absent or malformed on BOTH sides.
        foreach (var field in arguments.CurrentContract.Select(p => p.Key).ToArray())
        {
            var changed = new Arguments();
            changed.CurrentContract[field] = field switch
            {
                "format" => "antiphon-rc-input-contract-v2",
                "inputMapVersion" => "2",
                "sdkVersion" => "10.0.205",
                "runtimeVersion" => "9.0.11",
                "platform" => "windows",
                "executionImplementation" => "fallback",
                _ => new string('b', 64),
            };
            Expect(Decide(changed), [field == "format" ? "contract-current-unknown:format" : "contract-changed:" + field], "changed " + field);
            foreach (var side in new[] { "CurrentContract", "BaselineContract" })
            {
                var missing = new Arguments();
                missing.Side(side).Remove(field);
                var prefix = side == "CurrentContract" ? "current" : "baseline";
                Expect(Decide(missing), ["contract-" + prefix + "-unknown:" + field], "missing " + side + "." + field);
            }
            var unknown = new Arguments();
            unknown.CurrentContract[field] = null;
            unknown.BaselineContract[field] = null;
            Expect(Decide(unknown), ["contract-baseline-unknown:" + field, "contract-current-unknown:" + field], "unknown both " + field);
        }
        foreach (var bad in new Func<JsonNode?>[]
                 {
                     () => JsonValue.Create(""), () => JsonValue.Create(42), () => new JsonArray(JsonValue.Create("a")), () => JsonValue.Create(new string('x', 64)),
                     () => JsonValue.Create(new string('a', 64) + "\n"),
                 })
        {
            var invalid = new Arguments();
            invalid.CurrentContract["policyHash"] = bad();
            Expect(Decide(invalid), ["contract-current-unknown:policyHash"], "invalid hash type/value");
        }
        foreach (var (field, value, reason) in new[]
                 {
                     ("baselineState", "absent", "baseline-unproven"), ("baselineState", "unrelated", "baseline-unproven"),
                     ("admissionChainState", "corrupt", "admission-chain-unproven"), ("admissionChainState", "missing", "admission-chain-unproven"),
                     ("historyState", "missing", "history-unproven"), ("historyState", "corrupt", "history-unproven"),
                     ("globalOwnershipState", "unresolved", "global-ownership-unresolved"),
                     ("shadowState", "discrepancy", "shadow-discrepancy"), ("shadowState", "unknown", "shadow-unproven"),
                 })
        {
            var uncertain = new Arguments();
            uncertain.Evidence[field] = value;
            Expect(Decide(uncertain), [reason], field + ":" + value + ":" + reason);
            var missing = new Arguments();
            missing.Evidence.Remove(field);
            Expect(Decide(missing), [field == "shadowState" ? "shadow-unproven" : reason], "absent " + field);
        }
        var many = new Arguments { ForceFull = true };
        many.Evidence["historyState"] = "missing";
        many.CurrentContract["policyHash"] = new string('b', 64);
        Expect(Decide(many), ["contract-changed:policyHash", "explicit-force-full", "history-unproven"], "all sorted reasons");
        arguments.Text().ShouldBe(before, "policy mutated its input");
        // Forward-compatible extra fields must not silently escape this complete contract.
        var extra = new Arguments();
        extra.CurrentContract["unrecognizedInput"] = "new semantics";
        Expect(Decide(extra), ["contract-current-unknown:unrecognizedInput"], "unrecognized contract field");
    }

    [Test]
    public void AnchorAgeCannotRenewThroughCarry()
    {
        foreach (var (when, reason) in new[]
                 {
                     ("2026-11-06T12:00:00.0000001Z", ""), ("2026-11-06T12:00:00Z", "full-anchor-expired"),
                     ("2026-11-06T11:59:59.9999999Z", "full-anchor-expired"), ("2026-11-13T12:00:00.0000001Z", "full-anchor-future"),
                     ("", "full-anchor-time-unknown"), ("yesterday", "full-anchor-time-unknown"), ("2026-11-12T12:00:00", "full-anchor-time-unknown"),
                 })
        {
            var arguments = new Arguments { FullAnchor = Anchor(when) };
            Expect(Decide(arguments), reason.Length == 0 ? Array.Empty<string>() : new[] { reason }, "anchor time " + when);
        }
        Expect(Decide(new Arguments { FullAnchor = null }), ["full-anchor-unproven"], "no anchor");
        foreach (var field in new[] { "kind", "admissionState", "sourceSha", "closureDigest" })
        {
            var missing = new Arguments();
            missing.FullAnchor!.Remove(field);
            Expect(Decide(missing), ["full-anchor-unproven"], "anchor missing " + field);
        }
        foreach (var field in new[] { "sourceSha", "closureDigest" })
        {
            var invalid = new Arguments();
            invalid.FullAnchor![field] = (string)invalid.FullAnchor[field]! + "\n";
            Expect(Decide(invalid), ["full-anchor-unproven"], "anchor malformed " + field);
        }
        var carry = new Arguments { FullAnchor = Anchor("2026-11-01T12:00:00Z") };
        carry.Evidence["candidateCompletedAtUtc"] = "2026-11-13T11:59:00Z";
        carry.Evidence["releaseCreatedAtUtc"] = "2026-11-13T11:59:00Z";
        carry.Evidence["sampleCompletedAtUtc"] = "2026-11-13T11:59:00Z";
        var original = carry.FullAnchor!.DeepClone();
        var decision = Decide(carry);
        Expect(decision, ["full-anchor-expired"], "carry/release/sample cannot renew");
        JsonNode.DeepEquals(JsonNode.Parse(decision.FullAnchor.GetRawText()), original).ShouldBeTrue("original Full anchor not retained");
        carry.FullAnchor["kind"] = "carried";
        Expect(Decide(carry), ["full-anchor-unproven"], "carried result is not Full");
    }

    private static JsonObject Closure() => new()
    {
        ["validationState"] = "verified", ["mode"] = "Full", ["coverageComplete"] = true, ["requiredCount"] = 12, ["executedCount"] = 10,
        ["expectedSkipCount"] = 2, ["carriedCount"] = 0, ["failedCount"] = 0, ["unexpectedSkipCount"] = 0, ["completedAtUtc"] = "2026-11-13T11:59:00Z",
        ["sourceSha"] = new string('2', 40), ["closureDigest"] = new string('b', 64),
        ["forcedFull"] = new JsonArray(JsonValue.Create("explicit-force-full"), JsonValue.Create("full-anchor-expired")),
    };

    [Test]
    public void ExecutedFullCanCloseWithReasons()
    {
        var closure = Closure();
        var before = closure.ToJsonString();
        var result = RcForcedFull.ExecutedFullAnchor(Element(closure), Now);
        (result.Eligible && result.Reasons.Length == 0).ShouldBeTrue("fresh all-executed Full rejected because of pre-run reasons");
        string.Join("|", result.ForcedFull.Select(r => r.GetString())).ShouldBe("explicit-force-full|full-anchor-expired", "pre-run reasons erased");
        result.FullAnchor.ShouldNotBeNull("no new anchor");
        result.FullAnchor["completedAtUtc"].ShouldBe("2026-11-13T11:59:00Z", "anchor time not execution time");
        (result.FullAnchor["sourceSha"] == new string('2', 40) && result.FullAnchor["closureDigest"] == new string('b', 64)).ShouldBeTrue("new anchor lost origin");
        var next = new Arguments { FullAnchor = JsonNode.Parse(RcForcedFull.ToElement(result.FullAnchor).GetRawText())!.AsObject() };
        Expect(Decide(next), [], "newly executed Full resets age");
        closure.ToJsonString().ShouldBe(before, "closure mutated");
        foreach (var (field, value, reason) in new (string, object?, string)[]
                 {
                     ("mode", "Incremental", "closure-not-full"), ("validationState", "unproven", "closure-unproven"),
                     ("coverageComplete", false, "closure-incomplete"), ("coverageComplete", "true", "closure-incomplete"),
                     ("carriedCount", 1, "closure-has-carries"), ("executedCount", 9, "closure-membership-incomplete"),
                     ("failedCount", 1, "closure-failed"), ("unexpectedSkipCount", 1, "closure-unexpected-skip"),
                     ("executedCount", -1, "closure-counts-unknown"), ("executedCount", "10", "closure-counts-unknown"),
                     ("requiredCount", 0, "closure-counts-unknown"), ("sourceSha", "label", "closure-origin-unknown"),
                     ("sourceSha", new string('2', 40) + "\n", "closure-origin-unknown"),
                     ("closureDigest", new string('b', 64) + "\n", "closure-origin-unknown"),
                     ("closureDigest", null, "closure-origin-unknown"), ("completedAtUtc", "2026-11-14T00:00:00Z", "closure-time-future"),
                 })
        {
            var bad = Closure();
            bad[field] = Value(value);
            var refused = RcForcedFull.ExecutedFullAnchor(Element(bad), Now);
            (refused.Eligible == false && refused.FullAnchor == null).ShouldBeTrue("ineligible anchor: " + field);
            string.Join("|", refused.Reasons).ShouldBe(reason, "closure reason " + field + ": " + string.Join("|", refused.Reasons));
        }
        foreach (var field in new[] { "requiredCount", "executedCount", "expectedSkipCount", "carriedCount", "failedCount", "unexpectedSkipCount" })
        {
            var bad = Closure();
            bad.Remove(field);
            var refused = RcForcedFull.ExecutedFullAnchor(Element(bad), Now);
            (!refused.Eligible && string.Join("|", refused.Reasons) == "closure-counts-unknown").ShouldBeTrue("missing count " + field);
        }
        var noTime = Closure();
        noTime["completedAtUtc"] = null;
        var timeless = RcForcedFull.ExecutedFullAnchor(Element(noTime), Now);
        (!timeless.Eligible && string.Join("|", timeless.Reasons) == "closure-time-unknown").ShouldBeTrue("missing execution time");
    }
}
