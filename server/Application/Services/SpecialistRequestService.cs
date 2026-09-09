using System.Text.Json;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Durable logical Check requests. A poll may advance a request but owns no private retry clock:
/// every attempt, winner and consequence is committed under the same logical owner lock used by
/// routing edits. Process starts and qualification are deliberately outside the Check path.
/// </summary>
public sealed partial class SpecialistRequestService(AppDbContext db, IOptions<DelegationSettings> settings,
    TimeProvider time, IModelAvailability availability, ISpecialistExecutionEvidenceReader evidenceReader,
    SubscriptionQuotaGate quota, AgentSessionRuntime runtime, IEventBus events, ILogger<SpecialistRequestService> logger)
{
    public async Task<SpecialistRun> RunCheckAsync(AgentTask checkedTask, int checkNumber, string digest, CancellationToken ct,
        DelegateCheckProbe.CheckFacts? snapshot = null)
    {
        var began = time.GetUtcNow().UtcDateTime;
        var owner = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.StandingSpecialistOwnerId == a.Id
            && a.StandingSpecialistRole == AgentTaskRole.Check, ct);
        if (owner is null) return new(SpecialistRunOutcome.ProvisionFailed, null, 0, 0, null, "No managed interpreter exists.");
        Guid requestId;
        await using (var transaction = await db.Database.BeginTransactionAsync(ct))
        {
            await LockOwnerAsync(owner.Id, ct);
            var request = await db.SpecialistRequests.AsNoTracking().SingleOrDefaultAsync(r => r.CheckedTaskId == checkedTask.Id
                && r.CheckNumber == checkNumber && r.Purpose == SpecialistRequestPurpose.Check, ct);
            if (request is null)
            {
                var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == owner.Id, ct);
                request = new SpecialistRequest
                {
                    Id = Guid.NewGuid(), AgentId = owner.Id, CheckedTaskId = checkedTask.Id, CheckNumber = checkNumber,
                    Purpose = SpecialistRequestPurpose.Check, Status = SpecialistRequestStatus.Pending,
                    ConfigurationRevision = routing?.ConcurrencyToken, StartedAt = began,
                    DeadlineAt = began.AddSeconds(Math.Max(1, settings.Value.CheckInterpreterWaitSeconds)),
                    Title = CheckInterpretation.BuildTitle(checkedTask, checkNumber),
                    Facts = CheckInterpretation.BuildGoal(checkedTask, checkNumber, digest),
                    FactsSnapshotJson = snapshot is null ? null : JsonSerializer.Serialize(snapshot), CallerMessageId = Guid.NewGuid(),
                };
                db.SpecialistRequests.Add(request);
                await db.SaveChangesAsync(ct);
                db.Entry(request).State = EntityState.Detached;
            }
            requestId = request.Id;
            await transaction.CommitAsync(ct);
        }
        try
        {
            while (true)
            {
                await AdvanceAsync(requestId, ct);
                var request = await db.SpecialistRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, ct);
                if (request.CompletedAt is not null)
                {
                    var attempts = await db.SpecialistAttempts.AsNoTracking().Where(a => a.RequestId == requestId).ToListAsync(ct);
                    var last = attempts.OrderBy(a => a.Ordinal).LastOrDefault();
                    return new(request.Status == SpecialistRequestStatus.Succeeded ? SpecialistRunOutcome.Succeeded
                        : request.Outcome == SpecialistAttemptOutcome.Busy ? SpecialistRunOutcome.Busy
                        : request.Outcome == SpecialistAttemptOutcome.Disabled ? SpecialistRunOutcome.Disabled
                        : request.Status == SpecialistRequestStatus.Expired ? SpecialistRunOutcome.Timeout : SpecialistRunOutcome.Failed,
                        request.Reading, attempts.Sum(a => a.CostUsd), (int)Math.Max(0, (time.GetUtcNow().UtcDateTime - began).TotalMilliseconds),
                        last?.TaskId, request.Reason, RequestId: request.Id);
                }
                var remaining = request.DeadlineAt - time.GetUtcNow().UtcDateTime;
                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining < TimeSpan.FromSeconds(1) ? remaining : TimeSpan.FromSeconds(1), time, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Persist the caller's loss of interest; active work remains owned and may settle its
            // own task. It can never become this request's winner after cancellation.
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await CancelAsync(requestId, SpecialistAttemptOutcome.CallerCanceled, cleanup.Token);
            throw;
        }
    }

    public async Task AdvanceAsync(Guid requestId, CancellationToken ct)
    {
        var request = await db.SpecialistRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, ct);
        if (request.CompletedAt is not null) return;
        var pending = await db.SpecialistAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.RequestId == requestId && a.CompletedAt == null, ct);
        if (pending is not null)
        {
            await ObserveAttemptAsync(request, pending, ct);
            return;
        }
        if (request.Purpose == SpecialistRequestPurpose.Qualification)
        {
            await AdvanceQualificationAsync(request, ct);
            return;
        }
        if (time.GetUtcNow().UtcDateTime >= request.DeadlineAt)
        {
            await CompleteWithoutAttemptAsync(request, SpecialistAttemptOutcome.ExpiredBeforeDispatch, "The Check deadline expired.", ct);
            return;
        }
        var owner = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == request.AgentId, ct);
        if (owner is null || await StandingSpecialistSeatPolicy.StartRefusalAsync(db, owner, settings.Value, true, ct) is not null)
        {
            await CompleteWithoutAttemptAsync(request, SpecialistAttemptOutcome.Disabled, "Check interpretation is disabled or requires human continuation.", ct);
            return;
        }
        var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == owner.Id, ct);
        if (routing?.ConcurrencyToken != request.ConfigurationRevision)
        {
            await CompleteWithoutAttemptAsync(request, SpecialistAttemptOutcome.Disabled, "Routing changed after this Check was captured.", ct);
            return;
        }
        var pairs = routing is null ? new[] { new RoutingCandidate(owner.Kind, owner.ModelLevel) }
            : RoutingCandidate.Parse(routing.CandidatesJson).ToArray();
        var candidates = await db.StandingSpecialistCandidateStates.AsNoTracking().Where(c => c.AgentId == owner.Id && c.Enabled).ToListAsync(ct);
        var tried = await db.SpecialistAttempts.AsNoTracking().Where(a => a.RequestId == requestId).Select(a => a.CandidateId).ToListAsync(ct);
        if (tried.Count >= 2)
        {
            await CompleteWithoutAttemptAsync(request, SpecialistAttemptOutcome.TaskFailedUnknown, "Both submitted Check attempts failed.", ct);
            return;
        }
        var health = await db.StandingSpecialistHealths.AsNoTracking().SingleOrDefaultAsync(h => h.AgentId == owner.Id, ct);
        // A recovered primary does not thrash a working fallback before its residence is up.
        var preferActive = health?.ActiveSince is DateTime since && time.GetUtcNow().UtcDateTime - since < TimeSpan.FromMinutes(5);
        var ordered = candidates.Where(c => pairs.Contains(new(c.AgentKind, c.ModelLevel)) && !tried.Contains(c.Id))
            .OrderBy(c => preferActive && c.Id == health!.ActiveCandidateId ? -1 : Array.IndexOf(pairs, new(c.AgentKind, c.ModelLevel)));
        var refusal = SpecialistAttemptOutcome.DeclaredButUnprovisioned;
        foreach (var candidate in ordered)
        {
            if (candidate.Status != StandingSpecialistCandidateStatus.Qualified || candidate.QualifiedAt is null) continue;
            var seat = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == candidate.PhysicalAgentId, ct);
            if (seat is null || !Guid.TryParse(seat.PersistentSessionId, out var sessionId)) continue;
            var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session?.Status != SessionStatus.Running || candidate.SessionId != sessionId || candidate.SessionStartedAt != session.StartedAt) continue;
            if (await StandingSpecialistSeatPolicy.StartRefusalAsync(db, seat, settings.Value, true, ct) is not null) continue;
            SpecialistExecutionEvidence? evidence;
            try
            {
                if (await availability.IsHeldAsync(candidate.AgentKind, candidate.ModelAlias, ct)) { refusal = SpecialistAttemptOutcome.Held; continue; }
                if (await quota.EvaluateAsync(seat.Kind, SubscriptionUsageKey.For(seat, seat.Kind), ct) is not null)
                { refusal = SpecialistAttemptOutcome.QuotaUnavailable; continue; }
                evidence = await evidenceReader.ReadAsync(seat, session, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Check eligibility could not be verified for candidate {CandidateId}", candidate.Id);
                refusal = SpecialistAttemptOutcome.Held; // unavailable evidence never authorizes work
                continue;
            }
            if (!Qualified(candidate, evidence)) continue;
            if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct)) { refusal = SpecialistAttemptOutcome.Busy; continue; }
            if (await ClaimAsync(request, candidate, seat, session, evidence!, ct)) return;
            refusal = SpecialistAttemptOutcome.Busy;
        }
        await CompleteWithoutAttemptAsync(request, refusal, refusal == SpecialistAttemptOutcome.Busy
            ? "All qualified candidates are busy." : "No declared candidate is currently eligible for this Check.", ct);
    }

    private bool Qualified(StandingSpecialistCandidateState candidate, SpecialistExecutionEvidence? current)
    {
        if (current is null || candidate.Fingerprint != current.Fingerprint || candidate.CapabilityFingerprint != current.CapabilityFingerprint
            || candidate.MaxInputUtf8Bytes != current.MaxInputUtf8Bytes || candidate.QualificationEvidenceJson is null) return false;
        try
        {
            var qualification = JsonSerializer.Deserialize<SpecialistQualificationEvidence>(candidate.QualificationEvidenceJson);
            return qualification is not null && qualification.Fingerprint == current.Fingerprint && evidenceReader.Trusts(qualification);
        }
        catch (JsonException) { return false; }
    }

    private async Task<bool> ClaimAsync(SpecialistRequest expected, StandingSpecialistCandidateState selected,
        Agent seat, AgentSession session, SpecialistExecutionEvidence evidence, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtext('antiphon.capacity.counted-tasks'))", ct);
        await LockOwnerAsync(expected.AgentId, ct);
        var request = await db.SpecialistRequests.SingleAsync(r => r.Id == expected.Id, ct);
        var candidate = await db.StandingSpecialistCandidateStates.AsNoTracking().SingleOrDefaultAsync(c => c.Id == selected.Id, ct);
        var currentSeat = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == seat.Id, ct);
        var currentSession = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == session.Id, ct);
        var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == expected.AgentId, ct);
        var attempts = await db.SpecialistAttempts.AsNoTracking().Where(a => a.RequestId == request.Id).ToListAsync(ct);
        var now = time.GetUtcNow().UtcDateTime;
        var qualification = request.Purpose == SpecialistRequestPurpose.Qualification;
        if (request.CompletedAt is not null || now >= request.DeadlineAt || attempts.Count >= 2
            || attempts.Any(a => a.CompletedAt == null || (!qualification && a.CandidateId == selected.Id))
            || candidate is not { Enabled: true }
            || candidate.Status != (qualification ? StandingSpecialistCandidateStatus.Qualifying : StandingSpecialistCandidateStatus.Qualified)
            || (qualification && (candidate.Id != request.QualificationCandidateId
                || candidate.ClaimedQualificationAuthorization != request.QualificationAuthorization))
            || candidate.Fingerprint != evidence.Fingerprint || candidate.QualificationAuthorization != selected.QualificationAuthorization
            || routing?.ConcurrencyToken != request.ConfigurationRevision || routing is { Enabled: false }
            || currentSeat is null || currentSeat.PersistentSessionId != seat.PersistentSessionId || currentSeat.UpdatedAt != seat.UpdatedAt
            || currentSession?.StartedAt != session.StartedAt || currentSession.Status != SessionStatus.Running
            || await StandingSpecialistSeatPolicy.StartRefusalAsync(db, currentSeat, settings.Value, true, ct) is not null
            || await db.AgentTasks.AnyAsync(t => t.AgentId == seat.Id && (t.Status == AgentTaskStatus.Queued
                || t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked), ct)
            || await db.SessionQueuedMessages.AnyAsync(m => m.AgentSessionId == session.Id && m.Status == QueuedMessageStatus.Pending, ct))
        {
            db.Entry(request).State = EntityState.Detached;
            return false;
        }
        var taskId = Guid.NewGuid();
        var deadline = request.DeadlineAt;
        if (qualification)
            deadline = new[] { deadline, now.AddSeconds(Math.Max(1, settings.Value.CheckInterpreterWaitSeconds)) }.Min();
        // A configured split is not measured calibration. No certificate means the primary
        // retains the full request budget (the observed normal tail exceeds thirty seconds).
        else if (attempts.Count == 0 && evidence.CertifiedFirstAttemptSeconds is int first
            && settings.Value.CheckInterpreterFirstAttemptSeconds == first)
            deadline = new[] { deadline, request.StartedAt.AddSeconds(first) }.Min();
        var goal = qualification ? JsonSerializer.Deserialize<SpecialistQualificationFixture[]>(request.Facts)![attempts.Count].Goal : request.Facts;
        var task = new AgentTask
        {
            Id = taskId, RootTaskId = taskId, Title = request.Title, Goal = goal,
            Kind = AgentTaskKind.Worker, Role = AgentTaskRole.Check, AgentKind = seat.Kind, ModelLevel = seat.ModelLevel,
            SpecialistModelAlias = selected.ModelAlias, SpecialistModelId = seat.ModelId,
            SpecialistEffectiveModelId = session.EffectiveModelId, SpecialistSessionId = session.Id,
            SpecialistSessionStartedAt = session.StartedAt, SpecialistProfileRevisionId = session.TuiProfileRevisionId,
            Workspace = WorkspaceMode.Shared, WorkingDirectory = seat.WorkingDirectory, AgentId = seat.Id, AgentName = seat.Name,
            Ephemeral = false, ReplyTo = AgentTaskReplyTo.None, Status = AgentTaskStatus.Queued, CreatedAt = now, ExecutionDeadlineAt = deadline,
            SpecialistInputPolicyJson = new SpecialistInputPolicy(1, taskId, session.Id, session.StartedAt, seat.Kind,
                DeliveryBackend.ModernConPty, evidence.MaxInputUtf8Bytes, evidence.CapabilityFingerprint).Serialize(),
        };
        db.AgentTasks.Add(task);
        db.SpecialistAttempts.Add(new SpecialistAttempt
        {
            Id = Guid.NewGuid(), RequestId = request.Id, CandidateId = selected.Id, PhysicalAgentId = seat.Id,
            TaskId = taskId, Ordinal = attempts.Count + 1, SessionId = session.Id, SessionStartedAt = session.StartedAt,
            Fingerprint = evidence.Fingerprint, CapabilityFingerprint = evidence.CapabilityFingerprint, StartedAt = now, DeadlineAt = deadline,
        });
        db.AgentTaskEvents.Add(new AgentTaskEvent { Id = Guid.NewGuid(), AgentTaskId = taskId, Type = AgentTaskEventType.Created,
            ModelLevel = seat.ModelLevel, At = now, Detail = $"Check request {request.Id:D}, attempt {attempts.Count + 1}, {seat.Kind}/{seat.ModelLevel}/{selected.ModelAlias}." });
        request.Status = SpecialistRequestStatus.Running;
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.Entry(request).State = EntityState.Detached;
        return true;
    }

    private async Task ObserveAttemptAsync(SpecialistRequest request, SpecialistAttempt attempt, CancellationToken ct)
    {
        var task = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == attempt.TaskId, ct);
        var seat = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == attempt.PhysicalAgentId, ct);
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == attempt.SessionId, ct);
        SpecialistExecutionEvidence? current = null;
        if (seat is not null && session is not null) current = await evidenceReader.ReadAsync(seat, session, ct);
        var sameGeneration = seat?.PersistentSessionId == attempt.SessionId.ToString() && session?.StartedAt == attempt.SessionStartedAt
            && current?.Fingerprint == attempt.Fingerprint;
        SpecialistAttemptVerdict? verdict;
        if (!sameGeneration) verdict = new(SpecialistAttemptOutcome.IdentityMismatch, Reason: "The selected execution generation changed.");
        else if (task is null) verdict = new(SpecialistAttemptOutcome.TaskFailedUnknown, Reason: "The owned Check task is missing.");
        else
        {
            // A missing streamed row is not proof of missing input: pull the native transcript
            // before deciding delivery, timeout, tools or final-response absence.
            await runtime.CatchUpTranscriptAsync(attempt.SessionId, ct);
            var message = await db.SessionQueuedMessages.AsNoTracking().Where(m => m.ExecutionTaskId == task.Id)
                .OrderByDescending(m => m.Sequence).FirstOrDefaultAsync(ct);
            var entries = await db.TranscriptEntries.AsNoTracking().Where(e => e.AgentSessionId == attempt.SessionId
                && e.Sequence > (message == null ? long.MaxValue : message.LastDeliveryBaselineSequence ?? 0))
                .OrderBy(e => e.Sequence).ToListAsync(ct);
            verdict = SpecialistAttemptEvidence.Evaluate(attempt, task, message, entries, time.GetUtcNow().UtcDateTime);
        }
        if (verdict is null) return;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(request.AgentId, ct);
        var liveRequest = await db.SpecialistRequests.SingleAsync(r => r.Id == request.Id, ct);
        var liveAttempt = await db.SpecialistAttempts.SingleAsync(a => a.Id == attempt.Id, ct);
        if (liveAttempt.CompletedAt is not null || liveRequest.CompletedAt is not null)
        { db.Entry(liveRequest).State = EntityState.Detached; db.Entry(liveAttempt).State = EntityState.Detached; return; }
        var now = time.GetUtcNow().UtcDateTime;
        var owner = await db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == request.AgentId, ct);
        var routing = await db.StandingSpecialistRoutings.AsNoTracking().SingleOrDefaultAsync(r => r.AgentId == request.AgentId, ct);
        var publicationAllowed = owner is not null && routing?.ConcurrencyToken == request.ConfigurationRevision
            && await StandingSpecialistSeatPolicy.StartRefusalAsync(db, owner, settings.Value, true, ct) is null;
        if (now >= attempt.DeadlineAt && verdict.Outcome == SpecialistAttemptOutcome.ValidReading)
            verdict = new(SpecialistAttemptOutcome.TimedOutAfterDispatch, Reason: "The reading arrived after its deadline.");
        liveAttempt.CompletedAt = now;
        liveAttempt.Outcome = verdict.Outcome;
        liveAttempt.Reason = verdict.Reason;
        liveAttempt.Reading = verdict.Reading;
        liveAttempt.PromptSequence = verdict.PromptSequence;
        liveAttempt.ReportSequence = verdict.ReportSequence;
        liveAttempt.CostUsd = task?.CostUsd ?? 0;
        var candidate = await db.StandingSpecialistCandidateStates.SingleOrDefaultAsync(c => c.Id == attempt.CandidateId, ct);
        var qualification = request.Purpose == SpecialistRequestPurpose.Qualification;
        if (qualification && verdict.Outcome == SpecialistAttemptOutcome.ValidReading)
        {
            var fixtures = JsonSerializer.Deserialize<SpecialistQualificationFixture[]>(request.Facts)!;
            if (!SpecialistQualificationContract.Validate(fixtures[attempt.Ordinal - 1], verdict.Reading))
                verdict = verdict with { Outcome = SpecialistAttemptOutcome.InvalidReading, Reading = null,
                    Reason = "Qualification reading did not explain the current fixture's evidence." };
            liveAttempt.Outcome = verdict.Outcome;
            liveAttempt.Reading = verdict.Reading;
            liveAttempt.Reason = verdict.Reason;
        }
        var validBatch = false;
        SpecialistQualificationEvidence? batchEvidence = null;
        if (qualification && attempt.Ordinal == 2 && verdict.Outcome == SpecialistAttemptOutcome.ValidReading && current is not null)
        {
            var first = await db.SpecialistAttempts.AsNoTracking().SingleOrDefaultAsync(a => a.RequestId == request.Id && a.Ordinal == 1, ct);
            var fixtures = JsonSerializer.Deserialize<SpecialistQualificationFixture[]>(request.Facts)!;
            if (first?.Outcome == SpecialistAttemptOutcome.ValidReading && first.Fingerprint == current.Fingerprint)
            {
                batchEvidence = new(current.Fingerprint, current.Provenance, [first.TaskId, attempt.TaskId], fixtures.Select(f => f.Nonce).ToArray());
                validBatch = evidenceReader.Trusts(batchEvidence);
            }
            if (!validBatch)
                verdict = verdict with { Outcome = SpecialistAttemptOutcome.InvalidReading, Reading = null,
                    Reason = "Qualification evidence has no trusted current provider provenance." };
        }
        if (candidate is not null)
        {
            var decision = SpecialistFailurePolicy.Evaluate(validBatch ? SpecialistAttemptOutcome.ValidQualificationBatch : verdict.Outcome, candidate.TransientFailures,
                settings.Value.CheckInterpreterTransientFailureThreshold,
                publicationAllowed && (!qualification || verdict.Outcome != SpecialistAttemptOutcome.ValidReading || validBatch)
                    && sameGeneration && candidate.Fingerprint == attempt.Fingerprint
                    && candidate.SessionStartedAt == attempt.SessionStartedAt, true);
            candidate.TransientFailures = decision.TransientFailures;
            if (decision.Quarantine)
            {
                candidate.Status = StandingSpecialistCandidateStatus.Quarantined;
                candidate.QualifiedAt = null;
                candidate.Reason = verdict.Reason;
            }
            candidate.UpdatedAt = now;
        }
        if (qualification)
        {
            var authorized = publicationAllowed && sameGeneration && candidate is { Enabled: true }
                && candidate.Fingerprint == attempt.Fingerprint && candidate.SessionStartedAt == attempt.SessionStartedAt
                && candidate.QualificationAuthorization == request.QualificationAuthorization
                && candidate.ClaimedQualificationAuthorization == request.QualificationAuthorization;
            if (authorized && validBatch && now < request.DeadlineAt)
            {
                candidate!.Status = StandingSpecialistCandidateStatus.Qualified;
                candidate.QualifiedAt = now;
                candidate.QualificationEvidenceJson = JsonSerializer.Serialize(batchEvidence);
                candidate.Reason = "Two current-generation semantic qualification fixtures passed; awaiting a real Check.";
                liveRequest.Status = SpecialistRequestStatus.Succeeded;
                liveRequest.Outcome = SpecialistAttemptOutcome.ValidQualificationBatch;
                liveRequest.CompletedAt = now;
            }
            else if (!authorized || verdict.Outcome != SpecialistAttemptOutcome.ValidReading || now >= request.DeadlineAt)
            {
                liveRequest.Status = authorized ? SpecialistRequestStatus.Failed : SpecialistRequestStatus.Canceled;
                liveRequest.Outcome = authorized ? verdict.Outcome : SpecialistAttemptOutcome.Disabled;
                liveRequest.Reason = verdict.Reason;
                liveRequest.CompletedAt = now;
                if (candidate is { Status: StandingSpecialistCandidateStatus.Qualifying })
                {
                    candidate.Status = StandingSpecialistCandidateStatus.Unqualified;
                    candidate.QualifiedAt = null;
                    candidate.Reason = "Qualification failed; awaiting authorized revalidate. " + verdict.Reason;
                }
            }
        }
        else if (publicationAllowed && sameGeneration && verdict.Outcome == SpecialistAttemptOutcome.ValidReading && now < request.DeadlineAt
            && candidate is { Enabled: true, Status: StandingSpecialistCandidateStatus.Qualified }
            && candidate.Fingerprint == attempt.Fingerprint && candidate.SessionStartedAt == attempt.SessionStartedAt)
        {
            liveRequest.WinnerAttemptId = attempt.Id;
            liveRequest.Reading = verdict.Reading;
            liveRequest.Status = SpecialistRequestStatus.Succeeded;
            liveRequest.Outcome = verdict.Outcome;
            liveRequest.CompletedAt = now;
        }
        else if (!publicationAllowed || now >= request.DeadlineAt)
        {
            liveRequest.Status = publicationAllowed ? SpecialistRequestStatus.Expired : SpecialistRequestStatus.Canceled;
            liveRequest.Outcome = publicationAllowed ? verdict.Outcome : SpecialistAttemptOutcome.Disabled;
            liveRequest.Reason = verdict.Reason;
            liveRequest.CompletedAt = now;
        }
        liveAttempt.Outcome = verdict.Outcome;
        liveAttempt.Reading = verdict.Reading;
        liveAttempt.Reason = verdict.Reason;
        await CancelUnsubmittedAsync(attempt.TaskId, now, ct);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        db.Entry(liveRequest).State = EntityState.Detached;
        db.Entry(liveAttempt).State = EntityState.Detached;
        if (candidate is not null) db.Entry(candidate).State = EntityState.Detached;
        await PublishChangedAsync(request.AgentId, ct);
    }

    private async Task CompleteWithoutAttemptAsync(SpecialistRequest request, SpecialistAttemptOutcome outcome, string reason, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(request.AgentId, ct);
        if (await db.SpecialistAttempts.AnyAsync(a => a.RequestId == request.Id && a.CompletedAt == null, ct)) return;
        var now = time.GetUtcNow().UtcDateTime;
        await db.SpecialistRequests.Where(r => r.Id == request.Id && r.CompletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, now >= request.DeadlineAt ? SpecialistRequestStatus.Expired : SpecialistRequestStatus.Failed)
                .SetProperty(r => r.Outcome, outcome).SetProperty(r => r.Reason, reason).SetProperty(r => r.CompletedAt, now), ct);
        if (request.Purpose == SpecialistRequestPurpose.Qualification)
            await db.StandingSpecialistCandidateStates.Where(c => c.Id == request.QualificationCandidateId
                && c.Status == StandingSpecialistCandidateStatus.Qualifying && c.ClaimedQualificationAuthorization == request.QualificationAuthorization)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, StandingSpecialistCandidateStatus.Unqualified)
                    .SetProperty(c => c.Reason, "Qualification failed; awaiting authorized revalidate. " + reason), ct);
        await transaction.CommitAsync(ct);
    }

    public async Task CancelAsync(Guid requestId, SpecialistAttemptOutcome outcome, CancellationToken ct)
    {
        var request = await db.SpecialistRequests.AsNoTracking().SingleAsync(r => r.Id == requestId, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await LockOwnerAsync(request.AgentId, ct);
        var now = time.GetUtcNow().UtcDateTime;
        await db.SpecialistRequests.Where(r => r.Id == requestId && r.CompletedAt == null).ExecuteUpdateAsync(s =>
            s.SetProperty(r => r.Status, SpecialistRequestStatus.Canceled).SetProperty(r => r.Outcome, outcome).SetProperty(r => r.CompletedAt, now), ct);
        var tasks = await db.SpecialistAttempts.Where(a => a.RequestId == requestId && a.CompletedAt == null).Select(a => a.TaskId).ToListAsync(ct);
        foreach (var taskId in tasks) await CancelUnsubmittedAsync(taskId, now, ct);
        if (request.Purpose == SpecialistRequestPurpose.Qualification)
            await db.StandingSpecialistCandidateStates.Where(c => c.Id == request.QualificationCandidateId
                && c.Status == StandingSpecialistCandidateStatus.Qualifying && c.ClaimedQualificationAuthorization == request.QualificationAuthorization)
                .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, StandingSpecialistCandidateStatus.Unqualified)
                    .SetProperty(c => c.Reason, "Qualification stopped; awaiting authorized revalidate."), ct);
        await transaction.CommitAsync(ct);
    }

    private async Task CancelUnsubmittedAsync(Guid taskId, DateTime now, CancellationToken ct)
    {
        await db.AgentTasks.Where(t => t.Id == taskId && t.Status == AgentTaskStatus.Queued).ExecuteUpdateAsync(s =>
            s.SetProperty(t => t.Status, AgentTaskStatus.Canceled).SetProperty(t => t.CompletedAt, now)
                .SetProperty(t => t.FailureReason, "The owning Check attempt stopped waiting.").SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()), ct);
        await db.SessionQueuedMessages.Where(m => m.ExecutionTaskId == taskId && m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts == 0)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, QueuedMessageStatus.Canceled).SetProperty(m => m.CanceledAt, now), ct);
    }

    private Task<Agent?> LockOwnerAsync(Guid ownerId, CancellationToken ct) => db.Agents.FromSqlInterpolated(
        $"SELECT * FROM \"Agents\" WHERE \"Id\" = {ownerId} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);

    private async Task PublishChangedAsync(Guid ownerId, CancellationToken ct)
    {
        try { await events.PublishToAllAsync("AgentChanged", new { agentId = ownerId }, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { logger.LogWarning(ex, "Specialist transition for {AgentId} committed; notification failed", ownerId); }
    }
}
