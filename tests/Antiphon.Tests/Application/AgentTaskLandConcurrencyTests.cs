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
[ParallelLimiter<ProcessSpawnLimit>]
public sealed class AgentTaskLandConcurrencyTests
{
    [Test]
    [Arguments("shared")]
    [Arguments("follow-up")]
    [Arguments("lease")]
    public async Task C448_V14_AlreadyPresentHonoursWriterAndLeaseHolds(string holder)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
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
        (await h.OperationAsync()).ShouldBeNull();
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        await using var check = h.CreateContext();
        var task = await check.AgentTasks.SingleAsync(t => t.Id == h.Fixture.TaskId);
        task.LandAttempt.ShouldBe(0);
        task.LandRequestedAt.ShouldNotBeNull();
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("rebase") || a.Contains("remove"));
    }

    [Test]
    [Arguments("source")]
    [Arguments("source-dirty")]
    [Arguments("target")]
    [Arguments("target-dirty")]
    public async Task C448_V10_VerificationDoesNotAuthorizeChangedSourceOrTarget(string change)
    {
        await using var h = new LandingSafetyHarness();
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
            if (change.EndsWith("dirty", StringComparison.Ordinal))
                await File.WriteAllTextAsync(Path.Combine(path, "keep.txt"), "concurrent writer\n");
            else await h.Fixture.RequiredAsync(path, "commit", "--allow-empty", "-m", "concurrent writer");
        };
        h.Fixture.Git.Trace.Clear();
        await h.RunAsync();
        h.Verifier.Calls.ShouldBe(1);
        var op = (await h.OperationAsync()).ShouldNotBeNull();
        op.RemoteConfirmedAt.ShouldBeNull();
        op.LastReason.ShouldBe(change switch
        {
            "source" => "source_changed", "source-dirty" => "source_dirty",
            "target" => "target_changed", _ => "target_dirty_or_unknown",
        });
        Directory.Exists(h.Fixture.Source).ShouldBeTrue();
        h.Fixture.Git.Trace.ShouldNotContain(a => a[0] == "push" || a.Contains("remove"));
        await h.Fixture.AssertRemoteSourceAsync();
    }
}
