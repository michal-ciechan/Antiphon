using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
[Category("Slow")]
[ParallelLimiter<ProcessSpawnLimit>]
public class CheckCompactionCrashTests
{
    [Test]
    public async Task Resume_reservation_survives_crash_without_a_second_launch()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using var root = CheckCompactionFixture.CreateRoot();
        await using var first = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", "resume-committed"));
        await first.WaitForHeldAsync();
        await using (var db = NewDb(schema.ConnectionString))
        {
            var episode = await db.CheckCompactionRecoveries.SingleAsync();
            new[] { episode.AcceptedStartedAt, episode.ResumeAcceptedStartedAt }
                .Where(value => value is not null)
                .Select(value => value!.Value)
                .Distinct()
                .Count()
                .ShouldBe(2);
        }

        await first.DrainAsync();
        await using var second = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", ""));
        await second.WaitForExitAsync();
        second.Process.ExitCode.ShouldBe(0, second.Error);
        File.ReadAllLines(Path.Combine(root.FullPath, "resume-calls.txt")).Length.ShouldBe(1);
        await using var verify = NewDb(schema.ConnectionString);
        var stored = await verify.CheckCompactionRecoveries.SingleAsync();
        new[] { stored.AcceptedStartedAt, stored.ResumeAcceptedStartedAt }
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .Distinct()
            .Count()
            .ShouldBe(2);
        await second.DrainAsync();
    }

    [Test]
    public async Task Concurrent_workers_claim_one_stop_allowance()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using var root = CheckCompactionFixture.CreateRoot();
        await using var first = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", "before-stop-commit"));
        await using var second = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", "before-stop-commit"));
        await CheckCompactionFixture.WaitForHeldAsync(root.FullPath, 2, first, second);
        await File.WriteAllTextAsync(Path.Combine(root.FullPath, "release"), "go");
        foreach (var worker in new[] { first, second })
        {
            await worker.WaitForExitAsync();
            await worker.DrainAsync();
        }

        await using var db = NewDb(schema.ConnectionString);
        var attemptIds = await db.CheckCompactionRecoveries.Select(r => r.AttemptId).ToListAsync();
        attemptIds.Where(id => id is not null).Select(id => id!.Value).Distinct().Count().ShouldBe(1);
    }

    [Test]
    public async Task Stop_commit_spends_allowance_before_worker_death()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using var root = CheckCompactionFixture.CreateRoot();
        await using var worker = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", "stop-committed"));
        await worker.WaitForHeldAsync();
        await worker.DrainAsync();
        await using var db = NewDb(schema.ConnectionString);
        var episode = await db.CheckCompactionRecoveries.SingleAsync();
        var supervision = await db.AgentSupervisionStates.SingleAsync(s => s.AgentId == seat.AgentId);
        supervision.LastAutomaticCompactionRestartAt.ShouldBe(episode.StopRequestedAt!.Value);
    }

    [Test]
    public async Task Task_failure_commit_gap_retries_untyped_cleanup()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        var occupant = Guid.NewGuid();
        await using (var db = NewDb(schema.ConnectionString))
        {
            var episode = await db.CheckCompactionRecoveries.SingleAsync();
            episode.State = CheckCompactionRecoveryState.Stopped;
            episode.StopRequestedAt = episode.DetectedAt;
            episode.StopOutcomeAt = episode.DetectedAt;
            db.AgentTasks.Add(new AgentTask
            {
                Id = occupant, RootTaskId = occupant, Title = "occupant", Goal = "wait",
                Role = AgentTaskRole.Check, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Dispatched,
                AgentId = seat.AgentId, AgentSessionId = seat.SessionId, WorkingDirectory = Path.GetTempPath(),
                CreatedAt = episode.DetectedAt, DispatchedAt = episode.DetectedAt.AddMinutes(-20),
            });
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = seat.SessionId, Body = "untyped " + occupant,
                Status = QueuedMessageStatus.Pending, CreatedAt = episode.DetectedAt, Origin = QueuedMessageOrigin.Check,
            });
            await db.SaveChangesAsync();
        }

        await using var root = CheckCompactionFixture.CreateRoot();
        await using var killed = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", "failure-committed"));
        await killed.WaitForHeldAsync();
        await killed.DrainAsync();
        await using var resumed = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", ""));
        await resumed.WaitForExitAsync();
        resumed.Process.ExitCode.ShouldBe(0, resumed.Error);
        await using var verify = NewDb(schema.ConnectionString);
        (await verify.SessionQueuedMessages.SingleAsync(m => m.Body.Contains(occupant.ToString()))).Status
            .ShouldBe(QueuedMessageStatus.Canceled);
        await resumed.DrainAsync();
    }

    [Test]
    public async Task Retirement_failure_keeps_g2_admission_closed()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using (var db = NewDb(schema.ConnectionString))
        {
            var episode = await db.CheckCompactionRecoveries.SingleAsync();
            episode.State = CheckCompactionRecoveryState.ResumeReserved;
            episode.ResumeSessionId = seat.SessionId;
            episode.ResumeAcceptedStartedAt = seat.Accepted;
            await db.SaveChangesAsync();
        }

        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var builder = new DbContextOptionsBuilder<AppDbContext>(options);
        builder.AddInterceptors(new ThrowOnAwaitingCheck());
        await using var dbFault = new AppDbContext(builder.Options);
        var service = new CheckCompactionContinuationService(
            dbFault, TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.DelegationSettings()),
            new CheckCompactionContinuationGate(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CheckCompactionContinuationService>.Instance);
        await service.SweepAsync(CancellationToken.None);
        await using var verify = NewDb(schema.ConnectionString);
        (await verify.CheckCompactionRecoveries.SingleAsync()).State.ShouldBe(CheckCompactionRecoveryState.ResumeReserved);
        (await verify.AgentTasks.CountAsync(t => t.Role == AgentTaskRole.Check && t.Status == AgentTaskStatus.Queued))
            .ShouldBe(0);
    }

    [Test]
    public async Task Committed_failure_reaches_parent_after_worker_death()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await ClaudeDialogFixture.CreateBridgeAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        await parent.InsertTranscriptEntryAsync(TranscriptKinds.TurnEnd, stopReason: "end_turn", timestamp: DateTime.UtcNow);
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using (var db = NewDb(schema.ConnectionString))
        {
            var episode = await db.CheckCompactionRecoveries.SingleAsync(r => r.Id == seat.EpisodeId);
            episode.State = CheckCompactionRecoveryState.Stopped;
            episode.StopRequestedAt = episode.DetectedAt;
            var occupant = Guid.NewGuid();
            db.AgentTasks.Add(new AgentTask
            {
                Id = occupant, RootTaskId = occupant, Title = "retired", Goal = "wait",
                Role = AgentTaskRole.Check, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Dispatched,
                AgentId = seat.AgentId, AgentSessionId = seat.SessionId,
                ParentSessionId = parent.SessionId, ReplyTo = AgentTaskReplyTo.Session,
                WorkingDirectory = parent.TempRoot, CreatedAt = episode.DetectedAt,
                DispatchedAt = episode.DetectedAt.AddMinutes(-20),
            });
            await db.SaveChangesAsync();
        }

        await using var root = CheckCompactionFixture.CreateRoot();
        var request = Request(seat, root.FullPath, "sweep", "failure-committed");
        await using var killed = CheckCompactionFixture.Start(request);
        await killed.WaitForHeldAsync();
        await killed.DrainAsync();
        string body;
        await using (var db = NewDb(schema.ConnectionString))
            body = (await db.AgentTaskLandNotifications.SingleAsync(n => n.Kind == LandNotificationKind.DeliveryFailure)).Body;
        await using var scan = CheckCompactionFixture.Start(new CrashWorkerRequest
        {
            Root = root.FullPath, Scenario = "scan", ConnectionString = schema.ConnectionString,
            SessionId = parent.SessionId, AgentId = parent.AgentId,
        });
        await scan.WaitForReadyAsync();
        var prompts = new List<string>();
        await scan.WaitUntilAsync(async () =>
        {
            await using var db = NewDb(schema.ConnectionString);
            prompts = await db.TranscriptEntries.AsNoTracking()
                .Where(t => t.AgentSessionId == parent.SessionId && t.Kind == TranscriptKinds.UserPrompt && t.Text != null)
                .Select(t => t.Text!)
                .ToListAsync();
            prompts = prompts.Where(text => PromptSubmissionMatch.IsCompleteIn(body, text)).ToList();
            return prompts.Count > 0;
        }, CheckCompactionFixture.PromptBudget, "the failure prompt");

        prompts.Count.ShouldBe(1);
        await scan.StopAsync();
    }

    [Test]
    public async Task Accepted_parent_prompt_survives_lost_verdict()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var parent = await ClaudeDialogFixture.CreateBridgeAsync(new() { AlwaysOn = false, ConnectionString = schema.ConnectionString });
        var body = "immutable failure note that was already accepted";
        var noteId = Guid.NewGuid();
        var taskId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        await using (var db = NewDb(schema.ConnectionString))
        {
            db.AgentTasks.Add(new AgentTask
            {
                Id = taskId, RootTaskId = taskId, Title = "subject", Goal = "watch",
                Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Failed,
                AgentId = parent.AgentId, AgentSessionId = parent.SessionId, ParentSessionId = parent.SessionId,
                ReplyTo = AgentTaskReplyTo.Session, WorkingDirectory = parent.TempRoot, CreatedAt = DateTime.UtcNow,
            });
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = eventId, AgentTaskId = taskId, Type = AgentTaskEventType.Failed,
                Detail = "accepted prompt lost its verdict", At = DateTime.UtcNow,
            });
            db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
            {
                Id = noteId, TaskId = taskId, SourceEventId = eventId, Kind = LandNotificationKind.DeliveryFailure,
                ReplyTo = AgentTaskReplyTo.Session, ParentSessionId = parent.SessionId, Body = body,
                ContentDigest = DelegationNoteDigest.Compute(body), CreatedAt = DateTime.UtcNow,
                NextAttemptAt = DateTime.UtcNow, State = LandNotificationState.AwaitingReceipt,
            });
            db.SessionQueuedMessages.Add(new SessionQueuedMessage
            {
                Id = Guid.NewGuid(), AgentSessionId = parent.SessionId, Body = body, Status = QueuedMessageStatus.Pending,
                Origin = QueuedMessageOrigin.Delegation, SourceTaskId = taskId, SourceLandNotificationId = noteId,
                ContentDigest = DelegationNoteDigest.Compute(body), DeliveryAttempts = 1,
                LastDeliveryBaselineSequence = 0, CreatedAt = DateTime.UtcNow,
            });
            db.TranscriptEntries.Add(new TranscriptEntry
            {
                Id = Guid.NewGuid(), AgentSessionId = parent.SessionId, Sequence = 1,
                Kind = TranscriptKinds.UserPrompt, Text = body, Timestamp = DateTime.UtcNow, CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        await using var root = CheckCompactionFixture.CreateRoot();
        await using var worker = CheckCompactionFixture.Start(new CrashWorkerRequest
        {
            Root = root.FullPath, Scenario = "reconcile", ConnectionString = schema.ConnectionString,
            SessionId = parent.SessionId, AgentId = parent.AgentId, NotificationId = noteId,
        });
        await worker.WaitForExitAsync();
        worker.Process.ExitCode.ShouldBe(0, worker.Error);
        await using var verify = NewDb(schema.ConnectionString);
        var prompts = await verify.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == parent.SessionId && t.Kind == TranscriptKinds.UserPrompt)
            .Select(t => t.Text!)
            .ToListAsync();
        prompts.Count(text => text.Contains(body, StringComparison.Ordinal)).ShouldBe(1);
        await worker.DrainAsync();
    }

    [Test]
    public async Task Committed_audit_publication_is_retried_after_death()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using var root = CheckCompactionFixture.CreateRoot();
        await using var killed = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", "before-audit-publish"));
        await killed.WaitForHeldAsync();
        await killed.DrainAsync();
        await using var recovered = CheckCompactionFixture.Start(Request(seat, root.FullPath, "sweep", ""));
        await recovered.WaitForExitAsync();
        recovered.Process.ExitCode.ShouldBe(0, recovered.Error);
        var published = await File.ReadAllTextAsync(Path.Combine(root.FullPath, $"{recovered.Process.Id}.episodes.txt"));
        published.ShouldContain(seat.EpisodeId.ToString());
        await recovered.DrainAsync();
    }

    [Test]
    public async Task Resume_enqueue_requires_committed_generation()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        var seat = await CheckCompactionFixture.SeedConfirmedAsync(schema.ConnectionString);
        await using (var db = NewDb(schema.ConnectionString))
        {
            var episode = await db.CheckCompactionRecoveries.SingleAsync();
            episode.State = CheckCompactionRecoveryState.Stopped;
            episode.StopRequestedAt = episode.DetectedAt;
            episode.StopOutcomeAt = episode.DetectedAt;
            await db.SaveChangesAsync();
        }

        var options = TestDbFixture.CreateDbContextOptions(schema.ConnectionString);
        var builder = new DbContextOptionsBuilder<AppDbContext>(options);
        builder.AddInterceptors(new ThrowOnResumeReserved());
        await using var dbFault = new AppDbContext(builder.Options);
        var creates = new List<Guid>();
        var resume = new LaunchAfterCommitResume(seat.SessionId, seat.Accepted, schema.ConnectionString, creates);
        var service = new CheckCompactionContinuationService(
            dbFault, TimeProvider.System,
            Microsoft.Extensions.Options.Options.Create(new Antiphon.Server.Application.Settings.DelegationSettings()),
            new CheckCompactionContinuationGate(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<CheckCompactionContinuationService>.Instance,
            resume: resume);
        await service.SweepAsync(CancellationToken.None);
        creates.Count.ShouldBe(0);
        await using var verify = NewDb(schema.ConnectionString);
        (await verify.CheckCompactionRecoveries.SingleAsync()).State.ShouldBe(CheckCompactionRecoveryState.Stopped);
    }

    private static CrashWorkerRequest Request(CheckCompactionFixture.ConfirmedSeat seat, string root, string scenario, string hold) =>
        new()
        {
            Root = root,
            Scenario = scenario,
            ConnectionString = seat.ConnectionString,
            Hold = hold,
            SessionId = seat.SessionId,
            AgentId = seat.AgentId,
            EpisodeId = seat.EpisodeId,
            Accepted = seat.Accepted,
        };

    private static AppDbContext NewDb(string connectionString) =>
        new(TestDbFixture.CreateDbContextOptions(connectionString));

    private sealed class ThrowOnAwaitingCheck : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<CheckCompactionRecovery>()
                    .Any(entry => entry.Entity.State == CheckCompactionRecoveryState.AwaitingCheck) == true)
                throw new InvalidOperationException("retirement save failed");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class ThrowOnResumeReserved : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context?.ChangeTracker.Entries<CheckCompactionRecovery>()
                    .Any(entry => entry.Entity.State == CheckCompactionRecoveryState.ResumeReserved) == true)
                throw new InvalidOperationException("reservation commit failed");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    private sealed class LaunchAfterCommitResume(
        Guid sessionId, DateTime accepted, string connectionString, List<Guid> creates) : ICompactionContinuationResume
    {
        public async Task<CompactionResumeResult> ResumeAsync(Guid episodeId, CancellationToken ct)
        {
            await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(connectionString));
            var episode = await db.CheckCompactionRecoveries.AsNoTracking().SingleAsync(r => r.Id == episodeId, ct);
            if (episode.State == CheckCompactionRecoveryState.ResumeReserved && episode.ResumeAcceptedStartedAt is not null)
                creates.Add(sessionId);
            var generation = SessionGeneration.Next(accepted, accepted.AddMinutes(30));
            return new CompactionResumeResult(true, sessionId, generation, "reserved");
        }
    }
}
