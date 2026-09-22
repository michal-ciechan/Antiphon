using System.Text.Json;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0079 coordinator. Discovery is read-only until a fresh observation
/// confirms the predicate; the stop request commits before the runner call;
/// resume is the strict same-id path and never a Fresh start.
/// </summary>
public sealed class CheckCompactionContinuationService
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _time;
    private readonly DelegationSettings _settings;
    private readonly CheckCompactionContinuationGate _gate;
    private readonly ILogger<CheckCompactionContinuationService> _logger;
    private readonly ISessionRunnerClient? _runner;
    private readonly ICompactionContinuationResume? _resume;
    private readonly SessionMessageQueueService? _queue;
    private readonly CheckCompactionBoundary _boundary;
    private readonly IEventBus? _events;
    private readonly SubscriptionQuotaGate? _quota;

    public CheckCompactionContinuationService(
        AppDbContext db,
        TimeProvider time,
        IOptions<DelegationSettings> settings,
        CheckCompactionContinuationGate gate,
        ILogger<CheckCompactionContinuationService> logger,
        ISessionRunnerClient? runner = null,
        ICompactionContinuationResume? resume = null,
        SessionMessageQueueService? queue = null,
        CheckCompactionBoundary? boundary = null,
        IEventBus? events = null,
        SubscriptionQuotaGate? quota = null)
    {
        _db = db;
        _time = time;
        _settings = settings.Value;
        _gate = gate;
        _logger = logger;
        _runner = runner;
        _resume = resume;
        _queue = queue;
        _boundary = boundary ?? new CheckCompactionBoundary();
        _events = events;
        _quota = quota;
    }

    public async Task<int> SweepAsync(CancellationToken ct)
    {
        await ReconcileUnresolvedAsync(ct);
        if (!_gate.TryEnterDiscovery(UtcNow()))
            return 0;
        if (!DiscoveryEnabled())
            return 0;

        var seats = await _db.Agents.AsNoTracking()
            .Where(StandingSpecialistSeatPolicy.Seat)
            .Select(a => a.Id)
            .ToListAsync(ct);
        foreach (var seatId in seats)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                await DiscoverSeatAsync(seatId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Compaction continuation discovery failed for seat {AgentId}", seatId);
            }
        }

        return 0;
    }

    private bool DiscoveryEnabled() =>
        _settings.Enabled
        && _settings.CheckEnabled
        && _settings.CheckInterpreterEnabled
        && _settings.CheckCompactionContinuationWaitMinutes >= 10;

    private async Task ReconcileUnresolvedAsync(CancellationToken ct)
    {
        var ids = await _db.CheckCompactionRecoveries.AsNoTracking()
            .Where(r => CheckCompactionRecoveryStates.Unresolved.Contains(r.State))
            .Select(r => r.Id)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var episode = await _db.CheckCompactionRecoveries.FirstOrDefaultAsync(r => r.Id == id, ct);
                if (episode is null || !CheckCompactionRecoveryStates.IsUnresolved(episode.State))
                    continue;
                await AdvanceAsync(episode, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogError(ex, "Compaction continuation reconcile failed for episode {EpisodeId}", id);
                _db.ChangeTracker.Clear();
            }
        }
    }

    private async Task DiscoverSeatAsync(Guid seatId, CancellationToken ct)
    {
        if (await CheckCompactionAdmission.BlocksAutomaticRestartAsync(_db, seatId, ct))
            return;

        var scope = await BuildScopeAsync(seatId, ct);
        if (scope is null)
            return;
        var entries = await LoadFactsAsync(scope.SessionId, ct);
        var verdict = CompactionContinuationPolicy.Evaluate(
            scope, entries, _settings.CheckCompactionContinuationWaitMinutes, UtcNow());
        if (!verdict.Overdue || verdict.NativeBoundaryIdentity is null || verdict.NativeContinuationIdentity is null)
            return;

        var observation = await ObserveAsync(scope.SessionId, ct);
        if (observation is null
            || !observation.IsSuccessful
            || !string.Equals(observation.NativeBoundaryId, verdict.NativeBoundaryIdentity, StringComparison.Ordinal)
            || !string.Equals(observation.NativeContinuationId, verdict.NativeContinuationIdentity, StringComparison.Ordinal))
            return;

        var again = await LoadFactsAsync(scope.SessionId, ct);
        var confirmed = CompactionContinuationPolicy.Evaluate(
            scope, again, _settings.CheckCompactionContinuationWaitMinutes, UtcNow());
        if (!confirmed.Overdue
            || confirmed.NativeBoundaryIdentity != verdict.NativeBoundaryIdentity
            || confirmed.BoundarySequence != verdict.BoundarySequence)
            return;

        var accepted = SessionGeneration.Normalize(scope.AcceptedStartedAt);
        var boundaryId = confirmed.NativeBoundaryIdentity!;
        var existing = await _db.CheckCompactionRecoveries.FirstOrDefaultAsync(r =>
            r.PhysicalAgentId == seatId
            && r.SessionId == scope.SessionId
            && r.AcceptedStartedAt == accepted
            && r.BoundaryIdentity == boundaryId, ct);
        if (existing is not null)
            return;

        var boundary = again.First(e => e.NativeId == boundaryId);
        var continuation = again.First(e => e.Sequence == confirmed.ContinuationSequence);
        var now = UtcNow();
        var episode = new CheckCompactionRecovery
        {
            Id = Guid.NewGuid(),
            PhysicalAgentId = seatId,
            SessionId = scope.SessionId,
            AcceptedStartedAt = accepted,
            BoundaryIdentity = boundaryId,
            BoundarySequence = confirmed.BoundarySequence,
            ContinuationSequence = confirmed.ContinuationSequence,
            NativeContinuationIdentity = confirmed.NativeContinuationIdentity,
            OwningCheckTaskId = await FindOwningCheckAsync(scope.SessionId, again, confirmed.OrdinaryPromptSequence, ct),
            OrdinaryPromptSequence = confirmed.OrdinaryPromptSequence,
            BoundaryTimestamp = boundary.EventTime,
            BoundaryCreatedAt = boundary.CreatedAt,
            ContinuationTimestamp = continuation.EventTime,
            ContinuationCreatedAt = continuation.CreatedAt,
            ConfiguredThresholdMinutes = _settings.CheckCompactionContinuationWaitMinutes,
            DetectedAt = now,
            ObservedAt = now,
            EvidenceJson = Evidence(confirmed, observation),
            State = CheckCompactionRecoveryState.Confirmed,
            Reason = "overdue",
            ObservationBindingIdentity = observation.BindingIdentity,
            ObservationTranscriptRevision = observation.TranscriptRevision,
            ObservationOutputRevision = observation.OutputRevision,
        };
        var supervision = await GetOrCreateStateAsync(seatId, ct);
        if (supervision.ActiveCompactionRecoveryId is not null)
            return;

        await _boundary.ReachedAsync("observed", episode.Id, ct);
        supervision.ActiveCompactionRecoveryId = episode.Id;
        supervision.UpdatedAt = now;
        _db.CheckCompactionRecoveries.Add(episode);
        AddIncident(seatId, scope.SessionId, AgentIncidentKind.CompactionContinuationStalled, AlertSeverity.Warning,
            "Silent auto-compaction continuation is overdue. No stop has been requested.");
        await _db.SaveChangesAsync(ct);
        await PublishAsync(seatId, ct);
        await AdvanceAsync(episode, ct);
    }

    private async Task AdvanceAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        await CancelRetiredBriefsAsync(episode, ct);
        if (await SupersededByOperatorAsync(episode, ct))
            return;

        if (!DiscoveryEnabled()
            && episode.State is CheckCompactionRecoveryState.Stopped or CheckCompactionRecoveryState.Confirmed
            && episode.StopRequestedAt is null)
        {
            if (episode.State == CheckCompactionRecoveryState.Stopped)
                await TransitionAsync(episode, CheckCompactionRecoveryState.DisabledNeedsDecision, "disabled", ct);
            return;
        }

        if (!DiscoveryEnabled() && episode.State == CheckCompactionRecoveryState.Stopped)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.DisabledNeedsDecision, "disabled", ct);
            return;
        }

        switch (episode.State)
        {
            case CheckCompactionRecoveryState.Confirmed:
                await BeginStopAsync(episode, ct);
                break;
            case CheckCompactionRecoveryState.StopRequested:
                await FinishStopAsync(episode, ct);
                break;
            case CheckCompactionRecoveryState.Stopped:
                await ReleaseOccupantsAsync(episode, afterStop: true, ct);
                await ResumeOnceAsync(episode, ct);
                break;
            case CheckCompactionRecoveryState.ResumeReserved:
                await AdoptLaunchAsync(episode, ct);
                break;
            case CheckCompactionRecoveryState.AwaitingCheck:
                await TryReceiptAsync(episode, ct);
                break;
            case CheckCompactionRecoveryState.NeedsDecision:
            case CheckCompactionRecoveryState.DisabledNeedsDecision:
                await ReleaseOccupantsAsync(episode, afterStop: false, ct);
                break;
        }
    }

    private async Task BeginStopAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        var supervision = await GetOrCreateStateAsync(episode.PhysicalAgentId, ct);
        if (supervision.Suspended)
        {
            await SupersedeAsync(episode, "suspended", ct);
            return;
        }

        if (!CheckCompactionBudget.Allows(
                supervision.LastAutomaticCompactionRestartAt,
                supervision.CompactionRestartReceiptEligible,
                UtcNow()))
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "budget", ct);
            await ReleaseOccupantsAsync(episode, afterStop: false, ct);
            return;
        }

        var scope = await BuildScopeAsync(episode.PhysicalAgentId, ct);
        if (scope is null)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "scope", ct);
            return;
        }

        if (CheckCompactionScope.Refusal(scope) is { } refusal)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, refusal, ct);
            return;
        }

        if (scope.SessionId != episode.SessionId
            || !SessionGeneration.Equal(scope.AcceptedStartedAt, episode.AcceptedStartedAt))
        {
            await SupersedeAsync(episode, "generation", ct);
            return;
        }

        var facts = await LoadFactsAsync(episode.SessionId, ct);
        var verdict = CompactionContinuationPolicy.Evaluate(
            scope, facts, episode.ConfiguredThresholdMinutes, UtcNow());
        if (verdict.Reason == "progress")
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.AbortedProgress, "progress", ct);
            return;
        }

        if (!verdict.Overdue)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, verdict.Reason, ct);
            return;
        }

        if (_runner is null || episode.NativeContinuationIdentity is null)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "unsupported", ct);
            return;
        }

        var now = UtcNow();
        episode.AttemptId = Guid.NewGuid();
        episode.StopRequestedAt = now;
        episode.State = CheckCompactionRecoveryState.StopRequested;
        episode.Reason = "stop-requested";
        episode.ConcurrencyToken = Guid.NewGuid();
        supervision.LastAutomaticCompactionRestartAt = now;
        supervision.CompactionRestartReceiptEligible = false;
        supervision.UpdatedAt = now;
        AddIncident(episode.PhysicalAgentId, episode.SessionId,
            AgentIncidentKind.CompactionContinuationRestartRequested, AlertSeverity.Warning,
            $"Stop requested for attempt {episode.AttemptId:D} before any runner call. Actor {CheckCompactionRecovery.Actor}; authorization {CheckCompactionRecovery.Authorization}.");
        await _boundary.ReachedAsync("before-stop-commit", episode.Id, ct);
        await _db.SaveChangesAsync(ct);
        await _boundary.ReachedAsync("stop-committed", episode.Id, ct);
        await PublishAsync(episode.PhysicalAgentId, ct);
        await FinishStopAsync(episode, ct);
    }

    private async Task FinishStopAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        if (_runner is null || episode.AttemptId is not Guid attempt
            || episode.ObservationBindingIdentity is null
            || episode.ObservationTranscriptRevision is null
            || episode.ObservationOutputRevision is null
            || episode.NativeContinuationIdentity is null)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "unsupported", ct);
            return;
        }

        var supervision = await _db.AgentSupervisionStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentId == episode.PhysicalAgentId, ct);
        if (supervision?.Suspended == true)
        {
            await SupersedeAsync(episode, "suspended", ct);
            return;
        }

        await _boundary.ReachedAsync("before-stop-rpc", episode.Id, ct);
        CompactionContinuationStopResult result;
        try
        {
            result = await _runner.StopCompactionContinuationAsync(episode.SessionId, new CompactionContinuationStopRequest(
                attempt,
                episode.AcceptedStartedAt,
                episode.BoundaryIdentity,
                episode.NativeContinuationIdentity,
                episode.ConfiguredThresholdMinutes,
                episode.ObservationBindingIdentity,
                episode.ObservationTranscriptRevision.Value,
                episode.ObservationOutputRevision.Value), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Conditional compaction stop for {EpisodeId} returned no exit proof", episode.Id);
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "stop-lost", ct);
            return;
        }

        if (!result.ConfirmsExit
            || !SessionGeneration.Equal(result.AcceptedStartedAt, episode.AcceptedStartedAt))
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, result.Outcome, ct);
            return;
        }

        var session = await _db.AgentSessions.FirstOrDefaultAsync(s => s.Id == episode.SessionId, ct);
        if (session is not null && session.TerminationSource != SessionTerminationSource.OperatorRequest)
        {
            session.Status = SessionStatus.Stopped;
            session.EndedAt = UtcNow();
            session.TerminationSource = SessionTerminationSource.CompactionContinuationRecovery;
            session.ExitCode = 0;
        }

        episode.State = CheckCompactionRecoveryState.Stopped;
        episode.StopOutcomeAt = UtcNow();
        episode.Reason = result.Outcome;
        episode.ConcurrencyToken = Guid.NewGuid();
        await _boundary.ReachedAsync("stopped-committed", episode.Id, ct);
        await _db.SaveChangesAsync(ct);
        await PublishAsync(episode.PhysicalAgentId, ct);

        if (!DiscoveryEnabled())
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.DisabledNeedsDecision, "disabled", ct);
            return;
        }

        await ReleaseOccupantsAsync(episode, afterStop: true, ct);
        await ResumeOnceAsync(episode, ct);
    }

    private async Task ResumeOnceAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        if (episode.State != CheckCompactionRecoveryState.Stopped)
            return;
        if (_resume is null)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "resume-unavailable", ct);
            return;
        }

        CompactionResumeResult result;
        try
        {
            await _boundary.ReachedAsync("before-resume-enqueue", episode.Id, ct);
            result = await _resume.ResumeAsync(episode.Id, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "resume-failed", ct);
            return;
        }

        await _db.Entry(episode).ReloadAsync(ct);
        if (!result.Accepted)
        {
            if (result.Outcome is "standing_start_intent_revoked")
                await SupersedeAsync(episode, result.Outcome, ct);
            else if (episode.State == CheckCompactionRecoveryState.Stopped)
                await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, result.Outcome, ct);
            return;
        }

        if (episode.State == CheckCompactionRecoveryState.Stopped)
        {
            episode.State = CheckCompactionRecoveryState.ResumeReserved;
            episode.ResumeSessionId = result.SessionId ?? episode.SessionId;
            episode.ResumeAcceptedStartedAt = result.AcceptedStartedAt;
            episode.LaunchOutcome = result.Outcome;
            episode.ConcurrencyToken = Guid.NewGuid();
            await _db.SaveChangesAsync(ct);
            await _boundary.ReachedAsync("resume-committed", episode.Id, ct);
        }

        await PublishAsync(episode.PhysicalAgentId, ct);
    }

    private async Task AdoptLaunchAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        if (episode.ResumeSessionId is not Guid sessionId || episode.ResumeAcceptedStartedAt is not DateTime generation)
            return;
        var session = await _db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || !SessionGeneration.Equal(session.StartedAt, generation))
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "resume-generation", ct);
            return;
        }

        if (session.Status is SessionStatus.Failed)
        {
            await TransitionAsync(episode, CheckCompactionRecoveryState.NeedsDecision, "launch-failed", ct);
            return;
        }

        if (session.Status == SessionStatus.Running)
        {
            episode.State = CheckCompactionRecoveryState.AwaitingCheck;
            episode.LaunchOutcome = "running";
            episode.ConcurrencyToken = Guid.NewGuid();
            AddIncident(episode.PhysicalAgentId, sessionId,
                AgentIncidentKind.CompactionContinuationAwaitingCheck, AlertSeverity.Warning,
                "Resumed generation is running. Recovery waits for a useful Check and a whole caller receipt.");
            await _boundary.ReachedAsync("launch-accepted", episode.Id, ct);
            await _db.SaveChangesAsync(ct);
            await PublishAsync(episode.PhysicalAgentId, ct);
        }
    }

    private async Task TryReceiptAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        if (episode.ResumeSessionId is not Guid sessionId || episode.ResumeAcceptedStartedAt is null)
            return;
        var since = episode.StopOutcomeAt ?? episode.DetectedAt;
        var check = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Role == StandingSpecialistSeatPolicy.Role
                && t.Status == AgentTaskStatus.Succeeded
                && t.Result != null
                && t.Result != ""
                && t.CreatedAt >= since)
            .OrderByDescending(t => t.CreatedAt)
            .FirstOrDefaultAsync(ct);
        if (check is null)
            return;

        var marker = DelegationReportFormatter.TaskMarker(check.Id);
        var prompt = await _db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null
                && t.Text.Contains(marker))
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (prompt is null)
            return;
        var ended = await _db.TranscriptEntries.AsNoTracking().AnyAsync(t =>
            t.AgentSessionId == sessionId
            && t.Kind == TranscriptKinds.TurnEnd
            && t.Sequence > prompt.Sequence, ct);
        if (!ended || check.ParentSessionId is not Guid parent)
            return;

        if (await LegacyReceiptAsync(episode, sessionId, parent, ct) is not LegacyReceipt.NotApplicable)
            return;

        var note = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == parent
                && m.SourceTaskId == check.Id
                && m.Status == QueuedMessageStatus.Sent
                && m.Body != ""
                && m.LastDeliveryBaselineSequence != null)
            .OrderByDescending(m => m.Sequence)
            .FirstOrDefaultAsync(ct);
        if (note?.Body is null)
            return;
        var receipt = await _db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == parent
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null
                && t.Text.Contains(note.Body)
                && t.Sequence > note.LastDeliveryBaselineSequence)
            .OrderBy(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (receipt is null)
            return;

        var supervision = await GetOrCreateStateAsync(episode.PhysicalAgentId, ct);
        episode.State = CheckCompactionRecoveryState.Recovered;
        episode.UsefulCheckTaskId = check.Id;
        episode.CallerReceiptNotificationId = note.SourceLandNotificationId;
        episode.ConfirmingPromptSequence = receipt.Sequence;
        episode.Reason = "receipt";
        episode.ConcurrencyToken = Guid.NewGuid();
        supervision.CompactionRestartReceiptEligible = true;
        supervision.ActiveCompactionRecoveryId = null;
        supervision.UpdatedAt = UtcNow();
        AddIncident(episode.PhysicalAgentId, sessionId,
            AgentIncidentKind.CompactionContinuationRecovered, AlertSeverity.Info,
            $"Useful Check {check.Id:D} and caller prompt {receipt.Sequence} closed the episode.");
        await _db.SaveChangesAsync(ct);
        await PublishAsync(episode.PhysicalAgentId, ct);
    }

    private async Task ReleaseOccupantsAsync(CheckCompactionRecovery episode, bool afterStop, CancellationToken ct)
    {
        var now = UtcNow();
        var delivery = TimeSpan.FromMinutes(Math.Max(0, _settings.DeliveryFailTimeoutMinutes));
        var silence = TimeSpan.FromMinutes(Math.Max(10, episode.ConfiguredThresholdMinutes));
        var tasks = await _db.AgentTasks
            .Where(t => t.AgentId == episode.PhysicalAgentId
                && t.AgentSessionId == episode.SessionId
                && t.Role == StandingSpecialistSeatPolicy.Role
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working))
            .ToListAsync(ct);
        foreach (var task in tasks)
        {
            var messages = await _db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == episode.SessionId
                    && m.Body.Contains(task.Id.ToString()))
                .ToListAsync(ct);
            var attempted = messages.Any(m => !StandingQueueSwitchPolicy.NeverAttempted(m) || m.Status == QueuedMessageStatus.Sent);
            var neverAttempted = messages.Count == 0 || messages.All(StandingQueueSwitchPolicy.NeverAttempted);
            var owning = task.Id == episode.OwningCheckTaskId;
            if (!afterStop)
            {
                if (task.Status != AgentTaskStatus.Dispatched || !neverAttempted || attempted)
                    continue;
                if (task.DispatchedAt is not DateTime dispatched || now - dispatched < delivery)
                    continue;
                if (now - episode.DetectedAt < silence)
                    continue;
                await FailOccupantAsync(task, AgentTaskFailureCode.CompactionContinuationStalled,
                    $"Compaction continuation stalled: boundary {episode.BoundaryIdentity}, generation {episode.AcceptedStartedAt:O}, episode {episode.State}.",
                    ct);
            }
            else if (neverAttempted && !attempted)
            {
                await FailOccupantAsync(task, AgentTaskFailureCode.CompactionRecoveryRetiredGeneration,
                    $"Compaction recovery retired this untyped Check generation {episode.AcceptedStartedAt:O}.",
                    ct);
                if (_queue is not null)
                {
                    foreach (var message in messages.Where(m => m.Status == QueuedMessageStatus.Pending))
                        await _queue.CancelPendingIfUntypedAsync(episode.SessionId, message.Id, ct);
                }
            }
            else if (owning && task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
            {
                await FailOccupantAsync(task, AgentTaskFailureCode.CompactionRecoveryRetiredGeneration,
                    "The owning Check turn was interrupted by authorized compaction-continuation recovery. Its delivery rows were left in place.",
                    ct);
            }
        }
    }

    private async Task FailOccupantAsync(
        AgentTask task, AgentTaskFailureCode code, string reason, CancellationToken ct)
    {
        if (task.Status is AgentTaskStatus.Failed or AgentTaskStatus.Canceled or AgentTaskStatus.Succeeded)
            return;
        var now = UtcNow();
        task.Status = AgentTaskStatus.Failed;
        task.FailureReason = reason;
        task.FailureCode = code;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        var failed = new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Failed,
            Detail = reason.Length <= 4000 ? reason : reason[..4000],
            At = now,
        };
        _db.AgentTaskEvents.Add(failed);
        if (task.ReplyTo == AgentTaskReplyTo.Session && task.ParentSessionId is not null)
        {
            var note = DelegationReportFormatter.BuildCompletionNote(
                task, _settings, reason, land: await LandCompletionFacts.LoadAsync(_db, task, ct));
            _db.AgentTaskLandNotifications.Add(new AgentTaskLandNotification
            {
                Id = Guid.NewGuid(),
                TaskId = task.Id,
                SourceEventId = failed.Id,
                Kind = LandNotificationKind.DeliveryFailure,
                ReplyTo = task.ReplyTo,
                ParentSessionId = task.ParentSessionId,
                Body = note.Body,
                ContentDigest = DelegationNoteDigest.Compute(reason),
                CreatedAt = now,
                NextAttemptAt = now,
                State = LandNotificationState.Queued,
            });
        }

        await _db.SaveChangesAsync(ct);
        await _boundary.ReachedAsync("failure-committed", task.Id, ct);
    }

    private async Task CancelRetiredBriefsAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        if (_queue is null)
            return;
        var retired = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentId == episode.PhysicalAgentId
                && t.AgentSessionId == episode.SessionId
                && t.Role == StandingSpecialistSeatPolicy.Role
                && t.Status == AgentTaskStatus.Failed
                && (t.FailureCode == AgentTaskFailureCode.CompactionRecoveryRetiredGeneration
                    || t.FailureCode == AgentTaskFailureCode.CompactionContinuationStalled))
            .Select(t => t.Id)
            .ToListAsync(ct);
        foreach (var taskId in retired)
        {
            var token = taskId.ToString();
            var messages = await _db.SessionQueuedMessages
                .Where(m => m.AgentSessionId == episode.SessionId
                    && m.Status == QueuedMessageStatus.Pending
                    && m.Body.Contains(token))
                .ToListAsync(ct);
            foreach (var message in messages.Where(StandingQueueSwitchPolicy.NeverAttempted))
                await _queue.CancelPendingIfUntypedAsync(episode.SessionId, message.Id, ct);
        }
    }

    private enum LegacyReceipt { NotApplicable, Handled }

    private async Task<LegacyReceipt> LegacyReceiptAsync(
        CheckCompactionRecovery episode, Guid sessionId, Guid parent, CancellationToken ct)
    {
        var related = await _db.LegacyCheckNotePublications.AsNoTracking()
            .Where(p => p.PhysicalAgentId == episode.PhysicalAgentId
                && p.InterpreterSessionId == sessionId)
            .ToListAsync(ct);
        if (related.Count == 0)
            return LegacyReceipt.NotApplicable;

        var publication = related.FirstOrDefault(p =>
            p.State == LegacyCheckNoteState.Produced
            && p.RecoveryId == episode.Id
            && episode.ResumeAcceptedStartedAt is DateTime generation
            && SessionGeneration.Equal(p.InterpreterAcceptedStartedAt, generation)
            && p.InterpretationTaskId is Guid);
        if (publication?.InterpretationTaskId is not Guid runId || publication.Body is not { Length: > 0 } body)
            return LegacyReceipt.Handled;

        var subject = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == publication.CheckedTaskId, ct);
        if (subject?.DispatchedAt is not DateTime dispatched
            || publication.CheckedTaskAttempt != subject.Attempt
            || publication.CheckNumber != subject.CheckCount
            || !SessionGeneration.Equal(publication.CheckedTaskDispatchedAt, SessionGeneration.Normalize(dispatched)))
            return LegacyReceipt.Handled;

        var check = await _db.AgentTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == runId, ct);
        if (check is null
            || check.Status != AgentTaskStatus.Succeeded
            || string.IsNullOrWhiteSpace(check.Result)
            || check.CreatedAt < (episode.StopOutcomeAt ?? episode.DetectedAt))
            return LegacyReceipt.Handled;

        var marker = DelegationReportFormatter.TaskMarker(check.Id);
        var prompt = await _db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null
                && t.Text.Contains(marker))
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (prompt is null)
            return LegacyReceipt.Handled;
        var ended = await _db.TranscriptEntries.AsNoTracking().AnyAsync(t =>
            t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd && t.Sequence > prompt.Sequence, ct);
        if (!ended)
            return LegacyReceipt.Handled;

        var queued = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.SourceLandNotificationId == publication.NotificationId)
            .OrderByDescending(m => m.Sequence)
            .FirstOrDefaultAsync(ct);
        if (queued?.LastDeliveryBaselineSequence is not long floor)
            return LegacyReceipt.Handled;
        var receipt = (await _db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == parent
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Text != null
                && t.Sequence > floor)
            .OrderBy(t => t.Sequence)
            .ToListAsync(ct))
            .FirstOrDefault(t => PromptSubmissionMatch.IsCompleteIn(body, t.Text!));
        if (receipt is null)
            return LegacyReceipt.Handled;

        var notification = await _db.AgentTaskLandNotifications.FirstOrDefaultAsync(n => n.Id == publication.NotificationId, ct);
        if (notification is not null && notification.State != LandNotificationState.Confirmed)
        {
            notification.State = LandNotificationState.Confirmed;
            notification.ConfirmedAt = UtcNow();
            notification.ConfirmingPromptSequence = receipt.Sequence;
        }

        var supervision = await GetOrCreateStateAsync(episode.PhysicalAgentId, ct);
        episode.State = CheckCompactionRecoveryState.Recovered;
        episode.UsefulCheckTaskId = check.Id;
        episode.CallerReceiptNotificationId = publication.NotificationId;
        episode.ConfirmingPromptSequence = receipt.Sequence;
        episode.Reason = "legacy-receipt";
        episode.ConcurrencyToken = Guid.NewGuid();
        supervision.CompactionRestartReceiptEligible = true;
        supervision.ActiveCompactionRecoveryId = null;
        supervision.UpdatedAt = UtcNow();
        AddIncident(episode.PhysicalAgentId, sessionId,
            AgentIncidentKind.CompactionContinuationRecovered, AlertSeverity.Info,
            $"Legacy Check publication {publication.Id:D} and caller prompt {receipt.Sequence} closed the episode.");
        await _db.SaveChangesAsync(ct);
        await PublishAsync(episode.PhysicalAgentId, ct);
        return LegacyReceipt.Handled;
    }

    private async Task<bool> SupersededByOperatorAsync(CheckCompactionRecovery episode, CancellationToken ct)
    {
        var supervision = await _db.AgentSupervisionStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentId == episode.PhysicalAgentId, ct);
        if (supervision?.Suspended == true
            && episode.State is CheckCompactionRecoveryState.Confirmed or CheckCompactionRecoveryState.StopRequested)
        {
            await SupersedeAsync(episode, "suspended", ct);
            return true;
        }

        var session = await _db.AgentSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == episode.SessionId, ct);
        if (session?.TerminationSource == SessionTerminationSource.OperatorRequest
            && episode.State is not CheckCompactionRecoveryState.Recovered)
        {
            await SupersedeAsync(episode, "operator-stop", ct);
            return true;
        }

        return false;
    }

    private async Task SupersedeAsync(CheckCompactionRecovery episode, string reason, CancellationToken ct)
    {
        var supervision = await GetOrCreateStateAsync(episode.PhysicalAgentId, ct);
        episode.State = CheckCompactionRecoveryState.SupersededByOperator;
        episode.Reason = reason;
        episode.ConcurrencyToken = Guid.NewGuid();
        if (supervision.ActiveCompactionRecoveryId == episode.Id)
            supervision.ActiveCompactionRecoveryId = null;
        supervision.UpdatedAt = UtcNow();
        AddIncident(episode.PhysicalAgentId, episode.SessionId,
            AgentIncidentKind.CompactionContinuationAborted, AlertSeverity.Warning,
            $"Operator superseded compaction recovery: {reason}.");
        await _db.SaveChangesAsync(ct);
        await PublishAsync(episode.PhysicalAgentId, ct);
    }

    private async Task TransitionAsync(
        CheckCompactionRecovery episode, CheckCompactionRecoveryState state, string reason, CancellationToken ct)
    {
        episode.State = state;
        episode.Reason = reason.Length <= 200 ? reason : reason[..200];
        episode.ConcurrencyToken = Guid.NewGuid();
        var kind = state is CheckCompactionRecoveryState.AbortedProgress
            ? AgentIncidentKind.CompactionContinuationAborted
            : AgentIncidentKind.CompactionContinuationNeedsDecision;
        var severity = state is CheckCompactionRecoveryState.AbortedProgress
            ? AlertSeverity.Warning
            : AlertSeverity.Error;
        AddIncident(episode.PhysicalAgentId, episode.SessionId, kind, severity,
            $"Compaction recovery {state}: {episode.Reason}.");
        if (state is CheckCompactionRecoveryState.AbortedProgress or CheckCompactionRecoveryState.Superseded)
        {
            var supervision = await GetOrCreateStateAsync(episode.PhysicalAgentId, ct);
            if (supervision.ActiveCompactionRecoveryId == episode.Id)
                supervision.ActiveCompactionRecoveryId = null;
            supervision.UpdatedAt = UtcNow();
        }

        await _db.SaveChangesAsync(ct);
        await PublishAsync(episode.PhysicalAgentId, ct);
    }

    private async Task<CompactionScopeSnapshot?> BuildScopeAsync(Guid seatId, CancellationToken ct)
    {
        var agent = await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == seatId, ct);
        if (agent is null || !Guid.TryParse(agent.PersistentSessionId, out var sessionId))
            return null;
        var session = await _db.AgentSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null)
            return null;
        var ownerId = agent.StandingSpecialistOwnerId ?? agent.Id;
        var owner = ownerId == agent.Id
            ? agent
            : await _db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.Id == ownerId, ct);
        var supervision = await _db.AgentSupervisionStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.AgentId == seatId, ct);
        var ownerSupervision = ownerId == seatId
            ? supervision
            : await _db.AgentSupervisionStates.AsNoTracking().FirstOrDefaultAsync(s => s.AgentId == ownerId, ct);
        var routing = await _db.StandingSpecialistRoutings.AsNoTracking()
            .FirstOrDefaultAsync(r => r.AgentId == ownerId, ct);
        var open = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentId == seatId
                && (t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working
                    || t.Status == AgentTaskStatus.Blocked
                    || t.Status == AgentTaskStatus.Queued))
            .Select(t => new { t.Role, t.CardId })
            .ToListAsync(ct);
        var pending = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId && m.Status == QueuedMessageStatus.Pending)
            .Select(m => new { m.Origin, m.DeliveryAttempts, m.LastDeliveryStartedAt, m.LastDeliveryBaselineSequence, m.DeliveryVerdict, m.SentAt })
            .ToListAsync(ct);
        DateTime? runnerGeneration = null;
        var runnerRunning = false;
        if (_runner is not null)
        {
            try
            {
                var live = await _runner.GetAsync(sessionId, ct);
                runnerGeneration = live.AcceptedStartedAt;
                runnerRunning = string.Equals(live.Status, "Running", StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                runnerGeneration = null;
            }
        }

        var slugOnly = !StandingSpecialistSeatPolicy.IsCheck(agent)
            && StandingSpecialistSeatPolicy.IsCheck(agent, _settings);
        var correlated = await _db.AgentTasks.AsNoTracking().AnyAsync(t =>
            t.AgentSessionId == sessionId && t.Role == StandingSpecialistSeatPolicy.Role, ct);

        // D-2 holds. Each is read from the state its own subsystem already writes, because a
        // clause that is never populated is not a guard at all: it reads `false` forever and the
        // one code path allowed to kill a live Working session loses that veto silently.
        var now = UtcNow();
        var alias = (ModelAlias.Normalize(agent.Kind, agent.ModelId)
            ?? ModelLevelAliases.For(agent.Kind, agent.ModelLevel))
            .Trim().ToLowerInvariant();
        // Read-only twin of ModelAvailability.FindActiveAsync's live predicate (a legacy
        // AutoDetected row with no DisabledUntil counts as held: for a kill guard, conservative
        // is the safe direction). Discovery must not write, so the lazy normalization is skipped.
        var modelHold = await _db.ModelAvailabilityHolds.AsNoTracking().AnyAsync(h =>
            h.Kind == agent.Kind
            && h.ClearedAt == null
            && (h.ModelAlias == alias || h.ModelAlias == ModelAlias.KindWide)
            && (h.DisabledUntil == null || h.DisabledUntil > now), ct);
        // CARD-0412 admission clock for this execution kind: while the provider is not admitting,
        // a restart is exactly the launch it is holding back.
        var providerHold = await _db.CapacityRecoveryProviderStates.AsNoTracking().AnyAsync(s =>
            s.Kind == agent.Kind && s.NextAdmissionAt != null && s.NextAdmissionAt > now, ct);
        // CARD-0136 launch gate. Same reading a human Start would be refused on.
        var quotaHold = _quota is not null
            && await _quota.EvaluateAsync(agent.Kind, SubscriptionUsageKey.For(owner, agent.Kind), ct) is not null;
        var capacityHold = await SessionMessageQueueService.HasTerminalCapacityHoldAsync(_db, sessionId, ct);
        // CARD-0072: NeedsHuman is authentication_failed / model_not_found - the class whose whole
        // point is that nothing automatic fixes it. Unresolved means nobody has continued past it.
        // The CARD-0324 provider sign-in probe is Grok-only and cannot reach a seat that already
        // has to be effective ClaudeCode, so this is the ClaudeCode half of the same refusal.
        var authenticationRefusal = await _db.ApiErrorRecoveries.AsNoTracking().AnyAsync(r =>
            r.AgentSessionId == sessionId
            && r.ResolvedAt == null
            && r.Classification == ApiErrorClassification.NeedsHuman, ct);
        // The standing-ownership pointer, not the seat's own belief about itself: a stamped
        // StandingAgentId that names somebody else, or ambiguous legacy ownership, means this
        // physical seat no longer owns the conversation it is about to stop.
        var ownership = await new StandingSessionOwnership(_db).ResolveAsync(session, ct);

        return new CompactionScopeSnapshot
        {
            SessionId = sessionId,
            AcceptedStartedAt = SessionGeneration.Normalize(session.StartedAt),
            PhysicalAlwaysOn = agent.AlwaysOn,
            LogicalOwnerAlwaysOn = owner?.AlwaysOn == true,
            PoolOwned = agent.IsPoolDelegate,
            Suspended = supervision?.Suspended == true || ownerSupervision?.Suspended == true,
            EffectiveClaudeCode = agent.Kind == AgentKind.ClaudeCode && session.AgentKind == AgentKind.ClaudeCode,
            CurrentStandingOwnership = ownership.Owner == seatId,
            CheckSeat = StandingSpecialistSeatPolicy.IsCheck(agent),
            LegacySlugOnly = slugOnly,
            PositivelyCorrelatedOwningCheck = correlated,
            OpenNonCheckAssignment = open.Any(t => t.Role != StandingSpecialistSeatPolicy.Role),
            PendingCardAssignment = agent.CurrentCardId is not null || open.Any(t => t.CardId is not null && t.Role != StandingSpecialistSeatPolicy.Role),
            InteractiveHumanTurn = pending.Any(m => m.Origin is QueuedMessageOrigin.Ui or QueuedMessageOrigin.Channel or QueuedMessageOrigin.Scheduled),
            UnresolvedAttemptedInput = pending.Any(m =>
                m.DeliveryAttempts != 0 || m.LastDeliveryStartedAt != null || m.LastDeliveryBaselineSequence != null
                || m.DeliveryVerdict != null || m.SentAt != null),
            PhysicalEnabled = agent.Status == AgentStatus.Running,
            OwnerEnabled = owner is not null && routing?.Enabled != false,
            CheckInterpretationEnabled = _settings.CheckEnabled && _settings.CheckInterpreterEnabled,
            LivenessHold = supervision?.LivenessLatchedAt != null || ownerSupervision?.LivenessLatchedAt != null,
            ContinuityHold = supervision?.ContinuityHeldAt != null || ownerSupervision?.ContinuityHeldAt != null,
            HerdrHold = supervision?.HerdrFailureHeldAt != null || ownerSupervision?.HerdrFailureHeldAt != null,
            ProviderHold = providerHold,
            QuotaHold = quotaHold,
            CapacityHold = capacityHold,
            ModelHold = modelHold,
            AuthenticationRefusal = authenticationRefusal,
            Running = session.Status == SessionStatus.Running && (_runner is null || runnerRunning),
            Working = await SessionMessageQueueService.IsWorkingAsync(_db, sessionId, ct),
            GenerationTokensEqual = runnerGeneration is not null
                && SessionGeneration.Equal(runnerGeneration, session.StartedAt),
            DelegationEnabled = _settings.Enabled,
        };
    }

    private async Task<IReadOnlyList<CompactionTranscriptFact>> LoadFactsAsync(Guid sessionId, CancellationToken ct)
    {
        var rows = await _db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId)
            .OrderBy(t => t.Sequence)
            .Select(t => new { t.Sequence, t.Kind, t.Text, t.Timestamp, t.CreatedAt, t.Uuid, t.ToolUseId, t.StopReason })
            .ToListAsync(ct);
        var checkIds = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Role == StandingSpecialistSeatPolicy.Role)
            .Select(t => t.Id)
            .ToListAsync(ct);
        var tokens = checkIds.SelectMany(id => new[] { id.ToString("D"), id.ToString("N"), DelegationReportFormatter.TaskMarker(id) }).ToArray();
        // A prompt a PERSON put in this session. The owning-prompt predicate needs it: a human who
        // re-types or pastes a Check brief owns that turn, and its silence is a human's to break,
        // not this mechanism's. The evidence is the delivered queue row behind the prompt - the
        // same body match delivery itself confirms on - never the text alone.
        var humanBodies = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.AgentSessionId == sessionId
                && m.SentAt != null
                && (m.Origin == QueuedMessageOrigin.Ui
                    || m.Origin == QueuedMessageOrigin.Channel
                    || m.Origin == QueuedMessageOrigin.Scheduled))
            .Select(m => m.Body)
            .ToListAsync(ct);
        // A body too short to identify makes IsCompleteIn vacuously true, which would stamp every
        // prompt in the session as human. Only bodies that can actually be matched count.
        var humanMatchable = humanBodies.Where(PromptSubmissionMatch.RequiresTextMatch).ToArray();
        return rows.Select(row => new CompactionTranscriptFact(
            row.Sequence,
            row.Kind,
            row.Text,
            row.Timestamp,
            row.CreatedAt,
            row.Uuid,
            row.ToolUseId,
            IsCorrelatedCheck: row.Kind == TranscriptKinds.UserPrompt
                && row.Text is not null
                && tokens.Any(token => row.Text.Contains(token, StringComparison.Ordinal)),
            IsHumanOrigin: row.Kind == TranscriptKinds.UserPrompt
                && row.Text is not null
                && humanMatchable.Any(body => PromptSubmissionMatch.IsCompleteIn(body, row.Text)),
            StopReason: row.StopReason)).ToArray();
    }

    private async Task<CompactionTailObservation?> ObserveAsync(Guid sessionId, CancellationToken ct)
    {
        if (_runner is null)
            return null;
        try
        {
            return await _runner.ObserveCompactionAsync(sessionId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Compaction observation failed for session {SessionId}", sessionId);
            return CompactionTailObservation.Unavailable();
        }
    }

    private async Task<Guid?> FindOwningCheckAsync(
        Guid sessionId, IReadOnlyList<CompactionTranscriptFact> facts, long? sequence, CancellationToken ct)
    {
        if (sequence is null)
            return null;
        var prompt = facts.FirstOrDefault(f => f.Sequence == sequence.Value);
        if (string.IsNullOrEmpty(prompt.Text))
            return null;
        var ids = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Role == StandingSpecialistSeatPolicy.Role)
            .Select(t => t.Id)
            .ToListAsync(ct);
        foreach (var id in ids)
        {
            if (prompt.Text.Contains(id.ToString("D"), StringComparison.Ordinal)
                || prompt.Text.Contains(id.ToString("N"), StringComparison.Ordinal)
                || prompt.Text.Contains(DelegationReportFormatter.TaskMarker(id), StringComparison.Ordinal))
                return id;
        }

        return null;
    }

    private static string Evidence(CompactionContinuationVerdict verdict, CompactionTailObservation observation)
    {
        var json = JsonSerializer.Serialize(new
        {
            verdict.BoundarySequence,
            verdict.ContinuationSequence,
            observation.BindingIdentity,
            observation.TranscriptRevision,
            observation.OutputRevision,
        });
        return json.Length <= CheckCompactionRecovery.EvidenceMaxLength
            ? json
            : json[..CheckCompactionRecovery.EvidenceMaxLength];
    }

    private async Task<AgentSupervisionState> GetOrCreateStateAsync(Guid agentId, CancellationToken ct)
    {
        var state = await _db.AgentSupervisionStates.FirstOrDefaultAsync(s => s.AgentId == agentId, ct);
        if (state is not null)
            return state;
        state = new AgentSupervisionState { AgentId = agentId, UpdatedAt = UtcNow() };
        _db.AgentSupervisionStates.Add(state);
        return state;
    }

    private void AddIncident(
        Guid agentId, Guid? sessionId, AgentIncidentKind kind, AlertSeverity severity, string message)
    {
        _db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = agentId,
            SessionId = sessionId,
            Kind = kind,
            Severity = severity,
            Message = ColumnText.Clip(
                $"{message} Actor {CheckCompactionRecovery.Actor}; authorization {CheckCompactionRecovery.Authorization}.",
                AgentIncident.MessageMaxLength),
            CreatedAt = UtcNow(),
        });
    }

    private async Task PublishAsync(Guid agentId, CancellationToken ct)
    {
        await _boundary.ReachedAsync("audit-committed", agentId, ct);
        await _boundary.ReachedAsync("before-audit-publish", agentId, ct);
        if (_events is null)
            return;
        await _events.PublishToAllAsync("AgentChanged", new AgentChangedEventDto(agentId), ct);
    }

    private DateTime UtcNow()
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return now.Kind == DateTimeKind.Utc ? now : DateTime.SpecifyKind(now, DateTimeKind.Utc);
    }
}
