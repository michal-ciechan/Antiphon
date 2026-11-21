using System.Text.Json;
using System.Text.Json.Nodes;
using Antiphon.Nightly;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

/// <summary>
/// CARD-1836 S7a: read-only baseline proposals, in-process (moved from test-rc-baseline.ps1). Release pointers at
/// three dated commits share the real sealed S5b corpus of the admission reader fixture: the recorded release wins,
/// then the newest ancestor release (ordinal ties), then the newest same-line candidate; a later failure, another
/// platform, a moved or corrupt ref, a missing or unrelated baseline and an unproven history all force Full.
/// </summary>
[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
[Timeout(120_000)]
public sealed class RcReleaseBaselineTests : IDisposable
{
    private const string OwnersMap =
        "{\"schemaVersion\":1,\"suites\":[{\"id\":\"server\",\"project\":\"tests/Server/Server.csproj\"}],\"roots\":[{\"path\":\"scripts/**\",\"consumers\":[\"*\"],\"reason\":\"harness\"}]}";

    /// <summary>The fixture commits, the two sealed corpora, their pointers, and the editable context and history.</summary>
    private sealed class World
    {
        public string Old = "", New = "", Candidate = "";
        public RcAdmissionEvidenceFixture A = null!, B = null!;
        public RcBaselinePointer Po = null!, Pn = null!, Pt = null!, Rc = null!;
        public RcAdmissionContext Context = null!;
        public JsonObject History = null!;
        public string HistoryPath = "", HistoryDigest = "";

        public void SaveHistory()
        {
            File.WriteAllText(HistoryPath, History.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            HistoryDigest = RcAdmission.FileDigest(HistoryPath);
        }
    }

    private readonly RcInputFixtureRepository repo = new("bl-");
    private World world = null!;

    public void Dispose() => repo.Dispose();

    private string CommitAt(string date, params string[] args)
    {
        repo.GitWith(new Dictionary<string, string> { ["GIT_COMMITTER_DATE"] = date }, args);
        return repo.Head();
    }

    private static RcBaselinePointer Pointer(RcAdmissionReadArgs args, string refName) => new()
    {
        RefName = refName, SourceSha = args.ExpectedSha, Path = args.Path, Digest = args.ExpectedDigest, EvidenceRoot = args.EvidenceRoot,
    };

    private static RcBaselinePointer Copy(RcBaselinePointer p) => new() { RefName = p.RefName, SourceSha = p.SourceSha, Path = p.Path, Digest = p.Digest, EvidenceRoot = p.EvidenceRoot };

    [Before(Test)]
    public void Seed()
    {
        repo.Init();
        repo.Put(RcInputInventory.MapPath, OwnersMap);
        repo.Put("tests/Server/Server.csproj", "<Project />");
        repo.Put("tests/Server/Input.cs", "test input");
        repo.Git("add", "--all");
        var w = new World
        {
            Old = CommitAt("2026-11-10T12:00:00Z", "commit", "-qm", "base"),
        };
        w.New = CommitAt("2026-11-11T12:00:00Z", "commit", "--allow-empty", "-qm", "next");
        w.Candidate = CommitAt("2026-11-13T12:00:00Z", "commit", "--allow-empty", "-qm", "candidate");
        repo.Git("branch", "release/1.0.0", w.Old);
        repo.Git("branch", "release/1.0.1", w.New);
        repo.Git("branch", "release/1.0.2", w.New);
        // Real sealed S5b source/build/discovery/launch/TRX/anchor corpus, shared by these cases.
        var results = Path.Combine(repo.Scratch, "results");
        w.A = new RcAdmissionEvidenceFixture(Path.Combine(results, "old"), repo.Root, w.Old);
        w.A.FreezeAndRead();
        w.B = new RcAdmissionEvidenceFixture(Path.Combine(results, "new"), repo.Root, w.New);
        w.B.FreezeAndRead();
        w.Po = Pointer(w.A.Args, "refs/heads/release/1.0.0");
        w.Pn = Pointer(w.B.Args, "refs/heads/release/1.0.1");
        w.Pt = Pointer(w.B.Args, "refs/heads/release/1.0.2");
        w.Rc = Pointer(w.B.Args, w.New);
        var context = w.A.Args.CurrentContext;
        w.Context = new RcAdmissionContext { Repository = context.Repository, Platform = context.Platform, RcLine = context.RcLine, Contract = context.Contract };
        w.HistoryPath = Path.Combine(results, "current-history.json");
        w.History = new JsonObject
        {
            ["format"] = "antiphon-rc-baseline-history-v1", ["candidateSha"] = w.Candidate, ["complete"] = true, ["failures"] = new JsonArray(),
        };
        w.SaveHistory();
        world = w;
    }

    private RcBaselineProposal Pick(RcBaselinePointer[] pointers, RcBaselinePointer? recorded = null) =>
        RcBaseline.Propose(repo.Root, world.Candidate, world.Context, pointers, recorded!, world.HistoryPath, world.HistoryDigest, RcAdmissionEvidenceFixture.UtcNow);

    private static void Refused(RcBaselineProposal d, string reason) =>
        (d.Selected == null && d.ForceFull && d.Reasons.Contains(reason)).ShouldBeTrue(reason + ": reason=" + d.Reason + " reasons=" + string.Join(",", d.Reasons));

    [Test]
    public void AncestorReleaseThenSameLineFallback()
    {
        var w = world;
        var d = Pick([w.Rc, w.Po, w.Pt, w.Pn]);
        (d.Selected?.RefName == w.Pn.RefName && d.Selected.SourceSha == w.New && d.Reason == "ancestor-release")
            .ShouldBeTrue("release-precedence-and-ordinal-tie: " + d.Selected?.RefName + " " + d.Reason);
        (Pick([w.Pn, w.Pt, w.Po, w.Rc]).Selected?.RefName).ShouldBe(w.Pn.RefName, "input-order-independent");
        d = Pick([w.Pn, w.Po, w.Rc], w.Po);
        (d.Selected?.SourceSha == w.Old && d.Reason == "recorded-release").ShouldBeTrue("explicit-release-precedence: " + d.Reason);
        d = Pick([w.Rc]);
        (d.Selected?.SourceSha == w.New && d.Reason == "same-line-candidate").ShouldBeTrue("same-line-fallback: " + d.Reason);
        (d.FullAnchor.GetProperty("sourceSha").GetString() == w.New && d.ForceFull && !d.GateCredit && d.Reasons.Contains("release-graph-incomplete"))
            .ShouldBeTrue("anchor-and-inherited-full-gate");
        // Later failure facts survive an older explicit green baseline.
        w.History["failures"] = new JsonArray(new JsonObject { ["sourceSha"] = w.Candidate, ["identity"] = "Example.Tests/Check", ["outcome"] = "Failed" });
        w.SaveHistory();
        d = Pick([w.Po, w.Rc], w.Po);
        (d.Selected?.SourceSha == w.Old && d.Failures.Length == 1 && d.Failures[0].GetProperty("sourceSha").GetString() == w.Candidate
            && d.Reasons.Contains("post-baseline-failures") && d.ForceFull).ShouldBeTrue("later-failure-retained");
    }

    [Test]
    public void CrossPlatformMovedAndCorruptRefRefuse()
    {
        var w = world;
        w.Context.Platform = "linux";
        Refused(Pick([w.Po]), "baseline-platform-mismatch");
        w.Context.Platform = "windows";
        repo.Git("branch", "-f", "release/1.0.0", w.New);
        Refused(Pick([w.Po, w.Rc], w.Po), "baseline-ref-moved");
        repo.Git("branch", "-f", "release/1.0.0", w.Old);
        var corrupt = Copy(w.Po);
        corrupt.Digest = new string('f', 64);
        Refused(Pick([corrupt, w.Rc]), "admission-seal-mismatch");
        var conflict = Copy(w.Po);
        conflict.Digest = new string('e', 64);
        Refused(Pick([w.Po, conflict]), "baseline-pointer-conflict");
        File.AppendAllText(w.A.Args.Path, " ");
        Refused(Pick([w.Po, w.Rc]), "admission-seal-mismatch");
        Refused(Pick([w.Rc], w.Po), "baseline-recorded-pointer-missing");
    }

    [Test]
    public void MissingBaselineIsFull()
    {
        var w = world;
        Refused(Pick([]), "baseline-absent");
        // A real orphan commit with valid Git identity is unrelated, never a fallback.
        var tree = repo.Git("rev-parse", w.Old + "^{tree}");
        var orphan = repo.Git("commit-tree", tree, "-m", "unrelated");
        repo.Git("branch", "release/9.0.0", orphan);
        var unrelated = Copy(w.Po);
        unrelated.RefName = "refs/heads/release/9.0.0";
        unrelated.SourceSha = orphan;
        Refused(Pick([unrelated]), "baseline-absent");
        w.Context.RcLine = "rc/2026-11-14";
        Refused(Pick([w.Rc]), "baseline-absent");
        w.Context.RcLine = "rc/2026-11-13";
        w.History["complete"] = false;
        w.SaveHistory();
        Refused(Pick([w.Po]), "baseline-history-unproven");
    }
}
