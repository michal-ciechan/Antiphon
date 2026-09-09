using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingRestartAccountingTests
{
    [Test]
    [Arguments(SessionStatus.Starting)] [Arguments(SessionStatus.Running)]
    public async Task An_old_start_timestamp_without_completed_boot_cannot_reset_failure_counters(SessionStatus status)
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [], definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root); var id = Guid.NewGuid();
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var old = h.Clock.GetUtcNow().UtcDateTime.AddHours(-1);
                db.AgentSessions.Add(new AgentSession { Id = id, StandingAgentId = agent.Id, AgentKind = AgentKind.ClaudeCode,
                    Cwd = root, DefinitionName = "fake", Status = status, CreatedAt = old, StartedAt = old, LastSeenAt = old });
                (await db.Agents.FindAsync(agent.Id))!.PersistentSessionId = id.ToString("D");
                db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = agent.Id, ConsecutiveFailures = 7,
                    RestartBackoffFailures = 9, LastEscalationTier = 2, UpdatedAt = old });
                await db.SaveChangesAsync();
            }
            await h.Supervisor().TickAsync(default);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures.ShouldBe(7); state.RestartBackoffFailures.ShouldBe(9); state.LastEscalationTier.ShouldBe(2);
                var row = (await db.AgentSessions.FindAsync(id))!; row.Status = SessionStatus.Running;
                row.InteractiveLaunchCompletedAt = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10); await db.SaveChangesAsync();
            }
            await h.Supervisor().TickAsync(default);
            await using var verify = AgentSupervisionTests.CreateContext();
            (await verify.AgentSupervisionStates.FindAsync(agent.Id))!.RestartBackoffFailures.ShouldBe(0);
            (await verify.AgentSupervisionStates.FindAsync(agent.Id))!.ConsecutiveFailures.ShouldBe(0);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task Early_running_infrastructure_evidence_survives_provider_rebuild_and_distinct_failed_generations()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        Guid agentId = default, sessionId = default; DateTime previousGeneration = default;
        try
        {
            for (var generation = 1; generation <= 2; generation++)
            {
                var fault = new EarlyRunningFailure();
                var adapter = new FakeAgentProtocolAdapter();
                await using var h = AgentSupervisionTests.BuildHarness(root, [adapter], definitionKind: "ClaudeCode",
                    configureDb: b => b.AddInterceptors(fault));
                try
                {
                    if (generation == 1)
                    {
                        var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root); agentId = agent.Id;
                        fault.AgentId = agentId;
                        var start = await h.Control.StartAsync(agentId, new(), default); sessionId = Guid.Parse(start.PersistentSessionId!);
                    }
                    else
                    {
                        fault.AgentId = agentId;
                        await h.Supervisor().TickAsync(default);
                        await using (var db = AgentSupervisionTests.CreateContext())
                            (await db.AgentSupervisionStates.FindAsync(agentId))!.RestartBackoffFailures.ShouldBe(1);
                        h.Clock.Advance(TimeSpan.FromSeconds(11)); await h.Supervisor().TickAsync(default);
                    }
                    await fault.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                    h.LaunchQueue.Owns(sessionId).ShouldBeTrue();
                    await h.Supervisor().TickAsync(default);
                    await using (var db = AgentSupervisionTests.CreateContext())
                    {
                        var state = (await db.AgentSupervisionStates.FindAsync(agentId))!;
                        state.RestartBackoffFailures.ShouldBe(generation - 1); state.ConsecutiveFailures.ShouldBe(0);
                        var row = (await db.AgentSessions.FindAsync(sessionId))!;
                        row.Status.ShouldBe(SessionStatus.Running); row.RestartFailureKind.ShouldBeNull();
                        row.InteractiveLaunchCompletedAt.ShouldBeNull(); row.StartedAt.ShouldNotBe(previousGeneration);
                        previousGeneration = row.StartedAt;
                    }
                    fault.Release.TrySetResult(); await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                    adapter.Killed.ShouldBeTrue(); adapter.Disposed.ShouldBeTrue(); adapter.Inputs.ShouldBeEmpty();
                    if (generation == 2) { adapter.StartedArgs.ShouldContain("--resume"); adapter.StartedArgs.ShouldNotContain("--session-id"); }
                    await h.Provider.GetRequiredService<AgentSessionRuntime>().ObserveExitAsync(sessionId, 1, AgentExitReason.ProcessExited, default);
                    for (var repeat = 0; repeat < 2; repeat++) await h.Supervisor().TickAsync(default);
                    await using var verify = AgentSupervisionTests.CreateContext();
                    var outcome = (await verify.AgentSessions.FindAsync(sessionId))!;
                    outcome.RestartFailureKind.ShouldBe(RestartFailureKind.Infrastructure); outcome.InteractiveLaunchCompletedAt.ShouldBeNull();
                    var consumed = (await verify.AgentSupervisionStates.FindAsync(agentId))!;
                    consumed.RestartBackoffFailures.ShouldBe(generation); consumed.ConsecutiveFailures.ShouldBe(0);
                    consumed.LastObservedRestartStartedAt.ShouldBe(outcome.StartedAt); consumed.ContinuityHeldAt.ShouldBeNull();
                }
                finally { fault.Release.TrySetResult(); await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default); }
            }
            await using var rebuilt = AgentSupervisionTests.BuildHarness(root, [], definitionKind: "ClaudeCode");
            await rebuilt.Supervisor().TickAsync(default);
            await using var final = AgentSupervisionTests.CreateContext();
            (await final.AgentSupervisionStates.FindAsync(agentId))!.RestartBackoffFailures.ShouldBe(2);
            (await final.AgentSessions.CountAsync(s => s.StandingAgentId == agentId)).ShouldBe(1);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    [Arguments(false)] [Arguments(true)]
    public async Task Timeout_retries_but_requested_cancellation_does_not_charge(bool requested)
    {
        var root = AgentSupervisionTests.NewTempRoot(); using var cancellation = new CancellationTokenSource();
        var fault = new CompositionFailure(); var resumed = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [new FakeAgentProtocolAdapter(), resumed],
                definitionKind: "ClaudeCode", configureDb: b => b.AddInterceptors(fault));
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var start = await h.Control.StartAsync(agent.Id, new(), default); var id = Guid.Parse(start.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var row = (await db.AgentSessions.FindAsync(id))!; row.Status = SessionStatus.Failed;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.LastObservedRestartSessionId = id; state.LastObservedRestartStartedAt = row.StartedAt;
                state.ConsecutiveFailures = 7; state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
            fault.AgentId = agent.Id; fault.Remaining = 1;
            fault.Failure = () => { if (requested) cancellation.Cancel(); return new TaskCanceledException("synthetic composition timeout", null, cancellation.Token); };
            if (requested) await Should.ThrowAsync<OperationCanceledException>(h.Supervisor().TickAsync(cancellation.Token));
            else await h.Supervisor().TickAsync(cancellation.Token);
            fault.Hits.ShouldBe(1); resumed.Started.ShouldBeFalse();
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures.ShouldBe(7); state.RestartBackoffFailures.ShouldBe(requested ? 0 : 1);
                state.ContinuityHeldAt.ShouldBeNull();
            }
            h.Clock.Advance(TimeSpan.FromSeconds(20)); await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            resumed.StartedSessionId.ShouldBe(id); resumed.StartedArgs.ShouldContain("--resume");
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    private sealed class EarlyRunningFailure : SaveChangesInterceptor
    {
        public Guid AgentId { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hits;
        public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<AgentSession>().Any(e => e.Entity.StandingAgentId == AgentId
                && e.Entity.Status == SessionStatus.Running && e.Entity.InteractiveLaunchCompletedAt == null)
                && Interlocked.CompareExchange(ref _hits, 1, 0) == 0)
            {
                Entered.TrySetResult(); await Release.Task.WaitAsync(ct);
                throw new DbUpdateException("after early Running commit", new PostgresException("starting", "FATAL", "FATAL", "57P03"));
            }
            return result;
        }
    }
}
