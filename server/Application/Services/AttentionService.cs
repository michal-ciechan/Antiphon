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
/// <para><b>One row per piece of work.</b> The five task-scoped conditions are evaluated in priority
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

    private readonly AppDbContext _db;
    private readonly ISessionRunnerClient _runnerClient;
    private readonly SupervisionSettings _supervision;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AttentionService> _logger;

    public AttentionService(
        AppDbContext db,
        ISessionRunnerClient runnerClient,
        IOptions<SupervisionSettings> supervision,
        TimeProvider timeProvider,
        ILogger<AttentionService> logger)
    {
        _db = db;
        _runnerClient = runnerClient;
        _supervision = supervision.Value;
        _timeProvider = timeProvider;
        _logger = logger;
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
        items.AddRange(await BuildOpenTaskItemsAsync(open, now, costs, checkDigests, attachedIncidents, ct));
        items.AddRange(await BuildParkedMessageItemsAsync(ct));
        items.AddRange(await BuildRecentIncidentItemsAsync(since, attachedIncidents, ct));
        items.AddRange(BuildRecentFailureItems(failed, costs, checkDigests));

        // Asked unconditionally, and its answer is only ever a flag in slice 1: CARD-0035 slice 2
        // diffs this list against the session rows to produce SessionDisagreement. The call is here
        // now because RunnerConsulted is a claim about whether anybody asked, and a flag that is
        // hard-coded false is not a claim at all.
        var runnerConsulted = await TryConsultRunnerAsync(ct);

        // Severity first — a row's rank IS its severity — then oldest-stuck first inside a band, so
        // the thing that has been waiting longest is the thing at the top of its group.
        var ordered = items
            .OrderByDescending(i => i.Severity)
            .ThenBy(i => i.SinceUtc ?? DateTime.MaxValue)
            .ToList();

        return new AttentionDto(now, runnerConsulted, ordered);
    }

    // ---- condition 1: a delegate asked a question ------------------------------------------------

    private async Task<List<AttentionItemDto>> BuildBlockedAsync(
        IReadOnlyList<AgentTask> blocked,
        IReadOnlyDictionary<Guid, decimal> costs,
        IReadOnlyDictionary<Guid, string> checkDigests,
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

    // ---- conditions 3-7: the open-task conditions, first match wins ------------------------------

    private async Task<List<AttentionItemDto>> BuildOpenTaskItemsAsync(
        IReadOnlyList<AgentTask> open,
        DateTime now,
        IReadOnlyDictionary<Guid, decimal> costs,
        IReadOnlyDictionary<Guid, string> checkDigests,
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
        var sessionById = sessions.ToDictionary(s => s.Id);

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

        foreach (var task in open)
        {
            var session = task.AgentSessionId is Guid sid ? sessionById.GetValueOrDefault(sid) : null;
            var elapsed = task.DispatchedAt is { } dispatched ? now - dispatched : (TimeSpan?)null;
            var digest = checkDigests.GetValueOrDefault(task.Id);
            var cost = costs.GetValueOrDefault(task.Id);

            // 3. DeadSession. A Dispatched task always gets an AgentSessionId written in the same
            // save as its status, so a null one is as broken as a Stopped row and belongs here too.
            if (task.AgentSessionId is null || session is null
                || session.Status is SessionStatus.Stopped or SessionStatus.Failed
                || session.EndedAt is not null)
            {
                var what = task.AgentSessionId is null
                    ? "the task has no session at all"
                    : session is null
                        ? "its session row is gone"
                        : session.EndedAt is not null && session.Status is not (SessionStatus.Stopped or SessionStatus.Failed)
                            ? $"its session ended at {session.EndedAt:u} while still marked {session.Status}"
                            : $"its session is {session.Status}";

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

            // 5. UncorrelatedReport — it reported, and the report could not be tied back to the task.
            var orphanReport = uncorrelated
                .Where(i => i.SessionId == task.AgentSessionId
                    && (task.DispatchedAt is null || i.CreatedAt >= task.DispatchedAt))
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

            // 6. PastExpectedIdle. THE exclusion lives here: a session that is mid-turn is not
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

            // 7. ChecksSpent — checks ran, the budget is gone, the task is still open.
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
        var sessionKeys = sessionIds.Select(id => id.ToString("D")).ToList();

        // Two routes from a session to its agent, because two kinds of session exist: a standing
        // agent owns its session through PersistentSessionId, and a delegate's session is named by
        // the task that dispatched it.
        var standing = await _db.Agents.AsNoTracking()
            .Where(a => a.PersistentSessionId != null && sessionKeys.Contains(a.PersistentSessionId))
            .Select(a => new { a.Id, a.Name, a.PersistentSessionId })
            .ToListAsync(ct);
        var agentBySessionKey = standing
            .GroupBy(a => a.PersistentSessionId!)
            .ToDictionary(g => g.Key, g => g.First());

        var delegates = await _db.AgentTasks.AsNoTracking()
            .Where(t => t.AgentSessionId != null
                && sessionIds.Contains(t.AgentSessionId!.Value)
                && t.AgentId != null)
            .Select(t => new { SessionId = t.AgentSessionId!.Value, AgentId = t.AgentId!.Value, t.AgentName })
            .ToListAsync(ct);
        var delegateBySession = delegates
            .GroupBy(t => t.SessionId)
            .ToDictionary(g => g.Key, g => g.First());

        var channelBound = (await _db.ChatChannels.AsNoTracking()
                .Where(c => c.AgentId != null)
                .Select(c => c.AgentId!.Value)
                .Distinct()
                .ToListAsync(ct))
            .ToHashSet();

        foreach (var message in parked)
        {
            var key = message.AgentSessionId.ToString("D");
            var owner = agentBySessionKey.GetValueOrDefault(key);
            var fallback = delegateBySession.GetValueOrDefault(message.AgentSessionId);
            var agentId = owner?.Id ?? fallback?.AgentId;
            var agentName = owner?.Name ?? fallback?.AgentName;

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
        IReadOnlyDictionary<Guid, string> checkDigests) =>
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

    // ---- the runner ------------------------------------------------------------------------------

    private async Task<bool> TryConsultRunnerAsync(CancellationToken ct)
    {
        try
        {
            await _runnerClient.ListAsync(ct);
            return true;
        }
        // An HttpClient timeout arrives as a TaskCanceledException with NOTHING cancelled, so the
        // token has to be consulted before an OCE may be treated as shutdown.
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(
                ex, "The session runner did not answer the attention sweep; runner-derived conditions "
                + "are omitted from this response");
            return false;
        }
    }

    // ---- shared lookups --------------------------------------------------------------------------

    /// <summary>
    /// Rolled-up spend per listed task. Reuses <see cref="AgentTaskService.IsDescendantOf"/> rather
    /// than re-deriving the walk — two answers to "what has this run cost" would eventually differ,
    /// and the board's figure is the one an operator has already learned to read.
    /// </summary>
    private async Task<Dictionary<Guid, decimal>> LoadSubtreeCostsAsync(
        IReadOnlyList<AgentTask> subjects, CancellationToken ct)
    {
        var costs = new Dictionary<Guid, decimal>();
        if (subjects.Count == 0)
            return costs;

        var rootIds = subjects.Select(t => t.RootTaskId).Distinct().ToList();
        var family = await _db.AgentTasks.AsNoTracking()
            .Where(t => rootIds.Contains(t.RootTaskId))
            .ToListAsync(ct);
        var byRoot = family.GroupBy(t => t.RootTaskId).ToDictionary(g => g.Key, g => (IReadOnlyList<AgentTask>)[.. g]);

        foreach (var task in subjects)
        {
            if (costs.ContainsKey(task.Id))
                continue;
            var run = byRoot.GetValueOrDefault(task.RootTaskId) ?? [];
            var total = task.CostUsd;
            foreach (var other in run)
            {
                if (other.Id == task.Id)
                    continue;
                if (AgentTaskService.IsDescendantOf(other, task.Id, run))
                    total += other.CostUsd;
            }
            costs[task.Id] = total;
        }

        return costs;
    }

    /// <summary>
    /// The tail of each task's most recent check digest. This is the v1 explanation column: it is
    /// deterministic, it is already stored, and it is present on every task a check has ever run on
    /// (CARD-0035 §D4 — slice 5 puts the interpreter's own reading above it at the same site).
    /// </summary>
    private async Task<Dictionary<Guid, string>> LoadLatestCheckDigestsAsync(
        IReadOnlyList<AgentTask> subjects, CancellationToken ct)
    {
        var digests = new Dictionary<Guid, string>();
        if (subjects.Count == 0)
            return digests;

        var ids = subjects.Select(t => t.Id).Distinct().ToList();
        var events = await _db.AgentTaskEvents.AsNoTracking()
            .Where(e => ids.Contains(e.AgentTaskId) && e.Type == AgentTaskEventType.Check)
            .Select(e => new { e.AgentTaskId, e.Detail, e.At })
            .ToListAsync(ct);

        foreach (var group in events.GroupBy(e => e.AgentTaskId))
        {
            var latest = group.OrderByDescending(e => e.At).First();
            var tail = Tail(latest.Detail, CheckDigestTailLines);
            if (!string.IsNullOrWhiteSpace(tail))
                digests[group.Key] = tail;
        }

        return digests;
    }

    // ---- text ------------------------------------------------------------------------------------

    private static string Evidence(string primary, string? checkDigest)
    {
        var head = Excerpt(primary);
        if (string.IsNullOrWhiteSpace(checkDigest))
            return head;
        return string.IsNullOrWhiteSpace(head)
            ? $"Last check:\n{checkDigest}"
            : $"{head}\n\nLast check:\n{checkDigest}";
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
