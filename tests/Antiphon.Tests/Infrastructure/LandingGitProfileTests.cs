using Antiphon.Server.Application.Dtos;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

/// <summary>CARD-0642 V-1: the per-land git profile counts what the land spends on git.</summary>
[Category("Unit")]
public sealed class LandingGitProfileTests
{
    [Test]
    public void C642_WorktreeListCountsAsListAndProcess()
    {
        var profile = new LandingGitProfile();
        profile.Record(["worktree", "list", "--porcelain", "-z"], TimeSpan.FromSeconds(1.2));
        profile.Record(["worktree", "remove", "x"], TimeSpan.FromSeconds(0.3));
        profile.Record(["status", "--porcelain=v1"], TimeSpan.FromSeconds(0.5));
        profile.Processes.ShouldBe(3);
        profile.WorktreeLists.ShouldBe(1, "only `worktree list` is a registration listing");
        profile.RemoteRoundTrips.ShouldBe(0);
        profile.GitSeconds.ShouldBe(2.0, 0.0001);
    }

    [Test]
    [Arguments("ls-remote", true)]
    [Arguments("fetch", true)]
    [Arguments("push", true)]
    [Arguments("rev-parse", false)]
    [Arguments("merge-base", false)]
    public void C642_RemoteCommandsCountAsRoundTrips(string command, bool remote)
    {
        var profile = new LandingGitProfile();
        profile.Record([command, "origin", "refs/heads/master"], TimeSpan.FromMilliseconds(10));
        profile.RemoteRoundTrips.ShouldBe(remote ? 1 : 0);
        profile.Processes.ShouldBe(1);
        profile.WorktreeLists.ShouldBe(0);
    }

    [Test]
    public void C642_DescribeRendersEveryCounter()
    {
        var profile = new LandingGitProfile();
        profile.Record(["worktree", "list", "--porcelain", "-z"], TimeSpan.FromSeconds(1.5));
        profile.Record(["ls-remote", "--refs"], TimeSpan.FromSeconds(1));
        profile.RegistrationHit();
        profile.RegistrationHit();
        profile.CanonicalHit();
        profile.CanonicalHit();
        profile.CanonicalHit();
        profile.Inspection();
        profile.Describe().ShouldBe(
            "processes=2 worktreeList=1 registrationHits=2 canonicalHits=3 inspections=1 remote=1 gitSeconds=2.50");
    }

    [Test]
    public void C642_CountersAreSafeUnderConcurrency()
    {
        var profile = new LandingGitProfile();
        Parallel.For(0, 10_000, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i =>
        {
            profile.Record(i % 2 == 0 ? ["worktree", "list"] : ["fetch", "origin"], TimeSpan.FromTicks(10));
            profile.RegistrationHit();
            profile.CanonicalHit();
            profile.Inspection();
        });
        profile.Processes.ShouldBe(10_000);
        profile.WorktreeLists.ShouldBe(5_000);
        profile.RemoteRoundTrips.ShouldBe(5_000);
        profile.RegistrationHits.ShouldBe(10_000);
        profile.CanonicalHits.ShouldBe(10_000);
        profile.Inspections.ShouldBe(10_000);
        profile.GitSeconds.ShouldBe(TimeSpan.FromTicks(100_000).TotalSeconds, 1e-9);
    }

    [Test]
    public void C642_GlobalOptionsDoNotHideTheSubcommand()
    {
        var profile = new LandingGitProfile();
        profile.Record(["-c", "http.extraHeader=x", "push", "origin", "HEAD"], TimeSpan.FromTicks(1));
        profile.Record(["-C", "/repo", "worktree", "list", "--porcelain"], TimeSpan.FromTicks(1));
        profile.RemoteRoundTrips.ShouldBe(1);
        profile.WorktreeLists.ShouldBe(1);
    }

    [Test]
    [Arguments("-c|merge.autoStash=false|merge|--ff-only|abc", true, false)]
    [Arguments("-c|rebase.autoStash=false|-c|rebase.updateRefs=false|rebase|abc", true, false)]
    [Arguments("commit|--allow-empty|-m|m", true, false)]
    [Arguments("worktree|remove|/x", true, true)]
    [Arguments("worktree|list|--porcelain|-z", false, false)]
    [Arguments("-c|core.x=y|worktree|list", false, false)]
    [Arguments("update-ref|refs/antiphon/land/t/o/source|abc|000", false, false)]
    [Arguments("update-ref|-d|refs/heads/feat/x|abc", true, false)]
    [Arguments("symbolic-ref|-q|HEAD", false, false)]
    [Arguments("symbolic-ref|HEAD|refs/heads/x", true, false)]
    [Arguments("fetch|--no-tags|origin|refs/heads/x:refs/antiphon/land/p", false, false)]
    [Arguments("-c|x=y|rev-parse|HEAD", false, false)]
    [Arguments("read-tree|HEAD", true, false)]
    [Arguments("gc|--auto", true, true)]
    public void C642_OwnMutationClassifierFindsTheSubcommand(string command, bool invalidates, bool topology)
    {
        // The land's own rebase and ff-merge run behind `-c` options; missing them left a stale HEAD cached.
        Antiphon.Server.Infrastructure.Git.LandingGit.ChangesRegistrations(command.Split('|'), out var drops).ShouldBe(invalidates);
        drops.ShouldBe(topology);
    }
}
