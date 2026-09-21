using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0079 legacy Check publication. Capture freezes identity; Produce commits
/// the body, Check event and outbox together. A Produced row never re-renders.
/// </summary>
public sealed class LegacyCheckNotePublicationService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly AgentTaskLandNotificationService? _notifications;
    private readonly CheckCompactionBoundary _boundary;

    public LegacyCheckNotePublicationService(
        AppDbContext db,
        TimeProvider time,
        AgentTaskLandNotificationService? notifications = null,
        CheckCompactionBoundary? boundary = null)
    {
        _db = db;
        _time = time;
        _notifications = notifications;
        _boundary = boundary ?? new CheckCompactionBoundary();
    }

    public enum PublishResult { NotAssociated, Published, Suppressed }

    public async Task<PublishResult> TryPublishAsync(
        AgentTask checkedTask,
        int checkNumber,
        string body,
        string? eventDetail,
        Guid? interpretationTaskId,
        bool suppress,
        string? suppressionReason,
        CancellationToken ct)
    {
        if (checkedTask.DispatchedAt is not DateTime dispatched || interpretationTaskId is not Guid runId)
            return PublishResult.NotAssociated;
        if (checkedTask.ParentSessionId is not Guid parent)
            return PublishResult.NotAssociated;

        var run = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == runId, ct);
        if (run?.AgentId is not Guid seat || run.AgentSessionId is not Guid interpreterSession)
            return PublishResult.NotAssociated;
        var session = await _db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == interpreterSession, ct);
        if (session is null)
            return PublishResult.NotAssociated;
        var generation = SessionGeneration.Normalize(session.StartedAt);
        var episode = await _db.CheckCompactionRecoveries.FirstOrDefaultAsync(r =>
            r.State == CheckCompactionRecoveryState.AwaitingCheck
            && r.PhysicalAgentId == seat
            && r.ResumeSessionId == interpreterSession
            && r.ResumeAcceptedStartedAt == generation, ct);
        if (episode is null)
            return PublishResult.NotAssociated;

        var attempt = checkedTask.Attempt;
        var dispatchedAt = SessionGeneration.Normalize(dispatched);
        var existing = await _db.LegacyCheckNotePublications.FirstOrDefaultAsync(p =>
            p.CheckedTaskId == checkedTask.Id
            && p.CheckedTaskAttempt == attempt
            && p.CheckedTaskDispatchedAt == dispatchedAt
            && p.CheckNumber == checkNumber, ct);
        var publication = existing ?? await CaptureAsync(
            checkedTask, attempt, dispatchedAt, checkNumber, episode, interpreterSession, generation, parent, runId, ct);
        if (publication.State == LegacyCheckNoteState.Produced)
            return PublishResult.Published;
        if (publication.State == LegacyCheckNoteState.Suppressed)
            return PublishResult.Suppressed;

        await ProduceAsync(publication, body, eventDetail, suppress, suppressionReason, ct);
        return suppress ? PublishResult.Suppressed : PublishResult.Published;
    }

    public async Task<LegacyCheckNotePublication> CaptureAsync(
        AgentTask checkedTask,
        int attempt,
        DateTime dispatchedAt,
        int checkNumber,
        CheckCompactionRecovery episode,
        Guid interpreterSessionId,
        DateTime interpreterGeneration,
        Guid parentSessionId,
        Guid interpretationTaskId,
        CancellationToken ct)
    {
        var now = UtcNow();
        var publication = new LegacyCheckNotePublication
        {
            Id = Guid.NewGuid(),
            CheckedTaskId = checkedTask.Id,
            CheckedTaskAttempt = attempt,
            CheckedTaskDispatchedAt = dispatchedAt,
            CheckNumber = checkNumber,
            RecoveryId = episode.Id,
            PhysicalAgentId = episode.PhysicalAgentId,
            InterpreterSessionId = interpreterSessionId,
            InterpreterAcceptedStartedAt = interpreterGeneration,
            ParentSessionId = parentSessionId,
            CapturedAt = now,
            FactsSnapshotJson = JsonSerializer.Serialize(new { checkNumber, checkedTask.Id }),
            RenderContextJson = JsonSerializer.Serialize(new { parentSessionId }),
            InterpretationTaskId = interpretationTaskId,
            InterpretationDeadlineAt = now,
            State = LegacyCheckNoteState.Captured,
            SourceEventId = Guid.NewGuid(),
            NotificationId = Guid.NewGuid(),
            NextAttemptAt = now,
        };
        _db.LegacyCheckNotePublications.Add(publication);
        await _db.SaveChangesAsync(ct);
        await _boundary.ReachedAsync("legacy-capture-committed", publication.Id, ct);
        return publication;
    }

    public async Task ProduceAsync(
        LegacyCheckNotePublication publication,
        string body,
        string? eventDetail,
        bool suppress,
        string? suppressionReason,
        CancellationToken ct)
    {
        await _db.Entry(publication).ReloadAsync(ct);
        if (publication.State == LegacyCheckNoteState.Produced)
            return;
        if (publication.State == LegacyCheckNoteState.Suppressed)
            return;

        var canonical = body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();
        var now = UtcNow();
        var detail = eventDetail ?? canonical;
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = publication.SourceEventId,
            AgentTaskId = publication.CheckedTaskId,
            Type = AgentTaskEventType.Check,
            Detail = detail.Length <= 4000 ? detail : detail[..4000],
            At = now,
        });
        if (suppress)
        {
            publication.State = LegacyCheckNoteState.Suppressed;
            publication.SuppressionReason = suppressionReason;
            publication.SuppressedAt = now;
            publication.EventDetail = detail;
        }
        else
        {
            var digest = DelegationNoteDigest.Compute(canonical);
            publication.State = LegacyCheckNoteState.Produced;
            publication.ProducedAt = now;
            publication.Body = canonical;
            publication.ContentDigest = digest;
            publication.EventDetail = detail;
            _db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
            {
                Id = publication.NotificationId,
                IsLegacy = false,
                TaskId = publication.CheckedTaskId,
                SourceEventId = publication.SourceEventId,
                Kind = LandNotificationKind.LegacyCheckNote,
                ReplyTo = AgentTaskReplyTo.Session,
                ParentSessionId = publication.ParentSessionId,
                Body = canonical,
                ContentDigest = digest,
                CreatedAt = now,
                NextAttemptAt = now,
                State = LandNotificationState.Queued,
            });
        }

        publication.ConcurrencyToken = Guid.NewGuid();
        await _boundary.ReachedAsync("legacy-before-produce-commit", publication.Id, ct);
        await _db.SaveChangesAsync(ct);
        await _boundary.ReachedAsync("legacy-note-produced", publication.Id, ct);
        if (publication.State == LegacyCheckNoteState.Produced && _notifications is not null)
            await _notifications.ReconcileAsync(publication.NotificationId, ct);
    }

    private DateTime UtcNow()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}
