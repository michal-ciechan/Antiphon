using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
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
    /// The delivery watchdog's own clock, read from delegation settings so this projection and the
    /// sweep cannot disagree about when a brief is overdue for typing (CARD-0117 S5).
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
    private readonly AgentFilesService? _files;
    // CARD-0040 S3: only StaleAfterDays is read here. Optional for the same reason _files is —
    // every harness that predates this card keeps constructing this service unchanged; production
    // registers the options section, so DI always supplies it.
    private readonly CardWorkTransitionSettings _cardTransitions;

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
        AgentFilesService? files = null,
        IOptions<CardWorkTransitionSettings>? cardTransitions = null)
    {
        _db = db;
        _runnerClient = runnerClient;
        _supervision = supervision.Value;
        _delegation = delegation.Value;
        _timeProvider = timeProvider;
        _logger = logger;
        _files = files;
        _cardTransitions = cardTransitions?.Value ?? new CardWorkTransitionSettings();
    }

    public async Task<AttentionDto> GetAsync(CancellationToken ct)
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
            .Where(t => t.Status == AgentTaskStatus.Blocked)
            .ToListAsync(ct);
        var failed = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.Status == AgentTaskStatus.Failed && t.CompletedAt != null && t.CompletedAt >= since)
            .OrderByDescending(t => t.CompletedAt)
            .Take(RecentFailureCap)
            .ToListAsync(ct);

        var subjects = open.Concat(blocked).Concat(failed).ToList();
        var costs = await LoadSubtreeCostsAsync(subjects, ct);
        var checkDigests = await LoadLatestCheckDigestsAsync(subjects, ct);

        items.AddRange(await BuildBlockedAsync(blocked, costs, checkDigests, ct));
        items.AddRange(await BuildCardNeedsDecisionAsync(ct));
        items.AddRange(await BuildCardStalledAsync(now, ct));
        var openItems = await BuildOpenTaskItemsAsync(open, now, costs, checkDigests, attachedIncidents, ct);
        items.AddRange(openItems);
        items.AddRange(await BuildParkedMessageItemsAsync(ct));
        items.AddRange(await BuildRecentIncidentItemsAsync(since, attachedIncidents, ct));
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
            .Select(e => new { e.AgentTaskId, e.At })
            .ToListAsync(ct);
        var blockedAt = events
            .GroupBy(e => e.AgentTaskId)
            .ToDictionary(g => g.Key, g => g.Max(e => e.At));

        foreach (var task in blocked)
        {
            var at = blockedAt.TryGetValue(task.Id, out var stamp) ? stamp : task.DispatchedAt;

            // FailureReason is what the dispatcher and the conflict path write; the question path
            // writes the delegate's own words to Result and leaves FailureReason null. Preferring
            // the reason keeps a conflict readable, and falling through to Result is what puts the
            // actual question on the row.
            var primary = !string.IsNullOrWhiteSpace(task.FailureReason)
                ? task.FailureReason!
                : task.Result ?? "The delegate is blocked and gave no reason.";

            items.Add(new AttentionItemDto(
                AttentionKind.BlockedQuestion,
                AlertSeverity.Critical,
                task.Id,
                task.AgentSessionId,
                task.AgentId,
                null,
                task.Title,
                "Blocked — waiting on a human answer.",
                Evidence(primary, checkDigests.GetValueOrDefault(task.Id)),
                at,
                costs.GetValueOrDefault(task.Id),
                [AttentionAction.Reply, AttentionAction.Cancel, AttentionAction.Escalate]));
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
            .Select(s => new { s.Id, s.Status, s.EndedAt, s.FailureReason })
            .ToListAsync(ct);
        var sessionById = sessions.ToDictionary(
            s => s.Id,
            s => new AgentTaskLiveness.SessionSnapshot(s.Status, s.EndedAt, s.FailureReason));

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

            // 6. UncorrelatedReport — it reported, and the report could not be tied back to the task.
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

            // 7. PastExpectedIdle. THE exclusion lives here: a session that is mid-turn is not
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

            // 8. ProgressStalled (CARD-0153) — working, rows still landing, none of them novel.
            // After PastExpectedIdle (which declines the mid-turn case) and before Overdue, so a
            // working session with a stall verdict gets the more explanatory row.
            TaskProgressPolicy.WorkspaceArm? workspace = null;
            if (_files is not null && task.DispatchedAt is DateTime dispatchedAt)
            {
                workspace = await _files.ProbeProgressAsync(
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

            // 9. Overdue — closing on a deadline that will FAIL this task (CARD-0020 S2/S3). It is
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

            // 10. ChecksSpent — checks ran, the budget is gone, the task is still open.
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

    // ---- condition 9: recent Error-or-worse incidents, grouped ----------------------------------

    private async Task<List<AttentionItemDto>> BuildRecentIncidentItemsAsync(
        DateTime since, HashSet<Guid> attachedIncidents, CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();

        var recent = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.Severity >= AlertSeverity.Error && i.CreatedAt >= since)
            .Select(i => new { i.Id, i.AgentId, i.SessionId, i.Kind, i.Severity, i.Message, i.CreatedAt })
            .ToListAsync(ct);

        var fresh = recent.Where(i => !attachedIncidents.Contains(i.Id)).ToList();
        if (fresh.Count == 0)
            return items;

        var agentIds = fresh.Select(i => i.AgentId).Distinct().ToList();
        var agentNames = await _db.Agents.AsNoTracking()
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
            var name = agentNames.GetValueOrDefault(group.Key.AgentId) ?? group.Key.AgentId.ToString("N")[..8];

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
