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
public sealed class AgentTaskLandHoldVisibilityTests
{
    [Test]
    [Arguments("shared", AgentTaskStatus.Dispatched)]
    [Arguments("shared", AgentTaskStatus.Working)]
    [Arguments("shared", AgentTaskStatus.Blocked)]
    [Arguments("source", AgentTaskStatus.Blocked)]
    [Arguments("lease", AgentTaskStatus.Blocked)]
    [Arguments("unrelated", AgentTaskStatus.Blocked)]
    public async Task C467_V03_RealWriterLeaseAndEpisodeMatrix(string writer, AgentTaskStatus status)
    {
        await using var h = new LandingSafetyHarness();
        await h.InitializeAsync();
        await h.AddSourceAsync();
        var ownerId = Guid.NewGuid();
        await using var unrelated = new LandingGitFixture();
        if (writer == "unrelated") await unrelated.InitializeAsync();
        await using (var db = h.CreateContext())
        {
            if (writer != "lease") db.AgentTasks.Add(new AgentTask
            {
                Id = ownerId, RootTaskId = ownerId, Title = "C467 writer", Goal = "owned fixture",
                Status = status, Role = AgentTaskRole.Code, ReplyTo = AgentTaskReplyTo.None,
                Workspace = writer == "source" ? WorkspaceMode.Worktree : WorkspaceMode.Shared,
                WorkingDirectory = writer == "unrelated" ? unrelated.Repository : writer == "source" ? h.Fixture.Source : h.Fixture.Repository,
                RepoPath = writer == "unrelated" ? unrelated.Repository : h.Fixture.Repository,
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var lease = writer == "lease" ? await h.Services.GetRequiredService<IRepositoryMutationLease>()
            .TryAcquireAsync(h.Fixture.Repository, CancellationToken.None) : null;
        try
        {
            h.Fixture.Git.Trace.Clear();
            var result = await h.RunAsync();
            if (writer == "unrelated")
            {
                result.ShouldBe(LandRunResult.Complete);
                await h.Fixture.AssertRemoteSourceAsync();
                return;
            }
            result.ShouldBe(LandRunResult.Held);
            h.Verifier.Calls.ShouldBe(0);
            h.Fixture.Git.Trace.ShouldNotContain(a => a.Contains("rebase") || a.Contains("remove") || a.Contains("--ff-only") || a[0] == "push");
            await using (var observer = h.CreateContext())
            {
                var request = await observer.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
                request.Attempt.ShouldBe(0);
                request.State.ShouldBe(LandRequestState.Held);
                request.HoldEpisode.ShouldBe(1);
                request.HoldingTaskId.ShouldBe(writer == "lease" ? null : ownerId);
                request.HoldingTaskStatus.ShouldBe(writer == "lease" ? null : status);
                request.HoldReasonCode.ShouldBe(writer == "lease" ? "repository_mutation_lease_busy" : "repository_or_source_writer");
                (await observer.AgentTaskLandings.CountAsync(o => o.TaskId == h.Fixture.TaskId)).ShouldBe(0);
                (await observer.AgentTaskLandNotifications.SingleAsync(n => n.RequestId == request.Id)).Kind.ShouldBe(LandNotificationKind.Held);
            }
            await h.RestartServicesAsync();
            await h.RunAsync();
            await using var again = h.CreateContext();
            (await again.AgentTaskLandNotifications.CountAsync(n => n.TaskId == h.Fixture.TaskId)).ShouldBe(1);
        }
        finally { if (lease is not null) await lease.DisposeAsync(); }
        await using (var db = h.CreateContext())
        {
            var owner = await db.AgentTasks.SingleOrDefaultAsync(t => t.Id == ownerId);
            if (owner is not null) { owner.Status = AgentTaskStatus.Succeeded; await db.SaveChangesAsync(); }
        }
        await h.RunAsync();
        await h.Fixture.AssertRemoteSourceAsync();
        await using var released = h.CreateContext();
        var completed = await released.AgentTaskLandRequests.SingleAsync(r => r.TaskId == h.Fixture.TaskId);
        completed.State.ShouldBe(LandRequestState.Completed);
        completed.HoldingTaskId.ShouldBeNull();
        completed.Attempt.ShouldBe(1);
        (await released.AgentTaskEvents.CountAsync(e => e.AgentTaskId == h.Fixture.TaskId && e.Type == AgentTaskEventType.HeldReleased)).ShouldBe(1);
    }

    [Test]
    public async Task C467_V04_HoldAndAgeAreAtomicBeforePublish()
    {
        await using var h = new LandingSafetyHarness();
        var observed = 0;
        h.Events = new ObservingBus(async () =>
        {
            await using var observer = h.CreateContext();
            var request = await observer.AgentTaskLandRequests.AsNoTracking().SingleAsync(r => r.TaskId == h.Fixture.TaskId);
            request.State.ShouldBe(LandRequestState.Held);
            var held = await observer.AgentTaskEvents.SingleAsync(e => e.LandRequestId == request.Id && e.Type == AgentTaskEventType.Held);
            (await observer.AgentTaskLandNotifications.SingleAsync(n => n.SourceEventId == held.Id)).Kind.ShouldBe(LandNotificationKind.Held);
            observed++;
        });
        await h.InitializeAsync();
        await using var lease = await h.Services.GetRequiredService<IRepositoryMutationLease>().TryAcquireAsync(h.Fixture.Repository, CancellationToken.None);
        await h.RunAsync();
        observed.ShouldBe(1);
    }

    private sealed class ObservingBus(Func<Task> observe) : IEventBus
    {
        public Task PublishToAllAsync(string name, object payload, CancellationToken ct = default) => observe();
        public Task PublishToGroupAsync(string group, string name, object payload, CancellationToken ct = default) => observe();
    }
}
