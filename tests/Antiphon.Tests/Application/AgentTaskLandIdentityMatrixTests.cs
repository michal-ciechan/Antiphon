using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandIdentityMatrixTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V01_ServicePreservesDetachedIncidentAtAnAncestorBranch(bool uniqueCommit)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "--detach");
        if (uniqueCommit)
        {
            await File.WriteAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"), "unique detached incident\n");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-am", "unique detached work");
        }
        var head = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
        var bytes = await File.ReadAllBytesAsync(Path.Combine(h.Fixture.Source, "keep.txt"));
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue("unique detached work must survive even though its recorded branch is already remotely contained");
        (await File.ReadAllBytesAsync(Path.Combine(h.Fixture.Source, "keep.txt"))).ShouldBe(bytes);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim().ShouldBe(head);
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        (await h.OperationAsync()).ShouldBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a[0] == "push");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V02_CaseDistinctRecordedRefIsNotSymbolicIdentity()
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "pack-refs", "--all");
        var other = h.Fixture.SourceRef.Replace("feat/", "Feat/", StringComparison.Ordinal);
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "update-ref", other, h.Fixture.SeedSha);
        await h.Fixture.RequiredAsync(h.Fixture.Source, "symbolic-ref", "HEAD", other);
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "symbolic-ref", "HEAD")).Trim().ShouldBe(other);
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue("Git full-ref spelling is exact even on a case-insensitive filesystem");
        (await h.Fixture.RequiredAsync(h.Fixture.Source, "symbolic-ref", "HEAD")).Trim().ShouldBe(other);
        (await h.OperationAsync()).ShouldBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a[0] == "push");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("missing-registration")]
    [Arguments("ambiguous-registration")]
    [Arguments("registration-error")]
    [Arguments("unresolved-head")]
    [Arguments("malformed-git")]
    [Arguments("inaccessible-git")]
    public async Task C448_V03_UnknownFilesystemAndQueryIdentityCannotPublish(string variant)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        using var markerLock = variant == "inaccessible-git"
            ? new FileStream(Path.Combine(h.Fixture.Source, ".git"), FileMode.Open, FileAccess.Read, FileShare.None) : null;
        if (variant == "malformed-git")
        {
            var marker = Path.Combine(h.Fixture.Source, ".git");
            File.SetAttributes(marker, FileAttributes.Normal); // Git marks this fixture-owned file hidden on Windows.
            await File.WriteAllTextAsync(marker, "not a git directory\n");
        }
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args.Contains("worktree") && args.Contains("list"))
            {
                if (variant == "registration-error") return Task.FromResult<LandingGitResult?>(new(128, "", "query error"));
                var main = $"worktree {h.Fixture.Repository}\0HEAD {h.Fixture.SeedSha}\0branch {h.Fixture.TargetRef}\0\0";
                if (variant == "missing-registration") return Task.FromResult<LandingGitResult?>(new(0, main, ""));
                if (variant == "ambiguous-registration")
                {
                    var source = $"worktree {h.Fixture.Source}\0HEAD {h.Fixture.SeedSha}\0branch {h.Fixture.SourceRef}\0\0";
                    return Task.FromResult<LandingGitResult?>(new(0, main + source + source, ""));
                }
            }
            if (variant == "unresolved-head" && args[0] == "rev-parse" && args.Contains("HEAD^{commit}"))
                return Task.FromResult<LandingGitResult?>(new(128, "", "missing commit object"));
            return Task.FromResult<LandingGitResult?>(null);
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        (await h.OperationAsync()).ShouldBeNull("unknown source cannot acquire a publication operation by an ancestry shortcut");
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a[0] == "push" || a.Contains("-d"));
        (await File.ReadAllTextAsync(Path.Combine(h.Fixture.Source, "keep.txt"))).ShouldBe("seed\n");
        h.Fixture.Git.BeforeCommand = null;
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("submodule")]
    [Arguments("nested-repository")]
    [Arguments("ignored-nested-repository")]
    public async Task C448_V04_NestedWorkSurvivesTheActualLandingService(string kind)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var nested = Path.Combine(h.Fixture.Source, kind == "ignored-nested-repository" ? ".antiphon/nested" : "nested");
        if (kind == "submodule")
        {
            await h.Fixture.RequiredAsync(h.Fixture.Source, "-c", "protocol.file.allow=always", "submodule", "add", h.Fixture.Remote, "nested");
            await h.Fixture.RequiredAsync(h.Fixture.Source, "commit", "-am", "submodule fixture");
            var sha = (await h.Fixture.RequiredAsync(h.Fixture.Source, "rev-parse", "HEAD")).Trim();
            await h.Fixture.RequiredAsync(h.Fixture.Source, "push", "origin", sha + ":" + h.Fixture.TargetRef);
        }
        else
        {
            Directory.CreateDirectory(nested);
            await h.Fixture.RequiredAsync(nested, "init", "-b", "private");
            await File.WriteAllTextAsync(Path.Combine(nested, "keep.txt"), "nested committed bytes\n");
            await h.Fixture.RequiredAsync(nested, "add", ".");
            await h.Fixture.RequiredAsync(nested, "commit", "-m", "private history");
        }
        var file = Path.Combine(nested, "keep.txt");
        await File.WriteAllTextAsync(file, "uncommitted nested work\n");
        var head = (await h.Fixture.RequiredAsync(nested, "rev-parse", "HEAD")).Trim();
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove") || a.Contains("rebase") || a[0] == "push");
        (await File.ReadAllTextAsync(file)).ShouldBe("uncommitted nested work\n");
        (await h.Fixture.RequiredAsync(nested, "rev-parse", "HEAD")).Trim().ShouldBe(head);
        if (kind == "ignored-nested-repository") (await h.OperationAsync())!.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        else (await h.OperationAsync()).ShouldBeNull();
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    public async Task C448_V30_RealSpacesAndUnicodePathsRemainUsable()
    {
        var root = Path.Combine(Path.GetTempPath(), "antiphon-c448-" + Guid.NewGuid().ToString("N") + " space ü");
        await using var h = new LandingSafetyHarness(root);
        await h.InitializeAsync();
        await h.RunAsync();
        var op = (await h.OperationAsync())!;
        op.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
