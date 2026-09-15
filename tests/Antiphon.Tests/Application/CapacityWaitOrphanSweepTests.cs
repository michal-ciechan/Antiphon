using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class CapacityWaitOrphanSweepTests
{
    [Test]
    [Arguments(SessionStatus.Stopped, false)]
    [Arguments(SessionStatus.Failed, false)]
    [Arguments(SessionStatus.Stopped, true)]
    public async Task Session_ended_wait_is_cancelled_and_its_grant_reissued(SessionStatus status, bool missing)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        var now = time.GetUtcNow().UtcDateTime;
        var endedSession = Guid.NewGuid();
        var liveSession = Guid.NewGuid();
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            if (!missing)
                db.AgentSessions.Add(Session(endedSession, status, now));
            db.AgentSessions.Add(Session(liveSession, SessionStatus.Running, now));
            await db.SaveChangesAsync();
        }
        var first = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{endedSession:N}", CapacityWaitConsumerKind.LiveSession, sessionId: endedSession,
            holdAlreadyCleared: true, blockedAt: now.AddHours(-2)), CancellationToken.None);
        var second = await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            $"session:{liveSession:N}", CapacityWaitConsumerKind.LiveSession, sessionId: liveSession,
            holdAlreadyCleared: true, blockedAt: now.AddHours(-1)), CancellationToken.None);
        (await service.GrantReadyAsync(CancellationToken.None)).ShouldBe(1);
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            (await db.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(first.Id);

        await service.ReconcileAsync(CancellationToken.None);

        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var canceled = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == first.Id);
        canceled.State.ShouldBe(CapacityRecoveryWaitState.Canceled);
        canceled.Outcome.ShouldBe("Canceled");
        canceled.OutcomeReason.ShouldBe(missing ? "owner-missing" : "session-ended");
        canceled.Version.ShouldBe(first.Version + 2); // grant, then cancel
        var live = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == second.Id);
        live.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
        live.Version.ShouldBe(second.Version + 1); // its first grant
        live.AdmissionCount.ShouldBe(0);
        live.SessionId.ShouldBe(liveSession);
        (await verify.CapacityRecoveryProviderStates.SingleAsync(s => s.Kind == AgentKind.ClaudeCode)).GrantedWaitId.ShouldBe(second.Id);
        (await verify.AgentIncidents.CountAsync()).ShouldBe(0);
    }

    [Test]
    public async Task Terminal_task_wait_is_cancelled()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        var waits = new List<CapacityRecoveryWait>();
        foreach (var status in new AgentTaskStatus?[] { AgentTaskStatus.Succeeded, AgentTaskStatus.Failed, AgentTaskStatus.Canceled, null })
        {
            var id = Guid.NewGuid();
            if (status is { } terminal)
            {
                await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
                db.AgentTasks.Add(TaskOwner(id, terminal, time.GetUtcNow().UtcDateTime));
                await db.SaveChangesAsync();
            }
            waits.Add(await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"task:{id:N}", CapacityWaitConsumerKind.QueuedTask,
                taskId: status is null ? null : id), CancellationToken.None));
        }
        (await service.CancelOrphanedWaitsAsync(CancellationToken.None)).ShouldBe(4);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var ids = waits.Select(w => w.Id).ToArray();
        var canceled = await verify.CapacityRecoveryWaits.Where(w => ids.Contains(w.Id)).ToListAsync();
        canceled.Count.ShouldBe(4);
        canceled.ShouldAllBe(w => w.State == CapacityRecoveryWaitState.Canceled && w.Outcome == "Canceled" && w.Version == 2);
        canceled.Count(w => w.OutcomeReason == "task-terminal").ShouldBe(3);
        canceled.Count(w => w.OutcomeReason == "owner-missing").ShouldBe(1);
        (await service.CancelOrphanedWaitsAsync(CancellationToken.None)).ShouldBe(0);
    }

    [Test]
    public async Task Live_owners_are_kept()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema);
        await using var services = provider;
        var now = time.GetUtcNow().UtcDateTime;
        var waits = new List<CapacityRecoveryWait>();
        foreach (var status in new[] { SessionStatus.Created, SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping })
        {
            var id = Guid.NewGuid();
            await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
            db.AgentSessions.Add(Session(id, status, now));
            await db.SaveChangesAsync();
            waits.Add(await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"session:{id:N}", CapacityWaitConsumerKind.LiveSession, sessionId: id), CancellationToken.None));
        }
        foreach (var status in new[] { AgentTaskStatus.Queued, AgentTaskStatus.Dispatched, AgentTaskStatus.Working, AgentTaskStatus.Blocked })
        {
            var id = Guid.NewGuid();
            await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
            var task = TaskOwner(id, status, now);
            task.CapacityWaitRetained = status == AgentTaskStatus.Working;
            db.AgentTasks.Add(task);
            await db.SaveChangesAsync();
            waits.Add(await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"task:{id:N}", status == AgentTaskStatus.Blocked ? CapacityWaitConsumerKind.RoutingBlockedTask : CapacityWaitConsumerKind.QueuedTask,
                taskId: id), CancellationToken.None));
        }
        var stoppedId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            db.AgentSessions.Add(Session(stoppedId, SessionStatus.Stopped, now));
            db.Agents.Add(new Agent
            {
                Id = agentId, Name = $"standing-{agentId:N}", Slug = $"standing-{agentId:N}",
                Kind = AgentKind.ClaudeCode, AlwaysOn = true, PersistentSessionId = stoppedId.ToString("D"),
                CreatedAt = now, UpdatedAt = now,
            });
            await db.SaveChangesAsync();
        }
        foreach (var kind in new[] { CapacityWaitConsumerKind.StandingStart, CapacityWaitConsumerKind.PendingQueue, CapacityWaitConsumerKind.CreateRefusal })
            waits.Add(await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                kind == CapacityWaitConsumerKind.StandingStart ? $"agent:{agentId:N}" : $"control:{Guid.NewGuid():N}",
                kind, agentId: agentId, sessionId: stoppedId, taskId: Guid.NewGuid()), CancellationToken.None));
        waits.Add(await service.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
            CapacityRecoveryService.CompatibilityCursorKey, CapacityWaitConsumerKind.LiveSession), CancellationToken.None));

        (await service.CancelOrphanedWaitsAsync(CancellationToken.None)).ShouldBe(0);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        foreach (var original in waits)
        {
            var current = await verify.CapacityRecoveryWaits.SingleAsync(w => w.Id == original.Id);
            current.State.ShouldBe(original.State);
            current.Version.ShouldBe(original.Version);
        }
    }

    [Test]
    public async Task Sweep_is_batch_bounded_and_idempotent()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var (service, time, provider) = CapacityRecoveryTestSupport.CreateService(schema, batch: 100);
        await using var services = provider;
        await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
        {
            for (var i = 0; i < 150; i++)
            {
                var id = Guid.NewGuid();
                db.CapacityRecoveryWaits.Add(new CapacityRecoveryWait
                {
                    Id = id, ConsumerKey = $"task:{Guid.NewGuid():N}", ConsumerKind = CapacityWaitConsumerKind.QueuedTask,
                    ExecutionKind = AgentKind.ClaudeCode, State = CapacityRecoveryWaitState.WaitingForHold,
                    BlockedAt = time.GetUtcNow().UtcDateTime.AddMinutes(-1), UpdatedAt = time.GetUtcNow().UtcDateTime,
                    ActionKey = CapacityRecoveryPolicy.ActionKey(id, 0), Version = 1,
                });
            }
            await db.SaveChangesAsync();
        }
        (await service.CancelOrphanedWaitsAsync(CancellationToken.None)).ShouldBe(100);
        (await service.CancelOrphanedWaitsAsync(CancellationToken.None)).ShouldBe(50);
        (await service.CancelOrphanedWaitsAsync(CancellationToken.None)).ShouldBe(0);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var rows = await verify.CapacityRecoveryWaits.ToListAsync();
        rows.Count.ShouldBe(150);
        rows.ShouldAllBe(w => w.State == CapacityRecoveryWaitState.Canceled && w.Version == 2);
    }

    private static AgentSession Session(Guid id, SessionStatus status, DateTime now) => new()
    {
        Id = id, DefinitionName = "fake", AgentKind = AgentKind.ClaudeCode, Status = status,
        CreatedAt = now, StartedAt = now, LastSeenAt = now, Cwd = Path.GetTempPath(),
    };

    private static AgentTask TaskOwner(Guid id, AgentTaskStatus status, DateTime now) => new()
    {
        Id = id, RootTaskId = id, Title = "capacity owner", Goal = "test", AgentKind = AgentKind.ClaudeCode,
        Status = status, CreatedAt = now, ConcurrencyToken = Guid.NewGuid(),
    };
}
