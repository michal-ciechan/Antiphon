using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
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
