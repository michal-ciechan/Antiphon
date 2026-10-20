using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingRestartEscalationTests
{
    [Test]
    public async Task C510_S2_refused_fresh_stays_held_across_later_ticks()
    {
        foreach (var capacity in new[] { false, true })
        foreach (var alreadyHeld in new[] { false, true })
        {
            await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
            var root = AgentSupervisionTests.NewTempRoot();
            try
            {
                var fresh = new FakeAgentProtocolAdapter();
                await using var h = AgentSupervisionTests.BuildHarness(root, [fresh],
                    new SupervisionSettings { CapacityRecovery = new CapacityRecoverySettings
                        { Enabled = capacity, JitterSeconds = 0 } },
                    definitionKind: "ClaudeCode", connectionString: schema.ConnectionString);
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
                var oldId = Guid.NewGuid();
                var messageId = Guid.NewGuid();
                var generation = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
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
                        NextRestartAt = h.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1), UpdatedAt = generation });
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage { Id = messageId,
                        AgentSessionId = oldId, Body = "Retain attempted input", Sequence = 7,
                        CreatedAt = generation, Origin = QueuedMessageOrigin.Ui,
                        DeliveryAttempts = 1, LastDeliveryBaselineSequence = 42 });
                    await db.SaveChangesAsync();
                }
                if (capacity)
                {
                    var recovery = h.Provider.GetRequiredService<CapacityRecoveryService>();
                    await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                        $"agent:{agent.Id:N}", CapacityWaitConsumerKind.StandingStart,
                        executionKind: AgentKind.ClaudeCode, holdAlreadyCleared: true,
                        agentId: agent.Id, sessionId: oldId), default);
                    await recovery.ReconcileAsync(default);
                }
                var probes = 0;
                h.Runner.ListOverride = _ =>
                {
                    Interlocked.Increment(ref probes);
                    return Task.FromResult<IReadOnlyList<Antiphon.Server.Application.Dtos.SessionRunnerSessionDto>>([]);
                };
                await h.Supervisor().TickAsync(default);
                probes.ShouldBe(2, "one supervisor probe and one real Start preflight");
                string.Join(Environment.NewLine, h.SupervisorLog).ShouldContain("Resolve attempted pending input");
                for (var tick = 0; tick < 3; tick++)
                {
                    h.Clock.Advance(TimeSpan.FromDays(1));
                    await h.Supervisor().TickAsync(default);
                }
                probes.ShouldBe(5, $"capacity={capacity}, held={alreadyHeld}: later ticks must not enter Start again");
                await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
                var held = (await verify.AgentSupervisionStates.FindAsync(agent.Id))!;
                held.ContinuityHeldAt.ShouldNotBeNull();
                if (alreadyHeld) held.ContinuityHeldAt!.Value.ShouldBe(generation, TimeSpan.FromMilliseconds(1));
                held.ContinuitySessionId.ShouldBe(oldId);
                held.NextRestartAt.ShouldBeNull();
                held.AutomaticFreshConsumed.ShouldBeFalse("a refused reservation did not spend the allowance");
                held.AutomaticResumeFailures.ShouldBe(3);
                (await verify.Agents.FindAsync(agent.Id))!.PersistentSessionId.ShouldBe(oldId.ToString("D"));
                (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == agent.Id)).ShouldBe(1);
                var message = (await verify.SessionQueuedMessages.FindAsync(messageId))!;
                message.AgentSessionId.ShouldBe(oldId);
                message.Status.ShouldBe(QueuedMessageStatus.Pending);
                message.DeliveryAttempts.ShouldBe(1);
                message.LastDeliveryBaselineSequence.ShouldBe(42);
                fresh.Started.ShouldBeFalse();
            }
            finally { await AgentSupervisionTests.CleanupAsync(root); }
        }
    }
}
