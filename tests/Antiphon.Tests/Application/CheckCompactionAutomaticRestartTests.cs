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

    // ---- D-2 holds: every clause of CheckCompactionScope.Refusal that BuildScopeAsync populates
    // from another subsystem's state. Each case leaves every OTHER gate eligible, so the only
    // thing between the seat and an automatic stop of a live Working session is the one hold under
    // test; the control is One_stop_is_reconciled_without_a_second_launch, which stops this same
    // world. A clause nothing populates reads false forever and stops anyway.

    [Test]
    public async Task Model_hold_vetoes_stop()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using (var seed = NewDb(schema.ConnectionString))
        {
            seed.ModelAvailabilityHolds.Add(new ModelAvailabilityHold
            {
                Id = Guid.NewGuid(),
                Kind = AgentKind.ClaudeCode,
                ModelAlias = ModelAlias.KindWide,
                Source = ModelAvailabilitySource.Manual,
                HitAt = world.Now.AddMinutes(-5),
                DisabledUntil = world.Now.AddHours(2),
                Reason = "operator paused the kind",
            });
            await seed.SaveChangesAsync();
        }

        await AssertVetoedAsync(schema.ConnectionString, world, "model");
    }

    [Test]
    public async Task Provider_hold_vetoes_stop()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using (var seed = NewDb(schema.ConnectionString))
        {
            // CARD-0412 admission clock: while the provider is not admitting for this kind, a
            // restart is precisely the launch it is holding back.
            seed.CapacityRecoveryProviderStates.Add(new CapacityRecoveryProviderState
            {
                Kind = AgentKind.ClaudeCode,
                NextAdmissionAt = world.Now.AddMinutes(30),
                UpdatedAt = world.Now,
            });
            await seed.SaveChangesAsync();
        }

        await AssertVetoedAsync(schema.ConnectionString, world, "provider");
    }

    [Test]
    public async Task Quota_hold_vetoes_stop()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using (var seed = NewDb(schema.ConnectionString))
        {
            // 2% left with three days to reset trips the shipped low-with-a-day-left rule - the
            // same reading that refuses a human Start with 409 subscription_quota_low.
            seed.SubscriptionUsageSamples.Add(new SubscriptionUsageSample
            {
                Id = Guid.NewGuid(),
                Provider = AgentKind.ClaudeCode,
                SubscriptionKey = AgentKind.ClaudeCode.ToString(),
                RemainingPercent = 2,
                ResetsAt = world.Now.AddDays(3),
                ObservedAt = world.Now.AddMinutes(-1),
                AgentSessionId = world.SessionId,
                SourceCommand = "/usage",
                ParseStatus = SubscriptionUsageParseStatus.Parsed,
            });
            await seed.SaveChangesAsync();
        }

        await AssertVetoedAsync(schema.ConnectionString, world, "quota");
    }

    [Test]
    public async Task Capacity_hold_vetoes_stop()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using (var seed = NewDb(schema.ConnectionString))
        {
            // A terminal provider-capacity recovery newer than the last prompt: the very state
            // SessionMessageQueueService.HasTerminalCapacityHoldAsync already holds queue rows on.
            seed.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(),
                AgentSessionId = world.SessionId,
                StubSequence = 14,
                Classification = ApiErrorClassification.Wall,
                DetectedAt = world.Now.AddMinutes(-2),
                ResolvedAt = world.Now.AddMinutes(-2),
                ResolvedReason = ApiErrorRecoveryReasons.WallParked,
            });
            await seed.SaveChangesAsync();
        }

        await AssertVetoedAsync(schema.ConnectionString, world, "capacity");
    }

    [Test]
    public async Task Authentication_refusal_vetoes_stop()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using (var seed = NewDb(schema.ConnectionString))
        {
            // NeedsHuman is authentication_failed / model_not_found: nothing automatic fixes it,
            // and relaunching into it is the one response that class exists to forbid.
            seed.ApiErrorRecoveries.Add(new ApiErrorRecovery
            {
                Id = Guid.NewGuid(),
                AgentSessionId = world.SessionId,
                StubSequence = 14,
                Classification = ApiErrorClassification.NeedsHuman,
                ApiErrorClass = "authentication_failed",
                DetectedAt = world.Now.AddMinutes(-2),
            });
            await seed.SaveChangesAsync();
        }

        await AssertVetoedAsync(schema.ConnectionString, world, "authentication");
    }

    [Test]
    public async Task Unproven_standing_owner_never_restarts()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using (var seed = NewDb(schema.ConnectionString))
        {
            // The standing-ownership pointer now names a different physical seat. This seat's own
            // PersistentSessionId still points here, and that ambiguity must not read as license.
            await seed.AgentSessions.Where(s => s.Id == world.SessionId)
                .ExecuteUpdateAsync(u => u.SetProperty(s => s.StandingAgentId, Guid.NewGuid()));
        }

        await AssertVetoedAsync(schema.ConnectionString, world, "ownership");
    }

    [Test]
    public async Task Human_turn_vetoes_automatic_restart()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var world = await SeedConfirmedAsync(schema.ConnectionString);
        await using (var seed = NewDb(schema.ConnectionString))
        {
            // The owning prompt was delivered from a HUMAN queue row, not by the Check dispatcher.
            // It still carries the Check task id, so correlation alone still says yes; human
            // origin is the half that has to say no.
            seed.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(),
                AgentSessionId = world.SessionId,
                Body = world.CheckPrompt,
                Origin = QueuedMessageOrigin.Ui,
                Status = QueuedMessageStatus.Sent,
                CreatedAt = world.Accepted.AddMinutes(1),
                SentAt = world.Accepted.AddMinutes(1),
                DeliveryVerdict = DeliveryVerdict.Delivered,
            });
            await seed.SaveChangesAsync();
        }

        await AssertVetoedAsync(schema.ConnectionString, world, "owning-prompt");
    }

    /// <summary>
    /// No conditional stop, no generic kill, no resume, the live session still Running, and the
    /// episode parked for a human with the refusing clause named. Asserting the REASON, not merely
    /// that nothing happened, is what stops one clause's test passing on another clause's veto.
    /// </summary>
    private static async Task AssertVetoedAsync(string connectionString, World world, string reason)
    {
        await using var db = NewDb(connectionString);
        var runner = Stopper(world);
        var resume = new CountingResume();
        await Service(db, world.Now, runner, resume).SweepAsync(CancellationToken.None);

        runner.CompactionStops.Count.ShouldBe(0, $"'{reason}' must veto the conditional stop");
        runner.KillCalls.ShouldBe(0);
        resume.Calls.ShouldBe(0);
        var episode = await db.CheckCompactionRecoveries.SingleAsync();
        episode.State.ShouldBe(CheckCompactionRecoveryState.NeedsDecision);
        episode.Reason.ShouldBe(reason);
        (await db.AgentSessions.AsNoTracking().SingleAsync(s => s.Id == world.SessionId)).Status
            .ShouldBe(SessionStatus.Running, "the live session was never stopped");
    }

    private static FakeSessionRunnerClient Stopper(World world) => new()
    {
        AdvertiseCompactionStop = true,
        GetOverride = (_, _) => Task.FromResult(new SessionRunnerSessionDto(
            world.SessionId, 1, world.Accepted, "Running", null, AgentExitReason.Unknown, 1,
            AcceptedStartedAt: world.Accepted)),
        CompactionObservation = new CompactionTailObservation(
            CompactionObservationStatuses.Success, true, 1, "bind-1", 1, 1, world.Boundary, world.Continuation),
        // The real runner answers about the attempt it was asked about; echoing it keeps this
        // success fake honest against the CARD-0606 attempt fence.
        CompactionStopResultFor = request => new CompactionContinuationStopResult(
            world.SessionId, request.AttemptId, true, CompactionStopOutcomes.Exited, world.Accepted),
    };

    private static CheckCompactionContinuationService Service(
        AppDbContext db, DateTime now, FakeSessionRunnerClient runner, CountingResume resume)
    {
        var clock = new FixedClock(now);
        return new CheckCompactionContinuationService(
            db, clock, Options.Create(new DelegationSettings()),
            new CheckCompactionContinuationGate(),
            NullLogger<CheckCompactionContinuationService>.Instance,
            runner, resume,
            // Production always has the CARD-0136 gate behind it; with no sample it is inert, so
            // every case below runs against the same graph the server builds.
            quota: new SubscriptionQuotaGate(
                new SubscriptionUsageReader(db, clock),
                Options.Create(new SubscriptionQuotaGateSettings()),
                clock,
                NullLogger<SubscriptionQuotaGate>.Instance));
    }

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
        return new World(sessionId, agentId, accepted, now, "boundary-1", "cont-1",
            "check " + checkId.ToString("D"));
    }

    private sealed record World(
        Guid SessionId, Guid AgentId, DateTime Accepted, DateTime Now,
        string Boundary, string Continuation, string CheckPrompt);

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
