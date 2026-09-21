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
public class CheckCompactionContinuationTests
{
    [Test]
    public async Task Successful_unchanged_pull_confirms_but_failed_pull_does_not()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedAsync(schema.ConnectionString);
        await using var db = NewDb(schema.ConnectionString);
        var runner = Runner(world, success: false);
        var service = Service(db, world.Now, runner, new RecordingResume(world.SessionId, world.Generation2));
        await service.SweepAsync(CancellationToken.None);

        (await db.CheckCompactionRecoveries.CountAsync()).ShouldBe(0);
        runner.CompactionStops.Count.ShouldBe(0);

        await using var confirmed = NewDb(schema.ConnectionString);
        var live = Runner(world, success: true);
        await Service(confirmed, world.Now, live, new RecordingResume(world.SessionId, world.Generation2))
            .SweepAsync(CancellationToken.None);
        var episode = await confirmed.CheckCompactionRecoveries.SingleAsync();
        episode.State.ShouldBe(CheckCompactionRecoveryState.ResumeReserved);
        episode.AttemptId.ShouldNotBeNull();
        live.CompactionStops.Count.ShouldBe(1);
        live.KillCalls.ShouldBe(0);
        (await confirmed.AgentSessions.SingleAsync()).TerminationSource
            .ShouldBe(SessionTerminationSource.CompactionContinuationRecovery);
    }

    [Test]
    public async Task Confirmed_episode_survives_restart_and_incident_pruning()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedAsync(schema.ConnectionString);
        await using var first = NewDb(schema.ConnectionString);
        await Service(first, world.Now, Runner(world, success: true), new RecordingResume(world.SessionId, world.Generation2))
            .SweepAsync(CancellationToken.None);
        var episodeId = (await first.CheckCompactionRecoveries.SingleAsync()).Id;

        await using var prune = NewDb(schema.ConnectionString);
        await prune.AgentIncidents.ExecuteDeleteAsync();
        var kept = await prune.CheckCompactionRecoveries.SingleAsync();
        kept.Id.ShouldBe(episodeId);
        kept.State.ShouldBe(CheckCompactionRecoveryState.ResumeReserved);
    }

    private static FakeSessionRunnerClient Runner(World world, bool success) => new()
    {
        AdvertiseCompactionStop = true,
        GetOverride = (_, _) => Task.FromResult(new SessionRunnerSessionDto(
            world.SessionId, 1, world.Accepted, "Running", null, AgentExitReason.Unknown, 4,
            AcceptedStartedAt: world.Accepted)),
        CompactionObservation = success
            ? new CompactionTailObservation(
                CompactionObservationStatuses.Success, true, 80, "bind-1", 3, 4, "boundary-1", "cont-1")
            : CompactionTailObservation.Unavailable(),
        CompactionStopResult = new CompactionContinuationStopResult(
            world.SessionId, Guid.NewGuid(), true, CompactionStopOutcomes.Exited, world.Accepted),
    };

    private static CheckCompactionContinuationService Service(
        AppDbContext db, DateTime now, FakeSessionRunnerClient runner, RecordingResume resume) =>
        new(db, new FixedClock(now), Options.Create(new DelegationSettings()),
            new CheckCompactionContinuationGate(),
            NullLogger<CheckCompactionContinuationService>.Instance,
            runner, resume);

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private static async Task<World> SeedAsync(string connectionString)
    {
        var accepted = SessionGeneration.Normalize(new DateTime(2026, 9, 14, 22, 50, 7, DateTimeKind.Utc));
        var promptAt = accepted.AddMinutes(1);
        var boundaryAt = promptAt.AddSeconds(3);
        var continuationAt = promptAt.AddSeconds(4);
        var now = continuationAt.AddMinutes(10);
        var sessionId = Guid.NewGuid();
        var agentId = Guid.NewGuid();
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
            Entry(sessionId, 10, TranscriptKinds.TurnEnd, null, accepted),
            Entry(sessionId, 11, TranscriptKinds.UserPrompt, "check " + checkId.ToString("D"), promptAt),
            Entry(sessionId, 12, TranscriptKinds.CompactBoundary, "Context compacted (auto)", boundaryAt, "boundary-1"),
            Entry(sessionId, 13, TranscriptKinds.UserPrompt,
                TranscriptKinds.CompactionContinuationPromptPrefix + " summary", continuationAt, "cont-1"));
        await db.SaveChangesAsync();
        return new World(sessionId, accepted, SessionGeneration.Next(accepted, now), now);
    }

    private static TranscriptEntry Entry(
        Guid sessionId, long sequence, string kind, string? text, DateTime at, string? uuid = null) => new()
    {
        Id = Guid.NewGuid(),
        AgentSessionId = sessionId,
        Sequence = sequence,
        Kind = kind,
        Text = text,
        Timestamp = at,
        CreatedAt = at,
        Uuid = uuid,
    };

    private sealed record World(Guid SessionId, DateTime Accepted, DateTime Generation2, DateTime Now);

    private sealed class RecordingResume(Guid sessionId, DateTime generation) : ICompactionContinuationResume
    {
        public Task<CompactionResumeResult> ResumeAsync(Guid episodeId, CancellationToken ct) =>
            Task.FromResult(new CompactionResumeResult(true, sessionId, generation, "reserved"));
    }

    private sealed class FixedClock(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now, TimeSpan.Zero);
    }
}
