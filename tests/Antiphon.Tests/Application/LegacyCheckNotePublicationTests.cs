using Antiphon.Server.Application.Services;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Tests.TestHelpers;
using Microsoft.EntityFrameworkCore;
using Shouldly;
using TUnit.Core;

namespace Antiphon.Tests.Application;

[Category("Integration")]
public class LegacyCheckNotePublicationTests
{
    [Test]
    public async Task Legacy_production_retries_keep_the_first_body()
    {
        await using var schema = await TestDbFixture.CreateIsolatedSchemaAsync();
        await using var harness = await BridgeQueueHarness.CreateAsync(
            new BridgeQueueHarness.HarnessOptions { ConnectionString = schema.ConnectionString });
        await using var db = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var session = await db.AgentSessions.SingleAsync(s => s.Id == harness.SessionId);
        var generation = SessionGeneration.Normalize(session.StartedAt);
        var now = DateTime.UtcNow;
        var checkedId = Guid.NewGuid();
        var runId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        db.CheckCompactionRecoveries.Add(new CheckCompactionRecovery
        {
            Id = episodeId,
            PhysicalAgentId = harness.AgentId,
            SessionId = harness.SessionId,
            AcceptedStartedAt = generation,
            BoundaryIdentity = "boundary-1",
            BoundaryCreatedAt = now,
            ContinuationCreatedAt = now,
            ConfiguredThresholdMinutes = 10,
            DetectedAt = now,
            State = CheckCompactionRecoveryState.AwaitingCheck,
            ResumeSessionId = harness.SessionId,
            ResumeAcceptedStartedAt = generation,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = checkedId, RootTaskId = checkedId, Title = "subject", Goal = "watch",
            Role = AgentTaskRole.Code, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Working,
            AgentId = harness.AgentId, AgentSessionId = harness.SessionId,
            ParentSessionId = harness.SessionId, ReplyTo = AgentTaskReplyTo.Session,
            WorkingDirectory = harness.TempRoot, CreatedAt = now, DispatchedAt = now, Attempt = 1,
            CheckCount = 1,
        });
        db.AgentTasks.Add(new AgentTask
        {
            Id = runId, RootTaskId = runId, Title = "read", Goal = "interpret",
            Role = AgentTaskRole.Check, Kind = AgentTaskKind.Worker, Status = AgentTaskStatus.Succeeded,
            AgentId = harness.AgentId, AgentSessionId = harness.SessionId,
            WorkingDirectory = harness.TempRoot, CreatedAt = now, Result = "useful",
        });
        await db.SaveChangesAsync();

        var publisher = new LegacyCheckNotePublicationService(
            db, TimeProvider.System,
            new AgentTaskLandNotificationService(
                db, harness.Queue, new CompletionNoteFlushQueue(), harness.Runtime, TimeProvider.System));
        var subject = await db.AgentTasks.SingleAsync(t => t.Id == checkedId);
        var first = await publisher.TryPublishAsync(
            subject, 1, "Original check note\r\nline", "event", runId, false, null, CancellationToken.None);
        first.ShouldBe(LegacyCheckNotePublicationService.PublishResult.Published);

        subject.Title = "changed after production";
        var second = await publisher.TryPublishAsync(
            subject, 1, "A different body", "event", runId, false, null, CancellationToken.None);
        second.ShouldBe(LegacyCheckNotePublicationService.PublishResult.Published);

        await using var verify = new AppDbContext(TestDbFixture.CreateDbContextOptions(schema.ConnectionString));
        var publication = await verify.LegacyCheckNotePublications.SingleAsync();
        publication.State.ShouldBe(LegacyCheckNoteState.Produced);
        publication.Body.ShouldBe("Original check note\nline");
        publication.InterpretationTaskId.ShouldBe(runId);
        var note = await verify.AgentTaskLandNotifications.SingleAsync();
        note.Kind.ShouldBe(LandNotificationKind.LegacyCheckNote);
        note.Body.ShouldBe(publication.Body);
        note.TaskId.ShouldBe(checkedId);
        var queued = await verify.SessionQueuedMessages.SingleAsync(m => m.SourceLandNotificationId == note.Id);
        queued.Origin.ShouldBe(QueuedMessageOrigin.Check);
        queued.ConversationKey.ShouldBe($"check:{checkedId:N}");
        queued.Body.ShouldBe(publication.Body);
        (await verify.AgentTasks.SingleAsync(t => t.Id == checkedId)).CompletionNoteQueuedAt.ShouldBeNull();
        (await verify.AgentTaskEvents.CountAsync(e => e.AgentTaskId == checkedId && e.Type == AgentTaskEventType.Check))
            .ShouldBe(1);

        await verify.LegacyCheckNotePublications.ExecuteDeleteAsync();
        await verify.AgentTaskLandNotifications.ExecuteDeleteAsync();
        await verify.SessionQueuedMessages.Where(m => m.SourceTaskId == checkedId || m.SourceTaskId == runId).ExecuteDeleteAsync();
        await verify.AgentTaskEvents.Where(e => e.AgentTaskId == checkedId || e.AgentTaskId == runId).ExecuteDeleteAsync();
        await verify.AgentTasks.Where(t => t.Id == checkedId || t.Id == runId).ExecuteDeleteAsync();
        await verify.CheckCompactionRecoveries.ExecuteDeleteAsync();
    }
}
