using Antiphon.Server.Application.Dtos;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<GitProcessSpawnLimit>]
public sealed class LandRecoveryIgnoredNestedRepositoryTests
{
    [Test]
    [Arguments("ignored", true)]
    [Arguments("nonignored", false)]
    [Arguments("gitlink", false)]
    [Arguments("git-file", false)]
    [Arguments("symlink", false)]
    [Arguments("dirty", false)]
    [Arguments("lookalike", false)]
    public async Task C971_NestedRepositoryMatrix(string variant, bool expected)
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var nested = Path.Combine(f.Source, "nested");
        var outside = Path.Combine(f.Root, "nested-outside");
        if (variant == "symlink")
        {
            await SeedNestedAsync(f, outside);
            Directory.CreateSymbolicLink(nested, outside);
        }
        else if (variant == "lookalike")
        {
            Directory.CreateDirectory(Path.Combine(nested, ".git"));
            await File.WriteAllTextAsync(Path.Combine(nested, "sentinel.txt"), "private bytes\n");
        }
        else
        {
            Directory.CreateDirectory(nested);
            if (variant == "git-file")
                await f.RequiredAsync(nested, "init", "--separate-git-dir", outside);
            await SeedNestedAsync(f, nested);
            if (variant == "dirty")
                await File.WriteAllTextAsync(Path.Combine(nested, "sentinel.txt"), "dirty private bytes\n");
            if (variant == "gitlink")
            {
                var nestedSha = (await f.RequiredAsync(nested, "rev-parse", "HEAD")).Trim();
                await f.RequiredAsync(f.Source, "update-index", "--add", "--cacheinfo", "160000", nestedSha, "nested");
                await f.RequiredAsync(f.Source, "commit", "-m", "tracked gitlink");
            }
        }
        try
        {
            if (variant is "ignored" or "git-file") await IgnoreAsync(f);
            var (local, reviewed) = await HalfResetAsync(f);
            if (variant == "ignored")
                (await f.RequiredAsync(f.Source, "ls-files", "--others", "--ignored", "--exclude-standard", "-z"))
                    .ShouldBe("nested/\0", "N.Ignored.DirectoryRecordReproduced");
            await AssertProofAsync(f, local, reviewed, nested, expected, variant);
        }
        finally
        {
            if (variant == "symlink") Directory.Delete(nested);
        }
    }

    [Test]
    [Arguments("exact")]
    [Arguments("parent")]
    [Arguments("child")]
    public async Task C971_IgnoredNestedRepositoryCollision(string variant)
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var relative = variant == "child" ? "nested/repo" : "nested";
        var nested = Path.Combine(f.Source, relative.Replace('/', Path.DirectorySeparatorChar));
        await SeedNestedAsync(f, nested);
        await IgnoreAsync(f);
        var reviewedPath = variant == "parent" ? "nested/reviewed.txt" : "nested";
        var (local, reviewed) = await HalfResetAsync(f, reviewedPath);
        await AssertProofAsync(f, local, reviewed, nested, false, "collision." + variant);
    }

    [Test]
    [Arguments("ignore-error")]
    [Arguments("not-ignored")]
    [Arguments("tracked-error")]
    [Arguments("tracked-output")]
    [Arguments("git-symlink")]
    public async Task C971_IgnoredNestedRepositoryEvidenceRefused(string variant)
    {
        await using var f = new LandingGitFixture();
        await f.InitializeAsync();
        var nested = Path.Combine(f.Source, "nested");
        await SeedNestedAsync(f, nested);
        await IgnoreAsync(f);
        var (local, reviewed) = await HalfResetAsync(f);
        if (variant == "git-symlink")
        {
            var admin = Path.Combine(nested, ".git");
            var outside = Path.Combine(f.Root, "nested-admin");
            Directory.Move(admin, outside);
            Directory.CreateSymbolicLink(admin, outside);
        }
        f.Git.BeforeCommand = (directory, args) =>
        {
            LandingGitResult? result = null;
            if (directory == f.Source && args[0] == "check-ignore")
            {
                if (variant == "ignore-error") result = new(128, "", "fixture_error");
                if (variant == "not-ignored") result = new(1, "", "");
            }
            if (directory == f.Source && args[0] == "ls-files" && args.Contains("--cached"))
            {
                if (variant == "tracked-error") result = new(128, "", "fixture_error");
                if (variant == "tracked-output") result = new(0, "nested/tracked.txt\0", "");
            }
            return Task.FromResult(result);
        };
        try
        {
            await AssertProofAsync(f, local, reviewed, nested, false, variant);
        }
        finally
        {
            if (variant == "git-symlink") Directory.Delete(Path.Combine(nested, ".git"));
        }
    }

    private static async Task SeedNestedAsync(LandingGitFixture f, string nested)
    {
        Directory.CreateDirectory(nested);
        await f.RequiredAsync(nested, "init");
        await File.WriteAllTextAsync(Path.Combine(nested, "sentinel.txt"), "private bytes\n");
        await f.RequiredAsync(nested, "add", "sentinel.txt");
        await f.RequiredAsync(nested, "commit", "-m", "nested private content");
    }

    private static async Task IgnoreAsync(LandingGitFixture f)
    {
        var exclude = (await f.RequiredAsync(f.Source, "rev-parse", "--git-path", "info/exclude")).Trim();
        await File.AppendAllTextAsync(Path.GetFullPath(exclude, f.Source), "\n/nested/\n");
    }

    private static async Task<(string Local, string Reviewed)> HalfResetAsync(
        LandingGitFixture f, string reviewedPath = "keep.txt")
    {
        var local = (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim();
        var tree = Path.Combine(f.Root, "trees", "reviewed");
        await f.RequiredAsync(f.Repository, "worktree", "add", "--detach", tree, local);
        var file = Path.Combine(tree, reviewedPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "reviewed bytes\n");
        // Linked worktrees share info/exclude; deliberately seed the reviewed obstruction anyway.
        await f.RequiredAsync(tree, "add", "--force", "--", reviewedPath);
        await f.RequiredAsync(tree, "commit", "-m", "reviewed descendant");
        var reviewed = (await f.RequiredAsync(tree, "rev-parse", "HEAD")).Trim();
        await f.RequiredAsync(f.Repository, "update-ref", "--no-deref", f.SourceRef, reviewed, local);
        return (local, reviewed);
    }

    private static async Task AssertProofAsync(LandingGitFixture f, string local, string reviewed,
        string nested, bool expected, string label)
    {
        var sentinel = Path.Combine(nested, "sentinel.txt");
        var bytes = await File.ReadAllBytesAsync(sentinel);
        var index = (await f.RequiredAsync(f.Source, "write-tree")).Trim();
        f.Git.Commands.Clear();
        var proof = await f.Git.InspectRecoveryCheckoutAsync(f.Coordinates, local, reviewed, CancellationToken.None);
        proof.Accepted.ShouldBe(expected, "N." + label + ".ExpectedProof");
        if (!expected) proof.Reason.ShouldNotBeNull("N." + label + ".RefusalReason");
        f.Git.Commands.ShouldNotContain(x => new[] { "reset", "clean", "checkout", "checkout-index",
            "add", "update-index", "update-ref", "push" }.Contains(x.Arguments[0]), "N.InspectionReadOnly");
        (await File.ReadAllBytesAsync(sentinel)).ShouldBe(bytes, "N.NestedBytesPreserved");
        (await f.RequiredAsync(f.Source, "write-tree")).Trim().ShouldBe(index, "N.OuterIndexPreserved");
        (await f.RequiredAsync(f.Source, "rev-parse", "HEAD")).Trim().ShouldBe(reviewed, "N.OuterHeadPreserved");
    }
}
