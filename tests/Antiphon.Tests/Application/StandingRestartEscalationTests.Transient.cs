using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingRestartEscalationTests
{
    [Test]
    public Task C510_S2_fresh_inventory_failure_keeps_bounded_retry() =>
        AssertTransientFreshFailureAsync(modelDisabled: false);

    [Test]
    public Task C510_S2_fresh_model_disabled_keeps_capacity_wait_and_bounded_retry() =>
        AssertTransientFreshFailureAsync(modelDisabled: true);

    private async Task AssertTransientFreshFailureAsync(bool modelDisabled)
    {
        foreach (var capacity in new[] { false, true })
        foreach (var alreadyHeld in new[] { false, true })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var root = AgentSupervisionTests.NewTempRoot();
            try
            {
                var fresh = new FakeAgentProtocolAdapter();
                // Freeze scheduling and its fresh failure scopes, while launch/queue polling
                // continues to use the harness's advancing clock.
                var supervisorClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
                await using var h = AgentSupervisionTests.BuildHarness(root, [fresh],
                    new SupervisionSettings { CapacityRecovery = new CapacityRecoverySettings
                        { Enabled = true, JitterSeconds = 0 } }, includeModelAvailability: modelDisabled,
                    definitionKind: "ClaudeCode", connectionString: schema.ConnectionString,
                    supervisorTimeProvider: supervisorClock);
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
                var oldId = Guid.NewGuid();
                var now = supervisorClock.GetUtcNow().UtcDateTime;
                var generation = now.AddMinutes(-10);
                var holdId = Guid.NewGuid();
                await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
                {
                    var owner = (await db.Agents.FindAsync(agent.Id))!;
                    owner.Kind = AgentKind.ClaudeCode;
                    owner.PersistentSessionId = oldId.ToString("D");
                    db.AgentSessions.Add(new AgentSession { Id = oldId, StandingAgentId = agent.Id,
                        AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake", Cwd = owner.WorkingDirectory,
                        Status = SessionStatus.Failed, CreatedAt = generation, StartedAt = generation });
                    db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = agent.Id,
                        AutomaticResumeFailures = 3, ContinuityResumeFailures = 4, RestartBackoffFailures = 4,
                        LastAutomaticResumeSessionId = oldId, LastAutomaticResumeStartedAt = generation,
                        LastObservedRestartSessionId = oldId, LastObservedRestartStartedAt = generation,
                        ContinuityHeldAt = alreadyHeld ? generation : null,
                        ContinuitySessionId = alreadyHeld ? oldId : null,
                        ContinuityReason = alreadyHeld ? StandingContinuityReason.RepeatedResumeFailure : null,
                        NextRestartAt = now.AddSeconds(-1), UpdatedAt = generation });
                    await db.SaveChangesAsync();
                }
                var recovery = h.Provider.GetRequiredService<CapacityRecoveryService>();
                if (capacity)
                {
                    await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                        $"agent:{agent.Id:N}", CapacityWaitConsumerKind.StandingStart,
                        executionKind: AgentKind.ClaudeCode, holdAlreadyCleared: true,
                        agentId: agent.Id, sessionId: oldId), default);
                    await recovery.ReconcileAsync(default);
                }
                if (modelDisabled)
                {
                    await using var db = CapacityRecoveryTestSupport.CreateContext(schema);
                    db.ModelAvailabilityHolds.Add(CapacityRecoveryTestSupport.Hold(holdId, alias: "*", until: now.AddHours(1)));
                    await db.SaveChangesAsync();
                }
                var probes = 0;
                h.Runner.ListOverride = _ =>
                {
                    if (Interlocked.Increment(ref probes) == 2 && !modelDisabled)
                        throw new HttpRequestException("fresh inventory unavailable");
                    return Task.FromResult<IReadOnlyList<SessionRunnerSessionDto>>([]);
                };
                await h.Supervisor().TickAsync(default);
                probes.ShouldBe(modelDisabled ? 1 : 2, "failure must occur in the fresh preflight before reservation");
                await using (var verify = CapacityRecoveryTestSupport.CreateContext(schema))
                {
                    var state = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
                    state.ContinuityReason.ShouldNotBe(StandingContinuityReason.AutomaticFreshRefused,
                        $"capacity={capacity}, held={alreadyHeld}: a transient failure is not a durable refusal");
                    if (alreadyHeld)
                    {
                        state.ContinuityReason.ShouldBe(StandingContinuityReason.RepeatedResumeFailure);
                        state.ContinuityHeldAt!.Value.ShouldBe(generation, TimeSpan.FromMilliseconds(1));
                    }
                    else state.ContinuityHeldAt.ShouldBeNull();
                    state.NextRestartAt.ShouldNotBeNull();
                    state.NextRestartAt!.Value.ShouldBe(now.AddSeconds(modelDisabled ? 80 : 160), TimeSpan.FromMilliseconds(1));
                    state.RestartBackoffFailures.ShouldBe(modelDisabled ? 4 : 5);
                    state.ContinuityResumeFailures.ShouldBe(4);
                    state.AutomaticFreshConsumed.ShouldBeFalse();
                    state.AutomaticResumeFailures.ShouldBe(3);
                    (await verify.Agents.FindAsync(agent.Id))!.PersistentSessionId.ShouldBe(oldId.ToString("D"));
                    (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == agent.Id)).ShouldBe(1);
                    if (modelDisabled)
                    {
                        var wait = await verify.CapacityRecoveryWaits.SingleAsync(w => w.AgentId == agent.Id);
                        if (!capacity) wait.State.ShouldBe(CapacityRecoveryWaitState.WaitingForHold);
                        (await verify.CapacityRecoveryWaitHolds.AnyAsync(l => l.WaitId == wait.Id && l.HoldId == holdId)).ShouldBeTrue();
                    }
                    else
                        (await verify.AgentIncidents.CountAsync(i => i.AgentId == agent.Id && i.Kind == AgentIncidentKind.StartFailure)).ShouldBe(1);
                }
                // The repeated-resume hold bypass cannot make the failed fresh attempt due now.
                await h.Supervisor().TickAsync(default);
                probes.ShouldBe(modelDisabled ? 2 : 3, "a tick before the deadline must only probe, never attempt Start");
                fresh.Started.ShouldBeFalse();
                if (!modelDisabled && !capacity)
                {
                    h.Clock.Advance(TimeSpan.FromSeconds(161));
                    supervisorClock.Advance(TimeSpan.FromSeconds(161));
                    await h.Supervisor().TickAsync(default);
                    await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                    fresh.StartedSessionId.ShouldNotBeNull("the infrastructure failure must remain recoverable");
                    fresh.StartedSessionId.ShouldNotBe(oldId);
                }
            }
            finally { await AgentSupervisionTests.CleanupAsync(root); }
        }
    }
}
