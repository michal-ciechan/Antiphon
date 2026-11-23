using Antiphon.Server.Application.Settings;
using Antiphon.Server.Infrastructure.Git;
using Antiphon.Tests.TestHelpers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Infrastructure;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class WorktreeRemovalAuthorityTests
{
    [Test]
    public async Task C448_V35_BuildJunkScriptPreservesOpaqueWildcardMatches()
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var path = Path.Combine(fixture.Source, "bin-private", "nested", "bin-archive", "report.md");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "irreplaceable");
        Directory.SetLastWriteTime(Path.Combine(fixture.Source, "bin-private"), DateTime.Now.AddDays(-10));
        // The cleanup-build-junk command in process (Antiphon.Nightly.BuildJunkCleanup, unit 21 of the 2026-11-22 plan), as the
        // CLI and so the scheduled launcher run it: no producer creation records.
        var output = new StringWriter();
        var removed = Antiphon.Nightly.BuildJunkCleanup.Run(
            Antiphon.Nightly.BuildJunkCleanupOptions.Parse(["--repo-root", fixture.Source, "--skip-if-modified-within-minutes", "0"], null),
            Antiphon.Nightly.NightlyHost.Production(output, output), new System.IO.Abstractions.FileSystem(), removeOwned: null);
        removed.ShouldBe(0);
        File.Exists(path).ShouldBeTrue("build cleanup must preserve opaque wildcard-matched bytes");
        output.ToString().ShouldContain("0 dir(s) removed");
        (await File.ReadAllTextAsync(path)).ShouldBe("irreplaceable");
        await fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("clean")]
    [Arguments("branch-moved")]
    [Arguments("remote-rewritten")]
    public async Task C688_PublicationCleanupDeletesBranchAtLocalSha(string change)
    {
        // CARD-0688 V-17 (D-6): the branch never moved, so cleanup deletes it at SourceLocalSha after
        // proving the remote contains the landed VerifiedSourceSha.
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var local = await h.AddSourceAsync();
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "commit", "--allow-empty", "-m", "new base");
        await h.Fixture.RequiredAsync(h.Fixture.Repository, "push", "origin", h.Fixture.TargetRef);
        var outsider = new LandingGitFixture.FixtureGit(Path.Combine(h.Fixture.Root, "home"), h.Fixture.TaskId);
        string? third = null;
        h.Fault.AfterAcknowledged = async phase =>
        {
            if (phase != Antiphon.Server.Domain.Enums.LandPhase.CleanupStarted || third is not null || change == "clean") return;
            if (change == "branch-moved")
            {
                // Same tree, new commit: the checkout stays clean, only the branch identity changes.
                third = (await outsider.RunAsync(h.Fixture.Repository, ["commit-tree", local + "^{tree}", "-p", local, "-m", "third"],
                    CancellationToken.None)).Output.Trim();
                (await outsider.RunAsync(h.Fixture.Repository, ["update-ref", h.Fixture.SourceRef, third, local], CancellationToken.None))
                    .Succeeded.ShouldBeTrue();
            }
            else
            {
                third = "rewritten";
                (await outsider.RunAsync(h.Fixture.Remote, ["update-ref", h.Fixture.TargetRef, h.Fixture.SeedSha], CancellationToken.None))
                    .Succeeded.ShouldBeTrue();
            }
        };

        await h.RunAsync();

        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.SourceLocalSha.ShouldBe(local);
        op.VerifiedSourceSha.ShouldNotBe(local);
        op.ExpectedDeletionSha.ShouldBe(local);
        var branch = await outsider.RunAsync(h.Fixture.Repository, ["show-ref", "--verify", "--hash", h.Fixture.SourceRef], CancellationToken.None);
        switch (change)
        {
            case "clean":
                op.Cleanup.ShouldBe(Antiphon.Server.Domain.Enums.LandCleanupStatus.Complete);
                Directory.Exists(h.Fixture.Source).ShouldBeFalse();
                branch.Succeeded.ShouldBeFalse("the branch is deleted at the SHA it actually has");
                break;
            case "branch-moved":
                op.Cleanup.ShouldBe(Antiphon.Server.Domain.Enums.LandCleanupStatus.Refused);
                op.LastReason.ShouldBe("source_changed");
                branch.Output.Trim().ShouldBe(third);
                Directory.Exists(h.Fixture.Source).ShouldBeTrue();
                break;
            default:
                op.Cleanup.ShouldBe(Antiphon.Server.Domain.Enums.LandCleanupStatus.Refused);
                op.LastReason.ShouldBe("remote_no_longer_contains_source");
                branch.Output.Trim().ShouldBe(local);
                Directory.Exists(h.Fixture.Source).ShouldBeTrue();
                break;
        }
    }

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.local.json")]
    [Arguments("bin-private/irreplaceable.txt")]
    [Arguments("untracked.txt")]
    [Arguments("keep.txt")]
    public async Task C448_V24_LegacyRemovalCannotEraseTaskContents(string relative)
    {
        await using var fixture = new LandingGitFixture();
        await fixture.InitializeAsync();
        var file = Path.Combine(fixture.Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        await File.WriteAllTextAsync(file, "opaque user work\n");
        var manager = new WorktreeManager(Options.Create(new GitSettings
        {
            WorktreeBasePath = Path.Combine(fixture.Root, "trees"),
        }), TimeProvider.System, NullLogger<WorktreeManager>.Instance);

        var result = await manager.TryRemoveAsync(fixture.Repository, fixture.Source, "master", CancellationToken.None);

        File.Exists(file).ShouldBeTrue("removal without typed task authority must preserve the sentinel");
        (await File.ReadAllTextAsync(file)).ShouldBe("opaque user work\n");
        result.IsClean.ShouldBeFalse();
        (await fixture.RequiredAsync(fixture.Repository, "rev-parse", fixture.SourceRef)).Trim().ShouldBe(fixture.SeedSha);
        (await fixture.RequiredAsync(fixture.Repository, "worktree", "list", "--porcelain")).ShouldContain(fixture.Source.Replace('\\', '/'));
        await fixture.AssertRemoteSourceAsync();
    }
}
