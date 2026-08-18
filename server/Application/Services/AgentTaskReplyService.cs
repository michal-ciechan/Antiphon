using Antiphon.Agents.Pty;
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

            var turn = await ExtractMarkedTurnAsync(db, sessionId, task, ct);

            if (turn.ApiErrorStub is { } stub)
            {
                // CARD-0071 S3: the marked turn was killed by the API itself. The error text is NOT
                // a report — two limit-killed delegates settled Succeeded with "You've hit your
                // usage limit…" stored as their Result on 2026-08-17, and every surface said the
                // work was done. Checked before every other verdict about the turn: nothing further
                // is coming from a dead turn, so there is nothing to defer on or settle with.
                await FailApiErrorTurnAsync(scope.ServiceProvider, db, task, sessionId, stub, ct);
                return;
            }

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
                    // Nothing to settle on AT ALL: the response that ended the turn wrote no text
                    // and neither did the rest of the turn. Measured 1 in 180 — a lone end_turn
                    // thinking record followed by "API Error: Connection lost mid-response". A
                    // Succeeded with an empty report would be a lie, and leaving it Dispatched hides
                    // it until the 10-minute watchdog; failing says what happened (CARD-0046 slice 3).
                    await FailUnreportedTurnAsync(scope.ServiceProvider, db, task, sessionId, ct);
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
                // whatever the turn produced — most likely preamble, not a verdict. SettleAsync
                // carries this all the way to the caller's note (CARD-0046 slice 3).
                _logger.LogWarning(
                    "Session {SessionId}: the turn-ending response for task {ShortId} never wrote its "
                    + "own text within the {Grace}s grace; settling on {Chars:N0} characters of "
                    + "mid-turn text instead",
                    sessionId, DelegationReportFormatter.Short(task.Id),
                    _settings.FinalMessageGraceSeconds, report.Length);
            }

            await SettleAsync(scope.ServiceProvider, db, task, report, turn, ct);
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

    /// <summary>
    /// Tell a delegate something while it is STILL WORKING (CARD-0062): a constraint remembered, a
    /// failure already diagnosed elsewhere, "skip slice 3". The alternative was cancel-and-redispatch,
    /// throwing away everything done so far. Rides the same queue as a reply — WhenIdle, so it lands
    /// between turns and can never corrupt work in progress, and delivery is transcript-confirmed
    /// (CARD-0055) rather than assumed.
    ///
    /// <para>Deliberately NOT a state change: the task stays Dispatched/Working and only a
    /// <see cref="AgentTaskEventType.Refined"/> event records what the delegate was told and when.
    /// A still-Queued task has no session to speak to, so its refinement amends the BRIEF instead —
    /// the goal is what the dispatcher types at dispatch, so the amendment rides the brief itself.
    /// A Blocked task is redirected to the reply verb (a refinement would not unblock it), and a
    /// settled one is refused: nothing may correlate to a settled task and reopen it. If the task
    /// settles AFTER the enqueue, the queued note still cannot reopen anything —
    /// <see cref="OnTurnEndAsync"/> only settles tasks that are Dispatched or Working.</para>
    /// </summary>
    public async Task<AgentTaskSummaryDto> RefineAsync(Guid taskId, string message, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ValidationException(nameof(message), "A refinement message is required.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var task = await db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId);

        var now = UtcNow();
        var trimmed = message.Trim();

        switch (task.Status)
        {
            case AgentTaskStatus.Queued:
                // Nothing is running yet, so there is nobody to message — fold the refinement into
                // the goal, which is what BuildBrief types verbatim at dispatch.
                task.Goal = $"{task.Goal.TrimEnd()}\n\nREFINEMENT (added by the caller before dispatch):\n{trimmed}";
                task.ConcurrencyToken = Guid.NewGuid();
                db.AgentTaskEvents.Add(NewEvent(
                    taskId, AgentTaskEventType.Refined,
                    $"Caller refined the brief before dispatch: {trimmed}", now));
                await db.SaveChangesAsync(ct);
                break;

            case AgentTaskStatus.Dispatched:
            case AgentTaskStatus.Working:
                if (task.AgentSessionId is not Guid sessionId)
                    throw new ConflictException("The delegate's session is no longer available.");

                // The event is saved BEFORE the enqueue: if delivery fails the timeline still shows
                // what the caller tried to say, which is the record a diverging report is judged by.
                db.AgentTaskEvents.Add(NewEvent(
                    taskId, AgentTaskEventType.Refined,
                    $"Caller refined the running task: {trimmed}", now));
                await db.SaveChangesAsync(ct);

                var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();
                // Whose composer this is typed into decides whether it may be typed at all
                // (CARD-0084 S1) — a Grok session joins every line, so its refinement spills.
                var agentKind = await db.AgentSessions.AsNoTracking()
                    .Where(s => s.Id == sessionId)
                    .Select(s => (AgentKind?)s.AgentKind)
                    .FirstOrDefaultAsync(ct) ?? AgentKind.ClaudeCode;
                var body = FitRefinementForTyping(task, trimmed, now, agentKind);
                await queue.EnqueueAsync(sessionId, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);
                break;

            case AgentTaskStatus.Blocked:
                throw new ConflictException(
                    $"Task {DelegationReportFormatter.Short(taskId)} is waiting for an ANSWER — "
                    + "reply to its question instead (the reply verb), so it resumes.");

            default:
                throw new ConflictException(
                    $"Task {DelegationReportFormatter.Short(taskId)} has already settled "
                    + $"({task.Status}) — there is nothing left to refine.");
        }

        await PublishAsync(task, ct);
        var family = await db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
        return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetSummaryAsync(task, family);
    }

    /// <summary>
    /// The refinement as it will actually be typed — the same spill-or-inline gate a brief gets
    /// (<see cref="AgentTaskDispatcher.FitBriefForTyping"/>), against the same ceiling: a refinement
    /// is an instruction, not a deliverable, so <c>BriefInlineMaxBytes</c> is the number that
    /// governs it. Above the ceiling the full text goes to a timestamped file (never overwriting an
    /// earlier refinement's) and a pointer is typed instead; if the file cannot be written the
    /// pointer names the task's event timeline, where the head of the text is on record.
    /// </summary>
    /// <param name="agentKind">
    /// See <see cref="AgentTaskDispatcher.FitBriefForTyping"/> — a kind whose composer joins the
    /// lines we type has an inline ceiling of 0, so its refinement always travels as a file plus a
    /// join-proof pointer (CARD-0084 S1).
    /// </param>
    private string FitRefinementForTyping(AgentTask task, string message, DateTime now, AgentKind agentKind)
    {
        var ceilings = (_ptyProfile?.Ceilings
            ?? _settings.CeilingsFor(PtyBackend.InboxConhost, "no pty profile — assuming the default backend"))
            .ForAgentKind(agentKind);
        var body = DelegationReportFormatter.BuildRefinement(task, message);
        if (System.Text.Encoding.UTF8.GetByteCount(body) <= ceilings.BriefInlineMaxBytes)
            return body;

        string? spillPath = null;
        try
        {
            var absolute = Path.Combine(
                task.WorkingDirectory,
                ".antiphon",
                $"task-{DelegationReportFormatter.Short(task.Id)}-refinement-{now:yyyyMMddHHmmss}.md");
            Directory.CreateDirectory(Path.GetDirectoryName(absolute)!);
            File.WriteAllText(absolute, body);
            spillPath = absolute;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(
                ex, "Task {ShortId}: could not write the refinement spill file; pointing at the API instead",
                DelegationReportFormatter.Short(task.Id));
        }

        return DelegationReportFormatter.BuildRefinementPointer(
            task, _settings, spillPath, body.Length, agentKind);
    }

    private async Task SettleAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, string report, TurnOutcome turn,
        CancellationToken ct)
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

        // What the report was built from, on the record. The report is the turn-ending response's
        // own text, so a delegate that front-loaded findings mid-turn left some behind — name how
        // much, or that loss is invisible from every surface (CARD-0046 slice 2).
        var reported = turn.NarrationDiscardedChars > 0
            ? $"Delegate reported {report.Length:N0} characters (final message; "
              + $"{turn.NarrationDiscardedChars:N0} characters of mid-turn narration not included)."
            : $"Delegate reported {report.Length:N0} characters.";

        db.AgentTaskEvents.Add(NewEvent(
            task.Id,
            task.Status == AgentTaskStatus.Blocked ? AgentTaskEventType.Blocked : AgentTaskEventType.Completed,
            task.Status == AgentTaskStatus.Blocked ? "Delegate asked a question." : reported,
            now));

        // A settlement that could not get the final message is LOUD (CARD-0046 slice 3). Succeeded
        // is still the right status — the work happened and the text is real — but "Succeeded" on
        // its own says the caller got the verdict, and here it did not. Three surfaces, because the
        // whole character of this failure was that every surface said the task was fine: an event on
        // the task, an incident on the agent's timeline, and a line the CALLER reads above the
        // report itself.
        string? callerWarning = null;
        if (turn.FinalMessageMissing)
        {
            callerWarning = FinalMessageMissingWarning(report.Length, _settings.FinalMessageGraceSeconds);
            db.AgentTaskEvents.Add(NewEvent(
                task.Id, AgentTaskEventType.Warning,
                FinalMessageMissingDetail(report.Length, _settings.FinalMessageGraceSeconds), now));
        }

        // Same three surfaces, different fact: the turn handed work to background subagents that
        // never came back (CARD-0046 slice 4). Succeeded is still right — the delegate did what it
        // could — but this report is at best incomplete and may be the announcement of work that
        // never landed, which is not something the caller can tell from the text.
        if (turn.AbandonedSubagents > 0)
        {
            var detail = SubagentsNeverReportedDetail(turn.AbandonedSubagents, _settings.SubagentGraceMinutes);
            callerWarning = callerWarning is null ? detail : $"{callerWarning}\n\n{detail}";
            db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Warning, detail, now));
        }

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

        if (turn.FinalMessageMissing && task.AgentSessionId is Guid missingFrom)
        {
            await RecordFinalMessageMissingAsync(
                services, db, task, missingFrom,
                $"Task {DelegationReportFormatter.Short(task.Id)} settled without the delegate's final "
                + $"message. {FinalMessageMissingDetail(report.Length, _settings.FinalMessageGraceSeconds)} "
                + "The caller has been told the report may be preamble; the whole turn is in this "
                + "session's transcript.",
                ct);
        }

        if (turn.AbandonedSubagents > 0 && task.AgentSessionId is Guid abandonedFrom)
        {
            await RecordIncidentOnceAsync(
                services, db, task, abandonedFrom,
                AgentIncidentKind.DelegateSubagentsNeverReported,
                $"Delegate task {DelegationReportFormatter.Short(task.Id)} settled with background "
                + "subagents unreported",
                $"Task {DelegationReportFormatter.Short(task.Id)} settled while "
                + $"{turn.AbandonedSubagents} background subagent(s) it launched had still not "
                + $"reported after {_settings.SubagentGraceMinutes} minutes. The stored report may be "
                + "the delegate's announcement of that work rather than its outcome — the launches "
                + "and any notifications are in this session's transcript.",
                ct);
        }

        _logger.LogInformation(
            "Task {ShortId} settled as {Status} ({Chars:N0} chars, ${Cost:0.000})",
            DelegationReportFormatter.Short(task.Id), task.Status, report.Length, task.CostUsd);

        await DeliverToParentAsync(task, report, ct, workspaceNote, callerWarning);
        await PublishAsync(task, ct);
    }

    /// <summary>
    /// The turn ended and produced NO text at all — not the turn-ending response's, not anything
    /// earlier. Measured 1 in 180 responses: a lone <c>end_turn</c> thinking record followed 106 ms
    /// later by "API Error: Connection lost mid-response".
    ///
    /// <para>Failing is the correct verdict, not a fallback. Succeeded with an empty report tells the
    /// caller the work is done and hands it nothing; leaving the task Dispatched hides it until the
    /// 10-minute delivery watchdog kills it with a reason about undelivered briefs that is simply
    /// untrue here. Failed with this reason is retryable by the caller and says what happened.</para>
    ///
    /// <para>The delegate goes through the ordinary release path: its session is alive and healthy —
    /// one response died, not the agent — so a Shared delegate is pooled warm rather than killed.
    /// Skipping the release would leak the agent Busy forever, since only settlement frees it.</para>
    /// </summary>
    private async Task FailUnreportedTurnAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, Guid sessionId, CancellationToken ct)
    {
        var now = UtcNow();
        var reason =
            $"The delegate's turn ended with no report at all: the response that ended it never wrote "
            + $"any text within the {_settings.FinalMessageGraceSeconds}s grace, and neither did the "
            + "rest of the turn. This is the shape an API error mid-response leaves (CARD-0046). The "
            + $"work may well be real — read session {sessionId} before re-running this task.";

        task.Status = AgentTaskStatus.Failed;
        task.FailureReason = reason;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Failed, reason, now));

        await ReleaseDelegateAsync(services, db, task, now, ct);
        await db.SaveChangesAsync(ct);

        await RecordFinalMessageMissingAsync(
            services, db, task, sessionId,
            $"Task {DelegationReportFormatter.Short(task.Id)} failed with no report: its turn-ending "
            + $"response wrote no text within the {_settings.FinalMessageGraceSeconds}s grace and the "
            + "turn produced none either. The delegate may have done the work — the session's "
            + "transcript is the only record of it.",
            ct);

        _logger.LogWarning(
            "Task {ShortId} failed: session {SessionId} ended a turn with no text at all within the "
            + "{Grace}s grace",
            DelegationReportFormatter.Short(task.Id), sessionId, _settings.FinalMessageGraceSeconds);

        await DeliverToParentAsync(task, reason, ct);
        await PublishAsync(task, ct);
    }

    /// <summary>
    /// The marked turn was ended by an API-error stub (CARD-0071 S3, spec §D6): the API killed the
    /// turn — usage limit, 5xx, auth-expired — after Claude Code's own retry was exhausted, and the
    /// stub's error string is the only "text" the turn-ending response wrote. It is NOT a report and
    /// is never stored as <see cref="AgentTask.Result"/>; the task FAILS with a reason naming the
    /// class and the error, because visibly dead beats invisibly "Succeeded" (both 2026-08-17
    /// limit-killed delegates settled Succeeded with the limit message as their report). When S5's
    /// resume machinery lands, a scheduled resume will keep the task Working instead — this arm is
    /// the no-resume-coming half of §D6 and stays as the NeedsHuman/retries-exhausted terminal.
    ///
    /// <para>The delegate goes through the ordinary release path for the same reason
    /// <see cref="FailUnreportedTurnAsync"/> does: the SESSION is structurally healthy (idle, its
    /// own TurnEnd written) — the API refused one response, not the agent — and skipping the
    /// release would leak the agent Busy forever, since only settlement frees it.</para>
    /// </summary>
    private async Task FailApiErrorTurnAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, Guid sessionId,
        ApiErrorStubFacts stub, CancellationToken ct)
    {
        var now = UtcNow();
        var classification = ApiErrorClassifier.Classify(stub.ErrorClass, stub.ErrorStatus, stub.ErrorText);
        var errorText = string.IsNullOrWhiteSpace(stub.ErrorText)
            ? "(the stub carried no error text)"
            : stub.ErrorText.Trim();
        if (errorText.Length > 600)
            errorText = errorText[..600] + "…";

        var reason =
            $"The delegate's turn was killed by an API error ({classification}: "
            + (stub.ErrorClass ?? "no error class")
            + (stub.ErrorStatus is int status ? $", HTTP {status}" : string.Empty)
            + $") — {errorText} The error text is not a report and no report exists. The work may "
            + $"well be real — read session {sessionId} before re-running this task.";

        task.Status = AgentTaskStatus.Failed;
        task.FailureReason = reason;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Failed, reason, now));

        await ReleaseDelegateAsync(services, db, task, now, ct);
        await db.SaveChangesAsync(ct);

        // Severity is the CARD-0055/0067 rule: Critical when the agent is channel-bound (a human is
        // waiting on a line this death just went silent on) — and for NeedsHuman, where no retry
        // will ever exist and a human is the ONLY recovery.
        var channelBound = task.AgentId is Guid boundAgentId
            && await db.ChatChannels.AsNoTracking().AnyAsync(c => c.AgentId == boundAgentId, ct);
        var severity = channelBound || classification == ApiErrorClassification.NeedsHuman
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        // A dirty SHARED checkout is the human's exposure to see when deciding about the dead task
        // (spec §D6): auto-salvage is rejected — the operator's own edits live there too — so the
        // incident carries the evidence instead. Best-effort by design.
        var dirt = task.Workspace == WorkspaceMode.Shared
            ? await TryReadGitStatusShortAsync(task.WorkingDirectory, ct)
            : null;

        await RecordIncidentOnceAsync(
            services, db, task, sessionId, AgentIncidentKind.ApiErrorTurnDied,
            $"Delegate task {DelegationReportFormatter.Short(task.Id)} died on an API error ({classification})",
            $"Task {DelegationReportFormatter.Short(task.Id)} was killed by an API error, not "
            + $"finished: {classification} ({stub.ErrorClass ?? "no error class"}"
            + (stub.ErrorStatus is int s ? $", HTTP {s}" : string.Empty)
            + $"). The task is Failed and the error text was NOT stored as its result: {errorText}"
            + (string.IsNullOrWhiteSpace(dirt)
                ? string.Empty
                : $"\n\nThe shared checkout at {task.WorkingDirectory} has uncommitted changes the "
                  + $"dead task may own (git status --short):\n{dirt}"),
            ct, severity);

        _logger.LogWarning(
            "Task {ShortId} failed: session {SessionId}'s turn was killed by an API error "
            + "({Classification}: {Class}/{Status})",
            DelegationReportFormatter.Short(task.Id), sessionId, classification,
            stub.ErrorClass, stub.ErrorStatus);

        await DeliverToParentAsync(task, reason, ct);
        await PublishAsync(task, ct);
    }

    /// <summary>
    /// <c>git status --short</c> of a directory, or null when it cannot be read (not a repo, no
    /// git, timeout). Diagnostics only — a failure here must never affect settlement.
    /// </summary>
    private async Task<string?> TryReadGitStatusShortAsync(string workingDirectory, CancellationToken ct)
    {
        try
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                ArgumentList = { "status", "--short" },
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (!process.Start())
                return null;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            if (process.ExitCode != 0)
                return null;

            var trimmed = output.TrimEnd();
            if (trimmed.Length == 0)
                return null;
            return trimmed.Length <= 2000 ? trimmed : trimmed[..2000] + "\n…(truncated)";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogDebug(ex, "Could not read git status of {Dir} for the API-error incident", workingDirectory);
            return null;
        }
    }

    /// <summary>
    /// What the caller reads ABOVE the report, in the note delivered to its terminal. The report is
    /// forwarded either way — it is real text and it may well be useful — but a caller that acts on
    /// preamble as though it were a verdict is exactly what CARD-0046 cost six times over.
    /// </summary>
    private static string FinalMessageMissingWarning(int reportChars, int graceSeconds) =>
        $"WARNING: this may be PREAMBLE, not the verdict. The delegate's turn-ending response never "
        + $"wrote any text within {graceSeconds}s, so what follows is the {reportChars:N0} characters "
        + "it produced earlier in the same turn. Check the session transcript before acting on it.";

    private static string FinalMessageMissingDetail(int reportChars, int graceSeconds) =>
        $"The response that ended the turn never wrote its own text within {graceSeconds}s. This "
        + $"report is the {reportChars:N0} characters the turn produced BEFORE it — most likely "
        + "mid-turn narration, not the delegate's verdict.";

    /// <summary>
    /// What the caller reads above a report whose turn handed work to background subagents that
    /// never came back (CARD-0046 slice 4). "Four review agents are running in parallel" reads
    /// exactly like a finished report unless something says otherwise.
    /// </summary>
    private static string SubagentsNeverReportedDetail(int unanswered, int graceMinutes) =>
        $"WARNING: {unanswered} background subagent(s) this delegate launched never reported within "
        + $"{graceMinutes} minutes, so what follows may be its ANNOUNCEMENT of that work rather than "
        + "the outcome. Check the session transcript before acting on it.";

    /// <summary>
    /// The incident behind CARD-0046 slice 3, ONCE per session for the same reason
    /// <see cref="RecordUncorrelatedReportAsync"/> is: a delegate in this state keeps ending turns,
    /// and the same finding on every one of them buries the first.
    /// </summary>
    private Task RecordFinalMessageMissingAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, Guid sessionId, string message,
        CancellationToken ct) =>
        RecordIncidentOnceAsync(
            services, db, task, sessionId, AgentIncidentKind.DelegateFinalMessageMissing,
            $"Delegate task {DelegationReportFormatter.Short(task.Id)} settled without its final message",
            message, ct);

    /// <summary>
    /// One incident + alert per (session, kind), Warning unless overridden — the shared body behind
    /// <see cref="RecordFinalMessageMissingAsync"/>, slice 4's abandoned-subagent incident and the
    /// API-error death (whose severity varies with channel-binding and error class). The dedup is
    /// what keeps a delegate that keeps ending turns from burying its own first finding.
    /// </summary>
    private async Task RecordIncidentOnceAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, Guid sessionId,
        AgentIncidentKind kind, string title, string message, CancellationToken ct,
        AlertSeverity severity = AlertSeverity.Warning)
    {
        if (task.AgentId is not Guid agentId)
            return;

        try
        {
            var already = await db.AgentIncidents.AnyAsync(
                i => i.SessionId == sessionId && i.Kind == kind, ct);
            if (already)
                return;

            db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agentId,
                SessionId = sessionId,
                Kind = kind,
                Severity = severity,
                Message = message,
                CreatedAt = UtcNow(),
            });
            await db.SaveChangesAsync(ct);

            if (services.GetService<IAlertService>() is { } alerts)
            {
                await alerts.RaiseAsync(
                    new AlertRaise(
                        severity,
                        Source: "delegation",
                        Title: title,
                        Detail: message,
                        DedupKey: $"delegation:{kind}:{task.Id}",
                        AgentId: agentId,
                        SessionId: sessionId),
                    ct);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Observability must never be able to break settlement.
            _logger.LogError(
                ex, "Could not record a {Kind} incident for task {ShortId}",
                kind, DelegationReportFormatter.Short(task.Id));
        }
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
    ///
    /// <para>Also runs when a task FAILS unreported (CARD-0046 slice 3). The judgement is about the
    /// agent, not the verdict: one response died, the session did not, so a live Shared delegate is
    /// as reusable as after any success — and skipping the release would leak it Busy forever,
    /// because settlement is the only thing that frees a delegate.</para>
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
        AgentTask task, string report, CancellationToken ct, string? workspaceNote = null,
        string? warning = null)
    {
        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return;

        var note = DelegationReportFormatter.BuildCompletionNote(
            task, _settings, report, workspaceNote, ReplyInlineMaxChars, warning);
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
    /// <param name="NarrationDiscardedChars">
    /// How much mid-turn text the report deliberately leaves out, because the report is the
    /// turn-ending response alone (CARD-0046 slice 2). Recorded on the task's Completed event so a
    /// delegate that front-loaded its findings can be seen to have done so.
    /// </param>
    /// <param name="AbandonedSubagents">
    /// How many background subagents this turn launched and never heard back from before the
    /// subagent grace expired (CARD-0046 slice 4). Non-zero means the report is at best incomplete
    /// and at worst the announcement of work that never landed.
    /// </param>
    /// <param name="ApiErrorStub">
    /// Non-null when the marked turn was ENDED by an API-error stub (CARD-0071 S3): the API killed
    /// the turn and the stub's error string is not a report. Outranks every other verdict — the
    /// task fails with a reason naming the class, and the error text is never stored as Result.
    /// </param>
    private readonly record struct TurnOutcome(
        string? Report,
        bool UncorrelatedReport,
        bool DeferredForFinalMessage = false,
        bool FinalMessageMissing = false,
        int NarrationDiscardedChars = 0,
        int AbandonedSubagents = 0,
        ApiErrorStubFacts? ApiErrorStub = null)
    {
        public static readonly TurnOutcome Nothing = new(null, false);
        public static readonly TurnOutcome Deferred = new(null, false, DeferredForFinalMessage: true);
    }

    /// <summary>
    /// What the stub itself carried (S1's three fields plus its error string) — everything the
    /// fail arm needs to classify and to name the death, straight off the turn-ending row.
    /// </summary>
    private readonly record struct ApiErrorStubFacts(string? ErrorClass, int? ErrorStatus, string? ErrorText);

    /// <summary>
    /// The turn's assistant text, but only if the turn was the one we asked for — its prompt must
    /// carry this task's marker AND the response that ended the turn must have written its own text.
    /// </summary>
    private async Task<TurnOutcome> ExtractMarkedTurnAsync(
        AppDbContext db, Guid sessionId, AgentTask task, CancellationToken ct)
    {
        var taskId = task.Id;

        // The ROW, not just its sequence: settling correctly needs the turn-ending response's
        // identity (ApiCallId) and when we actually stored it (CreatedAt) — see FinalMessageOf and
        // ResolveFinalMessageState.
        var end = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.TurnEnd)
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(ct);
        if (end is null)
            return TurnOutcome.Nothing;
        var turnEnd = end.Sequence;

        var span = await LoadPromptsInSpanAsync(db, sessionId, task.DispatchedAt, ct);
        var prompt = span.TurnPrompts.LastOrDefault(p => p.Sequence < turnEnd);
        if (prompt?.Text is not string promptText)
            return TurnOutcome.Nothing;

        var nextPrompt = span.TurnPrompts.FirstOrDefault(p => p.Sequence > prompt.Sequence)?.Sequence;

        var query = db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.AssistantText
                && t.Sequence > prompt.Sequence);
        if (nextPrompt is long cap)
            query = query.Where(t => t.Sequence < cap);

        // Each row with the API response it belongs to: the report is one of those responses, not
        // the whole turn (slice 2), and telling them apart is what ApiCallId is for.
        var rows = await query.OrderBy(t => t.Sequence)
            .Select(t => new TurnText(t.Text, t.ApiCallId, t.IsApiError))
            .ToListAsync(ct);

        // An API-error stub's error string is never report material, whatever else happens below —
        // not the final message, not the joined fallback, not an "uncorrelated report" a human gets
        // blamed for (CARD-0071 S3). Structural, never text-matched: an agent legitimately writing
        // ABOUT these errors must not trip it.
        var texts = rows
            .Where(t => !TranscriptKinds.IsApiErrorStub(TranscriptKinds.AssistantText, t.IsApiError))
            .ToList();
        var joined = Join(texts.Select(t => t.Text));

        // The marker gate. A human typing in this terminal produces a prompt without it — but so
        // does a brief whose marker was eaten in transit, and those two look identical from here.
        // Distinguishing them is not possible; SAYING SO is, which is the whole point of the flag.
        if (!promptText.Contains(DelegationReportFormatter.TaskMarker(taskId), StringComparison.Ordinal))
            return new TurnOutcome(null, joined.Length > 0);

        // CARD-0071 S3, checked the moment the turn is known to be OURS and before any other
        // verdict: a turn whose ending is an API-error stub is DEAD — the API killed it after
        // Claude Code's own retry was exhausted, no more records are coming, and its error text is
        // not a report. Deciding here (rather than in OnTurnEndAsync) keeps the whole shape of the
        // turn in one place; deferring for subagents or the final-message grace would wait on a
        // response that structurally cannot arrive.
        if (TranscriptKinds.IsApiErrorStub(end.Kind, end.IsApiError))
        {
            var stubText = Join(rows
                .Where(t => TranscriptKinds.IsApiErrorStub(TranscriptKinds.AssistantText, t.IsApiError))
                .Select(t => t.Text));
            return new TurnOutcome(
                null, false,
                ApiErrorStub: new ApiErrorStubFacts(
                    end.ApiErrorClass, end.ApiErrorStatus,
                    stubText.Length > 0 ? stubText : null));
        }

        // CARD-0046 slice 4. The turn is ours and it ended — but a turn that handed its work to
        // BACKGROUND subagents has not done the work, it has announced it. Checked before the
        // final-message gate below because the answer is the same whichever record the boundary
        // came from: this turn is not the report, so there is nothing to wait for and nothing to
        // fail on.
        // <= 0 is the documented escape hatch: no wait at all, i.e. exactly pre-slice-4 behaviour,
        // so a regression can be proved to come from here.
        var subagents = _settings.SubagentGraceMinutes <= 0
            ? new SubagentWait(0, null)
            : await ResolveSubagentWaitAsync(db, sessionId, prompt.Sequence, nextPrompt, span, ct);
        var abandonedSubagents = 0;
        if (subagents.Unanswered > 0)
        {
            if (subagents.LastEntryAt is DateTime lastAt
                && UtcNow() - lastAt < TimeSpan.FromMinutes(_settings.SubagentGraceMinutes))
            {
                // Each notification arrives as a USER record and ends a turn of its own, which
                // re-triggers us; the LAST one settles with the folded verdict (ac09cffd seq 34).
                _logger.LogDebug(
                    "Session {SessionId} ended a turn for task {ShortId} with {Count} background "
                    + "subagent(s) still unreported; deferring settlement",
                    sessionId, DelegationReportFormatter.Short(taskId), subagents.Unanswered);
                return TurnOutcome.Nothing;
            }

            // Past the grace: a background subagent can die without ever notifying, and nothing
            // else would ever come back for this task. Settle on what there is, and say so.
            abandonedSubagents = subagents.Unanswered;
        }

        // CARD-0046. The turn is ours; is this response FINISHED speaking? One API response is
        // written as several JSONL records — a signature-only thinking record, then the text record
        // — and every one carries the response's stop_reason, so the boundary that reaches us first
        // is a bare TurnEnd with the report still milliseconds away. Settling here hands the caller
        // the mid-turn narration and discards the verdict (six delegates, 2026-08-13/14).
        var finalMessage = FinalMessageOf(end, texts);

        switch (ResolveFinalMessageState(end, finalMessage))
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
                    : new TurnOutcome(
                        joined, false, FinalMessageMissing: true, AbandonedSubagents: abandonedSubagents);
        }

        // THE REPORT IS THE FINAL MESSAGE, not a join of everything the delegate happened to say
        // (CARD-0046 slice 2). Joining the whole turn made the caller's report open with "I'll start
        // by reading the spec." and the head+tail excerpt excerpt the preamble; LooksLikeAQuestion
        // inspected the wrong last line for the same reason.
        //
        // The trade-off, taken deliberately: a delegate that front-loads its findings mid-turn and
        // ends with "done" loses that mid-turn text from Result — it stays in TranscriptEntries, and
        // the discarded length is named in the Completed event so the loss is never silent.
        if (finalMessage is not null)
        {
            var narration = Join(texts.Where(t => t.ApiCallId != end.ApiCallId).Select(t => t.Text));
            return new TurnOutcome(
                finalMessage, false,
                NarrationDiscardedChars: narration.Length,
                AbandonedSubagents: abandonedSubagents);
        }

        return joined.Length == 0
            ? TurnOutcome.Nothing
            : new TurnOutcome(joined, false, AbandonedSubagents: abandonedSubagents);
    }

    /// <summary>
    /// What a turn handed to background subagents, and whether they have come back. Claude Code's
    /// built-in <c>Agent</c> tool can be spawned ASYNCHRONOUSLY: the ToolResult is
    /// "<c>Async agent launched successfully</c>" within milliseconds, the delegate writes an
    /// announcement, and the turn ends for real — the work has not started. Task 26421cf2 was
    /// settled and priced on that announcement, and wrote its actual 6 195-character verdict four
    /// minutes later into a task that no longer existed (CARD-0046 §1.4).
    ///
    /// <para>Paired by ID, never by count: each <c>&lt;task-notification&gt;</c> names the
    /// <c>toolu_…</c> id of the launch it answers, so four launches and three notifications is an
    /// unambiguous "one still running" even if something else spawns an agent in the same span.
    /// A SYNCHRONOUS Agent call returns the subagent's answer as its ToolResult and carries no
    /// marker, so it is not counted — its work is already in the turn.</para>
    ///
    /// <para>Phrase drift degrades safely: if Claude Code reworded the marker, nothing would be
    /// counted as launched and settlement falls back to exactly today's behaviour (settle on the
    /// announcement turn) rather than hanging. Same exposure as the local-command shapes.</para>
    /// </summary>
    private static async Task<SubagentWait> ResolveSubagentWaitAsync(
        AppDbContext db, Guid sessionId, long promptSequence, long? cap, SpanPrompts span,
        CancellationToken ct)
    {
        // The ToolResult text is joined in SQL and never transferred — a tool result can be a whole
        // file, and there can be hundreds of them in one span.
        var launched = await (
            from call in db.TranscriptEntries
            where call.AgentSessionId == sessionId
                && call.Sequence > promptSequence
                && (cap == null || call.Sequence < cap)
                && call.Kind == TranscriptKinds.ToolCall
                && call.ToolName == TranscriptKinds.AgentToolName
                && call.ToolUseId != null
            join result in db.TranscriptEntries
                    .Where(r => r.AgentSessionId == sessionId && r.Kind == TranscriptKinds.ToolResult)
                on call.ToolUseId equals result.ToolUseId
            where result.Text != null && result.Text.Contains(TranscriptKinds.AsyncAgentLaunchMarker)
            select call.ToolUseId!)
            .Distinct()
            .ToListAsync(ct);
        if (launched.Count == 0)
            return new SubagentWait(0, null);

        // The notifications were loaded with the span's prompts — they ARE prompts, just not ones
        // that open a turn. They sit past `cap` by construction (each one starts a new turn), so
        // the cap deliberately does not apply to them.
        var notified = span.Notifications
            .Where(n => n.Sequence > promptSequence)
            .Select(n => TranscriptKinds.TryReadNotifiedToolUseId(n.Text))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        var unanswered = launched.Count(id => !notified.Contains(id));
        if (unanswered == 0)
            return new SubagentWait(0, null);

        // The grace runs from the last thing that happened on this session, not from the turn end:
        // a notification arriving resets it, which is the whole point — three of four reporting is
        // evidence the fourth is still coming.
        var lastEntryAt = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId && t.Sequence > promptSequence)
            .MaxAsync(t => (DateTime?)t.CreatedAt, ct);
        return new SubagentWait(unanswered, lastEntryAt);
    }

    /// <param name="Unanswered">Background subagents launched in the span with no notification yet.</param>
    /// <param name="LastEntryAt">
    /// When this session last wrote anything after the turn's prompt — the grace clock. Null only
    /// when nothing is unanswered, since there is then nothing to time.
    /// </param>
    private readonly record struct SubagentWait(int Unanswered, DateTime? LastEntryAt);

    /// <summary>One assistant-text row of the turn, carrying the API response it was part of.</summary>
    private readonly record struct TurnText(string? Text, string? ApiCallId, bool? IsApiError);

    /// <summary>A candidate turn-opening prompt: everything the walk-back needs to judge one.</summary>
    private sealed record PromptRow(long Sequence, string? Text, DateTime? Timestamp);

    /// <summary>
    /// The prompts a turn of THIS task could have answered, in sequence order. Two filters, and
    /// both of them killed a delegate that was still working on 2026-08-14 (tasks bcc982b7 /
    /// session c8d07c43 and fbcd6af2 / 861eaefb, each failed at the 10-minute mark as
    /// "reported but the result could not be attributed" while the delegate kept going and left
    /// finished work uncommitted).
    ///
    /// <para><b>Bounded at the task's dispatch.</b> A warm-pool delegate's session outlives its
    /// tasks, so the newest TurnEnd on it stays the PREVIOUS task's until this one ends a turn of
    /// its own — several minutes, since the reuse path opens with a <c>/compact</c> that makes room
    /// before the brief is even typed. Every AssistantText re-runs this extraction
    /// (<c>AgentSessionRuntime</c> :219 → :350), so the previous task's report was read as an
    /// unattributable report for THIS task, an incident was raised, and
    /// <c>AgentTaskDispatcher.FailNeverStartedAsync</c> acted on that incident ten minutes later.
    /// A prompt written before this task was dispatched cannot be its brief.</para>
    ///
    /// <para><b>Compaction's own records are not prompts.</b> Between the <c>/compact</c> and the
    /// brief a manual compaction writes four USER records that nobody typed as a prompt: the
    /// <c>&lt;command-name&gt;</c> wrapper, the <c>&lt;local-command-stdout&gt;</c> result, the
    /// synthetic continuation prompt, and the raw echo of the typed command line (CARD-0041). Left
    /// in, the first one of those below a turn end becomes "the prompt this turn answered", carries
    /// no marker, and fails the gate — so an agent that compacts MID-task, after its brief, strands
    /// the same way. Skipping them lets the walk-back reach the brief that is still sitting there
    /// intact (c8d07c43 seq 137).</para>
    ///
    /// <para><see cref="TranscriptEntry.Timestamp"/> is the clock, not <c>CreatedAt</c>: stored
    /// sequences are ARRIVAL-ordered and a backfill re-persists old records long after the fact
    /// (2026-08-08), while the record's own timestamp survives reordering. An entry with no
    /// timestamp cannot be placed in time and is KEPT rather than dropped, exactly as
    /// <see cref="DelegationUsageRollup"/> keeps it — the conservative direction here is to let the
    /// marker gate judge it.</para>
    ///
    /// <para><b>A background subagent's notification is not a prompt either</b> (CARD-0046 slice 4).
    /// It is kept, separately, because the launches it answers are what settlement waits for — but
    /// it must never own the span: without the skip the marker gate fails on every notification
    /// turn, <see cref="RecordUncorrelatedReportAsync"/> fires, and the delivery watchdog kills the
    /// task at ten minutes. That hazard is created by slice 4 and closed here.</para>
    /// </summary>
    private static async Task<SpanPrompts> LoadPromptsInSpanAsync(
        AppDbContext db, Guid sessionId, DateTime? dispatchedAt, CancellationToken ct)
    {
        var rows = await db.TranscriptEntries
            .Where(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.UserPrompt
                && (dispatchedAt == null || t.Timestamp == null || t.Timestamp > dispatchedAt))
            .OrderBy(t => t.Sequence)
            .Select(t => new PromptRow(t.Sequence, t.Text, t.Timestamp))
            .ToListAsync(ct);

        // The wrappers are the PROOF that a raw "/compact …" line was a command rather than a
        // prompt that happens to start with a slash — so the names come out of the span itself.
        var invoked = rows
            .Select(r => TranscriptKinds.TryReadLocalCommandName(TranscriptKinds.UserPrompt, r.Text))
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);

        return new SpanPrompts(
            rows.Where(r => !IsHousekeepingPrompt(r.Text, invoked)).ToList(),
            rows.Where(r => TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, r.Text))
                .ToList());
    }

    /// <param name="TurnPrompts">USER records a turn could actually be answering, in sequence order.</param>
    /// <param name="Notifications">
    /// The background-subagent notifications among them — skipped as turn prompts, kept because
    /// they are what proves a launch came back (CARD-0046 slice 4).
    /// </param>
    private sealed record SpanPrompts(
        IReadOnlyList<PromptRow> TurnPrompts, IReadOnlyList<PromptRow> Notifications);

    /// <summary>A USER record that no one typed as a prompt (see <see cref="LoadPromptsInSpanAsync"/>).</summary>
    private static bool IsHousekeepingPrompt(string? text, IReadOnlyCollection<string> invokedCommands) =>
        TranscriptKinds.IsLocalCommandRecord(TranscriptKinds.UserPrompt, text)
        || TranscriptKinds.IsCompactionContinuationPrompt(TranscriptKinds.UserPrompt, text)
        || TranscriptKinds.IsRawLocalCommandEcho(TranscriptKinds.UserPrompt, text, invokedCommands)
        || TranscriptKinds.IsTaskNotificationPrompt(TranscriptKinds.UserPrompt, text);

    private static string Join(IEnumerable<string?> texts) =>
        string.Join("\n\n", texts.Where(t => !string.IsNullOrWhiteSpace(t))).Trim();

    /// <summary>
    /// The text of the response that ENDED the turn — the delegate's actual final message — or null
    /// when there is no response identity to key on and the whole-turn join is all there is.
    ///
    /// <para>One response can carry several text blocks, so this is a join too; it is bounded to one
    /// <c>message.id</c> rather than to a stretch of the transcript, and sequence order is the order
    /// the blocks were written in.</para>
    /// </summary>
    private string? FinalMessageOf(TranscriptEntry end, IReadOnlyList<TurnText> texts)
    {
        // The documented escape hatch is total: at <= 0 settlement behaves exactly as it did before
        // CARD-0046, report included, so it can prove or clear a regression from this whole change.
        if (_settings.FinalMessageGraceSeconds <= 0)
            return null;
        if (end.ApiCallId is not string apiCallId)
            return null;

        var text = Join(texts.Where(t => t.ApiCallId == apiCallId).Select(t => t.Text));
        return text.Length == 0 ? null : text;
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
    ///
    /// <para>Reads the turn's OWN rows (<paramref name="finalMessage"/>) rather than querying the
    /// session again, so the text this waits for and the text it then reports are by construction
    /// the same rows.</para>
    /// </summary>
    private FinalMessageState ResolveFinalMessageState(TranscriptEntry end, string? finalMessage)
    {
        if (finalMessage is not null)
            return FinalMessageState.Landed;
        // Nothing to wait for: the escape hatch, or a boundary with no response identity on it.
        if (_settings.FinalMessageGraceSeconds <= 0 || end.ApiCallId is null)
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
