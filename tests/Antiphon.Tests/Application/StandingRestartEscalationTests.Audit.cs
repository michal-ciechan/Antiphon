using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Tests.Agents;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

public partial class StandingRestartEscalationTests
{
    [Test]
    public async Task C510_S3_refused_fresh_hold_raises_one_error_alert()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            var fresh = new FakeAgentProtocolAdapter();
            await using (var h = Harness(root, schema, [fresh]))
            {
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
                var oldId = Guid.NewGuid();
                var messageId = Guid.NewGuid();
                var generation = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
                const string body = "CANARY-attempted-body";
                await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
                {
                    var owner = (await db.Agents.FindAsync(agent.Id))!;
                    owner.Kind = AgentKind.ClaudeCode;
                    owner.PersistentSessionId = oldId.ToString("D");
                    db.AgentSessions.Add(Session(oldId, agent.Id, owner.WorkingDirectory, generation));
                    db.AgentSupervisionStates.Add(Eligible(agent.Id, oldId, generation, h));
                    db.SessionQueuedMessages.Add(new SessionQueuedMessage
                    {
                        Id = messageId, AgentSessionId = oldId, Body = body, Sequence = 7,
                        CreatedAt = generation, Origin = QueuedMessageOrigin.Ui,
                        DeliveryAttempts = 1, LastDeliveryBaselineSequence = 42,
                    });
                    await db.SaveChangesAsync();
                }

                await h.Supervisor().TickAsync(default);
                for (var tick = 0; tick < 3; tick++)
                {
                    h.Clock.Advance(TimeSpan.FromDays(1));
                    await h.Supervisor().TickAsync(default);
                }

                await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
                await AssertRefusedHoldAsync(verify, agent.Id, oldId, messageId, body);
                fresh.Started.ShouldBeFalse();
            }

            await using var rebuilt = Harness(root, schema, []);
            await rebuilt.Supervisor().TickAsync(default);
            await using var again = CapacityRecoveryTestSupport.CreateContext(schema);
            var held = (await again.AgentSupervisionStates.FindAsync(
                (await again.Agents.SingleAsync(a => a.WorkingDirectory.StartsWith(root))).Id))!;
            await AssertRefusedHoldAsync(again, held.AgentId, oldId: held.ContinuitySessionId!.Value,
                messageId: (await again.SessionQueuedMessages.SingleAsync()).Id,
                body: "CANARY-attempted-body");
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C510_S3_fresh_episode_audits_once()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            Guid agentId;
            Guid oldId;
            var fresh = new FakeAgentProtocolAdapter();
            await using (var h = Harness(root, schema, [fresh]))
            {
                var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
                agentId = agent.Id;
                oldId = Guid.NewGuid();
                var generation = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
                await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
                {
                    var owner = (await db.Agents.FindAsync(agent.Id))!;
                    owner.Kind = AgentKind.ClaudeCode;
                    owner.PersistentSessionId = oldId.ToString("D");
                    db.AgentSessions.Add(Session(oldId, agent.Id, owner.WorkingDirectory, generation));
                    db.AgentSupervisionStates.Add(Eligible(agent.Id, oldId, generation, h));
                    await db.SaveChangesAsync();
                }

                await h.Supervisor().TickAsync(default);
                await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
                await AssertFreshAuditAsync(schema, agentId, oldId, fresh.StartedSessionId, queueIds: []);
                h.Clock.Advance(TimeSpan.FromDays(1));
                await h.Supervisor().TickAsync(default);
                await AssertFreshAuditAsync(schema, agentId, oldId, fresh.StartedSessionId, queueIds: []);
            }

            await using var rebuilt = Harness(root, schema, []);
            await rebuilt.Supervisor().TickAsync(default);
            await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
            var pointer = Guid.Parse((await verify.Agents.FindAsync(agentId))!.PersistentSessionId!);
            await AssertFreshAuditAsync(schema, agentId, oldId, pointer, queueIds: []);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    [Test]
    public async Task C510_S3_untouched_input_transfers_with_the_fresh_reservation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var root = AgentSupervisionTests.NewTempRoot();
        try
        {
            var fresh = new FakeAgentProtocolAdapter();
            await using var h = Harness(root, schema, [fresh]);
            var agent = await AgentSupervisionTests.CreateAlwaysOnAgentAsync(h, root);
            var oldId = Guid.NewGuid();
            var firstId = Guid.NewGuid();
            var secondId = Guid.NewGuid();
            const string firstBody = "CANARY-alpha-body";
            const string secondBody = "CANARY-beta-body";
            var generation = h.Clock.GetUtcNow().UtcDateTime.AddMinutes(-10);
            await using (var db = CapacityRecoveryTestSupport.CreateContext(schema))
            {
                var owner = (await db.Agents.FindAsync(agent.Id))!;
                owner.Kind = AgentKind.ClaudeCode;
                owner.PersistentSessionId = oldId.ToString("D");
                db.AgentSessions.Add(Session(oldId, agent.Id, owner.WorkingDirectory, generation));
                db.AgentSupervisionStates.Add(Eligible(agent.Id, oldId, generation, h));
                db.SessionQueuedMessages.AddRange(
                    Untouched(firstId, oldId, firstBody, 1, generation),
                    Untouched(secondId, oldId, secondBody, 2, generation));
                await db.SaveChangesAsync();
            }

            await h.Supervisor().TickAsync(default);
            await h.LaunchQueue.WaitForIdleAsync(TimeSpan.FromSeconds(15), default);
            fresh.StartedSessionId.ShouldNotBeNull(string.Join(Environment.NewLine, h.SupervisorLog));
            var newId = fresh.StartedSessionId!.Value;
            newId.ShouldNotBe(oldId);
            await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
            var moved = await verify.SessionQueuedMessages.OrderBy(m => m.Sequence).ThenBy(m => m.Id).ToListAsync();
            moved.Select(m => m.Id).ShouldBe([firstId, secondId]);
            moved.Select(m => m.Body).ShouldBe([firstBody, secondBody]);
            moved.ShouldAllBe(m => m.AgentSessionId == newId && m.Status == QueuedMessageStatus.Pending
                && m.DeliveryAttempts == 0 && m.LastDeliveryBaselineSequence == null);
            (await verify.AgentSessions.FindAsync(oldId)).ShouldNotBeNull();
            await AssertFreshAuditAsync(schema, agent.Id, oldId, newId, [firstId, secondId]);
            var audit = await verify.AgentIncidents.SingleAsync(i => i.AgentId == agent.Id
                && i.Kind == AgentIncidentKind.StandingAutomaticFreshStarted);
            audit.Message.ShouldNotContain(firstBody);
            audit.Message.ShouldNotContain(secondBody);
        }
        finally { await AgentSupervisionTests.CleanupAsync(root); }
    }

    private static AgentSupervisionTests.Harness Harness(
        string root, IsolatedTestSchema schema, IReadOnlyList<Antiphon.Server.Application.Interfaces.IAgentProtocolAdapter> adapters) =>
        AgentSupervisionTests.BuildHarness(root, adapters, new SupervisionSettings(),
            definitionKind: "ClaudeCode", connectionString: schema.ConnectionString);

    private static AgentSession Session(Guid id, Guid agentId, string cwd, DateTime generation) => new()
    {
        Id = id, StandingAgentId = agentId, AgentKind = AgentKind.ClaudeCode, DefinitionName = "fake",
        Cwd = cwd, Status = SessionStatus.Failed, CreatedAt = generation, StartedAt = generation,
    };

    private static AgentSupervisionState Eligible(
        Guid agentId, Guid sessionId, DateTime generation, AgentSupervisionTests.Harness harness) => new()
    {
        AgentId = agentId, AutomaticResumeFailures = 3, ContinuityResumeFailures = 4, RestartBackoffFailures = 4,
        LastAutomaticResumeSessionId = sessionId, LastAutomaticResumeStartedAt = generation,
        LastObservedRestartSessionId = sessionId, LastObservedRestartStartedAt = generation,
        NextRestartAt = harness.Clock.GetUtcNow().UtcDateTime.AddSeconds(-1), UpdatedAt = generation,
    };

    private static SessionQueuedMessage Untouched(Guid id, Guid sessionId, string body, long sequence, DateTime created) =>
        new()
        {
            Id = id, AgentSessionId = sessionId, Body = body, Sequence = sequence,
            CreatedAt = created, Origin = QueuedMessageOrigin.Ui, Status = QueuedMessageStatus.Pending,
        };

    private static async Task AssertRefusedHoldAsync(
        Antiphon.Server.Infrastructure.Data.AppDbContext verify, Guid agentId, Guid oldId, Guid messageId, string body)
    {
        var held = (await verify.AgentSupervisionStates.FindAsync(agentId))!;
        held.ContinuityHeldAt.ShouldNotBeNull();
        held.ContinuityReason.ShouldBe(StandingContinuityReason.AutomaticFreshRefused);
        held.ContinuitySessionId.ShouldBe(oldId);
        held.NextRestartAt.ShouldBeNull();
        held.AutomaticFreshConsumed.ShouldBeFalse();
        held.AutomaticFreshAlertedHoldAt.ShouldNotBeNull();
        held.AutomaticFreshAlertedHoldAt!.Value.ShouldBe(held.ContinuityHeldAt!.Value, TimeSpan.FromMilliseconds(1));
        (await verify.Agents.FindAsync(agentId))!.PersistentSessionId.ShouldBe(oldId.ToString("D"));
        (await verify.AgentSessions.CountAsync(s => s.StandingAgentId == agentId)).ShouldBe(1);
        var message = (await verify.SessionQueuedMessages.SingleAsync(m => m.Id == messageId))!;
        message.AgentSessionId.ShouldBe(oldId);
        message.Body.ShouldBe(body);
        message.DeliveryAttempts.ShouldBe(1);
        message.LastDeliveryBaselineSequence.ShouldBe(42);
        var incidents = await verify.AgentIncidents.Where(i => i.AgentId == agentId
            && i.Kind == AgentIncidentKind.StandingAutomaticFreshRefused).ToListAsync();
        var incident = incidents.ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Error);
        incident.SessionId.ShouldBe(oldId);
        incident.Message.ShouldContain(agentId.ToString("D"));
        incident.Message.ShouldContain(oldId.ToString("D"));
        incident.Message.ShouldContain(messageId.ToString("D"));
        incident.Message.ShouldContain("prior conversation retained");
        incident.Message.ShouldNotContain(body);
        incident.Message.ShouldNotContain("recovered");
        var alerts = await verify.Alerts.Where(a => a.AgentId == agentId && a.Severity == AlertSeverity.Error).ToListAsync();
        var alert = alerts.ShouldHaveSingleItem();
        alert.Source.ShouldBe("supervisor");
        alert.Detail.ShouldContain(agentId.ToString("D"));
        alert.Detail.ShouldContain(oldId.ToString("D"));
        alert.Detail.ShouldContain(messageId.ToString("D"));
        alert.Detail.ShouldNotContain(body);
    }

    private static async Task AssertFreshAuditAsync(
        IsolatedTestSchema schema, Guid agentId, Guid oldId, Guid? newId, IReadOnlyList<Guid> queueIds)
    {
        newId.ShouldNotBeNull();
        newId.ShouldNotBe(oldId);
        await using var verify = CapacityRecoveryTestSupport.CreateContext(schema);
        var state = (await verify.AgentSupervisionStates.FindAsync(agentId))!;
        state.AutomaticFreshConsumed.ShouldBeTrue();
        (await verify.Agents.FindAsync(agentId))!.PersistentSessionId.ShouldBe(newId!.Value.ToString("D"));
        (await verify.AgentSessions.FindAsync(oldId)).ShouldNotBeNull();
        var incidents = await verify.AgentIncidents.Where(i => i.AgentId == agentId
            && i.Kind == AgentIncidentKind.StandingAutomaticFreshStarted).ToListAsync();
        var incident = incidents.ShouldHaveSingleItem();
        incident.Severity.ShouldBe(AlertSeverity.Warning);
        incident.SessionId.ShouldBe(newId);
        incident.Message.ShouldContain(agentId.ToString("D"));
        incident.Message.ShouldContain(oldId.ToString("D"));
        incident.Message.ShouldContain(newId.Value.ToString("D"));
        incident.Message.ShouldContain("count 3");
        incident.Message.ShouldContain("threshold 3");
        incident.Message.ShouldContain("reason repeated-resume");
        incident.Message.ShouldContain("fresh launch queued");
        incident.Message.ShouldContain("prior conversation retained");
        incident.Message.ShouldNotContain("recovered");
        var queues = queueIds.Count == 0 ? "none" : string.Join(",", queueIds.Select(id => id.ToString("D")));
        incident.Message.ShouldContain($"queues {queues}");
        var alerts = await verify.Alerts.Where(a => a.AgentId == agentId
            && a.Detail != null && a.Detail.Contains("StandingAutomaticFreshStarted")).ToListAsync();
        var alert = alerts.ShouldHaveSingleItem();
        alert.Severity.ShouldBe(AlertSeverity.Warning);
        alert.Source.ShouldBe("supervisor");
        alert.SessionId.ShouldBe(newId);
        alert.Detail.ShouldContain("fresh launch queued");
        alert.Detail.ShouldNotContain("recovered");
    }
}
