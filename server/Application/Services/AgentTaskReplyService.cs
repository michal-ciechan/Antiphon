using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Turns a delegate's finished turn into a task result and a note delivered to its parent.
///
/// Correlation matches the <c>[antiphon-task:id]</c> MARKER carried in the brief, not the prompt
/// text. Prompt-text matching is right for chat (where a human's stray turn should be ignored) but
/// wrong here: a delegate that reformulates, or a human typing in the delegate's terminal, would
/// otherwise end the task with the wrong text.
///
/// Singleton — invoked from the singleton <see cref="AgentSessionRuntime"/> transcript observer,
/// with a DI scope per operation, mirroring the channel dispatcher's pattern.
/// </summary>
public sealed class AgentTaskReplyService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskReplyService> _logger;
    private readonly PtyDeliveryProfile? _ptyProfile;

    public AgentTaskReplyService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AgentTaskReplyService> logger,
        PtyDeliveryProfile? ptyProfile = null)
    {
        _ptyProfile = ptyProfile;
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// How much report is forwarded whole, from the pseudoconsole that will carry it (CARD-0037):
    /// the note is TYPED into the caller's terminal, so its ceiling belongs to the pty, not to
    /// taste. No profile — every test that predates one — means the conservative inbox number.
    /// </summary>
    private int ReplyInlineMaxChars =>
        _ptyProfile?.Ceilings.ReplyInlineMaxChars ?? _settings.ReplyInlineMaxChars;

    /// <summary>
    /// A session finished a turn. If that session is running a delegated task and the turn was the
    /// one we asked for, settle the task and deliver its report.
    /// </summary>
    public async Task OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var task = await db.AgentTasks
                .FirstOrDefaultAsync(t => t.AgentSessionId == sessionId
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working), ct);
            if (task is null)
                return;

            var turn = await ExtractMarkedTurnAsync(db, sessionId, task.Id, ct);
            if (turn.Report is not string report)
            {
                if (turn.UncorrelatedReport)
                {
                    // A finished-looking turn we cannot attribute. Left at Debug this printed
                    // nothing under an Information file sink, and three delegates that had done
                    // their work sat Dispatched overnight with no record of why (CARD-0003).
                    _logger.LogWarning(
                        "Session {SessionId} ended a turn WITH a report but the prompt carried no "
                        + "marker for task {ShortId} — not settling it. Either a human typed here, or "
                        + "the brief's marker did not survive delivery and this task will strand.",
                        sessionId, DelegationReportFormatter.Short(task.Id));
                    await RecordUncorrelatedReportAsync(scope.ServiceProvider, db, task, sessionId, ct);
                    return;
                }

                if (turn.DeferredForFinalMessage)
                {
                    // CARD-0046: this TurnEnd is the thinking record of a response whose text is
                    // still in flight. Waiting is the whole fix — the text's own arrival re-triggers
                    // us, and the dispatcher sweeps the grace if it never comes.
                    _logger.LogDebug(
                        "Session {SessionId} ended a turn for task {ShortId} but its own response has "
                        + "not written text yet; deferring settlement",
                        sessionId, DelegationReportFormatter.Short(task.Id));
                    return;
                }

                if (turn.FinalMessageMissing)
                {
                    // Slice 3 makes this an incident and a caller-visible warning; until then the
                    // log is the only record that a task settled without the verdict it was owed.
                    _logger.LogWarning(
                        "Session {SessionId}: the turn-ending response for task {ShortId} never wrote "
                        + "any text within the {Grace}s grace — nothing to settle on",
                        sessionId, DelegationReportFormatter.Short(task.Id),
                        _settings.FinalMessageGraceSeconds);
                    return;
                }

                // The delegate hasn't produced text yet — Claude can write the stop marker before
                // its reply, and the AssistantText arrival re-triggers us. Leave the task running.
                _logger.LogDebug(
                    "Session {SessionId} ended a turn with no report for task {ShortId}; still working",
                    sessionId, DelegationReportFormatter.Short(task.Id));
                return;
            }

            if (turn.FinalMessageMissing)
            {
                // Same as above, but there WAS other text: the report about to be stored is
                // whatever the turn produced — most likely preamble, not a verdict.
                _logger.LogWarning(
                    "Session {SessionId}: the turn-ending response for task {ShortId} never wrote its "
                    + "own text within the {Grace}s grace; settling on {Chars:N0} characters of "
                    + "mid-turn text instead",
                    sessionId, DelegationReportFormatter.Short(task.Id),
                    _settings.FinalMessageGraceSeconds, report.Length);
            }

            await SettleAsync(scope.ServiceProvider, db, task, report, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to settle a delegated task for session {SessionId}", sessionId);
        }
    }

    /// <summary>
    /// Put an uncorrelated report on the agent's incident timeline, ONCE per session. A log line is
    /// the diagnostic; an incident is what the board and the alert pipeline can actually see, and
    /// this failure's whole character is that every surface said the task was fine. Once per
    /// session because a stranded delegate keeps ending turns, and the same finding repeated on
    /// every one of them is noise that buries the first.
    /// </summary>
    private async Task RecordUncorrelatedReportAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, Guid sessionId, CancellationToken ct)
    {
        if (task.AgentId is not Guid agentId)
            return;

        try
        {
            var already = await db.AgentIncidents.AnyAsync(
                i => i.SessionId == sessionId && i.Kind == AgentIncidentKind.DelegateReportUncorrelated, ct);
            if (already)
                return;

            var message =
                $"Task {DelegationReportFormatter.Short(task.Id)} could not be settled from this "
                + "session's finished turn: the prompt it answered does not carry the task marker. "
                + "If the delegate has in fact reported, its brief was mangled in delivery and the "
                + "task will sit Dispatched until the delivery watchdog fails it.";

            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = sessionId,
                Kind = AgentIncidentKind.DelegateReportUncorrelated,
                Severity = AlertSeverity.Warning,
                Message = message,
                CreatedAt = UtcNow(),
            });
            await db.SaveChangesAsync(ct);

            // The timeline row is the record; the alert is what actually reaches someone. Optional
            // so the reply path keeps working in hosts that wire no alerting.
            if (services.GetService<IAlertService>() is { } alerts)
            {
                await alerts.RaiseAsync(
                    new AlertRaise(
                        AlertSeverity.Warning,
                        Source: "delegation",
                        Title: $"Delegate report could not be correlated to task {DelegationReportFormatter.Short(task.Id)}",
                        Detail: message,
                        DedupKey: $"delegation:uncorrelated:{task.Id}",
                        AgentId: agentId,
                        SessionId: sessionId),
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Observability must never be able to break settlement.
            _logger.LogError(
                ex, "Could not record an uncorrelated-report incident for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
        }
    }

    /// <summary>Answer a Blocked delegate's question and let it resume.</summary>
    public async Task<AgentTaskSummaryDto> AnswerAsync(Guid taskId, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ValidationException(nameof(message), "A reply message is required.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();

        var task = await db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId);
        if (task.Status != AgentTaskStatus.Blocked)
            throw new ConflictException($"Task {DelegationReportFormatter.Short(taskId)} is not waiting for an answer.");
        if (task.AgentSessionId is not Guid sessionId)
            throw new ConflictException("The delegate's session is no longer available.");

        var now = UtcNow();
        task.Status = AgentTaskStatus.Working;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(taskId, AgentTaskEventType.Replied, "Caller answered the delegate's question.", now));
        await db.SaveChangesAsync(ct);

        // The marker rides the answer so the delegate's NEXT turn correlates back to this task.
        var body = $"{DelegationReportFormatter.TaskMarker(taskId)}\n\n{message.Trim()}";
        await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);

        await PublishAsync(task, ct);
        var family = await db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
        return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetSummaryAsync(task, family);
    }

    private async Task SettleAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, string report, CancellationToken ct)
    {
        var now = UtcNow();
        task.Result = report;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        task.Status = LooksLikeAQuestion(report) ? AgentTaskStatus.Blocked : AgentTaskStatus.Succeeded;

        // Roll the delegate's token spend up onto the task so the board and the per-root ceiling
        // see it. The four counters stay SEPARATE all the way to the price: collapsing them and
        // applying the input rate to the total is what made a $2.47 task read as $31.29
        // (CARD-0023). Cost is derived from the tier, since Claude Code reports no dollars per turn.
        if (task.AgentSessionId is Guid sessionId)
        {
            // Bounded to THIS task's window: a warm pool delegate's session outlives its first
            // task, and charging its tokens to the next one would double-count them against the
            // per-root ceiling.
            var spend = await DelegationUsageRollup.ForSessionAsync(db, sessionId, task.DispatchedAt, now, ct);
            task.TokensIn = spend.InputTokens;
            task.CacheReadTokens = spend.CacheReadTokens;
            task.CacheCreationTokens = spend.CacheCreationTokens;
            task.TokensOut = spend.OutputTokens;
            task.CostUsd = DelegationCost.Estimate(_settings.Pricing, task.ModelLevel, spend, now);
            task.CostPricingVersion = DelegationCost.PricingVersion;
        }

        // The delegate was told to spill a long report to a file. Note it if it did — and if it
        // ignored the instruction, write the file ourselves so the excerpt has somewhere to point.
        task.ResultFilePath = await ResolveSpillFileAsync(task, report, ct);

        db.AgentTaskEvents.Add(NewEvent(
            task.Id,
            task.Status == AgentTaskStatus.Blocked ? AgentTaskEventType.Blocked : AgentTaskEventType.Completed,
            task.Status == AgentTaskStatus.Blocked
                ? "Delegate asked a question."
                : $"Delegate reported {report.Length:N0} characters.",
            now));

        // The work landed; now the BRANCH has to. Only a genuinely finished Worktree task merges —
        // a question-Blocked one keeps its worktree and session alive to continue.
        string? workspaceNote = null;
        if (task.Status == AgentTaskStatus.Succeeded && task.Workspace == WorkspaceMode.Worktree)
            workspaceNote = await MergeBackAsync(services, db, task, now, ct);

        // A finished Merge task is what un-blocks the conflicted task it was spawned for.
        if (task.Status == AgentTaskStatus.Succeeded && task.Role == AgentTaskRole.Merge)
            await ResolveConflictedParentAsync(services, db, task, now, ct);

        // A settled delegate is not spent — it is WARM. A Shared task's agent goes into the pool
        // for follow-ups and unrelated work in its directory; only worktree delegates retire on
        // the spot (their directory dies with the merge). Blocked tasks keep everything — the
        // session is how the conversation continues.
        if (task.Status == AgentTaskStatus.Succeeded)
            await ReleaseDelegateAsync(services, db, task, now, ct);

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Task {ShortId} settled as {Status} ({Chars:N0} chars, ${Cost:0.000})",
            DelegationReportFormatter.Short(task.Id), task.Status, report.Length, task.CostUsd);

        await DeliverToParentAsync(task, report, ct, workspaceNote);
        await PublishAsync(task, ct);
    }

    /// <summary>
    /// Land a succeeded Worktree task's branch on its target. On conflict the task flips to
    /// Blocked and a Merge-role delegate is spawned with the conflict list — never an automatic
    /// resolution. Returns the one-phrase outcome for the completion note's header.
    /// </summary>
    private async Task<string?> MergeBackAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, DateTime now, CancellationToken ct)
    {
        DelegationWorktreeService.MergeOutcome outcome;
        try
        {
            outcome = await services.GetRequiredService<DelegationWorktreeService>()
                .TryMergeBackAsync(task, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            outcome = new DelegationWorktreeService.MergeOutcome(
                DelegationWorktreeService.MergeResult.Failed, [], ex.Message);
        }

        switch (outcome.Result)
        {
            case DelegationWorktreeService.MergeResult.Merged:
                db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Merged, $"Merged: {outcome.Detail}", now));
                return $"merged → {task.MergeTargetRef}";

            case DelegationWorktreeService.MergeResult.NothingToMerge:
                db.AgentTaskEvents.Add(NewEvent(
                    task.Id, AgentTaskEventType.Merged, "No changes beyond the target — worktree removed.", now));
                return "no changes";

            case DelegationWorktreeService.MergeResult.LeftForHuman:
                db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Completed, outcome.Detail!, now));
                return $"branch {task.WorktreeBranch} left for review";

            case DelegationWorktreeService.MergeResult.Conflicted:
                // The report said "done", but done work that cannot land is not done — Blocked
                // until the Merge delegate integrates it.
                task.Status = AgentTaskStatus.Blocked;
                task.FailureReason =
                    $"Rebase onto {task.MergeTargetRef} conflicted in {outcome.ConflictFiles.Count} file(s).";
                db.AgentTaskEvents.Add(NewEvent(
                    task.Id, AgentTaskEventType.Conflicted,
                    $"Conflicts: {string.Join(", ", outcome.ConflictFiles)}", now));

                var merge = await services.GetRequiredService<AgentTaskService>()
                    .CreateMergeTaskAsync(task, outcome.ConflictFiles, ct);
                return merge is null
                    ? $"MERGE CONFLICT in {string.Join(", ", outcome.ConflictFiles)} — run at task cap, resolve by hand"
                    : $"merge conflict → task {DelegationReportFormatter.Short(merge.Id)} is resolving it";

            default:
                db.AgentTaskEvents.Add(NewEvent(
                    task.Id, AgentTaskEventType.Failed, $"Merge-back failed: {outcome.Detail}", now));
                return $"NOT merged ({outcome.Detail}) — branch {task.WorktreeBranch} kept";
        }
    }

    /// <summary>
    /// A Merge delegate finishing means its conflicted parent's work has finally landed — flip that
    /// parent from Blocked to Succeeded and release its session, or the row (and its delegate)
    /// dangle forever on a conflict that no longer exists.
    /// </summary>
    private async Task ResolveConflictedParentAsync(
        IServiceProvider services, AppDbContext db, AgentTask merge, DateTime now, CancellationToken ct)
    {
        if (merge.ParentTaskId is not Guid parentId)
            return;

        var conflicted = await db.AgentTasks.FirstOrDefaultAsync(
            t => t.Id == parentId
                && t.Status == AgentTaskStatus.Blocked
                && t.Workspace == WorkspaceMode.Worktree, ct);
        if (conflicted is null)
            return;

        conflicted.Status = AgentTaskStatus.Succeeded;
        conflicted.FailureReason = null;
        conflicted.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(
            conflicted.Id, AgentTaskEventType.Merged,
            $"Conflict resolved by merge task {DelegationReportFormatter.Short(merge.Id)}.", now));
        await ReleaseDelegateAsync(services, db, conflicted, now, ct);
        await PublishAsync(conflicted, ct);
    }

    /// <summary>
    /// What happens to the delegate when its task settles: a Shared pool delegate with a live
    /// session goes WARM — reserved for its own run for a window, then open to any work in its
    /// directory, until the pool janitor retires it. Everything else pool-spawned (worktree
    /// delegates, dead sessions) retires now. A user's standing agent is never touched.
    /// </summary>
    private async Task ReleaseDelegateAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, DateTime now, CancellationToken ct)
    {
        if (task.AgentId is not Guid agentId)
            return;

        var agent = await db.Agents.FirstOrDefaultAsync(a => a.Id == agentId, ct);
        if (agent is null || !agent.IsPoolDelegate)
            return;

        var sessionAlive = task.AgentSessionId is Guid sid
            && await db.AgentSessions.AsNoTracking().AnyAsync(
                s => s.Id == sid
                    && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running), ct);

        if (_settings.PoolEnabled && task.Workspace == WorkspaceMode.Shared && sessionAlive)
        {
            agent.Status = AgentStatus.Idle;
            agent.PoolIdleSince = now;
            // Reserved for ITS run first: the caller that just used it can send follow-up work to
            // the same context without racing the rest of the queue for the agent.
            agent.PoolReservedForRootTaskId = task.RootTaskId;
            agent.UpdatedAt = now;
            _logger.LogInformation(
                "Delegate '{Name}' pooled warm in {Dir} (reserved for run {Root} first)",
                agent.Name, agent.WorkingDirectory, DelegationReportFormatter.Short(task.RootTaskId));
            return;
        }

        if (task.AgentSessionId is Guid sessionId)
        {
            try
            {
                await services.GetRequiredService<IDelegateSessionStopper>().KillAsync(sessionId, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "Could not stop finished delegate session {SessionId}", sessionId);
            }
        }

        db.Agents.Remove(agent);
    }

    /// <summary>
    /// Deliver the completion note into the parent's session — WhenIdle, so it lands between the
    /// parent's turns rather than interrupting one.
    /// </summary>
    private async Task DeliverToParentAsync(
        AgentTask task, string report, CancellationToken ct, string? workspaceNote = null)
    {
        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return;

        var note = DelegationReportFormatter.BuildCompletionNote(
            task, _settings, report, workspaceNote, ReplyInlineMaxChars);
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();

            // ConversationKey batches a contiguous run of same-root completions into ONE delivery,
            // so five delegates landing together produce one note, not five turns. The queue's
            // size-aware batching stops before the combined body crosses the inline ceiling.
            await queue.EnqueueAsync(
                parentSession, note.Body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A dead parent session must not lose the result — it is already persisted on the task.
            _logger.LogWarning(
                ex, "Could not deliver task {ShortId} report to parent session {SessionId}",
                DelegationReportFormatter.Short(task.Id), parentSession);
        }
    }

    /// <summary>
    /// What a finished turn was: our task's report, nothing yet, or — the dangerous one — a real
    /// report we cannot attribute because the prompt it answered carries no marker.
    /// </summary>
    /// <param name="Report">The report text, non-null only when the turn correlated.</param>
    /// <param name="UncorrelatedReport">
    /// The turn produced assistant text but failed the marker gate. Either a human typed in this
    /// delegate's terminal (benign, and what the gate is for), or the marker did not survive
    /// delivery and a finished task is about to strand (the 2026-08-11 miss).
    /// </param>
    /// <param name="DeferredForFinalMessage">
    /// The turn-ending response has not written its own text yet (CARD-0046). Not a verdict about
    /// the task at all — come back when the text lands, or when the grace expires.
    /// </param>
    /// <param name="FinalMessageMissing">
    /// The grace expired and the turn-ending response never wrote text, so this report was built
    /// from whatever else the turn produced — most likely mid-turn narration, not a verdict.
    /// </param>
    private readonly record struct TurnOutcome(
        string? Report,
        bool UncorrelatedReport,
        bool DeferredForFinalMessage = false,
        bool FinalMessageMissing = false)
    {
        public static readonly TurnOutcome Nothing = new(null, false);
        public static readonly TurnOutcome Deferred = new(null, false, DeferredForFinalMessage: true);
    }

    /// <summary>
    /// The turn's assistant text, but only if the turn was the one we asked for — its prompt must
    /// carry this task's marker AND the response that ended the turn must have written its own text.
    /// </summary>
    private async Task<TurnOutcome> ExtractMarkedTurnAsync(
        AppDbContext db, Guid sessionId, Guid taskId, CancellationToken ct)
    {
        // The ROW, not just its sequence: settling correctly needs the turn-ending response's
        // identity (ApiCallId) and when we actually stored it (CreatedAt) — see FinalMessageLandedAsync.
        var end = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (end is null)
            return TurnOutcome.Nothing;
        var turnEnd = end.Sequence;

        var prompt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence < turnEnd)
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (prompt?.Text is not string promptText)
            return TurnOutcome.Nothing;

        var nextPrompt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && t.Sequence > prompt.Sequence)
            .MinAsync(t => (long?)t.Sequence, ct);

        var query = db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.Sequence > prompt.Sequence);
        if (nextPrompt is long cap)
            query = query.Where(t => t.Sequence < cap);

        var texts = await query.OrderBy(t => t.Sequence).Select(t => t.Text).ToListAsync(ct);
        var joined = string.Join("\n\n", texts.Where(t => !string.IsNullOrWhiteSpace(t))).Trim();

        // The marker gate. A human typing in this terminal produces a prompt without it — but so
        // does a brief whose marker was eaten in transit, and those two look identical from here.
        // Distinguishing them is not possible; SAYING SO is, which is the whole point of the flag.
        if (!promptText.Contains(DelegationReportFormatter.TaskMarker(taskId), StringComparison.Ordinal))
            return new TurnOutcome(null, joined.Length > 0);

        // CARD-0046. The turn is ours; is this response FINISHED speaking? One API response is
        // written as several JSONL records — a signature-only thinking record, then the text record
        // — and every one carries the response's stop_reason, so the boundary that reaches us first
        // is a bare TurnEnd with the report still milliseconds away. Settling here hands the caller
        // the mid-turn narration and discards the verdict (six delegates, 2026-08-13/14).
        switch (await FinalMessageStateAsync(db, sessionId, end, ct))
        {
            case FinalMessageState.Landed:
                break;

            case FinalMessageState.Pending:
                // The text record's own arrival re-triggers us (AgentSessionRuntime :219 → :350),
                // and AgentTaskDispatcher.SettleDeferredReportsAsync sweeps the grace.
                return TurnOutcome.Deferred;

            case FinalMessageState.NeverArrived:
                // Past the grace: a response that ends the turn with no text at all is real (1 in
                // 180 measured — a lone thinking record followed by "API Error: Connection lost
                // mid-response"). Settle on what there is rather than strand the task, and say so.
                return joined.Length == 0
                    ? new TurnOutcome(null, false, FinalMessageMissing: true)
                    : new TurnOutcome(joined, false, FinalMessageMissing: true);
        }

        return joined.Length == 0 ? TurnOutcome.Nothing : new TurnOutcome(joined, false);
    }

    private enum FinalMessageState
    {
        /// <summary>The turn-ending response's own text is persisted (or there is no id to check).</summary>
        Landed = 0,

        /// <summary>Not yet, and still inside the grace window.</summary>
        Pending = 1,

        /// <summary>The grace expired; this response is never going to write text.</summary>
        NeverArrived = 2,
    }

    /// <summary>
    /// Whether the response that ended the turn has written its own text, decided by IDENTITY: the
    /// thinking record and the text record of one API response share a <c>message.id</c>, stored as
    /// <see cref="TranscriptEntry.ApiCallId"/>. Exact where a debounce would be a guess.
    ///
    /// <para>Two deliberate escapes. A TurnEnd with NO ApiCallId is the legacy/synthetic path (a
    /// SessionRestartBoundary, an older row, a fake that emits no message.id) — there is nothing to
    /// wait for and it behaves exactly as it always did. And the grace is measured from
    /// <see cref="TranscriptEntry.CreatedAt"/>, never the record's own <c>Timestamp</c>: the
    /// thinking record's timestamp is BACKDATED to when its block finished, by 1-30 s against a
    /// measured persist gap of 0.01-1.17 s, so a Timestamp-based window would expire before the
    /// text it is waiting for could possibly arrive.</para>
    /// </summary>
    private async Task<FinalMessageState> FinalMessageStateAsync(
        AppDbContext db, Guid sessionId, TranscriptEntry end, CancellationToken ct)
    {
        if (_settings.FinalMessageGraceSeconds <= 0)
            return FinalMessageState.Landed; // escape hatch: never defer
        if (end.ApiCallId is not string apiCallId)
            return FinalMessageState.Landed;

        var landed = await db.TranscriptEntries.AnyAsync(
            t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.ApiCallId == apiCallId, ct);
        if (landed)
            return FinalMessageState.Landed;

        var waited = UtcNow() - end.CreatedAt;
        return waited < TimeSpan.FromSeconds(_settings.FinalMessageGraceSeconds)
            ? FinalMessageState.Pending
            : FinalMessageState.NeverArrived;
    }

    /// <summary>
    /// Where the full detail lives when a report is too big to forward. The delegate was told to
    /// write one; if it did and the path exists, use it. Otherwise the server writes it, so the
    /// head+tail excerpt always has something real to point at.
    /// </summary>
    private async Task<string?> ResolveSpillFileAsync(AgentTask task, string report, CancellationToken ct)
    {
        if (report.Length <= ReplyInlineMaxChars)
            return null;

        var relative = Path.Combine(".antiphon", $"task-{DelegationReportFormatter.Short(task.Id)}.md");
        var absolute = Path.Combine(task.WorkingDirectory, relative);
        try
        {
            if (File.Exists(absolute))
                return absolute;

            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            await File.WriteAllTextAsync(absolute, report, ct);
            _logger.LogInformation(
                "Task {ShortId} reported {Chars:N0} chars without spilling; wrote {Path} as the backstop",
                DelegationReportFormatter.Short(task.Id), report.Length, absolute);
            return absolute;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A read-only or vanished workspace must not lose the result — it is on the task row.
            _logger.LogWarning(ex, "Could not write the spill file for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
            return null;
        }
    }

    /// <summary>
    /// A delegate that ends its turn asking something needs an ANSWER, not a retry. Deliberately
    /// conservative: only a question mark in the last couple of lines counts, so a report that
    /// merely mentions a question mid-text still reads as finished.
    /// </summary>
    internal static bool LooksLikeAQuestion(string report)
    {
        var lines = report.ReplaceLineEndings("\n")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToList();
        if (lines.Count == 0)
            return false;

        return lines.TakeLast(2).Any(l => l.EndsWith('?'));
    }

    private async Task PublishAsync(AgentTask task, CancellationToken ct) =>
        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);

    private static AgentTaskEvent NewEvent(Guid taskId, AgentTaskEventType type, string detail, DateTime at) =>
        new()
        {
            Id = Guid.NewGuid(),
            AgentTaskId = taskId,
            Type = type,
            Detail = detail.Length <= 4000 ? detail : detail[..4000],
            At = at,
        };

    private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;
}
