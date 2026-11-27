using System.Collections;
using System.Collections.Specialized;
using System.Text;
using System.Text.Json;
using Antiphon.Nightly;
using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using LibGit2Sharp;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1545: the rc project planner (Antiphon.Nightly.RcProjectSelection) on a fixture repository, in process. The
/// cases of the deleted scripts/test-rc-project-selection.ps1 (2026-11-22 plan, unit 8), with its row names: ownership and
/// downstream closure, batches and categories, every fail-closed reason, historical edges at B, and consumer safety
/// (linked content, globs, imports, the batch census). The planner reads git objects only.
/// </summary>
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class RcProjectSelectionTests
{

    [Test, Timeout(8_000)]
    [Arguments(ProofScenario.Composition)]
    [Arguments(ProofScenario.RosterAndIdentity)]
    [Arguments(ProofScenario.IndependentEndpoints)]
    public void BothEndpointProofsPreserveIndependentlyOwedChunks(ProofScenario scenario, CancellationToken cancellationToken) =>
        CheckEndpointProofs(cancellationToken, scenario);

    [Test, Timeout(6_000)]
    public void EndpointProofsPreserveOwnedAndUnownedCaps(CancellationToken cancellationToken) =>
        CheckEndpointProofs(cancellationToken, ProofScenario.Caps);

    [Test, Timeout(7_000)]
    public void EndpointProofsPreserveIndependentReadersAndAddedConsumers(CancellationToken cancellationToken) =>
        CheckEndpointProofs(cancellationToken, ProofScenario.Readers);

    public enum ProofScenario { Composition, RosterAndIdentity, IndependentEndpoints, Caps, Readers }

    private void CheckEndpointProofs(CancellationToken cancellationToken, ProofScenario scenario)
    {
        const string leftProject = "tests/L/L.csproj";
        const string rightProject = "tests/R/R.csproj";
        const string north = "tests/L/A.cs";
        const string south = "tests/R/B.cs";
        const string southAtBaseline = "namespace South; public class Probe {}\n";
        const string leftSource = "tests/L/Use.cs";
        const string rightSource = "tests/R/Use.cs";
        const string index = "tests/derived-impact-index.json";
        using var repo = new Repo();
        using var pack = new ReleaseImpactTypeProofFixture();
        var tunit = pack.Emit("TUnit.Core", ReleaseImpactTypeProofFixture.TUnitAttributes, []);
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [".gitignore"] = ".antiphon/selector-replay/\n",
            [leftProject] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [rightProject] = "<Project Sdk=\"Microsoft.NET.Sdk\" />\n",
            [north] = "namespace North; public class Probe {}\n",
            [south] = southAtBaseline,
            [leftSource] = "namespace L; public class LeftTests { [TUnit.Core.Test, TUnit.Core.Arguments(\"l1\")] public void M(string key) { _ = typeof(North.Probe); } }\n",
            [rightSource] = "namespace R; public class RightTests { [TUnit.Core.Test, TUnit.Core.Arguments(\"r1\")] public void M(string key) { _ = typeof(South.Probe); } }\n",
            [index] = JsonSerializer.Serialize(new
            {
                schema = 1, cap = 25, byteLimit = 2000000, allowlist = Array.Empty<string>(),
                windowsOnly = Array.Empty<string>(), linuxOnly = Array.Empty<string>(), cappedPaths = Array.Empty<object>(),
                edges = new[] { north, south }.Select(path => new { path, classes = new[] { "LeftTests", "RightTests" } }),
            }),
        };
        repo.Catalog(new OrderedDictionary { [leftProject] = new[] { "other" }, [rightProject] = new[] { "other" } }, leftProject, rightProject);
        foreach (var (path, text) in files) repo.Put(path, text);
        var baseline = repo.Commit();
        ReleaseImpactTypeProofFixture.Context[] contexts =
        [
            new(leftProject, "L", [north, leftSource], [], [pack.Core, tunit]),
            new(rightProject, "R", [south, rightSource], [], [pack.Core, tunit]),
        ];
        var baselineArtifact = Produce(baseline, "baseline");
        files[south] = "namespace South; public class Probe { public int Value; }\n";
        repo.Put(south, files[south]);
        var candidate = repo.Commit();

        var chunks = new[]
        {
            Chunk("left", leftProject, "L.LeftTests"),
            Chunk("right", rightProject, "R.RightTests"),
        };
        chunks[0].Cases = ["l1"];
        chunks[1].Cases = ["r1"];
        chunks[0].CaseRoster = [new("L", "L.LeftTests", "M", "l1")];
        chunks[1].CaseRoster = [new("R", "R.RightTests", "M", "r1")];
        foreach (var chunk in chunks)
        {
            chunk.Suite = "antiphon";
            chunk.ContentDigest = RcChunkReceipt.ContentDigest(chunk.Suite, "native", chunk.CaseRoster);
        }
        foreach (var chunk in chunks)
            repo.Put(".antiphon/selector-replay/" + chunk.Id + ".trx",
                $"""<TestRun><TestDefinitions><UnitTest id="{chunk.Cases.Single()}"><TestMethod className="{chunk.Classes.Single()}" name="M" /></UnitTest></TestDefinitions><Results><UnitTestResult testId="{chunk.Cases.Single()}" outcome="Passed" /></Results></TestRun>""");

        var scope = new RcReceiptScope { Platform = "windows", Architecture = "x64", Profile = "rc" };
        var receipts = chunks.Select(chunk => (IDictionary)new OrderedDictionary
        {
            ["id"] = "antiphon", ["chunk"] = chunk.Id, ["result"] = "pass", ["exitCode"] = 0, ["sha"] = baseline,
            ["contentDigest"] = chunk.ContentDigest, ["disposition"] = "executed", ["platform"] = "windows",
            ["architecture"] = "x64", ["profile"] = "rc",
        }).ToArray();

        RcReuse Admit(IDictionary row)
        {
            var chunk = chunks.Single(item => item.Id == PsValue.Str(row["chunk"]));
            var trx = RcChunkReceipt.ReadTrx(repo.PathOf(".antiphon", "selector-replay", chunk.Id + ".trx"));
            trx.Single().Class.ShouldBe(chunk.Classes.Single());
            trx.Single().Method.ShouldBe("M");
            return RcChunkReceipt.Reusable(row, trx, chunk.Cases, scope);
        }

        if (scenario == ProofScenario.IndependentEndpoints)
        {
            CheckChangedIndexAndRemovedLink();
            return;
        }

        var candidateArtifact = Produce(candidate, "candidate");
        var off = RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, receipts, [], Admit);
        off.Mode.ShouldBe("Scoped", "C.off project coverage");
        off.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "execute"], "C.off");
        var proof = RcTypeProofComparison.Read(baselineArtifact.Path, baselineArtifact.Digest, baseline,
            candidateArtifact.Path, candidateArtifact.Digest, candidate, Digest("fixture-algorithm"), Digest("fixture-policy"));
        var on = RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, receipts, [], Admit, proof);
        on.Decisions.Select(decision => decision.Decision).ShouldBe(["carry", "execute"], "C.delta");
        on.Proof.Removed.ShouldBe([new("A", south, "LeftTests"), new("B", south, "LeftTests")], "C.sides");
        Text(on.Of("left").Origin, "sha").ShouldBe(baseline, "C.origin");
        on.Of("right").Reasons.ShouldContain("project-closure:A:" + south, "C.right");
        on.Of("right").Reasons.ShouldContain("release-relation-type:A:" + south, "C.sides candidate positive");
        on.Of("right").Reasons.ShouldContain("release-relation-type:B:" + south, "C.sides baseline positive");
        on.Of("left").Reasons.ShouldBeEmpty("C.delta left owes nothing");
        if (scenario == ProofScenario.Caps)
        {
            CheckCaps();
            return;
        }
        if (scenario == ProofScenario.Readers)
        {
            CheckReadersAndAddedConsumer();
            return;
        }

        if (scenario == ProofScenario.Composition)
        {
            var reader = new RcReaderCensusEntry { Reader = "L.LeftTests", Globs = [south] };
            var owed = RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, receipts, [reader], Admit, proof);
            owed.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "execute"], "C.owed");
            owed.Of("left").Reasons.ShouldContain("runtime-reader:" + south, "C.owed");
            var missing = RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, [receipts[1]], [], Admit, proof);
            missing.Of("left").Decision.ShouldBe("execute", "C.origin missing");
            var dirty = new OrderedDictionary();
            foreach (DictionaryEntry entry in receipts[0]) dirty[entry.Key] = entry.Value;
            dirty["coverageComplete"] = false;
            RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, [dirty, receipts[1]], [], Admit, proof)
                .Of("left").Decision.ShouldBe("execute", "C.origin dirty");
            dirty["coverageComplete"] = true;
            dirty["sha"] = baseline;
            dirty["result"] = "FAIL";
            RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, [dirty, receipts[1]], [], Admit, proof)
                .Of("left").Decision.ShouldBe("execute", "C.debt failed execution cannot be an origin");
            return;
        }

        chunks[0].CaseRoster = [new("wrong-assembly", "L.LeftTests", "M", "l1")];
        RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, receipts, [], Admit, proof)
            .Of("left").Decision.ShouldBe("execute", "C.roster assembly");
        chunks[0].CaseRoster = [new("L", "L.LeftTests", "M", "wrong-case")];
        RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, receipts, [], Admit, proof)
            .Mode.ShouldBe("Full", "C.roster case");
        chunks[0].CaseRoster = [new("L", "L.LeftTests", "ChangedMethod", "l1")];
        var staleMethod = RcChunkSelection.Select(repo.Root, baseline, candidate, chunks, receipts, [], Admit, proof);
        staleMethod.Of("left").Decision.ShouldBe("execute", "C.roster unbound method");
        staleMethod.Mode.ShouldBe("Full", "C.roster digest must bind the method");
        chunks[0].CaseRoster = [new("L", "L.LeftTests", "M", "l1")];
        var duplicate = Chunk("duplicate-case", leftProject, "L.LeftTests");
        duplicate.Suite = chunks[0].Suite;
        duplicate.Cases = chunks[0].Cases;
        duplicate.CaseRoster = chunks[0].CaseRoster;
        duplicate.ContentDigest = chunks[0].ContentDigest;
        var duplicateRoster = RcChunkSelection.Select(repo.Root, baseline, candidate, [..chunks, duplicate], receipts, [], Admit, proof);
        duplicateRoster.Mode.ShouldBe("Full", "C.roster duplicate case");
        duplicateRoster.Decisions.All(decision => decision.Decision == "execute").ShouldBeTrue("C.roster entire owed set");

        var sameName = Chunk("same-name", rightProject, "R.LeftTests");
        sameName.Suite = "antiphon";
        sameName.Cases = ["same-name-case"];
        sameName.CaseRoster = [new("R", "R.LeftTests", "M", "same-name-case")];
        sameName.ContentDigest = RcChunkReceipt.ContentDigest(sameName.Suite, "native", sameName.CaseRoster);
        var widened = RcChunkSelection.Select(repo.Root, baseline, candidate, [..chunks, sameName], receipts, [], Admit, proof);
        widened.Of("left").Reasons.ShouldContain("index-edge:B:" + south, "C.legacy same short name left");
        widened.Of("same-name").Reasons.ShouldContain("index-edge:B:" + south, "C.legacy same short name sibling");
        widened.Decisions.All(decision => decision.Decision == "execute").ShouldBeTrue("C.legacy entire owed set");
        RcChunkSelection.Select(repo.Root, baseline, baseline, chunks, receipts, [], Admit, proof)
            .Mode.ShouldBe("Full", "C.stale endpoint");

        var newPolicy = RcTypeProofComparison.Read(baselineArtifact.Path, baselineArtifact.Digest, baseline,
            candidateArtifact.Path, candidateArtifact.Digest, candidate, Digest("fixture-algorithm"), Digest("changed-policy"));
        newPolicy.Identity.ShouldNotBe(proof.Identity, "C.identity");
        RcTypeProofComparison.Read(baselineArtifact.Path, baselineArtifact.Digest, baseline,
            candidateArtifact.Path, candidateArtifact.Digest, candidate, Digest("different-algorithm"), Digest("fixture-policy"))
            .Identity.ShouldNotBe(proof.Identity, "C.identity algorithm");
        var replacement = repo.PathOf(".antiphon", "selector-replay", "resealed.json");
        var proposed = File.ReadAllText(candidateArtifact.Path) + "\n";
        File.WriteAllText(replacement, proposed);
        Digest(proposed).ShouldNotBe(candidateArtifact.Digest, "C.reseal fixture");
        Should.Throw<InvalidDataException>(() => RcTypeProofComparison.Read(baselineArtifact.Path, baselineArtifact.Digest,
            baseline, replacement, candidateArtifact.Digest, candidate, Digest("fixture-algorithm"), Digest("fixture-policy")),
            "C.reseal independent artifact pin");
        var mismatchedTarget = System.Text.Json.Nodes.JsonNode.Parse(proposed)!;
        foreach (var context in mismatchedTarget["compilerContexts"]!.AsArray())
            context!["configuration"] = "Debug";
        var targetBytes = mismatchedTarget.ToJsonString();
        File.WriteAllText(replacement, targetBytes);
        Should.Throw<InvalidDataException>(() => RcTypeProofComparison.Read(baselineArtifact.Path, baselineArtifact.Digest,
            baseline, replacement, Digest(targetBytes), candidate, Digest("fixture-algorithm"), Digest("fixture-policy")),
            "C.identity a consistently resealed target mismatch is refused");
        Should.Throw<FileNotFoundException>(() => RcTypeProofComparison.Read(baselineArtifact.Path, baselineArtifact.Digest,
            baseline, Path.Combine(Path.GetDirectoryName(replacement)!, "absent.json"), candidateArtifact.Digest,
            candidate, Digest("fixture-algorithm"), Digest("fixture-policy")), "C.reseal missing endpoint artifact");

        void CheckChangedIndexAndRemovedLink()
        {
            var originalIndex = files[index];
            var changedIndex = System.Text.Json.Nodes.JsonNode.Parse(originalIndex)!;
            var changedEdge = changedIndex["edges"]!.AsArray().Single(edge => (string?)edge!["path"] == south);
            changedEdge!["classes"] = new System.Text.Json.Nodes.JsonArray("RightTests");
            repo.Put(index, files[index] = changedIndex.ToJsonString());
            var indexCandidate = repo.Commit();
            var indexArtifact = Produce(indexCandidate, "candidate-index");
            var indexProof = RcTypeProofComparison.Read(baselineArtifact.Path, baselineArtifact.Digest, baseline,
                indexArtifact.Path, indexArtifact.Digest, indexCandidate, Digest("fixture-algorithm"), Digest("fixture-policy"));
            var changed = RcChunkSelection.Select(repo.Root, baseline, indexCandidate, chunks, receipts, [], Admit, indexProof);
            changed.Of("left").Reasons.ShouldContain("index-edge-changed:LeftTests", "C.index independent change");
            changed.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "execute"], "C.index owed set");
            repo.Checkout(baseline);
            files[index] = originalIndex;

            var unlinkedProject = files[leftProject];
            var currentSouth = files[south];
            files[south] = "namespace South; public class Probe {}\n";
            files[leftProject] = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><Compile Include=\"../R/B.cs\" Link=\"B.cs\" /></ItemGroup></Project>\n";
            repo.Put(leftProject, files[leftProject]);
            repo.Put(south, files[south]);
            var linkedBaseline = repo.Commit();
            contexts[0] = contexts[0] with { SourcePaths = [north, leftSource, south] };
            var linkedArtifact = Produce(linkedBaseline, "baseline-linked");
            repo.Put(leftProject, files[leftProject] = unlinkedProject);
            repo.Put(south, files[south] = currentSouth);
            var unlinkedCandidate = repo.Commit();
            contexts[0] = contexts[0] with { SourcePaths = [north, leftSource] };
            var unlinkedArtifact = Produce(unlinkedCandidate, "candidate-link-removed");
            var linkProof = RcTypeProofComparison.Read(linkedArtifact.Path, linkedArtifact.Digest, linkedBaseline,
                unlinkedArtifact.Path, unlinkedArtifact.Digest, unlinkedCandidate, Digest("fixture-algorithm"), Digest("fixture-policy"));
            var linked = RcChunkSelection.Select(repo.Root, linkedBaseline, unlinkedCandidate, chunks, receipts, [], Admit, linkProof);
            linked.Mode.ShouldBe("Scoped", "C.sides " + string.Join(", ", linked.FallbackReasons));
            linked.Of("left").Reasons.ShouldContain("project-closure:B:" + south, "C.sides B-only link");
            linked.Of("left").Reasons.ShouldNotContain("project-closure:A:" + south, "C.sides link removed at A");
            linked.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "execute"], "C.sides B-only link owed set");
        }

        void CheckCaps()
        {
            const string uncatalogued = "docs/capped.md";
            var southAtCandidate = files[south];

            repo.Checkout(baseline);
            files[south] = southAtBaseline;
            var cappedIndex = System.Text.Json.Nodes.JsonNode.Parse(files[index])!;
            cappedIndex["cappedPaths"] = new System.Text.Json.Nodes.JsonArray(south, uncatalogued);
            Put(index, cappedIndex.ToJsonString());
            Put(uncatalogued, "baseline\n");
            var capBaseline = repo.Commit();
            var capBaselineArtifact = Produce(capBaseline, "baseline-cap");
            Put(south, southAtCandidate);
            var capCandidate = repo.Commit();
            var capCandidateArtifact = Produce(capCandidate, "candidate-cap");
            var capPair = Compare(capBaseline, capBaselineArtifact, capCandidate, capCandidateArtifact);
            var ownedCap = RcChunkSelection.Select(repo.Root, capBaseline, capCandidate, chunks, receipts, [], Admit, capPair);
            ownedCap.Mode.ShouldBe("Scoped", "C.cap owned");
            ownedCap.Of("left").Reasons.ShouldContain("index-edge:B:" + south, "C.cap cannot discharge the capped hit");
            ownedCap.Of("right").Reasons.ShouldContain("index-capped-closure:" + south + ":B", "C.cap baseline owning closure");
            ownedCap.Of("right").Reasons.ShouldContain("index-capped-closure:" + south + ":A", "C.cap candidate owning closure");
            ownedCap.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "execute"], "C.cap owned set");
            ownedCap.Proof.Removed.ShouldBeEmpty("C.cap no subtraction from the capped path");

            repo.Checkout(capBaseline);
            files[south] = southAtBaseline;
            Put(uncatalogued, "changed\n");
            var unknownCandidate = repo.Commit();
            var unknownArtifact = Produce(unknownCandidate, "candidate-unknown-cap");
            var unknownPair = Compare(capBaseline, capBaselineArtifact, unknownCandidate, unknownArtifact);
            var unownedCap = RcChunkSelection.Select(repo.Root, capBaseline, unknownCandidate, chunks, receipts,
                [new() { Reader = "L.LeftTests", Globs = [uncatalogued] }], Admit, unknownPair);
            unownedCap.Mode.ShouldBe("Full", "C.cap unowned and independently read");
            unownedCap.FallbackReasons.ShouldContain("index-capped:" + uncatalogued + ":B");
            unownedCap.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "execute"], "C.cap unowned set");
        }

        void CheckReadersAndAddedConsumer()
        {
            const string hand = "tests/impacted-classes.json";
            const string script = "scripts/test-startup.ps1";
            var southAtCandidate = files[south];
            repo.Checkout(baseline);
            files[south] = southAtBaseline;
            Put(hand, JsonSerializer.Serialize(new { kinds = new[] { new { globs = new[] { south }, classes = new[] { "LeftTests" } } } }));
            Put(script, "Write-Output 'baseline'\n");
            var readerBaseline = repo.Commit();
            var readerBaselineArtifact = Produce(readerBaseline, "baseline-readers");
            Put(south, southAtCandidate);
            var readerCandidate = repo.Commit();
            var readerArtifact = Produce(readerCandidate, "candidate-hand");
            var readerPair = Compare(readerBaseline, readerBaselineArtifact, readerCandidate, readerArtifact);
            var handRule = RcChunkSelection.Select(repo.Root, readerBaseline, readerCandidate, chunks, receipts, [], Admit, readerPair);
            handRule.Of("left").Reasons.ShouldContain("hand:A:" + south, "C.owed hand rule");
            handRule.Of("left").Reasons.ShouldContain("hand:B:" + south, "C.owed hand rule");
            handRule.Of("left").Reasons.ShouldNotContain("release-relation-type:A:" + south, "C.owed proof still removed its own contribution");
            handRule.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "execute"], "C.owed hand set");

            repo.Checkout(readerBaseline);
            files[south] = southAtBaseline;
            Put(script, "Write-Output 'candidate'\n");
            var scriptCandidate = repo.Commit();
            var scriptArtifact = Produce(scriptCandidate, "candidate-script");
            var scriptPair = Compare(readerBaseline, readerBaselineArtifact, scriptCandidate, scriptArtifact);
            var scriptReader = RcChunkSelection.Select(repo.Root, readerBaseline, scriptCandidate, chunks, receipts,
                [new() { Reader = "L.LeftTests", Globs = ["scripts/test-*.ps1"] }], Admit, scriptPair);
            scriptReader.Mode.ShouldBe("Scoped", "C.owed script reader scope");
            scriptReader.Of("left").Reasons.ShouldContain("runtime-reader:scripts/test-*.ps1", "C.owed script reader");
            scriptReader.Decisions.Select(decision => decision.Decision).ShouldBe(["execute", "carry"], "C.owed script set");

            repo.Checkout(baseline);
            files.Remove(hand);
            files.Remove(script);
            const string added = "tests/R/New.cs";
            Put(added, "namespace R; public class AddedTests { [TUnit.Core.Test] public void M() { _ = typeof(South.Probe); } }\n");
            contexts[1] = contexts[1] with { SourcePaths = [south, rightSource, added] };
            var addedCandidate = repo.Commit();
            var addedArtifact = Produce(addedCandidate, "candidate-added-consumer");
            var addedPair = Compare(baseline, baselineArtifact, addedCandidate, addedArtifact);
            var addedChunk = Chunk("added", rightProject, "R.AddedTests");
            addedChunk.Suite = "antiphon";
            addedChunk.Cases = ["added-case"];
            addedChunk.CaseRoster = [new("R", "R.AddedTests", "M", "added-case")];
            addedChunk.ContentDigest = RcChunkReceipt.ContentDigest(addedChunk.Suite, "native", addedChunk.CaseRoster);
            var addedConsumer = RcChunkSelection.Select(repo.Root, baseline, addedCandidate, [.. chunks, addedChunk],
                receipts, [], Admit, addedPair);
            addedConsumer.Of("added").Reasons.ShouldContain("release-relation-declaration:A:" + added, "C.sides A-only consumer");
            addedConsumer.Of("added").Reasons.ShouldNotContain("release-relation-declaration:B:" + added, "C.sides no consumer at B");
            addedConsumer.Decisions.Select(decision => decision.Decision)
                .ShouldBe(["carry", "execute", "execute"], "C.sides A-only owed set");
        }

        void Put(string path, string bytes) => repo.Put(path, files[path] = bytes);

        static RcTypeProofComparison Compare(string first, (string Path, string Digest) firstGraph, string last,
            (string Path, string Digest) lastGraph) => RcTypeProofComparison.Read(firstGraph.Path, firstGraph.Digest, first,
                lastGraph.Path, lastGraph.Digest, last, Digest("fixture-algorithm"), Digest("fixture-policy"));

        (string Path, string Digest) Produce(string sha, string label)
        {
            var output = repo.PathOf(".antiphon", "selector-replay", label + ".json");
            pack.WriteReleaseGraph(repo.Root, sha, output, files, contexts, cancellationToken);
            var bytes = File.ReadAllBytes(output);
            using var graph = JsonDocument.Parse(bytes);
            graph.RootElement.GetProperty("proofEligible").GetBoolean().ShouldBeTrue(
                label + " compiler binding " + graph.RootElement.GetProperty("reason").GetString());
            return (output, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        }

        static string Digest(string value) => Convert.ToHexStringLower(
            System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
    private static readonly OrderedDictionary Projects = new()
    {
        ["src/Messaging/Messaging.csproj"] = new[] { "messaging" },
        ["src/Server/Server.csproj"] = new[] { "server", "git-operations" },
        ["src/Runner/Runner.csproj"] = new[] { "claude-runner", "codex-runner", "shared-pty-runner" },
        ["tests/Server/Server.csproj"] = new[] { "server", "git-operations" },
        ["tests/Runner/Runner.csproj"] = new[] { "claude-runner", "codex-runner", "shared-pty-runner" },
        ["tests/Other/Other.csproj"] = new[] { "other" },
    };

    [Test]
    public void C1545_Closure()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        fx.Put("src/Messaging/Message.cs", "changed");
        var a = fx.Commit();
        var projectReads = 0;
        var sharedReads = 0;
        // Every project and shared start input is read through the census batch: every path asked of git, one read each.
        Dictionary<string, string> Counted(string root, string sha, string[] paths)
        {
            projectReads += paths.Count(p => p.EndsWith(".csproj", StringComparison.Ordinal));
            sharedReads += paths.Count(p => p == "Directory.Build.props");
            return TestSelectionGit.ReadBlobBatch(root, sha, paths);
        }
        var s = RcProjectSelection.Select(fx.Root, b, a, Counted);
        var reads = projectReads;
        fx.Put("Directory.Build.props", "not XML");
        var invalidBase = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "invalid shared input candidate");
        var invalidCandidate = fx.Commit();
        var invalid = RcProjectSelection.Select(fx.Root, invalidBase, invalidCandidate, Counted);
        var projectBlobs = fx.Git("ls-tree", "-r", a).Split('\n').Where(l => l.EndsWith(".csproj", StringComparison.Ordinal))
            .Select(l => l.Split((char[]?)null, 4, StringSplitOptions.RemoveEmptyEntries)[2]).Distinct().Count();
        var rows = new NightlyCaseRows();
        rows.Check(reads == projectBlobs && sharedReads == 1 && Text(invalid, "mode") == "Full"
            && Strings(invalid, "fallbackReasons").Any(r => r.StartsWith("graph:Directory.Build.props:", StringComparison.Ordinal)),
            "C1545 immutable-blob-read-once", "reads=" + reads + " blobs=" + projectBlobs + " shared=" + sharedReads + " " + string.Join(",", Strings(invalid, "fallbackReasons")));
        rows.Check(Text(s, "mode") == "Scoped", "C1545 scoped", string.Join(",", Strings(s, "fallbackReasons")));
        rows.Check(Same(Strings(s, "changedProjects"), "src/Messaging/Messaging.csproj"), "C1545 owners");
        rows.Check(Same(Strings(s, "affectedProjects"), "src/Messaging/Messaging.csproj", "src/Server/Server.csproj", "src/Runner/Runner.csproj",
            "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 downstream");
        rows.Check(Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 batches");
        rows.Check(Same(Strings(s, "categories"), "messaging", "server", "git-operations", "claude-runner", "codex-runner", "shared-pty-runner"), "C1545 categories");
        var messaging = Project(s, a, "src/Messaging/Messaging.csproj");
        rows.Check(messaging != null && Same(Strings(messaging, "downstream"), "src/Server/Server.csproj", "src/Runner/Runner.csproj", "tests/Server/Server.csproj",
            "tests/Runner/Runner.csproj"), "C1545 manifest-closure");
        rows.Check(Text(s, "effectiveMode") == "Full" && s["gateCredit"] is false && s["executionEvidence"] is false && Text(s, "activation") == "shadow-only", "C1545 no-credit");
        var again = RcProjectSelection.Select(fx.Root, b, a);
        rows.Check(TestSelectionJson.Canonical(s) == TestSelectionJson.Canonical(again), "C1545 deterministic");
        rows.ShouldAllPass(9);
    }

    [Test]
    public void C1545_FailClosed()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        var rows = new NightlyCaseRows();
        fx.Put("unowned/file.txt", "new");
        var s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Contains("unknown-path:unowned/file.txt") && Items(s["batches"]).Length == 3, "C1545 unknown-full");
        fx.Put("Directory.Build.props", "<Project />");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Strings(s, "fallbackReasons").Contains("shared-input:Directory.Build.props"), "C1545 shared-full");
        fx.Put("src/Runner/Runner.csproj", "<Project><ItemGroup><ProjectReference Include=\"$(Unknown)/Runner.csproj\" /></ItemGroup></Project>");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Any(r => r.Contains("unresolved-input", StringComparison.Ordinal)), "C1545 dynamic-full");
        var projects = new OrderedDictionary();
        foreach (DictionaryEntry entry in Projects) if ((string)entry.Key != "tests/Other/Other.csproj") projects[entry.Key] = entry.Value;
        fx.Catalog(projects, "tests/Server/Server.csproj", "tests/Runner/Runner.csproj", "tests/Other/Other.csproj");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Contains("unknown-project:tests/Other/Other.csproj"), "C1545 category-full");
        rows.ShouldAllPass(4);
    }

    [Test]
    public void C1545_HistoricalEdges()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        var rows = new NightlyCaseRows();
        fx.Put("tests/Server/Linked.cs", "changed");
        var s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 linked-consumer");
        fx.Put("src/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var a = fx.Commit();
        s = RcProjectSelection.Select(fx.Root, b, a);
        var oldServer = Project(s, b, "src/Server/Server.csproj");
        var newServer = Project(s, a, "src/Server/Server.csproj");
        rows.Check(Strings(s, "affectedProjects").Contains("tests/Server/Server.csproj") && oldServer != null && Strings(oldServer, "references").Contains("src/Messaging/Messaging.csproj")
            && newServer != null && Strings(newServer, "references").Length == 0, "C1545 old-edge");
        File.Delete(fx.PathOf("src/Messaging/Message.cs"));
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Strings(s, "affectedProjects").Contains("src/Runner/Runner.csproj"), "C1545 deleted-owner");
        fx.Put("src/Runner/Runner.csproj", "<Project><ItemGroup><ProjectReference Include=\"../Server/Server.csproj\" /></ItemGroup></Project>");
        fx.Put("src/Server/Server.csproj", "<Project><ItemGroup><ProjectReference Include=\"../Runner/Runner.csproj\" /></ItemGroup></Project>");
        s = RcProjectSelection.Select(fx.Root, b, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Any(r => r.Contains("cycle", StringComparison.Ordinal)), "C1545 cycle-full");
        rows.ShouldAllPass(4);
    }

    [Test]
    public void C1545_ConsumerSafety()
    {
        using var fx = new Repo();
        fx.Baseline();
        var rows = new NightlyCaseRows();
        const string Runner = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Runner/Runner.csproj\" />";
        fx.Put("tests/Runner/Runner.csproj", Runner + "<Content Include=\"../Server/Data.txt\" /></ItemGroup></Project>");
        fx.Put("tests/Server/Data.txt", "baseline");
        var cb = fx.Commit();
        fx.Put("tests/Server/Data.txt", "changed");
        var s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(Text(s, "mode") == "Scoped" && Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 content-consumer");
        fx.Put("tests/Runner/Runner.csproj", Runner + "<Content Include=\"../Server/Fixtures/*.json\" /></ItemGroup></Project>");
        fx.Put("tests/Server/Fixtures/example.json", "{}");
        cb = fx.Commit();
        fx.Put("tests/Server/Fixtures/example.json", "{\"changed\":true}");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        var wildcard = Text(s, "mode") == "Scoped" && Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj");
        fx.Put("tests/Runner/Runner.csproj", Runner + "<Content Include=\"../Server/[sample].json\" /></ItemGroup></Project>");
        fx.Put("tests/Server/[sample].json", "{}");
        cb = fx.Commit();
        fx.Put("tests/Server/[sample].json", "{\"changed\":true}");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(wildcard && Text(s, "mode") == "Scoped" && Same(BatchProjects(s), "tests/Server/Server.csproj", "tests/Runner/Runner.csproj"), "C1545 glob-consumer");
        fx.Put("src/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><Import Project=\"Dependencies.props\" /></Project>");
        fx.Put("src/Server/Dependencies.props", "<Project><ItemGroup><ProjectReference Include=\"../Messaging/Messaging.csproj\" /></ItemGroup></Project>");
        cb = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "imported consumer change");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(Text(s, "mode") == "Scoped" && BatchProjects(s).Contains("tests/Server/Server.csproj"), "C1545 imported-reference", string.Join(",", Strings(s, "fallbackReasons")));
        fx.Put("tests/Runner/Runner.csproj", Runner + "<PackageReference Include=\"TUnit\" Version=\"1.0\" /></ItemGroup></Project>");
        fx.Catalog(Projects, "tests/Server/Server.csproj", "tests/Other/Other.csproj");
        cb = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "missing batch change");
        s = RcProjectSelection.Select(fx.Root, cb, fx.Commit());
        rows.Check(Text(s, "mode") == "Full" && Strings(s, "fallbackReasons").Contains("unbatched-test-project:tests/Runner/Runner.csproj"), "C1545 batch-census");
        rows.ShouldAllPass(4);
    }

    /// <summary>The command: the selection's canonical JSON on stdout and once into --output-path; a refusal is exit 2 with one line.</summary>
    [Test]
    public void Command_prints_writes_once_and_refuses()
    {
        using var fx = new Repo();
        var b = fx.Baseline();
        fx.Put("src/Messaging/Message.cs", "changed");
        var a = fx.Commit();
        var output = fx.PathOf("out", "batches.json");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var options = SelectRcProjectBatchesOptions.Parse(["--baseline-sha", b, "--candidate-sha", a, "--repository-root", fx.Root, "--output-path", output], "unused");
        var (code, stdout, stderr) = Run(options);
        var rows = new NightlyCaseRows();
        using (var document = JsonDocument.Parse(stdout))
            rows.Check(code == 0 && document.RootElement.GetProperty("format").GetString() == RcProjectSelection.Format
                && document.RootElement.GetProperty("mode").GetString() == "Scoped" && File.ReadAllText(output) == stdout.TrimEnd('\r', '\n') + "\n", "printed-and-written", stderr);
        var (again, _, againError) = Run(options);
        rows.Check(again == 2 && againError.StartsWith("project selection refused: ", StringComparison.Ordinal), "existing-output-refused", againError);
        var (refused, _, why) = Run(SelectRcProjectBatchesOptions.Parse(["--baseline-sha", new string('0', 40), "--candidate-sha", a, "--repository-root", fx.Root], "unused"));
        rows.Check(refused == 2 && why.Trim() == "project selection refused: full-commit-required", "missing-commit-refused", why);
        rows.ShouldAllPass(3);
    }

    // CARD-1836 S2 (CP-2, V-2, R-2, R-3, G-5..G-7): both index blobs and both project graphs select; the unrelated chunk carries.
    [Test, Timeout(60_000)]
    public void Removed_harness_edges_select_both_graphs_and_keep_the_unrelated_chunk(CancellationToken cancellationToken)
    {
        using var fx = new Repo();
        fx.Baseline();
        const string Index = "tests/derived-impact-index.json";
        string IndexJson(object[] edges, object[] capped) => JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["schema"] = 1, ["cap"] = 25, ["byteLimit"] = 2000000, ["allowlist"] = Array.Empty<string>(), ["windowsOnly"] = Array.Empty<string>(),
            ["linuxOnly"] = Array.Empty<string>(), ["cappedPaths"] = capped, ["edges"] = edges,
        });
        object Edge(string path, params string[] classes) => new Dictionary<string, object> { ["path"] = path, ["classes"] = classes };
        const string RunnerLinked = "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Runner/Runner.csproj\" /><Compile Include=\"../Server/Linked.cs\" />";
        fx.Put("tests/Runner/Runner.csproj", RunnerLinked + "<Compile Include=\"../Server/Harness.cs\" /></ItemGroup></Project>");
        fx.Put("tests/Server/Harness.cs", "harness");
        fx.Put("tests/fixtures/data/one.txt", "1");
        fx.Put("tests/fixtures/data/two.txt", "2");
        fx.Put("scripts/test-x-startup.ps1", "startup");
        fx.Put(Index, IndexJson([Edge("tests/Server/Harness.cs", "ServerHarnessTests")], []));
        var b = fx.Commit();

        var chunks = new[]
        {
            Chunk("ServerHarness", "tests/Server/Server.csproj", "ServerHarnessTests"), Chunk("RunnerLinked", "tests/Runner/Runner.csproj", "RunnerLinkedTests"),
            Chunk("Other", "tests/Other/Other.csproj", "OtherTests"), Chunk("NewEdge", "tests/Other/Other.csproj", "NewEdgeTests"),
            Chunk("AmbiguousServer", "tests/Server/Server.csproj", "Ns.Server.Ambiguous"), Chunk("AmbiguousOther", "tests/Other/Other.csproj", "Ns.Other.Ambiguous"),
            Chunk("FixtureReader", "tests/Other/Other.csproj", "FixtureReaderTests"), Chunk("StartupReader", "tests/Other/Other.csproj", "StartupReaderTests"),
            Chunk("IndexReader", "tests/Other/Other.csproj", "IndexReaderTests"),
        };
        var receipts = chunks.Select(c => (IDictionary)new OrderedDictionary
        {
            ["id"] = "antiphon", ["chunk"] = c.Id, ["result"] = "pass", ["exitCode"] = 0, ["sha"] = b, ["contentDigest"] = c.ContentDigest, ["disposition"] = "executed",
        }).ToArray();
        RcReuse Admit(IDictionary row) => new() { Reusable = PsValue.Str(row["result"]) == "pass" && PsValue.Str(row["sha"]) == b };
        RcReaderCensusEntry[] census =
        [
            new() { Reader = "FixtureReaderTests", Globs = ["tests/fixtures/data/**"] }, new() { Reader = "StartupReaderTests", Globs = ["scripts/test-*-startup.ps1"] },
            new() { Reader = "IndexReaderTests", Globs = [Index] },
        ];
        RcChunkSelectionResult Select(string baseline, string candidate, RcReaderCensusEntry[]? withCensus = null) =>
            RcChunkSelection.Select(fx.Root, baseline, candidate, chunks, receipts, withCensus ?? census, Admit);
        string Reasons(RcChunkSelectionResult s, string chunk) => s.Of(chunk).Decision + ":" + string.Join(",", s.Of(chunk).Reasons);
        bool Runs(RcChunkSelectionResult s, string chunk, string reason) => s.Of(chunk).Decision == "execute" && s.Of(chunk).Reasons.Contains(reason);
        var rows = new NightlyCaseRows();

        // A1: delete the harness and its linked Compile together; the A index drops the edge.
        File.Delete(fx.PathOf("tests", "Server", "Harness.cs"));
        fx.Put("tests/Runner/Runner.csproj", RunnerLinked + "</ItemGroup></Project>");
        fx.Put(Index, IndexJson([], []));
        var a1 = fx.Commit();
        var s1 = Select(b, a1);
        rows.Check(s1.Mode == "Scoped" && Runs(s1, "ServerHarness", "index-edge:B:tests/Server/Harness.cs"), "s2-old-only-edge",
            s1.Mode + " " + string.Join(",", s1.FallbackReasons) + " " + Reasons(s1, "ServerHarness"));
        rows.Check(Runs(s1, "RunnerLinked", "project-closure:B:tests/Server/Harness.cs"), "s2-deleted-linked-reader", Reasons(s1, "RunnerLinked"));
        rows.Check(s1.Mode == "Scoped" && s1.Of("Other").Decision == "carry" && PsValue.Str(s1.Of("Other").Origin["sha"]) == b
            && s1.Decisions.Count(d => d.Decision == "carry") > 0, "s2-unrelated-carry", Reasons(s1, "Other"));
        cancellationToken.ThrowIfCancellationRequested();

        // A2 (from B): a new file gets an A-only edge to NewEdgeTests and to a simple name two projects share.
        fx.Checkout(b);
        fx.Put("src/Messaging/Wire.cs", "wire");
        fx.Put(Index, IndexJson([Edge("tests/Server/Harness.cs", "ServerHarnessTests"), Edge("src/Messaging/Wire.cs", "NewEdgeTests", "Ambiguous")], []));
        var a2 = fx.Commit();
        var s2 = Select(b, a2);
        rows.Check(s2.Mode == "Scoped" && Runs(s2, "NewEdge", "index-edge:A:src/Messaging/Wire.cs") && s2.Of("Other").Decision == "carry", "s2-new-only-hit", Reasons(s2, "NewEdge"));
        rows.Check(Runs(s2, "AmbiguousServer", "index-edge:A:src/Messaging/Wire.cs") && Runs(s2, "AmbiguousOther", "index-edge:A:src/Messaging/Wire.cs"), "s2-ambiguous-name",
            Reasons(s2, "AmbiguousServer") + " | " + Reasons(s2, "AmbiguousOther"));

        // Capped at B: an owned path widens to its project closure; an unowned capped path forces Full.
        fx.Checkout(b);
        fx.Put("docs/capped.md", "baseline");
        fx.Put(Index, IndexJson([Edge("tests/Server/Harness.cs", "ServerHarnessTests")], [new Dictionary<string, object> { ["path"] = "src/Messaging/Message.cs", ["reason"] = "cap" }, "docs/capped.md"]));
        var bc = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "changed");
        var a3 = fx.Commit();
        var s3 = Select(bc, a3);
        fx.Checkout(bc);
        fx.Put("docs/capped.md", "changed");
        var s3b = Select(bc, fx.Commit());
        rows.Check(s3.Mode == "Scoped" && Runs(s3, "AmbiguousServer", "index-capped-closure:src/Messaging/Message.cs:B") && s3b.Mode == "Full"
            && s3b.FallbackReasons.Contains("index-capped:docs/capped.md:B"), "s2-cap", s3.Mode + " " + string.Join(",", s3b.FallbackReasons));

        // No index blob at B.
        fx.Checkout(b);
        File.Delete(fx.PathOf("tests", "derived-impact-index.json"));
        var b0 = fx.Commit();
        fx.Put("src/Messaging/Message.cs", "changed again");
        var s4 = Select(b0, fx.Commit());
        rows.Check(s4.Mode == "Full" && s4.FallbackReasons.Contains("index-unbound:B") && s4.Decisions.All(d => d.Decision == "execute"), "s2-index-unbound",
            string.Join(",", s4.FallbackReasons));

        string Blob(string sha) => fx.Git("rev-parse", sha + ":" + Index).Trim();
        string Sha(string sha) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(new UTF8Encoding(false).GetBytes(fx.Git("cat-file", "blob", Blob(sha))))).ToLowerInvariant();
        rows.Check(s1.Index.Length == 2 && s1.Index[0].Side == "B" && s1.Index[0].Blob == Blob(b) && s1.Index[0].Sha256 == Sha(b) && s1.Index[1].Side == "A"
            && s1.Index[1].Blob == Blob(a1) && s1.Index[1].Sha256 == Sha(a1) && Blob(b) != Blob(a1), "s2-index-binding");

        // R-2: every file under a directory a reader lists is deleted.
        fx.Checkout(b);
        File.Delete(fx.PathOf("tests", "fixtures", "data", "one.txt"));
        File.Delete(fx.PathOf("tests", "fixtures", "data", "two.txt"));
        var s5 = Select(b, fx.Commit());
        rows.Check(s5.Mode == "Scoped" && Runs(s5, "FixtureReader", "runtime-reader:tests/fixtures/data/**") && s5.Of("Other").Decision == "carry", "s2-runtime-reader",
            s5.Mode + " " + string.Join(",", s5.FallbackReasons) + " " + Reasons(s5, "FixtureReader"));

        // R-3: an assembled-name reader of a deleted driver, no index edge on either side.
        fx.Checkout(b);
        File.Delete(fx.PathOf("scripts", "test-x-startup.ps1"));
        var a6 = fx.Commit();
        var s6 = Select(b, a6);
        var s6Bare = Select(b, a6, []);
        rows.Check(s6.Mode == "Scoped" && Runs(s6, "StartupReader", "runtime-reader:scripts/test-*-startup.ps1") && s6Bare.Mode == "Full"
            && s6Bare.FallbackReasons.Contains("unknown-path:scripts/test-x-startup.ps1"), "s2-assembled-name-reader",
            s6.Mode + " " + string.Join(",", s6.FallbackReasons) + " | " + string.Join(",", s6Bare.FallbackReasons));

        // Review r1 D-4(a): a census glob over compiled project source (scripts/lib/** over Antiphon.Nightly) keeps the project closure.
        fx.Checkout(b);
        fx.Put("src/Messaging/Message.cs", "census");
        var s7 = Select(b, fx.Commit(), [.. census, new RcReaderCensusEntry { Reader = "MessagingReaderTests", Globs = ["src/Messaging/**"] }]);
        rows.Check(s7.Mode == "Scoped" && Runs(s7, "ServerHarness", "project-closure:B:src/Messaging/Message.cs") && Runs(s7, "RunnerLinked", "project-closure:A:src/Messaging/Message.cs")
            && s7.Of("Other").Decision == "carry", "s2-census-keeps-closure", s7.Mode + " " + Reasons(s7, "ServerHarness") + " | " + Reasons(s7, "RunnerLinked"));

        // Review r1 D-4(b): an index of another schema, or without its edges or cappedPaths arrays, is malformed and forces Full.
        string Raw(object schema, bool edges, bool capped)
        {
            var doc = new Dictionary<string, object> { ["schema"] = schema, ["cap"] = 25 };
            if (capped) doc["cappedPaths"] = Array.Empty<object>();
            if (edges) doc["edges"] = new[] { Edge("tests/Server/Harness.cs", "ServerHarnessTests") };
            return JsonSerializer.Serialize(doc);
        }
        RcChunkSelectionResult IndexAt(string text)
        {
            fx.Checkout(b);
            fx.Put(Index, text);
            fx.Put("src/Messaging/Wire.cs", "index-shape");
            return Select(b, fx.Commit());
        }
        var schema2 = IndexAt(Raw(2, true, true));
        var noEdges = IndexAt(Raw(1, false, true));
        var noCapped = IndexAt(Raw(1, true, false));
        rows.Check(new[] { schema2, noEdges, noCapped }.All(s => s.Mode == "Full" && s.FallbackReasons.Contains("index-malformed:A") && s.Decisions.All(d => d.Decision == "execute")),
            "s2-index-schema-unknown", string.Join(" | ", new[] { schema2, noEdges, noCapped }.Select(s => s.Mode + ":" + string.Join(",", s.FallbackReasons))));

        // Review r1 D-4(c): an index-only regeneration that removes an edge selects the removed class and the index's own readers.
        fx.Checkout(b);
        fx.Put(Index, IndexJson([], []));
        var s8 = Select(b, fx.Commit());
        rows.Check(s8.Mode == "Scoped" && Runs(s8, "ServerHarness", "index-edge-changed:ServerHarnessTests") && Runs(s8, "IndexReader", "runtime-reader:" + Index)
            && s8.Of("Other").Decision == "carry" && s8.Of("RunnerLinked").Decision == "carry", "s2-index-readers",
            s8.Mode + " " + string.Join(",", s8.FallbackReasons) + " " + Reasons(s8, "ServerHarness") + " | " + Reasons(s8, "IndexReader"));

        // A change to a non-edge index field (cap, allowlist, windowsOnly, linuxOnly) changes what every edge means: Full.
        fx.Checkout(b);
        fx.Put(Index, IndexJson([Edge("tests/Server/Harness.cs", "ServerHarnessTests")], []).Replace("\"cap\":25", "\"cap\":30", StringComparison.Ordinal));
        var s9 = Select(b, fx.Commit());
        rows.Check(s9.Mode == "Full" && s9.FallbackReasons.Contains("index-fields-changed"), "s2-index-field-change", s9.Mode + " " + string.Join(",", s9.FallbackReasons));

        rows.ShouldAllPass(14, "s2-old-only-edge", "s2-deleted-linked-reader", "s2-unrelated-carry", "s2-new-only-hit", "s2-ambiguous-name", "s2-cap",
            "s2-index-unbound", "s2-index-binding", "s2-runtime-reader", "s2-assembled-name-reader", "s2-census-keeps-closure", "s2-index-schema-unknown",
            "s2-index-readers", "s2-index-field-change");
    }

    private static RcPlannedChunk Chunk(string id, string project, string cls) => new()
    {
        Id = id, Project = project, Classes = [cls],
        ContentDigest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("content/" + id))).ToLowerInvariant(),
    };

    private static (int Code, string Out, string Error) Run(SelectRcProjectBatchesOptions options)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var code = RcProjectSelection.Run(options, NightlyHost.Production(output, error));
        return (code, output.ToString(), error.ToString());
    }

    private static string Text(IDictionary document, string name) => PsValue.Str(document[name]);

    private static string[] Strings(IDictionary document, string name) => Items(document[name]).Select(PsValue.Str).ToArray();

    private static object[] Items(object? value) => value is IEnumerable items and not string ? items.Cast<object>().ToArray() : value == null ? [] : [value];

    private static string[] BatchProjects(IDictionary selection) => Items(selection["batches"]).Select(r => PsValue.Str(((IDictionary)r)["project"])).ToArray();

    private static IDictionary? Project(IDictionary selection, string sha, string path) =>
        Items(selection["graphs"]).Cast<IDictionary>().Where(g => PsValue.Str(g["sha"]) == sha)
            .SelectMany(g => Items(g["projects"]).Cast<IDictionary>()).FirstOrDefault(p => PsValue.Str(p["path"]) == path);

    private static bool Same(IEnumerable<string> actual, params string[] expected) =>
        string.Join(",", TestSelectionJson.Sorted(actual.ToArray())) == string.Join(",", TestSelectionJson.Sorted(expected));

    /// <summary>The fixture repository of the deleted driver: six projects, their categories and three batches.</summary>
    private sealed class Repo : IDisposable
    {
        private static readonly Signature Author = new("Fixture", "fixture@example.invalid", new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero));
        private readonly NightlyFixture _fixture = NightlyFixture.Create("c1545-projects");
        private readonly Repository _repository;

        public Repo()
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.FixtureSetup, "initialize rc project repository");
            _repository = new Repository(Repository.Init(Root));
            _repository.Refs.UpdateTarget("HEAD", "refs/heads/main");
            _repository.Config.Set("core.autocrlf", false);
        }

        public string Root => _fixture.PathOf("repository with spaces");

        public string PathOf(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

        public string Git(params string[] arguments) => NightlyRcChain.Git(Root, arguments);

        public void Checkout(string sha) => Commands.Checkout(_repository, _repository.Lookup<Commit>(sha));

        public void Put(string path, string text)
        {
            var full = PathOf(path.Split('/'));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, text, new UTF8Encoding(false));
        }

        public string Commit()
        {
            using var phase = TestTiming.BeginPhase(TestPhaseKind.Filesystem, "commit rc project fixture");
            // Fixture commits use libgit2. The selector reads the resulting Git objects.
            Commands.Stage(_repository, "*");
            return _repository.Commit("fixture", Author, Author, new CommitOptions { AllowEmptyCommit = true }).Sha;
        }

        public void Catalog(IDictionary projects, params string[] batches) =>
            Put("tests/rc-project-categories.json", JsonSerializer.Serialize(new Dictionary<string, object> { ["schemaVersion"] = 1, ["projects"] = projects, ["batches"] = batches }));

        /// <summary>The baseline commit: the catalog, the six projects (references, a linked Compile item) and two sources.</summary>
        public string Baseline()
        {
            Catalog(Projects, "tests/Server/Server.csproj", "tests/Runner/Runner.csproj", "tests/Other/Other.csproj");
            Put("src/Messaging/Messaging.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Put("src/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Messaging/Messaging.csproj\" /></ItemGroup></Project>");
            Put("src/Runner/Runner.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../Messaging/Messaging.csproj\" /></ItemGroup></Project>");
            Put("tests/Server/Server.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Server/Server.csproj\" /></ItemGroup></Project>");
            Put("tests/Runner/Runner.csproj",
                "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><ProjectReference Include=\"../../src/Runner/Runner.csproj\" /><Compile Include=\"../Server/Linked.cs\" /></ItemGroup></Project>");
            Put("tests/Other/Other.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
            Put("src/Messaging/Message.cs", "baseline");
            Put("tests/Server/Linked.cs", "baseline");
            return Commit();
        }

        public void Dispose()
        {
            _repository.Dispose();
            _fixture.Dispose();
        }
    }
}
