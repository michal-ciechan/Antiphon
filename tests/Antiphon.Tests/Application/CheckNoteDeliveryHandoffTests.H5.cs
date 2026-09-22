using System.Text.Json;
using Antiphon.Server.Application.Services;
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

public partial class CheckNoteDeliveryHandoffTests
{
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task Legacy_produced_note_survives_pre_enqueue_worker_death(bool busy)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await BridgeQueueHarness.CreateAsync(new()
        {
            AlwaysOn = false, ConnectionString = schema.ConnectionString,
        });
        if (busy)
            await parent.MarkWorkingAsync();
        var seeded = await SeedLegacyWorldAsync(schema.ConnectionString, parent);
        var beforeChecks = seeded.CheckCount;
        var beforeRuns = seeded.RunCount;
        var root = CheckCompactionFixture.CreateRoot();
        var producer = CheckCompactionFixture.Start(new CrashWorkerRequest
        {
            Root = root, Scenario = "produce-hold", Hold = "before-note-enqueue",
            ConnectionString = schema.ConnectionString, SessionId = parent.SessionId, AgentId = parent.AgentId,
            EpisodeId = seeded.EpisodeId, CheckedTaskId = seeded.CheckedTaskId, RunId = seeded.RunId,
            CheckNumber = 1, Body = seeded.Body, Accepted = seeded.Generation,
        });
        await CheckCompactionFixture.WaitForHeldAsync(root, 1, TimeSpan.FromSeconds(40));
        var receipt = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(root, "produced.json")));
        receipt.RootElement.GetProperty("mvid").GetGuid()
            .ShouldBe(typeof(CheckCompactionCrashWorker).Assembly.ManifestModule.ModuleVersionId);
        receipt.RootElement.GetProperty("dbLifecycle").GetString().ShouldBe("never-requested");
        await using (var db = NewDb(schema.ConnectionString))
        {
            (await db.LegacyCheckNotePublications.SingleAsync()).State.ShouldBe(LegacyCheckNoteState.Produced);
            (await db.SessionQueuedMessages.CountAsync(m => m.SourceLandNotificationId != null)).ShouldBe(0);
            (await db.AgentTasks.SingleAsync(t => t.Id == seeded.CheckedTaskId)).CheckCount.ShouldBe(beforeChecks);
        }

        await CheckCompactionFixture.DrainAsync(root, producer.Process, producer.Output, producer.Error);
        var scanner = CheckCompactionFixture.Start(new CrashWorkerRequest
        {
            Root = root, Scenario = "scan", ConnectionString = schema.ConnectionString,
            SessionId = parent.SessionId, AgentId = parent.AgentId,
        });
        if (busy)
        {
            await Task.Delay(500);
            await using var mid = NewDb(schema.ConnectionString);
            (await MatchingPromptsAsync(mid, parent.SessionId, seeded.Body)).Count.ShouldBe(0);
            await BridgeQueueHarness.InsertEntryAsync(parent.SessionId, TranscriptKinds.TurnEnd, stopReason: "end_turn",
                timestamp: DateTime.UtcNow, connectionString: schema.ConnectionString);
        }

        var prompts = new List<string>();
        var until = DateTime.UtcNow.AddSeconds(25);
        while (DateTime.UtcNow < until && prompts.Count == 0)
        {
            await using var db = NewDb(schema.ConnectionString);
            prompts = await MatchingPromptsAsync(db, parent.SessionId, seeded.Body);
            if (prompts.Count == 0)
                await Task.Delay(100);
        }

        prompts.Count.ShouldBe(1);
        await using var verify = NewDb(schema.ConnectionString);
        (await verify.AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Check)).ShouldBe(beforeRuns);
        (await verify.AgentTasks.SingleAsync(t => t.Id == seeded.CheckedTaskId)).CheckCount.ShouldBe(beforeChecks);
        await File.WriteAllTextAsync(Path.Combine(root, "stop"), "stop");
        await scanner.Process.WaitForExitAsync();
        await CheckCompactionFixture.DrainAsync(root, scanner.Process, scanner.Output, scanner.Error);
    }

    [Test]
    public async Task Legacy_matching_publication_recovers_the_seat() =>
        await AssertLegacyReceiptAsync(mutate: null, expectRecovered: true);

    [Test]
    public async Task Legacy_suppressed_note_never_recovers_the_seat() =>
        await AssertLegacyReceiptAsync(publication => publication.State = LegacyCheckNoteState.Suppressed, expectRecovered: false);

    [Test]
    public async Task Legacy_note_receipt_cannot_validate_another_episode() =>
        await AssertLegacyReceiptAsync(async (publication, db) =>
        {
            var other = Guid.NewGuid();
            db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
            {
                Id = other, PhysicalAgentId = publication.PhysicalAgentId, SessionId = publication.InterpreterSessionId,
                AcceptedStartedAt = publication.InterpreterAcceptedStartedAt, BoundaryIdentity = "other",
                BoundaryCreatedAt = DateTime.UtcNow, ContinuationCreatedAt = DateTime.UtcNow,
                ConfiguredThresholdMinutes = 10, DetectedAt = DateTime.UtcNow,
                State = CheckCompactionRecoveryState.AwaitingCheck,
            });
            await db.SaveChangesAsync();
            publication.RecoveryId = other;
        }, expectRecovered: false);

    [Test]
    public async Task Legacy_note_receipt_cannot_validate_another_generation() =>
        await AssertLegacyReceiptAsync(publication =>
            publication.InterpreterAcceptedStartedAt = publication.InterpreterAcceptedStartedAt.AddMinutes(5),
            expectRecovered: false);

    [Test]
    public async Task Legacy_note_requires_its_own_interpretation_proof() =>
        await AssertLegacyReceiptAsync(async (publication, db) =>
        {
            var other = Guid.NewGuid();
            var now = DateTime.UtcNow;
            db.AgentTasks.Add(new AgentTask
            {
                Id = other, RootTaskId = other, Title = "other", Goal = "read", Role = AgentTaskRole.Check,
                Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded, Result = "other useful reading",
                AgentId = publication.PhysicalAgentId, AgentSessionId = publication.InterpreterSessionId,
                WorkingDirectory = Path.GetTempPath(), CreatedAt = now,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = publication.InterpreterSessionId, Sequence = 50,
                Kind = TranscriptKinds.UserPrompt, Text = DelegationReportFormatter.TaskMarker(other) + " brief",
                Timestamp = now, CreatedAt = now,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = publication.InterpreterSessionId, Sequence = 51,
                Kind = TranscriptKinds.TurnEnd, Timestamp = now, CreatedAt = now,
            });
            publication.InterpretationTaskId = Guid.NewGuid();
            await db.SaveChangesAsync();
        }, expectRecovered: false);

    [Test]
    public async Task Legacy_note_requires_the_captured_check_identity() =>
        await AssertLegacyReceiptAsync(publication => publication.CheckNumber = 99, expectRecovered: false);

    [Test]
    public async Task Legacy_pointer_only_prompt_is_not_original_note_receipt() =>
        await AssertLegacyReceiptAsync((publication, db) =>
        {
            var pointer = "pointer-only-spill-not-the-original-note";
            var row = db.SessionQueuedMessages.Single(m => m.SourceLandNotificationId == publication.NotificationId);
            row.Body = pointer;
            var prompt = db.TranscriptEntries.Single(t => t.AgentSessionId == publication.ParentSessionId
                && t.Kind == TranscriptKinds.UserPrompt);
            prompt.Text = pointer;
            return Task.CompletedTask;
        }, expectRecovered: false);

    private static async Task AssertLegacyReceiptAsync(
        Action<LegacyCheckNotePublication>? mutate, bool expectRecovered) =>
        await AssertLegacyReceiptAsync(mutate is null ? null : (publication, _) =>
        {
            mutate(publication);
            return Task.CompletedTask;
        }, expectRecovered);

    private static async Task AssertLegacyReceiptAsync(
        Func<LegacyCheckNotePublication, AppDbContext, Task>? mutate, bool expectRecovered)
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var db = NewDb(schema.ConnectionString);
        var now = DateTime.UtcNow;
        var generation = SessionGeneration.Normalize(now.AddMinutes(-30));
        var agentId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var subjectId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var noteId = Guid.NewGuid();
        var body = "original legacy check note for the caller";
        db.Agents.Add(new Agent
        {
            Id = agentId, Name = "check", Slug = "c" + agentId.ToString("N")[..8], WorkingDirectory = Path.GetTempPath(),
            Kind = AgentKind.ClaudeCode, AlwaysOn = true, Status = AgentStatus.Running,
            StandingSpecialistRole = AgentTaskRole.Check, StandingSpecialistOwnerId = agentId,
            PersistentSessionId = sessionId.ToString("D"), CreatedAt = generation, UpdatedAt = generation,
        });
        db.AgentSessions.AddRange(
            new AgentSession
            {
                Id = sessionId, StandingAgentId = agentId, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode,
                Status = SessionStatus.Running, Cwd = Path.GetTempPath(), CreatedAt = generation, StartedAt = generation,
                LastSeenAt = now,
            },
            new AgentSession
            {
                Id = parentId, DefinitionName = "claude", AgentKind = AgentKind.ClaudeCode, Status = SessionStatus.Running,
                Cwd = Path.GetTempPath(), CreatedAt = generation, StartedAt = generation, LastSeenAt = now,
            });
        db.AgentSupervisionStates.Add(new AgentSupervisionState
        {
            AgentId = agentId, ActiveCompactionRecoveryId = episodeId, UpdatedAt = now,
        });
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId, PhysicalAgentId = agentId, SessionId = sessionId, AcceptedStartedAt = generation,
            BoundaryIdentity = "boundary", BoundaryCreatedAt = generation, ContinuationCreatedAt = generation,
            ConfiguredThresholdMinutes = 10, DetectedAt = now.AddMinutes(-20), StopOutcomeAt = now.AddMinutes(-10),
            State = CheckCompactionRecoveryState.AwaitingCheck, ResumeSessionId = sessionId,
            ResumeAcceptedStartedAt = generation,
        });
        db.AgentTasks.AddRange(
            new AgentTask
            {
                Id = subjectId, RootTaskId = subjectId, Title = "subject", Goal = "watch", Role = AgentTaskRole.Code,
                Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working, AgentId = agentId,
                AgentSessionId = sessionId, ParentSessionId = parentId, ReplyTo = AgentTaskReplyTo.Session,
                WorkingDirectory = Path.GetTempPath(), CreatedAt = generation, DispatchedAt = generation,
                Attempt = 1, CheckCount = 1,
            },
            new AgentTask
            {
                Id = runId, RootTaskId = runId, Title = "read", Goal = "interpret", Role = AgentTaskRole.Check,
                Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded, Result = "useful reading",
                AgentId = agentId, AgentSessionId = sessionId, WorkingDirectory = Path.GetTempPath(),
                CreatedAt = now.AddMinutes(-5),
            });
        var publication = new LegacyCheckNotePublication
        {
            Id = Guid.NewGuid(), CheckedTaskId = subjectId, CheckedTaskAttempt = 1, CheckedTaskDispatchedAt = generation,
            CheckNumber = 1, RecoveryId = episodeId, PhysicalAgentId = agentId, InterpreterSessionId = sessionId,
            InterpreterAcceptedStartedAt = generation, ParentSessionId = parentId, CapturedAt = now.AddMinutes(-4),
            FactsSnapshotJson = "{}", RenderContextJson = "{}", InterpretationTaskId = runId,
            InterpretationDeadlineAt = now, State = LegacyCheckNoteState.Produced, SourceEventId = Guid.NewGuid(),
            NotificationId = noteId, ProducedAt = now.AddMinutes(-4), Body = body,
            ContentDigest = DelegationNoteDigest.Compute(body), NextAttemptAt = now,
        };
        db.LegacyCheckNotePublications.Add(publication);
        db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
        {
            Id = noteId, TaskId = subjectId, SourceEventId = publication.SourceEventId,
            Kind = LandNotificationKind.LegacyCheckNote, ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = parentId,
            Body = body, ContentDigest = publication.ContentDigest, CreatedAt = now.AddMinutes(-4),
            NextAttemptAt = now, State = LandNotificationState.AwaitingReceipt, QueueMessageId = Guid.NewGuid(),
        });
        db.SessionQueuedMessages.Add(new SessionQueuedMessage
        {
            Id = db.AgentTaskLandNotifications.Local.Single().QueueMessageId!.Value, AgentSessionId = parentId, Body = body,
            Status = QueuedMessageStatus.Sent, Origin = QueuedMessageOrigin.Check, SourceTaskId = subjectId,
            SourceLandNotificationId = noteId, ContentDigest = publication.ContentDigest,
            LastDeliveryBaselineSequence = 1, CreatedAt = now.AddMinutes(-3), ConversationKey = $"check:{subjectId:N}",
        });
        db.TranscriptEntries.AddRange(
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 2, Kind = TranscriptKinds.UserPrompt,
                Text = DelegationReportFormatter.TaskMarker(runId) + " brief", Timestamp = now.AddMinutes(-5), CreatedAt = now,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = sessionId, Sequence = 3, Kind = TranscriptKinds.TurnEnd,
                Timestamp = now.AddMinutes(-5), CreatedAt = now,
            },
            new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = parentId, Sequence = 2, Kind = TranscriptKinds.UserPrompt,
                Text = body, Timestamp = now.AddMinutes(-3), CreatedAt = now,
            });
        if (mutate is not null)
            await mutate(publication, db);
        await db.SaveChangesAsync();
        var service = new CheckCompactionContinuationService(
            db, TimeProvider.System, Options.Create(new Antiphon.Server.Application.Settings.DelegationSettings()),
            new CheckCompactionContinuationGate(), NullLogger<CheckCompactionContinuationService>.Instance);
        await service.SweepAsync(CancellationToken.None);
        var episode = await db.CheckCompactionRecoveries.SingleAsync(r => r.Id == episodeId);
        if (expectRecovered)
            episode.State.ShouldBe(CheckCompactionRecoveryState.Recovered);
        else
        {
            episode.State.ShouldBe(CheckCompactionRecoveryState.AwaitingCheck);
            (await db.AgentTaskLandNotifications.SingleAsync(n => n.Id == noteId)).ConfirmedAt.ShouldBeNull();
        }
    }

    private static async Task<List<string>> MatchingPromptsAsync(AppDbContext db, Guid sessionId, string body)
    {
        var prompts = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
            .Select(t => t.Text!)
            .ToListAsync();
        return prompts.Where(text => PromptSubmissionMatch.IsCompleteIn(body, text)).ToList();
    }

    private static async Task<SeededLegacy> SeedLegacyWorldAsync(string connectionString, BridgeQueueHarness parent)
    {
        await using var db = NewDb(connectionString);
        var session = await db.AgentSessions.SingleAsync(s => s.Id == parent.SessionId);
        var generation = SessionGeneration.Normalize(session.StartedAt);
        var dispatched = SessionGeneration.Normalize(DateTime.UtcNow.AddMinutes(-2));
        var episodeId = Guid.NewGuid();
        var checkedId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId, PhysicalAgentId = parent.AgentId, SessionId = parent.SessionId,
            AcceptedStartedAt = generation, BoundaryIdentity = "boundary-1", BoundaryCreatedAt = dispatched,
            ContinuationCreatedAt = dispatched, ConfiguredThresholdMinutes = 10, DetectedAt = dispatched,
            State = CheckCompactionRecoveryState.AwaitingCheck, ResumeSessionId = parent.SessionId,
            ResumeAcceptedStartedAt = generation,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = checkedId, RootTaskId = checkedId, Title = "subject", Goal = "watch", Role = AgentTaskRole.Code,
            Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working, AgentId = parent.AgentId,
            AgentSessionId = parent.SessionId, ParentSessionId = parent.SessionId, ReplyTo = AgentTaskReplyTo.Session,
            WorkingDirectory = parent.TempRoot, CreatedAt = dispatched, DispatchedAt = dispatched, Attempt = 1, CheckCount = 1,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = runId, RootTaskId = runId, Title = "read", Goal = "interpret", Role = AgentTaskRole.Check,
            Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded, Result = "useful",
            AgentId = parent.AgentId, AgentSessionId = parent.SessionId, WorkingDirectory = parent.TempRoot,
            CreatedAt = dispatched,
        });
        await db.SaveChangesAsync();
        return new SeededLegacy(episodeId, checkedId, runId, generation, 1, 1, "original legacy check note for the caller");
    }

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private sealed record SeededLegacy(
        Guid EpisodeId, Guid CheckedTaskId, Guid RunId, DateTime Generation, int CheckCount, int RunCount, string Body);
}
