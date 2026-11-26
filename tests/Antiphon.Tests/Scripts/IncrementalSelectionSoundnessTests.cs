using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// Replays twelve historical rc failures through two pure selections over the
/// stored path lists. The owner-map replay uses the committed RC input inventory.
/// The proposed project closure selects a suite only when a changed path sits under
/// the suite csproj or a project it reaches by ProjectReference (reflexive, transitive,
/// every Include, conditions included, no Directory.Build import walk) or the path
/// is a build-graph root. Linked Compile items are not edges. Statuses are computed
/// and compared with the fixture, so a map or graph change that moves a case fails
/// until the recorded known-unsound set is updated.
/// </summary>
[Category("Unit")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class IncrementalSelectionSoundnessTests
{

    [Test, Timeout(28_000)]
    public async Task ProvenSubtractionReplayRecordsEverySkippedFailure(CancellationToken cancellationToken)
    {
        using var world = new ProvenReplayWorld();
        await world.InitializeAsync(cancellationToken);
        var input = world.Input("missed", fails: true);
        var output = world.Output("missed");
        var code = ReplayProvenSelection(world, input, output, true);
        code.ShouldBe(1, "P.incident");
        using var verdict = Read(output, "verdict.json");
        verdict.RootElement.GetProperty("proofDependentCarries").GetInt32().ShouldBe(1, "P.opportunity");
        verdict.RootElement.GetProperty("enables").GetBoolean().ShouldBeFalse("P.criterion");
        var runThree = verdict.RootElement.GetProperty("runs")[2].GetProperty("matrix");
        runThree.GetProperty("carry/FAIL").GetInt32().ShouldBe(1, "P.incident");
        runThree.GetProperty("run/clean").GetInt32().ShouldBe(1, "P.incident");
        foreach (var run in new[] { 0, 1, 3, 4 })
            verdict.RootElement.GetProperty("runs")[run].GetProperty("matrix").GetProperty("run/clean").GetInt32().ShouldBe(2);

        var records = verdict.RootElement.GetProperty("missRecords").EnumerateArray().ToArray();
        records.Length.ShouldBe(2, "P.records");
        var identities = new List<string>();
        foreach (var record in records)
        {
            var path = record.GetProperty("path").GetString()!;
            var raw = File.ReadAllBytes(Path.Combine(output, path));
            DigestBytes(raw).ShouldBe(record.GetProperty("sha256").GetString(), "P.evidence");
            using var stored = JsonDocument.Parse(raw);
            stored.RootElement.GetProperty("schema").GetString().ShouldBe("antiphon-rc-selection-miss-v1");
            var doc = stored.RootElement.GetProperty("observation");
            identities.Add(doc.GetProperty("case").GetProperty("caseKey").GetString()!);
            doc.GetProperty("candidateSha").GetString().ShouldBe(world.Sha(3));
            doc.GetProperty("baselineSha").GetString().ShouldBe(world.Sha(2));
            doc.GetProperty("chunk").GetString().ShouldBe("left");
            doc.GetProperty("observedBy").GetString().ShouldBe("replay");
            doc.GetProperty("observedAtUtc").GetString().ShouldBe("2026-11-25T12:00:00.0000000Z");
            doc.GetProperty("repository").GetString().ShouldBe("card-2076-fixture");
            doc.GetProperty("line").GetString().ShouldBe("rc");
            doc.GetProperty("platform").GetString().ShouldBe("windows");
            doc.GetProperty("architecture").GetString().ShouldBe("x64");
            doc.GetProperty("profile").GetString().ShouldBe("rc");
            doc.GetProperty("suite").GetString().ShouldBe("antiphon");
            doc.GetProperty("attempt").GetString().ShouldBe(ProvenReplayWorld.RunId(3));
            doc.GetProperty("run").GetString().ShouldBe(ProvenReplayWorld.RunId(3));
            doc.GetProperty("predicted").GetString().ShouldBe("carry");
            doc.GetProperty("case").GetProperty("assembly").GetString().ShouldBe("L");
            doc.GetProperty("case").GetProperty("class").GetString().ShouldBe("L.LeftTests");
            doc.GetProperty("case").GetProperty("method").GetString().ShouldBe("M");
            doc.GetProperty("case").GetProperty("outcome").GetString().ShouldBe("Failed");
            doc.GetProperty("trx").GetString().ShouldBe("runs/r3000000/logs/left.trx");
            doc.GetProperty("actual").GetString().ShouldBe("FAIL");
            foreach (var (property, relative) in new[]
            {
                ("trxSha256", "frozen/runs/r3000000/logs/left.trx"),
                ("predictionDigest", "predictions/3.json"),
                ("resolvedDigest", "predictions/3.resolved.json"),
                ("baselineGraph", "frozen/type-graphs/" + world.Sha(2) + ".json"),
                ("candidateGraph", "frozen/type-graphs/" + world.Sha(3) + ".json"),
            })
                doc.GetProperty(property).GetString().ShouldBe(DigestBytes(File.ReadAllBytes(Path.Combine(output, relative))),
                    "P.evidence rehash " + property);
            var removed = doc.GetProperty("removed").EnumerateArray().Select(row =>
                (row.GetProperty("side").GetString(), row.GetProperty("path").GetString(), row.GetProperty("class").GetString()));
            removed.SequenceEqual(new[] { ("A", ProvenReplayWorld.Changed, "LeftTests"), ("B", ProvenReplayWorld.Changed, "LeftTests") })
                .ShouldBeTrue("P.evidence endpoint contributions");
            var changes = doc.GetProperty("diff").EnumerateArray().ToArray();
            changes.Length.ShouldBe(1, "P.evidence diff");
            changes[0].GetProperty("status").GetString().ShouldBe("M");
            changes[0].GetProperty("oldPath").GetString().ShouldBe(ProvenReplayWorld.Changed);
            changes[0].GetProperty("newPath").GetString().ShouldBe(ProvenReplayWorld.Changed);
        }
        identities.Order(StringComparer.Ordinal).SequenceEqual(["l1", "l2"], StringComparer.Ordinal).ShouldBeTrue("P.records");
        records.Select(record => record.GetProperty("missId").GetString()).Distinct().Count().ShouldBe(2);
        var firstPath = records[0].GetProperty("path").GetString()!;
        var firstBytes = File.ReadAllBytes(Path.Combine(output, firstPath));
        using var firstDocument = JsonDocument.Parse(firstBytes);
        var observation = firstDocument.RootElement.GetProperty("observation").Deserialize<RcSelectionMissObservation>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var vector = observation with
        {
            BaselineSha = new string('1', 40), CandidateSha = new string('2', 40),
            Run = "fixture-run", Attempt = "fixture-attempt", Chunk = "chunk-1", ContentDigest = new string('a', 64),
            Case = new("case", "L", "L.LeftTests", "M", "l1", "Failed"),
        };
        var vectors = world.Output("literal-miss-identities");
        RcSelectionMiss.Record(vectors, vector).MissId
            .ShouldBe("c4270fc2d16562b39fefd2018b5d22a6b94b80a8cb7b65e0d0592b2d4631e325", "P.identity literal versioned case vector");
        RcSelectionMiss.Record(vectors, vector with { Attempt = "second-attempt" }).MissId
            .ShouldBe("98d6c07b51b32ecff6687e9fcf9c0bfd3dd4c77482d0789128ede5a100a6fa10", "P.identity exact attempt separates observations");
        var identical = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => Task.Run(() => RcSelectionMiss.Record(output, observation), cancellationToken)));
        identical.Select(record => record.MissId).Distinct().Single().ShouldBe(records[0].GetProperty("missId").GetString(), "P.atomic");
        Should.Throw<InvalidDataException>(() => RcSelectionMiss.Record(output, observation with { Attribution = "wrongly-dropped" }));
        File.ReadAllBytes(Path.Combine(output, firstPath)).ShouldBe(firstBytes, "P.conflict");
        var revision = RcSelectionMiss.AppendDisposition(output, records[0].GetProperty("missId").GetString()!,
            DigestBytes(firstBytes), new("unresolved", null, null, null, null, null, null));
        using var revisionDocument = Read(output, revision.Path);
        revisionDocument.RootElement.GetProperty("status").GetString().ShouldBe("open", "P.revision");
        revisionDocument.RootElement.GetProperty("observationSha").GetString().ShouldBe(DigestBytes(firstBytes));
        File.ReadAllBytes(Path.Combine(output, firstPath)).ShouldBe(firstBytes, "P.revision immutable observation");
        var unavailable = world.Output("unavailable");
        Directory.CreateDirectory(unavailable);
        File.WriteAllText(Path.Combine(unavailable, "misses"), "fixture fault");
        Should.Throw<IOException>(() => RcSelectionMiss.Record(unavailable, observation));
        var manifest = string.Concat(records.OrderBy(record => record.GetProperty("missId").GetString(), StringComparer.Ordinal)
            .Select(record => record.GetProperty("missId").GetString() + " " +
                DigestBytes(File.ReadAllBytes(Path.Combine(output, record.GetProperty("path").GetString()!))) + "\n"));
        verdict.RootElement.GetProperty("missRecordSetDigest").GetString().ShouldBe(
            DigestBytes(System.Text.Encoding.UTF8.GetBytes(manifest)), "P.evidence set digest");

        var clean = world.Output("clean");
        ReplayProvenSelection(world, world.Input("clean", fails: false), clean, true).ShouldBe(0, "P.clean");
        using var cleanVerdict = Read(clean, "verdict.json");
        cleanVerdict.RootElement.GetProperty("enables").GetBoolean().ShouldBeFalse();
        var candidateOnly = world.Output("candidate-only-approval");
        var approvalRoot = RcIncrementalCut.IncrementalRoot(candidateOnly);
        Directory.CreateDirectory(approvalRoot);
        File.WriteAllText(Path.Combine(approvalRoot, "approval.json"), JsonSerializer.Serialize(new
        {
            format = RcIncrementalCut.ApprovalFormat, provenSubtraction = true,
            replayVerdictDigest = DigestBytes(File.ReadAllBytes(Path.Combine(clean, "verdict.json"))),
            selectorHash = cleanVerdict.RootElement.GetProperty("selectorHash").GetString(),
            trustPolicyHash = cleanVerdict.RootElement.GetProperty("trustPolicyHash").GetString(),
            shadowRunIds = new[] { "fixture-not-authority-1", "fixture-not-authority-2" },
        }));
        var denied = RcIncrementalCut.Admit(candidateOnly, world.Sha(3), "", DateTime.UnixEpoch, world.Repository);
        denied.Admitted.ShouldBeFalse("P.criterion candidate proof verdict cannot activate");
        denied.Reason.ShouldBe("incremental-not-admitted:approval-selector-mismatch");
        var initial = "predictions/3.json";
        var resolved = "predictions/3.resolved.json";
        using var access = Read(output, "runs/3/outcome-access.json");
        access.RootElement.EnumerateArray().Select(read =>
            read.GetProperty("purpose").GetString() + "/" + read.GetProperty("chunk").GetString())
            .SequenceEqual(["score/left", "score/right"], StringComparer.Ordinal).ShouldBeTrue("P.outcome access");
        File.ReadAllBytes(Path.Combine(clean, initial)).ShouldBe(File.ReadAllBytes(Path.Combine(output, initial)), "P.blind");
        File.ReadAllBytes(Path.Combine(clean, resolved)).ShouldBe(File.ReadAllBytes(Path.Combine(output, resolved)), "P.resolved");
        DigestBytes(File.ReadAllBytes(Path.Combine(output, resolved))).ShouldBe(
            verdict.RootElement.GetProperty("resolvedPredictionDigests").GetProperty("3").GetString(), "P.resolved digest");

        var ordinary = world.Output("ordinary");
        ReplayProvenSelection(world, input, ordinary, false).ShouldBe(3, "P.legacy");
        using var ordinaryVerdict = Read(ordinary, "verdict.json");
        ordinaryVerdict.RootElement.GetProperty("caughtFailures").GetArrayLength().ShouldBe(1);
        ordinaryVerdict.RootElement.GetProperty("missedFailures").GetArrayLength().ShouldBe(0);
    }

    [Test, Timeout(9_000)]
    public async Task ProvenSubtractionReplayRejectsInvalidArtifactsAndCohorts(CancellationToken cancellationToken)
    {
        using var world = new ProvenReplayWorld();
        await world.InitializeAsync(cancellationToken);
        var corrupt = world.Input("corrupt", fails: false);
        File.AppendAllText(Path.Combine(corrupt, "type-graphs", world.Sha(3) + ".json"), "\n");
        ReplayProvenSelection(world, corrupt, world.Output("corrupt"), true).ShouldBe(2, "P.invalid artifact");

        var gap = world.Input("gap", fails: false);
        var custodyPath = Path.Combine(gap, "custody.json");
        var custody = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(custodyPath))!;
        custody["runs"]!.AsArray().RemoveAt(2);
        File.WriteAllText(custodyPath, custody.ToJsonString());
        ProvenReplayWorld.Seal(gap);
        ReplayProvenSelection(world, gap, world.Output("gap"), true).ShouldBe(2, "P.cohort");

        var duplicate = world.Input("duplicate-attempt", fails: false);
        var duplicatePath = Path.Combine(duplicate, "runs", "r3000000", "attempt", "selection-attempt.json");
        File.WriteAllText(duplicatePath, JsonSerializer.Serialize(new
        {
            runId = ProvenReplayWorld.RunId(3), attemptId = ProvenReplayWorld.RunId(2),
        }));
        ProvenReplayWorld.Seal(duplicate);
        ReplayProvenSelection(world, duplicate, world.Output("duplicate-attempt"), true).ShouldBe(2, "P.cohort duplicate attempt");

        var lostRoster = world.Input("lost-roster", fails: false);
        Overlay(lostRoster, "r3000000/logs/groups-planned.json", doc => doc["chunks"]!.AsArray().RemoveAt(0));
        ReplayProvenSelection(world, lostRoster, world.Output("lost-roster"), true).ShouldBe(2, "P.cohort lost planned member");
    }

    [Test, Timeout(25_000)]
    public async Task ProvenSubtractionReplayRefusesUnverifiableOutcomes(CancellationToken cancellationToken)
    {
        using var world = new ProvenReplayWorld();
        await world.InitializeAsync(cancellationToken);
        var unknown = world.Output("unknown");
        ReplayProvenSelection(world, world.Input("unknown", fails: true, carryHasEvidence: false), unknown, true).ShouldBe(1, "P.unknown");
        using var unknownVerdict = Read(unknown, "verdict.json");
        var unknownFile = unknownVerdict.RootElement.GetProperty("missRecords")[0].GetProperty("path").GetString()!;
        using var unknownRecord = Read(unknown, unknownFile);
        unknownRecord.RootElement.GetProperty("observation").GetProperty("case").GetProperty("kind").GetString()
            .ShouldBe("chunk-unknown-case");

        var wrongTarget = world.Input("wrong-target", fails: true);
        Overlay(wrongTarget, "r3000000/logs/groups-planned.json", doc => doc["chunks"]![0]!["chunk"] = "renumbered-left");
        Overlay(wrongTarget, "r3000000/logs/summary.json", doc =>
        {
            doc["suites"]![0]!["chunk"] = "renumbered-left";
            doc["suites"]![0]!["architecture"] = "arm64";
        });
        var wrongTargetOutput = world.Output("wrong-target");
        ReplayProvenSelection(world, wrongTarget, wrongTargetOutput, true).ShouldBe(1, "P.unknown wrong-target observation");
        using var targetVerdict = Read(wrongTargetOutput, "verdict.json");
        var invalidCarry = targetVerdict.RootElement.GetProperty("missRecords")[0].GetProperty("path").GetString()!;
        using var invalidRecord = Read(wrongTargetOutput, invalidCarry);
        var invalidObservation = invalidRecord.RootElement.GetProperty("observation");
        invalidObservation.GetProperty("chunk").GetString().ShouldBe("renumbered-left", "P.roster presentation ordinal");
        // v1 content vector for native antiphon cases (L, L.LeftTests, M, l1/l2).
        invalidObservation.GetProperty("contentDigest").GetString().ShouldBe(
            "1645f49fecef4f856b6ac7d4d11c81d1ecb417ffa405ae8084673428df05eeb5", "P.roster stable content");
        invalidObservation.GetProperty("case").GetProperty("kind").GetString()
            .ShouldBe("chunk-unknown-case", "P.unknown wrong-target evidence cannot name a case");

        var timeout = world.Input("timeout", fails: false);
        Overlay(timeout, "r3000000/logs/summary.json", doc => doc["suites"]![0]!["timedOut"] = true);
        var timeoutOutput = world.Output("timeout");
        ReplayProvenSelection(world, timeout, timeoutOutput, true).ShouldBe(1, "P.unsafe timed out carry");
        using var timeoutVerdict = Read(timeoutOutput, "verdict.json");
        timeoutVerdict.RootElement.GetProperty("runs")[2].GetProperty("timeouts").GetInt32().ShouldBe(1);

        var skipped = world.Input("mixed-skipped", fails: false);
        var skippedTrxPath = Path.Combine(skipped, "runs", "r3000000", "logs", "left.trx");
        var skippedTrx = XDocument.Load(skippedTrxPath);
        skippedTrx.Descendants().Single(element => element.Name.LocalName == "UnitTestResult" &&
            (string?)element.Attribute("testId") == "l2").SetAttributeValue("outcome", "NotExecuted");
        skippedTrx.Save(skippedTrxPath);
        ProvenReplayWorld.Seal(skipped);
        var skippedOutput = world.Output("mixed-skipped");
        ReplayProvenSelection(world, skipped, skippedOutput, true).ShouldBe(1, "P.unsafe mixed passed and skipped");
        using var skippedVerdict = Read(skippedOutput, "verdict.json");
        skippedVerdict.RootElement.GetProperty("runs")[2].GetProperty("skipped").GetInt32().ShouldBe(1);
    }

    [Test, Timeout(23_000)]
    public async Task ProvenSubtractionReplayPreservesRepairAndRenameEvidence(CancellationToken cancellationToken)
    {
        using var world = new ProvenReplayWorld();
        await world.InitializeAsync(cancellationToken, includeRenameEndpoint: true);
        var output = world.Output("missed-for-repair");
        ReplayProvenSelection(world, world.Input("missed-for-repair", fails: true), output, true).ShouldBe(1, "P.revision initial miss");
        using var verdict = Read(output, "verdict.json");
        var record = verdict.RootElement.GetProperty("missRecords")[0];
        var firstPath = record.GetProperty("path").GetString()!;
        var firstBytes = File.ReadAllBytes(Path.Combine(output, firstPath));

        var owed = world.Input("owed", fails: true);
        var thirdPlan = Path.Combine(owed, "runs", "r3000000", "logs", "groups-planned.json");
        var chunkPlan = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(thirdPlan))!;
        chunkPlan["census"] = JsonSerializer.SerializeToNode(
            new[] { new { reader = "L.LeftTests", globs = new[] { ProvenReplayWorld.Changed } } });
        File.WriteAllText(thirdPlan, chunkPlan.ToJsonString());
        ProvenReplayWorld.Seal(owed);
        var owedOutput = world.Output("owed");
        ReplayProvenSelection(world, owed, owedOutput, true).ShouldBe(3, "P.opportunity all owed");
        using var owedVerdict = Read(owedOutput, "verdict.json");
        owedVerdict.RootElement.GetProperty("proofDependentCarries").GetInt32().ShouldBe(0);
        owedVerdict.RootElement.GetProperty("caughtFailures").GetArrayLength().ShouldBe(1);
        using var repaired = Read(owedOutput, "predictions/3.resolved.json");
        var caught = repaired.RootElement.GetProperty("predictions")[0];
        caught.GetProperty("decision").GetString().ShouldBe("run", "P.absent-reader repaired execute");
        caught.GetProperty("reasons").EnumerateArray().Select(x => x.GetString())
            .ShouldContain("runtime-reader:" + ProvenReplayWorld.Changed, "P.absent-reader repaired reason");
        var repair = RcSelectionMiss.AppendDisposition(output, record.GetProperty("missId").GetString()!,
            DigestBytes(firstBytes), new("absent-reader", nameof(ProvenSubtractionReplayPreservesRepairAndRenameEvidence), null, null,
                "runtime-reader:" + ProvenReplayWorld.Changed, null,
                DigestBytes(File.ReadAllBytes(Path.Combine(owedOutput, "verdict.json")))));
        using var repairRevision = Read(output, repair.Path);
        repairRevision.RootElement.GetProperty("status").GetString().ShouldBe("open", "P.revision reader classification awaits qualification");
        repairRevision.RootElement.GetProperty("disposition").GetProperty("attribution").GetString().ShouldBe("absent-reader");
        File.ReadAllBytes(Path.Combine(output, firstPath)).ShouldBe(firstBytes, "P.revision repaired output preserves original");

        var withDecoy = world.Input("repaired-decoy", fails: true, extraDecoy: true);
        Overlay(withDecoy, "r3000000/logs/groups-planned.json", doc => doc["census"] = JsonSerializer.SerializeToNode(
            new[] { new { reader = "L.LeftTests", globs = new[] { ProvenReplayWorld.Changed } } }));
        var decoyOutput = world.Output("repaired-decoy");
        ReplayProvenSelection(world, withDecoy, decoyOutput, true).ShouldBe(0, "P.absent-reader fixed with one genuine safe carry");
        using var decoyVerdict = Read(decoyOutput, "verdict.json");
        var decoyScore = decoyVerdict.RootElement.GetProperty("runs")[2].GetProperty("matrix");
        decoyScore.GetProperty("carry/clean").GetInt32().ShouldBe(1, "P.absent-reader unaffected decoy carries");
        decoyScore.GetProperty("run/FAIL").GetInt32().ShouldBe(1, "P.absent-reader damaged consumer executes");
        decoyVerdict.RootElement.GetProperty("proofDependentCarries").GetInt32().ShouldBe(1);

        var renamedOutput = world.Output("rename");
        ReplayProvenSelection(world, world.Input("rename", fails: true, rename: true), renamedOutput, true).ShouldBe(1, "P.evidence rename carry remains unsafe");
        using var renamedVerdict = Read(renamedOutput, "verdict.json");
        var renamedRecord = renamedVerdict.RootElement.GetProperty("missRecords")[0].GetProperty("path").GetString()!;
        using var rename = Read(renamedOutput, renamedRecord);
        rename.RootElement.GetProperty("observation").GetProperty("diff").EnumerateArray().Select(row =>
            (row.GetProperty("status").GetString(), row.GetProperty("oldPath").GetString(), row.GetProperty("newPath").GetString()))
            .SequenceEqual(new[]
            {
                ("M", ProvenReplayWorld.Changed, ProvenReplayWorld.Changed),
                ("R100", ProvenReplayWorld.RenamedFrom, ProvenReplayWorld.RenamedTo),
            }).ShouldBeTrue("P.evidence both rename ends and the other change");
    }

    [Test, Timeout(18_000)]
    public async Task ProvenSubtractionReplayResolvesGateAndFailureDebt(CancellationToken cancellationToken)
    {
        using var world = new ProvenReplayWorld();
        await world.InitializeAsync(cancellationToken, includeGateEndpoints: true);
        var holdPass = world.Output("gate-pass");
        var holdRed = world.Output("gate-red");
        ReplayProvenSelection(world, world.Input("gate-pass", fails: false, held: true), holdPass, true).ShouldBe(0, "P.held gate pass");
        ReplayProvenSelection(world, world.Input("gate-red", fails: false, held: true, gateRed: true), holdRed, true).ShouldBe(0, "P.held gate red");
        File.ReadAllBytes(Path.Combine(holdPass, "predictions/5.json"))
            .ShouldBe(File.ReadAllBytes(Path.Combine(holdRed, "predictions/5.json")), "P.held sealed before own outcomes");
        using var heldPrediction = Read(holdPass, "predictions/5.json");
        var held = heldPrediction.RootElement.GetProperty("predictions")[0];
        held.GetProperty("decision").GetString().ShouldBe("held", "P.held initial");
        held.GetProperty("ifGatePasses").GetString().ShouldBe("carry");
        held.GetProperty("ifGateFails").GetString().ShouldBe("run");
        using var heldAccess = Read(holdPass, "runs/5/outcome-access.json");
        heldAccess.RootElement.EnumerateArray().Select(read =>
            read.GetProperty("purpose").GetString() + "/" + read.GetProperty("chunk").GetString())
            .SequenceEqual(["own-gate/right", "score/left", "score/right"], StringComparer.Ordinal)
            .ShouldBeTrue("P.held own-gate only until resolved seal");
        using var passPartition = Read(holdPass, "predictions/5.resolved.json");
        using var redPartition = Read(holdRed, "predictions/5.resolved.json");
        passPartition.RootElement.GetProperty("predictions")[0].GetProperty("decision").GetString().ShouldBe("carry", "P.held resolved pass");
        redPartition.RootElement.GetProperty("predictions")[0].GetProperty("decision").GetString().ShouldBe("run", "P.held resolved fail");
        var direct = world.Output("gate-direct");
        ReplayProvenSelection(world, world.Input("gate-direct", fails: false, held: true, direct: true), direct, true).ShouldBe(0, "P.held direct obligation");
        using var directPartition = Read(direct, "predictions/5.resolved.json");
        directPartition.RootElement.GetProperty("predictions")[0].GetProperty("decision").GetString().ShouldBe("run", "P.held direct hit remains owed");

        var debt = world.Input("later-debt", fails: false);
        var priorTrx = Path.Combine(debt, "runs", "r2000000", "logs", "left.trx");
        NightlyFixture.WriteTrx(priorTrx,
            [new("l1", "L.LeftTests", "M", "Failed"), new("l2", "L.LeftTests", "M", "Failed")]);
        Overlay(debt, "r2000000/logs/summary.json", doc =>
        {
            doc["suites"]![0]!["result"] = "FAIL";
            doc["suites"]![0]!["exitCode"] = 1;
        });
        var debtOutput = world.Output("later-debt");
        ReplayProvenSelection(world, debt, debtOutput, true).ShouldBe(3, "C.debt prior failure leaves no proof-only carry");
        using var debtPartition = Read(debtOutput, "predictions/3.resolved.json");
        var debtPredictions = debtPartition.RootElement.GetProperty("predictions").EnumerateArray().ToArray();
        debtPredictions.Select(item => item.GetProperty("decision").GetString()).ToArray().ShouldBe(["run", "run"], "C.debt owed set");
        debtPredictions[0].GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString())
            .ShouldContain("open-failure", "C.debt survives subtraction");
    }

    private static void Overlay(string input, string relative, Action<System.Text.Json.Nodes.JsonNode> edit)
    {
        var path = Path.Combine(input, "runs", relative);
        var document = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        edit(document);
        File.WriteAllText(path, document.ToJsonString());
        ProvenReplayWorld.Seal(input);
    }

    private static int ReplayProvenSelection(ProvenReplayWorld world, string custody, string destination, bool enabled)
    {
        string[] arguments = ["--input-root", custody, "--output", destination,
            "--as-of", ProvenReplayWorld.AsOf, "--repository-root", world.Repository];
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var exit = RcSelectionReplay.Run(enabled ? [..arguments, "--proven-subtraction"] : arguments, stdout, stderr);
        Console.WriteLine($"C2076 replay case={Path.GetFileName(destination)} elapsedMs={clock.ElapsedMilliseconds}");
        if (exit == 2) Console.WriteLine(stderr.ToString());
        return exit;
    }

    private static JsonDocument Read(string root, string name) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, name)));
    private static string DigestBytes(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

    [Test, Timeout(13_000)]
    public async Task ArchivedWrongAliasWinnerUsesTheReplayScorerAndLeavesAnOpenRepair(CancellationToken cancellationToken)
    {
        using var world = new ProvenReplayWorld(alias: true);
        await world.InitializeAsync(cancellationToken);
        var oldRoot = world.Output("archived-alias");
        var newRoot = world.Output("repaired-alias");
        Run(world.Input("archived", fails: true, historical: true), oldRoot).ShouldBe(1, "P.wrongly-dropped archived miss");
        Run(world.Input("repaired", fails: true), newRoot).ShouldBe(3, "P.wrongly-dropped repaired run has no extra carry");
        using var archived = Read(oldRoot, "verdict.json");
        using var repaired = Read(newRoot, "verdict.json");
        archived.RootElement.GetProperty("runs")[2].GetProperty("matrix").GetProperty("carry/FAIL").GetInt32().ShouldBe(1);
        repaired.RootElement.GetProperty("runs")[2].GetProperty("matrix").GetProperty("run/FAIL").GetInt32().ShouldBe(1);
        repaired.RootElement.GetProperty("missRecords").GetArrayLength().ShouldBe(0);
        var records = archived.RootElement.GetProperty("missRecords").EnumerateArray().ToArray();
        records.Length.ShouldBe(2);
        using var oldGraph = Read(oldRoot, "frozen/type-graphs/" + world.Sha(3) + ".json");
        using var newGraph = Read(newRoot, "frozen/type-graphs/" + world.Sha(3) + ".json");
        var oldSite = AliasSite(oldGraph);
        var newSite = AliasSite(newGraph);
        oldSite.GetProperty("origin").GetProperty("candidates").EnumerateArray().Select(path => path.GetString())
            .SequenceEqual(["tests/L/A.cs", ProvenReplayWorld.Changed]).ShouldBeTrue("P.wrongly-dropped frozen candidates");
        oldSite.GetProperty("winners")[0].GetString().ShouldBe("tests/L/A.cs", "P.wrongly-dropped archived winner");
        newSite.GetProperty("winners")[0].GetString().ShouldBe(ProvenReplayWorld.Changed, "P.wrongly-dropped repaired winner");
        var repairDigest = ProvenReplayWorld.Digest(File.ReadAllBytes(Path.Combine(newRoot, "verdict.json")));
        foreach (var record in records)
        {
            var relative = record.GetProperty("path").GetString()!;
            var bytes = File.ReadAllBytes(Path.Combine(oldRoot, relative));
            using var observed = JsonDocument.Parse(bytes);
            observed.RootElement.GetProperty("observation").GetProperty("attribution").GetString().ShouldBe("unresolved");
            var revision = RcSelectionMiss.AppendDisposition(oldRoot, record.GetProperty("missId").GetString()!,
                ProvenReplayWorld.Digest(bytes), new("wrongly-dropped", nameof(ArchivedWrongAliasWinnerUsesTheReplayScorerAndLeavesAnOpenRepair),
                    null, null, "bind the alias RHS against the pinned compiler inputs", null, repairDigest));
            using var disposition = Read(oldRoot, revision.Path);
            disposition.RootElement.GetProperty("status").GetString().ShouldBe("open");
            disposition.RootElement.GetProperty("disposition").GetProperty("rerunDigest").GetString().ShouldBe(repairDigest);
            File.ReadAllBytes(Path.Combine(oldRoot, relative)).ShouldBe(bytes);
        }

        int Run(string input, string output)
        {
            using var logger = new StringWriter();
            var exit = RcSelectionReplay.Run(["--input-root", input, "--output", output, "--repository-root", world.Repository,
                "--as-of", ProvenReplayWorld.AsOf, "--proven-subtraction"], logger, logger);
            if (exit == 2) Console.WriteLine(logger.ToString());
            return exit;
        }

        static JsonDocument Read(string root, string relative) => JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root, relative)));
        static JsonElement AliasSite(JsonDocument graph) => graph.RootElement.GetProperty("typeSites").EnumerateArray().Single(site =>
            site.GetProperty("origin").GetProperty("consumer").GetProperty("fullName").GetString() == "L.LeftTests" &&
            site.GetProperty("origin").GetProperty("name").GetString() == "Pick");
    }

    // CARD-1836 S5 (CP-5, V-5, R-4, R-5, G-14..G-19): the offline five-run replay exposes a hidden miss and blocks activation.
    [Test, Timeout(60_000)]
    public void Five_run_replay_exposes_a_missed_failure_before_activation(CancellationToken cancellationToken)
    {
        using var world = new ReplayWorld();
        var rows = new Antiphon.Tests.Scripts.Nightly.NightlyCaseRows();
        var asOf = "2026-11-25T12:00:00Z";
        (int Exit, string Out, string Err) Replay(string root, string output)
        {
            var o = new StringWriter();
            var e = new StringWriter();
            var exit = RcSelectionReplay.Run(["--input-root", root, "--output", output, "--as-of", asOf, "--repository-root", world.Repo], o, e);
            return (exit, o.ToString(), e.ToString());
        }
        JsonElement Doc(string path)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.Clone();
        }

        var missedRoot = world.Root("missed", ReplayWorld.Missed);
        var missedTree = ReplayWorld.TreeHash(missedRoot);
        var outsideBefore = Directory.GetFileSystemEntries(world.Fixture.Root).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var missedOut = world.Fixture.PathOf("out-missed");
        var missed = Replay(missedRoot, missedOut);
        var verdict = Doc(Path.Combine(missedOut, "verdict.json"));
        var missedRows = verdict.GetProperty("missedFailures").EnumerateArray().ToArray();
        var k = world.Chunk("K");
        rows.Check(missed.Exit == 1 && verdict.GetProperty("activation").GetString() == "blocked" && missedRows.Length == 1
            && missedRows[0].GetProperty("run").GetString() == world.RunId(3) && missedRows[0].GetProperty("suite").GetString() == "antiphon"
            && missedRows[0].GetProperty("contentDigest").GetString() == k.Digest && missedRows[0].GetProperty("case").GetString() == "k1"
            && missedRows[0].GetProperty("sha").GetString() == world.Sha(3) && missedRows[0].GetProperty("baseline").GetString() == world.Sha(2)
            && missedRows[0].GetProperty("reason").GetString()!.Length > 0 && missedRows[0].GetProperty("trx").GetString() == "runs/" + world.Run8(3) + "/logs/antiphon-" + k.Id(3) + ".trx",
            "s5-missed-failure-listed", "exit=" + missed.Exit + " " + missed.Err + " " + verdict.GetProperty("missedFailures"));

        var unsafeRows = verdict.GetProperty("unsafeCarries").EnumerateArray().ToArray();
        var o5 = world.Chunk("O");
        rows.Check(unsafeRows.Any(u => u.GetProperty("run").GetString() == world.RunId(5) && u.GetProperty("contentDigest").GetString() == o5.Digest
            && u.GetProperty("actual").GetString() == "unknown"), "s5-unknown-is-unsafe", verdict.GetProperty("unsafeCarries").ToString());

        var caught = verdict.GetProperty("caughtFailures").EnumerateArray().Select(c => c.GetProperty("run").GetString() + "/" + c.GetProperty("contentDigest").GetString()).ToArray();
        rows.Check(caught.Contains(world.RunId(2) + "/" + world.Chunk("A").Digest) && caught.Contains(world.RunId(4) + "/" + world.Chunk("E").Digest)
            && verdict.GetProperty("failureRecall").ValueKind == JsonValueKind.Number && Math.Abs(verdict.GetProperty("failureRecall").GetDouble() - 0.75) < 1e-9,
            "s5-caught-failures", string.Join(",", caught) + " recall=" + verdict.GetProperty("failureRecall"));

        var r4Caught = verdict.GetProperty("caughtFailures").EnumerateArray().Where(c => c.GetProperty("run").GetString() == world.RunId(4)).ToArray();
        var p4 = Doc(Path.Combine(missedOut, "predictions", "4.json")).GetProperty("predictions").EnumerateArray().ToArray();
        rows.Check(r4Caught.Length == 1 && r4Caught[0].GetProperty("chunk").GetString() == world.Chunk("E").Id(4) && world.Chunk("E").Id(4) != world.Chunk("E").Id(3)
            && r4Caught[0].GetProperty("contentDigest").GetString() == world.Chunk("E").Digest, "s5-content-not-ordinal", string.Join(",", r4Caught.Select(c => c.ToString())));

        var k4 = p4.Single(p => p.GetProperty("contentDigest").GetString() == k.Digest);
        rows.Check(k4.GetProperty("chunk").GetString() == k.Id(4) && k4.GetProperty("decision").GetString() == "carry" && k4.GetProperty("originRun").GetString() == world.RunId(2),
            "s5-chain-hides-skipped-results", k4.ToString());

        var teacher = Doc(Path.Combine(missedOut, "teacher-forced.json"));
        var teacherK4 = teacher.GetProperty("runs")[3].GetProperty("decisions").EnumerateArray().Single(d => d.GetProperty("contentDigest").GetString() == k.Digest);
        rows.Check(teacherK4.GetProperty("decision").GetString() == "run" && missedRows.Length == 1 && !File.ReadAllText(Path.Combine(missedOut, "verdict.json")).Contains("teacher", StringComparison.Ordinal),
            "s5-teacher-forced-separate", teacherK4.ToString());
        cancellationToken.ThrowIfCancellationRequested();

        // Blinding: flip or remove the outcome of every chunk each run carried; the sealed predictions keep their bytes.
        var carried = Enumerable.Range(1, 5).ToDictionary(i => i, i => Doc(Path.Combine(missedOut, "predictions", i + ".json")).GetProperty("predictions").EnumerateArray()
            .Where(p => p.GetProperty("decision").GetString() == "carry").Select(p => p.GetProperty("chunk").GetString()!).ToHashSet());
        var perturbedRoot = world.Root("perturbed", ReplayWorld.Missed, carried);
        var perturbedOut = world.Fixture.PathOf("out-perturbed");
        var perturbed = Replay(perturbedRoot, perturbedOut);
        var same = Directory.GetFiles(Path.Combine(missedOut, "predictions")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToArray();
        rows.Check(carried.Values.Sum(c => c.Count) > 0 && same.Length >= 5
            && same.SequenceEqual(Directory.GetFiles(Path.Combine(perturbedOut, "predictions")).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal))
            && same.All(n => File.ReadAllBytes(Path.Combine(missedOut, "predictions", n!)).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(perturbedOut, "predictions", n!))))
            && ReplayWorld.TreeHash(perturbedRoot) != ReplayWorld.TreeHash(missedRoot), "s5-blind-perturbation", "exit=" + perturbed.Exit + " " + perturbed.Err);

        var zeroRoot = world.Root("zero", ReplayWorld.AllFull);
        var zeroOut = world.Fixture.PathOf("out-zero");
        var zero = Replay(zeroRoot, zeroOut);
        rows.Check(zero.Exit == 3 && Doc(Path.Combine(zeroOut, "verdict.json")).GetProperty("reason").GetString() == "insufficient:no-carry-opportunity", "s5-zero-carry-insufficient",
            "exit=" + zero.Exit + " " + zero.Err);

        var cleanRoot = world.Root("clean", ReplayWorld.Clean);
        var cleanTree = ReplayWorld.TreeHash(cleanRoot);
        var beforeClean = Directory.GetFileSystemEntries(world.Fixture.Root).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var cleanOut = world.Fixture.PathOf("out-clean");
        var clean = Replay(cleanRoot, cleanOut);
        var cleanVerdict = Doc(Path.Combine(cleanOut, "verdict.json"));
        var afterClean = Directory.GetFileSystemEntries(world.Fixture.Root).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        rows.Check(clean.Exit == 0 && cleanVerdict.GetProperty("activation").GetString() == "criterion-met" && !cleanVerdict.GetProperty("enables").GetBoolean()
            && afterClean.SequenceEqual(beforeClean.Append(cleanOut).OrderBy(p => p, StringComparer.Ordinal)) && ReplayWorld.TreeHash(cleanRoot) == cleanTree,
            "s5-clean-cohort-admits", "exit=" + clean.Exit + " " + clean.Err + " " + cleanVerdict.GetProperty("reason"));

        var inside = Replay(missedRoot, Path.Combine(missedRoot, "out"));
        rows.Check(ReplayWorld.TreeHash(missedRoot) == missedTree && inside.Exit == 2 && inside.Err.Contains("output-inside-input", StringComparison.Ordinal)
            && !Directory.Exists(Path.Combine(missedRoot, "out")) && outsideBefore.All(p => Directory.Exists(p) || File.Exists(p)), "s5-input-read-only", inside.Err);

        var runs = verdict.GetProperty("runs").EnumerateArray().ToArray();
        rows.Check(runs.Length == 5 && runs.All(r => r.GetProperty("predictedRun").GetInt32() + r.GetProperty("predictedCarry").GetInt32() + r.GetProperty("predictedHeld").GetInt32()
            == r.GetProperty("universe").GetInt32() && r.GetProperty("universe").GetInt32() == 6 && r.GetProperty("reconciled").GetBoolean() && r.GetProperty("predictedHeld").GetInt32() == 0),
            "s5-reconciles-universe", string.Join(" ", runs.Select(r => r.GetProperty("predictedRun") + "+" + r.GetProperty("predictedCarry"))));

        rows.ShouldAllPass(11, "s5-missed-failure-listed", "s5-unknown-is-unsafe", "s5-caught-failures", "s5-content-not-ordinal", "s5-blind-perturbation",
            "s5-chain-hides-skipped-results", "s5-teacher-forced-separate", "s5-zero-carry-insufficient", "s5-clean-cohort-admits", "s5-input-read-only", "s5-reconciles-universe");
    }

    private static readonly string[] RequiredRuns =
    [
        "6591e378", "d88de419", "0b292fa4", "97150a0b", "34d41b7e", "f2de5b30", "a6278259",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false,
    };

    // In-process: the HistoricalSelectionSoundness case (RcInputOwnersMapTests) replays the corpus through the committed
    // owner inventory (Antiphon.Nightly.RcInputInventory), with its three rows: contract, unowned and composer-inputs.
    [Test, Timeout(150_000)]
    public void Recorded_owner_map_status_matches_the_committed_map(CancellationToken cancellationToken)
    {
        _ = LoadCorpus(RepoRoot());
        cancellationToken.ThrowIfCancellationRequested();
        RcInputOwnersMapTests.HistoricalSelectionSoundness();
    }

    [Test]
    public void Owner_globs_match_paths_with_repeated_directory_names()
    {
        var pattern = "tests/" + string.Concat(Enumerable.Repeat("**/part/", 16));
        var path = "tests/" + string.Concat(Enumerable.Repeat("part/", 34));
        var owner = RcInputInventory.Pattern(pattern + "marker/Final.cs");

        owner.IsMatch(path + "changed/Final.cs").ShouldBeFalse();
        owner.IsMatch(path + "marker/Final.cs").ShouldBeTrue();

        var longPrefix = new string('x', 2046);
        RcInputInventory.Pattern(longPrefix + "?*").IsMatch(longPrefix + "z").ShouldBeTrue();
        RcInputInventory.Pattern("**/NuGet.config", ignoreCase: true).IsMatch("nested/nuget.CONFIG").ShouldBeTrue();
        RcInputInventory.Pattern("src/**/a?*.cs").IsMatch("src/ab.cs").ShouldBeTrue();
        RcInputInventory.Pattern("src/**/a?*.cs").IsMatch("src/dir/a/b.cs").ShouldBeFalse();
    }

    [Test, Timeout(60_000)]
    public void Project_closure_misses_match_the_recorded_known_unsound_set()
    {
        var root = RepoRoot();
        var corpus = LoadCorpus(root);
        var graph = ProjectGraph.Load(root);
        var missed = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var item in corpus.Cases!)
        {
            var status = graph.Selects(item.SuiteProject, item.ChangedPaths!) ? "selected" : "missed";
            status.ShouldBe(item.M1, item.Id);
            if (status == "missed")
                missed.Add(item.Id);
        }

        var recorded = new SortedSet<string>(corpus.KnownUnsound!, StringComparer.Ordinal);
        string.Join(",", missed).ShouldBe(string.Join(",", recorded));
    }

    private static Corpus LoadCorpus(string root)
    {
        var path = Path.Combine(root, "tests", "fixtures", "incremental-selection-soundness.json");
        var corpus = JsonSerializer.Deserialize<Corpus>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException("soundness fixture did not parse.");
        if (corpus.SchemaVersion != 1)
            throw new InvalidOperationException("soundness fixture schemaVersion must be 1.");
        if (corpus.KnownUnsound is null || corpus.Cases is null)
            throw new InvalidOperationException("soundness fixture is missing knownUnsound or cases.");
        if (corpus.Cases.Length is < 1 or > 12)
            throw new InvalidOperationException("soundness fixture must hold 1 to 12 cases.");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var runs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in corpus.Cases)
        {
            if (string.IsNullOrWhiteSpace(item.Id) || !ids.Add(item.Id))
                throw new InvalidOperationException("soundness case id is missing or duplicated: " + item.Id);
            if (item.Run is null || !Regex.IsMatch(item.Run, "^[0-9a-f]{8}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(item.Id + " run is not an 8-hex qualification id.");
            runs.Add(item.Run);
            if (item.IntroducingCommit is null || !Regex.IsMatch(item.IntroducingCommit, "^[0-9a-f]{40}$", RegexOptions.CultureInvariant))
                throw new InvalidOperationException(item.Id + " introducing commit is not 40 hex.");
            if (string.IsNullOrWhiteSpace(item.FailingClass) || string.IsNullOrWhiteSpace(item.Reason))
                throw new InvalidOperationException(item.Id + " needs a failing class and a reason.");
            if (item.Note is null || !item.Note.StartsWith("docs/", StringComparison.Ordinal)
                || !File.Exists(Path.Combine(root, item.Note.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidOperationException(item.Id + " note is missing: " + item.Note);
            if (item.CurrentMap is not ("selected" or "missed") || item.M1 is not ("selected" or "missed"))
                throw new InvalidOperationException(item.Id + " status must be selected or missed.");
            if (item.SuiteProject is null || !item.SuiteProject.EndsWith(".csproj", StringComparison.Ordinal)
                || item.SuiteProject.Contains('\\') || !File.Exists(Path.Combine(root, item.SuiteProject.Replace('/', Path.DirectorySeparatorChar))))
                throw new InvalidOperationException(item.Id + " suite project is missing: " + item.SuiteProject);
            if (item.ChangedPaths is null || item.ChangedPaths.Length == 0)
                throw new InvalidOperationException(item.Id + " has no changed paths.");

            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (var changed in item.ChangedPaths)
            {
                if (string.IsNullOrWhiteSpace(changed) || changed.Contains('\\') || changed.StartsWith('/')
                    || changed.Split('/').Contains("..") || !paths.Add(changed))
                    throw new InvalidOperationException(item.Id + " has an invalid changed path: " + changed);
            }

            var projectName = item.SuiteProject[(item.SuiteProject.LastIndexOf('/') + 1)..^".csproj".Length];
            if (!string.Equals(item.FailingClass, projectName, StringComparison.Ordinal))
            {
                var suiteDirectory = Path.Combine(root, Path.GetDirectoryName(item.SuiteProject.Replace('/', Path.DirectorySeparatorChar))!);
                if (!ClassFileExists(suiteDirectory, item.FailingClass))
                    throw new InvalidOperationException(item.Id + " failing class is not in " + item.SuiteProject);
            }
        }

        foreach (var recorded in corpus.KnownUnsound)
        {
            if (!ids.Contains(recorded))
                throw new InvalidOperationException("knownUnsound id is not a case: " + recorded);
        }

        if (corpus.KnownUnsound.Distinct(StringComparer.Ordinal).Count() != corpus.KnownUnsound.Length)
            throw new InvalidOperationException("knownUnsound contains a duplicate.");
        foreach (var required in RequiredRuns)
        {
            if (!runs.Contains(required))
                throw new InvalidOperationException("corpus is missing qualification run " + required);
        }

        foreach (var run in runs)
        {
            if (!RequiredRuns.Contains(run, StringComparer.Ordinal))
                throw new InvalidOperationException("corpus run is outside the seven qualification runs: " + run);
        }

        return corpus;
    }

    private static bool ClassFileExists(string suiteDirectory, string className)
    {
        var stack = new Stack<string>();
        stack.Push(suiteDirectory);
        var fileName = className + ".cs";
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            foreach (var sub in Directory.EnumerateDirectories(dir))
            {
                var name = Path.GetFileName(sub);
                if (name is "bin" or "obj" or "node_modules")
                    continue;
                stack.Push(sub);
            }

            if (Directory.EnumerateFiles(dir, fileName).Any())
                return true;
        }

        return false;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Antiphon.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("Antiphon.sln was not found.");
    }

    private static bool IsBuildInput(string path)
    {
        var slash = path.LastIndexOf('/');
        var name = slash < 0 ? path : path[(slash + 1)..];
        if (name.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase)
            || name.Equals("global.json", StringComparison.OrdinalIgnoreCase)
            || name.Equals("NuGet.Config", StringComparison.OrdinalIgnoreCase)
            || name.Equals("packages.lock.json", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.StartsWith("Directory.Build.", StringComparison.OrdinalIgnoreCase))
            return true;
        return name.EndsWith(".sln", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsUnderProject(string path, string csproj)
    {
        var slash = csproj.LastIndexOf('/');
        if (slash < 0)
            return string.Equals(path, csproj, StringComparison.Ordinal);
        return path.StartsWith(csproj[..(slash + 1)], StringComparison.Ordinal);
    }

    private sealed class ProjectGraph
    {
        private readonly Dictionary<string, string[]> _references;
        private readonly Dictionary<string, HashSet<string>> _closures = new(StringComparer.Ordinal);

        private ProjectGraph(Dictionary<string, string[]> references) => _references = references;

        public static ProjectGraph Load(string root)
        {
            var projects = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (var full in EnumerateProjects(root))
            {
                var relative = Path.GetRelativePath(root, full).Replace('\\', '/');
                projects.Add(relative, ReadReferences(relative, full));
            }

            if (projects.Count < 10)
                throw new InvalidOperationException("project graph found too few csproj files.");
            foreach (var pair in projects)
            {
                foreach (var reference in pair.Value)
                {
                    if (!projects.ContainsKey(reference))
                        throw new InvalidOperationException("missing ProjectReference target: " + pair.Key + " -> " + reference);
                }
            }

            return new ProjectGraph(projects);
        }

        public bool Selects(string suiteProject, string[] changedPaths)
        {
            if (changedPaths.Length == 0)
                throw new InvalidOperationException("empty changed path list.");
            if (!_closures.TryGetValue(suiteProject, out var closure))
            {
                closure = Closure(suiteProject);
                _closures.Add(suiteProject, closure);
            }

            foreach (var changed in changedPaths)
            {
                if (IsBuildInput(changed))
                    return true;
                foreach (var project in closure)
                {
                    if (IsUnderProject(changed, project))
                        return true;
                }
            }

            return false;
        }

        private HashSet<string> Closure(string suiteProject)
        {
            if (!_references.ContainsKey(suiteProject))
                throw new InvalidOperationException("suite project is not in the graph: " + suiteProject);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var queue = new Queue<string>();
            seen.Add(suiteProject);
            queue.Enqueue(suiteProject);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                foreach (var reference in _references[current])
                {
                    if (seen.Add(reference))
                        queue.Enqueue(reference);
                }
            }

            return seen;
        }

        private static IEnumerable<string> EnumerateProjects(string root)
        {
            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var dir = stack.Pop();
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (name is ".git" or "bin" or "obj" or "node_modules" or ".antiphon")
                        continue;
                    stack.Push(sub);
                }

                foreach (var file in Directory.EnumerateFiles(dir, "*.csproj"))
                    yield return file;
            }
        }

        private static string[] ReadReferences(string csproj, string fullPath)
        {
            var slash = csproj.LastIndexOf('/');
            if (slash < 0)
                throw new InvalidOperationException("csproj is not in a directory: " + csproj);
            var directory = csproj[..(slash + 1)];
            var document = XDocument.Load(fullPath);
            var references = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var element in document.Descendants().Where(item => item.Name.LocalName == "ProjectReference"))
            {
                var include = element.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include))
                    throw new InvalidOperationException("ProjectReference without Include in " + csproj);
                var resolved = Resolve(directory, csproj, include);
                if (seen.Add(resolved))
                    references.Add(resolved);
            }

            return references.ToArray();
        }

        private static string Resolve(string directory, string csproj, string include)
        {
            var value = include.Replace('\\', '/').Trim();
            if (value.Length == 0 || value.StartsWith('/') || value.Contains('$') || value.Contains('@')
                || value.Contains('%') || value.Contains(':'))
                throw new InvalidOperationException("unresolved ProjectReference in " + csproj + ": " + include);
            var parts = new List<string>();
            foreach (var part in (directory + value).Split('/'))
            {
                if (part == "..")
                {
                    if (parts.Count == 0)
                        throw new InvalidOperationException("outside-repository: " + csproj + " -> " + include);
                    parts.RemoveAt(parts.Count - 1);
                }
                else if (part.Length > 0 && part != ".")
                {
                    parts.Add(part);
                }
            }

            var resolved = string.Join('/', parts);
            if (!resolved.EndsWith(".csproj", StringComparison.Ordinal))
                throw new InvalidOperationException("project reference is not a csproj: " + csproj + " -> " + resolved);
            return resolved;
        }
    }

    private sealed class Corpus
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("knownUnsound")]
        public string[]? KnownUnsound { get; set; }

        [JsonPropertyName("cases")]
        public Case[]? Cases { get; set; }
    }

    private sealed class Case
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("run")]
        public string? Run { get; set; }

        [JsonPropertyName("failingClass")]
        public string? FailingClass { get; set; }

        [JsonPropertyName("suiteProject")]
        public string? SuiteProject { get; set; }

        [JsonPropertyName("introducingCommit")]
        public string? IntroducingCommit { get; set; }

        [JsonPropertyName("note")]
        public string? Note { get; set; }

        [JsonPropertyName("changedPaths")]
        public string[]? ChangedPaths { get; set; }

        [JsonPropertyName("currentMap")]
        public string? CurrentMap { get; set; }

        [JsonPropertyName("m1")]
        public string? M1 { get; set; }

        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }
}

/// <summary>
/// CARD-1836 S5: the replay fixture. One scratch git repository with the predecessor P, the five candidates R1..R5 (each
/// commit carries the derived index and the trust policy) and a zero-carry chain Z1..Z5 of global changes, and input roots
/// laid out like the server2 custody copy (MANIFEST.sha256, custody.json, runs/&lt;run8&gt;/{logs,timing,attempt,candidate},
/// state/) with synthesized per-case TRX. R1 is a global (Full) change; R2 hits A and K and A fails; R3 hits A only and K
/// fails (the deliberate miss); R4 drops an old-only edge to E, renumbers the ordinals and E fails; R5 changes the trusted
/// Messaging project privately, its own tests fail and one carried chunk (O) has no row or TRX.
/// </summary>
internal sealed class ReplayWorld : IDisposable
{
    public sealed record Spec(string Name, string Project, string Class, string Case, string[] Consumes, double Wall)
    {
        public string Digest => RcChunkReceipt.ContentDigest("antiphon", "native", [new RcCaseIdentity("Antiphon.Tests", "Ns." + Class, "M", Case)])!;

        /// <summary>The chunk id at run index i (0 = P): A, K, E and O renumber from R4 on.</summary>
        public string Id(int run) => Name switch
        {
            "A" => run < 4 ? "g-1" : "g-2",
            "K" => run < 4 ? "g-2" : "g-3",
            "E" => run < 4 ? "g-3" : "g-4",
            "O" => run < 4 ? "g-4" : "g-1",
            _ => Class,
        };
    }

    public static readonly Spec[] Specs =
    [
        new("A", "tests/Alpha/Alpha.Tests.csproj", "AlphaTests", "a1", [], 10), new("K", "tests/Kappa/Kappa.Tests.csproj", "KappaTests", "k1", [], 20),
        new("E", "tests/Echo/Echo.Tests.csproj", "EchoTests", "e1", [], 30), new("O", "tests/Other/Other.Tests.csproj", "OtherTests", "o1", [], 40),
        new("MT", "tests/Messaging/Messaging.Tests.csproj", "MessagingTests", "mt1", ["src/Messaging/Messaging.csproj"], 50),
        new("SV", "tests/Server/Server.Tests.csproj", "ServerTests", "sv1", ["src/Server/Server.csproj", "src/Messaging/Messaging.csproj"], 60),
    ];

    /// <summary>The missed cohort: A fails at R2, K at R3 (carried), E at R4, MT at R5, and O's R5 evidence is absent.</summary>
    public static string Missed(int run, string chunk) => (run, chunk) switch
    {
        (2, "A") or (3, "K") or (4, "E") or (5, "MT") => "fail",
        (5, "O") => "absent",
        _ => "pass",
    };

    /// <summary>The same cohort with K passing at R3 and O's evidence present.</summary>
    public static string Clean(int run, string chunk) => (run, chunk) is (3, "K") or (5, "O") ? "pass" : Missed(run, chunk);

    /// <summary>The zero-carry cohort: every run a global change, every chunk clean.</summary>
    public static string AllFull(int run, string chunk) => "pass";

    private readonly string[] _shas = new string[6];
    private readonly string[] _zero = new string[6];

    public ReplayWorld()
    {
        Fixture = Antiphon.Tests.Scripts.Nightly.NightlyFixture.Create("c1836-replay");
        Repo = Fixture.PathOf("repo");
        Directory.CreateDirectory(Repo);
        Git("init", "-q", "-b", "main");
        Git("config", "core.autocrlf", "false");
        var projects = new Dictionary<string, string[]>
        {
            ["src/Messaging/Messaging.csproj"] = ["messaging"], ["src/Server/Server.csproj"] = ["server"],
        };
        foreach (var spec in Specs) projects[spec.Project] = [spec.Name.ToLowerInvariant()];
        Put("tests/rc-project-categories.json", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["schemaVersion"] = 1, ["projects"] = projects, ["batches"] = Specs.Select(s => s.Project).ToArray(),
        }));
        const string Sdk = "<Project Sdk=\"Microsoft.NET.Sdk\">";
        string Refs(params string[] refs) => Sdk + "<ItemGroup>" + string.Concat(refs.Select(r => "<ProjectReference Include=\"" + r + "\" />")) + "</ItemGroup></Project>";
        Put("src/Messaging/Messaging.csproj", Sdk + "</Project>");
        Put("src/Server/Server.csproj", Refs("../Messaging/Messaging.csproj"));
        Put("tests/Messaging/Messaging.Tests.csproj", Refs("../../src/Messaging/Messaging.csproj"));
        Put("tests/Server/Server.Tests.csproj", Refs("../../src/Server/Server.csproj"));
        foreach (var spec in Specs.Take(4)) Put(spec.Project, Sdk + "</Project>");
        Put("Directory.Build.props", "<Project />");
        Put("src/Messaging/Internal.cs", "p");
        Put("tests/Alpha/A.cs", "p");
        Put("tests/Kappa/K.cs", "p");
        Put("tests/Echo/E.cs", "p");
        Put("tests/Other/O.cs", "p");
        Put("tests/Other/EchoData.txt", "p");
        Put("tests/derived-impact-index.json", Index(true));
        Put("scripts/lib/rc-trusted-projects.json", JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["format"] = "antiphon-rc-trusted-projects-v1", ["schema"] = 1, ["entries"] = new object[]
            {
                new Dictionary<string, object>
                {
                    ["project"] = "src/Messaging/Messaging.csproj", ["configurations"] = new[] { "Release|x64" },
                    ["ownedTests"] = new object[] { new Dictionary<string, object> { ["chunk"] = "MessagingTests", ["cases"] = new[] { "mt1" } } },
                    ["downstream"] = new[] { "src/Server/Server.csproj" }, ["contractProducer"] = "antiphon-contract-v1",
                },
            },
        }));
        _shas[0] = _zero[0] = Commit();
        Put("Directory.Build.props", "<Project><!-- r1 --></Project>");
        _shas[1] = Commit();
        Put("tests/Alpha/A.cs", "r2");
        Put("tests/Kappa/K.cs", "r2");
        _shas[2] = Commit();
        Put("tests/Alpha/A.cs", "r3");
        _shas[3] = Commit();
        File.Delete(Path.Combine(Repo, "tests", "Other", "EchoData.txt"));
        Put("tests/derived-impact-index.json", Index(false));
        _shas[4] = Commit();
        Put("src/Messaging/Internal.cs", "r5");
        _shas[5] = Commit();
        Git("checkout", "-q", "--detach", _zero[0]);
        for (var i = 1; i <= 5; i++)
        {
            Put("Directory.Build.props", "<Project><!-- z" + i + " --></Project>");
            _zero[i] = Commit();
        }
    }

    public Antiphon.Tests.Scripts.Nightly.NightlyFixture Fixture { get; }
    public string Repo { get; }
    public string Sha(int run) => _shas[run];
    public string RunId(int run) => "run-" + Run8(run) + "-attempt";
    public string Run8(int run) => run == 0 ? "p0000000" : "r" + run + "000000";
    public Spec Chunk(string name) => Specs.Single(s => s.Name == name);

    private static string Index(bool echoEdge) => JsonSerializer.Serialize(new Dictionary<string, object>
    {
        ["schema"] = 1, ["cap"] = 25, ["cappedPaths"] = Array.Empty<object>(),
        ["edges"] = echoEdge ? new object[] { new Dictionary<string, object> { ["path"] = "tests/Other/EchoData.txt", ["classes"] = new[] { "EchoTests" } } } : Array.Empty<object>(),
    });

    /// <summary>An input root for a cohort (Missed, Clean: P,R1..R5; AllFull: P,Z1..Z5). perturb flips or removes the outcome of the named chunk ids per run.</summary>
    public string Root(string name, Func<int, string, string> outcome, Dictionary<int, HashSet<string>>? perturb = null)
    {
        var root = Fixture.PathOf("input-" + name);
        var shas = outcome == AllFull ? _zero : _shas;
        var runs = new List<object>();
        for (var run = 0; run <= 5; run++)
        {
            var dir = Path.Combine(root, "runs", Run8(run));
            var logs = Path.Combine(dir, "logs");
            var rows = new List<object>();
            var chunks = new List<object>();
            foreach (var spec in Specs)
            {
                var id = spec.Id(outcome == AllFull ? 0 : run);
                chunks.Add(new Dictionary<string, object>
                {
                    ["suite"] = "antiphon", ["chunk"] = id, ["project"] = spec.Project, ["classes"] = new[] { spec.Class }, ["consumes"] = spec.Consumes,
                    ["priorWallSeconds"] = spec.Wall,
                    ["members"] = new object[] { new Dictionary<string, string> { ["assembly"] = "Antiphon.Tests", ["class"] = "Ns." + spec.Class, ["method"] = "M", ["case"] = spec.Case } },
                });
                var result = outcome(run, spec.Name);
                if (perturb != null && run > 0 && perturb[run].Contains(id)) result = result == "pass" ? (spec.Name.Length % 2 == 0 ? "absent" : "fail") : "pass";
                if (result == "absent") continue;
                var trx = "antiphon-" + id + ".trx";
                Antiphon.Tests.Scripts.Nightly.NightlyFixture.WriteTrx(Path.Combine(logs, trx),
                    [new Antiphon.Tests.Scripts.Nightly.NightlyTrxRow(spec.Case, "Ns." + spec.Class, "M", result == "pass" ? "Passed" : "Failed")]);
                rows.Add(new Dictionary<string, object?>
                {
                    ["id"] = "antiphon", ["chunk"] = id, ["result"] = result == "pass" ? "pass" : "FAIL", ["exitCode"] = result == "pass" ? 0 : 1, ["timedOut"] = false,
                    ["skipped"] = false, ["evidenceReasons"] = Array.Empty<string>(), ["trx"] = "C:\\Antiphon\\releases\\candidates\\c\\logs\\x\\" + trx,
                    ["sha"] = shas[run], ["contentDigest"] = spec.Digest, ["disposition"] = "executed",
                    ["platform"] = "windows", ["architecture"] = "x64", ["profile"] = "rc",
                });
            }
            Write(Path.Combine(logs, "groups-planned.json"), new Dictionary<string, object> { ["chunks"] = chunks, ["census"] = Array.Empty<object>() });
            Write(Path.Combine(logs, "dispatch-schedule.json"), new { order = chunks.Count });
            Write(Path.Combine(logs, "summary.json"), new Dictionary<string, object> { ["sha"] = shas[run], ["suites"] = rows });
            Write(Path.Combine(dir, "timing", "setup-phases.json"), new { phases = Array.Empty<object>() });
            Write(Path.Combine(dir, "attempt", "selection-attempt.json"), new { runId = RunId(run), requestedMode = "Full" });
            Write(Path.Combine(dir, "attempt", "contracts.json"), new object[]
            {
                new { project = "src/Messaging/Messaging.csproj", side = "B", extractorVersion = "contract-v1", contractHash = "h1" },
                new { project = "src/Messaging/Messaging.csproj", side = "A", extractorVersion = "contract-v1", contractHash = "h1" },
            });
            Write(Path.Combine(dir, "candidate", "candidate.json"), new { sha = shas[run] });
            if (run > 0) runs.Add(new { run8 = Run8(run), runId = RunId(run), stamp = "2026-11-2" + run + "-0100", cid = "rc-fixture", sha = shas[run] });
        }
        Write(Path.Combine(root, "state", "s1", "last-run.json"), new { runId = RunId(5) });
        Write(Path.Combine(root, "custody.json"), new
        {
            asOf = "2026-11-25T12:00:00Z", runs, predecessor = new { run8 = Run8(0), runId = RunId(0), sha = shas[0] }, missing = Array.Empty<string>(),
        });
        var lines = Directory.GetFiles(root, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(root, f).Replace('\\', '/'))
            .OrderBy(p => p, StringComparer.Ordinal).Select(p => Sha256(File.ReadAllBytes(Path.Combine(root, p))) + "  " + p);
        File.WriteAllText(Path.Combine(root, "MANIFEST.sha256"), string.Join("\n", lines) + "\n");
        return root;
    }

    /// <summary>A digest of every file under root (relative path and bytes).</summary>
    public static string TreeHash(string root)
    {
        var text = new System.Text.StringBuilder();
        foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories).OrderBy(p => p, StringComparer.Ordinal))
            text.Append(Path.GetRelativePath(root, file)).Append('\0').Append(Sha256(File.ReadAllBytes(file))).Append('\n');
        return Sha256(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
    }

    private static string Sha256(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();

    private static void Write(string path, object value) => Antiphon.Tests.Scripts.Nightly.NightlyFixture.WriteJson(path, value);

    private string Git(params string[] args) => Antiphon.Tests.Scripts.Nightly.NightlyRcChain.Git(Repo, args);

    private void Put(string path, string text)
    {
        var full = Path.Combine(Repo, path.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, text, new System.Text.UTF8Encoding(false));
    }

    private string Commit()
    {
        Git("add", "--all");
        Git("-c", "user.name=Fixture", "-c", "user.email=fixture@example.invalid", "-c", "commit.gpgsign=false", "commit", "-qm", "fixture", "--allow-empty");
        return Git("rev-parse", "HEAD").Trim();
    }

    public void Dispose() => Fixture.Dispose();
}
