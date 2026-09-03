using Antiphon.Server.Application.Dtos;
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
/// The fleet-global projection behind <c>GET /api/attention</c> (CARD-0035 slice 1): work that needs
/// a human, each row naming the condition that put it there and the evidence for it.
///
/// <para><b>What keeps it worth opening is the non-membership rule.</b> A session that is mid-turn
/// with a live transcript is never listed for being slow — <see cref="AttentionKind.PastExpectedIdle"/>
/// gates on the SHARED working verdict (<c>SessionMessageQueueService.IsWorkingAsync</c>, not a
/// second implementation of it: three already run in lockstep and a fourth would be a defect). The
/// list is empty on a healthy day by construction, and a diagnostic list that cries wolf is worse
/// than no list, because the one real row arrives among nine false ones.</para>
///
/// <para><b>Read-only, and degrading rather than failing.</b> Every query is <c>AsNoTracking</c>;
/// nothing here writes, kills, retries or settles — every verb in the view is a human click against
/// an endpoint that already exists. A runner that cannot answer costs the caller the runner-derived
/// condition and nothing else: <see cref="AttentionDto.RunnerConsulted"/> goes false and the
/// DB-derived rows are returned exactly as they were.</para>
///
/// <para><b>One row per piece of work.</b> The task-scoped conditions are evaluated in priority
/// order and the first match wins, so a task whose session died before writing anything appears once
/// as <see cref="AttentionKind.DeadSession"/> rather than twice. The order is most-explanatory
/// first: the row a human reads should name the cause, not a downstream symptom of it.</para>
/// </summary>
public sealed class AttentionService
{
    /// <summary>
    /// How far back the two recency-windowed conditions look. Fixed rather than configurable in v1
    /// (CARD-0035 §5): there is no ack model, so recency IS the lifecycle for conditions that never
    /// self-clear, and one number nobody has asked to tune does not need a settings class.
    /// </summary>
    private static readonly TimeSpan RecencyWindow = TimeSpan.FromHours(24);

    /// <summary>
    /// Grace before a dispatched task with a silent session counts as never started. The dispatcher's
    /// own watchdog fails it outright at <c>DeliveryFailTimeoutMinutes</c> (10 by default); this
    /// surfaces the same predicate INSIDE that window, so the operator sees it while it is still
    /// recoverable instead of reading about it afterwards in the failure list.
    /// </summary>
    private static readonly TimeSpan NeverStartedGrace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Boot and the <c>WhenIdle</c> launch queue need a moment to persist the UI-origin prompt
    /// row. Fixed, not a setting: CARD-0287 is a read-time Warning, not a tunable watchdog.
    /// </summary>
    private static readonly TimeSpan CardlessDetailsPromptGrace = TimeSpan.FromMinutes(2);

    /// <summary>
    /// How long a live non-AlwaysOn agent may sit idle with no open task before it is a leftover
    /// (CARD-0239 arm 1). Matches <c>ContextCompactionSettings.IdleMinutes</c>'s default (480 = 8 h)
    /// as justification only — not read live, so dropping compaction's idle window cannot flood
    /// this projection. Idle auto-compaction can refresh the transcript clock (delay, not
    /// suppression): it fires only at ≥ 50 % fullness with a 24 h cooldown.
    /// </summary>
    private static readonly TimeSpan AgentLiveIdleThreshold = TimeSpan.FromHours(8);

    /// <summary>
    /// How long a leftover one-off identity (worktree cwd, or sole agent on a same-named
    /// zero-card board) may sit untouched before it is flagged (CARD-0239 arm 2). A PATCH
    /// resets the clock: a touched row is a watched row.
    /// </summary>
    private static readonly TimeSpan AgentLeftoverThreshold = TimeSpan.FromDays(2);

    /// <summary>
    /// The delivery watchdog's own clock, read from delegation settings so this projection and the
    /// sweep cannot disagree about when a brief is overdue for typing (CARD-0117 S5) or when a
    /// caller-session note is overdue for delivery (CARD-0267).
    /// </summary>
    private TimeSpan DeliveryFailTimeout =>
        TimeSpan.FromMinutes(Math.Max(0, _delegation.DeliveryFailTimeoutMinutes));

    /// <summary>
    /// The absolute floor added to the caller's estimate before "past expected" means anything.
    /// Without it the condition is just <c>2 x expected</c>, which on the live database would have
    /// flagged 25 of 67 successful delegations at some point in their run; with it, 11 — and the
    /// idle gate then removes the ones that were merely working hard (measured 2026-08-17).
    /// </summary>
    private static readonly TimeSpan PastExpectedFloor = TimeSpan.FromMinutes(30);

    /// <summary>Failures are context, not an alarm — a bounded tail, newest first.</summary>
    private const int RecentFailureCap = 20;

    private const int EvidenceChars = 400;
    private const int CheckDigestTailLines = 6;

    /// <summary>
    /// The one runner status this projection acts on. The runner's vocabulary is its own (a string,
    /// not our <see cref="SessionStatus"/>), and "Running" is the only value that can contradict a
    /// settled database row — an "Exited" runner session next to a DB row that says Stopped is two
    /// systems AGREEING, which is not news.
    /// </summary>
    private const string RunnerRunningStatus = "Running";

    /// <summary>The DB verdicts a live runner session contradicts.</summary>
    private static bool IsSettled(SessionStatus status) =>
        status is SessionStatus.Stopped or SessionStatus.Failed;

    private readonly AppDbContext _db;
    private readonly ISessionRunnerClient _runnerClient;
    private readonly SupervisionSettings _supervision;
    private readonly DelegationSettings _delegation;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AttentionService> _logger;
    private readonly IWorkspaceProgressProbe? _workspaceProgress;
    // CARD-0040 S3: only StaleAfterDays is read here. Optional for the same reason
    // _workspaceProgress is — every harness that predates this card keeps constructing this
    // service unchanged; production registers the options section, so DI always supplies it.
    private readonly CardWorkTransitionSettings _cardTransitions;
    private readonly ScheduleSettings _schedules;

    public AttentionService(
        AppDbContext db,
        ISessionRunnerClient runnerClient,
        IOptions<SupervisionSettings> supervision,
        // The deadlines are READ from delegation settings, never restated here (CARD-0020 S2/S3) —
        // the same contract the parked-message row already has with MaxDeliveryAttempts, so this
        // view and the sweep that fails the task can never disagree about when a task is overdue.
        IOptions<DelegationSettings> delegation,
        TimeProvider timeProvider,
        ILogger<AttentionService> logger,
        IWorkspaceProgressProbe? workspaceProgress = null,
        IOptions<CardWorkTransitionSettings>? cardTransitions = null,
        IOptions<ScheduleSettings>? schedules = null)
    {
        _db = db;
        _runnerClient = runnerClient;
        _supervision = supervision.Value;
        _delegation = delegation.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _workspaceProgress = workspaceProgress;
        _cardTransitions = cardTransitions?.Value ?? new CardWorkTransitionSettings();
        _schedules = schedules?.Value ?? new ScheduleSettings();
    }

    public async Task<AttentionDto> GetAsync(CancellationToken ct, bool includeProgressProbe = true)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var since = now - RecencyWindow;

        var items = new List<AttentionItemDto>();

        // Incidents consumed as a row's own evidence are struck off the recent-incident sweep, so
        // the same fact is never reported twice under two names.
        var attachedIncidents = new HashSet<Guid>();

        var open = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
            .ToListAsync(ct);
        var blocked = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Blocked && t.Role != AgentTaskRole.Check)
            .ToListAsync(ct);
        // CARD-0231: an unacknowledged pre-dispatch failure is counted until the reminder
        // disarms, and is NOT subject to RecentFailure's 24h window or its cap of 20.
        var unacknowledged = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Failed
                && t.DispatchedAt == null
                && t.NextCheckAt != null)
            .ToListAsync(ct);
        var failed = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Failed
                && t.CompletedAt != null
                && t.CompletedAt >= since
                && !(t.DispatchedAt == null && t.NextCheckAt != null))
            .OrderByDescending(t => t.CompletedAt)
            .Take(RecentFailureCap)
            .ToListAsync(ct);

        var subjects = open.Concat(blocked).Concat(failed).Concat(unacknowledged).ToList();
        var costs = await LoadSubtreeCostsAsync(subjects, ct);
        var checkDigests = await LoadLatestCheckDigestsAsync(subjects, ct);

        var routingBlocked = blocked
            .Where(t => t.Complexity is not null
                && t.FailureReason is not null
                && t.FailureReason.StartsWith(ComplexityRoutingService.RoutingExhaustedPrefix, StringComparison.Ordinal))
            .ToList();
        var questionBlocked = blocked.Except(routingBlocked).ToList();
        items.AddRange(await BuildBlockedAsync(questionBlocked, costs, checkDigests, ct));
        items.AddRange(await BuildRoutingExhaustedItemsAsync(routingBlocked, costs, ct));
        items.AddRange(await BuildCardNeedsDecisionAsync(ct));
        items.AddRange(await BuildCardStalledAsync(now, ct));
        var openItems = await BuildOpenTaskItemsAsync(
            open, now, costs, checkDigests, attachedIncidents, includeProgressProbe, ct);
        items.AddRange(openItems);
        items.AddRange(await BuildParkedMessageItemsAsync(ct));
        items.AddRange(await BuildCallerNoteUndeliveredItemsAsync(now, ct));
        items.AddRange(await BuildCardlessDetailsNoPromptItemsAsync(now, ct));
        items.AddRange(await BuildInboundUnconsumedItemsAsync(since, ct));
        items.AddRange(await BuildRecentIncidentItemsAsync(since, attachedIncidents, ct));
        items.AddRange(BuildFailureUnacknowledgedItems(unacknowledged, costs, checkDigests));
        items.AddRange(await BuildOrchestratorInvestigationItemsAsync(since, ct));
        items.AddRange(await BuildQueuedInputStuckItemsAsync(since, ct));
        items.AddRange(await BuildAgentOutlivedTaskItemsAsync(now, ct));
        items.AddRange(await BuildModelAvailabilityHoldItemsAsync(now, ct));
        items.AddRange(await BuildScheduleMisfireItemsAsync(now, ct));
        items.AddRange(BuildRecentFailureItems(failed, costs, checkDigests));

        // Asked unconditionally, because RunnerConsulted is a claim about whether anybody asked and a
        // flag that is hard-coded false is not a claim at all. ONE call: the diff below consumes this
        // same list rather than asking the runner a second question about the same moment.
        var runnerSessions = await TryListRunnerSessionsAsync(ct);
        if (runnerSessions is not null)
        {
            // The sessions the task pass has just pronounced dead. A disagreement row about one of
            // them is not a second opinion, it is the reason those rows are wrong — so it says so.
            var deadSessions = openItems
                .Where(i => i.Kind == AttentionKind.DeadSession && i.SessionId is not null)
                .Select(i => i.SessionId!.Value)
                .ToHashSet();
            items.AddRange(await BuildSessionDisagreementItemsAsync(runnerSessions, deadSessions, ct));
        }

        // Severity first — a row's rank IS its severity — then oldest-stuck first inside a band, so
        // the thing that has been waiting longest is the thing at the top of its group.
        var ordered = items
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.SinceUtc ?? DateTime.MaxValue)
            .ToList();

        return new AttentionDto(now, runnerSessions is not null, ordered);
    }

    /// <summary>
    /// Lightweight badge counts. It preserves every normal attention predicate except the workspace
    /// progress probe, which the badge neither displays nor needs to trigger.
    /// </summary>
    public async Task<AttentionSummaryDto> GetSummaryAsync(CancellationToken ct) =>
        AttentionSummaryDto.From(await GetAsync(ct, includeProgressProbe: false));

    // ---- condition 1: a delegate asked a question ------------------------------------------------

    private async Task<List<AttentionItemDto>> BuildBlockedAsync(
        IReadOnlyList<AgentTask> blocked,
        IReadOnlyDictionary<Guid, decimal> costs,
        IReadOnlyDictionary<Guid, CheckExplanation> checkDigests,
        CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();
        if (blocked.Count == 0)
            return items;

        // SinceUtc wants the moment the task BECAME blocked. Two paths write that event and they do
        // not agree on its type: the dispatcher's BlockAsync and the settlement-path question
        // detector both write Blocked, but a merge-back conflict sets Status = Blocked while
        // writing a CONFLICTED event (AgentTaskReplyService). Reading only Blocked events would
        // silently date every conflicted task to its dispatch instead.
        var ids = blocked.Select(t => t.Id).ToList();
        var events = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => ids.Contains(e.AgentTaskId)
                && (e.Type == AgentTaskEventType.Blocked || e.Type == AgentTaskEventType.Conflicted))
            .Select(e => new { e.AgentTaskId, e.At, e.Type })
            .ToListAsync(ct);
        var blockedAt = events
            .GroupBy(e => e.AgentTaskId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.At));
        var latestType = events
            .GroupBy(e => e.AgentTaskId)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.At).Last().Type);
        var mergeChildren = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.ParentTaskId != null && ids.Contains(t.ParentTaskId.Value)
                && t.Role == AgentTaskRole.Merge)
            .Select(t => new { t.ParentTaskId, t.Id, t.CreatedAt })
            .ToListAsync(ct);
        var mergeByParent = mergeChildren
            .GroupBy(t => t.ParentTaskId!.Value)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(t => t.CreatedAt).First().Id);

        foreach (var task in blocked)
        {
            var at = blockedAt.TryGetValue(task.Id, out var stamp) ? stamp : task.DispatchedAt;
            var kind = ClassifyBlocked(task, latestType.GetValueOrDefault(task.Id));

            // FailureReason is what the dispatcher and the conflict path write; the question path
            // writes the delegate's own words to Result and leaves FailureReason null. Preferring
            // the reason keeps a conflict readable. For a question, extract the trailing question
            // so a long report does not bury it past the 400-char evidence head (CARD-0033).
            var primary = BlockedContextBuilder.AttentionPrimary(task);
            var headline = kind switch
            {
                BlockedKind.MergeConflict when mergeByParent.TryGetValue(task.Id, out var mergeId) =>
                    $"Blocked — merge conflict; task {DelegationReportFormatter.Short(mergeId)} is resolving it",
                BlockedKind.MergeConflict => "Blocked — merge conflict.",
                BlockedKind.CostCeiling => "Blocked — run cost ceiling reached.",
                _ => "Blocked — waiting on a human answer.",
            };
            var canContinue = kind == BlockedKind.Question
                && task.AgentSessionId is not null
                && !string.IsNullOrWhiteSpace(task.StandingAuthority);
            var actions = kind == BlockedKind.CostCeiling
                ? new[] { AttentionAction.Cancel, AttentionAction.Escalate }
                : canContinue
                    ? new[] { AttentionAction.Continue, AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.Escalate }
                    : new[] { AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.Escalate };

            items.Add(new AttentionItemDto(
                AttentionKind.BlockedQuestion,
                AlertSeverity.Critical,
                task.Id,
                task.AgentSessionId,
                task.AgentId,
                null,
                task.Title,
                headline,
                Evidence(primary, checkDigests.GetValueOrDefault(task.Id)),
                at,
                costs.GetValueOrDefault(task.Id),
                actions));
        }

        return items;
    }

    private static BlockedKind ClassifyBlocked(AgentTask task, AgentTaskEventType latestBlockType)
    {
        if (latestBlockType == AgentTaskEventType.Conflicted)
            return BlockedKind.MergeConflict;
        if (task.FailureReason is { } reason)
        {
            if (reason.StartsWith(BlockedQuestion.CostCeilingPrefix, StringComparison.Ordinal))
                return BlockedKind.CostCeiling;
            if (reason.StartsWith(ComplexityRoutingService.RoutingExhaustedPrefix, StringComparison.Ordinal))
                return BlockedKind.RoutingExhausted;
        }

        return BlockedKind.Question;
    }

    // ---- CARD-0090: a complexity chain has no available candidate --------------------------------

    private async Task<List<AttentionItemDto>> BuildRoutingExhaustedItemsAsync(
        IReadOnlyList<AgentTask> blocked,
        IReadOnlyDictionary<Guid, decimal> costs,
        CancellationToken ct)
    {
        if (blocked.Count == 0)
            return [];

        var grouped = blocked
            .GroupBy(t => t.Complexity)
            .OrderBy(g => g.Key)
            .ToList();
        var cardIds = blocked.Where(t => t.CardId is not null).Select(t => t.CardId!.Value).Distinct().ToList();
        var cards = cardIds.Count == 0
            ? new Dictionary<Guid, Card>()
            : await _db.Cards.AsNoTracking()
                .Where(c => cardIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, ct);

        var items = new List<AttentionItemDto>(grouped.Count);
        foreach (var group in grouped)
        {
            var ordered = group.OrderBy(t => t.CreatedAt).ToList();
            var oldest = ordered[0];
            var complexity = group.Key!.Value;
            Guid? cardId = ordered.Select(t => t.CardId).Distinct().Count() == 1 ? oldest.CardId : null;
            Guid? boardId = cardId is Guid id && cards.TryGetValue(id, out var card) ? card.BoardId : null;
            var headline = oldest.FailureReason
                ?? $"{complexity} chain exhausted";
            if (ordered.Count > 1)
                headline += $" {ordered.Count} tasks waiting";

            var evidenceBits = ordered.Select(t =>
            {
                var cardBit = t.CardId is Guid cid && cards.TryGetValue(cid, out var c)
                    ? c.Identifier
                    : "no card";
                return $"{DelegationReportFormatter.Short(t.Id)} {cardBit} {t.Role}";
            });

            items.Add(new AttentionItemDto(
                AttentionKind.RoutingExhausted,
                AlertSeverity.Error,
                oldest.Id,
                null,
                oldest.AgentId,
                null,
                $"{complexity} chain exhausted",
                headline,
                string.Join("\n", evidenceBits),
                oldest.CreatedAt,
                costs.GetValueOrDefault(oldest.Id),
                [AttentionAction.OpenDrawer, AttentionAction.OpenCard],
                CardId: cardId,
                BoardId: boardId));
        }

        return items;
    }

    // ---- condition 2: a card is parked on a human decision --------------------------------------

    private async Task<List<AttentionItemDto>> BuildCardNeedsDecisionAsync(CancellationToken ct)
    {
        var moves = await _db.CardRevisions.AsNoTracking()
            .Where(r => (r.Kind == CardRevisionKind.Move || r.Kind == CardRevisionKind.Reopen)
                && r.ToStatus == CardStatus.NeedsDecision)
            .Include(r => r.Card)
            .Where(r => r.Card.Status == CardStatus.NeedsDecision && r.Card.ArchivedAt == null)
            .ToListAsync(ct);

        return moves
            .GroupBy(r => r.CardId)
            .Select(g => g.OrderByDescending(r => r.RevisionNumber).First())
            .Select(r => new AttentionItemDto(
                AttentionKind.CardNeedsDecision,
                AlertSeverity.Critical,
                null,
                null,
                null,
                null,
                $"{r.Card.Identifier} — {r.Card.Title}",
                "Needs a decision — nobody can move this but you.",
                r.Reason ?? "No decision question was recorded.",
                r.CreatedAt,
                null,
                [AttentionAction.OpenCard],
                r.CardId,
                r.Card.BoardId))
            .ToList();
    }

    // ---- a card in In Progress with nobody on it (CARD-0040) ------------------------------------

    /// <summary>
    /// In Progress, past <c>StaleAfterDays</c>, with no open bound task, no live session and no
    /// owning card session. Every input is a durable row, so this is a read-time projection with
    /// nothing stored — the <see cref="BuildCardNeedsDecisionAsync"/> precedent.
    /// </summary>
    /// <remarks>
    /// Detection only (CARD-0153's rule): the row exists to be seen, and nothing here un-stalls
    /// anything. It is deliberately NOT in the away digest — a Warning is not "needs you now".
    /// </remarks>
    private async Task<List<AttentionItemDto>> BuildCardStalledAsync(DateTime now, CancellationToken ct)
    {
        var threshold = now.AddDays(-Math.Max(1, _cardTransitions.StaleAfterDays));

        var candidates = await _db.Cards.AsNoTracking()
            .Where(c => c.Status == CardStatus.InProgress
                && c.ArchivedAt == null
                // The RunAttempt / card-spawn path owns this one; its own liveness rules apply.
                && c.OwnerSessionId == null
                && !_db.AgentTasks.Any(t => t.CardId == c.Id
                    && t.Role != AgentTaskRole.Check
                    && (t.Status == AgentTaskStatus.Dispatched
                        || t.Status == AgentTaskStatus.Working
                        || t.Status == AgentTaskStatus.Blocked))
                && !_db.AgentSessions.Any(s => s.CardId == c.Id
                    && (s.Status == SessionStatus.Created
                        || s.Status == SessionStatus.Starting
                        || s.Status == SessionStatus.Running
                        || s.Status == SessionStatus.Stopping)))
            .Select(c => new { c.Id, c.BoardId, c.Identifier, c.Title, c.StartedAt, c.UpdatedAt })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return [];

        var cardIds = candidates.Select(c => c.Id).ToList();

        // "Entered In Progress at": the latest Move/Reopen INTO In Progress. Without history the
        // card's StartedAt is the first active landing, which is the honest answer for a card
        // nobody has moved since the revision log began; UpdatedAt is the last resort.
        var enteredAt = (await _db.CardRevisions.AsNoTracking()
                .Where(r => cardIds.Contains(r.CardId)
                    && (r.Kind == CardRevisionKind.Move || r.Kind == CardRevisionKind.Reopen)
                    && r.ToStatus == CardStatus.InProgress)
                .Select(r => new { r.CardId, r.CreatedAt })
                .ToListAsync(ct))
            .GroupBy(r => r.CardId)
            .ToDictionary(g => g.Key, g => g.Max(r => r.CreatedAt));

        var lastTasks = (await _db.AgentTasks.AsNoTracking()
                .Where(t => t.CardId != null && cardIds.Contains(t.CardId!.Value) && t.Role != AgentTaskRole.Check)
                .Select(t => new
                {
                    t.Id,
                    CardId = t.CardId!.Value,
                    t.Status,
                    t.CompletedAt,
                    t.DispatchedAt,
                    t.CreatedAt,
                    t.FailureReason,
                })
                .ToListAsync(ct))
            .GroupBy(t => t.CardId)
            .ToDictionary(
                g => g.Key,
                g => g.OrderByDescending(t => t.CompletedAt ?? t.DispatchedAt ?? t.CreatedAt).First());

        var items = new List<AttentionItemDto>();
        foreach (var card in candidates)
        {
            var since = enteredAt.TryGetValue(card.Id, out var moved)
                ? moved
                : card.StartedAt ?? card.UpdatedAt;
            if (since > threshold)
                continue;

            string evidence;
            if (lastTasks.TryGetValue(card.Id, out var task))
            {
                var at = task.CompletedAt ?? task.DispatchedAt ?? task.CreatedAt;
                evidence = $"last task {DelegationReportFormatter.Short(task.Id)} {task.Status} "
                    + $"on {at:dd MMM}";
                if (!string.IsNullOrWhiteSpace(task.FailureReason))
                    evidence += $": {Excerpt(task.FailureReason)}";
            }
            else
            {
                evidence = "no task has ever been bound to this card";
            }

            var days = Math.Max(1, (int)Math.Floor((now - since).TotalDays));
            items.Add(new AttentionItemDto(
                AttentionKind.CardStalled,
                AlertSeverity.Warning,
                null,
                null,
                null,
                null,
                $"{card.Identifier} — {card.Title}",
                $"In Progress for {days} days with nobody on it.",
                evidence,
                since,
                null,
                [AttentionAction.OpenCard],
                card.Id,
                card.BoardId));
        }

        return items;
    }

    // ---- conditions 3-8: the open-task conditions, first match wins ------------------------------

    private async Task<List<AttentionItemDto>> BuildOpenTaskItemsAsync(
        IReadOnlyList<AgentTask> open,
        DateTime now,
        IReadOnlyDictionary<Guid, decimal> costs,
        IReadOnlyDictionary<Guid, CheckExplanation> checkDigests,
        HashSet<Guid> attachedIncidents,
        bool includeProgressProbe,
        CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();
        if (open.Count == 0)
            return items;

        var sessionIds = open
            .Where(t => t.AgentSessionId is not null)
            .Select(t => t.AgentSessionId!.Value)
            .Distinct()
            .ToList();

        var sessions = await _db.AgentSessions.AsNoTracking()
            .Where(s => sessionIds.Contains(s.Id))
            .Select(s => new { s.Id, s.Status, s.EndedAt, s.FailureReason, s.TerminationSource, s.ExitCode, s.LaunchBlock })
            .ToListAsync(ct);
        var sessionById = sessions.ToDictionary(
            s => s.Id,
            s => new AgentTaskLiveness.SessionSnapshot(
                s.Status, s.EndedAt, s.FailureReason, s.TerminationSource, s.ExitCode, s.LaunchBlock));

        // "Has the session written anything at all" — the FailNeverStartedAsync predicate, asked in
        // one query for the whole candidate set rather than once per task.
        var withTranscript = (await _db.TranscriptEntries.AsNoTracking()
                .Where(e => sessionIds.Contains(e.AgentSessionId))
                .Select(e => e.AgentSessionId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var uncorrelated = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.DelegateReportUncorrelated
                && i.SessionId != null
                && sessionIds.Contains(i.SessionId!.Value))
            .Select(i => new { i.Id, i.SessionId, i.Message, i.CreatedAt })
            .ToListAsync(ct);

        var briefs = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Origin == QueuedMessageOrigin.Delegation
                && sessionIds.Contains(m.AgentSessionId))
            .Select(m => new { m.AgentSessionId, m.Body, m.Status, m.CreatedAt })
            .ToListAsync(ct);

        // CARD-0288: newest TurnEnd + assistant rows that carry a report token, scoped to this
        // open-task session set. Do not table-scan TranscriptEntries.
        var newestTurnEnds = (await _db.TranscriptEntries.AsNoTracking()
                .Where(e => sessionIds.Contains(e.AgentSessionId) && e.Kind == TranscriptKinds.TurnEnd)
                .Select(e => new { e.AgentSessionId, e.Sequence, e.CreatedAt, e.Kind, e.StopReason })
                .ToListAsync(ct))
            .GroupBy(e => e.AgentSessionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Sequence).First());
        var reportTextsBySession = (await _db.TranscriptEntries.AsNoTracking()
                .Where(e => sessionIds.Contains(e.AgentSessionId)
                    && e.Kind == TranscriptKinds.AssistantText
                    && e.Text != null
                    && e.Text.Contains("[antiphon-report:"))
                .Select(e => new { e.AgentSessionId, e.Text })
                .ToListAsync(ct))
            .GroupBy(e => e.AgentSessionId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.Text!).ToList());

        foreach (var task in open)
        {
            AgentTaskLiveness.SessionSnapshot? session =
                task.AgentSessionId is Guid sid && sessionById.TryGetValue(sid, out var row) ? row : null;
            var elapsed = task.DispatchedAt is { } dispatched ? now - dispatched : (TimeSpan?)null;
            var digest = checkDigests.GetValueOrDefault(task.Id);
            var cost = costs.GetValueOrDefault(task.Id);

            // 3. DeadSession. The predicate and its wording are BOTH shared with the dispatcher's
            // dead-session sweep (CARD-0021) — this projection surfaces the state and that sweep
            // acts on it, so a row here the sweep would not fail (or the reverse) would be a defect
            // with no single place to fix it. See AgentTaskLiveness.
            if (AgentTaskLiveness.IsDeadSession(task.AgentSessionId, session))
            {
                var what = AgentTaskLiveness.Describe(task.AgentSessionId, session);

                items.Add(new AttentionItemDto(
                    AttentionKind.DeadSession,
                    AlertSeverity.Error,
                    task.Id,
                    task.AgentSessionId,
                    task.AgentId,
                    null,
                    task.Title,
                    $"Still {task.Status} but {what}.",
                    Evidence(session?.FailureReason ?? task.FailureReason ?? what, digest),
                    session?.EndedAt ?? task.DispatchedAt,
                    cost,
                    [AttentionAction.Retry, AttentionAction.Cancel, AttentionAction.Escalate]));
                continue;
            }

            // 4. NeverStarted — dispatched, past the grace, and the session has written nothing.
            if (task.Status == AgentTaskStatus.Dispatched
                && elapsed is { } age
                && age > NeverStartedGrace
                && !withTranscript.Contains(task.AgentSessionId!.Value))
            {
                items.Add(new AttentionItemDto(
                    AttentionKind.NeverStarted,
                    AlertSeverity.Error,
                    task.Id,
                    task.AgentSessionId,
                    task.AgentId,
                    null,
                    task.Title,
                    $"Dispatched {Duration(age)} ago and the session has written nothing.",
                    Evidence(
                        "The session exists and is not dead, but no transcript entry has ever been "
                        + "ingested for it — the brief may never have reached the composer.",
                        digest),
                    task.DispatchedAt,
                    cost,
                    [AttentionAction.Retry, AttentionAction.Cancel, AttentionAction.Escalate]));
                continue;
            }

            // 5. BriefUndelivered — dispatched, past the delivery grace, brief still Pending,
            // session working (CARD-0117 S5). The watchdog deferred this; without a row here the
            // task is silent until Overdue previews the ceiling at 80%.
            if (task.Status == AgentTaskStatus.Dispatched
                && elapsed is { } deliveredAgo
                && deliveredAgo > DeliveryFailTimeout
                && task.AgentSessionId is Guid briefSession
                && briefs.Any(m => m.AgentSessionId == briefSession
                    && (task.DispatchedAt is null || m.CreatedAt >= task.DispatchedAt)
                    && m.Status == QueuedMessageStatus.Pending
                    && m.Body.Contains(DelegationReportFormatter.TaskMarker(task.Id)))
                && await SessionMessageQueueService.IsWorkingAsync(_db, briefSession, ct))
            {
                items.Add(new AttentionItemDto(
                    AttentionKind.BriefUndelivered,
                    AlertSeverity.Warning,
                    task.Id,
                    task.AgentSessionId,
                    task.AgentId,
                    null,
                    task.Title,
                    $"Brief still queued Pending after {Duration(deliveredAgo)}; the session is working.",
                    Evidence(
                        "The delivery watchdog declined to fail this: the brief's own queue row is "
                        + "still Pending and the session is mid-turn. TaskDeadlinePolicy owns the bound "
                        + "(20/90/240 minutes) and does not kill.",
                        digest),
                    task.DispatchedAt is { } at ? at + DeliveryFailTimeout : task.DispatchedAt,
                    cost,
                    [AttentionAction.OpenDrawer, AttentionAction.Retry, AttentionAction.Cancel,
                        AttentionAction.Escalate]));
                continue;
            }

            // 6. ReportUnsettled — marked closing line already in the transcript, task still open
            // (CARD-0288). Before UncorrelatedReport: a token for THIS task is the more
            // explanatory row. No idle gate — the token is the evidence it is not still working.
            if ((task.Status == AgentTaskStatus.Dispatched || task.Status == AgentTaskStatus.Working)
                && task.AgentSessionId is Guid reportSession
                && newestTurnEnds.TryGetValue(reportSession, out var newestEnd)
                && TranscriptKinds.IsReportBoundary(newestEnd.Kind, newestEnd.StopReason)
                && reportTextsBySession.TryGetValue(reportSession, out var reportTexts))
            {
                string? verdict = null;
                foreach (var text in reportTexts)
                {
                    if (!DelegationReportFormatter.TryFindReportToken(task.Id, text, out var found))
                        continue;
                    verdict = found;
                    break;
                }

                if (verdict is not null)
                {
                    items.Add(new AttentionItemDto(
                        AttentionKind.ReportUnsettled,
                        AlertSeverity.Warning,
                        task.Id,
                        task.AgentSessionId,
                        task.AgentId,
                        null,
                        task.Title,
                        $"Finished report is in the transcript; the task is still {task.Status}.",
                        Evidence(
                            $"Marked {verdict} at TurnEnd #{newestEnd.Sequence}. Nothing here settles it — "
                            + "the dispatcher re-hands on the next tick.",
                            digest),
                        newestEnd.CreatedAt,
                        cost,
                        [AttentionAction.OpenDrawer, AttentionAction.Retry, AttentionAction.Cancel]));
                    continue;
                }
            }

            // 7. UnmarkedWaiting — nudged, idle, no report token (CARD-0294). After
            // ReportUnsettled: a token for THIS task is the more explanatory row. No git
            // gate. Mid-turn is not waiting.
            if ((task.Status == AgentTaskStatus.Dispatched || task.Status == AgentTaskStatus.Working)
                && task.ReportNudgedAt is DateTime waitingSince
                && task.AgentSessionId is Guid waitingSession)
            {
                var hasToken = false;
                if (reportTextsBySession.TryGetValue(waitingSession, out var waitingTexts))
                {
                    foreach (var text in waitingTexts)
                    {
                        if (!DelegationReportFormatter.TryFindReportToken(task.Id, text, out _))
                            continue;
                        hasToken = true;
                        break;
                    }
                }

                if (!hasToken
                    && !await SessionMessageQueueService.IsWorkingAsync(_db, waitingSession, ct))
                {
                    var waitAge = now - waitingSince;
                    var blockAfter = Math.Max(0, _delegation.UnmarkedWaitingMinutes);
                    var blockHint = blockAfter > 0
                        ? $"S1 will Block at {blockAfter}m idle if they stay that way."
                        : "The Blocked sweep is disarmed (UnmarkedWaitingMinutes <= 0).";
                    items.Add(new AttentionItemDto(
                        AttentionKind.UnmarkedWaiting,
                        AlertSeverity.Warning,
                        task.Id,
                        task.AgentSessionId,
                        task.AgentId,
                        null,
                        task.Title,
                        "Ended a turn with no closing line; asked once, still idle.",
                        Evidence(
                            $"Nudged {Duration(waitAge)} ago. {blockHint} "
                            + "Do not read Herdr done as finished.",
                            digest),
                        waitingSince,
                        cost,
                        [AttentionAction.OpenDrawer, AttentionAction.Retry, AttentionAction.Cancel]));
                    continue;
                }
            }

            // 8. UncorrelatedReport — it reported, and the report could not be tied back to the task.
            var orphanReport = uncorrelated
                .Where(i => UncorrelatedReportEvidence.IsEvidenceFor(task, i.SessionId, i.CreatedAt))
                .OrderByDescending(i => i.CreatedAt)
                .FirstOrDefault();
            if (orphanReport is not null)
            {
                attachedIncidents.Add(orphanReport.Id);
                items.Add(new AttentionItemDto(
                    AttentionKind.UncorrelatedReport,
                    AlertSeverity.Error,
                    task.Id,
                    task.AgentSessionId,
                    task.AgentId,
                    null,
                    task.Title,
                    "The delegate reported, but the report could not be correlated to this task.",
                    Evidence(orphanReport.Message, digest),
                    orphanReport.CreatedAt,
                    cost,
                    [AttentionAction.Retry, AttentionAction.Cancel, AttentionAction.Escalate]));
                continue;
            }

            // 9. PastExpectedIdle. THE exclusion lives here: a session that is mid-turn is not
            // listed, however far past the estimate it has run. The working verdict is the shared
            // one, and it is asked LAST — only for tasks that already crossed the clock — so the
            // query cost is bounded by the handful of rows that could qualify.
            var threshold = PastExpectedThreshold(task.ExpectedDurationMinutes);
            if (elapsed is { } ran && ran > threshold)
            {
                var working = await SessionMessageQueueService.IsWorkingAsync(
                    _db, task.AgentSessionId!.Value, ct);
                if (!working)
                {
                    items.Add(new AttentionItemDto(
                        AttentionKind.PastExpectedIdle,
                        AlertSeverity.Warning,
                        task.Id,
                        task.AgentSessionId,
                        task.AgentId,
                        null,
                        task.Title,
                        $"Idle at the prompt after {Duration(ran)} against a "
                        + $"{task.ExpectedDurationMinutes}m estimate.",
                        Evidence(
                            "The session is not mid-turn, so it is not still working on this — the "
                            + "usual shape is a delegate that finished and never reported.",
                            digest),
                        task.DispatchedAt!.Value + threshold,
                        cost,
                        [AttentionAction.OpenDrawer, AttentionAction.Retry, AttentionAction.Cancel,
                            AttentionAction.Escalate]));
                    continue;
                }
            }

            // 9. ProgressStalled (CARD-0153) — working, rows still landing, none of them novel.
            // After PastExpectedIdle (which declines the mid-turn case) and before Overdue, so a
            // working session with a stall verdict gets the more explanatory row.
            // CARD-0216 §4 / CARD-0217 S8: the git probe is skipped until StallMinutes have
            // elapsed (a task dispatched a few minutes ago cannot be stalled yet) and is never
            // run for the badge summary. EvaluateAsync itself stays, so summary counts still
            // match a full sweep.
            WorkspaceProgressArm? workspace = null;
            var stallSettings = _delegation.StallDetection;
            if (includeProgressProbe
                && _workspaceProgress is not null
                && stallSettings.Enabled
                && stallSettings.StallMinutes > 0
                && task.DispatchedAt is DateTime dispatchedAt
                && now - dispatchedAt >= TimeSpan.FromMinutes(stallSettings.StallMinutes))
            {
                workspace = await _workspaceProgress.ProbeProgressAsync(
                    task.WorkingDirectory, dispatchedAt,
                    task.Workspace == WorkspaceMode.Shared, ct);
            }

            var stall = await TaskProgressPolicy.EvaluateAsync(
                _db, task, now, _delegation, ct, workspace);
            if (stall is not null)
            {
                items.Add(new AttentionItemDto(
                    AttentionKind.ProgressStalled,
                    AlertSeverity.Warning,
                    task.Id,
                    task.AgentSessionId,
                    task.AgentId,
                    null,
                    task.Title,
                    stall.Summary,
                    Evidence(stall.FailureReason, digest),
                    stall.LastProgressAt,
                    cost,
                    [AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.OpenDrawer]));
                continue;
            }

            // 10. Overdue — closing on a deadline that will FAIL this task (CARD-0020 S2/S3). It is
            // asked AFTER PastExpectedIdle because that condition owns the idle case and declines
            // the mid-turn one; the deadline that matters for a working session is the phase clock,
            // and the ceiling covers both. Same shared policy the dispatcher's sweep acts on, so a
            // row here is always the failure that is coming, in the same words.
            var deadline = await TaskDeadlinePolicy.EvaluateAsync(_db, task, now, _delegation, ct);
            if (deadline is not null)
            {
                items.Add(new AttentionItemDto(
                    AttentionKind.Overdue,
                    // Warning, not Error: nothing is broken yet, and at 80% of a 240-minute ceiling
                    // the honest reading is "look at this", not "this has failed".
                    AlertSeverity.Warning,
                    task.Id,
                    task.AgentSessionId,
                    task.AgentId,
                    null,
                    task.Title,
                    deadline.Breached
                        ? $"Past its deadline — the next sweep will fail it. {deadline.Summary}"
                        : $"Closing on the deadline that will fail it. {deadline.Summary}",
                    Evidence(
                        deadline.Kind == TaskDeadlinePolicy.DeadlineKind.Ceiling
                            ? "The hard wall-clock ceiling for this role. Crossing it fails the task "
                              + "with the phase named; nothing is killed and nothing is retried, so "
                              + "a reply, a check or a cancel are all still open to you."
                            : "The session is mid-turn and the phase it is in has run past its own "
                              + "deadline. Crossing it fails the task; the session is not killed.",
                        digest),
                    task.DispatchedAt,
                    cost,
                    [AttentionAction.OpenDrawer, AttentionAction.Reply, AttentionAction.Cancel,
                        AttentionAction.Escalate]));
                continue;
            }

            // 11. ChecksSpent — checks ran, the budget is gone, the task is still open.
            if (task.CheckCount > 0 && task.NextCheckAt is null)
            {
                items.Add(new AttentionItemDto(
                    AttentionKind.ChecksSpent,
                    AlertSeverity.Warning,
                    task.Id,
                    task.AgentSessionId,
                    task.AgentId,
                    null,
                    task.Title,
                    $"The check budget ran out after {task.CheckCount} check(s) and the task is "
                    + "still open — nothing is watching it now.",
                    Evidence(
                        "No further check-in is scheduled for this task, so no automatic surface "
                        + "will report on it again.",
                        digest),
                    task.DispatchedAt,
                    cost,
                    [AttentionAction.OpenDrawer, AttentionAction.Retry, AttentionAction.Cancel,
                        AttentionAction.Escalate]));
            }
        }

        return items;
    }

    /// <summary>
    /// Past-expected is <c>max(2 x expected, expected + 30m)</c>. Doubling alone is far too tight at
    /// the small end — a 5-minute estimate would flag at 10 — and the floor is what stops the
    /// condition from being an alarm about optimistic estimates rather than about stuck work.
    /// </summary>
    private static TimeSpan PastExpectedThreshold(int expectedMinutes)
    {
        var expected = TimeSpan.FromMinutes(Math.Max(0, expectedMinutes));
        var doubled = expected + expected;
        var floored = expected + PastExpectedFloor;
        return doubled > floored ? doubled : floored;
    }

    // ---- condition 2: a parked queued message ----------------------------------------------------

    private async Task<List<AttentionItemDto>> BuildParkedMessageItemsAsync(CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();

        // Parking is NOT a status (CARD-0055): a message is parked when it is still Pending and has
        // spent its attempts. The cap is READ from supervision settings — this feature adds nothing
        // to that file — so the view and the queue can never disagree about what parked means.
        var maxAttempts = Math.Max(1, _supervision.DeliveryVerification.MaxDeliveryAttempts);
        var parked = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Status == QueuedMessageStatus.Pending && m.DeliveryAttempts >= maxAttempts)
            .Select(m => new
            {
                m.Id, m.AgentSessionId, m.Body, m.Origin, m.CreatedAt,
                m.DeliveryAttempts, m.LastDeliveryStartedAt,
            })
            .ToListAsync(ct);
        if (parked.Count == 0)
            return items;

        var sessionIds = parked.Select(m => m.AgentSessionId).Distinct().ToList();
        var owners = await ResolveSessionOwnersAsync(sessionIds, ct);

        var channelBound = (await _db.ChatChannels.AsNoTracking()
                .Where(c => c.AgentId != null)
                .Select(c => c.AgentId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        foreach (var message in parked)
        {
            var owner = owners.GetValueOrDefault(message.AgentSessionId);
            var agentId = owner?.AgentId;
            var agentName = owner?.AgentName;

            // Critical when the agent is channel-bound: a parked channel reply is not a stalled
            // delivery, it is a person on the other end of a line that has gone dead.
            var bound = agentId is Guid id && channelBound.Contains(id);
            items.Add(new AttentionItemDto(
                AttentionKind.ParkedMessage,
                bound ? AlertSeverity.Critical : AlertSeverity.Error,
                null,
                message.AgentSessionId,
                agentId,
                message.Id,
                $"Parked message to {agentName ?? $"session {message.AgentSessionId.ToString("N")[..8]}"}",
                $"{message.Origin} message parked after {message.DeliveryAttempts} delivery "
                + $"attempt(s) — nothing will retry it."
                + (bound ? " This agent is channel-bound: someone is waiting on a reply." : string.Empty),
                Excerpt(message.Body),
                message.LastDeliveryStartedAt ?? message.CreatedAt,
                null,
                [AttentionAction.SendNow, AttentionAction.CancelMessage]));
        }

        return items;
    }

    // ---- CARD-0267: aging undelivered caller-session notes --------------------------------------

    private async Task<List<AttentionItemDto>> BuildCallerNoteUndeliveredItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();
        var cutoff = now - DeliveryFailTimeout;

        // Narrow Pending-origin lookup: IX_SessionQueuedMessages_OpenChannelCorrelations begins
        // with Origin, Status. Do not call IsWorkingAsync — the signal is that the note has not
        // arrived, not a claim about why. Parked notes stay eligible; ParkedMessage is the more
        // specific delivery-failure diagnosis for the same row.
        var candidates = await _db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Status == QueuedMessageStatus.Pending
                && (m.Origin == QueuedMessageOrigin.Delegation
                    || m.Origin == QueuedMessageOrigin.Check)
                && m.CreatedAt < cutoff)
            .Select(m => new
            {
                m.Id, m.AgentSessionId, m.Origin, m.SourceTaskId, m.ConversationKey, m.CreatedAt,
            })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return items;

        var messageTaskIds = new Dictionary<Guid, Guid>();
        foreach (var message in candidates)
        {
            if (message.SourceTaskId is Guid sourced)
            {
                messageTaskIds[message.Id] = sourced;
                continue;
            }

            if (message.Origin == QueuedMessageOrigin.Check
                && AgentTaskCheckService.TryParseCheckConversationKey(
                    message.ConversationKey, out var parsed))
            {
                messageTaskIds[message.Id] = parsed;
            }
        }

        var taskIds = messageTaskIds.Values.Distinct().ToList();
        if (taskIds.Count == 0)
            return items;

        var tasks = await _db.AgentTasks.AsNoTracking()
            .Where(t => taskIds.Contains(t.Id))
            .Select(t => new { t.Id, t.Title, t.ReplyTo, t.ParentSessionId, t.AgentId })
            .ToDictionaryAsync(t => t.Id, ct);

        foreach (var message in candidates)
        {
            if (!messageTaskIds.TryGetValue(message.Id, out var taskId))
                continue;
            if (!tasks.TryGetValue(taskId, out var task))
                continue;
            if (task.ReplyTo != AgentTaskReplyTo.Session)
                continue;
            if (task.ParentSessionId is not Guid parent || parent != message.AgentSessionId)
                continue;

            var age = now - message.CreatedAt;
            var sessionShort = message.AgentSessionId.ToString("N")[..8];
            items.Add(new AttentionItemDto(
                AttentionKind.CallerNoteUndelivered,
                AlertSeverity.Warning,
                task.Id,
                message.AgentSessionId,
                task.AgentId,
                message.Id,
                task.Title,
                $"{message.Origin} note still Pending on caller session {sessionShort} after {Duration(age)}.",
                Evidence(
                    "The caller-session note is still Pending past the shared delivery grace. "
                    + "Detection only: silence is not evidence the delegate is still running.",
                    check: null),
                message.CreatedAt,
                null,
                [AttentionAction.OpenDrawer]));
        }

        return items;
    }

    // ---- CARD-0287: cardless Details-only start still idle --------------------------------------

    /// <summary>
    /// Current cardless interactive sessions that launched without a prompt while Details is
    /// standing-job metadata. Detection only — nothing here types Details or queues a message.
    /// The CARD-0283 Information log in <c>AgentControlService</c> remains the launch-time
    /// forensic; this row is the caller-visible, self-clearing projection of the same fact.
    /// </summary>
    private async Task<List<AttentionItemDto>> BuildCardlessDetailsNoPromptItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();
        var cutoff = now - CardlessDetailsPromptGrace;

        // Fresh interactive launch shape only: StartInteractiveSessionAsync stamps CreatedAt and
        // StartedAt from one now and always records a composition (empty string is meaningful).
        // Resume restamps only StartedAt; Herdr attach leaves the composed stamp null.
        var candidates = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.Status == SessionStatus.Running
                && s.CardId == null
                && s.StartedAt < cutoff
                && s.CreatedAt == s.StartedAt
                && s.ComposedBundleStamp != null)
            .Select(s => new { s.Id, s.StartedAt })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return items;

        var sessionIds = candidates.Select(s => s.Id).ToList();
        var keys = sessionIds.Select(id => id.ToString("D")).ToList();

        var standing = await _db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null && keys.Contains(a.PersistentSessionId))
            .Select(a => new { a.Id, a.Name, a.PersistentSessionId, a.Details })
            .ToListAsync(ct);

        var owners = new Dictionary<Guid, (Guid AgentId, string AgentName, string Details)>();
        foreach (var agent in standing)
        {
            // Whitespace is not a standing job; apply the same semantics as the launch log.
            if (string.IsNullOrWhiteSpace(agent.Details))
                continue;
            if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                continue;
            owners.TryAdd(sessionId, (agent.Id, agent.Name, agent.Details));
        }

        if (owners.Count == 0)
            return items;

        var ownedIds = owners.Keys.ToList();

        var withTranscript = (await _db.TranscriptEntries.AsNoTracking()
                .Where(e => ownedIds.Contains(e.AgentSessionId))
                .Select(e => e.AgentSessionId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        var withUiQueue = (await _db.SessionQueuedMessages.AsNoTracking()
                .Where(m => ownedIds.Contains(m.AgentSessionId)
                    && m.Origin == QueuedMessageOrigin.Ui)
                .Select(m => m.AgentSessionId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        foreach (var session in candidates)
        {
            if (!owners.TryGetValue(session.Id, out var owner))
                continue;
            if (withTranscript.Contains(session.Id) || withUiQueue.Contains(session.Id))
                continue;

            var age = now - session.StartedAt;
            items.Add(new AttentionItemDto(
                AttentionKind.CardlessDetailsNoPrompt,
                AlertSeverity.Warning,
                null,
                session.Id,
                owner.AgentId,
                null,
                owner.AgentName,
                $"Cardless start still idle after {Duration(age)} because Details was not sent as a prompt.",
                Evidence(
                    "Current Details: "
                    + Excerpt(owner.Details)
                    + ". No transcript entries and no UI start or message queue row. "
                    + "Details is standing-job metadata, not a first prompt. "
                    + "Send a session message now, or pass StartAgentRequest.Prompt on a future cardless start.",
                    check: null),
                session.StartedAt,
                null,
                [AttentionAction.OpenAgent]));
        }

        return items;
    }

    // ---- condition 9: recent Error-or-worse incidents, grouped ----------------------------------

    private async Task<List<AttentionItemDto>> BuildRecentIncidentItemsAsync(
        DateTime since, HashSet<Guid> attachedIncidents, CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();

        var recent = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.Severity >= AlertSeverity.Error && i.CreatedAt >= since)
            .Select(i => new { i.Id, i.AgentId, i.SessionId, i.Kind, i.Severity, i.Message, i.CreatedAt, i.FailureReason })
            .ToListAsync(ct);

        var fresh = recent.Where(i => !attachedIncidents.Contains(i.Id)).ToList();
        if (fresh.Count == 0)
            return items;

        // CARD-0324: a later registry-path Grok ready launch closes the sign-in episode.
        var signIn = fresh
            .Where(i => i.Kind == AgentIncidentKind.ProviderSignInRequired)
            .ToList();
        if (signIn.Count > 0)
        {
            var closed = new HashSet<Guid>();
            foreach (var row in signIn)
            {
                if (await GrokSignInIncident.IsClosedAsync(_db, row.CreatedAt, ct))
                    closed.Add(row.Id);
            }

            if (closed.Count > 0)
            {
                fresh = fresh.Where(i => !closed.Contains(i.Id)).ToList();
                if (fresh.Count == 0)
                    return items;
            }
        }

        var agentIds = fresh.Where(i => i.AgentId != null).Select(i => i.AgentId!.Value).Distinct().ToList();
        var agentNames = agentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Agents.AsNoTracking()
                .Where(a => agentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        // Grouped per agent AND per kind. Per agent alone was the plan's word, but it would fold
        // DeliveryVerificationFailed into DeliveryTransportFailed on the same agent and lose the
        // diagnosis; per (agent, kind) collapsed 107 live rows into 4 without merging two different
        // problems into one line (measured 2026-08-17).
        foreach (var group in fresh.GroupBy(i => (i.AgentId, i.Kind)))
        {
            var newest = group.OrderByDescending(i => i.CreatedAt).First();
            var oldest = group.Min(i => i.CreatedAt);
            var severity = group.Max(i => i.Severity);
            var name = group.Key.AgentId is Guid agentId
                ? agentNames.GetValueOrDefault(agentId) ?? agentId.ToString("N")[..8]
                : $"session {newest.SessionId?.ToString("N")[..8] ?? "unknown"}";

            items.Add(new AttentionItemDto(
                AttentionKind.RecentCriticalIncident,
                severity,
                null,
                newest.SessionId,
                group.Key.AgentId,
                null,
                name,
                group.Count() == 1
                    ? $"{severity} {group.Key.Kind} in the last 24h."
                    : $"{group.Count()} x {severity} {group.Key.Kind} in the last 24h, most recently "
                      + $"{Duration(_timeProvider.GetUtcNow().UtcDateTime - newest.CreatedAt)} ago.",
                Excerpt(newest.Message),
                oldest,
                null,
                [AttentionAction.OpenAgent]));
        }

        return items;
    }

    // ---- inbound the AppHost never consumed (CARD-0245 S2) --------------------------------

    private async Task<List<AttentionItemDto>> BuildInboundUnconsumedItemsAsync(
        DateTime since, CancellationToken ct)
    {
        var sinceOffset = new DateTimeOffset(DateTime.SpecifyKind(since, DateTimeKind.Utc));
        var rows = await _db.ChannelIngressIncidents.AsNoTracking()
            .Where(i => i.DetectedAt >= sinceOffset)
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var keys = rows.Select(r => (r.Provider, r.ConversationId)).Distinct().ToList();
        var channels = await _db.ChatChannels.AsNoTracking()
            .Where(c => c.Enabled)
            .Select(c => new { c.Provider, c.ExternalId, c.AgentId, c.Title })
            .ToListAsync(ct);
        var bound = channels
            .Where(c => keys.Any(k => k.Provider == c.Provider && k.ConversationId == c.ExternalId))
            .GroupBy(c => (c.Provider, c.ExternalId))
            .ToDictionary(g => g.Key, g => g.First());

        return rows.Select(row =>
        {
            bound.TryGetValue((row.Provider, row.ConversationId), out var channel);
            var title = channel?.Title ?? $"{row.Provider}:{row.ConversationId}";
            var ack = row.Acknowledged
                ? "Acknowledged to the chat."
                : (row.AcknowledgementError ?? "Acknowledgement not yet delivered.");
            return new AttentionItemDto(
                AttentionKind.InboundUnconsumed,
                AlertSeverity.Critical,
                null,
                null,
                channel?.AgentId,
                null,
                title,
                $"Inbound {row.Provider} message unconsumed for {Duration(_timeProvider.GetUtcNow() - row.FirstSeenAt)}; queued at {row.Topic}/{row.Partition}:{row.Offset}.",
                $"message {row.OriginalMessageId}. first seen {row.FirstSeenAt:o}. {ack}"
                    + (string.IsNullOrWhiteSpace(row.AppHostHealth) ? "" : $" AppHost health: {row.AppHostHealth}."),
                row.FirstSeenAt.UtcDateTime,
                null,
                channel?.AgentId is Guid ? [AttentionAction.OpenAgent] : Array.Empty<AttentionAction>());
        }).ToList();
    }

    // ---- counted until heard: a never-dispatched failure whose reminder is still armed -----------

    private List<AttentionItemDto> BuildFailureUnacknowledgedItems(
        IReadOnlyList<AgentTask> unacknowledged,
        IReadOnlyDictionary<Guid, decimal> costs,
        IReadOnlyDictionary<Guid, CheckExplanation> checkDigests)
    {
        var max = Math.Max(1, _delegation.CheckMaxCount);
        return unacknowledged.Select(task =>
        {
            var session = task.ParentSessionId is Guid parent
                ? DelegationReportFormatter.Short(parent)
                : "none";
            return new AttentionItemDto(
                AttentionKind.FailureUnacknowledged,
                AlertSeverity.Error,
                task.Id,
                task.AgentSessionId,
                task.AgentId,
                null,
                task.Title,
                $"Failed before dispatch; no completion note has reached session {session} — reminder {task.CheckCount}/{max}",
                Evidence(task.FailureReason ?? "No failure reason was recorded.", checkDigests.GetValueOrDefault(task.Id)),
                task.CompletedAt,
                costs.GetValueOrDefault(task.Id),
                [AttentionAction.Retry, AttentionAction.OpenDrawer]);
        }).ToList();
    }

    // ---- CARD-0247: an orchestrator did a cold investigation run --------------------------------

    private async Task<List<AttentionItemDto>> BuildOrchestratorInvestigationItemsAsync(
        DateTime since, CancellationToken ct)
    {
        var rows = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.OrchestratorInvestigation
                && i.CreatedAt >= since
                && i.SessionId != null)
            .Select(i => new { i.AgentId, i.SessionId, i.Message, i.CreatedAt, i.FailureReason })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        var agentIds = rows.Where(r => r.AgentId is not null).Select(r => r.AgentId!.Value).Distinct().ToList();
        var agentNames = agentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Agents.AsNoTracking()
                .Where(a => agentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        return rows.Select(r =>
        {
            var shortSession = DelegationReportFormatter.Short(r.SessionId!.Value);
            var title = r.AgentId is Guid agentId && agentNames.TryGetValue(agentId, out var name)
                ? name
                : $"Orchestrator session {shortSession}";
            var actions = r.AgentId is null
                ? new[] { AttentionAction.OpenDrawer }
                : new[] { AttentionAction.OpenAgent, AttentionAction.OpenDrawer };
            return new AttentionItemDto(
                AttentionKind.OrchestratorInvestigation,
                AlertSeverity.Warning,
                null,
                r.SessionId,
                r.AgentId,
                null,
                title,
                r.Message,
                Excerpt(
                    "The orchestrator read source files itself instead of dispatching a Debug "
                    + "delegate. Detection only — nothing is killed. "
                    + (r.FailureReason ?? "")),
                r.CreatedAt,
                null,
                actions);
        }).ToList();
    }

    // ---- CARD-0292: queued input that never converted --------------------------------------------

    /// <summary>
    /// Projects <see cref="AttentionKind.QueuedInputStuck"/> from open
    /// <see cref="AgentIncidentKind.QueuedInputNeverConverted"/> incidents, so the row appears in
    /// the feed at Warning — not only via the recent-critical sweep when channel-bound. "Open" is
    /// re-verified at read time with the sweep's own closure predicate (any conversion or drain
    /// past the episode's enqueue closes it) plus a live-session check, so the row exists because
    /// the condition holds now — the ProgressStalled discipline.
    /// </summary>
    private async Task<List<AttentionItemDto>> BuildQueuedInputStuckItemsAsync(
        DateTime since, CancellationToken ct)
    {
        var rows = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.QueuedInputNeverConverted
                && i.CreatedAt >= since
                && i.SessionId != null)
            .Select(i => new { i.AgentId, i.SessionId, i.Severity, i.Message, i.CreatedAt, i.FailureReason })
            .ToListAsync(ct);
        if (rows.Count == 0)
            return [];

        // One row per episode — the ladder re-raises the same episode at a higher severity, and
        // the feed wants the latest word, not the history.
        var episodes = rows
            .GroupBy(r => (r.SessionId!.Value, r.FailureReason))
            .Select(g => g.OrderByDescending(r => r.CreatedAt).First())
            .ToList();

        var sessionIds = episodes.Select(e => e.SessionId!.Value).Distinct().ToList();
        var liveSessions = (await _db.AgentSessions.AsNoTracking()
                .Where(s => sessionIds.Contains(s.Id)
                    && (s.Status == SessionStatus.Starting
                        || s.Status == SessionStatus.Running
                        || s.Status == SessionStatus.Stopping))
                .Select(s => s.Id)
                .ToListAsync(ct))
            .ToHashSet();

        var agentIds = episodes.Where(r => r.AgentId is not null).Select(r => r.AgentId!.Value).Distinct().ToList();
        var agentNames = agentIds.Count == 0
            ? new Dictionary<Guid, string>()
            : await _db.Agents.AsNoTracking()
                .Where(a => agentIds.Contains(a.Id))
                .Select(a => new { a.Id, a.Name })
                .ToDictionaryAsync(a => a.Id, a => a.Name, ct);

        var items = new List<AttentionItemDto>();
        foreach (var episode in episodes)
        {
            var sessionId = episode.SessionId!.Value;
            if (!liveSessions.Contains(sessionId))
                continue;
            if (!QueuedInputWatchdogService.TryParseEpisodeKey(episode.FailureReason, out var enqueueSeq))
                continue;
            if (await QueuedInputWatchdogService.IsEpisodeClosedAsync(_db, sessionId, enqueueSeq, ct))
                continue;

            var shortSession = DelegationReportFormatter.Short(sessionId);
            var title = episode.AgentId is Guid agentId && agentNames.TryGetValue(agentId, out var name)
                ? name
                : $"Session {shortSession}";
            var actions = episode.AgentId is null
                ? new[] { AttentionAction.OpenDrawer }
                : new[] { AttentionAction.OpenAgent, AttentionAction.OpenDrawer };
            items.Add(new AttentionItemDto(
                AttentionKind.QueuedInputStuck,
                episode.Severity,
                null,
                sessionId,
                episode.AgentId,
                null,
                title,
                episode.Message,
                Excerpt(
                    "Input was accepted by the TUI and never became a prompt — the swallowed-input "
                    + "shape a blocking modal produces. Detection only: nothing is killed or typed. "
                    + (episode.FailureReason ?? "")),
                episode.CreatedAt,
                null,
                actions));
        }

        return items;
    }

    // ---- CARD-0239: standing agent that outlived its task ---------------------------------------

    /// <summary>
    /// Warning-only, self-clearing projection of a non-AlwaysOn agent that finished (or lost)
    /// its one job and nothing retired it. Detection only — nothing here stops an agent.
    /// </summary>
    private async Task<List<AttentionItemDto>> BuildModelAvailabilityHoldItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var holds = await _db.ModelAvailabilityHolds.AsNoTracking()
            .Where(h => h.ClearedAt == null && (h.DisabledUntil == null || h.DisabledUntil > now))
            .OrderBy(h => h.HitAt)
            .ToListAsync(ct);
        if (holds.Count == 0)
            return [];

        var heldKeys = holds
            .Select(h => (h.Kind, h.ModelAlias))
            .ToHashSet();
        var kindWide = holds
            .Where(h => h.ModelAlias == ModelAlias.KindWide)
            .Select(h => h.Kind)
            .ToHashSet();
        var available = ModelAlias.DelegatableAliases
            .Where(a => !kindWide.Contains(a.Kind) && !heldKeys.Contains((a.Kind, a.Alias)))
            .Select(a => a.Alias)
            .ToList();
        var availableSentence = available.Count == 0 ? "(none)" : string.Join(", ", available);

        var freshCutoff = now - TimeSpan.FromMinutes(180);
        var samples = await _db.SubscriptionUsageSamples.AsNoTracking()
            .Where(s => s.ParseStatus == SubscriptionUsageParseStatus.Parsed
                && s.RemainingPercent != null
                && s.ObservedAt >= freshCutoff)
            .ToListAsync(ct);
        var latestByKind = samples
            .GroupBy(s => s.Provider)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(s => s.ObservedAt).First());

        var items = new List<AttentionItemDto>(holds.Count);
        foreach (var hold in holds)
        {
            var alias = hold.ModelAlias;
            var name = alias == ModelAlias.KindWide ? hold.Kind.ToString() : alias;
            string headline;
            if (hold.Source == ModelAvailabilitySource.Manual)
            {
                headline = hold.DisabledUntil is { } manualUntil
                    ? $"{name} held until {manualUntil:yyyy-MM-ddTHH:mm:ssZ} (manual); dispatch paused for {name}"
                    : $"{name} held (manual, until cleared); dispatch paused for {name}; available: {availableSentence}";
            }
            else if (hold.DisabledUntil is { } until)
            {
                var remaining = until - now;
                if (hold.Reason.Contains("no reset stated", StringComparison.Ordinal))
                {
                    headline =
                        $"{alias} exhausted — provider gave no reset; fallback retry {until:yyyy-MM-ddTHH:mm:ssZ} (in {Duration(remaining)}); "
                        + $"dispatch paused for {alias}";
                }
                else
                {
                    var zone = ExtractZone(hold.Reason) ?? "UTC";
                    var local = until.ToString("HH:mm");
                    if (hold.Reason.Contains("resets ", StringComparison.Ordinal)
                        && hold.Reason.Length >= 24)
                    {
                        // Reason is "session-limit resets HH:mm Zone".
                        var resetsAt = hold.Reason.IndexOf("resets ", StringComparison.Ordinal);
                        if (resetsAt >= 0)
                        {
                            var rest = hold.Reason[(resetsAt + "resets ".Length)..];
                            var parts = rest.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 1)
                                local = parts[0];
                            if (parts.Length >= 2)
                                zone = parts[1];
                        }
                    }

                    headline =
                        $"{alias} exhausted — resets {local} {zone} (in {Duration(remaining)}); "
                        + $"dispatch paused for {alias}";
                }
            }
            else
            {
                headline =
                    $"{alias} exhausted (no reset stated); dispatch paused for {alias}; "
                    + $"available: {availableSentence}";
            }

            var evidenceBits = new List<string>();
            if (!string.IsNullOrWhiteSpace(hold.RawText))
                evidenceBits.Add(Excerpt(hold.RawText));
            if (hold.SourceSessionId is { } session)
                evidenceBits.Add($"source session {session:D}");
            if (hold.DisabledUntil is { } untilEvidence)
                evidenceBits.Add($"disabled until {untilEvidence:u}");
            else
                evidenceBits.Add("disabled until cleared");
            if (latestByKind.TryGetValue(hold.Kind, out var sample) && sample.RemainingPercent is { } pct)
                evidenceBits.Add($"latest {hold.Kind} sample {pct:0.#}% remaining (observed {sample.ObservedAt:u})");

            items.Add(new AttentionItemDto(
                AttentionKind.ModelAvailabilityHold,
                AlertSeverity.Error,
                hold.SourceTaskId,
                hold.SourceSessionId,
                null,
                null,
                $"{hold.Kind} {alias}",
                headline,
                string.Join("\n", evidenceBits),
                hold.HitAt,
                null,
                [AttentionAction.ClearHold],
                ModelKind: hold.Kind.ToString(),
                ModelAlias: alias));
        }

        return items;
    }

    /// <summary>
    /// CARD-0057 S5: a schedule whose last fire skipped, refused, failed, or is stuck at Claimed.
    /// Warning. Detection only. <c>SkippedLate</c> is a fire row, not this.
    /// </summary>
    private async Task<List<AttentionItemDto>> BuildScheduleMisfireItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var stuckAfter = TimeSpan.FromSeconds(Math.Max(1, _schedules.SweepSeconds) * 12);
        var stuckBefore = now - stuckAfter;
        var misfireOutcomes = new[]
        {
            ScheduleFireOutcome.SkippedNoSession,
            ScheduleFireOutcome.SkippedTargetGone,
            ScheduleFireOutcome.Refused,
            ScheduleFireOutcome.Failed,
        };

        var rows = await _db.Schedules.AsNoTracking()
            .Include(s => s.Agent)
            .Include(s => s.Card)
            .Where(s => s.Enabled
                && ((s.LastOutcome != null && misfireOutcomes.Contains(s.LastOutcome.Value))
                    || _db.ScheduleFires.Any(f =>
                        f.ScheduleId == s.Id
                        && f.Outcome == ScheduleFireOutcome.Claimed
                        && f.CompletedAt == null
                        && f.ClaimedAt <= stuckBefore)))
            .ToListAsync(ct);

        var items = new List<AttentionItemDto>(rows.Count);
        foreach (var schedule in rows)
        {
            var stuck = await _db.ScheduleFires.AsNoTracking()
                .Where(f => f.ScheduleId == schedule.Id
                    && f.Outcome == ScheduleFireOutcome.Claimed
                    && f.CompletedAt == null
                    && f.ClaimedAt <= stuckBefore)
                .OrderByDescending(f => f.FireNumber)
                .FirstOrDefaultAsync(ct);

            var outcome = stuck is not null
                ? ScheduleFireOutcome.Claimed
                : schedule.LastOutcome ?? ScheduleFireOutcome.Failed;
            var headline = stuck is not null
                ? $"{schedule.Name}: fire #{stuck.FireNumber} stuck at Claimed"
                : $"{schedule.Name}: {outcome}";
            var evidence = stuck is not null
                ? $"claimed {stuck.ClaimedAt:u}; the worker has not completed this fire"
                : schedule.LastOutcomeDetail ?? outcome.ToString();

            var actions = new List<AttentionAction>();
            if (schedule.AgentId is not null)
                actions.Add(AttentionAction.OpenAgent);
            if (schedule.CardId is not null)
                actions.Add(AttentionAction.OpenCard);

            items.Add(new AttentionItemDto(
                AttentionKind.ScheduleMisfired,
                AlertSeverity.Warning,
                null,
                null,
                schedule.AgentId,
                null,
                schedule.Name,
                headline,
                evidence,
                stuck?.ClaimedAt ?? schedule.LastFiredAt ?? schedule.UpdatedAt,
                null,
                actions,
                CardId: schedule.CardId,
                BoardId: schedule.Card?.BoardId));
        }

        return items;
    }

    private static string? ExtractZone(string reason)
    {
        var idx = reason.LastIndexOf(' ');
        return idx < 0 ? null : reason[(idx + 1)..];
    }

    private async Task<List<AttentionItemDto>> BuildAgentOutlivedTaskItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var candidates = await _db.Agents.AsNoTracking()
            .Where(a => !a.AlwaysOn
                && !a.IsPoolDelegate
                && (a.Status == AgentStatus.Running
                    || a.Status == AgentStatus.Idle
                    || a.Status == AgentStatus.Stopped
                    || a.Status == AgentStatus.Failed
                    || a.Status == AgentStatus.Disconnected)
                && !_db.AgentTasks.Any(t => t.AgentId == a.Id
                    && (t.Status == AgentTaskStatus.Queued
                        || t.Status == AgentTaskStatus.Dispatched
                        || t.Status == AgentTaskStatus.Working
                        || t.Status == AgentTaskStatus.Blocked))
                && !_db.ChatChannels.Any(ch => ch.AgentId == a.Id))
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Status,
                a.WorkingDirectory,
                a.BoardId,
                a.PersistentSessionId,
                a.CreatedAt,
                a.UpdatedAt,
            })
            .ToListAsync(ct);
        if (candidates.Count == 0)
            return [];

        var projects = await _db.Projects.AsNoTracking()
            .Select(p => new { p.Id, p.LocalRepositoryPath })
            .ToListAsync(ct);
        var liveCardProjectIds = (await _db.Cards.AsNoTracking()
                .Where(c => c.ArchivedAt == null)
                .Select(c => c.Board.ProjectId)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();
        var liveCardProjectPaths = projects
            .Where(p => liveCardProjectIds.Contains(p.Id) && !string.IsNullOrWhiteSpace(p.LocalRepositoryPath))
            .Select(p => p.LocalRepositoryPath!)
            .ToList();

        candidates = candidates
            .Where(a => liveCardProjectPaths.TrueForAll(path => !AgentService.PathsMatch(a.WorkingDirectory, path)))
            .ToList();
        if (candidates.Count == 0)
            return [];

        var items = new List<AttentionItemDto>();

        // Arm 1 — live idle. Unparseable / missing / not-Running sessions skip silently:
        // DeadSession and SessionDisagreement own latch-vs-reality drift.
        var liveIdle = candidates.Where(a => a.Status == AgentStatus.Running).ToList();
        var parsedSessions = new List<(Guid AgentId, string AgentName, string WorkingDirectory,
            AgentStatus Status, Guid SessionId)>();
        foreach (var agent in liveIdle)
        {
            if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                continue;
            parsedSessions.Add((agent.Id, agent.Name, agent.WorkingDirectory, agent.Status, sessionId));
        }

        if (parsedSessions.Count > 0)
        {
            var sessionIds = parsedSessions.Select(s => s.SessionId).Distinct().ToList();
            var runningSessions = (await _db.AgentSessions.AsNoTracking()
                    .Where(s => sessionIds.Contains(s.Id) && s.Status == SessionStatus.Running)
                    .Select(s => s.Id)
                    .ToListAsync(ct))
                .ToHashSet();
            var live = parsedSessions.Where(s => runningSessions.Contains(s.SessionId)).ToList();
            var liveIds = live.Select(s => s.SessionId).Distinct().ToList();
            var working = liveIds.Count == 0
                ? new Dictionary<Guid, bool>()
                : (await SessionMessageQueueService.IsWorkingBatchAsync(_db, liveIds, ct))
                    .ToDictionary(kv => kv.Key, kv => kv.Value);

            var idle = live.Where(s => !working.GetValueOrDefault(s.SessionId)).ToList();
            var idleIds = idle.Select(s => s.SessionId).Distinct().ToList();
            var newestBySession = idleIds.Count == 0
                ? new Dictionary<Guid, DateTime>()
                : (await _db.TranscriptEntries.AsNoTracking()
                        .Where(e => idleIds.Contains(e.AgentSessionId))
                        .GroupBy(e => e.AgentSessionId)
                        .Select(g => new { SessionId = g.Key, Newest = g.Max(e => e.Timestamp ?? e.CreatedAt) })
                        .ToListAsync(ct))
                    .ToDictionary(x => x.SessionId, x => x.Newest);

            var idleCutoff = now - AgentLiveIdleThreshold;
            foreach (var agent in idle)
            {
                // Zero transcript rows is CardlessDetailsNoPrompt / NeverStarted territory.
                if (!newestBySession.TryGetValue(agent.SessionId, out var newest))
                    continue;
                if (newest > idleCutoff)
                    continue;

                var age = now - newest;
                items.Add(new AttentionItemDto(
                    AttentionKind.AgentOutlivedTask,
                    AlertSeverity.Warning,
                    null,
                    agent.SessionId,
                    agent.AgentId,
                    null,
                    agent.AgentName,
                    $"Standing agent idle {Duration(age)} with no task.",
                    Evidence(
                        $"Status {agent.Status}, cwd {agent.WorkingDirectory}. "
                        + $"Last transcript {newest:u}. No open task, not AlwaysOn, not channel-bound. "
                        + "Nothing will stop it automatically; stop the agent once its work is "
                        + "confirmed done, or give it a task.",
                        check: null),
                    newest,
                    null,
                    [AttentionAction.OpenAgent]));
            }
        }

        // Arm 2 — leftover identity. The status latch IS the "no live session" fact;
        // SessionDisagreement owns drift.
        var leftoverCutoff = now - AgentLeftoverThreshold;
        var leftover = candidates
            .Where(a => (a.Status is AgentStatus.Idle or AgentStatus.Stopped
                    or AgentStatus.Failed or AgentStatus.Disconnected)
                && a.UpdatedAt <= leftoverCutoff)
            .ToList();

        if (leftover.Count > 0)
        {
            var boardIds = leftover
                .Where(a => a.BoardId is not null)
                .Select(a => a.BoardId!.Value)
                .Distinct()
                .ToList();
            var boards = boardIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _db.Boards.AsNoTracking()
                    .Where(b => boardIds.Contains(b.Id))
                    .ToDictionaryAsync(b => b.Id, b => b.Name, ct);
            var boardsWithCards = boardIds.Count == 0
                ? new HashSet<Guid>()
                : (await _db.Cards.AsNoTracking()
                        .Where(c => boardIds.Contains(c.BoardId) && c.ArchivedAt == null)
                        .Select(c => c.BoardId)
                        .Distinct()
                        .ToListAsync(ct))
                    .ToHashSet();
            var agentsOnBoard = boardIds.Count == 0
                ? new Dictionary<Guid, int>()
                : (await _db.Agents.AsNoTracking()
                        .Where(a => a.BoardId != null && boardIds.Contains(a.BoardId.Value))
                        .GroupBy(a => a.BoardId!.Value)
                        .Select(g => new { BoardId = g.Key, Count = g.Count() })
                        .ToListAsync(ct))
                    .ToDictionary(x => x.BoardId, x => x.Count);

            foreach (var agent in leftover)
            {
                var worktree = IsWorktreeCwd(agent.WorkingDirectory);
                string? boardShape = null;
                if (agent.BoardId is Guid boardId
                    && boards.TryGetValue(boardId, out var boardName)
                    && !boardsWithCards.Contains(boardId)
                    && agentsOnBoard.GetValueOrDefault(boardId) == 1
                    && BoardNameMatchesOneOff(boardName, agent.Name, agent.WorkingDirectory))
                {
                    boardShape = boardName;
                }

                if (!worktree && boardShape is null)
                    continue;

                var age = now - agent.UpdatedAt;
                var days = Math.Max(1, (int)age.TotalDays);
                var shape = worktree
                    ? $"worktree path '{agent.WorkingDirectory}'"
                    : $"sole agent on empty board '{boardShape}'";
                items.Add(new AttentionItemDto(
                    AttentionKind.AgentOutlivedTask,
                    AlertSeverity.Warning,
                    null,
                    null,
                    agent.Id,
                    null,
                    agent.Name,
                    $"Left-over one-off agent: {agent.Status} for {days} days with no task and no cards.",
                    Evidence(
                        $"Matched {shape}. No open task, not AlwaysOn, not channel-bound. "
                        + "Nothing will stop it automatically; delete it once its work is confirmed "
                        + "done, or keep it if it is still wanted.",
                        check: null),
                    agent.UpdatedAt,
                    null,
                    [AttentionAction.OpenAgent]));
            }
        }

        return items;
    }

    private static bool IsWorktreeCwd(string? workingDirectory)
    {
        if (string.IsNullOrWhiteSpace(workingDirectory))
            return false;
        var normalized = DelegationWorkspaceResolver.NormalizeSeparators(workingDirectory);
        var parts = normalized.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        return parts.Any(p => p.Equals(".worktrees", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Minted one-off boards are named <c>DeriveProjectName(cwd, agentName)</c>, then
    /// <c>UniqueBoardNameAsync</c> may suffix <c> (2)</c>, <c> (3)</c>, … . StartsWith covers
    /// the suffix; the space-then-paren check stops "foo" matching "foobar".
    /// </summary>
    private static bool BoardNameMatchesOneOff(string boardName, string agentName, string workingDirectory)
    {
        var derived = AgentService.DeriveProjectName(workingDirectory, agentName);
        return NameMatchesMintedBoard(boardName, derived) || NameMatchesMintedBoard(boardName, agentName);
    }

    private static bool NameMatchesMintedBoard(string boardName, string expected)
    {
        if (string.IsNullOrWhiteSpace(expected))
            return false;
        if (string.Equals(boardName, expected, StringComparison.OrdinalIgnoreCase))
            return true;
        return boardName.StartsWith(expected, StringComparison.OrdinalIgnoreCase)
            && boardName.Length > expected.Length
            && boardName[expected.Length] == ' '
            && boardName.AsSpan(expected.Length).Contains('(');
    }

    // ---- the collapsed context group: recent failures --------------------------------------------

    private static List<AttentionItemDto> BuildRecentFailureItems(
        IReadOnlyList<AgentTask> failed,
        IReadOnlyDictionary<Guid, decimal> costs,
        IReadOnlyDictionary<Guid, CheckExplanation> checkDigests) =>
        failed.Select(task => new AttentionItemDto(
            AttentionKind.RecentFailure,
            AlertSeverity.Warning,
            task.Id,
            task.AgentSessionId,
            task.AgentId,
            null,
            task.Title,
            "Failed in the last 24h.",
            Evidence(task.FailureReason ?? "No failure reason was recorded.", checkDigests.GetValueOrDefault(task.Id)),
            task.CompletedAt,
            costs.GetValueOrDefault(task.Id),
            [AttentionAction.Retry, AttentionAction.OpenDrawer])).ToList();

    // ---- condition 8: the runner and the database disagree ---------------------------------------

    /// <summary>
    /// The one condition that is not a query (CARD-0035 slice 2): what the session runner says it is
    /// running, diffed against what the database believes.
    ///
    /// <para><b>Why this duplicates <c>SessionReconciliationService</c> on purpose.</b> That service
    /// is the thing that FIXES a disagreement — it re-adopts, it retries a kill. This one only
    /// reports, read-only, and it is the safety net that stays honest when reconciliation is broken,
    /// disabled, or itself the bug. A diagnostic view whose correctness depends on the machinery it
    /// exists to diagnose is not a safety net. Nothing here reaches into that service.</para>
    ///
    /// <para><b>Neither arm implies a kill.</b> The 2026-08-16 miss (CARD-0056) was a perfectly
    /// healthy session — the operator's own working conversation — marked Failed by a launch path
    /// that leaked what it started. A pass that resolved that mismatch by killing would have killed
    /// somebody mid-sentence, so the verb is offered to a human behind a confirm and the row says
    /// plainly that the live process may be the side that is right.</para>
    /// </summary>
    private async Task<List<AttentionItemDto>> BuildSessionDisagreementItemsAsync(
        IReadOnlyList<SessionRunnerSessionDto> runnerSessions,
        IReadOnlySet<Guid> deadSessions,
        CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();

        var live = runnerSessions
            .Where(s => string.Equals(s.Status, RunnerRunningStatus, StringComparison.OrdinalIgnoreCase))
            .GroupBy(s => s.SessionId)
            .Select(g => g.First())
            .ToList();
        if (live.Count == 0)
            return items;

        var ids = live.Select(s => s.SessionId).ToList();
        var rows = await _db.AgentSessions.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .Select(s => new { s.Id, s.Status, s.Cwd, s.DefinitionName, s.EndedAt, s.FailureReason })
            .ToListAsync(ct);
        var byId = rows.ToDictionary(r => r.Id);
        var owners = await ResolveSessionOwnersAsync(ids, ct);

        foreach (var runner in live)
        {
            var owner = owners.GetValueOrDefault(runner.SessionId);
            var shortId = runner.SessionId.ToString("N")[..8];
            var started = Utc(runner.StartedAt);
            var where = runner.HostPid is { } host
                ? $"pid {runner.Pid?.ToString() ?? "?"} in pty-host {host}"
                : $"pid {runner.Pid?.ToString() ?? "?"}";

            // Arm 2 — the runner is running something the database has never heard of. Unclaimed is
            // SUSPECT, not broken: it is usually somebody's work that a server restart lost track of,
            // which is exactly why it is a row a human reads rather than a thing anything reclaims.
            if (!byId.TryGetValue(runner.SessionId, out var row))
            {
                items.Add(new AttentionItemDto(
                    AttentionKind.SessionDisagreement,
                    AlertSeverity.Warning,
                    null,
                    runner.SessionId,
                    owner?.AgentId,
                    null,
                    owner?.AgentName ?? $"Unclaimed session {shortId}",
                    $"The runner is running session {shortId} and the database has no row for it.",
                    Excerpt(
                        $"Running since {started:u} as {where}"
                        + (runner.Adopted ? ", adopted from a previous runner." : ".")
                        + " Nothing in the database claims this session, so no supervisor, check-in or"
                        + " queue flush will ever reach it — but it may still be somebody's live work."
                        + " Read it before killing it."),
                    started,
                    null,
                    owner?.AgentId is null
                        ? [AttentionAction.KillSession]
                        : [AttentionAction.OpenAgent, AttentionAction.KillSession]));
                continue;
            }

            // Arm 1 — the database has written this session off while the runner still has it. This
            // is the shape that silently disables check-ins (a Failed parent row makes the dispatcher
            // stop delivering to it), so it is Error: something IS broken, and it is the DB row.
            if (!IsSettled(row.Status))
                continue;

            var downstream = deadSessions.Contains(runner.SessionId)
                ? " Any task listed above as DeadSession on this session is downstream of this one"
                  + " fact — those tasks are not dead, and retrying them would start a second agent"
                  + " alongside the one already running."
                : string.Empty;

            items.Add(new AttentionItemDto(
                AttentionKind.SessionDisagreement,
                AlertSeverity.Error,
                null,
                runner.SessionId,
                owner?.AgentId,
                null,
                owner?.AgentName ?? row.DefinitionName ?? $"Session {shortId}",
                $"The database says {row.Status} but the runner is still running session {shortId}.",
                Excerpt(
                    $"Running since {started:u} as {where}, in {row.Cwd}."
                    + downstream
                    + " The live process is evidence the database row is wrong; reconciliation"
                    + " re-adopts this shape, so open it before killing anything."
                    + (string.IsNullOrWhiteSpace(row.FailureReason)
                        ? string.Empty
                        : $" The row's recorded reason: {row.FailureReason}")),
                row.EndedAt ?? started,
                null,
                [AttentionAction.OpenAgent, AttentionAction.KillSession]));
        }

        return items;
    }

    /// <summary>
    /// The runner's own list, or null when it could not answer. Null is the only way this projection
    /// says "nobody asked" — it must never be confused with an empty list, which is the far stronger
    /// claim that nothing disagrees.
    /// </summary>
    private async Task<IReadOnlyList<SessionRunnerSessionDto>?> TryListRunnerSessionsAsync(CancellationToken ct)
    {
        try
        {
            return await _runnerClient.ListAsync(ct);
        }
        // An HttpClient timeout arrives as a TaskCanceledException with NOTHING cancelled, so the
        // token has to be consulted before an OCE may be treated as shutdown.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                ex, "The session runner did not answer the attention sweep; runner-derived conditions "
                + "are omitted from this response");
            return null;
        }
    }

    /// <summary>Runner clocks are UTC; a round-trip that lost the marker must not shift the row.</summary>
    private static DateTime Utc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    // ---- shared lookups --------------------------------------------------------------------------

    /// <summary>Whose session this is, as far as the database can say.</summary>
    private sealed record SessionOwner(Guid? AgentId, string? AgentName);

    /// <summary>
    /// Session → owning agent, by BOTH routes, because two kinds of session exist: a standing agent
    /// owns its session through <c>PersistentSessionId</c> (a string column, hence the "D" keys), and
    /// a delegate's session is named only by the task that dispatched it. Neither route is queried
    /// through <c>AgentSessions</c>, so this answers for a runner session with no session row at all
    /// — which is precisely the case <see cref="AttentionKind.SessionDisagreement"/>'s second arm is.
    /// </summary>
    private async Task<Dictionary<Guid, SessionOwner>> ResolveSessionOwnersAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        var owners = new Dictionary<Guid, SessionOwner>();
        if (sessionIds.Count == 0)
            return owners;

        var ids = sessionIds.Distinct().ToList();
        var keys = ids.Select(id => id.ToString("D")).ToList();

        var standing = await _db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null && keys.Contains(a.PersistentSessionId))
            .Select(a => new { a.Id, a.Name, a.PersistentSessionId })
            .ToListAsync(ct);
        foreach (var agent in standing)
        {
            if (Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                owners.TryAdd(sessionId, new SessionOwner(agent.Id, agent.Name));
        }

        var delegates = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId != null
                && ids.Contains(t.AgentSessionId!.Value)
                && t.AgentId != null)
            .Select(t => new { SessionId = t.AgentSessionId!.Value, AgentId = t.AgentId!.Value, t.AgentName })
            .ToListAsync(ct);
        foreach (var group in delegates.GroupBy(t => t.SessionId))
        {
            // The standing agent wins: it OWNS the session, where a task merely ran in one.
            var first = group.First();
            owners.TryAdd(group.Key, new SessionOwner(first.AgentId, first.AgentName));
        }

        return owners;
    }

    /// <summary>
    /// Rolled-up spend per listed task. Reuses <see cref="AgentTaskService.IsDescendantOf"/> rather
    /// than re-deriving the walk — two answers to "what has this run cost" would eventually differ,
    /// and the board's figure is the one an operator has already learned to read.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadSubtreeCostsAsync(
        IReadOnlyList<AgentTask> subjects, CancellationToken ct)
    {
        if (subjects.Count == 0)
            return [];

        var rootIds = subjects.Select(t => t.RootTaskId).Distinct().ToList();
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => rootIds.Contains(t.RootTaskId))
            .ToListAsync(ct);
        var byRoot = family.GroupBy(t => t.RootTaskId).ToDictionary(g => g.Key, g => (IReadOnlyList<AgentTask>)[.. g]);

        return subjects.GroupBy(t => t.RootTaskId)
            .SelectMany(g => AgentTaskCostWalk.Calculate([.. g], byRoot.GetValueOrDefault(g.Key) ?? []))
            .ToDictionary(p => p.Key, p => p.Value);
    }

    /// <summary>
    /// What the last check on each task said — the interpreter's own reading when one is stored
    /// (CARD-0035 slice 5), otherwise the tail of the digest.
    ///
    /// <para>The reading WINS whenever it exists, and that is the whole point of slice 5: the digest
    /// tail is deterministic and always present, but it is six lines of <c>commits=3 changed=1</c>
    /// that a human still has to interpret, while the specialist has already done exactly that job at
    /// exactly this altitude. Before the reading was stored the best explanation the system produced
    /// was thrown away — it reached the caller's note and an uncorrelated task row, neither of which
    /// this projection can query.</para>
    ///
    /// <para>Absence is not a degradation to report: a check that ran before slice 5, or one whose
    /// interpreter was busy, simply falls back to the same digest tail v1 always showed.</para>
    /// </summary>
    private async Task<Dictionary<Guid, CheckExplanation>> LoadLatestCheckDigestsAsync(
        IReadOnlyList<AgentTask> subjects, CancellationToken ct)
    {
        var explanations = new Dictionary<Guid, CheckExplanation>();
        if (subjects.Count == 0)
            return explanations;

        var ids = subjects.Select(t => t.Id).Distinct().ToList();
        var events = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => ids.Contains(e.AgentTaskId) && e.Type == AgentTaskEventType.Check)
            .Select(e => new { e.AgentTaskId, e.Detail, e.At })
            .ToListAsync(ct);

        foreach (var group in events.GroupBy(e => e.AgentTaskId))
        {
            var latest = group.OrderByDescending(e => e.At).First();

            if (AgentTaskCheckService.TryReadInterpretation(latest.Detail) is { } reading)
            {
                explanations[group.Key] = new CheckExplanation(Excerpt(reading), FromInterpreter: true);
                continue;
            }

            var tail = Tail(latest.Detail, CheckDigestTailLines);
            if (!string.IsNullOrWhiteSpace(tail))
                explanations[group.Key] = new CheckExplanation(tail, FromInterpreter: false);
        }

        return explanations;
    }

    /// <summary>
    /// The last check's explanation and where it came from. The provenance is carried rather than
    /// inferred because the two read completely differently: one is a specialist's judgement and the
    /// other is raw counters, and a row that labelled counters as a reading would be claiming
    /// somebody looked when nobody did.
    /// </summary>
    private sealed record CheckExplanation(string Text, bool FromInterpreter);

    // ---- text ------------------------------------------------------------------------------------

    private static string Evidence(string primary, CheckExplanation? check)
    {
        var head = Excerpt(primary);
        if (check is null || string.IsNullOrWhiteSpace(check.Text))
            return head;

        var label = check.FromInterpreter ? "The last check read it as:" : "Last check:";
        return string.IsNullOrWhiteSpace(head)
            ? $"{label}\n{check.Text}"
            : $"{head}\n\n{label}\n{check.Text}";
    }

    private static string Excerpt(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var flat = text.ReplaceLineEndings("\n").Trim();
        return flat.Length <= EvidenceChars ? flat : flat[..EvidenceChars] + "…";
    }

    private static string Tail(string? text, int lines)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var all = text.ReplaceLineEndings("\n")
            .Split('\n')
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .ToList();
        var kept = all.Count <= lines ? all : all.Skip(all.Count - lines).ToList();
        return Excerpt(string.Join("\n", kept));
    }

    private static string Duration(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h{span.Minutes:00}m"
            : $"{(int)span.TotalMinutes}m";
    }
}
