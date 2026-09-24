using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Read-only board snapshot. Does not tick dispatch, run git, or touch a repository journal.
/// </summary>
public sealed class ExpectationSnapshotReader
{
    private static readonly AgentTaskStatus[] OpenStatuses =
    [
        AgentTaskStatus.Queued,
        AgentTaskStatus.Dispatched,
        AgentTaskStatus.Working,
        AgentTaskStatus.Blocked,
    ];

    private static readonly SessionStatus[] LiveSessions =
    [
        SessionStatus.Starting,
        SessionStatus.Running,
        SessionStatus.Stopping,
    ];

    private readonly AppDbContext _db;
    private readonly DelegationSettings _delegation;
    private readonly SupervisionSettings _supervision;

    public ExpectationSnapshotReader(
        AppDbContext db, DelegationSettings? delegation = null, SupervisionSettings? supervision = null)
    {
        _db = db;
        _delegation = delegation ?? new DelegationSettings();
        _supervision = supervision ?? new SupervisionSettings();
    }

    public async Task<ExpectationSnapshot> ReadAsync(
        ExpectationDirectiveSettings directive,
        string configDigest,
        DateTime asOf,
        ExpectationProbeInput probes,
        CancellationToken ct)
    {
        asOf = SpecifyUtc(asOf);
        var active = ExpectationDirectiveActivity.HasEffects(
            new ExpectationWatchdogSettings { Enabled = true, Directives = [directive] },
            directive,
            new DateTimeOffset(asOf, TimeSpan.Zero));
        if (probes.Unavailable)
        {
            return new ExpectationSnapshot
            {
                AsOf = asOf,
                DirectiveActive = active,
                ProbeUnknown = true,
                ProbeError = probes.Error ?? "probe unavailable",
            };
        }

        var board = await _db.Boards.AsNoTracking()
            .Where(row => row.Id == directive.BoardId)
            .Select(row => new { row.Id, row.ProjectId, row.ArchivedAt })
            .SingleOrDefaultAsync(ct);
        if (board is null)
        {
            return new ExpectationSnapshot
            {
                AsOf = asOf,
                DirectiveActive = false,
                ProbeUnknown = true,
                ProbeError = "board not found",
            };
        }

        if (board.ArchivedAt is not null)
        {
            return new ExpectationSnapshot
            {
                AsOf = asOf,
                DirectiveActive = false,
            };
        }

        var scope = new ExpectationScopeContext(board.Id, board.ProjectId, directive.AgentId);
        var windowStart = asOf - ExpectationWindows.Queue;
        var cardBindings = await _db.Cards.AsNoTracking()
            .Where(card => card.BoardId == board.Id || card.Board.ProjectId == board.ProjectId)
            .Select(card => new { card.Id, card.BoardId, ProjectId = card.Board.ProjectId })
            .ToListAsync(ct);
        var cardById = cardBindings.ToDictionary(card => card.Id);
        var cardIds = cardById.Keys.ToList();

        var taskQuery = _db.AgentTasks.AsNoTracking().Where(task =>
            OpenStatuses.Contains(task.Status)
            || (task.DispatchedAt != null && task.DispatchedAt > windowStart && task.DispatchedAt <= asOf));
        taskQuery = cardIds.Count == 0
            ? taskQuery.Where(task => task.CardId == null && task.ProjectId == board.ProjectId)
            : taskQuery.Where(task =>
                (task.CardId != null && cardIds.Contains(task.CardId.Value))
                || (task.CardId == null && task.ProjectId == board.ProjectId));

        var loaded = await taskQuery
            .Select(task => new TaskRow(
                task.Id,
                task.CardId,
                task.ProjectId,
                task.ParentSessionId,
                task.Status,
                task.Role,
                task.RunnerId,
                task.RepoPath,
                task.CreatedAt,
                task.AgentSessionId,
                task.DispatchedAt,
                task.Result != null || task.ResultFilePath != null))
            .ToListAsync(ct);

        var parentIds = loaded
            .Where(task => task.CardId is null && task.ParentSessionId is not null)
            .Select(task => task.ParentSessionId!.Value)
            .Distinct()
            .ToList();
        var parents = parentIds.Count == 0
            ? new Dictionary<Guid, Guid?>()
            : await _db.AgentSessions.AsNoTracking()
                .Where(session => parentIds.Contains(session.Id))
                .Select(session => new { session.Id, session.StandingAgentId })
                .ToDictionaryAsync(session => session.Id, session => session.StandingAgentId, ct);

        var included = new List<ScopedTask>();
        var ambiguous = 0;
        foreach (var task in loaded)
        {
            Guid? cardBoard = null;
            Guid? cardProject = null;
            if (task.CardId is Guid cardId && cardById.TryGetValue(cardId, out var binding))
            {
                cardBoard = binding.BoardId;
                cardProject = binding.ProjectId;
            }

            Guid? standing = null;
            var sessionKnown = false;
            if (task.ParentSessionId is Guid parentId && parents.TryGetValue(parentId, out var owner))
            {
                sessionKnown = true;
                standing = owner;
            }

            var decision = ExpectationScope.Decide(
                new ExpectationTaskScopeFacts(cardBoard, task.ProjectId, cardProject, standing, sessionKnown),
                scope);
            if (decision == ExpectationScopeDecision.AmbiguousUnbound)
                ambiguous++;
            if (decision != ExpectationScopeDecision.Included)
                continue;
            included.Add(new ScopedTask(task, ExpectationRepository.For(task.RepoPath, board.Id)));
        }

        var throughput = included.Where(task => !AgentTaskRoles.IsSpecialist(task.Row.Role)).ToList();
        var watchIds = throughput.Select(task => task.Row.Id).ToList();
        var events = watchIds.Count == 0
            ? []
            : await _db.AgentTaskEvents.AsNoTracking()
                .Where(row => watchIds.Contains(row.AgentTaskId)
                    && row.At <= asOf
                    && (row.Type == AgentTaskEventType.Dispatched
                        || row.Type == AgentTaskEventType.Created
                        || row.Type == AgentTaskEventType.Retried
                        || row.Type == AgentTaskEventType.Escalated
                        || row.Type == AgentTaskEventType.Rerouted
                        || row.Type == AgentTaskEventType.Held
                        || row.Type == AgentTaskEventType.HeldAged))
                .Select(row => new EventRow(row.Id, row.AgentTaskId, row.Type, row.At, row.Detail))
                .ToListAsync(ct);

        DateTime? lastDispatch = null;
        foreach (var row in events)
        {
            if (row.Type != AgentTaskEventType.Dispatched)
                continue;
            if (lastDispatch is null || row.At > lastDispatch)
                lastDispatch = row.At;
        }

        var queued = new List<ExpectationQueuedTask>();
        foreach (var task in throughput.Where(task => task.Row.Status == AgentTaskStatus.Queued))
        {
            var taskEvents = events.Where(row => row.TaskId == task.Row.Id).ToList();
            var stint = ExpectationQueueStint.Start(
                SpecifyUtc(task.Row.CreatedAt),
                taskEvents.Select(row => new ExpectationStintEvent(row.Type, SpecifyUtc(row.At))));
            var hold = taskEvents
                .Where(row =>
                    (row.Type is AgentTaskEventType.Held or AgentTaskEventType.HeldAged)
                    && SpecifyUtc(row.At) >= stint)
                .OrderByDescending(row => row.At)
                .ThenByDescending(row => row.Id)
                .FirstOrDefault();
            ExpectationHoldClass? holdClass = hold is null
                ? null
                : DispatchHoldDetails.Classify(hold.Detail).Class;
            if (holdClass is null
                && probes.Runners.TryGetValue(ExpectationSubjects.RunnerKey(task.Row.RunnerId), out var runnerProbe))
            {
                holdClass = runnerProbe.Available switch
                {
                    null => ExpectationHoldClass.Unknown,
                    false => ExpectationHoldClass.RunnerUnavailable,
                    _ => null,
                };
            }

            queued.Add(new ExpectationQueuedTask
            {
                TaskId = task.Row.Id,
                RunnerId = task.Row.RunnerId,
                RepositoryScope = task.RepositoryScope,
                StintStartedAt = stint,
                HoldClass = holdClass,
                HoldDetail = hold?.Detail,
                HoldObservedAt = hold is null ? null : SpecifyUtc(hold.At),
            });
        }

        var state = await _db.ExpectationWatchStates.AsNoTracking()
            .SingleOrDefaultAsync(row => row.DirectiveId == directive.Id, ct);
        var configChanged = state is not null && !string.Equals(state.ConfigDigest, configDigest, StringComparison.Ordinal);
        var episodes = await _db.ExpectationEpisodes.AsNoTracking()
            .Where(row => row.DirectiveId == directive.Id && row.ResolvedAt == null && row.ConfigDigest == configDigest)
            .Select(row => new ExpectationOpenEpisode
            {
                Kind = row.Kind,
                SubjectKey = row.SubjectKey,
                FirstObservedAt = row.FirstObservedAt,
                Evidence = row.Evidence,
            })
            .ToListAsync(ct);

        var lanes = new List<ExpectationLaneSnapshot>();
        foreach (var target in directive.Targets ?? [])
        {
            var key = ExpectationSubjects.RunnerKey(target.RunnerId);
            var matching = throughput.Where(task =>
                ExpectationSubjects.SameRunner(task.Row.RunnerId, target.RunnerId)).ToList();
            var subject = ExpectationSubjects.Capacity(directive.Id, target.RunnerId);
            var deficit = episodes.FirstOrDefault(row =>
                row.Kind == ExpectationEpisodeKind.CapacityDeficit && row.SubjectKey == subject);
            lanes.Add(new ExpectationLaneSnapshot
            {
                RunnerId = string.IsNullOrWhiteSpace(target.RunnerId) ? null : target.RunnerId.Trim(),
                Target = target.InFlightTarget,
                Running = matching.Count(task =>
                    task.Row.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working),
                Queued = matching.Count(task => task.Row.Status == AgentTaskStatus.Queued),
                Blocked = matching.Count(task => task.Row.Status == AgentTaskStatus.Blocked),
                DeficitSince = configChanged ? null : deficit?.FirstObservedAt,
                RunningTaskIds = matching
                    .Where(task => task.Row.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
                    .Select(task => task.Row.Id)
                    .ToList(),
                QueuedTaskIds = matching
                    .Where(task => task.Row.Status == AgentTaskStatus.Queued)
                    .Select(task => task.Row.Id)
                    .ToList(),
            });
        }

        var backlog = await ReadBacklogAsync(board.Id, ct);
        var inFlight = await ReadInFlightAsync(throughput.Select(task => task.Row).ToList(), asOf, probes, ct);
        var notes = await ReadNotesAsync(directive.AgentId, ct);
        return new ExpectationSnapshot
        {
            AsOf = asOf,
            DirectiveActive = active,
            ConfigChanged = configChanged,
            RepositoryScope = ExpectationRepository.For(null, board.Id),
            LastScopedDispatchAt = lastDispatch is null ? null : SpecifyUtc(lastDispatch.Value),
            EligibleBacklog = backlog.Count,
            BacklogCandidateIds = backlog,
            AmbiguousUnboundExcluded = ambiguous,
            IncludedTaskIds = throughput.Select(task => task.Row.Id).ToList(),
            Queued = queued,
            Lanes = lanes,
            Admission = Admission(directive, probes, asOf),
            OpenEpisodes = episodes,
            InFlight = inFlight,
            Notes = notes,
        };
    }

    private async Task<IReadOnlyList<Guid>> ReadBacklogAsync(Guid boardId, CancellationToken ct)
    {
        var cards = await _db.Cards.AsNoTracking()
            .Include(card => card.ExternalIssueRef)
            .Where(card => card.BoardId == boardId
                && card.Status == CardStatus.Backlog
                && card.ArchivedAt == null)
            .ToListAsync(ct);
        if (cards.Count == 0)
            return [];

        var cardIds = cards.Select(card => card.Id).ToList();
        var liveCardIds = await _db.AgentSessions.AsNoTracking()
            .Where(session => session.CardId != null
                && cardIds.Contains(session.CardId.Value)
                && LiveSessions.Contains(session.Status))
            .Select(session => session.CardId!.Value)
            .ToListAsync(ct);
        var openCardIds = await _db.AgentTasks.AsNoTracking()
            .Where(task => task.CardId != null
                && cardIds.Contains(task.CardId.Value)
                && OpenStatuses.Contains(task.Status))
            .Select(task => task.CardId!.Value)
            .ToListAsync(ct);
        var live = liveCardIds.ToHashSet();
        var open = openCardIds.ToHashSet();
        return cards
            .Where(card => card.AssignedAgentId is null
                && card.OwnerSessionId is null
                && card.AutoDispatchHeldAt is null
                && !BoardService.NeedsHumanReview(card)
                && !live.Contains(card.Id)
                && !open.Contains(card.Id))
            .Select(card => card.Id)
            .ToList();
    }

    /// <summary>
    /// Dispatched/Working scoped tasks with no report. A null binding, a missing row, or a runner
    /// answer that the session is gone is Missing; an unreachable runner is Unknown. Live sessions
    /// get the existing progress-stall verdict, with its thresholds, workspace arm and working rule.
    /// Watchdog Check events never count as activity.
    /// </summary>
    private async Task<IReadOnlyList<ExpectationInFlightTask>> ReadInFlightAsync(
        IReadOnlyList<TaskRow> scoped,
        DateTime asOf,
        ExpectationProbeInput probes,
        CancellationToken ct)
    {
        var rows = scoped
            .Where(task => task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working
                && task.DispatchedAt is not null
                && !task.HasReport)
            .ToList();
        if (rows.Count == 0)
            return [];

        var taskIds = rows.Select(task => task.Id).ToList();
        var sessionIds = rows.Where(task => task.AgentSessionId is not null)
            .Select(task => task.AgentSessionId!.Value)
            .Distinct()
            .ToList();
        var sessions = sessionIds.Count == 0
            ? new Dictionary<Guid, SessionStatus>()
            : await _db.AgentSessions.AsNoTracking()
                .Where(session => sessionIds.Contains(session.Id))
                .ToDictionaryAsync(session => session.Id, session => session.Status, ct);
        var taskActivity = await _db.AgentTaskEvents.AsNoTracking()
            .Where(row => taskIds.Contains(row.AgentTaskId)
                && row.Type != AgentTaskEventType.Check
                && row.At <= asOf)
            .GroupBy(row => row.AgentTaskId)
            .Select(group => new { TaskId = group.Key, At = group.Max(row => row.At) })
            .ToDictionaryAsync(row => row.TaskId, row => row.At, ct);
        var transcriptActivity = sessionIds.Count == 0
            ? new Dictionary<Guid, DateTime>()
            : await _db.TranscriptEntries.AsNoTracking()
                .Where(row => sessionIds.Contains(row.AgentSessionId))
                .GroupBy(row => row.AgentSessionId)
                .Select(group => new
                {
                    SessionId = group.Key,
                    At = group.Max(row => row.Timestamp ?? row.CreatedAt),
                })
                .ToDictionaryAsync(row => row.SessionId, row => row.At, ct);

        var results = new List<ExpectationInFlightTask>(rows.Count);
        foreach (var task in rows)
        {
            var dispatched = SpecifyUtc(task.DispatchedAt!.Value);
            var last = dispatched;
            if (taskActivity.TryGetValue(task.Id, out var eventAt) && SpecifyUtc(eventAt) > last)
                last = SpecifyUtc(eventAt);
            if (task.AgentSessionId is Guid bound
                && transcriptActivity.TryGetValue(bound, out var transcriptAt)
                && SpecifyUtc(transcriptAt) > last)
            {
                last = SpecifyUtc(transcriptAt);
            }

            var state = SessionState(task, sessions, probes);
            string? stall = null;
            if (state == ExpectationSessionState.Live)
            {
                WorkspaceProgressArmOrNull(probes, task.Id, out var arm);
                var verdict = await TaskProgressPolicy.EvaluateAsync(
                    _db,
                    new AgentTask { Id = task.Id, AgentSessionId = task.AgentSessionId, DispatchedAt = dispatched },
                    asOf,
                    _delegation,
                    ct,
                    arm);
                stall = verdict?.Summary;
            }

            results.Add(new ExpectationInFlightTask
            {
                TaskId = task.Id,
                AgentSessionId = task.AgentSessionId,
                RunnerId = task.RunnerId,
                DispatchedAt = dispatched,
                LastActivityAt = last,
                SessionState = state,
                ProgressStall = stall,
            });
        }

        return results;
    }

    private static void WorkspaceProgressArmOrNull(
        ExpectationProbeInput probes, Guid taskId, out Dtos.WorkspaceProgressArm? arm)
    {
        arm = null;
        if (probes.Workspace is { } workspace && workspace.TryGetValue(taskId, out var found))
            arm = found;
    }

    private static ExpectationSessionState SessionState(
        TaskRow task, IReadOnlyDictionary<Guid, SessionStatus> sessions, ExpectationProbeInput probes)
    {
        if (task.AgentSessionId is not Guid sessionId || !sessions.TryGetValue(sessionId, out var status))
            return ExpectationSessionState.Missing;
        if (status is SessionStatus.Stopped or SessionStatus.Failed)
            return ExpectationSessionState.Terminal;
        if (probes.Sessions is { } answers && answers.TryGetValue(sessionId, out var answer))
        {
            return answer.Live switch
            {
                null => ExpectationSessionState.Unknown,
                false => ExpectationSessionState.Missing,
                true => ExpectationSessionState.Live,
            };
        }

        if (probes.Runners.TryGetValue(ExpectationSubjects.RunnerKey(task.RunnerId), out var runner)
            && runner.Available is null)
        {
            return ExpectationSessionState.Unknown;
        }

        return ExpectationSessionState.Live;
    }

    /// <summary>
    /// Non-legacy caller notes to sessions the standing agent owns, not yet Confirmed or
    /// NotRequired. A note is excluded only by CARD-0641's own receipt (<see cref="LandNoteReceipt"/>):
    /// its keyed queue row was typed into the same destination, and a prompt above that row's
    /// delivery floor carries the complete expected text. A prompt that merely quotes the body,
    /// before or without that delivery, leaves the debt open. This read never settles or resends
    /// the note (CARD-0641 owns that).
    /// </summary>
    private async Task<IReadOnlyList<ExpectationNoteDebt>> ReadNotesAsync(Guid agentId, CancellationToken ct)
    {
        var rows = await _db.AgentTaskLandNotifications.AsNoTracking()
            .Where(note => !note.IsLegacy
                && note.ConfirmedAt == null
                && note.State != LandNotificationState.Confirmed
                && note.State != LandNotificationState.NotRequired
                && note.State != LandNotificationState.LegacyUnverified
                && note.ParentSessionId != null
                && _db.AgentSessions.Any(session =>
                    session.Id == note.ParentSessionId && session.StandingAgentId == agentId))
            .OrderBy(note => note.CreatedAt)
            .ThenBy(note => note.Id)
            .Take(100)
            .Select(note => new NoteRow(
                note.Id,
                note.TaskId,
                note.Kind,
                note.State,
                note.CreatedAt,
                note.ParentSessionId,
                note.LastErrorCode,
                note.QueueMessageId,
                note.Body,
                note.IsLegacy,
                note.CompletionSnapshotJson != null,
                note.CompletionDeliveryJson))
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var queueIds = rows.Where(row => row.QueueMessageId is not null)
            .Select(row => row.QueueMessageId!.Value)
            .ToList();
        var queue = queueIds.Count == 0
            ? new Dictionary<Guid, QueueRow>()
            : await _db.SessionQueuedMessages.AsNoTracking()
                .Where(message => queueIds.Contains(message.Id))
                .Select(message => new QueueRow(
                    message.Id,
                    message.AgentSessionId,
                    message.Status,
                    message.DeliveryAttempts,
                    message.LastDeliveryBaselineSequence,
                    message.LastDeliveryStartedAt))
                .ToDictionaryAsync(message => message.Id, ct);

        var results = new List<ExpectationNoteDebt>(rows.Count);
        foreach (var row in rows)
        {
            QueueRow? keyed = row.QueueMessageId is Guid queueId && queue.TryGetValue(queueId, out var found)
                ? found
                : null;
            if (await IsReceivedAsync(row, keyed, ct))
                continue;
            results.Add(new ExpectationNoteDebt
            {
                NotificationId = row.Id,
                TaskId = row.TaskId,
                Kind = row.Kind,
                State = row.State,
                CreatedAt = SpecifyUtc(row.CreatedAt),
                ParentSessionId = row.ParentSessionId,
                LastErrorCode = row.LastErrorCode,
                QueueStatus = keyed?.Status,
            });
        }

        return results;
    }

    /// <summary>
    /// CARD-0641's receipt, read-only: the reconciler's expected text (the frozen wire rendering
    /// for a profiled completion, else the immutable body), destination, kinds and floor.
    /// </summary>
    private async Task<bool> IsReceivedAsync(NoteRow row, QueueRow? keyed, CancellationToken ct)
    {
        if (row.ParentSessionId is not Guid session
            || keyed is null
            || keyed.AgentSessionId != session
            || keyed.DeliveryAttempts <= 0)
            return false;

        var expected = row.Body;
        TaskCompletionNotification.Delivery? rendering = null;
        if (row.Kind == LandNotificationKind.TaskCompletion && row.HasCompletionSnapshot)
        {
            rendering = TaskCompletionNotification.TryReadDelivery(row.CompletionDeliveryJson);
            if (rendering is null || !rendering.MemberQueueIds.Contains(keyed.Id))
                return false;
            expected = rendering.WireText;
        }

        var prompts = LandNoteReceipt.Prompts(
            _db.TranscriptEntries.AsNoTracking(),
            session,
            row.IsLegacy,
            row.Kind,
            keyed.LastDeliveryBaselineSequence,
            keyed.LastDeliveryStartedAt,
            _supervision.DeliveryVerification.UnobservableBaselineConfirmClockToleranceSeconds);
        if (prompts is null)
            return false;
        var texts = await prompts.OrderBy(p => p.Sequence).Select(p => p.Text!).ToListAsync(ct);
        if (!texts.Any(text => LandNoteReceipt.IsReceipt(expected, text)))
            return false;
        return rendering?.SpillPath is not { } spillPath
            || await AgentTaskLandNotificationService.FileHasSha256Async(spillPath, rendering.SpillSha256, ct);
    }

    private static List<ExpectationAdmissionCandidate> Admission(
        ExpectationDirectiveSettings directive, ExpectationProbeInput probes, DateTime asOf)
    {
        var settings = probes.QuotaSettings ?? new SubscriptionQuotaGateSettings();
        var results = new List<ExpectationAdmissionCandidate>();
        foreach (var target in directive.Targets ?? [])
        {
            foreach (var candidate in target.Candidates ?? [])
            {
                var probe = probes.Candidates.FirstOrDefault(row =>
                    ExpectationSubjects.SameRunner(row.RunnerId, target.RunnerId)
                    && row.AgentKind == candidate.AgentKind
                    && row.ModelLevel == candidate.ModelLevel
                    && string.Equals(
                        (row.SubscriptionKey ?? string.Empty).Trim(),
                        (candidate.SubscriptionKey ?? string.Empty).Trim(),
                        StringComparison.Ordinal));
                var state = probe is null
                    ? ExpectationAdmissionState.Unknown
                    : ExpectationAdmission.Classify(probe.Sample, probe.SampleKnown, probe.ModelHeld, settings, asOf);
                results.Add(new ExpectationAdmissionCandidate
                {
                    RunnerId = target.RunnerId,
                    AgentKind = candidate.AgentKind,
                    ModelLevel = candidate.ModelLevel,
                    SubscriptionKey = candidate.SubscriptionKey,
                    State = state,
                });
            }
        }

        return results;
    }

    private static DateTime SpecifyUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private sealed record TaskRow(
        Guid Id,
        Guid? CardId,
        Guid? ProjectId,
        Guid? ParentSessionId,
        AgentTaskStatus Status,
        AgentTaskRole Role,
        string? RunnerId,
        string? RepoPath,
        DateTime CreatedAt,
        Guid? AgentSessionId = null,
        DateTime? DispatchedAt = null,
        bool HasReport = false);

    private sealed record ScopedTask(TaskRow Row, string RepositoryScope);

    private sealed record EventRow(Guid Id, Guid TaskId, AgentTaskEventType Type, DateTime At, string Detail);

    private sealed record NoteRow(
        Guid Id,
        Guid TaskId,
        LandNotificationKind Kind,
        LandNotificationState State,
        DateTime CreatedAt,
        Guid? ParentSessionId,
        string? LastErrorCode,
        Guid? QueueMessageId,
        string Body,
        bool IsLegacy,
        bool HasCompletionSnapshot,
        string? CompletionDeliveryJson);

    private sealed record QueueRow(
        Guid Id,
        Guid AgentSessionId,
        QueuedMessageStatus Status,
        int DeliveryAttempts,
        long? LastDeliveryBaselineSequence,
        DateTime? LastDeliveryStartedAt);
}
