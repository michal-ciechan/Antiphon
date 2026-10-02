using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandRecoveryCheckoutTests
{
    [Test]
    [Arguments("auto")]
    [Arguments("explicit")]
    public async Task C883_CrLfCheckoutUsesBuiltInConversionAndPreservesRawEdits(string attributes)
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Source, ".gitattributes"),
            attributes == "auto" ? "* text=auto\n" : "* text=auto\nfeature.txt text eol=crlf\n");
        var feature = Path.Combine(f.Source, "feature.txt");
        await File.WriteAllTextAsync(feature, "first\nsecond\n");
        await f.RequiredAsync(f.Source, "add", ".");
        await f.RequiredAsync(f.Source, "commit", "-m", "old text tip");
        var local = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        await f.RequiredAsync(f.Source, "config", "core.autocrlf", "true");
        await f.RequiredAsync(f.Source, "reset", "--hard", local);
        (await File.ReadAllTextAsync(feature)).Contains("\r\n", StringComparison.Ordinal)
            .ShouldBeTrue($"G.{attributes}.FixtureCheckedOutCrLf");
        var reviewedTree = Path.Combine(f.Root, "trees", "reviewed");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", reviewedTree, local);
        await File.WriteAllTextAsync(Path.Combine(reviewedTree, "feature.txt"), "reviewed\n");
        await f.RequiredAsync(reviewedTree, "add", ".");
        await f.RequiredAsync(reviewedTree, "commit", "-m", "reviewed text tip");
        var reviewed = (await f.RequiredAsync(reviewedTree, "rev-parse", "HEAD")).Trim();
        await f.RequiredAsync(f.Repository, "update-ref", "--no-deref", f.SourceRef, reviewed, local);
        var clean = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        clean.Accepted.ShouldBeTrue($"G.{attributes}.CrLfCheckoutAccepted");
        await File.WriteAllTextAsync(feature, "first\r\nsecond\n");
        var edited = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        edited.Accepted.ShouldBeFalse($"G.{attributes}.RawMixedEolEditRefused");
        (await File.ReadAllTextAsync(feature)).ShouldBe("first\r\nsecond\n",
            $"G.{attributes}.RawMixedEolEditPreserved");
    }

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

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C883_UntrackedAndIgnoredRefused(bool ignored)
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        var file = Path.Combine(f.Source, "sentinel.txt");
        if (ignored)
        {
            var exclude = (await f.RequiredAsync(f.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
            await File.AppendAllTextAsync(Path.GetFullPath(exclude, f.Source), "\nsentinel.txt\n");
        }
        await File.WriteAllTextAsync(file, "owned sentinel\n");
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse(ignored ? "G.IgnoredRefused" : "G.UntrackedRefused");
        (await File.ReadAllTextAsync(file)).ShouldBe("owned sentinel\n",
            ignored ? "G.IgnoredBytesPreserved" : "G.UntrackedBytesPreserved");
    }

    [Test]
    [Arguments("assume")]
    [Arguments("skip")]
    public async Task C883_UnsafeIndexModesRefuse(string mode)
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        await f.RequiredAsync(f.Source, "update-index",
            mode == "assume" ? "--assume-unchanged" : "--skip-worktree", "feature.txt");
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse(mode == "assume" ? "G.AssumeUnchangedRefused" : "G.SkipWorktreeRefused");
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, "G.IndexFlagInspectionDidNotMoveHead");
    }

    [Test]
    [Arguments("tree")]
    [Arguments("attributes")]
    [Arguments("cached-diff")]
    [Arguments("worktree-diff")]
    [Arguments("untracked")]
    [Arguments("ignored")]
    [Arguments("index-flags")]
    public async Task C883_ProbeErrorsRefuse(string probe)
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        var reached = 0;
        f.Git.BeforeCommand = (_, args) =>
        {
            var selected = probe switch
            {
                "tree" => args[0] == "ls-tree",
                "attributes" => args[0] == "check-attr",
                "cached-diff" => args[0] == "diff" && args.Contains("--cached"),
                "worktree-diff" => args[0] == "diff" && !args.Contains("--cached"),
                "untracked" => args[0] == "ls-files" && args.Contains("--others") && !args.Contains("--ignored"),
                "ignored" => args[0] == "ls-files" && args.Contains("--ignored"),
                "index-flags" => args[0] == "ls-files" && args.Contains("-v"),
                _ => false,
            };
            if (!selected || reached != 0) return Task.FromResult<LandingGitResult?>(null);
            reached++;
            return Task.FromResult<LandingGitResult?>(new LandingGitResult(128, "", "git_exit_128"));
        };
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        reached.ShouldBe(1, $"G.{probe}.FailureReached");
        proof.Accepted.ShouldBeFalse($"G.{probe}.FailureRefused");
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, $"G.{probe}.HeadPreserved");
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
