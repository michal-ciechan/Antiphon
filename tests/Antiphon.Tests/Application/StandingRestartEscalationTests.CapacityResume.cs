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
    public async Task C510_S2_cleared_model_hold_grant_starts_resume_on_the_next_tick()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            var resume = new FakeAgentProtocolAdapter();
            var supervisorClock = new FakeTimeProvider(DateTimeOffset.UtcNow);
            var settings = new SupervisionSettings
            {
                CapacityRecovery = new CapacityRecoverySettings { Enabled = true, JitterSeconds = 0 },
            };
            await using var h = AgentSupervisionTests.BuildHarness(root, [resume], settings,
                includeModelAvailability: true, definitionKind: "ClaudeCode",
                connectionString: schema.ConnectionString, supervisorTimeProvider: supervisorClock);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var sessionId = Guid.NewGuid();
            var now = supervisorClock.GetUtcNow().UtcDateTime;
            var generation = now.AddMinutes(-10);
            var holdId = Guid.NewGuid();
            const int backoffFailures = 5;
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                var owner = (await db.Agents.FindAsync(agent.Id))!;
                owner.Kind = AgentKind.ClaudeCode;
                owner.PersistentSessionId = sessionId.ToString("D");
                var session = new AgentSession
                {
                    Id = sessionId, StandingAgentId = agent.Id, AgentKind = AgentKind.ClaudeCode,
                    DefinitionName = "fake", Cwd = owner.WorkingDirectory, Status = SessionStatus.Failed,
                    CreatedAt = generation, StartedAt = generation,
                };
                var state = new AgentSupervisionState
                {
                    AgentId = agent.Id, AutomaticResumeFailures = 0, RestartBackoffFailures = backoffFailures,
                    NextRestartAt = now.AddSeconds(-1), UpdatedAt = generation,
                };
                new StandingRestartEscalationPolicy(settings, new DelegationSettings())
                    .CanReserve(owner, state, session, AgentKind.ClaudeCode)
                    .ShouldBeFalse("this witness is the resume path, not a fresh reservation");
                db.AgentSessions.Add(session);
                db.AgentSupervisionStates.Add(state);
                db.ModelAvailabilityHolds.Add(CapacityRecoveryTestSupport.Hold(
                    holdId, alias: "*", until: now.AddHours(1)));
                await db.SaveChangesAsync();
            }

            await h.Supervisor().TickAsync(default);
            resume.Started.ShouldBeFalse("the active model hold must refuse the first start");
            await using (var held = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                var state = (await held.AgentSupervisionStates.FindAsync(agent.Id))!;
                state.RestartBackoffFailures.ShouldBe(backoffFailures);
                (await held.CapacityRecoveryWaits.SingleAsync(w => w.AgentId == agent.Id))
                    .State.ShouldBe(CapacityRecoveryWaitState.WaitingForHold);
                var row = await held.ModelAvailabilityHolds.SingleAsync(x => x.Id == holdId);
                row.ClearedAt = now;
                row.ClearCause = ModelAvailabilityClearCause.Expired;
                row.ReleasePendingAt = now;
                await held.SaveChangesAsync();
            }

            var recovery = h.Provider.GetRequiredService<CapacityRecoveryService>();
            await recovery.ReconcileAsync(default);
            await using (var granted = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                var wait = await granted.CapacityRecoveryWaits.SingleAsync(w => w.AgentId == agent.Id);
                wait.State.ShouldBe(CapacityRecoveryWaitState.ActionPending);
                (await granted.Set<CapacityRecoveryProviderState>().SingleAsync(s => s.Kind == AgentKind.ClaudeCode))
                    .GrantedWaitId.ShouldBe(wait.Id);
            }

            supervisorClock.GetUtcNow().UtcDateTime.ShouldBe(now, TimeSpan.FromMilliseconds(1));
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            supervisorClock.GetUtcNow().UtcDateTime.ShouldBe(now, TimeSpan.FromMilliseconds(1));
            resume.StartedSessionId.ShouldBe(sessionId,
                "a cleared hold's grant must resume on the next tick without waiting out Backoff(n>=5). "
                + string.Join(Environment.NewLine, h.SupervisorLog));
        }
        finally
        {
            await AgentSupervisionTests.CleanupAsync(root);
        }
    }
}
