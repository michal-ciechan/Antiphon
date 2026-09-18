using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingRestartAccountingTests
{
    [Test]
    public async Task C561_the_fifth_non_infrastructure_resume_failure_holds_continuity_instead_of_scheduling()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var t1 = Unknown(1); var t2 = Unknown(2); var t3 = Unknown(3); var t4 = Unknown(4);
        var sentinel = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(), t1, t2, t3, t4, sentinel],
                new SupervisionSettings { HealthyUptimeResetMinutes = 5, ResumeFailureHoldAttempts = 5 },
                definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await SessionExitObservation.ObserveMatchingAsync(
                h.Provider.GetRequiredService<AgentSessionRuntime>(),
                id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
            await h.Supervisor().TickAsync(default);
            await AssertNotHeldAsync(h, agent.Id, failures: 1);
            for (var k = 1; k <= 4; k++)
            {
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, k) + 1));
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await h.Supervisor().TickAsync(default);
                if (k < 4)
                    await AssertNotHeldAsync(h, agent.Id, failures: k + 1);
            }

            await using (var verify = AgentSupervisionTests.CreateContext())
            {
                var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ContinuityHeldAt.ShouldNotBeNull();
                state.ContinuityReason.ShouldBe(StandingContinuityReason.RepeatedResumeFailure);
                state.ContinuitySessionId.ShouldBe(id);
                state.ContinuityEvidence.ShouldContain("5 consecutive supervised resumes");
                state.ContinuityEvidence.ShouldContain("Unknown");
                state.ContinuityEvidence!.Length.ShouldBeLessThanOrEqualTo(1000);
                state.NextRestartAt.ShouldBeNull();
                (await verify.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RestartScheduled)).ShouldBe(4);
                (await verify.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.BackoffEscalated)).ShouldBe(0);
                (await verify.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.Crash && i.SessionId == id))
                    .ShouldBeGreaterThanOrEqualTo(1);
                (await verify.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.StandingContinuityHeld)).ShouldBe(1);
                (await verify.AgentIncidents.SingleAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.StandingContinuityHeld))
                    .Severity.ShouldBe(AlertSeverity.Info);
            }

            h.EventBus.PublishedEvents.Any(e =>
                e.EventName == "AgentChanged"
                && e.Payload is AgentChangedEventDto dto && dto.AgentId == agent.Id).ShouldBeTrue();
            await using (var scope = h.Provider.CreateAsyncScope())
            {
                var detail = await scope.ServiceProvider.GetRequiredService<AgentService>()
                    .GetByIdAsync(agent.Id, default);
                detail.Supervision!.ContinuityResumeFailures.ShouldBe(5);
            }

            h.Clock.Advance(TimeSpan.FromDays(1));
            await h.Supervisor().TickAsync(default);
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            sentinel.Started.ShouldBeFalse();
            await using (var verify = AgentSupervisionTests.CreateContext())
            {
                (await verify.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RestartScheduled)).ShouldBe(4);
            }

            var held = await Should.ThrowAsync<ConflictException>(() =>
                h.Control.StartAsync(agent.Id, new(), default));
            held.Code.ShouldBe(StandingContinuityState.HeldCode);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C561_a_start_that_throws_charges_the_same_counter_and_trips_the_same_hold()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var interceptor = new CompositionFailure
        {
            Remaining = 5,
            Failure = () => new InvalidOperationException("synthetic composition failure")
        };
        var resumed = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [new FakeAgentProtocolAdapter(), resumed],
                new SupervisionSettings { ResumeFailureHoldAttempts = 5 }, definitionKind: "ClaudeCode",
                configureDb: b => b.AddInterceptors(interceptor));
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var started = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(started.PersistentSessionId!);
            interceptor.AgentId = agent.Id;
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.Status = SessionStatus.Failed;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.LastObservedRestartSessionId = id;
                state.LastObservedRestartStartedAt = session.StartedAt;
                state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await db.SaveChangesAsync();
            }

            for (var attempt = 1; attempt <= 4; attempt++)
            {
                await h.Supervisor().TickAsync(default);
                await using var db = AgentSupervisionTests.CreateContext();
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ContinuityResumeFailures.ShouldBe(attempt);
                state.RestartBackoffFailures.ShouldBe(attempt);
                (await db.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.StartFailure)).ShouldBe(attempt);
                state.ContinuityHeldAt.ShouldBeNull();
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, attempt) + 1));
            }

            await h.Supervisor().TickAsync(default);
            interceptor.Hits.ShouldBe(5);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                (await db.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.StartFailure)).ShouldBe(5);
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ContinuityHeldAt.ShouldNotBeNull();
                state.ContinuityReason.ShouldBe(StandingContinuityReason.RepeatedResumeFailure);
                state.NextRestartAt.ShouldBeNull();
                (await db.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.BackoffEscalated)).ShouldBe(0);
            }

            h.Clock.Advance(TimeSpan.FromDays(1));
            await h.Supervisor().TickAsync(default);
            interceptor.Hits.ShouldBe(5);
            resumed.Started.ShouldBeFalse();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C561_infrastructure_outcomes_grow_the_ladder_and_never_hold()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var interceptor = new CompositionFailure { Remaining = 6 };
        var resumed = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [new FakeAgentProtocolAdapter(), resumed],
                new SupervisionSettings { ResumeFailureHoldAttempts = 5 }, definitionKind: "ClaudeCode",
                configureDb: b => b.AddInterceptors(interceptor));
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var started = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(started.PersistentSessionId!);
            interceptor.AgentId = agent.Id;
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.Status = SessionStatus.Failed;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.LastObservedRestartSessionId = id;
                state.LastObservedRestartStartedAt = session.StartedAt;
                state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await db.SaveChangesAsync();
            }

            for (var attempt = 1; attempt <= 6; attempt++)
            {
                await h.Supervisor().TickAsync(default);
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, attempt) + 1));
            }

            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.RestartBackoffFailures.ShouldBe(6);
                state.ContinuityResumeFailures.ShouldBe(0);
                state.ContinuityHeldAt.ShouldBeNull();
                (await db.Agents.FindAsync(agent.Id))!.PersistentSessionId.ShouldBe(id.ToString("D"));
            }

            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            resumed.StartedArgs.ShouldContain("--resume");
            resumed.StartedSessionId.ShouldBe(id);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C561_healthy_uptime_resets_the_resume_failure_counter()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var t1 = Unknown(1); var t2 = Unknown(2); var t3 = Unknown(3);
        var healthyResume = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(), t1, t2, t3, healthyResume],
                new SupervisionSettings { HealthyUptimeResetMinutes = 5, ResumeFailureHoldAttempts = 5 },
                definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await SessionExitObservation.ObserveMatchingAsync(
                h.Provider.GetRequiredService<AgentSessionRuntime>(),
                id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
            await h.Supervisor().TickAsync(default);
            for (var k = 1; k <= 3; k++)
            {
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, k) + 1));
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await h.Supervisor().TickAsync(default);
            }

            await using (var db = AgentSupervisionTests.CreateContext())
                (await db.AgentSupervisionStates.FindAsync(agent.Id))!.ContinuityResumeFailures.ShouldBe(4);

            h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, 4) + 1));
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            healthyResume.StartedArgs.ShouldContain("--resume");
            h.Clock.Advance(TimeSpan.FromMinutes(4));
            await h.Supervisor().TickAsync(default);
            await using (var db = AgentSupervisionTests.CreateContext())
                (await db.AgentSupervisionStates.FindAsync(agent.Id))!.ContinuityResumeFailures.ShouldBe(4);
            h.Clock.Advance(TimeSpan.FromMinutes(2));
            await h.Supervisor().TickAsync(default);
            await using var verify = AgentSupervisionTests.CreateContext();
            var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            state.ContinuityResumeFailures.ShouldBe(0);
            state.RestartBackoffFailures.ShouldBe(0);
            state.ConsecutiveFailures.ShouldBe(0);
            (await verify.AgentIncidents.CountAsync(i =>
                i.AgentId == agent.Id && i.Kind == AgentIncidentKind.Recovered)).ShouldBe(1);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C561_a_human_retry_resets_the_counter_and_the_next_five_hold_again()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var t = Enumerable.Range(1, 8).Select(Unknown).ToArray();
        var healthyRetry = new FakeAgentProtocolAdapter();
        var sentinel = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(), t[0], t[1], t[2], t[3], healthyRetry, t[4], t[5], t[6], t[7], sentinel],
                new SupervisionSettings { HealthyUptimeResetMinutes = 5, ResumeFailureHoldAttempts = 5 },
                definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await ReachHoldAsync(h, agent.Id, id);
            await using var retryScope = h.Provider.CreateAsyncScope();
            await retryScope.ServiceProvider.GetRequiredService<AgentControlService>()
                .StartAsync(agent.Id, new(RetryContinuity: true), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            healthyRetry.StartedSessionId.ShouldBe(id);
            healthyRetry.StartedArgs.ShouldContain("--resume");
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ContinuityResumeFailures.ShouldBe(0);
                state.ContinuityHeldAt.ShouldBeNull();
                (await db.AgentIncidents.SingleAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.StandingResumeSelected))
                    .Message.ShouldContain("repaired-target retry");
            }

            await SessionExitObservation.ObserveMatchingAsync(
                h.Provider.GetRequiredService<AgentSessionRuntime>(),
                id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
            await h.Supervisor().TickAsync(default);
            await using (var afterExit = AgentSupervisionTests.CreateContext())
            {
                var state = (await afterExit.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ContinuityResumeFailures.ShouldBe(1);
                state.ContinuityHeldAt.ShouldBeNull();
                state.NextRestartAt.ShouldNotBeNull();
            }
            for (var k = 1; k <= 4; k++)
            {
                // Human retry resets ContinuityResumeFailures, not RestartBackoffFailures, so
                // the remaining ladder is already past 5·2^k seconds.
                await AdvancePastNextRestartAsync(h, agent.Id);
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await h.Supervisor().TickAsync(default);
                if (k < 4)
                {
                    await using var db = AgentSupervisionTests.CreateContext();
                    var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                    state.ContinuityResumeFailures.ShouldBe(k + 1);
                    state.ContinuityHeldAt.ShouldBeNull();
                }
            }
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ContinuityHeldAt.ShouldNotBeNull();
                state.ContinuityReason.ShouldBe(StandingContinuityReason.RepeatedResumeFailure);
                (await db.AgentIncidents.CountAsync(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.StandingContinuityHeld)).ShouldBe(2);
            }

            sentinel.Started.ShouldBeFalse();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C561_a_zero_threshold_restores_the_never_give_up_ladder()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var throws = Enumerable.Range(1, 11).Select(Unknown).ToArray();
        var sentinel = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root,
                [new FakeAgentProtocolAdapter(), ..throws, sentinel],
                new SupervisionSettings { ResumeFailureHoldAttempts = 0, HealthyUptimeResetMinutes = 5 },
                definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var accepted = await h.Control.StartAsync(agent.Id, new(), default);
            var id = Guid.Parse(accepted.PersistentSessionId!);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await SessionExitObservation.ObserveMatchingAsync(
                h.Provider.GetRequiredService<AgentSessionRuntime>(),
                id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
            await h.Supervisor().TickAsync(default);
            for (var k = 1; k <= 11; k++)
            {
                await using (var db = AgentSupervisionTests.CreateContext())
                    (await db.AgentSupervisionStates.FindAsync(agent.Id))!.ContinuityHeldAt.ShouldBeNull();
                h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, Math.Min(k, 40)) + 1));
                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await h.Supervisor().TickAsync(default);
            }

            await using var verify = AgentSupervisionTests.CreateContext();
            var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            state.ContinuityHeldAt.ShouldBeNull();
            state.RestartBackoffFailures.ShouldBe(12);
            state.ContinuityResumeFailures.ShouldBe(12);
            state.LastEscalationTier.ShouldBe(1);
            (await verify.AgentIncidents.Where(i =>
                    i.AgentId == agent.Id && i.Kind == AgentIncidentKind.BackoffEscalated).ToListAsync())
                .ShouldHaveSingleItem().Severity.ShouldBe(AlertSeverity.Warning);
            (await verify.AgentIncidents.CountAsync(i =>
                i.AgentId == agent.Id && i.Kind == AgentIncidentKind.RestartScheduled)).ShouldBe(12);
            state.NextRestartAt.ShouldNotBeNull();
            sentinel.Started.ShouldBeFalse();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C561_a_supervised_resume_clears_the_hold_fields_but_not_the_counter()
    {
        var root = AgentSupervisionTests.NewTempRoot();
        var adapter = new FakeAgentProtocolAdapter();
        try
        {
            await using var h = AgentSupervisionTests.BuildHarness(root, [new FakeAgentProtocolAdapter(), adapter], definitionKind: "ClaudeCode");
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var started = await h.Control.StartAsync(agent.Id, new(), default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            var id = Guid.Parse(started.PersistentSessionId!);
            await using (var db = AgentSupervisionTests.CreateContext())
            {
                var session = (await db.AgentSessions.FindAsync(id))!;
                session.Status = SessionStatus.Failed;
                var state = (await db.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.ContinuityResumeFailures = 3;
                state.ContinuityEvidence = "stale-evidence";
                state.LastObservedRestartSessionId = id;
                state.LastObservedRestartStartedAt = session.StartedAt;
                state.NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1);
                await db.SaveChangesAsync();
            }

            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            adapter.StartedArgs.ShouldContain("--resume");
            await using var verify = AgentSupervisionTests.CreateContext();
            var after = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
            after.ContinuityResumeFailures.ShouldBe(3);
            after.ContinuityEvidence.ShouldBeNull();
            after.ContinuityHeldAt.ShouldBeNull();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    private static FakeAgentProtocolAdapter Unknown(int n) =>
        new() { ThrowOnStart = new InvalidOperationException($"synthetic unknown {n}") };

    private static async Task AssertNotHeldAsync(AgentSupervisionTests.Harness h, Guid agentId, int failures)
    {
        await using var verify = AgentSupervisionTests.CreateContext();
        var state = (await verify.AgentSupervisionStates.FindAsync(agentId))!;
        state.ContinuityResumeFailures.ShouldBe(failures);
        state.RestartBackoffFailures.ShouldBe(failures);
        state.ContinuityHeldAt.ShouldBeNull();
        state.NextRestartAt.ShouldNotBeNull();
        (await verify.AgentIncidents.CountAsync(i =>
            i.AgentId == agentId && i.Kind == AgentIncidentKind.RestartScheduled)).ShouldBe(failures);
    }

    private static async Task ReachHoldAsync(AgentSupervisionTests.Harness h, Guid agentId, Guid id)
    {
        await SessionExitObservation.ObserveMatchingAsync(
            h.Provider.GetRequiredService<AgentSessionRuntime>(),
            id, 1, AgentExitReason.ProcessExited, AgentSupervisionTests.CreateContext);
        await h.Supervisor().TickAsync(default);
        for (var k = 1; k <= 4; k++)
        {
            h.Clock.Advance(TimeSpan.FromSeconds(5 * Math.Pow(2, k) + 1));
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await h.Supervisor().TickAsync(default);
        }
    }

    private static async Task AdvancePastNextRestartAsync(AgentSupervisionTests.Harness h, Guid agentId)
    {
        await using var db = AgentSupervisionTests.CreateContext();
        var due = (await db.AgentSupervisionStates.FindAsync(agentId))!.NextRestartAt;
        due.ShouldNotBeNull();
        var now = h.Clock.GetUtcNow().UtcDateTime;
        if (due.Value > now)
            h.Clock.Advance(due.Value - now + TimeSpan.FromSeconds(1));
    }
}
