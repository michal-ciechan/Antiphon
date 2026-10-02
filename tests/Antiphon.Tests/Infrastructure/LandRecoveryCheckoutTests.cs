using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandRecoveryCheckoutTests
{
    [Test]
    public async Task C883_EqualIndexAndWorktreeAccepted()
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeTrue("G.EqualOldIndexAndBytesAccepted");
        (await f.RequiredAsync(f.Source, "write-tree")).Trim()
            .ShouldBe((await f.RequiredAsync(f.Repository, "rev-parse", local + "^{tree}")).Trim(),
                "G.InspectionDidNotMutateIndex");
    }

    [Test]
    public async Task C883_IndexOnlyDifferenceRefused()
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        var file = Path.Combine(f.Source, "feature.txt");
        var old = await File.ReadAllBytesAsync(file);
        await File.WriteAllTextAsync(file, "staged edit\n");
        await f.RequiredAsync(f.Source, "add", "feature.txt");
        await File.WriteAllBytesAsync(file, old);
        var staged = (await f.RequiredAsync(f.Source, "rev-parse", ":feature.txt")).Trim();
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse("G.IndexOnlyDifferenceRefused");
        (await f.RequiredAsync(f.Source, "rev-parse", ":feature.txt")).Trim()
            .ShouldBe(staged, "G.InspectionPreservedStagedBlob");
    }

    [Test]
    public async Task C883_WorktreeOnlyDifferenceRefused()
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        var file = Path.Combine(f.Source, "feature.txt");
        await File.WriteAllTextAsync(file, "unstaged edit\n");
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse("G.WorktreeOnlyDifferenceRefused");
        (await File.ReadAllTextAsync(file)).ShouldBe("unstaged edit\n", "G.InspectionPreservedBytes");
    }

    private static async Task<(string Local, string Reviewed)> HalfResetAsync(LandingGitFixture f)
    {
        await f.InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Source, "feature.txt"), "old\n");
        await f.RequiredAsync(f.Source, "add", ".");
        await f.RequiredAsync(f.Source, "commit", "-m", "old tip");
        var local = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        var reviewedTree = Path.Combine(f.Root, "trees", "reviewed");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", reviewedTree, local);
        await File.WriteAllTextAsync(Path.Combine(reviewedTree, "feature.txt"), "reviewed\n");
        await f.RequiredAsync(reviewedTree, "add", ".");
        await f.RequiredAsync(reviewedTree, "commit", "-m", "reviewed tip");
        var reviewed = (await f.RequiredAsync(reviewedTree, "rev-parse", "HEAD")).Trim();
        await f.RequiredAsync(f.Repository, "update-ref", "--no-deref", f.SourceRef, reviewed, local);
        return (local, reviewed);
    }
}
