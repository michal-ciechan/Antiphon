using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S5b: read-only historical admission, in-process (moved from test-rc-admission.ps1 and its reader fixture
/// test-rc-admission-reader.ps1). The carry predicate refuses any unproven fact, rehash mismatch, collision or stale
/// anchor; the reader verifies sealed bytes, the proof chain and the TRX; the import writes only an unproven shard;
/// and algorithm coverage is never complete, so nothing is admitted.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(120_000)]
public sealed class RcHistoricalAdmissionTests : IDisposable
{
    private const string OwnersMap =
        "{\"schemaVersion\":1,\"suites\":[{\"id\":\"server\",\"project\":\"tests/Server/Server.csproj\"}],\"roots\":[{\"path\":\"scripts/**\",\"consumers\":[\"*\"],\"reason\":\"harness\"}]}";

    private static readonly DateTimeOffset Now = RcAdmissionEvidenceFixture.UtcNow;

    // Every repository and retained corpus is under the fixture's owned temporary root.
    private readonly RcInputFixtureRepository repo = new("ra-");
    private string sha = "";
    private RcChunkInputManifest manifest = null!;

    public void Dispose() => repo.Dispose();

    [Before(Test)]
    public void Seed()
    {
        repo.Init();
        repo.Put(RcInputInventory.MapPath, OwnersMap);
        repo.Put("tests/Server/Server.csproj", "<Project />");
        repo.Put("tests/Server/Input.cs", "test input");
        sha = repo.Commit();
        var chunk = RcBoundary.NewChunk([new RcBoundaryRow { Suite = "antiphon", Boundary = "core", IdentityKey = "Example.Tests/Check", CaseKey = "row:a" }]);
        var cases = new[] { new RcChunkCase { Suite = "antiphon", ClassName = "Example.Tests", Method = "Check", CaseKey = "row:a", Stable = true } };
        var actual = RcChunkDigest.RequiredReceipts
            .Select(kind => new RcActualReceipt { Kind = kind, Digest = new string('a', 64), ProvenanceDigest = new string('b', 64), SourceSha = sha }).ToArray();
        manifest = RcChunkDigest.NewManifest(RcInputInventory.Read(repo.Root, sha), "server", chunk, cases, actual);
    }

    private static RcAdmissionFacts Facts() => new()
    {
        InventoryValid = true, ProvenanceValid = true, Outcome = "Passed", Complete = true, ReleaseStatus = "complete", AlgorithmDigest = new string('c', 64),
        CurrentAlgorithmDigest = new string('c', 64), AnchorAtUtc = "2026-11-12T12:00:00Z", AnchorVerified = true, HistoryValid = true, OriginValid = true,
        ContractValid = true, CaseOutcomes = ["Passed"],
    };

    // Each fact, cleared (the typed equivalent of removing the key from the PowerShell hashtable).
    private static readonly (string Name, Action<RcAdmissionFacts> Clear)[] FactFields =
    [
        ("inventoryValid", f => f.InventoryValid = null), ("provenanceValid", f => f.ProvenanceValid = null), ("outcome", f => f.Outcome = null),
        ("complete", f => f.Complete = null), ("releaseStatus", f => f.ReleaseStatus = null), ("algorithmDigest", f => f.AlgorithmDigest = null),
        ("currentAlgorithmDigest", f => f.CurrentAlgorithmDigest = null), ("anchorAtUtc", f => f.AnchorAtUtc = null), ("anchorVerified", f => f.AnchorVerified = null),
        ("historyValid", f => f.HistoryValid = null), ("originValid", f => f.OriginValid = null), ("contractValid", f => f.ContractValid = null),
        ("caseOutcomes", f => f.CaseOutcomes = null),
    ];

    private static RcChunkInputManifest Clone(RcChunkInputManifest value) =>
        JsonSerializer.Deserialize<RcChunkInputManifest>(JsonSerializer.Serialize(value, RcAdmission.RecordJson), RcAdmission.RecordJson)!;

    // No chunk: the predicate derives it from the later manifest's cases, as the script's two-manifest calls did.
    private static RcCarryAdmission Decide(RcChunkInputManifest before, RcChunkInputManifest after, RcAdmissionFacts facts, RcBoundaryChunk? chunk = null) =>
        RcAdmission.CarryAdmissionDecision(before, after, facts, chunk!, Now);

    private static void Check(RcChunkInputManifest before, RcChunkInputManifest after, RcAdmissionFacts facts, string reason)
    {
        var d = Decide(before, after, facts);
        (!d.Eligible && d.Reasons.Contains(reason)).ShouldBeTrue("missing-refusal:" + reason + " reasons=" + string.Join(",", d.Reasons));
    }

    private static RcAdmissionFacts With(Action<RcAdmissionFacts> change)
    {
        var facts = Facts();
        change(facts);
        return facts;
    }

    // The shared reader fixture: a sealed corpus at the fixture commit, whose first read is eligible but, with the
    // algorithm coverage incomplete, never admitted.
    private RcAdmissionEvidenceFixture Reader()
    {
        var fixture = new RcAdmissionEvidenceFixture(Path.Combine(repo.Scratch, "results"), repo.Root, sha);
        var good = fixture.FreezeAndRead();
        (good.Eligible && !good.Admitted && good.ForceFull).ShouldBeTrue("reader-witness:eligible-with-incomplete-coverage:" + string.Join(";", good.Reasons));
        return fixture;
    }

    [Test]
    public void FullProvenanceAdmitsAndReverifies()
    {
        var d = Decide(manifest, Clone(manifest), Facts());
        (d.Eligible && d.Reasons.Length == 0).ShouldBeTrue("complete-equal-proposal:" + string.Join(",", d.Reasons));
        var bad = Clone(manifest);
        bad.Inputs.First(r => r.Present).ObjectId = new string('f', 40);
        Check(manifest, bad, Facts(), "manifest-rehash-mismatch");
        bad = Clone(manifest);
        bad.Cases[0].CaseKey = "forged";
        Check(manifest, bad, Facts(), "manifest-invalid");
        bad = Clone(manifest);
        bad.Digest = new string('f', 64);
        Check(bad, bad, Facts(), "manifest-rehash-mismatch");

        // Also exercise the actual reader's byte and Git-object verification.
        var reader = Reader();
        var inventory = Antiphon.Rc.HistoricalEvidence.Verify(reader.Args.EvidenceRoot, (string?)reader.Envelope["inventorySha256"]);
        var entry = inventory.files.First(f => f.relativePath == "chunk.trx");
        File.AppendAllText(Path.Combine(reader.Args.EvidenceRoot, entry.bundlePath), "tamper");
        var tampered = reader.Args.Read();
        (!tampered.Eligible && tampered.Dirty).ShouldBeTrue("reader-witness:tampered-bundle-refused");
        reader.FreezeAndRead();
        File.AppendAllText(reader.Args.Path, " ");
        reader.Args.Read().Eligible.ShouldBeFalse("reader-witness:envelope-seal-refused");
        var contract = (JsonObject)reader.Contract.DeepClone();
        contract["runtimeHash"] = new string('f', 64);
        reader.Args.CurrentContext.Contract = RcAdmissionEvidenceFixture.Element(contract);
        reader.Args.Read().Eligible.ShouldBeFalse("reader-witness:current-contract-independent");
        reader.Proof["buildOutputDigest"] = new string('f', 64);
        reader.FreezeAndRead().Eligible.ShouldBeFalse("reader-witness:build-output-mismatch");
        reader.Proof["buildOutputDigest"] = reader.OutputDigest;
        var receipts = (JsonArray)reader.Proof["receipts"]!;
        receipts[0]!["path"] = (string)receipts[1]!["path"]!;
        reader.FreezeAndRead().Eligible.ShouldBeFalse("reader-witness:receipt-bytes-mismatch");
    }

    [Test]
    public void ShaLabelWithoutBuildProofIsUnproven()
    {
        Check(manifest, manifest, With(f => f.ProvenanceValid = false), "source-build-launch-unproven");
        var bad = Clone(manifest);
        bad.ActualReceipts = [];
        Check(manifest, bad, Facts(), "manifest-rehash-mismatch");
    }

    [Test]
    public void IncompleteChunkAndCaseAliasNeverCarry()
    {
        foreach (var outcome in new[] { "Failed", "Timeout", "Aborted", "partial", "NotExecuted", "passed", "Unknown" })
            Check(manifest, manifest, With(f => f.Outcome = outcome), "chunk-not-passed");
        Check(manifest, manifest, With(f => f.Complete = false), "chunk-incomplete");
        Check(manifest, manifest, With(f => f.CaseOutcomes = ["Failed"]), "cases-not-passed");
        Check(manifest, manifest, With(f => f.CaseOutcomes = []), "cases-not-passed");
        Check(manifest, manifest, With(f => f.CaseOutcomes = ["Passed", "Passed"]), "cases-not-passed");
        var bad = Clone(manifest);
        bad.Cases = [.. bad.Cases, Clone(manifest).Cases[0]];
        Check(manifest, bad, Facts(), "manifest-invalid");
    }

    [Test]
    public void ChangedEnvelopeHistoryAndOriginChainRefuse()
    {
        Check(manifest, manifest, With(f => f.InventoryValid = false), "inventory-unproven");
        Check(manifest, manifest, With(f => f.HistoryValid = false), "failure-history-unproven");
        Check(manifest, manifest, With(f => f.OriginValid = false), "origin-unproven");
        Check(manifest, manifest, With(f => f.ContractValid = false), "contract-unproven");

        var reader = Reader();
        reader.WriteJson("origin.json", reader.Proof);
        reader.Proof["kind"] = "carried";
        reader.Proof["originPath"] = "origin.json";
        var chained = reader.FreezeAndRead();
        chained.Eligible.ShouldBeTrue("reader-witness:valid-origin-chain:" + string.Join(";", chained.Reasons));
        reader.Proof["originPath"] = "proof.json";
        var cycle = reader.FreezeAndRead();
        (!cycle.Eligible && string.Join(";", cycle.Reasons).Contains("origin-cycle")).ShouldBeTrue("reader-witness:origin-cycle");
        reader.Proof["originPath"] = "missing.json";
        reader.FreezeAndRead().Eligible.ShouldBeFalse("reader-witness:origin-missing");
        reader.Proof["kind"] = "executed";
        reader.Proof["originPath"] = null;
        reader.Envelope["history"]!["historyState"] = "unknown";
        reader.FreezeAndRead().Eligible.ShouldBeFalse("reader-witness:history-unknown");
        reader.Envelope["history"]!["historyState"] = "complete";
        reader.Proof["sourceSha"] = new string('f', 40);
        reader.FreezeAndRead().Eligible.ShouldBeFalse("reader-witness:wrong-source");
        reader.Proof["sourceSha"] = sha;
        ((JsonArray)reader.Proof["caseMap"]!).Add(RcAdmissionEvidenceFixture.CaseMapRow("case-2"));
        reader.FreezeAndRead().Eligible.ShouldBeFalse("reader-witness:case-alias");
        reader.Proof["caseMap"] = new JsonArray(RcAdmissionEvidenceFixture.CaseMapRow("case-1"));
        File.WriteAllText(reader.Retained("chunk.trx"), RcAdmissionEvidenceFixture.Trx.Replace("outcome=\"Passed\"", "outcome=\"Failed\"", StringComparison.Ordinal));
        reader.FreezeAndRead().Eligible.ShouldBeFalse("reader-witness:failed-original");
    }

    [Test]
    public void EveryIngredientMustMatch()
    {
        foreach (var field in new[] { "digest", "inputDigest", "caseDigest", "receiptDigest", "membershipDigest", "chunkId", "format" })
        {
            var bad = Clone(manifest);
            switch (field)
            {
                case "digest": bad.Digest = "wrong"; break;
                case "inputDigest": bad.InputDigest = "wrong"; break;
                case "caseDigest": bad.CaseDigest = "wrong"; break;
                case "receiptDigest": bad.ReceiptDigest = "wrong"; break;
                case "membershipDigest": bad.MembershipDigest = "wrong"; break;
                case "chunkId": bad.ChunkId = "wrong"; break;
                default: bad.Format = "wrong"; break;
            }
            var d = Decide(manifest, bad, Facts());
            (!d.Eligible && d.Reasons.Length > 0).ShouldBeTrue("field:" + field);
        }
        Check(manifest, manifest, With(f => f.AlgorithmDigest = new string('d', 64)), "algorithm-binding-mismatch");
        Check(manifest, manifest, With(f => f.ReleaseStatus = "incomplete"), "release-evidence-incomplete");
        foreach (var time in new[] { "2026-11-06T12:00:00Z", "2026-11-14T00:00:00Z", "unknown", null })
            Check(manifest, manifest, With(f => f.AnchorAtUtc = time), "full-anchor-invalid-or-expired");
        Check(manifest, manifest, With(f => f.AnchorVerified = false), "full-anchor-invalid-or-expired");
        foreach (var (name, clear) in FactFields)
            Decide(manifest, manifest, With(clear)).Eligible.ShouldBeFalse("missing:" + name);
    }

    [Test]
    public void CollisionAndAlgorithmCoverageForceFull()
    {
        var members = new[] { "10788987499786589518", "17490688019260441962" }.Select(key => new RcBoundaryMembership
        {
            Suite = "antiphon", Boundary = "core", Identities = [new RcBoundaryMember { IdentityKey = "CTests/M", CaseKey = key }],
        }).ToArray();
        var resolved = RcBoundary.ResolveChunks(members);
        (resolved.Length == 2 && resolved[0].Collision && resolved[1].Collision).ShouldBeTrue("real-s2-collision");
        foreach (var c in resolved)
        {
            var d = Decide(manifest, manifest, Facts(), c);
            (!d.Eligible && d.Reasons.Contains("collision-or-unassigned")).ShouldBeTrue("collision-dirty-before-s3b");
        }
        var coverage = RcAdmission.AlgorithmCoverage();
        (!coverage.Complete && coverage.RequiredPaths.Length >= 8 && coverage.MissingPaths.Length > 0).ShouldBeTrue("explicit-incomplete-coverage");
        foreach (var path in coverage.RequiredPaths)
        {
            var c = RcAdmission.AlgorithmCoverage(coverage.RequiredPaths.Where(p => p != path).ToArray());
            (!c.Complete && c.MissingPaths.Contains(path)).ShouldBeTrue("coverage:" + path);
        }
        var all = RcAdmission.AlgorithmCoverage(coverage.RequiredPaths);
        (!all.Complete && all.Reasons.Contains("release-graph-incomplete")).ShouldBeTrue("no-coverage-override");
        // A byte edit in EACH prerequisite changes the bound algorithm identity.
        foreach (var path in coverage.RequiredPaths) repo.Put(path, "a");
        repo.Git("add", "--all");
        repo.Git("commit", "-qm", "algorithm fixture");
        var prior = repo.Head();
        using var git = new RcInputInventory.GitReader(repo.Root);
        var priorDigest = RcAdmission.AlgorithmDigest(git, prior, coverage.RequiredPaths);
        foreach (var path in coverage.RequiredPaths)
        {
            repo.Put(path, "b");
            repo.Git("add", "--", path);
            repo.Git("commit", "-qm", "one byte");
            var nextDigest = RcAdmission.AlgorithmDigest(git, repo.Head(), coverage.RequiredPaths);
            priorDigest.ShouldNotBe(nextDigest, "algorithm-byte:" + path);
            priorDigest = nextDigest;
        }
    }

    [Test]
    public void ImportIsInertAndWritesUnprovenShard()
    {
        var reader = Reader();
        var destination = Path.Combine(reader.ResultsDirectory, "import-output");
        var spec = Path.Combine(reader.ResultsDirectory, "request.json");
        File.WriteAllText(spec, new JsonObject
        {
            ["chunk"] = JsonSerializer.SerializeToNode(reader.Chunk, RcAdmission.RecordJson), ["manifest"] = JsonSerializer.SerializeToNode(reader.Manifest, RcAdmission.RecordJson),
        }.ToJsonString());
        // The import entry (antiphon-scripts rc-ledger-import), in-process; its checkout is this repository.
        RcLedgerImport.Run(reader.Source, destination, repo.Root, sha, RcAdmissionEvidenceFixture.RunId, spec, RcInputFixtureRepository.SourceRoot);
        var report = JsonNode.Parse(File.ReadAllText(Path.Combine(destination, "admission-report.json")))!;
        ((int)report["admitted"]! == 0 && (int)report["dirty"]! == 1 && (bool)report["forceFull"]! && !(bool)report["gateCredit"]!).ShouldBeTrue("reader-witness:import-full");
        var ledger = RcLedger.Read(Path.Combine(destination, "shards", "antiphon.json"));
        (ledger.Identities.Length == 1 && ledger.Identities[0].Outcome == "unproven").ShouldBeTrue("reader-witness:s1-unproven");
    }
}
