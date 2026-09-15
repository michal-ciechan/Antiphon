using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class WorktreeBaseSelectionTests
{
    [Test]
    public async Task C540_FullTipIdentityIsUsed()
    {
        using var repo = new ScratchGitRepo("c540-tip");
        await repo.CommitFileAsync("base", "base");
        var before = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("branch", "old");
        await repo.CommitFileAsync("next", "next");
        var after = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        var git = new SiblingProbeGit(); var service = ProbeService(git);
        var a = await service.ResolveKeptBranchTipAsync(repo.Path, "old", default);
        var b = await service.ResolveKeptBranchTipAsync(repo.Path, "master", default);
        a.ShouldBe(before); b.ShouldBe(after);
        await repo.GitAsync("branch", "-f", "old", after);
        (await service.IsCommitAncestorAsync(repo.Path, a!, b!, default)).ShouldBeTrue();
        git.Commands.Last().ShouldBe(new[] { "merge-base", "--is-ancestor", before, after });
        (await service.DescribeKeptBranchAsync(repo.Path, a!, b!, default))!.Subject.ShouldBe("add base");
        foreach (var output in new[] { "deadbeef", "unknown", before + "\n" + after, new string('g', 40) })
        {
            git.Probe = _ => new(0, output, "");
            (await service.ResolveKeptBranchTipAsync(repo.Path, "master", default)).ShouldBeNull();
        }
        git.Probe = _ => new(0, new string('A', 64) + "\n", "");
        (await service.ResolveKeptBranchTipAsync(repo.Path, "master", default)).ShouldBe(new string('a', 64));
    }

    [Test]
    public async Task C540_PatchEquivalentSiblingsStillWarn()
    {
        using var repo = new ScratchGitRepo("c540-cherry");
        await repo.CommitFileAsync("base", "base");
        await repo.GitAsync("checkout", "-b", "first");
        await repo.CommitFileAsync("work", "shared patch");
        var a = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        await repo.GitAsync("checkout", "-b", "second", "master");
        // Different commit message forces a distinct object with exactly the same patch.
        await repo.GitAsync("cherry-pick", a);
        await repo.GitAsync("commit", "--amend", "-m", "equivalent replay");
        var b = (await repo.GitReadAsync("rev-parse", "HEAD")).Trim();
        a.ShouldNotBe(b);
        var service = ProbeService(new SiblingProbeGit());
        (await service.ContainsPatchesAsync(repo.Path, a, b, default)).ShouldBeTrue();
        (await service.ContainsPatchesAsync(repo.Path, b, a, default)).ShouldBeTrue();
        (await service.IsCommitAncestorAsync(repo.Path, a, b, default)).ShouldBeFalse();
        (await service.IsCommitAncestorAsync(repo.Path, b, a, default)).ShouldBeFalse();
    }

    [Test]
    [Arguments(1)]
    [Arguments(128)]
    [Arguments(-1)]
    public async Task C540_AncestryProbeFailureRetainsWarnings(int exit)
    {
        using var repo = new ScratchGitRepo("c540-probe-failure");
        var git = new SiblingProbeGit { Probe = _ => exit == -1 ? throw new IOException("owned failure") : new(exit, "", "") };
        var service = ProbeService(git);
        var a = new string('a', 40); var b = new string('b', 40);
        var edges = new List<(string, string)>();
        if (await service.IsCommitAncestorAsync(repo.Path, a, b, default)) edges.Add((a, b));
        SiblingWarningReducer.Reduce([
            new(Guid.NewGuid(), "a", DateTime.UtcNow, a), new(Guid.NewGuid(), "b", DateTime.UtcNow, b)], edges).Count.ShouldBe(2);
        (await service.ResolveKeptBranchTipAsync(repo.Path, "master", default)).ShouldBeNull();
        var count = git.Commands.Count;
        (await service.IsCommitAncestorAsync(repo.Path, "deadbeef", b, default)).ShouldBeFalse();
        git.Commands.Count.ShouldBe(count);
    }

    [Test]
    public async Task C540_ProbeCancellationPropagates()
    {
        using var repo = new ScratchGitRepo("c540-cancel");
        var git = new SiblingProbeGit { Probe = _ => throw new OperationCanceledException() };
        var service = ProbeService(git);
        await Should.ThrowAsync<OperationCanceledException>(() => service.ResolveKeptBranchTipAsync(repo.Path, "master", default));
        await Should.ThrowAsync<OperationCanceledException>(() => service.IsCommitAncestorAsync(repo.Path, new string('a', 40), new string('b', 40), default));
    }

    private static DelegationWorktreeService ProbeService(LandingGit git) => new(
        null!, null!, NullLogger<DelegationWorktreeService>.Instance, null!, landingGit: git);

    private sealed class SiblingProbeGit : LandingGit
    {
        public List<string[]> Commands { get; } = [];
        public Func<IReadOnlyList<string>, LandingGitResult?>? Probe { get; set; }
        public override Task<LandingGitResult> RunAsync(string repository, IReadOnlyList<string> arguments, CancellationToken ct)
        {
            Commands.Add(arguments.ToArray());
            return Probe?.Invoke(arguments) is { } result ? Task.FromResult(result) : base.RunAsync(repository, arguments, ct);
        }
    }
}
