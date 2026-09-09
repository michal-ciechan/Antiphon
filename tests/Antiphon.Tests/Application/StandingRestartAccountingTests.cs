using System.Data.Common;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[NotInParallel]
public class StandingRestartAccountingTests
{
    [Test]
    public async Task Real_failures_cap_escalate_and_reset_only_after_healthy_completion()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var healthy = new FakeAgentProtocolAdapter();
        var settings = new SupervisionSettings { HealthyUptimeResetMinutes = 5 };
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(), new FakeAgentProtocolAdapter(),
                    new FakeAgentProtocolAdapter { ThrowOnStart = new ArgumentException("synthetic confirmed launch failure") }, healthy],
                settings, definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            for (var failure = 1; failure <= 3; failure++)
            {
                if (failure <= 2)
                    await h.Provider.GetRequiredService<AgentSessionRuntime>().ObserveExitAsync(id, 1, AgentExitReason.ProcessExited, default);
                await h.Supervisor().TickAsync(default);
                await h.Supervisor().TickAsync(default);
                await using (var verify = AgentSupervisionTests.CreateContext())
                {
                    var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
                    state.ConsecutiveFailures.ShouldBe(failure); state.RestartBackoffFailures.ShouldBe(failure);
                    state.LastObservedRestartSessionId.ShouldBe(id); state.ContinuityHeldAt.ShouldBeNull();
                    var delta = state.NextRestartAt!.Value - h.Clock.GetUtcNow().UtcDateTime;
                    delta.TotalSeconds.ShouldBe(5 * Math.Pow(2, failure), 2);
                }
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, failure) + 1));
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            }
            healthy.StartedSessionId.ShouldBe(id); healthy.StartedArgs.ShouldContain("--resume");
            healthy.StartedArgs.ShouldNotContain("--session-id");
            h.Clock.Advance(TimeSpan.FromMinutes(4));
            await h.Supervisor().TickAsync(default);
            await using (var verify = AgentSupervisionTests.CreateContext())
                (await verify.AgentSupervisionStates.FindAsync(agent.Id))!.ConsecutiveFailures.ShouldBe(3);
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            await h.Supervisor().TickAsync(default);
            await using (var verify = AgentSupervisionTests.CreateContext())
            {
                var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures.ShouldBe(0); state.RestartBackoffFailures.ShouldBe(0);
                state.NextRestartAt.ShouldBeNull(); state.LastEscalationTier.ShouldBe(0);
                (await verify.AgentIncidents.CountAsync(i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.Recovered)).ShouldBe(1);
            }
            h.Supervisor().Backoff(int.MaxValue).ShouldBe(TimeSpan.FromDays(30));
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task Sync_failure_after_restamp_is_not_charged_again_by_terminal_observation()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var fault = new AfterReservationFailure();
        var resumed = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [new FakeAgentProtocolAdapter(), resumed],
                definitionKind: "ClaudeCode", configureDb: b => b.AddInterceptors(fault));
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var started = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(started.PersistentSessionId!);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.Status = SessionStatus.Failed;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.LastObservedRestartSessionId = id; state.LastObservedRestartStartedAt = session.StartedAt;
                state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                fault.SessionId = id; fault.Before = session.StartedAt;
                await db.SaveChangesAsync();
            }
            h.Clock.Advance(TimeSpan.FromSeconds(1));
            fault.Armed = true;
            await h.Supervisor().TickAsync(default);
            fault.Hits.ShouldBe(1); resumed.Started.ShouldBeFalse(); h.LaunchQueue.Owns(id).ShouldBeFalse();
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                session.StartedAt.ShouldNotBe(fault.Before);
                state.LastObservedRestartStartedAt.ShouldBe(session.StartedAt);
                state.RestartBackoffFailures.ShouldBe(1); state.ConsecutiveFailures.ShouldBe(0);
                state.NextRestartAt = null;
                await db.SaveChangesAsync();
            }
            await h.Provider.GetRequiredService<AgentSessionRuntime>().ObserveExitAsync(id, 1, AgentExitReason.ProcessExited, default);
            await h.Supervisor().TickAsync(default);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.RestartBackoffFailures.ShouldBe(1); state.ConsecutiveFailures.ShouldBe(0);
            }
            h.Clock.Advance(TimeSpan.FromSeconds(11));
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            resumed.StartedSessionId.ShouldBe(id); resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedArgs.ShouldNotContain("--session-id");
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task Failed_outcome_save_recovers_as_unknown_without_fresh_authority()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var fault = new OutcomeStorageFailure();
        var first = new FakeAgentProtocolAdapter();
        Guid id; Guid agentId;
        try
        {
            await using (var h = AgentSupervisionTests.BuildHarness(root, [first], definitionKind: "ClaudeCode",
                configureDb: b => b.AddInterceptors(fault)))
            {
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
                agentId = agent.Id; fault.AgentId = agentId;
                var accepted = await h.Control.StartAsync(agentId, new(), default);
                id = Guid.Parse(accepted.PersistentSessionId!);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                fault.LaunchHits.ShouldBe(1); fault.OutcomeHits.ShouldBe(1);
                first.Killed.ShouldBeTrue(); first.Disposed.ShouldBeTrue();
                for (var i = 0; i < 2; i++) await h.Supervisor().TickAsync(default);
                fault.BookkeepingHits.ShouldBe(2);
                await using var verify = AgentSupervisionTests.CreateContext();
                var session = (await verify.AgentSessions.FindAsync(id))!;
                session.RestartFailureKind.ShouldBeNull(); session.InteractiveLaunchCompletedAt.ShouldBeNull();
                (await verify.AgentSupervisionStates.FindAsync(agentId))!.ConsecutiveFailures.ShouldBe(0);
                (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == agentId)).ShouldBe(1);
            }
            // Reconstruct the provider, queue and scopes after storage recovers. Reconciliation's
            // actual runtime exit path supplies terminal evidence; the lost save invents no cause.
            var resumed = new FakeAgentProtocolAdapter();
            await using var recovered = AgentSupervisionTests.BuildHarness(root, [resumed], definitionKind: "ClaudeCode");
            await recovered.Provider.GetRequiredService<AgentSessionRuntime>().ObserveExitAsync(id, 1, AgentExitReason.ProcessExited, default);
            await recovered.Supervisor().TickAsync(default);
            await recovered.Supervisor().TickAsync(default);
            await using (var verify = AgentSupervisionTests.CreateContext())
            {
                var state = (await verify.AgentSupervisionStates.FindAsync(agentId))!;
                state.RestartBackoffFailures.ShouldBe(1); state.ConsecutiveFailures.ShouldBe(0);
                state.ContinuityHeldAt.ShouldBeNull(); state.LastObservedRestartSessionId.ShouldBe(id);
            }
            recovered.Clock.Advance(TimeSpan.FromSeconds(11));
            await recovered.Supervisor().TickAsync(default);
            await recovered.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            resumed.StartedSessionId.ShouldBe(id); resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedArgs.ShouldNotContain("--session-id");
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task Observation_probe_timeout_and_requested_cancellation_authorize_no_attempt()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var adapter = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [adapter], definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            h.Runner.ListOverride = _ => throw new TaskCanceledException("synthetic transport timeout");
            await h.Supervisor().TickAsync(default);
            using var canceled = new CancellationTokenSource(); canceled.Cancel();
            h.Runner.ListOverride = ct => { ct.ThrowIfCancellationRequested(); throw new InvalidOperationException("gate not canceled"); };
            await Should.ThrowAsync<OperationCanceledException>(h.Supervisor().TickAsync(canceled.Token));
            adapter.Started.ShouldBeFalse();
            await using (var verify = AgentSupervisionTests.CreateContext())
            {
                (await verify.Agents.FindAsync(agent.Id))!.PersistentSessionId.ShouldBeNull();
                (await verify.AgentSupervisionStates.CountAsync(s => s.AgentId == agent.Id)).ShouldBe(0);
            }
            h.Runner.ListOverride = null;
            await h.Supervisor().TickAsync(default);
            await using var db = AgentSupervisionTests.CreateContext();
            var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
            state.NextRestartAt.ShouldNotBeNull(); state.RestartBackoffFailures.ShouldBe(0); state.ConsecutiveFailures.ShouldBe(0);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    [Arguments(0)] [Arguments(1)] [Arguments(2)] [Arguments(int.MaxValue)]
    public async Task Wrapped_57P03_retries_preserve_identity_and_grow_only_backoff(int threshold)
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var interceptor = new CompositionFailure();
        var boot = new FakeAgentProtocolAdapter(); var resumed = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [boot, resumed],
                new SupervisionSettings { FreshAfterResumeFailures = threshold }, definitionKind: "ClaudeCode",
                configureDb: b => b.AddInterceptors(interceptor));
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var started = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(started.PersistentSessionId!);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.Status = SessionStatus.Failed;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures = 7;
                state.LastObservedRestartSessionId = id; state.LastObservedRestartStartedAt = session.StartedAt;
                state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await db.SaveChangesAsync();
            }
            interceptor.AgentId = agent.Id; interceptor.Remaining = 3;
            for (var attempt = 1; attempt <= 3; attempt++)
            {
                await h.Supervisor().TickAsync(default);
                await using var db = AgentSupervisionTests.CreateContext();
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures.ShouldBe(7);
                state.RestartBackoffFailures.ShouldBe(attempt);
                state.ContinuityHeldAt.ShouldBeNull();
                (await db.Agents.FindAsync(agent.Id))!.PersistentSessionId.ShouldBe(id.ToString("D"));
                var before = interceptor.Hits;
                await h.Supervisor().TickAsync(default);
                interceptor.Hits.ShouldBe(before);
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, attempt) + 1));
            }
            interceptor.Hits.ShouldBe(3);
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            resumed.Started.ShouldBeTrue(); resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedSessionId.ShouldBe(id); resumed.StartedArgs.ShouldNotContain("--session-id");
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task Async_infrastructure_failure_is_consumed_once_across_recreation()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter { ThrowOnStart = new PostgresException("starting", "FATAL", "FATAL", "57P03") }],
                definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var detail = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(detail.PersistentSessionId!);
            for (var i = 0; i < 3; i++)
            {
                await h.Supervisor().TickAsync(default);
                await using var db = AgentSupervisionTests.CreateContext();
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ConsecutiveFailures.ShouldBe(0); state.RestartBackoffFailures.ShouldBe(1);
                state.LastObservedRestartSessionId.ShouldBe(id);
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.RestartFailureKind.ShouldBe(RestartFailureKind.Infrastructure);
                session.InteractiveLaunchCompletedAt.ShouldBeNull();
                // Re-enter the dead-row observation branch, as a fresh scheduler scope would.
                state.NextRestartAt = null;
                await db.SaveChangesAsync();
            }
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    private sealed class CompositionFailure : DbCommandInterceptor
    {
        public Guid AgentId { get; set; }
        public int Remaining { get; set; }
        public int Hits { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Remaining > 0 && command.CommandText.Contains("FROM \"AgentBundleAttachments\"")
                && command.Parameters.Cast<DbParameter>().Any(p => p.Value is IEnumerable<Guid> ids && ids.Contains(AgentId)))
            {
                Remaining--; Hits++;
                throw new DbUpdateException("composition storage", new PostgresException("starting", "FATAL", "FATAL", "57P03"));
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AfterReservationFailure : DbTransactionInterceptor
    {
        public Guid SessionId { get; set; }
        public DateTime Before { get; set; }
        public bool Armed { get; set; }
        public int Hits { get; private set; }
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Armed && eventData.Context!.ChangeTracker.Entries<AgentSession>().Any(e => e.Entity.Id == SessionId
                && e.Entity.Status == SessionStatus.Starting && e.Entity.StartedAt != Before))
            {
                Armed = false; Hits++;
                throw new DbUpdateException("reservation committed before enqueue", new PostgresException("starting", "FATAL", "FATAL", "57P03"));
            }
            return Task.CompletedTask;
        }
    }

    private sealed class OutcomeStorageFailure : SaveChangesInterceptor
    {
        public Guid AgentId { get; set; }
        public int LaunchHits { get; private set; }
        public int OutcomeHits { get; private set; }
        public int BookkeepingHits { get; private set; }
        private bool unavailable;
        private static Exception Outage() => new DbUpdateException("synthetic unavailable storage", new PostgresException("starting", "FATAL", "FATAL", "57P03"));
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            if (!unavailable && LaunchHits == 0 && eventData.Context!.ChangeTracker.Entries<AgentSession>().Any(e =>
                e.Entity.StandingAgentId == AgentId && e.Entity.Status == SessionStatus.Running && e.Entity.InteractiveLaunchCompletedAt == null))
            {
                LaunchHits++; unavailable = true; throw Outage();
            }
            return ValueTask.FromResult(result);
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (unavailable)
            {
                if (eventData.Context!.ChangeTracker.Entries<AgentSession>().Any(e => e.Entity.StandingAgentId == AgentId && e.Entity.RestartFailureKind != null))
                { OutcomeHits++; throw Outage(); }
                if (eventData.Context.ChangeTracker.Entries<AgentSupervisionState>().Any(e => e.Entity.AgentId == AgentId && e.State == EntityState.Modified))
                { BookkeepingHits++; throw Outage(); }
            }
            return ValueTask.FromResult(result);
        }
    }
}
