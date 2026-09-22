using System.Text.Json;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

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
    private readonly DelegationSettings _settings;

    public LegacyCheckNotePublicationService(
        AppDbContext db,
        TimeProvider time,
        AgentTaskLandNotificationService? notifications = null,
        CheckCompactionBoundary? boundary = null,
        IOptions<DelegationSettings>? settings = null)
    {
        _db = db;
        _time = time;
        _notifications = notifications;
        _boundary = boundary ?? new CheckCompactionBoundary();
        _settings = settings?.Value ?? new DelegationSettings();
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
        CancellationToken ct,
        Guid? associatedRecoveryId = null)
    {
        if (associatedRecoveryId is not Guid recoveryId)
            return PublishResult.NotAssociated;
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
            r.Id == recoveryId
            && r.State == CheckCompactionRecoveryState.AwaitingCheck
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
        if (publication is null)
            return PublishResult.NotAssociated;
        if (publication.State == LegacyCheckNoteState.Produced)
            return PublishResult.Published;
        if (publication.State == LegacyCheckNoteState.Suppressed)
            return PublishResult.Suppressed;

        await ProduceAsync(publication, body, eventDetail, suppress, suppressionReason, ct);
        return suppress ? PublishResult.Suppressed : PublishResult.Published;
    }

    public async Task<LegacyCheckNotePublication?> CaptureOnceAsync(
        AgentTask checkedTask,
        int attempt,
        DateTime dispatchedAt,
        int checkNumber,
        CheckCompactionRecovery episode,
        Guid interpreterSessionId,
        DateTime interpreterGeneration,
        Guid parentSessionId,
        Guid interpretationTaskId,
        Func<CancellationToken, Task>? probe,
        CancellationToken ct)
    {
        var existing = await FindAsync(checkedTask.Id, attempt, dispatchedAt, checkNumber, ct);
        if (existing is not null)
            return existing;
        if (probe is not null)
            await probe(ct);
        return await CaptureAsync(
            checkedTask, attempt, dispatchedAt, checkNumber, episode, interpreterSessionId,
            interpreterGeneration, parentSessionId, interpretationTaskId, ct);
    }

    public async Task<LegacyCheckNotePublication?> CaptureAsync(
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
        await _db.Entry(checkedTask).ReloadAsync(ct);
        if (checkedTask.Attempt != attempt
            || checkedTask.CheckCount != checkNumber
            || checkedTask.DispatchedAt is not DateTime dispatched
            || !SessionGeneration.Equal(SessionGeneration.Normalize(dispatched), dispatchedAt))
            return null;

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
            InterpretationDeadlineAt = now.AddSeconds(Math.Max(1, _settings.CheckInterpreterWaitSeconds)),
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

        var canonical = Canonical(body);
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

    public async Task BindInterpretationAsync(Guid publicationId, CancellationToken ct)
    {
        var publication = await _db.LegacyCheckNotePublications.FirstAsync(p => p.Id == publicationId, ct);
        if (publication.State != LegacyCheckNoteState.Captured)
            return;
        if (publication.InterpretationTaskId is Guid linked
            && await _db.AgentTasks.AnyAsync(t => t.Id == linked, ct))
        {
            await _boundary.ReachedAsync("legacy-run-link-committed", publication.Id, ct);
            return;
        }

        if (!await AdmissionStillHoldsAsync(publication, ct) || !ChecksEnabled())
            return;
        if (UtcNow() >= publication.InterpretationDeadlineAt)
            return;

        var now = UtcNow();
        var runId = Guid.NewGuid();
        var deadline = publication.InterpretationDeadlineAt;
        _db.AgentTasks.Add(new AgentTask
        {
            Id = runId,
            RootTaskId = runId,
            Title = "captured check",
            Goal = "interpret the captured observation",
            Role = AgentTaskRole.Check,
            Kind = AgentTaskKind.Worker,
            Status = AgentTaskStatus.Queued,
            AgentId = publication.PhysicalAgentId,
            AgentSessionId = publication.InterpreterSessionId,
            WorkingDirectory = "",
            CreatedAt = now,
        });
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = runId,
            Type = AgentTaskEventType.Created,
            Detail = "captured interpretation",
            At = now,
        });
        publication.InterpretationTaskId = runId;
        publication.InterpretationDeadlineAt = deadline;
        publication.ConcurrencyToken = Guid.NewGuid();
        await _db.SaveChangesAsync(ct);
        await _boundary.ReachedAsync("legacy-run-link-committed", publication.Id, ct);
    }

    public async Task SelectOutcomeAsync(Guid publicationId, string snapshot, CancellationToken ct)
    {
        var publication = await _db.LegacyCheckNotePublications.FirstAsync(p => p.Id == publicationId, ct);
        await _db.Entry(publication).ReloadAsync(ct);
        if (!string.IsNullOrEmpty(publication.InterpretationSnapshotJson))
            return;
        publication.InterpretationSnapshotJson = snapshot;
        publication.ConcurrencyToken = Guid.NewGuid();
        await _boundary.ReachedAsync("legacy-outcome-selected", publication.Id, ct);
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
        }
    }

    public async Task<string?> ConflictingReplayAsync(
        Guid publicationId, string? body, int? checkNumber, int? attempt, DateTime? dispatchedAt, CancellationToken ct)
    {
        var publication = await _db.LegacyCheckNotePublications.AsNoTracking()
            .FirstAsync(p => p.Id == publicationId, ct);
        var canonical = body is null ? null : Canonical(body);
        var changed = (checkNumber is int number && number != publication.CheckNumber)
            || (attempt is int nextAttempt && nextAttempt != publication.CheckedTaskAttempt)
            || (dispatchedAt is DateTime dispatched
                && !SessionGeneration.Equal(dispatched, publication.CheckedTaskDispatchedAt))
            || (canonical is not null && publication.Body is not null && canonical != publication.Body);
        return changed ? "conflict" : null;
    }

    public async Task RecoverCapturedAsync(Guid publicationId, CancellationToken ct)
    {
        await _boundary.ReachedAsync("legacy-captured-scan", publicationId, ct);
        var publication = await _db.LegacyCheckNotePublications.FirstOrDefaultAsync(p => p.Id == publicationId, ct);
        if (publication is null || publication.State != LegacyCheckNoteState.Captured)
            return;

        if (publication.InterpretationTaskId is Guid runId)
        {
            var run = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == runId, ct);
            if (run?.Status == AgentTaskStatus.Succeeded && !string.IsNullOrWhiteSpace(run.Result))
            {
                if (string.IsNullOrEmpty(publication.InterpretationSnapshotJson))
                    await SelectOutcomeAsync(publication.Id, run.Result, ct);
                await _db.Entry(publication).ReloadAsync(ct);
                if (publication.State == LegacyCheckNoteState.Captured)
                    await ProduceAsync(publication, publication.InterpretationSnapshotJson ?? run.Result!, "recovered", false, null, ct);
            }

            return;
        }

        if (!ChecksEnabled() || UtcNow() >= publication.InterpretationDeadlineAt || !await AdmissionStillHoldsAsync(publication, ct))
        {
            await ProduceAsync(publication, "Captured check could not be interpreted.", "degraded", false, null, ct);
            return;
        }
    }

    private async Task<LegacyCheckNotePublication?> FindAsync(
        Guid checkedTaskId, int attempt, DateTime dispatchedAt, int checkNumber, CancellationToken ct) =>
        await _db.LegacyCheckNotePublications.FirstOrDefaultAsync(p =>
            p.CheckedTaskId == checkedTaskId
            && p.CheckedTaskAttempt == attempt
            && p.CheckedTaskDispatchedAt == dispatchedAt
            && p.CheckNumber == checkNumber, ct);

    private bool ChecksEnabled() => _settings.Enabled && _settings.CheckEnabled && _settings.CheckInterpreterEnabled;

    private async Task<bool> AdmissionStillHoldsAsync(LegacyCheckNotePublication publication, CancellationToken ct)
    {
        var session = await _db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == publication.InterpreterSessionId, ct);
        return session is not null
            && SessionGeneration.Equal(session.StartedAt, publication.InterpreterAcceptedStartedAt);
    }

    private static string Canonical(string body) =>
        body.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Trim();

    private DateTime UtcNow()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}
