using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

/// <summary>
/// CARD-0340 S1: launch ownership is in-process and exact. A session id is owned from enqueue /
/// ResumeInterrupted until the launch task settles, including a faulted resume.
/// </summary>
[Category("Integration")]
[NotInParallel("AgentSessionLaunchQueueOwnership")]
public class AgentSessionLaunchQueueOwnershipTests
{
    [Test]
    public async Task Owns_is_true_from_enqueue_until_the_launch_settles()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = ready };
        await using var fixture = await OwnershipFixture.CreateAsync(adapter);
        var queue = fixture.Queue;

        queue.Owns(fixture.SessionId).ShouldBeFalse();
        queue.EnqueueInteractiveSession(
            fixture.SessionId, fixture.AgentId, fixture.Generation, fixture.Spec, remoteControlName: null);

        await WaitUntilAsync(() => adapter.Started);
        queue.Owns(fixture.SessionId).ShouldBeTrue();

        ready.SetResult(true);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        queue.Owns(fixture.SessionId).ShouldBeFalse();
    }

    [Test]
    public async Task ResumeInterrupted_registers_before_running_and_a_second_call_is_a_noop()
    {
        var ready = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var adapter = new FakeAgentProtocolAdapter { ReadyHold = ready };
        await using var fixture = await OwnershipFixture.CreateAsync(adapter, dispatchedTask: true);
        var queue = fixture.Queue;

        queue.ResumeInterrupted(fixture.SessionId, fixture.AgentId);
        await WaitUntilAsync(() => adapter.Attached);
        queue.Owns(fixture.SessionId).ShouldBeTrue();

        queue.ResumeInterrupted(fixture.SessionId, fixture.AgentId);
        adapter.Attached.ShouldBeTrue();

        ready.SetResult(true);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);
        queue.Owns(fixture.SessionId).ShouldBeFalse();
    }

    [Test]
    public async Task A_faulted_resume_still_releases_ownership()
    {
        var adapter = new FakeAgentProtocolAdapter { ReadyResult = false };
        await using var fixture = await OwnershipFixture.CreateAsync(adapter, dispatchedTask: true);
        var queue = fixture.Queue;

        queue.ResumeInterrupted(fixture.SessionId, fixture.AgentId);
        await queue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);

        queue.Owns(fixture.SessionId).ShouldBeFalse();
    }

    [Test]
    public async Task Queued_launch_carries_the_explicit_accepted_generation_and_never_re_reads_a_replaced_row()
    {
        var adapter = new FakeAgentProtocolAdapter();
        await using var fixture = await OwnershipFixture.CreateAsync(adapter);
        var g2 = SessionGeneration.Next(fixture.Generation, DateTime.UtcNow.AddMinutes(1));
        await using (var db = BridgeQueueHarness.CreateContext())
        {
            await db.AgentSessions.Where(s => s.Id == fixture.SessionId).ExecuteUpdateAsync(u => u
                .SetProperty(s => s.StartedAt, g2)
                .SetProperty(s => s.Status, SessionStatus.Starting));
        }

        fixture.Queue.EnqueueInteractiveSession(
            fixture.SessionId, fixture.AgentId, fixture.Generation, fixture.Spec, remoteControlName: null);
        await fixture.Queue.WaitForIdleAsync(TimeSpan.FromSeconds(15), CancellationToken.None);

        adapter.Started.ShouldBeFalse();
        adapter.StartedAcceptedGeneration.ShouldBeNull();
    }

    [Test]
    public async Task C514_Two_recovery_workers_resume_one_original_work()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync(isolated: true);
        var attempt = Guid.NewGuid();
        var body = "original deferred card work c514";
        await using (var db = h.CreateDb())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = body,
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.System,
                CreatedAt = DateTime.UtcNow,
                DeferredFromRunAttemptId = attempt,
                MaintenanceAcceptedStartedAt = h.Generation,
            });
            await db.SaveChangesAsync();
        }

        await using var db2 = h.CreateDb();
        db2.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = Guid.NewGuid(),
            AgentSessionId = h.SessionId,
            Body = body,
            Status = QueuedMessageStatus.Pending,
            Sequence = 2,
            Origin = QueuedMessageOrigin.System,
            CreatedAt = DateTime.UtcNow,
            DeferredFromRunAttemptId = attempt,
            MaintenanceAcceptedStartedAt = h.Generation,
        });
        var ex = await Should.ThrowAsync<DbUpdateException>(() => db2.SaveChangesAsync());
        (ex.InnerException as Npgsql.PostgresException)!.SqlState.ShouldBe("23505");
        await using var verify = h.CreateDb();
        (await verify.SessionQueuedMessages.CountAsync(m =>
            m.AgentSessionId == h.SessionId && m.DeferredFromRunAttemptId == attempt)).ShouldBe(1);
    }

    [Test]
    public async Task C514_Failed_enqueue_cannot_drop_launch_responsibility()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        h.LaunchQueue.TryRegister(h.SessionId).ShouldBeTrue();
        try
        {
            h.LaunchQueue.Owns(h.SessionId).ShouldBeTrue();
            h.Adapter.ConditionalInputs.ShouldBeEmpty();
        }
        finally
        {
            h.LaunchQueue.Unregister(h.SessionId);
        }
    }

    [Test]
    public async Task C514_Old_deferred_work_never_targets_replacement()
    {
        await using var h = await RemoteControlRecoveryHarness.CreateAsync();
        var oldG = h.Generation;
        await using (var db = h.CreateDb())
        {
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = h.SessionId,
                Body = "old generation deferred work c514",
                Status = QueuedMessageStatus.Pending,
                Sequence = 1,
                Origin = QueuedMessageOrigin.System,
                CreatedAt = DateTime.UtcNow,
                DeferredFromRunAttemptId = Guid.NewGuid(),
                MaintenanceAcceptedStartedAt = oldG,
            });
            await db.SaveChangesAsync();
        }

        await h.ReplaceGenerationAsync();
        h.Adapter.ConditionalInputs.Clear();
        await h.Queue.FlushSessionAsync(h.SessionId, CancellationToken.None);
        h.Adapter.SubmittedBodies.ShouldNotContain("old generation deferred work c514");
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        condition().ShouldBeTrue();
    }

    private sealed class OwnershipFixture : IAsyncDisposable
    {
        public required BridgeQueueHarness Harness { private get; init; }
        public required IServiceScope LaunchScope { private get; init; }
        public required Guid SessionId { get; init; }
        public required DateTime Generation { get; init; }
        public required Guid AgentId { get; init; }
        public required AgentLaunchSpec Spec { get; init; }
        public IServiceProvider Services => LaunchScope.ServiceProvider;
        public AgentSessionLaunchQueue Queue => Services.GetRequiredService<AgentSessionLaunchQueue>();

        public static async Task<OwnershipFixture> CreateAsync(
            FakeAgentProtocolAdapter adapter, bool dispatchedTask = false)
        {
            var harness = await BridgeQueueHarness.CreateAsync(new BridgeQueueHarness.HarnessOptions
            {
                AlwaysOn = false,
                ConfigureServices = s =>
                {
                    s.AddSingleton<IAgentProtocolAdapterFactory>(new OneAdapterFactory(adapter));
                },
            });

            var workspace = Path.Combine(harness.TempRoot, "ownership-workspace");
            Directory.CreateDirectory(workspace);
            var sessionId = Guid.NewGuid();
            var now = DateTime.UtcNow;
            await using (var db = BridgeQueueHarness.CreateContext())
            {
                db.AgentSessions.Add(new AgentSession
                {
                    Id = sessionId,
                    CardId = null,
                    DefinitionName = "fake",
                    AgentKind = AgentKind.ClaudeCode,
                    Status = SessionStatus.Starting,
                    Cwd = workspace,
                    Cols = 120,
                    Rows = 30,
                    CreatedAt = now,
                    StartedAt = now,
                    LastSeenAt = now,
                });
                await db.SaveChangesAsync();
                await db.Agents.Where(a => a.Id == harness.AgentId).ExecuteUpdateAsync(u => u
                    .SetProperty(a => a.Status, AgentStatus.Running)
                    .SetProperty(a => a.PersistentSessionId, sessionId.ToString("D")));

                if (dispatchedTask)
                {
                    var taskId = Guid.NewGuid();
                    db.AgentTasks.Add(new AgentTask
                    {
                        Id = taskId,
                        RootTaskId = taskId,
                        Title = "ownership resume",
                        Goal = "Do the thing.",
                        Role = AgentTaskRole.Plan,
                        AgentKind = AgentKind.ClaudeCode,
                        ModelLevel = AgentModelLevel.Frontier,
                        Workspace = WorkspaceMode.Shared,
                        WorkingDirectory = workspace,
                        AgentSessionId = sessionId,
                        AgentId = harness.AgentId,
                        Status = AgentTaskStatus.Dispatched,
                        CreatedAt = now,
                        DispatchedAt = now,
                    });
                    await db.SaveChangesAsync();
                }
            }

            return new OwnershipFixture
            {
                Harness = harness,
                LaunchScope = harness.Provider.CreateScope(),
                SessionId = sessionId,
                Generation = now,
                AgentId = harness.AgentId,
                Spec = new AgentLaunchSpec(
                    "fake", AgentKind.ClaudeCode, "fake", [], new Dictionary<string, string>(),
                    workspace, 120, 30, SessionId: sessionId),
            };
        }

        public async ValueTask DisposeAsync()
        {
            await using (var db = BridgeQueueHarness.CreateContext())
            {
                var taskIds = await db.AgentTasks
                    .Where(t => t.AgentSessionId == SessionId)
                    .Select(t => t.Id)
                    .ToListAsync();
                if (taskIds.Count > 0)
                    await db.AgentTaskEvents.Where(e => taskIds.Contains(e.AgentTaskId)).ExecuteDeleteAsync();
                await db.AgentTasks.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.SessionQueuedMessages.Where(m => m.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.TranscriptEntries.Where(t => t.AgentSessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentIncidents.Where(i => i.SessionId == SessionId).ExecuteDeleteAsync();
                await db.Alerts.Where(a => a.SessionId == SessionId).ExecuteDeleteAsync();
                await db.AgentSessions.Where(s => s.Id == SessionId).ExecuteDeleteAsync();
            }

            LaunchScope.Dispose();
            await Harness.DisposeAsync();
        }
    }

    private sealed class OneAdapterFactory(IAgentProtocolAdapter adapter) : IAgentProtocolAdapterFactory
    {
        public IAgentProtocolAdapter Create(AgentKind kind) => adapter;
    }
}
