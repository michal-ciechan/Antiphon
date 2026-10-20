using Antiphon.Server.Application.Interfaces;
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
    [Arguments("ClaudeCode")]
    [Arguments("Grok")]
    public async Task C510_S2_capacity_grant_is_required_and_follows_fresh(string kindName)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            var fresh = new FakeAgentProtocolAdapter();
            var kind = Enum.Parse<AgentKind>(kindName);
            await using var h = AgentSupervisionTests.BuildHarness(root, [fresh],
                new SupervisionSettings { CapacityRecovery = new CapacityRecoverySettings { Enabled = true, JitterSeconds = 0 } },
                definitionKind: kindName, connectionString: schema.ConnectionString);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var oldId = Guid.NewGuid();
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                var owner = (await db.Agents.FindAsync(agent.Id))!;
                owner.Kind = kind;
                owner.PersistentSessionId = oldId.ToString("D");
                var generation = DateTime.UtcNow.AddMinutes(-10);
                db.AgentSessions.Add(new AgentSession { Id = oldId, StandingAgentId = agent.Id,
                    AgentKind = kind, DefinitionName = "fake", Cwd = owner.WorkingDirectory,
                    Status = SessionStatus.Failed, CreatedAt = generation, StartedAt = generation });
                db.AgentSupervisionStates.Add(new AgentSupervisionState { AgentId = agent.Id,
                    AutomaticResumeFailures = 3, ContinuityResumeFailures = 4, RestartBackoffFailures = 4,
                    LastAutomaticResumeSessionId = oldId, LastAutomaticResumeStartedAt = generation,
                    LastObservedRestartSessionId = oldId, LastObservedRestartStartedAt = generation,
                    NextRestartAt = DateTime.UtcNow.AddSeconds(-1), UpdatedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }
            var recovery = h.Provider.GetRequiredService<CapacityRecoveryService>();
            var wait = await recovery.EnsureWaitAsync(CapacityRecoveryTestSupport.Registration(
                $"agent:{agent.Id:N}", CapacityWaitConsumerKind.StandingStart, executionKind: kind,
                holdAlreadyCleared: true, agentId: agent.Id, sessionId: oldId), default);
            await h.Supervisor().TickAsync(default);
            fresh.Started.ShouldBeFalse("no provider grant yet");
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
                (await db.AgentSupervisionStates.FindAsync(agent.Id))!.AutomaticFreshConsumed.ShouldBeFalse();
            await recovery.ReconcileAsync(default);
            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
            var newId = Guid.Parse((await verify.Agents.FindAsync(agent.Id))!.PersistentSessionId!);
            newId.ShouldNotBe(oldId, string.Join(Environment.NewLine, h.SupervisorLog));
            fresh.StartedSessionId.ShouldBe(newId);
            fresh.StartedArgs.ShouldNotContain("--resume");
            (await verify.AgentSupervisionStates.FindAsync(agent.Id))!.AutomaticFreshConsumed.ShouldBeTrue();
            (await verify.AgentSessions.FindAsync(newId))!.CapacityRecoveryActionKey.ShouldBe(wait.ActionKey);
            var admitted = (await verify.CapacityRecoveryWaits.FindAsync(wait.Id))!;
            admitted.AdmissionCount.ShouldBe(1);
            admitted.SessionId.ShouldBe(newId);
            // Acceptance is not a provider prompt receipt.
            admitted.ConfirmedPromptSequence.ShouldBeNull();
            (await verify.AgentSessions.FindAsync(oldId)).ShouldNotBeNull();
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }
}
