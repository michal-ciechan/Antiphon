using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S3b: chunk input identities, in-process (moved from test-rc-chunk-digest.ps1). A throwaway repository
/// with a server suite (whose project closure reaches a transitive input) and an unrelated other suite. A chunk's
/// digest binds its suite's input records and edges, its case membership and the six actual-input receipts; any
/// unknown reason keeps it dirty, and malformed UTF-16 fails closed.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(120_000)]
public sealed class RcChunkInputDigestTests : IDisposable
{
    private const string OwnersMap =
        "{\"schemaVersion\":1,\"suites\":[{\"id\":\"server\",\"project\":\"tests/Server/Server.csproj\"},{\"id\":\"other\",\"project\":\"tests/Other/Other.csproj\"}],"
        + "\"roots\":[{\"path\":\"scripts/**\",\"consumers\":[\"*\"],\"reason\":\"harness\"}]}";

    // A replacement character is valid text; a lone surrogate (high or low) is not.
    private static readonly string Replacement = ((char)0xFFFD).ToString();

    private readonly RcInputFixtureRepository repo = new("rcd-");
    private string seeded = "";
    // Retained actual receipt digests, not Git substitutes. Admission owns their verification.
    private RcActualReceipt[] actual = [];

    public void Dispose() => repo.Dispose();

    [Before(Test)]
    public void Seed()
    {
        repo.Init();
        repo.Put(RcInputInventory.MapPath, OwnersMap);
        repo.Put("tests/Server/Server.csproj", "<Project><ItemGroup><ProjectReference Include=\"../../server/Server.csproj\" /></ItemGroup></Project>");
        repo.Put("tests/Server/SessionMessageQueueServiceTests.cs", "unchanged test");
        repo.Put("server/Server.csproj", "<Project><ItemGroup><ProjectReference Include=\"../src/Core/Core.csproj\" /></ItemGroup></Project>");
        repo.Put("server/Application/SessionMessageQueueService.cs", "old service");
        repo.Put("src/Core/Core.csproj", "<Project />");
        repo.Put("src/Core/Input.txt", "old transitive input");
        repo.Put("tests/Other/Other.csproj", "<Project />");
        repo.Put("tests/Other/Input.cs", "unrelated");
        seeded = repo.Commit();
        actual = RcChunkDigest.RequiredReceipts
            .Select(kind => new RcActualReceipt { Kind = kind, Digest = new string('a', 64), ProvenanceDigest = new string('b', 64), SourceSha = seeded }).ToArray();
    }

    private static RcChunkCase NewCase(string suite, string className, string method, string caseKey, bool stable = true) =>
        new() { Suite = suite, ClassName = className, Method = method, CaseKey = caseKey, Stable = stable };

    // Fresh objects per call: a case may be edited by the test.
    private static RcChunkCase[] Cases() =>
    [
        NewCase("antiphon", "Example.Queue", "Check", "row:a"), NewCase("antiphon", "Example.Queue", "Check", "row:b"),
        NewCase("antiphon", "Example.Queue", "Other", "singleton"),
    ];

    private static RcBoundaryChunk DefaultChunk() => RcBoundary.NewChunk(
    [
        new RcBoundaryRow { Suite = "antiphon", Boundary = "application", IdentityKey = "Example.Queue/Check" },
        new RcBoundaryRow { Suite = "antiphon", Boundary = "application", IdentityKey = "Example.Queue/Other" },
    ]);

    private static RcBoundaryChunk CaseKeyedChunk(IEnumerable<RcChunkCase> cases) => RcBoundary.NewChunk(cases
        .Select(c => new RcBoundaryRow { Suite = c.Suite, Boundary = "application", IdentityKey = c.ClassName + "/" + c.Method, CaseKey = c.CaseKey }).ToArray());

    private RcChunkInputManifest Manifest(string sha, RcBoundaryChunk? chunk = null, IReadOnlyList<RcChunkCase>? cases = null, IReadOnlyList<RcActualReceipt>? receipts = null) =>
        RcChunkDigest.NewManifest(RcInputInventory.Read(repo.Root, sha), "server", chunk ?? DefaultChunk(), cases ?? Cases(), receipts ?? actual);

    private static bool IsDigest(string? value) => value != null && Regex.IsMatch(value, "^[a-f0-9]{64}$");

    private static string Json(object value) => JsonSerializer.Serialize(value);

    private static string[][] Rows(params string[][] rows) => rows;

    private static void Refuses(Action operation, string reason) =>
        Should.Throw<RcChunkDigestRefusal>(operation, "refusal:" + reason).Message.ShouldContain(reason, Case.Sensitive, "refusal-reason:" + reason);

    // No digest or decision is returned for text UTF-8 cannot encode: the strict encoder's error, however wrapped.
    private static void RefusesEncoding(Action operation, string label)
    {
        Exception? caught = null;
        try { operation(); }
        catch (Exception e) { caught = e; }
        caught.ShouldNotBeNull("encoding-accepted:" + label);
        for (var e = caught; e != null; e = e.InnerException)
            if (e is EncoderFallbackException) return;
        throw new ShouldAssertException("encoding-error-type:" + label + ":" + caught.Message);
    }

    [Test]
    public void TransitiveEditAndDeletionChangeDigest()
    {
        var before = Manifest(seeded);
        (before.Unknown.Length == 0 && IsDigest(before.Digest)).ShouldBeTrue("known-baseline");
        RcChunkInputManifest after = null!;
        var sha = "";
        foreach (var path in new[] { "server/Application/SessionMessageQueueService.cs", "src/Core/Input.txt" })
        {
            var previous = Manifest(repo.Head());
            repo.Put(path, "edit " + path);
            sha = repo.Commit();
            after = Manifest(sha);
            previous.InputDigest.ShouldNotBe(after.InputDigest, "transitive-edit:" + path);
            RcChunkDigest.Compare(previous, after).Dirty.ShouldBeTrue("changed-dirty:" + path);
        }
        repo.Git("rev-parse", seeded + ":tests/Server/SessionMessageQueueServiceTests.cs")
            .ShouldBe(repo.Git("rev-parse", sha + ":tests/Server/SessionMessageQueueServiceTests.cs"), "unchanged-test-shape");
        var prior = after;
        repo.Remove("src/Core/Input.txt");
        sha = repo.Commit();
        after = Manifest(sha);
        prior.InputDigest.ShouldNotBe(after.InputDigest, "deletion-digest");
        RcChunkDigest.Compare(prior, after).Dirty.ShouldBeTrue("deletion-dirty");
        repo.Put("server/Application/SessionMessageQueueService.cs", "uncommitted");
        Manifest(sha).Digest.ShouldBe(after.Digest, "object-only-no-working-bytes");
    }

    [Test]
    public void ProvenUnrelatedProjectKeepsDigest()
    {
        var before = Manifest(seeded);
        repo.Put("tests/Other/Input.cs", "changed unrelated");
        var sha = repo.Commit();
        var after = Manifest(sha);
        (before.Unknown.Length == 0 && after.Unknown.Length == 0).ShouldBeTrue("known-closure");
        IsDigest(before.Digest).ShouldBeTrue("unrelated-digest-equal");
        before.Digest.ShouldBe(after.Digest, "unrelated-digest-equal");
        RcChunkDigest.Compare(before, after).Dirty.ShouldBeFalse("unrelated-clean");
        var snapshot = RcInputInventory.Read(repo.Root, sha);
        var server = snapshot.Suites.Single(s => s.Id == "server");
        server.Inputs = Enumerable.Reverse(server.Inputs).ToArray();
        var cases = Cases();
        var reordered = RcChunkDigest.NewManifest(snapshot, "server", DefaultChunk(), [cases[2], cases[1], cases[0]], Enumerable.Reverse(actual).ToArray());
        reordered.Digest.ShouldBe(after.Digest, "ordinal-order-independent");
        Json(reordered.Inputs).ShouldBe(Json(after.Inputs), "canonical-record-order");
        var renewed = actual.Select(r => new RcActualReceipt { Kind = r.Kind, Digest = r.Digest, ProvenanceDigest = new string('d', 64), SourceSha = sha }).ToArray();
        Manifest(sha, receipts: renewed).Digest.ShouldBe(after.Digest, "run-provenance-is-not-input-equivalence");
        (!after.ExecutionEvidence && !after.GateCredit).ShouldBeTrue("no-carry-authority");
        // Independent fixed encoding vector: fields are UTF-8, uint32 big endian counts/lengths.
        RcChunkDigest.ManifestDigest(Rows(["ab", "c"], ["d"])).ShouldBe("bbebcb50cc28f8ce245d7a308abc76bda287ab2df8ae5eb804b23787e5b5649d", "encoding-vector");
        RcChunkDigest.ManifestDigest(Rows(["a", "bc"], ["d"])).ShouldNotBe(RcChunkDigest.ManifestDigest(Rows(["ab", "c"], ["d"])), "length-delimited");
    }

    [Test]
    public void UnknownAndCaseSetChangesAreDirty()
    {
        var before = Manifest(seeded);
        var cases = Cases();
        var after = Manifest(seeded, cases: [cases[0], cases[2]]);
        (before.CaseDigest != after.CaseDigest && RcChunkDigest.Compare(before, after).Dirty).ShouldBeTrue("removed-case-dirty");
        RcChunkDigest.Compare(before, after).Retired.Length.ShouldBe(1, "retired-case-recorded");
        after = Manifest(seeded, cases: [.. Cases(), NewCase("antiphon", "Example.Queue", "Check", "row:c")]);
        before.CaseDigest.ShouldNotBe(after.CaseDigest, "added-case-dirty");
        Refuses(() => Manifest(seeded, cases: [.. Cases(), Cases()[0]]), "duplicate-case");
        Refuses(() => Manifest(seeded, cases: [cases[0], cases[1]]), "incomplete-method-membership");
        Refuses(() => Manifest(seeded, cases: [.. Cases(), NewCase("antiphon", "Example.Queue", "Extra", "singleton")]), "extra-method-membership");
        Refuses(() => Manifest(seeded, cases: [.. Cases(), NewCase("another", "Example.Queue", "Check", "row:x")]), "case-suite-mismatch");
        var unstable = Cases();
        unstable[0].Stable = false;
        after = Manifest(seeded, cases: unstable);
        (after.Unknown.Contains("ambiguous-case:Example.Queue/Check") && RcChunkDigest.Compare(before, after).Dirty).ShouldBeTrue("ambiguous-method-dirty");
        after = Manifest(seeded, receipts: []);
        (after.Unknown.Length == 6 && RcChunkDigest.Compare(after, after).Dirty).ShouldBeTrue("missing-actual-receipts-stay-dirty:" + string.Join(",", after.Unknown));
        var changed = actual.Select(r => new RcActualReceipt
        {
            Kind = r.Kind, Digest = r.Kind == "immutable-images" ? new string('c', 64) : r.Digest, ProvenanceDigest = r.ProvenanceDigest, SourceSha = r.SourceSha,
        }).ToArray();
        Manifest(seeded, receipts: changed).Digest.ShouldNotBe(before.Digest, "actual-receipt-change-bound");
        Refuses(() => Manifest(seeded, receipts: [.. actual, actual[0]]), "duplicate-actual-receipt");
        repo.Put("unknown/file.txt", "unknown");
        after = Manifest(repo.Commit());
        (after.Unknown.Length > 0 && RcChunkDigest.Compare(after, after).Dirty).ShouldBeTrue("unowned-never-clean");
    }

    [Test]
    public void RenameSymlinkAndCaseCollision()
    {
        var before = Manifest(seeded);
        repo.Git("mv", "src/Core/Input.txt", "src/Core/Renamed.txt");
        var after = Manifest(repo.Commit());
        (after.InputDigest != before.InputDigest && RcChunkDigest.Compare(before, after).Dirty).ShouldBeTrue("path-only-rename");
        // Git object construction is portable: no native symlink privilege or case-sensitive filesystem required.
        var oid = repo.Git("rev-parse", "HEAD:src/Core/Renamed.txt");
        repo.Git("update-index", "--add", "--cacheinfo", "120000," + oid + ",src/Core/link");
        var tree = repo.Git("write-tree");
        var sha = repo.Git("commit-tree", tree, "-p", repo.Head(), "-m", "link fixture");
        after = Manifest(sha);
        after.Unknown.Where(u => u.Contains("unresolved-link-or-submodule", StringComparison.OrdinalIgnoreCase)).ShouldNotBeEmpty("unresolved-symlink-dirty");
        repo.Git("update-index", "--add", "--cacheinfo", "100644," + oid + ",src/Core/renamed.txt");
        tree = repo.Git("write-tree");
        sha = repo.Git("commit-tree", tree, "-p", sha, "-m", "collision fixture");
        after = Manifest(sha);
        after.Unknown.Where(u => u.Contains("case-collision", StringComparison.OrdinalIgnoreCase)).ShouldNotBeEmpty("windows-case-collision-dirty");
        RcChunkDigest.Compare(after, after).Dirty.ShouldBeTrue("unknown-equality-never-clean");
    }

    [Test]
    public void CaseKeyedMultipleCasesMatchMembership()
    {
        _ = Manifest(seeded);
        var chunk = CaseKeyedChunk(Cases());
        var manifest = Manifest(seeded, chunk);
        (manifest.Unknown.Length == 0 && manifest.Methods.Length == 2 && manifest.Cases.Length == 3).ShouldBeTrue("case-keyed-complete");
        manifest.Methods[0].Cases.Length.ShouldBe(2, "method-keys-deduplicated");
        manifest.MembershipDigest.ShouldBe(chunk.FullDigest, "canonical-membership");
        var cases = Cases();
        Refuses(() => Manifest(seeded, chunk, [cases[0], cases[2]]), "case-membership-mismatch");
        var renamed = Cases();
        renamed[0].CaseKey = "row:c";
        Refuses(() => Manifest(seeded, chunk, renamed), "case-membership-mismatch");
        var forged = new RcBoundaryChunk
        {
            ChunkId = chunk.ChunkId, Suite = chunk.Suite, Boundary = chunk.Boundary, IdentityKeys = chunk.IdentityKeys, CaseKeys = chunk.CaseKeys,
            FullDigest = new string('f', 64), Collision = chunk.Collision, AlwaysDirty = chunk.AlwaysDirty,
        };
        Refuses(() => RcChunkDigest.NewManifest(RcInputInventory.Read(repo.Root, seeded), "server", forged, Cases(), actual), "chunk-membership-mismatch");
    }

    [Test]
    public void CaseKeyedSingletonsMatchMembership()
    {
        _ = Manifest(seeded);
        var all = Cases();
        RcChunkCase[] cases = [all[0], all[2]];
        var chunk = CaseKeyedChunk(cases);
        var manifest = Manifest(seeded, chunk, cases);
        (manifest.Unknown.Length == 0 && manifest.Methods.Length == 2 && manifest.Cases.Length == 2).ShouldBeTrue("case-keyed-singletons-complete");
        manifest.MembershipDigest.ShouldBe(chunk.FullDigest, "singleton-canonical-membership");
        Refuses(() => Manifest(seeded, chunk, [.. cases, NewCase("antiphon", "Example.Queue", "Check", "extra")]), "case-membership-mismatch");
    }

    [Test]
    public void CaseOnlyMembershipChangesDigest()
    {
        _ = Manifest(seeded);
        var manifests = new List<RcChunkInputManifest>();
        foreach (var caseKey in new[] { "row:a", "row:b" })
        {
            var chunk = RcBoundary.NewChunk([new RcBoundaryRow { Suite = "antiphon", Boundary = "application", IdentityKey = "Example.Queue/Check", CaseKey = caseKey }]);
            var manifest = Manifest(seeded, chunk, [NewCase("antiphon", "Example.Queue", "Check", caseKey)]);
            manifest.MembershipDigest.ShouldBe(chunk.FullDigest, "canonical-case-digest:" + caseKey);
            manifests.Add(manifest);
        }
        manifests.Count.ShouldBe(2, "two-case-memberships");
        manifests[0].MembershipDigest.ShouldNotBe(manifests[1].MembershipDigest, "case-only-membership-different");
        RcChunkDigest.Compare(manifests[0], manifests[1]).Reasons.ShouldContain("membership-changed", "case-only-membership-dirty");
    }

    [Test]
    public void CanonicalCaseOrderAndEncoding()
    {
        _ = Manifest(seeded);
        // JSON escaping reverses these two case keys; S2 compares their raw strings.
        RcChunkCase[] rows = [NewCase("antiphon", "Example.Queue", "Check", "#"), NewCase("antiphon", "Example.Queue", "Check", "\""), Cases()[2]];
        var manifest = Manifest(seeded, cases: rows);
        (manifest.Cases[0].CaseKey == "\"" && manifest.Cases[1].CaseKey == "#").ShouldBeTrue("canonical-global-case-order");
        manifest.Methods[0].Cases[0].CaseKey.ShouldBe("\"", "canonical-method-case-order");
        Manifest(seeded, cases: [rows[2], rows[1], rows[0]]).Digest.ShouldBe(manifest.Digest, "case-order-independent");
        // CARD-1871 rejects invalid UTF-16; S3b must use the same encoding rule.
        RefusesEncoding(() => RcChunkDigest.ManifestDigest(Rows(["row", ((char)0xD800).ToString()], ["other"])), "canonical-field");
        IsDigest(RcChunkDigest.ManifestDigest(Rows(["row", Replacement], ["other"]))).ShouldBeTrue("replacement-character-is-valid");
    }

    [Test]
    public void LoneSurrogatesFailClosed()
    {
        // No digest/decision is returned for malformed paths or case names.
        foreach (var codeUnit in new[] { 0xD800, 0xDC00 })
        {
            var invalid = ((char)codeUnit).ToString();
            RefusesEncoding(() => RcChunkDigest.ManifestDigest(Rows(["row", invalid], ["other"])), "field-" + codeUnit);
            var snapshot = RcInputInventory.Read(repo.Root, seeded);
            var owner = snapshot.Suites.First(s => s.Id == "server");
            owner.Inputs[0].Path = "src/Core/bad" + invalid + ".txt";
            RefusesEncoding(() => RcChunkDigest.NewManifest(snapshot, "server", DefaultChunk(), Cases(), actual), "path-" + codeUnit);
            var badCases = Cases();
            badCases[0].CaseKey = "row:" + invalid;
            RefusesEncoding(() => Manifest(seeded, cases: badCases), "case-" + codeUnit);
        }
        var validCases = Cases();
        validCases[0].CaseKey = "row:" + Replacement;
        var valid = Manifest(seeded, cases: validCases);
        (valid.Unknown.Length == 0 && IsDigest(valid.Digest)).ShouldBeTrue("valid-unicode-still-accepted");
    }

    [Test]
    public void DependencyEdgesAffectDigest()
    {
        var before = Manifest(seeded);
        var snapshot = RcInputInventory.Read(repo.Root, seeded);
        var owner = snapshot.Suites.First(s => s.Id == "server");
        owner.Edges.Length.ShouldBeGreaterThan(0, "fixture-has-dependency-edges");
        var inputRecords = Json(owner.Inputs);
        owner.Edges = [];
        var withoutEdges = RcChunkDigest.NewManifest(snapshot, "server", DefaultChunk(), Cases(), actual);
        Json(withoutEdges.Inputs).ShouldBe(inputRecords, "edge-only-change-keeps-input-records");
        withoutEdges.InputDigest.ShouldNotBe(before.InputDigest, "dependency-edges-bound-independently");
        RcChunkDigest.Compare(before, withoutEdges).Reasons.ShouldContain("inputs-changed", "dependency-edge-only-dirty");
    }
}
