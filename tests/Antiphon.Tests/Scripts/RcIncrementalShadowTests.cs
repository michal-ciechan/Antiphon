using System.Collections;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S8b: the fixed-seed shadow sample, contract mismatch and trust refusal, in-process (moved from
/// test-rc-shadow-sample.ps1). No qualification launch, publish, promote or external service. Inputs are plain
/// dictionaries, arrays and scalars; JSON round trips are parsed into those (objects become ordinal dictionaries,
/// arrays object[], integers long), which is how RcPort reads parsed JSON.
/// </summary>
[Category("Unit")]
[Timeout(60_000)]
public sealed class RcIncrementalShadowTests
{
    private static readonly string SeedA = string.Concat(Enumerable.Repeat("0123456789abcdef", 4));
    private static readonly string SeedB = string.Concat(Enumerable.Repeat("fedcba9876543210", 4));
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-13T12:00:00Z", CultureInfo.InvariantCulture);
    private static readonly string Digest = new('a', 64);

    private static readonly string[] HashFields =
    [
        "policyHash", "catalogHash", "selectorHash", "skipResolverHash", "discoveryHash", "parserHash", "coverageHash", "launcherHash",
        "custodyHash", "buildHash", "harnessHash", "inputMapHash", "sdkHash", "runtimeHash",
    ];

    private readonly List<string> errors = [];

    private void Note(bool ok, string why)
    {
        if (!ok) errors.Add(why);
    }

    private void Contract(string name) =>
        errors.ShouldBeEmpty("RS " + name + ".contract: " + string.Join("; ", errors));

    private static Dictionary<string, object?> Map() => new(StringComparer.Ordinal);

    private static string JoinIds(object? value)
    {
        if (value == null) return "";
        if (value is string text) return text;
        if (value is not IEnumerable items) return Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        var parts = new List<string>();
        foreach (var item in items)
            if (item != null) parts.Add(Convert.ToString(item, CultureInfo.InvariantCulture) ?? "");
        return string.Join("|", parts);
    }

    private static string Serialize(object? value) => JsonSerializer.Serialize(value);

    /// <summary>JSON text as plain values: an object is an ordinal dictionary, an array object[], an integer long.</summary>
    private static object? Json(string text) => Plain(JsonNode.Parse(text));

    private static object? Plain(JsonNode? node) => node switch
    {
        null => null,
        JsonObject obj => obj.ToDictionary(pair => pair.Key, pair => Plain(pair.Value), StringComparer.Ordinal),
        JsonArray array => array.Select(Plain).ToArray(),
        JsonValue value => value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetValue<long>(out var whole) ? (object)whole : value.GetValue<double>(),
            var kind => throw new InvalidOperationException("json kind " + kind),
        },
        _ => throw new InvalidOperationException("json node " + node.GetType().Name),
    };

    private static RcShadowProposal Proposal(object[] chunks, string seed, string proposalPath = "") =>
        RcShadowSample.Proposal(chunks, seed, proposalPath, RcInputFixtureRepository.SourceRoot);

    private static bool Eligible(object pilot, string contractDigest) => RcShadowSample.PilotEligible(pilot, contractDigest);

    private static RcShadowTrust Trust(
        object? currentContract = null, object? baselineContract = null, object? evidence = null, object? fullAnchor = null, DateTimeOffset? utcNow = null,
        object?[]? pilots = null, object?[]? carries = null, object? sampleFailure = null, bool rosterChanged = false, object? operatorTrustRecord = null,
        string currentSample = "", string contractDigest = "", string trustRecordPath = "") =>
        RcShadowSample.Trust(currentContract, baselineContract, evidence, fullAnchor, utcNow ?? DateTimeOffset.MinValue, pilots ?? [], carries ?? [],
            sampleFailure, rosterChanged, operatorTrustRecord, currentSample, contractDigest, trustRecordPath);

    private static string FieldText(object? source, string name) => RcShadowSample.Field(source, name) as string ?? "";

    private static Dictionary<string, object?> Chunk(string id, string suite, string boundary) => new(StringComparer.Ordinal)
    {
        ["id"] = id, ["suite"] = suite, ["boundary"] = boundary,
    };

    private static object[] PinnedChunks()
    {
        var items = new List<object>();
        for (var i = 0; i <= 15; i++)
        {
            var chunk = Chunk("c" + i.ToString("d2", CultureInfo.InvariantCulture), "alpha", "north");
            chunk["duration"] = 1;
            chunk["passed"] = true;
            chunk["completelyCarryable"] = true;
            items.Add(chunk);
        }
        for (var i = 16; i <= 30; i++)
        {
            var chunk = Chunk("c" + i.ToString("d2", CultureInfo.InvariantCulture), "alpha", "south");
            chunk["duration"] = 100000;
            chunk["passed"] = false;
            chunk["completelyCarryable"] = true;
            items.Add(chunk);
        }
        foreach (var (id, suite, boundary) in new[] { ("skip-a", "alpha", "north"), ("skip-b", "beta", "west") })
        {
            var skip = Chunk(id, suite, boundary);
            skip["expectedSkipOnly"] = true;
            skip["duration"] = 1;
            skip["passed"] = true;
            items.Add(skip);
        }
        return items.ToArray();
    }

    private static object[] Population(int count, int strata)
    {
        var items = new object[count];
        for (var i = 0; i < count; i++)
        {
            var chunk = Chunk("p" + i.ToString("d4", CultureInfo.InvariantCulture), "suite", "b" + (i % strata).ToString("d2", CultureInfo.InvariantCulture));
            chunk["duration"] = i % 2 == 0 ? 1 : 100000;
            chunk["passed"] = i % 3 == 0;
            chunk["completelyCarryable"] = true;
            items[i] = chunk;
        }
        return items;
    }

    private static string[] ExpectedIds(string omitted) =>
        Enumerable.Range(0, 31).Select(i => "c" + i.ToString("d2", CultureInfo.InvariantCulture)).Where(id => id != omitted).ToArray();

    private static Dictionary<string, object?> NewContract()
    {
        var contract = Map();
        contract["format"] = "antiphon-rc-input-contract-v1";
        contract["inputMapVersion"] = "1";
        contract["sdkVersion"] = "10.0.204";
        contract["runtimeVersion"] = "9.0.10";
        contract["platform"] = "linux";
        contract["executionImplementation"] = "compiled";
        foreach (var field in HashFields) contract[field] = new string('a', 64);
        return contract;
    }

    private static Dictionary<string, object?> NewEvidence() => new(StringComparer.Ordinal)
    {
        ["baselineState"] = "admitted-ancestor", ["admissionChainState"] = "verified", ["historyState"] = "complete", ["globalOwnershipState"] = "resolved",
        ["shadowState"] = "clean",
    };

    private static Dictionary<string, object?> NewAnchor() => new(StringComparer.Ordinal)
    {
        ["kind"] = "executed-full", ["admissionState"] = "verified", ["completedAtUtc"] = "2026-11-12T12:00:00Z", ["sourceSha"] = new string('1', 40),
        ["closureDigest"] = new string('b', 64),
    };

    private static Dictionary<string, object?> NewPilot(string cutId, string platform = "windows", string sample = "executed")
    {
        var executed = sample == "executed";
        return new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cutId"] = cutId, ["platform"] = platform, ["sample"] = sample, ["shadowPilot"] = executed, ["discrepancy"] = false, ["openFailure"] = false,
            ["nativeReceipts"] = "current", ["fullOverdue"] = false, ["contractDigest"] = Digest, ["n"] = executed ? 30 : 0,
            ["nExecuted"] = executed ? 30 : 0, ["cchunk"] = 600, ["stratumCount"] = 1,
        };
    }

    private static Dictionary<string, object?> NewCountedPilot(
        string cutId, int planned = 30, int executed = 30, string digest = "USE", int cchunk = 600, int strata = 1, bool omitPlan = false)
    {
        if (digest == "USE") digest = Digest;
        var pilot = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["cutId"] = cutId, ["platform"] = "windows", ["sample"] = "executed", ["shadowPilot"] = true, ["discrepancy"] = false, ["openFailure"] = false,
            ["nativeReceipts"] = "current", ["fullOverdue"] = false, ["contractDigest"] = digest, ["nExecuted"] = executed,
        };
        if (!omitPlan)
        {
            pilot["n"] = planned;
            pilot["cchunk"] = cchunk;
            pilot["stratumCount"] = strata;
        }
        return pilot;
    }

    private static Dictionary<string, object?> Result(string chunkId, string outcome, string provenance = "verified") => new(StringComparer.Ordinal)
    {
        ["chunkId"] = chunkId, ["outcome"] = outcome, ["provenance"] = provenance,
    };

    private static Dictionary<string, object?> Evidence(object? sampled, object? results)
    {
        var evidence = Map();
        evidence["sampled"] = sampled;
        evidence["results"] = results;
        return evidence;
    }

    private static Dictionary<string, object?> ClosedFlagEvidence(string field, object? value)
    {
        var row = Result("c1", "passed");
        // Assign even when the value is null so the key is present.
        row[field] = value;
        return Evidence(new object[] { "c1" }, new object[] { row });
    }

    private static string OwnedRoot() => OwnedTempRoot.CreateOwned();

    private void AssertDerivedFailure(string label, object? evidence, string kind, string chunkId, string trustPath)
    {
        var decision = Trust(carries: ["c2", "c1"], evidence: evidence, currentSample: "roster", trustRecordPath: trustPath);
        Note(decision.CarriesInvalidated, label + " left carries valid");
        Note(JoinIds(decision.InvalidatedCarries) == "c1|c2", label + " carries=" + JoinIds(decision.InvalidatedCarries));
        Note(JoinIds(decision.Reasons) == "sample-failed", label + " reasons=" + JoinIds(decision.Reasons));
        Note(!decision.TrustGranted && !decision.GateCredit && decision.EffectiveMode == "Full" && decision.IncrementalValidation == "Shadow", label + " shadow flags");
        Note(!decision.ExpandedToFull && !decision.TrustRecordWritten, label + " wrote trust or expanded");
        Note(!File.Exists(trustPath), label + " created a trust record");
        var foundKind = FieldText(decision.PreservedSampleFailure, "kind");
        var foundChunk = FieldText(decision.PreservedSampleFailure, "chunkId");
        Note(foundKind == kind, label + " kind=" + foundKind);
        if (!string.IsNullOrEmpty(chunkId)) Note(foundChunk == chunkId, label + " chunk=" + foundChunk);
    }

    // The flags are typed bool, so the PowerShell -is [bool] half of each check is the type system's.
    private void AssertProposalShape(RcShadowProposal result, string label)
    {
        Note(result.Format == "antiphon-rc-shadow-proposal-v1", label + ": format");
        Note(result.EffectiveMode == "Full", label + ": effectiveMode");
        Note(!result.GateCredit, label + ": gateCredit");
        Note(result.BindingState == "proposal-only", label + ": bindingState");
        Note(result.IncrementalValidation == "Shadow", label + ": incrementalValidation");
        Note(!result.TrustGranted, label + ": trustGranted");
        Note(!result.TrustRecordWritten, label + ": trustRecordWritten");
        Note(!result.PilotClaim, label + ": pilotClaim");
        Note(!result.Launched, label + ": launched");
        Note(!result.ExpandedToFull, label + ": expandedToFull");
    }

    private void AssertOffSwitch()
    {
        const string modeLine = "    [ValidateSet('Full', 'Shadow', 'ChangeAware')][string]$SelectionMode = 'Full',";
        foreach (var name in new[] { "release-cut.ps1", "nightly-run.ps1" })
        {
            var lines = File.ReadAllLines(Path.Combine(RcInputFixtureRepository.SourceRoot, "scripts", name));
            Note(lines.Any(line => line == modeLine), "mode list " + name);
        }
        // W2-nightly S17: the nightly-tests command's mode list (scripts/nightly-tests.ps1 is deleted): the same three, Full by default.
        Note(NightlyTestsOptions.SelectionModes.SequenceEqual(["Full", "Shadow", "ChangeAware"]) && new NightlyTestsOptions().SelectionMode == "Full", "mode list nightly-tests"); 
        var sha = new string('a', 40);
        Dictionary<string, object?> State(string scope) => new(StringComparer.Ordinal)
        {
            ["profile"] = "rc", ["trigger"] = "rc", ["ref"] = "rc/2026-11-19", ["sha"] = sha, ["expectedSha"] = sha,
            ["candidateId"] = "rc-2026-11-19-aaaaaaaaaaaa", ["coverageComplete"] = true, ["testsPassed"] = true, ["reportDelivered"] = true,
            ["exitCode"] = 0, ["runId"] = "off-switch", ["policyHash"] = "ab", ["coverageScope"] = scope,
        };
        var fullVerdict = ReleaseGateCredit.Verdict(State("Full"));
        Note(fullVerdict.Ok && fullVerdict.CreditKind == "rc-release", "Full gate credit " + string.Join("|", fullVerdict.Reasons));
        var incrementalVerdict = ReleaseGateCredit.Verdict(State("Incremental"));
        Note(!incrementalVerdict.Ok && incrementalVerdict.Reasons.Contains("full-credit-scope-required"),
            "Incremental gate " + string.Join("|", incrementalVerdict.Reasons));
    }

    [Test]
    public void SampleSizeStrataAndReplay()
    {
        var root = OwnedRoot();
        try
        {
            AssertOffSwitch();
            var forward = PinnedChunks();
            var reverse = (object[])forward.Clone();
            Array.Reverse(reverse);
            var pathA = Path.Combine(root, "seed-a.json");
            var first = Proposal(reverse, SeedA, pathA);
            var expectedA = ExpectedIds("c06");
            AssertProposalShape(first, "seed-a");
            Note(first.Sample == "roster", "seed-a sample=" + first.Sample);
            Note(first.Seed == SeedA, "seed-a stored seed");
            Note(first.Cchunk == 31, "seed-a cchunk=" + first.Cchunk);
            Note(first.StratumCount == 2, "seed-a strata=" + first.StratumCount);
            Note(first.N == 30, "seed-a n=" + first.N);
            Note(first.ShadowPilot, "seed-a shadow pilot plan");
            Note(JoinIds(first.Selected) == JoinIds(expectedA), "seed-a roster=" + JoinIds(first.Selected));
            Note(JoinIds(first.ExpectedSkipOnly) == "skip-a|skip-b", "seed-a skips=" + JoinIds(first.ExpectedSkipOnly));
            Note(!JoinIds(first.Selected).Contains("skip-", StringComparison.Ordinal), "skip-only entered the executed sample");
            var stored = JsonNode.Parse(File.ReadAllText(pathA))!.AsObject();
            string? Text(string name) => stored[name] is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;
            bool? Flag(string name) => stored[name] is JsonValue value && value.GetValueKind() is JsonValueKind.True or JsonValueKind.False ? value.GetValue<bool>() : null;
            Note(Text("seed") == SeedA, "file seed");
            Note(Text("effectiveMode") == "Full" && Flag("gateCredit") == false && Text("incrementalValidation") == "Shadow", "file off switch");
            Note(Flag("launched") == false && Flag("trustGranted") == false && Flag("pilotClaim") == false, "file does not launch or trust");
            var storedSelected = stored["selected"] is JsonArray selected ? selected.Select(item => item!.GetValue<string>()).ToArray() : [];
            Note(JoinIds(storedSelected) == JoinIds(expectedA), "file roster");
            var replay = Proposal(forward, SeedA, Path.Combine(root, "seed-a-replay.json"));
            Note(JoinIds(replay.Selected) == JoinIds(first.Selected), "replay roster");
            Note(replay.Seed == first.Seed && replay.N == first.N, "replay seed and n");
            var second = Proposal(forward, SeedB, Path.Combine(root, "seed-b.json"));
            var expectedB = ExpectedIds("c10");
            Note(JoinIds(second.Selected) == JoinIds(expectedB), "seed-b roster=" + JoinIds(second.Selected));
            Note(JoinIds(second.Selected) != JoinIds(first.Selected), "distinct seeds collapsed");
            var wroteAgain = false;
            try { Proposal(forward, SeedA, pathA); }
            catch (Exception ex) { wroteAgain = ex.Message == "write-once"; }
            Note(wroteAgain, "second write refused");
            var skipOnly = Chunk("skip-only", "alpha", "north");
            skipOnly["expectedSkipOnly"] = true;
            var zero = Proposal([skipOnly], SeedA, Path.Combine(root, "zero.json"));
            AssertProposalShape(zero, "zero");
            Note(zero.Sample == "not-applicable", "zero sample=" + zero.Sample);
            Note(zero.Cchunk == 0 && zero.N == 0 && !zero.ShadowPilot && !zero.PilotClaim, "zero counts cchunk=" + zero.Cchunk + " n=" + zero.N);
            Note(JoinIds(zero.Selected) == "", "zero selected");
            Note(JoinIds(zero.ExpectedSkipOnly) == "skip-only", "zero skip report");
            Note(!Eligible(NewPilot("zero", sample: "not-applicable"), Digest), "zero-carry record counted as a pilot");
            int[][] budgets = [[1, 1, 1, 1], [30, 1, 30, 30], [600, 1, 600, 30], [601, 1, 601, 31], [1000, 1, 1000, 50], [32, 32, 32, 32]];
            foreach (var row in budgets)
            {
                var population = Population(row[0], row[1]);
                var budget = Proposal(population, SeedA, Path.Combine(root, "n-" + row[0] + ".json"));
                AssertProposalShape(budget, "n" + row[0]);
                Note(budget.Cchunk == row[2] && budget.N == row[3] && budget.Selected.Length == row[3],
                    "budget " + row[0] + " cchunk=" + budget.Cchunk + " n=" + budget.N + " selected=" + budget.Selected.Length);
                if (row[0] == 32)
                {
                    var all = new HashSet<string>(population.Select(item => (string)((Dictionary<string, object?>)item)["id"]!), StringComparer.Ordinal);
                    var got = new HashSet<string>(budget.Selected, StringComparer.Ordinal);
                    Note(all.SetEquals(got), "strata increase dropped a stratum");
                }
            }
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(SampleSizeStrataAndReplay));
    }

    [Test]
    public void MismatchInvalidatesCarries()
    {
        var beforeCurrent = NewContract();
        var beforeBaseline = NewContract();
        var beforeEvidence = NewEvidence();
        var beforeAnchor = NewAnchor();
        var snapshot = new
        {
            Current = Serialize(beforeCurrent), Baseline = Serialize(beforeBaseline), Evidence = Serialize(beforeEvidence), Anchor = Serialize(beforeAnchor),
        };
        var failure = new Dictionary<string, object?>(StringComparer.Ordinal) { ["chunkId"] = "c01", ["outcome"] = "failed", ["receipt"] = "receipt-1" };
        var failureJson = Serialize(failure);
        object[] carries = ["c2", "c1", "c3"];
        var changed = NewContract();
        changed["policyHash"] = new string('b', 64);
        var mismatch = Trust(changed, beforeBaseline, beforeEvidence, beforeAnchor, Now, carries: carries, sampleFailure: failure, contractDigest: Digest);
        Note(!mismatch.TrustGranted, "mismatch granted trust");
        Note(mismatch.EffectiveMode == "Full", "mismatch mode=" + mismatch.EffectiveMode);
        Note(!mismatch.GateCredit, "mismatch gate credit");
        Note(mismatch.IncrementalValidation == "Shadow", "mismatch validation=" + mismatch.IncrementalValidation);
        Note(!mismatch.ExpandedToFull, "mismatch expanded into Full");
        Note(mismatch.CarriesInvalidated, "mismatch left carries authorized");
        Note(JoinIds(mismatch.InvalidatedCarries) == "c1|c2|c3", "mismatch carries=" + JoinIds(mismatch.InvalidatedCarries));
        Note(JoinIds(mismatch.Reasons) == "contract-changed:policyHash|sample-failed", "mismatch reasons=" + JoinIds(mismatch.Reasons));
        Note(Serialize(mismatch.PreservedSampleFailure) == failureJson, "failed sample erased");
        Note(!mismatch.TrustRecordWritten, "mismatch wrote trust");
        var sampleOnly = Trust(NewContract(), NewContract(), NewEvidence(), NewAnchor(), Now, carries: carries, sampleFailure: failure, contractDigest: Digest);
        Note(sampleOnly.CarriesInvalidated && !sampleOnly.TrustGranted && !sampleOnly.ExpandedToFull, "sample failure kept carries");
        Note(JoinIds(sampleOnly.Reasons) == "sample-failed", "sample reasons=" + JoinIds(sampleOnly.Reasons));
        Note(Serialize(sampleOnly.PreservedSampleFailure) == failureJson, "sample-only failure erased");
        var roster = Trust(NewContract(), NewContract(), NewEvidence(), NewAnchor(), Now, carries: ["c1"], rosterChanged: true, contractDigest: Digest);
        Note(roster.CarriesInvalidated && JoinIds(roster.Reasons) == "sample-roster-changed" && !roster.ExpandedToFull, "roster reasons=" + JoinIds(roster.Reasons));
        var missing = NewContract();
        missing.Remove("policyHash");
        var unknown = Trust(missing, NewContract(), NewEvidence(), NewAnchor(), Now, carries: ["c9"], contractDigest: Digest);
        Note(unknown.CarriesInvalidated && JoinIds(unknown.Reasons) == "contract-current-unknown:policyHash", "unknown reasons=" + JoinIds(unknown.Reasons));
        Note(Serialize(beforeCurrent) == snapshot.Current, "current contract mutated");
        Note(Serialize(beforeBaseline) == snapshot.Baseline, "baseline contract mutated");
        Note(Serialize(beforeEvidence) == snapshot.Evidence, "evidence mutated");
        Note(Serialize(beforeAnchor) == snapshot.Anchor, "anchor mutated");
        Note(Serialize(failure) == failureJson, "failure receipt mutated");
        Contract(nameof(MismatchInvalidatesCarries));
    }

    [Test]
    public void NoCarryAndOnePilotDoNotGrantTrust()
    {
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            var none = Trust(pilots: [], currentSample: "not-applicable", contractDigest: Digest, trustRecordPath: trustPath);
            Note(none.Reason == "not-a-shadow-pilot", "none reason=" + none.Reason);
            Note(none.ObservedPilots == 0 && none.RequiredPilots == 2, "none pilots=" + none.ObservedPilots);
            Note(!none.TrustGranted && none.IncrementalValidation == "Shadow" && none.EffectiveMode == "Full", "none flipped validation");
            Note(!none.GateCredit && !none.TrustRecordWritten, "none granted credit");
            Note(!File.Exists(trustPath), "none created a trust record");
            var one = Trust(pilots: [NewPilot("cut-1")], currentSample: "not-applicable", contractDigest: Digest, trustRecordPath: trustPath);
            Note(one.Reason == "pilot-count" && one.ObservedPilots == 1 && one.RequiredPilots == 2 && !one.TrustGranted,
                "one reason=" + one.Reason + " observed=" + one.ObservedPilots);
            var mixed = Trust(pilots: [NewPilot("cut-1"), NewPilot("cut-zero", sample: "not-applicable")], currentSample: "roster", contractDigest: Digest);
            Note(mixed.Reason == "pilot-count" && mixed.ObservedPilots == 1 && !mixed.TrustGranted, "mixed observed=" + mixed.ObservedPilots + " reason=" + mixed.Reason);
            var duplicate = Trust(pilots: [NewPilot("cut-1"), NewPilot("cut-1")], currentSample: "roster", contractDigest: Digest);
            Note(duplicate.ObservedPilots == 1 && duplicate.Reason == "pilot-count", "duplicate observed=" + duplicate.ObservedPilots);
            var linux = Trust(pilots: [NewPilot("cut-1"), NewPilot("cut-linux", platform: "linux")], currentSample: "roster", contractDigest: Digest);
            Note(linux.ObservedPilots == 1 && linux.Reason == "pilot-count" && !linux.TrustGranted, "linux observed=" + linux.ObservedPilots);
            var two = Trust(pilots: [NewPilot("cut-1"), NewPilot("cut-2")], currentSample: "roster", contractDigest: Digest, trustRecordPath: trustPath);
            Note(two.ObservedPilots == 2 && two.Reason == "operator-trust-required" && !two.TrustGranted, "two reason=" + two.Reason);
            Note(two.IncrementalValidation == "Shadow" && two.EffectiveMode == "Full" && !two.GateCredit, "two promoted itself");
            Note(!File.Exists(trustPath) && !two.TrustRecordWritten, "two wrote a trust record");
            var record = new Dictionary<string, object?>(StringComparer.Ordinal) { ["approved"] = true, ["contractDigest"] = Digest, ["platform"] = "windows" };
            var held = Trust(pilots: [NewPilot("cut-1"), NewPilot("cut-2")], currentSample: "roster", contractDigest: Digest, operatorTrustRecord: record,
                trustRecordPath: trustPath);
            Note(held.ObservedPilots == 2 && held.Reason == "trust-transition-disabled" && !held.TrustGranted, "held reason=" + held.Reason);
            Note(held.IncrementalValidation == "Shadow" && held.EffectiveMode == "Full" && !held.ExpandedToFull, "held flipped IncrementalValidation");
            Note(!File.Exists(trustPath) && !held.TrustRecordWritten, "held persisted the operator record");
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(NoCarryAndOnePilotDoNotGrantTrust));
    }

    [Test]
    public void OneExecutedSampleIsNotAPilot()
    {
        var bare = NewCountedPilot("bare-one", executed: 1, omitPlan: true);
        Note(!Eligible(bare, Digest), "one executed sample counted as a pilot");
        var partial = NewCountedPilot("partial", planned: 30, executed: 1, cchunk: 600, strata: 1);
        Note(!Eligible(partial, Digest), "one of thirty planned samples counted as a pilot");
        var shortPlan = NewCountedPilot("short", planned: 1, executed: 1, cchunk: 600, strata: 1);
        Note(!Eligible(shortPlan, Digest), "planned N below the D-8 minimum counted as a pilot");
        var under = NewCountedPilot("under", planned: 30, executed: 30, cchunk: 601, strata: 1);
        Note(!Eligible(under, Digest), "thirty samples counted when D-8 requires 31");
        var legalOne = NewCountedPilot("legal-one", planned: 1, executed: 1, cchunk: 1, strata: 1);
        Note(Eligible(legalOne, Digest), "bound plan of one was refused");
        var legal = NewCountedPilot("legal-31", planned: 31, executed: 31, cchunk: 601, strata: 1);
        Note(Eligible(legal, Digest), "D-8 plan of 31 was refused");
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            var decision = Trust(pilots: [bare, partial], currentSample: "roster", contractDigest: Digest, trustRecordPath: trustPath);
            Note(decision.ObservedPilots == 0, "one-sample observed=" + decision.ObservedPilots);
            Note(decision.Reason == "pilot-count", "one-sample reason=" + decision.Reason);
            Note(!decision.TrustGranted && !decision.GateCredit && decision.EffectiveMode == "Full" && decision.IncrementalValidation == "Shadow",
                "one-sample granted trust");
            Note(!decision.TrustRecordWritten && !File.Exists(trustPath), "one-sample wrote a trust record");
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(OneExecutedSampleIsNotAPilot));
    }

    [Test]
    public void EmptyDigestIsUnproven()
    {
        var empty = NewCountedPilot("empty", digest: "");
        Note(!Eligible(empty, ""), "empty digest matched an empty pilot digest");
        var missing = NewCountedPilot("missing", digest: "");
        missing.Remove("contractDigest");
        Note(!Eligible(missing, ""), "missing digest matched");
        var upper = NewCountedPilot("upper", digest: new string('A', 64));
        Note(!Eligible(upper, new string('A', 64)), "uppercase digest matched");
        Note(Eligible(NewPilot("canonical"), Digest), "canonical digest was refused");
        var blankContract = NewCountedPilot("blank-contract");
        Note(!Eligible(blankContract, ""), "empty contract digest matched a real pilot");
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            var first = NewCountedPilot("cut-1", digest: "");
            var second = NewCountedPilot("cut-2", digest: "");
            var two = Trust(pilots: [first, second], currentSample: "roster", contractDigest: "", trustRecordPath: trustPath);
            Note(two.ObservedPilots == 0, "empty-digest observed=" + two.ObservedPilots);
            Note(two.Reason == "pilot-count", "empty-digest reason=" + two.Reason);
            Note(!two.TrustGranted && !two.GateCredit && two.EffectiveMode == "Full" && two.IncrementalValidation == "Shadow", "empty digest granted trust");
            Note(!two.TrustRecordWritten && !File.Exists(trustPath), "empty digest wrote a trust record");
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(EmptyDigestIsUnproven));
    }

    [Test]
    public void MissingCarryableIsDirty()
    {
        var unknown = Proposal([Chunk("unknown", "alpha", "north")], SeedA);
        AssertProposalShape(unknown, "unknown");
        Note(unknown.Cchunk == 0, "unknown cchunk=" + unknown.Cchunk);
        Note(unknown.N == 0, "unknown n=" + unknown.N);
        Note(unknown.Sample == "not-applicable", "unknown sample=" + unknown.Sample);
        Note(JoinIds(unknown.Selected) == "", "missing completelyCarryable entered the sample: " + JoinIds(unknown.Selected));
        var carry = Chunk("carry", "alpha", "north");
        carry["completelyCarryable"] = true;
        var dirty = Chunk("dirty", "beta", "south");
        dirty["completelyCarryable"] = false;
        var mixed = Proposal([Chunk("unknown", "alpha", "north"), carry, dirty], SeedA);
        AssertProposalShape(mixed, "mixed");
        Note(mixed.Cchunk == 1, "missing completelyCarryable diluted the sample cchunk=" + mixed.Cchunk);
        Note(mixed.N == 1, "mixed n=" + mixed.N);
        Note(JoinIds(mixed.Selected) == "carry", "mixed selected=" + JoinIds(mixed.Selected));
        Contract(nameof(MissingCarryableIsDirty));
    }

    [Test]
    public void EvidenceDerivesSampleFailure()
    {
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            object[] c1 = ["c1"];
            AssertDerivedFailure("empty results", Evidence(c1, Array.Empty<object>()), "empty-results", "c1", trustPath);
            var noOutcome = Map();
            noOutcome["chunkId"] = "c1";
            noOutcome["provenance"] = "verified";
            AssertDerivedFailure("missing outcome", Evidence(c1, new object[] { noOutcome }), "missing-outcome", "c1", trustPath);
            AssertDerivedFailure("empty outcome", Evidence(c1, new object[] { Result("c1", "") }), "missing-outcome", "c1", trustPath);
            AssertDerivedFailure("absent chunk", Evidence(new object[] { "c1", "c2" }, new object[] { Result("c1", "passed") }), "missing-outcome", "c2", trustPath);
            var unexpected = Result("c1", "passed");
            unexpected["unexpectedSkip"] = true;
            AssertDerivedFailure("unexpected skip", Evidence(c1, new object[] { unexpected }), "unexpected-skip", "c1", trustPath);
            AssertDerivedFailure("skipped outcome", Evidence(c1, new object[] { Result("c1", "skipped") }), "unexpected-skip", "c1", trustPath);
            AssertDerivedFailure("provenance", Evidence(c1, new object[] { Result("c1", "passed", "failed") }), "provenance-failure", "c1", trustPath);
            var flagged = Result("c1", "passed");
            flagged["provenanceFailure"] = true;
            AssertDerivedFailure("provenance flag", Evidence(c1, new object[] { flagged }), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("failed outcome", Evidence(c1, new object[] { Result("c1", "failed") }), "sample-outcome", "c1", trustPath);
            var cleanRow = Result("c1", "passed");
            cleanRow["unexpectedSkip"] = false;
            var cleanEvidence = Evidence(c1, new object[] { cleanRow });
            var clean = Trust(carries: ["c1"], evidence: cleanEvidence, currentSample: "roster", trustRecordPath: trustPath);
            Note(!clean.CarriesInvalidated, "clean sample invalidated carries");
            Note(clean.Reason == "pilot-count", "clean reason=" + clean.Reason);
            Note(!clean.TrustGranted && !clean.GateCredit && clean.EffectiveMode == "Full" && clean.IncrementalValidation == "Shadow", "clean sample granted trust");
            Note(!clean.TrustRecordWritten && !File.Exists(trustPath), "clean sample wrote a trust record");
            var caller = new Dictionary<string, object?>(StringComparer.Ordinal) { ["chunkId"] = "c1", ["outcome"] = "failed", ["receipt"] = "receipt-1" };
            var flag = Trust(carries: ["c9", "c8"], sampleFailure: caller, evidence: cleanEvidence, trustRecordPath: trustPath);
            Note(flag.CarriesInvalidated, "caller failure left carries valid");
            Note(JoinIds(flag.Reasons) == "sample-failed", "caller reasons=" + JoinIds(flag.Reasons));
            Note(JoinIds(flag.InvalidatedCarries) == "c8|c9", "caller carries=" + JoinIds(flag.InvalidatedCarries));
            Note(FieldText(flag.PreservedSampleFailure, "outcome") == "failed", "caller failure was replaced");
            Note(!flag.TrustGranted && flag.EffectiveMode == "Full" && !flag.GateCredit && flag.IncrementalValidation == "Shadow", "caller failure granted trust");
            var both = Trust(carries: ["c1"], sampleFailure: caller, evidence: Evidence(c1, Array.Empty<object>()));
            Note(JoinIds(both.Reasons) == "sample-failed", "duplicate sample-failed reasons=" + JoinIds(both.Reasons));
            Note(FieldText(both.PreservedSampleFailure, "outcome") == "failed", "derived failure replaced the caller flag");
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(EvidenceDerivesSampleFailure));
    }

    [Test]
    public void NonBooleanProvenanceFailureInvalidates()
    {
        // D-1. Only boolean false is a clean flag. A present string, number, null, or object is a failure, including
        // after a JSON round-trip. unexpectedSkip uses the same rule.
        // B-1. A one-element array is not boolean false. [false] and an array of false must fail closed, and so must
        // [true], [], and [[false]]. An array outcome, provenance, or chunkId is not the scalar type.
        // A-1. A wrong-case flag key fails closed. It is not a missing flag.
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            Dictionary<string, object?> Bad() => new(StringComparer.Ordinal) { ["bad"] = true };
            AssertDerivedFailure("string provenanceFailure", ClosedFlagEvidence("provenanceFailure", "true"), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("string false provenanceFailure", ClosedFlagEvidence("provenanceFailure", "false"), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("number provenanceFailure", ClosedFlagEvidence("provenanceFailure", 1), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("number zero provenanceFailure", ClosedFlagEvidence("provenanceFailure", 0), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("null provenanceFailure", ClosedFlagEvidence("provenanceFailure", null), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("object provenanceFailure", ClosedFlagEvidence("provenanceFailure", Bad()), "provenance-failure", "c1", trustPath);
            const string prefix = "{\"sampled\":[\"c1\"],\"results\":[{\"chunkId\":\"c1\",\"outcome\":\"passed\",\"provenance\":\"verified\"";
            AssertDerivedFailure("json string provenanceFailure", Json(prefix + ",\"provenanceFailure\":\"true\"}]}"), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("json number provenanceFailure", Json(prefix + ",\"provenanceFailure\":1}]}"), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("json null provenanceFailure", Json(prefix + ",\"provenanceFailure\":null}]}"), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("json object provenanceFailure", Json(prefix + ",\"provenanceFailure\":{\"bad\":true}}]}"), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("string unexpectedSkip", ClosedFlagEvidence("unexpectedSkip", "true"), "unexpected-skip", "c1", trustPath);
            AssertDerivedFailure("number unexpectedSkip", ClosedFlagEvidence("unexpectedSkip", 1), "unexpected-skip", "c1", trustPath);
            AssertDerivedFailure("null unexpectedSkip", ClosedFlagEvidence("unexpectedSkip", null), "unexpected-skip", "c1", trustPath);
            AssertDerivedFailure("object unexpectedSkip", ClosedFlagEvidence("unexpectedSkip", Bad()), "unexpected-skip", "c1", trustPath);
            var clean = Trust(carries: ["c1"], evidence: ClosedFlagEvidence("provenanceFailure", false), currentSample: "roster", trustRecordPath: trustPath);
            Note(!clean.CarriesInvalidated, "boolean false provenanceFailure invalidated carries");
            Note(clean.Reason == "pilot-count", "boolean false provenance reason=" + clean.Reason);
            Note(!clean.TrustGranted && !clean.GateCredit && clean.EffectiveMode == "Full" && clean.IncrementalValidation == "Shadow",
                "boolean false provenance granted trust");
            var cleanJson = Trust(carries: ["c1"], evidence: Json(prefix + ",\"provenanceFailure\":false}]}"), currentSample: "roster", trustRecordPath: trustPath);
            Note(!cleanJson.CarriesInvalidated, "json boolean false provenanceFailure invalidated carries");
            Note(cleanJson.Reason == "pilot-count", "json false provenance reason=" + cleanJson.Reason);
            Note(!cleanJson.TrustGranted && !cleanJson.GateCredit && cleanJson.EffectiveMode == "Full" && cleanJson.IncrementalValidation == "Shadow",
                "json false provenance granted trust");
            foreach (var flagName in new[] { "provenanceFailure", "unexpectedSkip" })
            {
                var flagKind = flagName == "unexpectedSkip" ? "unexpected-skip" : "provenance-failure";
                AssertDerivedFailure("hashtable [false] " + flagName, ClosedFlagEvidence(flagName, new object[] { false }), flagKind, "c1", trustPath);
                AssertDerivedFailure("hashtable [true] " + flagName, ClosedFlagEvidence(flagName, new object[] { true }), flagKind, "c1", trustPath);
                AssertDerivedFailure("hashtable [] " + flagName, ClosedFlagEvidence(flagName, Array.Empty<object>()), flagKind, "c1", trustPath);
                AssertDerivedFailure("hashtable [[false]] " + flagName, ClosedFlagEvidence(flagName, new object[] { new object[] { false } }), flagKind, "c1",
                    trustPath);
                AssertDerivedFailure("json [false] " + flagName, Json(prefix + ",\"" + flagName + "\":[false]}]}"), flagKind, "c1", trustPath);
                AssertDerivedFailure("json [true] " + flagName, Json(prefix + ",\"" + flagName + "\":[true]}]}"), flagKind, "c1", trustPath);
                AssertDerivedFailure("json [] " + flagName, Json(prefix + ",\"" + flagName + "\":[]}]}"), flagKind, "c1", trustPath);
                AssertDerivedFailure("json [[false]] " + flagName, Json(prefix + ",\"" + flagName + "\":[[false]]}]}"), flagKind, "c1", trustPath);
            }
            AssertDerivedFailure("outcome [passed]", ClosedFlagEvidence("outcome", new object[] { "passed" }), "missing-outcome", "c1", trustPath);
            AssertDerivedFailure("provenance [verified]", ClosedFlagEvidence("provenance", new object[] { "verified" }), "provenance-failure", "c1", trustPath);
            AssertDerivedFailure("chunkId [c1]", ClosedFlagEvidence("chunkId", new object[] { "c1" }), "unreadable-chunk-id", "", trustPath);
            AssertDerivedFailure("json outcome [passed]",
                Json("{\"sampled\":[\"c1\"],\"results\":[{\"chunkId\":\"c1\",\"outcome\":[\"passed\"],\"provenance\":\"verified\"}]}"), "missing-outcome", "c1",
                trustPath);
            AssertDerivedFailure("json provenance [verified]",
                Json("{\"sampled\":[\"c1\"],\"results\":[{\"chunkId\":\"c1\",\"outcome\":\"passed\",\"provenance\":[\"verified\"]}]}"), "provenance-failure", "c1",
                trustPath);
            AssertDerivedFailure("json chunkId [c1]",
                Json("{\"sampled\":[\"c1\"],\"results\":[{\"chunkId\":[\"c1\"],\"outcome\":\"passed\",\"provenance\":\"verified\"}]}"), "unreadable-chunk-id", "",
                trustPath);
            Dictionary<string, object?> WrongCase(string key, bool value)
            {
                var row = Result("c1", "passed");
                row[key] = value;
                return row;
            }
            object[] c1 = ["c1"];
            AssertDerivedFailure("wrong-case ProvenanceFailure true", Evidence(c1, new object[] { WrongCase("ProvenanceFailure", true) }), "provenance-failure", "c1",
                trustPath);
            AssertDerivedFailure("wrong-case ProvenanceFailure false", Evidence(c1, new object[] { WrongCase("ProvenanceFailure", false) }), "provenance-failure", "c1",
                trustPath);
            AssertDerivedFailure("wrong-case UnexpectedSkip true", Evidence(c1, new object[] { WrongCase("UnexpectedSkip", true) }), "unexpected-skip", "c1",
                trustPath);
            AssertDerivedFailure("json wrong-case ProvenanceFailure", Json(prefix + ",\"ProvenanceFailure\":true}]}"), "provenance-failure", "c1", trustPath);
            var bothFlags = Result("c1", "passed");
            bothFlags["provenanceFailure"] = false;
            bothFlags["ProvenanceFailure"] = true;
            AssertDerivedFailure("ordinal both flag cases", Evidence(c1, new object[] { bothFlags }), "provenance-failure", "c1", trustPath);
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(NonBooleanProvenanceFailureInvalidates));
    }

    [Test]
    public void CarriedChunkResultInvalidatesCarries()
    {
        // D-2. A carry is not launched unless sampled names it. Any result row for another carry is a contradiction,
        // including a passed row, and invalidates every carry. A dirty unsampled row stays dropped.
        // B-2. Once sampled or results is present, a row whose chunkId is missing, null, empty, whitespace, a number,
        // an object, or an array fails closed as unreadable-chunk-id, whatever its outcome.
        // A-3. A non-string carry entry fails closed. It is not skipped.
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            object[] s1 = ["s1"];
            var passedSample = Result("s1", "passed");
            var failedCarry = Evidence(s1, new object[] { passedSample, Result("c2", "failed") });
            var failed = Trust(carries: ["c1", "c2"], evidence: failedCarry, currentSample: "roster", trustRecordPath: trustPath);
            Note(failed.CarriesInvalidated, "failed carried row left carries valid reason=" + failed.Reason);
            Note(JoinIds(failed.InvalidatedCarries) == "c1|c2", "failed carried carries=" + JoinIds(failed.InvalidatedCarries));
            Note(JoinIds(failed.Reasons) == "sample-failed", "failed carried reasons=" + JoinIds(failed.Reasons));
            Note(FieldText(failed.PreservedSampleFailure, "kind") == "carried-result", "failed carried kind=" + FieldText(failed.PreservedSampleFailure, "kind"));
            Note(FieldText(failed.PreservedSampleFailure, "chunkId") == "c2", "failed carried chunk=" + FieldText(failed.PreservedSampleFailure, "chunkId"));
            Note(!failed.TrustGranted && !failed.GateCredit && failed.EffectiveMode == "Full" && failed.IncrementalValidation == "Shadow",
                "failed carried row granted trust");
            Note(!failed.TrustRecordWritten && !File.Exists(trustPath), "failed carried row wrote a trust record");
            var passedCarry = Evidence(s1, new object[] { passedSample, Result("c2", "passed") });
            var passed = Trust(carries: ["c1", "c2"], evidence: passedCarry, currentSample: "roster", trustRecordPath: trustPath);
            Note(passed.CarriesInvalidated, "passed carried row left carries valid reason=" + passed.Reason);
            Note(JoinIds(passed.InvalidatedCarries) == "c1|c2", "passed carried carries=" + JoinIds(passed.InvalidatedCarries));
            Note(JoinIds(passed.Reasons) == "sample-failed", "passed carried reasons=" + JoinIds(passed.Reasons));
            Note(FieldText(passed.PreservedSampleFailure, "kind") == "carried-result", "passed carried kind=" + FieldText(passed.PreservedSampleFailure, "kind"));
            Note(FieldText(passed.PreservedSampleFailure, "chunkId") == "c2", "passed carried chunk=" + FieldText(passed.PreservedSampleFailure, "chunkId"));
            Note(!passed.TrustGranted && !passed.GateCredit && passed.EffectiveMode == "Full" && passed.IncrementalValidation == "Shadow",
                "passed carried row granted trust");
            var dirty = Evidence(s1, new object[] { passedSample, Result("dirty", "failed") });
            var dropped = Trust(carries: ["c1", "c2"], evidence: dirty, currentSample: "roster", trustRecordPath: trustPath);
            Note(!dropped.CarriesInvalidated, "dirty unsampled row invalidated carries reason=" + dropped.Reason);
            Note(dropped.Reason == "pilot-count", "dirty unsampled reason=" + dropped.Reason);
            var launched = Evidence(s1, new object[] { passedSample });
            var kept = Trust(carries: ["s1", "c2"], evidence: launched, currentSample: "roster", trustRecordPath: trustPath);
            Note(!kept.CarriesInvalidated, "launched carry sample invalidated carries reason=" + kept.Reason);
            Note(kept.Reason == "pilot-count", "launched carry reason=" + kept.Reason);
            Note(!kept.TrustGranted && !kept.GateCredit && kept.EffectiveMode == "Full" && kept.IncrementalValidation == "Shadow", "launched carry sample granted trust");
            (string Label, string Row)[] badRows =
            [
                ("missing chunkId", "{\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("null chunkId", "{\"chunkId\":null,\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("empty chunkId", "{\"chunkId\":\"\",\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("whitespace chunkId", "{\"chunkId\":\" \",\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("number chunkId", "{\"chunkId\":1,\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("object chunkId", "{\"chunkId\":{\"bad\":true},\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("array chunkId", "{\"chunkId\":[\"other\"],\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("multi array chunkId", "{\"chunkId\":[\"c2\",\"c3\"],\"outcome\":\"failed\",\"provenance\":\"verified\"}"),
                ("passed number chunkId", "{\"chunkId\":2,\"outcome\":\"passed\",\"provenance\":\"verified\"}"),
                ("flag with null chunkId", "{\"chunkId\":null,\"outcome\":\"passed\",\"provenance\":\"verified\",\"provenanceFailure\":true}"),
            ];
            foreach (var (label, row) in badRows)
            {
                var json = "{\"sampled\":[\"s1\"],\"results\":[{\"chunkId\":\"s1\",\"outcome\":\"passed\",\"provenance\":\"verified\"}," + row + "]}";
                AssertDerivedFailure("json " + label, Json(json), "unreadable-chunk-id", "", trustPath);
            }
            AssertDerivedFailure("results-only passed number chunkId", Json("{\"results\":[{\"chunkId\":1,\"outcome\":\"passed\",\"provenance\":\"verified\"}]}"),
                "unreadable-chunk-id", "", trustPath);
            AssertDerivedFailure("results-only missing chunkId", Json("{\"results\":[{\"outcome\":\"failed\",\"provenance\":\"verified\"}]}"), "unreadable-chunk-id", "",
                trustPath);
            var noChunk = Map();
            noChunk["outcome"] = "failed";
            noChunk["provenance"] = "verified";
            var hashtableMissing = Evidence(s1, new object[] { Result("s1", "passed"), noChunk });
            AssertDerivedFailure("hashtable missing chunkId", hashtableMissing, "unreadable-chunk-id", "", trustPath);
            (string Label, object?[] Carries)[] carryCases =
            [
                ("integer carry", ["c1", 2]),
                ("object carry", [new Dictionary<string, object?>(StringComparer.Ordinal) { ["id"] = "c9" }]),
            ];
            foreach (var (label, carries) in carryCases)
            {
                var threw = false;
                var throwMessage = "";
                try
                {
                    _ = Trust(carries: carries, evidence: launched, currentSample: "roster", trustRecordPath: trustPath);
                }
                catch (Exception ex)
                {
                    threw = true;
                    throwMessage = ex.Message;
                }
                Note(threw, label + " was skipped");
                Note(throwMessage == "shadow-carry-id", label + " message=" + throwMessage);
            }
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(CarriedChunkResultInvalidatesCarries));
    }

    [Test]
    public void AbsentEvidenceKeepsCarriesUntilWired()
    {
        // F-1 precondition, current behavior. S6c/S6d must flip this case before the library decides a carry: absent
        // evidence stays valid, and a pilot with no cchunk or stratumCount stays eligible at 30.
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            var wrongCase = Map();
            wrongCase["Sampled"] = new object[] { "c1" };
            wrongCase["Results"] = Array.Empty<object>();
            (string Label, object? Evidence, string Sample)[] absent =
            [
                ("null evidence", null, "roster"),
                ("empty evidence", Map(), "roster"),
                ("string evidence", "not-a-record", "executed"),
                ("wrong-case keys", wrongCase, "executed"),
            ];
            foreach (var (label, evidence, sample) in absent)
            {
                var decision = Trust(carries: ["c1", "c2"], evidence: evidence, currentSample: sample, trustRecordPath: trustPath);
                Note(!decision.CarriesInvalidated, label + " invalidated carries reason=" + decision.Reason);
                Note(JoinIds(decision.InvalidatedCarries) == "", label + " carries=" + JoinIds(decision.InvalidatedCarries));
                Note(decision.Reason == "pilot-count", label + " reason=" + decision.Reason);
                Note(decision.PreservedSampleFailure == null, label + " preserved a sample failure");
                Note(!decision.TrustGranted && !decision.GateCredit && decision.EffectiveMode == "Full" && decision.IncrementalValidation == "Shadow",
                    label + " granted trust");
                Note(!decision.TrustRecordWritten && !File.Exists(trustPath), label + " wrote a trust record");
            }
            Dictionary<string, object?> Floor(string cutId) => new(StringComparer.Ordinal)
            {
                ["cutId"] = cutId, ["platform"] = "windows", ["sample"] = "executed", ["shadowPilot"] = true, ["discrepancy"] = false,
                ["openFailure"] = false, ["nativeReceipts"] = "current", ["fullOverdue"] = false, ["contractDigest"] = Digest, ["n"] = 30, ["nExecuted"] = 30,
            };
            var floor = Floor("floor-1");
            Note(Eligible(floor, Digest), "incomplete pilot without cchunk or stratumCount was ineligible");
            var second = Floor("floor-2");
            var counted = Trust(pilots: [floor, second], carries: ["c1"], evidence: null, currentSample: "executed", contractDigest: Digest, trustRecordPath: trustPath);
            Note(counted.ObservedPilots == 2, "incomplete pilots observed=" + counted.ObservedPilots);
            Note(!counted.CarriesInvalidated, "incomplete pilots invalidated carries reason=" + counted.Reason);
            Note(counted.Reason == "operator-trust-required", "incomplete pilots reason=" + counted.Reason);
            Note(!counted.TrustGranted && !counted.GateCredit && counted.EffectiveMode == "Full" && counted.IncrementalValidation == "Shadow",
                "incomplete pilots granted trust");
            Note(!counted.TrustRecordWritten && !File.Exists(trustPath), "incomplete pilots wrote a trust record");
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(AbsentEvidenceKeepsCarriesUntilWired));
    }

    [Test]
    public void EmptyCollectionFailsClosed()
    {
        // Round-4 B-1. A present sampled or results value that yields no ids or rows is empty-results. On 38efdbde an
        // empty List or ArrayList keeps carries valid with reason pilot-count. An empty string is missing-outcome or
        // unreadable-chunk-id. Whitespace results are unreadable-chunk-id. Empty arrays already fail closed.
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            Dictionary<string, object?> Only(string key, object value)
            {
                var evidence = Map();
                evidence[key] = value;
                return evidence;
            }
            (string Label, Dictionary<string, object?> Evidence)[] forms =
            [
                ("List results", Only("results", new List<object>())),
                ("List sampled", Only("sampled", new List<string>())),
                ("ArrayList results", Only("results", new ArrayList())),
                ("ArrayList sampled", Only("sampled", new ArrayList())),
                ("empty array results", Only("results", new object[0])),
                ("empty array sampled", Only("sampled", new string[0])),
                ("@() results", Only("results", Array.Empty<object>())),
                ("@() sampled", Only("sampled", Array.Empty<object>())),
                ("empty string results", Only("results", "")),
                ("empty string sampled", Only("sampled", "")),
                ("whitespace results", Only("results", "   ")),
                ("whitespace sampled", Only("sampled", "   ")),
            ];
            foreach (var (label, evidence) in forms) AssertDerivedFailure(label, evidence, "empty-results", "", trustPath);
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(EmptyCollectionFailsClosed));
    }

    [Test]
    public void DictionaryKeyShadowFailsClosed()
    {
        // Round-4 B-2. A dictionary key named Keys, Count, Values, or Item must not hide sampled, results, or a closed
        // flag. On 38efdbde a Keys entry keeps carries valid. Parsed JSON and a hand-built dictionary both do this.
        var root = OwnedRoot();
        try
        {
            var trustPath = Path.Combine(root, "trust-record.json");
            object[] c1 = ["c1"];
            (string Flag, bool Value, string Kind)[] rowFlags =
            [
                ("provenanceFailure", true, "provenance-failure"),
                ("unexpectedSkip", true, "unexpected-skip"),
                ("UnexpectedSkip", true, "unexpected-skip"),
            ];
            string[] shadows = ["Keys", "Count", "Values", "Item"];
            foreach (var (flag, value, kind) in rowFlags)
            {
                foreach (var shadow in shadows)
                {
                    var hand = Result("c1", "passed");
                    hand[flag] = value;
                    hand[shadow] = new object[] { "chunkId", "outcome", "provenance" };
                    AssertDerivedFailure("hand row " + shadow + " " + flag, Evidence(c1, new object[] { hand }), kind, "c1", trustPath);
                    var json = "{\"sampled\":[\"c1\"],\"results\":[{\"chunkId\":\"c1\",\"outcome\":\"passed\",\"provenance\":\"verified\",\"" + flag + "\":true,\""
                        + shadow + "\":[\"chunkId\",\"outcome\",\"provenance\"]}]}";
                    AssertDerivedFailure("json row " + shadow + " " + flag, Json(json), kind, "c1", trustPath);
                }
            }
            foreach (var shadow in shadows)
            {
                var handEvidence = Evidence(c1, new object[] { Result("c1", "failed") });
                handEvidence[shadow] = Array.Empty<object>();
                AssertDerivedFailure("hand evidence " + shadow, handEvidence, "sample-outcome", "c1", trustPath);
                const string rest = "\"sampled\":[\"c1\"],\"results\":[{\"chunkId\":\"c1\",\"outcome\":\"failed\",\"provenance\":\"verified\"}]}";
                AssertDerivedFailure("json evidence empty " + shadow, Json("{\"" + shadow + "\":[]," + rest), "sample-outcome", "c1", trustPath);
                AssertDerivedFailure("json evidence text " + shadow, Json("{\"" + shadow + "\":\"x\"," + rest), "sample-outcome", "c1", trustPath);
            }
            var cleanRow = Result("c1", "passed");
            cleanRow["provenanceFailure"] = false;
            cleanRow["Keys"] = new object[] { "chunkId" };
            var kept = Trust(carries: ["c1"], evidence: Evidence(c1, new object[] { cleanRow }), currentSample: "roster", trustRecordPath: trustPath);
            Note(!kept.CarriesInvalidated, "clean Keys row invalidated carries reason=" + kept.Reason);
            Note(kept.Reason == "pilot-count", "clean Keys row reason=" + kept.Reason);
            Note(!kept.TrustGranted && !kept.GateCredit && kept.EffectiveMode == "Full" && kept.IncrementalValidation == "Shadow", "clean Keys row granted trust");
        }
        finally
        {
            OwnedTempRoot.Release(root);
        }
        Contract(nameof(DictionaryKeyShadowFailsClosed));
    }
}
