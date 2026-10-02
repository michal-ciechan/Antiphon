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
        foreach (var relative in (await f.RequiredAsync(f.Source, "ls-files", "-z"))
                     .Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(f.Source, relative.Replace('/', Path.DirectorySeparatorChar));
            var lf = (await File.ReadAllTextAsync(full)).Replace("\r\n", "\n", StringComparison.Ordinal);
            await File.WriteAllTextAsync(full, lf.Replace("\n", "\r\n", StringComparison.Ordinal));
        }
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
        var (local, reviewed) = await HalfResetAsync(f, "added");
        var file = Path.Combine(f.Source, "reviewed-addition.txt");
        if (ignored)
        {
            var exclude = (await f.RequiredAsync(f.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
            await File.AppendAllTextAsync(Path.GetFullPath(exclude, f.Source), "\nreviewed-addition.txt\n");
        }
        await File.WriteAllTextAsync(file, "owned sentinel\n");
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse(ignored ? "G.IgnoredRefused" : "G.UntrackedRefused");
        (await File.ReadAllTextAsync(file)).ShouldBe("owned sentinel\n",
            ignored ? "G.IgnoredBytesPreserved" : "G.UntrackedBytesPreserved");
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C954_IgnoredReviewedPathObstructsReset(bool directory)
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        await File.WriteAllTextAsync(Path.Combine(f.Source, "feature.txt"), "old\n");
        await f.RequiredAsync(f.Source, "add", ".");
        await f.RequiredAsync(f.Source, "commit", "-m", "old tip");
        var local = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        var reviewedTree = Path.Combine(f.Root, "trees", "reviewed");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", reviewedTree, local);
        var tracked = directory ? Path.Combine("obstruction", "tracked.txt") : "obstruction";
        var reviewedPath = Path.Combine(reviewedTree, tracked);
        Directory.CreateDirectory(Path.GetDirectoryName(reviewedPath)!);
        await File.WriteAllTextAsync(reviewedPath, "reviewed\n");
        await f.RequiredAsync(reviewedTree, "add", ".");
        await f.RequiredAsync(reviewedTree, "commit", "-m", "reviewed tip");
        var reviewed = (await f.RequiredAsync(reviewedTree, "rev-parse", "HEAD")).Trim();
        await f.RequiredAsync(f.Repository, "update-ref", "--no-deref", f.SourceRef, reviewed, local);
        var excluded = (await f.RequiredAsync(f.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(excluded, f.Source), "\nobstruction\n");
        var obstructingPath = directory ? Path.Combine(f.Source, "obstruction", "tracked.txt")
            : Path.Combine(f.Source, "obstruction");
        Directory.CreateDirectory(Path.GetDirectoryName(obstructingPath)!);
        await File.WriteAllTextAsync(obstructingPath, "owner bytes\n");
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse(directory ? "G.IgnoredDirectoryRefused" : "G.IgnoredTrackedPathRefused");
        proof.Reason.ShouldBe("source_dirty", "G.IgnoredObstructionIsSourceDirty");
        (await File.ReadAllTextAsync(obstructingPath)).ShouldBe("owner bytes\n", "G.IgnoredObstructionBytesPreserved");
    }

    [Test]
    [Arguments("assume")]
    [Arguments("skip")]
    [Arguments("unmerged")]
    [Arguments("sparse")]
    public async Task C883_UnsafeIndexModesRefuse(string mode)
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        if (mode == "sparse") await f.RequiredAsync(f.Source, "config", "core.sparseCheckout", "true");
        else if (mode == "unmerged")
        {
            await f.RequiredAsync(f.Source, "read-tree", "-m", f.SeedSha, local, reviewed);
            (await f.RequiredAsync(f.Source, "ls-files", "--unmerged", "-z")).ShouldNotBeEmpty("G.RealUnmergedIndexSeeded");
            // Reach the unmerged-entry predicate without a redundant diff refusal masking it.
            f.Git.BeforeCommand = (_, args) => Task.FromResult<LandingGitResult?>(args[0] == "diff" ? new LandingGitResult(0, "", "") : null);
        }
        else await f.RequiredAsync(f.Source, "update-index", mode == "assume" ? "--assume-unchanged" : "--skip-worktree", "feature.txt");
        f.Git.Commands.Clear();
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse($"G.{mode}.UnsafeIndexRefused");
        AssertReadOnly(f);
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
    [Arguments("blob-read")]
    [Arguments("byte-io")]
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
                "blob-read" or "byte-io" => args[0] == "cat-file",
                _ => false,
            };
            if (!selected || reached != 0) return Task.FromResult<LandingGitResult?>(null);
            reached++;
            if (probe == "byte-io")
            {
                File.Delete(Path.Combine(f.Source, "feature.txt")); // Owned file disappears after enumeration, before raw read.
                return Task.FromResult<LandingGitResult?>(null);
            }
            return Task.FromResult<LandingGitResult?>(new LandingGitResult(128, "", "git_exit_128"));
        };
        f.Git.Commands.Clear();
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        reached.ShouldBe(1, $"G.{probe}.FailureReached");
        AssertReadOnly(f);
        proof.Accepted.ShouldBeFalse($"G.{probe}.FailureRefused");
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, $"G.{probe}.HeadPreserved");
    }

    [Test]
    [Arguments("merge")]
    [Arguments("rebase")]
    [Arguments("cherry-pick")]
    [Arguments("sequencer-directory")]
    public async Task C883_SequencerRefuses(string variant)
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        var admin = (await f.RequiredAsync(f.Source, "rev-parse", "--absolute-git-dir")).Trim();
        var marker = Path.Combine(admin, variant switch { "merge" => "MERGE_HEAD", "rebase" => "rebase-merge", "cherry-pick" => "CHERRY_PICK_HEAD", _ => "sequencer" });
        if (variant is "rebase" or "sequencer-directory") Directory.CreateDirectory(marker);
        else await File.WriteAllTextAsync(marker, local + "\n");
        f.Git.Commands.Clear();
        var identity = await f.Git.InspectAsync(f.Coordinates, LandInspectionScope.IdentityOnly, CancellationToken.None);
        identity.Reason.ShouldBe("active_sequencer", "G.SequencerIdentityRefused");
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse("G.SequencerRecoveryRefused");
        Path.Exists(marker).ShouldBeTrue("G.SequencerMarkerPreserved");
        AssertReadOnly(f);
    }

    [Test]
    [Arguments("gitlink")]
    [Arguments("custom-filter")]
    [Arguments("working-tree-encoding")]
    [Arguments("ident")]
    public async Task C883_SubmoduleAndFilterRefuse(string variant)
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f, variant);
        var filterCalls = 0;
        f.Git.BeforeCommand = (_, args) =>
        {
            if (args.Any(a => a.Contains("c939-forbidden-filter", StringComparison.Ordinal)))
            {
                filterCalls++;
                return Task.FromResult<LandingGitResult?>(new LandingGitResult(128, "", "external_filter_refused"));
            }
            return Task.FromResult<LandingGitResult?>(null);
        };
        f.Git.Commands.Clear();
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBeFalse($"G.{variant}.UnsupportedTransformRefused");
        filterCalls.ShouldBe(0, "G.ExternalFilterNeverInvoked");
        File.Exists(Path.Combine(f.Root, "filter-invoked")).ShouldBeFalse("G.NoExternalFilterProcess");
        AssertReadOnly(f);
    }

    [Test]
    public async Task C883_IdentityResampleRefusesChange()
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f);
        var hit = 0;
        f.Git.AfterCommand = async (_, args, result) =>
        {
            if (args[0] != "cat-file" || !result.Succeeded) return;
            hit++;
            await f.RequiredAsync(f.Repository, "update-ref", f.SourceRef, local, reviewed);
        };
        f.Git.Commands.Clear();
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        hit.ShouldBe(1, "G.IdentityChangedAfterInitialSample");
        proof.Accepted.ShouldBeFalse("G.FinalIdentityChangeRefused");
        proof.Reason.ShouldBe("adopt_local_changed");
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim().ShouldBe(local, "G.ChangedHeadPreserved");
        f.Git.Commands.Count(x => x.Arguments[0] == "update-ref").ShouldBe(1, "G.OnlyFixtureMutates");
        f.Git.Commands.ShouldNotContain(x => x.Arguments[0] == "reset");
    }

    [Test]
    [Arguments("binary")]
    [Arguments("eol")]
    [Arguments("mode")]
    public async Task C883_BinaryEolAndModeComparison(string variant)
    {
        await using var f = new LandingGitFixture();
        var (local, reviewed) = await HalfResetAsync(f, variant);
        var feature = Path.Combine(f.Source, "feature.txt");
        if (variant == "eol")
        {
            await f.RequiredAsync(f.Source, "config", "core.autocrlf", "true");
            foreach (var relative in (await f.RequiredAsync(f.Source, "ls-files", "-z")).Split('\0', StringSplitOptions.RemoveEmptyEntries))
            {
                var full = Path.Combine(f.Source, relative);
                var text = (await File.ReadAllTextAsync(full)).Replace("\r\n", "\n", StringComparison.Ordinal);
                await File.WriteAllTextAsync(full, text.Replace("\n", "\r\n", StringComparison.Ordinal));
            }
        }
        f.Git.Commands.Clear();
        var equal = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        if (variant == "mode" && OperatingSystem.IsWindows())
        {
            equal.Accepted.ShouldBeFalse("G.WindowsModeEvidenceUncertain");
            AssertReadOnly(f);
            return;
        }
        equal.Accepted.ShouldBeTrue($"G.{variant}.EqualBytesAccepted");
        if (variant == "binary") await File.WriteAllBytesAsync(feature, [0, 1, 3, 10]);
        else if (variant == "eol")
        {
            await File.WriteAllTextAsync(feature, "old\r\nsecond\n");
            (await f.Git.RunAsync(f.Source, ["diff", "--quiet", local, "--"], CancellationToken.None)).ExitCode.ShouldBe(0, "G.RawEolEditNormalizesClean");
        }
        else File.SetUnixFileMode(feature, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var editedBytes = await File.ReadAllBytesAsync(feature);
        var edited = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        edited.Accepted.ShouldBeFalse($"G.{variant}.RawEditRefused");
        (await File.ReadAllBytesAsync(feature)).ShouldBe(editedBytes, "G.RawEditPreserved");
        if (variant == "mode")
        {
            var link = Path.Combine(f.Source, "link");
            new FileInfo(link).LinkTarget.ShouldBe("feature.txt");
            File.Delete(link);
            File.CreateSymbolicLink(link, "keep.txt");
            (await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None)).Accepted.ShouldBeFalse("G.SymlinkTargetEditRefused");
            new FileInfo(link).LinkTarget.ShouldBe("keep.txt", "G.SymlinkTargetPreserved");
        }
        AssertReadOnly(f);
    }

    private static void AssertReadOnly(LandingGitFixture f) =>
        f.Git.Commands.ShouldNotContain(x => (x.Arguments[0] == "reset" || x.Arguments[0] == "clean" || x.Arguments[0] == "checkout" || x.Arguments[0] == "checkout-index" || x.Arguments[0] == "add" || x.Arguments[0] == "update-index" || x.Arguments[0] == "update-ref" || x.Arguments[0] == "push"), "G.InspectorRunsNoMutation");

    private static async Task<(string Local, string Reviewed)> HalfResetAsync(LandingGitFixture f, string? shape = null)
    {
        await f.InitializeAsync();
        var feature = Path.Combine(f.Source, "feature.txt");
        if (shape == "binary") await File.WriteAllBytesAsync(feature, [0, 1, 2, 10]);
        else await File.WriteAllTextAsync(feature, shape == "eol" ? "old\nsecond\n" : "old\n");
        await f.RequiredAsync(f.Source, "add", ".");
        if (shape == "gitlink") await f.RequiredAsync(f.Source, "update-index", "--add", "--cacheinfo", "160000," + f.SeedSha + ",module");
        if (shape == "mode")
        {
            await f.RequiredAsync(f.Source, "update-index", "--chmod=+x", "feature.txt");
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(feature, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.CreateSymbolicLink(Path.Combine(f.Source, "link"), "feature.txt");
                await f.RequiredAsync(f.Source, "add", "link");
            }
        }
        await f.RequiredAsync(f.Source, "commit", "-m", "old tip");
        var local = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        var reviewedTree = Path.Combine(f.Root, "trees", "reviewed");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", reviewedTree, local);
        await File.WriteAllTextAsync(Path.Combine(reviewedTree, "feature.txt"), "reviewed\n");
        if (shape == "added") await File.WriteAllTextAsync(Path.Combine(reviewedTree, "reviewed-addition.txt"), "reviewed addition\n");
        await f.RequiredAsync(reviewedTree, "add", ".");
        await f.RequiredAsync(reviewedTree, "commit", "-m", "reviewed tip");
        var reviewed = (await f.RequiredAsync(reviewedTree, "rev-parse", "HEAD")).Trim();
        await f.RequiredAsync(f.Repository, "update-ref", "--no-deref", f.SourceRef, reviewed, local);
        if (shape is "custom-filter" or "working-tree-encoding" or "ident")
        {
            var attrPath = (await f.RequiredAsync(f.Source, "rev-parse", "--git-path", "info/attributes")).Trim();
            var attr = shape switch { "custom-filter" => "filter=c939", "working-tree-encoding" => "working-tree-encoding=UTF-16", _ => "ident" };
            await File.WriteAllTextAsync(Path.GetFullPath(attrPath, f.Source), "feature.txt " + attr + "\n");
            if (shape == "custom-filter")
            {
                await f.RequiredAsync(f.Source, "config", "filter.c939.clean", "sh -c 'touch " + Path.Combine(f.Root, "filter-invoked") + "; cat' c939-forbidden-filter");
                await f.RequiredAsync(f.Source, "config", "filter.c939.smudge", "sh -c 'touch " + Path.Combine(f.Root, "filter-invoked") + "; cat' c939-forbidden-filter");
            }
        }
        if (shape == "mode") await f.RequiredAsync(f.Source, "config", "core.filemode", "true");
        return (local, reviewed);
    }
}
