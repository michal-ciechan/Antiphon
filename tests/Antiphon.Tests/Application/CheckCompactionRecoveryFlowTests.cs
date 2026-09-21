using Antiphon.Server.Application.Services;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class CheckCompactionRecoveryFlowTests
{
    [Test]
    public async Task Held_recovery_releases_the_expired_untyped_occupant_without_generic_kill()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var accepted = SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc));
        var now = accepted.AddHours(2);
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        await using (var db = NewDb(schema.ConnectionString))
        {
            db.Agents.Add(new Agent
            {
                Id = agentId, Name = "check", Slug = "check-" + agentId.ToString("N")[..8],
                WorkingDirectory = Path.GetTempPath(), Kind = AgentKind.ClaudeCode, AlwaysOn = true,
                Status = AgentStatus.Running, StandingSpecialistRole = AgentTaskRole.Check,
                StandingSpecialistOwnerId = agentId, PersistentSessionId = sessionId.ToString("D"),
                CreatedAt = accepted, UpdatedAt = accepted,
            });
            db.AgentSessions.Add(new AgentSession
            {
                Id = sessionId, StandingAgentId = agentId, DefinitionName = "claude",
                AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running, Cwd = Path.GetTempPath(),
                CreatedAt = accepted, StartedAt = accepted, LastSeenAt = now,
            });
            db.AgentSupervisionStates.Add(new AgentSupervisionState
            {
                AgentId = agentId,
                ActiveCompactionRecoveryId = episodeId,
                LastAutomaticCompactionRestartAt = now.AddHours(-1),
                CompactionRestartReceiptEligible = false,
                UpdatedAt = now,
            });
            db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
            {
                Id = episodeId, PhysicalAgentId = agentId, SessionId = sessionId,
                AcceptedStartedAt = accepted, BoundaryIdentity = "boundary-1",
                BoundaryCreatedAt = accepted, ContinuationCreatedAt = accepted,
                ConfiguredThresholdMinutes = 10, DetectedAt = now.AddMinutes(-11),
                State = CheckCompactionRecoveryState.Confirmed,
            });
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "check", Goal = "look",
                Role = AgentTaskRole.Check, Kind = AgentTaskKind.Worker,
                Status = AgentTaskStatus.Dispatched, AgentId = agentId, AgentSessionId = sessionId,
                WorkingDirectory = Path.GetTempPath(), CreatedAt = now.AddMinutes(-11),
                DispatchedAt = now.AddMinutes(-11),
            });
            await db.SaveChangesAsync();

            var runner = new FakeSessionRunnerClient();
            await new CheckCompactionContinuationService(
                db, new FixedClock(now), Options.Create(new DelegationSettings()),
                new CheckCompactionContinuationGate(),
                NullLogger<CheckCompactionContinuationService>.Instance,
                runner).SweepAsync(CancellationToken.None);
            runner.KillCalls.ShouldBe(0);
            runner.CompactionStops.Count.ShouldBe(0);
        }

        await using var verify = NewDb(schema.ConnectionString);
        var task = await verify.AgentTasks.SingleAsync();
        task.Status.ShouldBe(AgentTaskStatus.Failed);
        task.FailureCode.ShouldBe(AgentTaskFailureCode.CompactionContinuationStalled);
        (await verify.CheckCompactionRecoveries.SingleAsync()).State
            .ShouldBe(CheckCompactionRecoveryState.NeedsDecision);
    }

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
