using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
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
public class CheckCompactionAutomaticRestartTests
{
    [Test]
    public async Task Unsupported_stop_never_falls_back_or_resumes()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using var db = NewDb(schema.ConnectionString);
        var runner = new FakeSessionRunnerClient
        {
            GetOverride = (_, _) => Task.FromResult(new SessionRunnerSessionDto(
                world.SessionId, 1, world.Accepted, "Running", null, AgentExitReason.Unknown, 1,
                AcceptedStartedAt: world.Accepted)),
            CompactionObservation = new CompactionTailObservation(
                CompactionObservationStatuses.Success, true, 1, "bind-1", 1, 1, world.Boundary, world.Continuation),
        };
        var resume = new CountingResume();
        await Service(db, world.Now, runner, resume).SweepAsync(CancellationToken.None);

        runner.CompactionStops.Count.ShouldBe(1);
        runner.KillCalls.ShouldBe(0);
        resume.Calls.ShouldBe(0);
        (await db.CheckCompactionRecoveries.SingleAsync()).State.ShouldBe(CheckCompactionRecoveryState.NeedsDecision);
    }

    [Test]
    public async Task One_stop_is_reconciled_without_a_second_launch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        var resume = new CountingResume();
        await using (var db = NewDb(schema.ConnectionString))
        {
            var runner = Stopper(world);
            await Service(db, world.Now, runner, resume).SweepAsync(CancellationToken.None);
            runner.CompactionStops.Count.ShouldBe(1);
            resume.Calls.ShouldBe(1);
        }

        await using var again = NewDb(schema.ConnectionString);
        var second = Stopper(world);
        await Service(again, world.Now.AddMinutes(2), second, resume).SweepAsync(CancellationToken.None);
        second.CompactionStops.Count.ShouldBe(0);
        resume.Calls.ShouldBe(1);
        (await again.CheckCompactionRecoveries.SingleAsync()).State.ShouldBe(CheckCompactionRecoveryState.ResumeReserved);
    }

    private static FakeSessionRunnerClient Stopper(World world) => new()
    {
        AdvertiseCompactionStop = true,
        GetOverride = (_, _) => Task.FromResult(new SessionRunnerSessionDto(
            world.SessionId, 1, world.Accepted, "Running", null, AgentExitReason.Unknown, 1,
            AcceptedStartedAt: world.Accepted)),
        CompactionObservation = new CompactionTailObservation(
            CompactionObservationStatuses.Success, true, 1, "bind-1", 1, 1, world.Boundary, world.Continuation),
        CompactionStopResult = new CompactionContinuationStopResult(
            world.SessionId, Guid.NewGuid(), true, CompactionStopOutcomes.Exited, world.Accepted),
    };

    private static CheckCompactionContinuationService Service(
        AppDbContext db, DateTime now, FakeSessionRunnerClient runner, CountingResume resume) =>
        new(db, new FixedClock(now), Options.Create(new DelegationSettings()),
            new CheckCompactionContinuationGate(),
            NullLogger<CheckCompactionContinuationService>.Instance,
            runner, resume);

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static async Task<World> SeedConfirmedAsync(string connectionString)
    {
        var accepted = SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc));
        var promptAt = accepted.AddMinutes(1);
        var now = promptAt.AddSeconds(4).AddMinutes(10);
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var checkId = Guid.NewGuid();
        await using var db = NewDb(connectionString);
        db.Agents.Add(new Agent
        {
            Id = agentId,
            Name = "check",
            Slug = "check-" + agentId.ToString("N")[..8],
            WorkingDirectory = Path.GetTempPath(),
            Kind = AgentKind.ClaudeCode,
            AlwaysOn = true,
            Status = AgentStatus.Running,
            StandingSpecialistRole = AgentTaskRole.Check,
            StandingSpecialistOwnerId = agentId,
            PersistentSessionId = sessionId.ToString("D"),
            CreatedAt = accepted,
            UpdatedAt = accepted,
        });
        db.AgentSessions.Add(new AgentSession
        {
            Id = sessionId,
            StandingAgentId = agentId,
            DefinitionName = "claude",
            AgentKind = AgentKind.ClaudeCode,
            Status = SessionStatus.Running,
            Cwd = Path.GetTempPath(),
            CreatedAt = accepted,
            StartedAt = accepted,
            LastSeenAt = now,
        });
        db.AgentSupervisionStates.Add(new AgentSupervisionState
        {
            AgentId = agentId,
            ActiveCompactionRecoveryId = episodeId,
            UpdatedAt = now,
        });
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId,
            PhysicalAgentId = agentId,
            SessionId = sessionId,
            AcceptedStartedAt = accepted,
            BoundaryIdentity = "boundary-1",
            NativeContinuationIdentity = "cont-1",
            BoundaryCreatedAt = promptAt.AddSeconds(3),
            ContinuationCreatedAt = promptAt.AddSeconds(4),
            ConfiguredThresholdMinutes = 10,
            DetectedAt = promptAt.AddSeconds(4),
            State = CheckCompactionRecoveryState.Confirmed,
            ObservationBindingIdentity = "bind-1",
            ObservationTranscriptRevision = 1,
            ObservationOutputRevision = 1,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = checkId,
            RootTaskId = checkId,
            Title = "check",
            Goal = "look",
            Role = AgentTaskRole.Check,
            Kind = AgentTaskKind.Worker,
            Status = AgentTaskStatus.Succeeded,
            AgentId = agentId,
            AgentSessionId = sessionId,
            WorkingDirectory = Path.GetTempPath(),
            CreatedAt = promptAt,
            CompletedAt = promptAt,
        });
        db.TranscriptEntries.AddRange(
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 10,
                Kind = TranscriptKinds.TurnEnd, Timestamp = accepted, CreatedAt = accepted,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 11,
                Kind = TranscriptKinds.UserPrompt, Text = "check " + checkId.ToString("D"),
                Timestamp = promptAt, CreatedAt = promptAt,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 12,
                Kind = TranscriptKinds.CompactBoundary, Text = "Context compacted (auto)",
                Timestamp = promptAt.AddSeconds(3), CreatedAt = promptAt.AddSeconds(3), Uuid = "boundary-1",
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 13,
                Kind = TranscriptKinds.UserPrompt,
                Text = TranscriptKinds.CompactionContinuationPromptPrefix + " summary",
                Timestamp = promptAt.AddSeconds(4), CreatedAt = promptAt.AddSeconds(4), Uuid = "cont-1",
            });
        await db.SaveChangesAsync();
        return new World(sessionId, accepted, now, "boundary-1", "cont-1");
    }

    private sealed record World(Guid SessionId, DateTime Accepted, DateTime Now, string Boundary, string Continuation);

    private sealed class CountingResume : ICompactionContinuationResume
    {
        public int Calls { get; private set; }

        public Task<CompactionResumeResult> ResumeAsync(Guid episodeId, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(new CompactionResumeResult(true, null, null, "reserved"));
        }
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
