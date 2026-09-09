using System.Text.Json;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class SpecialistRequestService
{
    /// <summary>Called by the dedicated worker, never by a Check request or the supervisor's launch lane.</summary>
    public async Task ReconcileAsync(CancellationToken ct)
    {
        var pending = await db.SpecialistRequests.AsNoTracking().Where(r => r.CompletedAt == null)
            .OrderBy(r => r.DeadlineAt).Take(32).Select(r => r.Id).ToListAsync(ct);
        foreach (var id in pending) await AdvanceAsync(id, ct);
        var candidates = await db.StandingSpecialistCandidateStates.AsNoTracking().Where(c => c.Enabled
            && c.PhysicalAgentId != null && c.AgentKind == AgentKind.ClaudeCode
            && c.Status != StandingSpecialistCandidateStatus.PendingDependency && c.Status != StandingSpecialistCandidateStatus.Disabled)
            .OrderBy(c => c.UpdatedAt).Take(32).Select(c => c.Id).ToListAsync(ct);
        foreach (var id in candidates) await AuthorizeQualificationAsync(id, ct);
    }

    public async Task<bool> AuthorizeQualificationAsync(Guid candidateId, CancellationToken ct)
    {
        var candidate = await db.StandingSpecialistCandidateStates.AsNoTracking().SingleAsync(c => c.Id == candidateId, ct);
        var seat = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == candidate.PhysicalAgentId, ct);
        if (!candidate.Enabled || seat is null || !Guid.TryParse(seat.PersistentSessionId, out var sessionId)
            || await StandingSpecialistSeatPolicy.StartRefusalAsync(db, seat, settings.Value, true, ct) is not null) return false;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session?.Status != SessionStatus.Running || session.AgentKind != AgentKind.ClaudeCode) return false;
        var evidence = await evidenceReader.ReadAsync(seat, session, ct);
        if (evidence is null) return false;
        var changed = candidate.Fingerprint != evidence.Fingerprint;
        if (!changed && candidate.QualificationAuthorization == candidate.ClaimedQualificationAuthorization) return false;
        if (await availability.IsHeldAsync(candidate.AgentKind, candidate.ModelAlias, ct)
            || await quota.EvaluateAsync(seat.Kind, SubscriptionUsageKey.For(seat, seat.Kind), ct) is not null) return false;
        await runtime.CatchUpTranscriptAsync(sessionId, ct);
        if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct)) return false;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('antiphon.capacity.counted-tasks'))", ct);
        await LockOwnerAsync(candidate.AgentId, ct);
        var current = await db.StandingSpecialistCandidateStates.SingleAsync(c => c.Id == candidate.Id, ct);
        var owner = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == candidate.AgentId, ct);
        var currentSeat = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == seat.Id, ct);
        var currentSession = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (!current.Enabled || current.Fingerprint != candidate.Fingerprint
            || current.QualificationAuthorization != candidate.QualificationAuthorization
            || current.ClaimedQualificationAuthorization != candidate.ClaimedQualificationAuthorization
            || owner is null || currentSeat?.UpdatedAt != seat.UpdatedAt || currentSeat.PersistentSessionId != seat.PersistentSessionId
            || currentSession?.StartedAt != session.StartedAt || currentSession.Status != SessionStatus.Running
            || await StandingSpecialistSeatPolicy.StartRefusalAsync(db, currentSeat, settings.Value, true, ct) is not null
            || await db.SpecialistRequests.AnyAsync(r => r.QualificationCandidateId == current.Id && r.CompletedAt == null, ct)
            || await db.AgentTasks.AnyAsync(t => t.AgentId == seat.Id && (t.Status == AgentTaskStatus.Queued
                || t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked), ct)
            || await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending, ct))
        { db.Entry(current).State = EntityState.Detached; return false; }
        var now = time.GetUtcNow().UtcDateTime;
        if (changed) current.QualificationAuthorization = Guid.NewGuid();
        current.ClaimedQualificationAuthorization = current.QualificationAuthorization;
        current.Status = StandingSpecialistCandidateStatus.Qualifying;
        current.QualifiedAt = null;
        current.Fingerprint = evidence.Fingerprint;
        current.CapabilityFingerprint = evidence.CapabilityFingerprint;
        current.MaxInputUtf8Bytes = evidence.MaxInputUtf8Bytes;
        current.SessionId = sessionId;
        current.SessionStartedAt = session.StartedAt;
        current.ProfileRevisionId = session.TuiProfileRevisionId;
        current.UpdatedAt = now;
        current.Reason = "One authorized two-fixture qualification batch is pending.";
        var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == owner.Id, ct);
        db.SpecialistRequests.Add(new SpecialistRequest
        {
            Id = Guid.NewGuid(), AgentId = owner.Id, Purpose = SpecialistRequestPurpose.Qualification,
            QualificationCandidateId = current.Id, QualificationAuthorization = current.QualificationAuthorization,
            ConfigurationRevision = routing?.ConcurrencyToken, Status = SpecialistRequestStatus.Pending,
            StartedAt = now, DeadlineAt = now.AddSeconds(2 * Math.Max(1, settings.Value.CheckInterpreterWaitSeconds)),
            Title = "Check interpreter qualification", Facts = JsonSerializer.Serialize(SpecialistQualificationContract.CreateBatch(now)),
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.Entry(current).State = EntityState.Detached;
        await PublishChangedAsync(owner.Id, ct);
        return true;
    }

    private async Task AdvanceQualificationAsync(SpecialistRequest request, CancellationToken ct)
    {
        if (time.GetUtcNow().UtcDateTime >= request.DeadlineAt)
        {
            await CompleteWithoutAttemptAsync(request, SpecialistAttemptOutcome.ExpiredBeforeDispatch, "The authorized qualification batch expired.", ct);
            return;
        }
        var candidate = await db.StandingSpecialistCandidateStates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == request.QualificationCandidateId, ct);
        if (candidate is not { Enabled: true, Status: StandingSpecialistCandidateStatus.Qualifying }
            || candidate.ClaimedQualificationAuthorization != request.QualificationAuthorization)
        {
            await CancelAsync(request.Id, SpecialistAttemptOutcome.Disabled, ct);
            return;
        }
        var seat = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == candidate.PhysicalAgentId, ct);
        if (seat is null || !Guid.TryParse(seat.PersistentSessionId, out var sessionId)) return;
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session?.Status != SessionStatus.Running) return;
        if (await StandingSpecialistSeatPolicy.StartRefusalAsync(db, seat, settings.Value, true, ct) is not null) return;
        var evidence = await evidenceReader.ReadAsync(seat, session, ct);
        if (evidence is null || evidence.Fingerprint != candidate.Fingerprint)
        {
            await CancelAsync(request.Id, SpecialistAttemptOutcome.IdentityMismatch, ct);
            return;
        }
        if (await availability.IsHeldAsync(candidate.AgentKind, candidate.ModelAlias, ct)
            || await quota.EvaluateAsync(seat.Kind, SubscriptionUsageKey.For(seat, seat.Kind), ct) is not null) return;
        if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct)) return;
        await ClaimAsync(request, candidate, seat, session, evidence, ct);
    }
}
