using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandSourceIdentityTests
{
    [Test]
    [Arguments("missing_ref", "source_ref_missing")]
    [Arguments("ref_error", "source_ref_error")]
    [Arguments("status_error", "status_error")]
    [Arguments("wrong_repo", "wrong_repository")]
    public async Task C448_V03_UnknownIdentityIsPreserved(string variant, string reason)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        if (variant == "missing_ref")
            await fixture.RequiredAsync(fixture.Repository, "update-ref", "-d", fixture.SourceRef, fixture.SeedSha);
        fixture.Git.BeforeCommand = (_, args) => Task.FromResult<Antiphon.Server.Application.Dtos.LandingGitResult?>(
            variant == "ref_error" && args[0] == "show-ref" && args.Contains("--exists")
                || variant == "status_error" && args[0] == "status"
                ? new(1, "", "injected_error") : null);
        var coordinates = variant == "wrong_repo"
            ? fixture.Coordinates with { RepositoryPath = fixture.Remote } : fixture.Coordinates;
        var result = await fixture.Git.InspectAsync(coordinates, CancellationToken.None);
        result.Reason.ShouldBe(reason);
        File.Exists(Path.Combine(fixture.Source, "keep.txt")).ShouldBeTrue();
        (await File.ReadAllTextAsync(Path.Combine(fixture.Source, "keep.txt"))).ShouldBe("seed\n");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V01_DetachedHeadIsPreserved(bool uniqueCommit)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Source, "checkout", "--detach");
        if (uniqueCommit)
        {
            await File.WriteAllTextAsync(Path.Combine(fixture.Source, "keep.txt"), "unique detached work\n");
            await fixture.RequiredAsync(fixture.Source, "commit", "-am", "unique");
        }
        var head = await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD");
        var bytes = await File.ReadAllBytesAsync(Path.Combine(fixture.Source, "keep.txt"));
        fixture.Git.Trace.Clear();
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        result.Accepted.ShouldBeFalse();
        result.Reason.ShouldBe("detached_head");
        fixture.Git.Trace.ShouldNotContain(a => a[0] == "rebase" || a[0] == "push" || a[0] == "update-ref"
            || a[0] == "worktree" && a[1] == "remove");
        (await fixture.RequiredAsync(fixture.Source, "rev-parse", "HEAD")).ShouldBe(head);
        (await File.ReadAllBytesAsync(Path.Combine(fixture.Source, "keep.txt"))).ShouldBe(bytes);
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V02_SwitchedBranchIsPreserved(bool unequalSha)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        await fixture.RequiredAsync(fixture.Source, "checkout", "-b", "other");
        if (unequalSha) await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "other");
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        result.Reason.ShouldBe("source_branch_mismatch");
        (await fixture.RequiredAsync(fixture.Source, "symbolic-ref", "HEAD")).Trim().ShouldBe("refs/heads/other");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("staged")]
    [Arguments("unstaged")]
    [Arguments("untracked")]
    [Arguments(".antiphon/report.txt")]
    [Arguments(".claude/notes.txt")]
    [Arguments("bin-private/keep.txt")]
    [Arguments("bin-land/keep.txt")]
    [Arguments("MERGE_HEAD")]
    [Arguments("CHERRY_PICK_HEAD")]
    [Arguments("REVERT_HEAD")]
    [Arguments("rebase-merge")]
    [Arguments("rebase-apply")]
    [Arguments("sequencer")]
    public async Task C448_V04_ProtectedContentsArePreserved(string variant)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var initial = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        initial.Accepted.ShouldBeTrue(initial.Reason);
        var isState = variant is "MERGE_HEAD" or "CHERRY_PICK_HEAD" or "REVERT_HEAD" or "rebase-merge" or "rebase-apply" or "sequencer";
        var ignored = variant.Contains('/');
        var file = isState ? Path.Combine(initial.Snapshot!.GitDirectory, variant)
            : Path.Combine(fixture.Source, variant is "staged" or "unstaged" ? "keep.txt" : variant);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "protected unique bytes\n");
        if (variant == "staged") await fixture.RequiredAsync(fixture.Source, "add", "keep.txt");
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        if (ignored)
        {
            result.Accepted.ShouldBeTrue(result.Reason);
            result.Snapshot!.IgnoredPaths.ShouldContain(variant);
        }
        else result.Reason.ShouldBe(isState ? "active_sequencer" : "source_dirty");
        (await File.ReadAllTextAsync(file)).ShouldBe("protected unique bytes\n");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V10_SourceMutationInvalidatesVerification()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var reached = false;
        fixture.Git.BeforeCommand = async (path, args) =>
        {
            if (args[0] == "status" && !reached)
            {
                reached = true;
                await fixture.RequiredAsync(fixture.Source, "commit", "--allow-empty", "-m", "race");
            }
            return null;
        };
        var result = await fixture.Git.InspectAsync(fixture.Coordinates, CancellationToken.None);
        reached.ShouldBeTrue();
        result.Reason.ShouldBe("source_changed");
        await fixture.AssertRemoteSourceAsync();
    }
}
