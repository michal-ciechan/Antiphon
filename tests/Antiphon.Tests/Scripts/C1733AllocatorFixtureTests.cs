using Antiphon.Tests.Scripts.Nightly;
using Antiphon.Tests.TestHelpers;
using TUnit.Core;

namespace Antiphon.Tests.Scripts;

// CARD-1733: the C1339 real-git fixture's own guards, in process on its C# twin (ReleaseGateGitFixture). Unit 15 of
// 2026-11-22-remaining-ps1-to-csharp.md deleted scripts/test-release-gate.ps1 and the PowerShell fixtures these cases ran
// (scripts/fixtures/c1733-allocator.ps1, c1339-fixture-state.ps1 and the adapter c1339-release-git.ps1); the rows keep their
// names. Dropped with them: C1733_NativeMetadata (it counted PowerShell provider cmdlet calls inside
// Assert-C1339ContainedPath; ReleaseGateGitFixture.AssertContained reads File.GetAttributes, with no provider to count) and
// SharedSeed's seed-built-once row (the PowerShell seeded once per candidate kind and cloned per case; the C# fixture builds
// every case fresh, so there is no seed to share). Unit 14 had already dropped C1733_ReservationFinally, C1733_SnapshotWork
// and SharedSeed's clone-fresh-authority row with the promoter's PowerShell snapshot.
[Category("Slow")]
[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class C1733AllocatorFixtureTests
{
    [Test]
    public void Boundaries_and_changed_links_are_checked_fresh()
    {
        using var fx = NightlyFixture.Create("c1733-boundaries");
        var rows = new NightlyCaseRows();
        var root = fx.PathOf("root");
        var path = Path.Combine(root, "child");
        Directory.CreateDirectory(path);
        ReleaseGateGitFixture.AssertContained(root, path);
        ReleaseGateGitFixture.AssertContained(root, Path.Combine(path, "absent", "leaf"));
        rows.Check(Refusal(() => ReleaseGateGitFixture.AssertContained(root, root + "-sibling" + Path.DirectorySeparatorChar + "leaf")) == "fixture-path-outside-root", "C1733 sibling-prefix");
        rows.Check(Refusal(() => ReleaseGateGitFixture.AssertContained(root, root)) == "fixture-path-outside-root", "C1733 root-boundary");
        // Replace a previously accepted ancestor. Fresh metadata must observe the link.
        Directory.Delete(path);
        var target = fx.PathOf("target");
        Directory.CreateDirectory(target);
        EvidenceLinks.Owned(path, target);
        rows.Check(Refusal(() => ReleaseGateGitFixture.AssertContained(root, Path.Combine(path, "absent", "leaf"))) == "fixture-linked-path", "C1733 changed-ancestor-link");
        var alias = fx.PathOf("aliases", "alias");
        Directory.CreateDirectory(fx.PathOf("aliases"));
        EvidenceLinks.Owned(alias, root);
        rows.Check(Refusal(() => ReleaseGateGitFixture.AssertContained(Path.Combine(alias, "nested"), Path.Combine(alias, "nested", "absent"))) == "fixture-linked-path",
            "C1733 above-root-link");
        var dangling = Path.Combine(root, "dangling");
        var danglingTarget = fx.PathOf("dangling-target");
        Directory.CreateDirectory(danglingTarget);
        EvidenceLinks.Owned(dangling, danglingTarget);
        Directory.Delete(danglingTarget);
        rows.Check(Refusal(() => ReleaseGateGitFixture.AssertContained(root, dangling)) == "fixture-linked-path", "C1733 dangling-link");
        rows.ShouldAllPass(5, "C1733 sibling-prefix", "C1733 root-boundary", "C1733 changed-ancestor-link", "C1733 above-root-link", "C1733 dangling-link");
    }

    [Test]
    public void Shared_seed_keeps_recipients_private()
    {
        // Two fixtures never share a recipient: a tag on one's remote, a release in one's GitHub store and one's fault file
        // leave the other's untouched, and their roots and release card routes differ.
        var rows = new NightlyCaseRows();
        using var a = ReleaseGateGitFixture.Create();
        using var b = ReleaseGateGitFixture.Create();
        var bStore = b.Fx.GitHub.State();
        a.AddTag(a.FinalTag, a.Head);
        a.Fx.GitHub.Seed(new FakeReleaseGitHub.StoredRelease { Id = "private", TagName = "private" });
        a.SetRead("refs/tags/" + a.FinalTag, push: "before");
        var remote = b.LocalGit(["show-ref", "--verify", "--hash", "refs/tags/" + b.FinalTag], remote: true, allowAbsent: true);
        rows.Check(b.Fx.GitHub.State() == bStore && remote.Length == 0 && b.Faults == null && a.Root != b.Root && a.Fx.CardApi.Url != b.Fx.CardApi.Url,
            "C1733 private-recipients", "remote=" + remote + " roots=" + a.Root + "|" + b.Root);
        rows.ShouldAllPass(1, "C1733 private-recipients");
    }

    private static string Refusal(Action action)
    {
        try { action(); }
        catch (InvalidOperationException e) { return e.Message; }
        return "accepted";
    }
}
