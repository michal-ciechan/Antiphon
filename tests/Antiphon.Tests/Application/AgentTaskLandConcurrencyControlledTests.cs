using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public sealed class AgentTaskLandConcurrencyControlledTests
{
    [Test]
    [Arguments("shared", "AlreadyPresent")]
    [Arguments("follow-up", "AlreadyPresent")]
    [Arguments("lease", "AlreadyPresent")]
    [Arguments("shared", "Fresh")]
    [Arguments("follow-up", "Fresh")]
    [Arguments("lease", "Fresh")]
    [Arguments("shared", "ResumePublication")]
    [Arguments("follow-up", "ResumePublication")]
    [Arguments("lease", "ResumePublication")]
    [Arguments("shared", "CleanupRetry")]
    [Arguments("follow-up", "CleanupRetry")]
    [Arguments("lease", "CleanupRetry")]
    public async Task C448_V14_EveryModeHonoursWriterAndLeaseHolds(string holder, string mode)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        if (mode != "AlreadyPresent") await h.AddSourceAsync();
        if (mode == "ResumePublication")
        {
            h.Fault.Phase = LandPhase.LocalTargetAdvanced;
            h.Fault.AfterCommit = true;
            await Should.ThrowAsync<LandingProtocolHarness.InjectedSaveFailure>(() => h.RunAsync());
        }
        if (mode == "CleanupRetry")
        {
            var sentinel = Path.Combine(h.Fixture.Source, ".antiphon", "report.md");
            Directory.CreateDirectory(Path.GetDirectoryName(sentinel)!);
            await File.WriteAllTextAsync(sentinel, "preserve");
            await h.RunAsync();
            await h.RepostAsync();
        }
        var operationBefore = await h.OperationAsync();
        int attemptBefore;
        await using (var observer = h.CreateContext())
            attemptBefore = await observer.AgentTasks.Where(t => t.Id == h.Fixture.TaskId).Select(t => t.LandAttempt).SingleAsync();
        await using var lease = holder == "lease" ? await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Source, CancellationToken.None) : null;
        if (holder != "lease")
        {
            await using var db = h.CreateContext();
            var id = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = id, RootTaskId = id, Title = "writer", Goal = "write", Role = AgentTaskRole.Code,
                Workspace = holder == "shared" ? WorkspaceMode.Shared : WorkspaceMode.Worktree,
                Status = AgentTaskStatus.Working, RepoPath = h.Fixture.Repository,
                WorkingDirectory = h.Fixture.Repository, WorktreePath = holder == "follow-up" ? h.Fixture.Source : null,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        h.Fixture.Git.Trace.Clear();
        (await h.RunAsync()).ShouldBe(LandRunResult.Held);
        ((await h.OperationAsync())?.Id).ShouldBe(operationBefore?.Id);
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await using var check = h.CreateContext();
        var task = await check.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        task.LandAttempt.ShouldBe(attemptBefore);
        task.LandRequestedAt.ShouldNotBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase") || a.Contains("remove"));
        await h.Fixture.AssertRemoteSourceAsync();
        if (lease is not null) await lease.DisposeAsync();
        else
        {
            await using var releaseClaim = h.CreateContext();
            await releaseClaim.AgentTasks.Where(t => t.Id != h.Fixture.TaskId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.Status, AgentTaskStatus.Canceled));
        }
        if (mode == "CleanupRetry") File.Delete(Path.Combine(h.Fixture.Source, ".antiphon", "report.md"));
        await h.RestartServicesAsync();
        (await h.RunAsync()).ShouldBe(LandRunResult.Complete);
        var after = (await h.OperationAsync()).ShouldNotBeNull();
        after.Cleanup.ShouldBe(LandCleanupStatus.Complete, "release must permit fresh progress in every mode");
        await check.Entry(task).ReloadAsync();
        task.LandAttempt.ShouldBe(attemptBefore + 1, "every admitted mode spends exactly one attempt after a hold");
        await h.Fixture.AssertRemoteSourceAsync();
    }

    [Test]
    [Arguments("source")]
    [Arguments("source-dirty")]
    [Arguments("source-switch")]
    [Arguments("source-staged")]
    [Arguments("source-untracked")]
    [Arguments("source-registration")]
    [Arguments("target")]
    [Arguments("target-dirty")]
    [Arguments("target-staged")]
    [Arguments("target-switch")]
    public async Task C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(string change)
    {
        await using var h = new LandingProtocolHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        await using (var db = h.CreateContext())
        {
            var task = await db.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
            task.LandVerifyFilter = "/*/*/Fixture/*";
            await db.SaveChangesAsync();
        }
        h.Verifier.Barrier = async () =>
        {
            var path = change.StartsWith("source", StringComparison.Ordinal) ? h.Fixture.Source : h.Fixture.Repository;
            if (change.EndsWith("switch", StringComparison.Ordinal))
                await h.Fixture.RequiredAsync(path, "checkout", "-b", "same-sha-other-branch");
            else if (change == "source-registration")
                await h.Fixture.RequiredAsync(h.Fixture.Repository, "worktree", "lock", h.Fixture.Source);
            else if (change == "source-untracked")
                await File.WriteAllTextAsync(Path.Combine(path, "new-writer.txt"), "untracked writer\n");
            else if (change.EndsWith("staged", StringComparison.Ordinal))
            {
                await File.WriteAllTextAsync(Path.Combine(path, "keep.txt"), "staged writer\n");
                await h.Fixture.RequiredAsync(path, "add", "keep.txt");
            }
            else if (change.EndsWith("dirty", StringComparison.Ordinal))
                await File.WriteAllTextAsync(Path.Combine(path, "keep.txt"), "concurrent writer\n");
            else await h.Fixture.RequiredAsync(path, "commit", "--allow-empty", "-m", "concurrent writer");
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        h.Verifier.Calls.ShouldBe(1);
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldBeNull();
        op.Phase.ShouldBe(LandPhase.Refused, "invalid post-verification evidence must be rejected before committing Verified");
        op.LastReason.ShouldBe(change switch
        {
            "source" => "source_changed", "source-dirty" or "source-staged" or "source-untracked" => "source_dirty",
            "source-registration" => "registration_unavailable",
            "source-switch" => "source_branch_mismatch", "target-switch" => "target_checkout_changed",
            "target" => "target_changed", _ => "target_dirty_or_unknown",
        });
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
