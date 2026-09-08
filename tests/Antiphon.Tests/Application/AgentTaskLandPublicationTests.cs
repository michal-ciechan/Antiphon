using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandPublicationTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V22_ConfirmedPublicationUsesSavedReceipt(bool alreadyPresent)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var sha = alreadyPresent ? h.Fixture.SeedSha : await h.AddSourceAsync();
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        new AgentTaskLandingState().HasPublication(op).ShouldBeTrue();
        op.Publication.ShouldBe(alreadyPresent ? LandPublicationOutcome.AlreadyPresent : LandPublicationOutcome.Landed);
        op.Cleanup.ShouldBe(LandCleanupStatus.Complete);
        op.VerifiedSourceSha.ShouldBe(sha);
        op.ObservedRemoteTargetSha.ShouldBe((await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim());
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", op.RecoveryRefPrefix + "/source")).Trim().ShouldBe(sha);
        Directory.Exists(h.Fixture.Source).ShouldBeFalse();
        h.Fixture.Git.Trace.Count(a => a[0] == "push").ShouldBe(alreadyPresent ? 0 : 1);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("detached")]
    [Arguments("switched")]
    [Arguments("dirty")]
    [Arguments("untracked")]
    public async Task C448_V01_ActualServiceRefusesInvalidSourceBeforeShortcut(string change)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        if (change == "detached") await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "--detach");
        if (change == "switched") await h.Fixture.RequiredAsync(h.Fixture.Source, "checkout", "-b", "other");
        var sentinel = Path.Combine(h.Fixture.Source, change == "untracked" ? "new.txt" : "keep.txt");
        if (change is "dirty" or "untracked") await File.WriteAllTextAsync(sentinel, "changed\n");
        var bytes = await File.ReadAllBytesAsync(sentinel);
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        (await File.ReadAllBytesAsync(sentinel)).ShouldBe(bytes);
        h.Fixture.Git.Trace.ShouldNotContain(a => (a[0] == "push" || a[0] == "rebase") || a.Contains("remove"));
        await using var db = h.CreateContext();
        (await db.AgentTaskEvents.SingleAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.LandRefused))
            .Detail.ShouldContain(change switch { "detached" => "detached_head", "switched" => "source_branch_mismatch", _ => "source_dirty" });
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(".antiphon/report.md")]
    [Arguments(".claude/settings.local.json")]
    [Arguments("bin-private/data.txt")]
    public async Task C448_V04_IgnoredFilesSurviveConfirmedPublication(string relative)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var path = Path.Combine(h.Fixture.Source, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "irreplaceable\n");
        await h.RunAsync();
        File.Exists(path).ShouldBeTrue("explicit ignored-content guard must run before real git worktree remove");
        (await File.ReadAllTextAsync(path)).ShouldBe("irreplaceable\n");
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.Publication.ShouldBe(LandPublicationOutcome.AlreadyPresent);
        op.Cleanup.ShouldBe(LandCleanupStatus.Refused);
        op.LastReason.ShouldBe("ignored_content_preserved");
        (await h.Fixture.RequiredAsync(h.Fixture.Repository, "rev-parse", h.Fixture.SourceRef)).Trim().ShouldBe(h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task C448_V09_PushExitCannotReplaceRemoteConfirmation(bool pushAccepted)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        var source = await h.AddSourceAsync();
        var pushed = false;
        h.Fixture.Git.BeforeCommand = (_, args) =>
        {
            if (args[0] == "push")
            {
                pushed = true;
                if (!pushAccepted) return Task.FromResult<LandingGitResult?>(new(1, "", "fixture rejection"));
            }
            return Task.FromResult<LandingGitResult?>(pushed && args[0] == "ls-remote" ? new(128, "", "fixture read failure") : null);
        };
        await h.RunAsync();
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.LocalTargetAfterSha.ShouldBe(source);
        op.RemoteConfirmedAt.ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("remove"));
        h.Fixture.Git.BeforeCommand = null;
        (await h.Fixture.RequiredAsync(h.Fixture.Remote, "rev-parse", h.Fixture.TargetRef)).Trim()
            .ShouldBe(pushAccepted ? source : h.Fixture.SeedSha);
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
