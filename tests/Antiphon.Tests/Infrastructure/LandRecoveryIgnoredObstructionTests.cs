using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class LandRecoveryIgnoredObstructionTests
{
    [Test]
    public async Task C970_IgnoredParentFileOfReviewedChildRefused()
    {
        await using var f = new LandingGitFixture();
        var (local, tree) = await PrepareAsync(f);
        await WriteAsync(tree, "gen/a.txt", "reviewed child\n");
        var reviewed = await PublishReviewedAsync(f, local, tree, "ParentFile");
        await IgnoreAsync(f, "/gen");
        await WriteAsync(f.Source, "gen", "owner parent\n");

        await AssertRefusedAsync(f, local, reviewed, "gen", "ParentFile");
        (await File.ReadAllTextAsync(Path.Combine(f.Source, "gen")))
            .ShouldBe("owner parent\n", "G.ParentFile.OwnerBytesPreserved");
    }

    [Test]
    public async Task C970_IgnoredChildOfReviewedFileRefused()
    {
        await using var f = new LandingGitFixture();
        var (local, tree) = await PrepareAsync(f);
        await WriteAsync(tree, "gen", "reviewed file\n");
        var reviewed = await PublishReviewedAsync(f, local, tree, "ChildFile");
        await IgnoreAsync(f, "/gen/");
        await WriteAsync(f.Source, "gen/x.o", "owner child\n");

        await AssertRefusedAsync(f, local, reviewed, "gen/x.o", "ChildFile");
        (await File.ReadAllTextAsync(Path.Combine(f.Source, "gen", "x.o")))
            .ShouldBe("owner child\n", "G.ChildFile.OwnerBytesPreserved");
    }

    [Test]
    public async Task C970_IgnoredSiblingDuringDirectoryToFileReplacementRefused()
    {
        await using var f = new LandingGitFixture();
        var (local, tree) = await PrepareAsync(f, "d/x.txt");
        await f.RequiredAsync(tree, "rm", "d/x.txt");
        await WriteAsync(tree, "d", "reviewed replacement\n");
        var reviewed = await PublishReviewedAsync(f, local, tree, "Replacement");
        await IgnoreAsync(f, "/d/*.o");
        await WriteAsync(f.Source, "d/y.o", "owner sibling\n");

        await AssertRefusedAsync(f, local, reviewed, "d/y.o", "Replacement");
        (await File.ReadAllTextAsync(Path.Combine(f.Source, "d", "y.o")))
            .ShouldBe("owner sibling\n", "G.Replacement.OwnerBytesPreserved");
        (await File.ReadAllTextAsync(Path.Combine(f.Source, "d", "x.txt")))
            .ShouldBe("old tracked bytes\n", "G.Replacement.OldTrackedBytesPreserved");
    }

    [Test]
    public async Task C970_IgnoredRealDirectoryChildOfReviewedSymlinkRefused()
    {
        await using var f = new LandingGitFixture();
        var (local, tree) = await PrepareAsync(f);
        // Create a real mode-120000 Git entry without requiring Windows symlink privileges.
        var targetBlob = Path.Combine(f.Root, "link-target");
        await File.WriteAllTextAsync(targetBlob, "keep.txt");
        var oid = (await f.RequiredAsync(tree, "hash-object", "-w", "--", targetBlob)).Trim();
        await f.RequiredAsync(tree, "update-index", "--add", "--cacheinfo", "120000," + oid + ",link");
        await f.RequiredAsync(tree, "commit", "-m", "reviewed symlink");
        (await f.RequiredAsync(tree, "ls-tree", "HEAD", "--", "link"))
            .ShouldStartWith("120000 blob ", customMessage: "G.SymlinkChild.ReviewedTreeTracksSymlink");
        var reviewed = await MoveRefAsync(f, local, tree, "SymlinkChild");
        await IgnoreAsync(f, "/link/");
        await WriteAsync(f.Source, "link/x", "owner real directory child\n");
        new DirectoryInfo(Path.Combine(f.Source, "link")).LinkTarget
            .ShouldBeNull("G.SymlinkChild.ObstructionIsRealDirectory");

        await AssertRefusedAsync(f, local, reviewed, "link/x", "SymlinkChild");
        (await File.ReadAllTextAsync(Path.Combine(f.Source, "link", "x")))
            .ShouldBe("owner real directory child\n", "G.SymlinkChild.OwnerBytesPreserved");
    }

    [Test]
    public async Task C970_IgnoredDirectorySymlinkParentOfReviewedChildRefused()
    {
        await using var f = new LandingGitFixture();
        var (local, tree) = await PrepareAsync(f);
        await WriteAsync(tree, "gen/a.txt", "reviewed child\n");
        var reviewed = await PublishReviewedAsync(f, local, tree, "SymlinkParent");
        var outside = Path.Combine(f.Root, "outside-checkout");
        await WriteAsync(outside, "sentinel.txt", "outside owner bytes\n");
        await IgnoreAsync(f, "/gen");
        var link = Path.Combine(f.Source, "gen");
        // A Git index entry changes the old index; a junction is enumerated as a directory.
        // Keep the real ignored symlink so the reviewed-parent rule is the only refusal.
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception ex) when (OperatingSystem.IsWindows()
            && ex is IOException or UnauthorizedAccessException)
        {
            Skip.Test("G.SymlinkParent.WindowsDeniedSymlinkCreation: Windows denied symlink creation. "
                + "The ignored directory-symlink obstruction runs on Linux without that privilege.");
            return;
        }
        try
        {
            new DirectoryInfo(link).LinkTarget.ShouldBe(outside, "G.SymlinkParent.ExternalTargetSeeded");
            await AssertRefusedAsync(f, local, reviewed, "gen", "SymlinkParent");
            new DirectoryInfo(link).LinkTarget.ShouldBe(outside, "G.SymlinkParent.LinkPreserved");
            (await File.ReadAllTextAsync(Path.Combine(outside, "sentinel.txt")))
                .ShouldBe("outside owner bytes\n", "G.SymlinkParent.ExternalBytesPreserved");
        }
        finally
        {
            // Remove only the link before the fixture's recursive owned-root disposal.
            Directory.Delete(link);
        }
    }

    [Test]
    [Arguments("GEN")]
    [Arguments("Gen")]
    public async Task C970_WindowsIgnoredCaseAliasParentOfReviewedChildRefused(string alias)
    {
        if (!OperatingSystem.IsWindows())
        {
            Skip.Test("G.WindowsAlias.RequiresWindowsOrdinalIgnoreCase: recovery path aliases are Windows-only.");
            return;
        }

        await using var f = new LandingGitFixture();
        var (local, tree) = await PrepareAsync(f);
        await WriteAsync(tree, "gen/a.txt", "reviewed child\n");
        var reviewed = await PublishReviewedAsync(f, local, tree, "WindowsAlias." + alias);
        await IgnoreAsync(f, "/" + alias);
        await WriteAsync(f.Source, alias, "owner case alias\n");

        await AssertRefusedAsync(f, local, reviewed, alias, "WindowsAlias." + alias);
        (await File.ReadAllTextAsync(Path.Combine(f.Source, alias)))
            .ShouldBe("owner case alias\n", "G.WindowsAlias." + alias + ".OwnerBytesPreserved");
    }

    private static async Task<(string Local, string Tree)> PrepareAsync(
        LandingGitFixture f, string oldTracked = "feature.txt")
    {
        await f.InitializeAsync();
        await WriteAsync(f.Source, oldTracked, "old tracked bytes\n");
        await f.RequiredAsync(f.Source, "add", ".");
        await f.RequiredAsync(f.Source, "commit", "-m", "old checkout");
        var local = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        var tree = Path.Combine(f.Root, "trees", "reviewed");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", tree, local);
        return (local, tree);
    }

    private static async Task<string> PublishReviewedAsync(
        LandingGitFixture f, string local, string tree, string label)
    {
        await f.RequiredAsync(tree, "add", ".");
        await f.RequiredAsync(tree, "commit", "-m", "reviewed obstruction shape");
        return await MoveRefAsync(f, local, tree, label);
    }

    private static async Task<string> MoveRefAsync(
        LandingGitFixture f, string local, string tree, string label)
    {
        var reviewed = (await f.RequiredAsync(tree, "rev-parse", "HEAD")).Trim();
        // Reproduce the half-reset boundary: HEAD at S, index and checkout still at L.
        await f.RequiredAsync(f.Repository, "update-ref", "--no-deref", f.SourceRef, reviewed, local);
        var clean = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        clean.Accepted.ShouldBeTrue("G." + label + ".UnobstructedCheckoutAccepted");
        return reviewed;
    }

    private static async Task IgnoreAsync(LandingGitFixture f, string pattern)
    {
        var exclude = (await f.RequiredAsync(f.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, f.Source), "\n" + pattern + "\n");
    }

    private static async Task WriteAsync(string root, string relative, string contents)
    {
        var path = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, contents);
    }

    private static async Task AssertRefusedAsync(
        LandingGitFixture f, string local, string reviewed, string ignoredPath, string label)
    {
        (await f.RequiredAsync(f.Source, "ls-files", "--others", "--exclude-standard", "-z"))
            .ShouldBeEmpty("G." + label + ".NoUntrackedFallbackRefusal");
        (await f.RequiredAsync(f.Source, "ls-files", "--others", "--ignored", "--exclude-standard", "-z"))
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .ShouldBe(new[] { ignoredPath }, "G." + label + ".IgnoredObstructionEnumerated");
        f.Git.Commands.Clear();

        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);

        proof.Accepted.ShouldBeFalse("G." + label + ".ObstructionRefused");
        proof.Reason.ShouldBe("source_dirty", "G." + label + ".ObstructionIsSourceDirty");
        f.Git.Commands.ShouldNotContain(x => x.Arguments[0] == "reset" || x.Arguments[0] == "clean"
            || x.Arguments[0] == "checkout" || x.Arguments[0] == "checkout-index"
            || x.Arguments[0] == "add" || x.Arguments[0] == "update-index"
            || x.Arguments[0] == "update-ref" || x.Arguments[0] == "push",
            "G." + label + ".InspectionIsReadOnly");
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim()
            .ShouldBe(reviewed, "G." + label + ".ReviewedHeadPreserved");
        (await f.RequiredAsync(f.Source, "write-tree")).Trim()
            .ShouldBe((await f.RequiredAsync(f.Repository, "rev-parse", local + "^{tree}")).Trim(),
                "G." + label + ".OldIndexPreserved");
    }
}
