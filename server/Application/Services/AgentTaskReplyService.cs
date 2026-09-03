using Antiphon.Agents.Pty;
using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
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
    // CARD-0320: one in-flight OnTurnEndAsync per session. Arm 0 used to re-enter while the live
    // observer was still inside SettleAsync; ConcurrencyToken is not an EF token, so both saves
    // succeeded and both delivered.
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _settleLocks = new();

    /// <summary>
    /// Test hook: runs after the open task is loaded, while the per-session settle lock is held
    /// and before any persist. Null in production.
    /// </summary>
    internal Func<Guid, CancellationToken, Task>? DelayAfterOpenTaskLoadedAsync { get; set; }

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
    /// True while <see cref="OnTurnEndAsync"/> holds this session's settle lock — including the
    /// test hook that pauses after the open task is loaded. Arm 0 uses this to skip rather than
    /// stall the 5 s dispatcher tick behind a live settle.
    /// </summary>
    internal bool IsSettleInFlight(Guid sessionId) =>
        _settleLocks.TryGetValue(sessionId, out var gate) && gate.CurrentCount == 0;

    /// <summary>
    /// A session finished a turn. If that session is running a delegated task and the turn was the
    /// one we asked for, settle the task and deliver its report.
    /// </summary>
    public async Task OnTurnEndAsync(Guid sessionId, CancellationToken ct)
    {
        var gate = _settleLocks.GetOrAdd(sessionId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(ct);
        try
        {
            await OnTurnEndLockedAsync(sessionId, ct);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task OnTurnEndLockedAsync(Guid sessionId, CancellationToken ct)
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

            if (DelayAfterOpenTaskLoadedAsync is not null)
                await DelayAfterOpenTaskLoadedAsync(sessionId, ct);

            var turn = await ExtractMarkedTurnAsync(db, sessionId, task, ct);

            if (turn.Interrupted is { } interrupted)
            {
                await RecordInterruptedTurnAsync(db, task, interrupted, ct);
                return;
            }

            if (turn.ApiErrorStub is { } stub)
            {
                // CARD-0071 S3 / CARD-0072 S5a-3: the marked turn was killed by the API itself.
                // The error text is NOT a report. A retryable class defers (task stays Working,
                // resume scheduled); NeedsHuman / exhausted / wall-parked still Fail.
                await HandleApiErrorTurnAsync(scope.ServiceProvider, db, task, sessionId, stub, ct);
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
    /// Put an uncorrelated report on the agent's incident timeline, once per task. A log line is
    /// the diagnostic; an incident is what the board and the alert pipeline can actually see, and
    /// this failure's whole character is that every surface said the task was fine. Once per task
    /// because a stranded delegate keeps ending turns, and the same finding repeated on every one
    /// of them is noise that buries the first. Scoped through
    /// <see cref="UncorrelatedReportEvidence"/> so a later task on the same session still gets its
    /// own row (CARD-0117 — the once-per-session dedup poisoned every later task on 2026-08-21).
    /// </summary>
    private async Task RecordUncorrelatedReportAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, Guid sessionId, CancellationToken ct)
    {
        if (task.AgentId is not Guid agentId)
            return;

        try
        {
            var existing = await db.AgentIncidents
                .Where(i => i.SessionId == sessionId
                    && i.Kind == AgentIncidentKind.DelegateReportUncorrelated)
                .Select(i => new { i.SessionId, i.CreatedAt })
                .ToListAsync(ct);
            if (existing.Any(i => UncorrelatedReportEvidence.IsEvidenceFor(task, i.SessionId, i.CreatedAt)))
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

    /// <summary>
    /// Answer a delegate. Blocked stays WhenIdle + task marker (the next turn). Dispatched/Working
    /// with an open question-tool on the session is the CARD-0241 overlay path: Now semantics, no
    /// marker (same turn as the brief), a persisted queue row so a 409 can late-confirm. Anything
    /// else is a 409 naming Refine vs Reply-on-Blocked vs Reply-on-open-question.
    /// </summary>
    public Task<AgentTaskSummaryDto> AnswerAsync(Guid taskId, string message, CancellationToken ct) =>
        AnswerAsync(taskId, message, AnswerOrigin.Web, round: null, ct);

    public async Task<AgentTaskSummaryDto> AnswerAsync(
        Guid taskId, string message, AnswerOrigin origin, int? round, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(message))
            throw new ValidationException(nameof(message), "A reply message is required.");

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();

        var task = await db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct)
            ?? throw new NotFoundException(nameof(AgentTask), taskId);

        if (task.Status == AgentTaskStatus.Blocked)
        {
            if (task.AgentSessionId is not Guid blockedSessionId)
                throw new ConflictException("The delegate's session is no longer available.");

            var blockCount = await db.AgentTaskEvents.AsNoTracking()
                .CountAsync(
                    e => e.AgentTaskId == taskId
                        && (e.Type == AgentTaskEventType.Blocked || e.Type == AgentTaskEventType.Conflicted),
                    ct);
            var currentRound = blockCount == 0 ? 1 : blockCount;
            if (round is int requested && requested != currentRound)
            {
                throw new ConflictException(
                    $"Task {DelegationReportFormatter.Short(taskId)} has moved on: it asked a new question (round {currentRound}) since the one you are answering (round {requested}).");
            }

            var now = UtcNow();
            task.Status = AgentTaskStatus.Working;
            task.ConcurrencyToken = Guid.NewGuid();
            db.AgentTaskEvents.Add(NewEvent(
                taskId,
                AgentTaskEventType.Replied,
                BlockedQuestion.RepliedEventDetail(origin, currentRound, message.Trim()),
                now));
            await db.SaveChangesAsync(ct);

            // The marker rides the answer so the delegate's NEXT turn correlates back to this task.
            var blockedBody = $"{DelegationReportFormatter.TaskMarker(taskId)}\n\n{message.Trim()}";
            await queue.EnqueueAsync(blockedSessionId, blockedBody, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation);

            await PublishAsync(task, ct);
            var blockedFamily = await db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetSummaryAsync(task, blockedFamily);
        }

        if (task.Status is AgentTaskStatus.Dispatched or AgentTaskStatus.Working)
        {
            if (task.AgentSessionId is not Guid sessionId)
                throw new ConflictException("The delegate's session is no longer available.");

            var runtime = scope.ServiceProvider.GetRequiredService<AgentSessionRuntime>();
            await runtime.CatchUpTranscriptAsync(sessionId, ct);
            if (!await HasOpenQuestionToolAsync(db, sessionId, ct))
            {
                throw new ConflictException(
                    $"Task {DelegationReportFormatter.Short(taskId)} is not waiting for an answer. "
                    + "Refine a running task (WhenIdle) to steer it between turns; "
                    + "reply while Blocked to answer a question; "
                    + "or reply while an ask_user_question popup is open to answer in-turn.");
            }

            db.AgentTaskEvents.Add(NewEvent(
                taskId, AgentTaskEventType.Replied,
                "Caller answered an in-turn question-tool popup.", UtcNow()));
            await db.SaveChangesAsync(ct);

            // Same turn as the brief: type the answer only. Prefixing the task marker would fail
            // option matching and appear inside the completed ToolResult, not as a UserPrompt.
            await queue.EnqueueDeliveringNowAsync(
                sessionId, message.Trim(), ct, QueuedMessageOrigin.Delegation);

            await PublishAsync(task, ct);
            var family = await db.AgentTasks.AsNoTracking().Where(t => t.RootTaskId == task.RootTaskId).ToListAsync(ct);
            return await scope.ServiceProvider.GetRequiredService<AgentTaskService>().GetSummaryAsync(task, family);
        }

        throw new ConflictException($"Task {DelegationReportFormatter.Short(taskId)} is not waiting for an answer.");
    }

    /// <summary>
    /// Transcript-grounded: the newest question-tool <c>ToolCall</c> has no later <c>ToolResult</c>
    /// for that <c>ToolUseId</c>. Screen guesses are not evidence.
    /// </summary>
    internal static async Task<bool> HasOpenQuestionToolAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var calls = await db.TranscriptEntries
            .AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId && t.Kind == TranscriptKinds.ToolCall)
            .OrderByDescending(t => t.Sequence)
            .Select(t => new { t.Sequence, t.ToolName, t.ToolUseId })
            .ToListAsync(ct);

        var open = calls.FirstOrDefault(c => GrokQuestionTool.IsQuestionToolName(c.ToolName));
        if (open is null)
            return false;
        if (string.IsNullOrEmpty(open.ToolUseId))
            return true;

        return !await db.TranscriptEntries
            .AsNoTracking()
            .AnyAsync(t => t.AgentSessionId == sessionId
                && t.Kind == TranscriptKinds.ToolResult
                && t.ToolUseId == open.ToolUseId
                && t.Sequence > open.Sequence, ct);
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

    /// <summary>
    /// One Warning per cancelled boundary (CARD-0159). Deduped on the boundary's sequence so a
    /// re-trigger (split response, replay, deferred sweep that we now skip) cannot stack the same
    /// line. Task status is untouched — the task stays Working.
    /// </summary>
    private async Task RecordInterruptedTurnAsync(
        AppDbContext db, AgentTask task, InterruptedFacts interrupted, CancellationToken ct)
    {
        var reason = interrupted.StopReason ?? "interrupted";
        var needle = $"at #{interrupted.Sequence}";
        var already = await db.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.Warning
                && e.Detail.Contains(needle),
            ct);
        if (already)
            return;

        db.AgentTaskEvents.Add(NewEvent(
            task.Id, AgentTaskEventType.Warning,
            $"Turn interrupted ({reason}) at #{interrupted.Sequence} — not a report; the task stays Working.",
            UtcNow()));
        await db.SaveChangesAsync(ct);
        _logger.LogInformation(
            "Task {ShortId}: cancelled turn at #{Seq} is not a report; staying {Status}",
            DelegationReportFormatter.Short(task.Id), interrupted.Sequence, task.Status);
    }

    private async Task SettleAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, string report, TurnOutcome turn,
        CancellationToken ct)
    {
        var now = UtcNow();
        if (!DelegationReportFormatter.TryReadReportVerdict(task.Id, report, out var verdict, out var body))
        {
            body = report;
            verdict = string.Empty;
        }

        var classified = await ClassifyReportAsync(
            services, db, task, body, verdict, turn, now, ct);
        if (classified is null)
            return;

        var (status, evidence, settledBody, failureReason) = classified.Value;
        task.Result = settledBody;
        task.ReportEvidence = evidence;
        if (failureReason is not null)
            task.FailureReason = failureReason;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        task.Status = status;

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
            // Priced by KIND as well as tier: the tiers are an abstraction over model families
            // that cost nothing alike, and a Grok delegate run through the Claude ladder reads
            // several times its real spend (CARD-0084 S5). ClaudeCode is the column default, so
            // every Claude task — and every row written before kinds existed — is unmoved.
            task.CostUsd = DelegationCost.Estimate(
                _settings.Pricing, task.ModelLevel, spend, now, task.AgentKind);
            task.CostPricingVersion = DelegationCost.PricingVersion;
        }

        // The delegate was told to spill a long report to a file. Note it if it did — and if it
        // ignored the instruction, write the file ourselves so the excerpt has somewhere to point.
        task.ResultFilePath = await ResolveSpillFileAsync(task, settledBody, ct);
        (task.DeliverablePath, task.DeliverableRef) = await ResolveDeliverableAsync(services, task, settledBody, ct);
        if (task.Status == AgentTaskStatus.Succeeded)
            await TryBuildDeliverableBundleAsync(services, db, task, settledBody, ct);

        // What the report was built from, on the record. The report is the turn-ending response's
        // own text, so a delegate that front-loaded findings mid-turn left some behind — name how
        // much, or that loss is invisible from every surface (CARD-0046 slice 2). CARD-0159 adds
        // the verdict / evidence class so a Succeeded that was not positively reported is never silent.
        var reported = DescribeReported(
            settledBody.Length, turn, evidence, string.IsNullOrEmpty(verdict) ? null : verdict);

        var eventType = task.Status switch
        {
            AgentTaskStatus.Blocked => AgentTaskEventType.Blocked,
            AgentTaskStatus.Failed => AgentTaskEventType.Failed,
            _ => AgentTaskEventType.Completed,
        };
        var eventDetail = task.FailureCode == AgentTaskFailureCode.CompletedWithoutProgress
            && failureReason is not null
            ? failureReason
            : task.Status == AgentTaskStatus.Blocked && evidence == AgentTaskReportEvidence.QuestionHeuristic
                ? BlockedQuestion.BlockedEventDetail(settledBody)
                : reported;
        db.AgentTaskEvents.Add(NewEvent(task.Id, eventType, eventDetail, now));

        // A settlement that could not get the final message is LOUD (CARD-0046 slice 3). Succeeded
        // is still the right status — the work happened and the text is real — but "Succeeded" on
        // its own says the caller got the verdict, and here it did not. Three surfaces, because the
        // whole character of this failure was that every surface said the task was fine: an event on
        // the task, an incident on the agent's timeline, and a line the CALLER reads above the
        // report itself.
        string? callerWarning = null;
        if (turn.FinalMessageMissing)
        {
            callerWarning = FinalMessageMissingWarning(settledBody.Length, _settings.FinalMessageGraceSeconds);
            db.AgentTaskEvents.Add(NewEvent(
                task.Id, AgentTaskEventType.Warning,
                FinalMessageMissingDetail(settledBody.Length, _settings.FinalMessageGraceSeconds), now));
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

        var (gitHeader, gitWarning) = await TryDescribeGitAsync(services, db, task, settledBody, now, ct);
        if (gitWarning is not null)
            callerWarning = callerWarning is null ? gitWarning : $"{callerWarning}\n\n{gitWarning}";

        // A finished Merge task is what un-blocks the conflicted task it was spawned for.
        if (task.Status == AgentTaskStatus.Succeeded && task.Role == AgentTaskRole.Merge)
            await ResolveConflictedParentAsync(services, db, task, now, ct);

        // What the delegate ACTUALLY touched, against what it said it would (CARD-0063 S4).
        // Before ReleaseDelegateAsync, which retires the ephemeral agent whose working directory
        // the file data hangs off. Observability only: it can add an event and fill a column, and
        // it can do neither of those things badly enough to stop a settlement.
        var drift = await RecordScopeDriftAsync(services, db, task, now, ct);

        // A settled delegate is not spent — it is WARM. A Shared task's agent goes into the pool
        // for follow-ups and unrelated work in its directory; only worktree delegates retire on
        // the spot (their directory dies with the merge). Blocked tasks keep everything — the
        // session is how the conversation continues. CARD-0286's zero-progress failure keeps the
        // worktree for inspection and records the incident before release so cascade-delete of
        // the ephemeral agent cannot erase it (CARD-0085's ownership-safe order).
        //
        // CARD-0319: persist the task row and deliver the parent note BEFORE releasing the
        // agent. KillAsync's own SaveChanges used to flush this tracker (including the still-
        // uncommitted settlement); the pool sweeper then deleted the agent; the later save
        // threw; OnTurnEndAsync swallowed it and skipped delivery.
        if (task.FailureCode == AgentTaskFailureCode.CompletedWithoutProgress
            && task.AgentSessionId is Guid noProgressFrom)
        {
            await RecordIncidentOnceAsync(
                services, db, task, noProgressFrom,
                AgentIncidentKind.DelegateCompletedWithoutProgress,
                $"Delegate task {DelegationReportFormatter.Short(task.Id)} reported completion without worktree progress",
                failureReason ?? "The delegate reported completion but Antiphon observed no post-dispatch worktree progress.",
                ct, AlertSeverity.Error);
        }

        if (task.FailureCode == AgentTaskFailureCode.CompletedWithoutProgress
            && failureReason is not null)
        {
            callerWarning = callerWarning is null ? failureReason : $"{failureReason}\n\n{callerWarning}";
        }

        var shouldRelease = task.FailureCode == AgentTaskFailureCode.CompletedWithoutProgress
            || task.Status == AgentTaskStatus.Succeeded;
        var killSession = task.FailureCode != AgentTaskFailureCode.CompletedWithoutProgress;

        await PersistDeliverThenReleaseAsync(
            services, db, task, now, settledBody, ct,
            release: shouldRelease,
            killSession: killSession,
            workspaceNote: workspaceNote,
            warning: callerWarning,
            drift: drift,
            git: gitHeader,
            afterPersist: async token =>
            {
                if (turn.FinalMessageMissing && task.AgentSessionId is Guid missingFrom)
                {
                    await RecordFinalMessageMissingAsync(
                        services, db, task, missingFrom,
                        $"Task {DelegationReportFormatter.Short(task.Id)} settled without the delegate's final "
                        + $"message. {FinalMessageMissingDetail(settledBody.Length, _settings.FinalMessageGraceSeconds)} "
                        + "The caller has been told the report may be preamble; the whole turn is in this "
                        + "session's transcript.",
                        token);
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
                        token);
                }

                _logger.LogInformation(
                    "Task {ShortId} settled as {Status} ({Chars:N0} chars, ${Cost:0.000}, {Evidence})",
                    DelegationReportFormatter.Short(task.Id), task.Status, settledBody.Length, task.CostUsd, evidence);
            });
    }

    /// <summary>
    /// Map the task's touched paths onto the repo's areas, store them on the row, and write one
    /// <see cref="AgentTaskEventType.ScopeDrift"/> event when the declaration did not cover them
    /// (CARD-0063 S4). Returns the completion header's <c>drift=</c> value, or null.
    ///
    /// <para><b>It never blocks, holds, kills or re-types anything</b> — that is the whole design
    /// decision (§2.5): a path hook could only ever be armed in a worktree, where an out-of-area
    /// write is already isolated, and it would turn every wrong prediction into a stuck delegate at
    /// exactly the moment it found the file nobody predicted. And it never throws: observability
    /// must not be able to break settlement.</para>
    /// </summary>
    private async Task<string?> RecordScopeDriftAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, DateTime now, CancellationToken ct)
    {
        if (task.AgentId is not Guid agentId)
            return null;

        try
        {
            if (services.GetService<AgentFilesService>() is not { } files)
                return null;

            var dto = await files.GetFilesAsync(agentId, since: null, ct);
            if (dto?.Files is not { Count: > 0 } touched)
                return null;

            var map = services.GetService<AreaMapLoader>()?.Load(task.RepoPath) ?? AreaMap.Empty;
            var result = ScopeDriftPolicy.Evaluate(task.Scope, touched.Select(f => f.Path), map);
            task.ObservedScope = result.ObservedScope;

            if (ScopeDriftPolicy.DescribeDrift(task.Scope, result.Drifted) is not { } detail)
                return null;

            db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.ScopeDrift, detail, now));
            _logger.LogInformation(
                "Task {ShortId} drifted: {Detail}", DelegationReportFormatter.Short(task.Id), detail);
            return ScopeDriftPolicy.DescribeHeader(result.Drifted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not record scope drift for task {ShortId}.",
                DelegationReportFormatter.Short(task.Id));
            return null;
        }
    }

    /// <summary>
    /// CARD-0085: the delivery watchdog (or the dead-session twin) was about to write Failed
    /// because the session ingested nothing, but the working directory has positive evidence the
    /// work happened. Succeeded is the right status — Failed is what makes a less-careful caller
    /// redispatch on top of already-pushed work — and CARD-0046's "Succeeded but loud" is how the
    /// recovered verdict stays visible: Warning event, incident, caller-facing caveat.
    ///
    /// Does not bind or ingest the refused file. Does not kill the session (CARD-0056: a kill on
    /// a false Failed is how you kill a live worker).
    /// </summary>
    public async Task RecoverFromBindRefusalAsync(
        Guid taskId, DelegateBindRefusalEvidence evidence, CancellationToken ct)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var task = await db.AgentTasks.FirstOrDefaultAsync(
            t => t.Id == taskId
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working),
            ct);
        if (task is null)
            return;

        var now = UtcNow();
        var where = evidence.Describe();
        var note =
            $"Recovered from an unbound session; work is at {where}. C1–C4 were not changed.";
        var warning =
            $"WARNING: this task was recovered from an unbound session (zero ingested transcript "
            + $"rows). The work is at {where}. C1–C4 were not changed. Do not redispatch — that "
            + "would run again on top of already-landed work.";

        task.Status = AgentTaskStatus.Succeeded;
        task.Result = note;
        task.FailureReason = null;
        task.CompletedAt = now;
        task.RecoveredAt = now;
        task.ConcurrencyToken = Guid.NewGuid();

        db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Completed, note, now));
        db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Warning, warning, now));

        string? workspaceNote = null;
        if (task.Workspace == WorkspaceMode.Worktree)
            workspaceNote = await MergeBackAsync(scope.ServiceProvider, db, task, now, ct);

        // Incident before release: AgentIncidents cascade-delete with the agent row, and a
        // worktree pool delegate's ordinary success removes that row.
        if (task.AgentSessionId is Guid sessionId)
        {
            await RecordIncidentOnceAsync(
                scope.ServiceProvider, db, task, sessionId,
                AgentIncidentKind.DelegateBindRefusalRecovered,
                $"Delegate task {DelegationReportFormatter.Short(task.Id)} recovered from an unbound session",
                $"task {DelegationReportFormatter.Short(task.Id)} recovered from an unbound session; "
                + $"work is at {where}. C1–C4 were not changed.",
                ct);
        }

        // Ordinary success release, minus the kill: the session may still be a live worker whose
        // only crime was an unbound transcript (CARD-0056). killSession: false also skips the
        // agent-row delete so the incident just recorded is not cascaded away.
        // CARD-0319: persist + deliver before release, same order as SettleAsync.
        await PersistDeliverThenReleaseAsync(
            scope.ServiceProvider, db, task, now, note, ct,
            release: task.Status == AgentTaskStatus.Succeeded,
            killSession: false,
            workspaceNote: workspaceNote,
            warning: warning,
            afterPersist: _ =>
            {
                _logger.LogWarning(
                    "Task {ShortId} recovered from bind refusal ({Evidence}); settled Succeeded. Session not killed.",
                    DelegationReportFormatter.Short(task.Id), where);
                return Task.CompletedTask;
            });
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

        await PersistDeliverThenReleaseAsync(
            services, db, task, now, reason, ct,
            afterPersist: async token =>
            {
                await RecordFinalMessageMissingAsync(
                    services, db, task, sessionId,
                    $"Task {DelegationReportFormatter.Short(task.Id)} failed with no report: its turn-ending "
                    + $"response wrote no text within the {_settings.FinalMessageGraceSeconds}s grace and the "
                    + "turn produced none either. The delegate may have done the work — the session's "
                    + "transcript is the only record of it.",
                    token);

                _logger.LogWarning(
                    "Task {ShortId} failed: session {SessionId} ended a turn with no text at all within the "
                    + "{Grace}s grace",
                    DelegationReportFormatter.Short(task.Id), sessionId, _settings.FinalMessageGraceSeconds);
            });
    }

    /// <summary>
    /// The marked turn was ended by an API-error stub (CARD-0071 S3 / CARD-0072 S5a-3, spec §D6):
    /// the API killed the turn after Claude Code's own retry was exhausted, and the stub's error
    /// string is not a report. A retryable class leaves the task <see cref="AgentTaskStatus.Working"/>
    /// with a scheduled resume (do not release the delegate, do not tell the parent it failed).
    /// NeedsHuman, Unknown-exhausted, and wall-parked still Fail — visibly dead beats invisibly
    /// "Succeeded".
    ///
    /// <para>Idempotency is the <see cref="ApiErrorRecovery"/> row: SettleDeferredReportsAsync
    /// re-triggers this on every pass while the task stays Working, and without the marker it
    /// would write an event and re-enter forever. The event is written exactly once per stub.</para>
    /// </summary>
    private async Task HandleApiErrorTurnAsync(
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

        var recovery = services.GetService<ApiErrorRecoveryService>() is { } recoveryService
            ? await recoveryService.EnsureAdoptedAsync(
                sessionId, stub.Sequence, stub.Uuid, stub.ErrorClass, stub.ErrorStatus, stub.ErrorText,
                ct, raiseIncident: false)
            : null;

        if (recovery is { ResolvedAt: null })
        {
            await DeferApiErrorTurnAsync(services, db, task, sessionId, stub, classification, recovery, errorText, now, ct);
            return;
        }

        var terminalReason = recovery?.ResolvedReason;
        var reason =
            $"The delegate's turn was killed by an API error ({classification}: "
            + (stub.ErrorClass ?? "no error class")
            + (stub.ErrorStatus is int status ? $", HTTP {status}" : string.Empty)
            + $") — {errorText}"
            + (terminalReason is null
                ? string.Empty
                : $" Recovery ended ({terminalReason}).")
            + " The error text is not a report and no report exists. The work may "
            + $"well be real — read session {sessionId} before re-running this task.";

        task.Status = AgentTaskStatus.Failed;
        task.FailureReason = reason;
        if (stub.ErrorStatus == 401)
            task.FailureCode = AgentTaskFailureCode.AuthenticationRequired;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Failed, reason, now));

        await PersistDeliverThenReleaseAsync(
            services, db, task, now, reason, ct,
            afterPersist: async token =>
            {
                // Severity is the CARD-0055/0067 rule: Critical when the agent is channel-bound (a human is
                // waiting on a line this death just went silent on) — and for NeedsHuman, where no retry
                // will ever exist and a human is the ONLY recovery.
                var channelBound = task.AgentId is Guid boundAgentId
                    && await db.ChatChannels.AsNoTracking().AnyAsync(c => c.AgentId == boundAgentId, token);
                var severity = channelBound || classification == ApiErrorClassification.NeedsHuman
                    ? AlertSeverity.Critical
                    : AlertSeverity.Warning;

                // A dirty SHARED checkout is the human's exposure to see when deciding about the dead task
                // (spec §D6): auto-salvage is rejected — the operator's own edits live there too — so the
                // incident carries the evidence instead. Best-effort by design.
                var dirt = task.Workspace == WorkspaceMode.Shared
                    ? await TryReadGitStatusShortAsync(task.WorkingDirectory, token)
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
                    token, severity);

                _logger.LogWarning(
                    "Task {ShortId} failed: session {SessionId}'s turn was killed by an API error "
                    + "({Classification}: {Class}/{Status})",
                    DelegationReportFormatter.Short(task.Id), sessionId, classification,
                    stub.ErrorClass, stub.ErrorStatus);
            });
    }

    /// <summary>
    /// Retryable death: keep the task Working, write the timeline event once, do not release the
    /// delegate (it still owns the session for the resumed turn), do not deliver a failure to the
    /// parent. A second pass on the same stub is a no-op besides the already-recorded incident.
    /// </summary>
    private async Task DeferApiErrorTurnAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, Guid sessionId,
        ApiErrorStubFacts stub, ApiErrorClassification classification, ApiErrorRecovery recovery,
        string errorText, DateTime now, CancellationToken ct)
    {
        if (task.Status == AgentTaskStatus.Dispatched)
            task.Status = AgentTaskStatus.Working;

        var seqNeedle = $"seq {stub.Sequence}";
        var alreadyDeferred = await db.AgentTaskEvents.AnyAsync(
            e => e.AgentTaskId == task.Id
                && e.Type == AgentTaskEventType.ApiErrorDeferred
                && e.Detail.Contains(seqNeedle), ct);
        if (!alreadyDeferred)
        {
            var when = recovery.NextAttemptAt is DateTime fire
                ? $"resume scheduled {fire:u}"
                : "resume pending";
            db.AgentTaskEvents.Add(NewEvent(
                task.Id, AgentTaskEventType.ApiErrorDeferred,
                $"turn killed by {classification} — {when} ({seqNeedle})", now));
        }

        await db.SaveChangesAsync(ct);
        if (!alreadyDeferred)
            await PublishAsync(task, ct);

        var channelBound = task.AgentId is Guid boundAgentId
            && await db.ChatChannels.AsNoTracking().AnyAsync(c => c.AgentId == boundAgentId, ct);
        var severity = channelBound || classification == ApiErrorClassification.NeedsHuman
            ? AlertSeverity.Critical
            : AlertSeverity.Warning;

        var dirt = task.Workspace == WorkspaceMode.Shared
            ? await TryReadGitStatusShortAsync(task.WorkingDirectory, ct)
            : null;

        await RecordIncidentOnceAsync(
            services, db, task, sessionId, AgentIncidentKind.ApiErrorTurnDied,
            $"Delegate task {DelegationReportFormatter.Short(task.Id)} died on an API error ({classification})",
            $"Task {DelegationReportFormatter.Short(task.Id)} was killed by an API error, not "
            + $"finished: {classification} ({stub.ErrorClass ?? "no error class"}"
            + (stub.ErrorStatus is int s ? $", HTTP {s}" : string.Empty)
            + $"). A timed resume is scheduled"
            + (recovery.NextAttemptAt is DateTime at ? $" for {at:u}" : string.Empty)
            + $". The error text was NOT stored as its result: {errorText}"
            + (string.IsNullOrWhiteSpace(dirt)
                ? string.Empty
                : $"\n\nThe shared checkout at {task.WorkingDirectory} has uncommitted changes the "
                  + $"dead task may own (git status --short):\n{dirt}"),
            ct, severity);

        if (!alreadyDeferred)
        {
            _logger.LogWarning(
                "Task {ShortId} deferred: session {SessionId}'s turn was killed by an API error "
                + "({Classification}: {Class}/{Status}); resume at {Next:u}",
                DelegationReportFormatter.Short(task.Id), sessionId, classification,
                stub.ErrorClass, stub.ErrorStatus, recovery.NextAttemptAt);
        }
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

            case DelegationWorktreeService.MergeResult.AlreadyCleanedUp:
                db.AgentTaskEvents.Add(NewEvent(
                    task.Id, AgentTaskEventType.Merged,
                    outcome.Detail ?? "worktree already cleaned up by the task", now));
                return "worktree already cleaned up by the task";

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
        conflicted.LandRequestedAt = null;
        conflicted.LandVerifyFilter = null;
        conflicted.LandStartedAt = null;
        conflicted.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(
            conflicted.Id, AgentTaskEventType.Merged,
            $"Conflict resolved by merge task {DelegationReportFormatter.Short(merge.Id)}.", now));
        await StageOutcomeService.AttachMergeResolutionAsync(db, merge, ct);
        await ReleaseDelegateAsync(services, db, conflicted, now, ct);
        await PublishAsync(conflicted, ct);
    }

    /// <summary>
    /// CARD-0319: persist the task row, notify the parent, THEN release the agent. A later
    /// concurrency exception on an unrelated row (the agent the pool sweeper already deleted)
    /// must not skip delivery — the task being correctly Succeeded is the delivery trigger,
    /// not a successful save of the retire.
    /// </summary>
    private async Task PersistDeliverThenReleaseAsync(
        IServiceProvider services,
        AppDbContext db,
        AgentTask task,
        DateTime now,
        string report,
        CancellationToken ct,
        bool release = true,
        bool killSession = true,
        string? workspaceNote = null,
        string? warning = null,
        string? drift = null,
        string? git = null,
        Func<CancellationToken, Task>? afterPersist = null)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            if (!await TaskAlreadyPersistedAsync(task, ct))
                throw;
            _logger.LogWarning(
                ex,
                "Settlement of task {ShortId} raced on a later row; the task is already {Status} (CARD-0319)",
                DelegationReportFormatter.Short(task.Id), task.Status);
        }

        if (afterPersist is not null)
            await afterPersist(ct);

        await DeliverToParentAsync(task, report, ct, workspaceNote, warning, drift, git);
        await PublishAsync(task, ct);

        if (!release)
            return;

        try
        {
            await ReleaseDelegateAsync(services, db, task, now, ct, killSession);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not persist delegate release for task {ShortId} after the parent note was delivered (CARD-0319)",
                DelegationReportFormatter.Short(task.Id));
        }
    }

    /// <summary>
    /// Fresh-context read: true when the store already has this task in the status we just
    /// assigned, with a CompletedAt. Used to keep delivering after an unrelated row (the
    /// agent) caused the settlement SaveChanges to throw.
    /// </summary>
    private async Task<bool> TaskAlreadyPersistedAsync(AgentTask task, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var store = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var stored = await store.AgentTasks.AsNoTracking()
                .Where(t => t.Id == task.Id)
                .Select(t => new { t.Status, t.CompletedAt })
                .FirstOrDefaultAsync(ct);
            return stored is not null
                && stored.Status == task.Status
                && stored.CompletedAt is not null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Could not re-read task {ShortId} after a settlement concurrency exception",
                DelegationReportFormatter.Short(task.Id));
            return false;
        }
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
    ///
    /// <para>CARD-0221: a CARD-0085 recovery (<paramref name="killSession"/> false) used to
    /// <c>return</c> leaving the Worktree row <c>Running</c> with no owner. That is a zombie by
    /// construction. The arm now marks the row Idle for the janitor without making it claimable
    /// (no reservation; reuse skips worktree-shaped directories) and without killing now.</para>
    /// </summary>
    private async Task ReleaseDelegateAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, DateTime now, CancellationToken ct,
        bool killSession = true)
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

        // CARD-0085 / CARD-0221: do not kill now (a kill on a false Failed is how you kill a live
        // worker) and do not delete the row (AgentIncidents cascade with it). Mark Idle so the
        // janitor owns the process after PoolIdleRetireMinutes — the same hour a Shared recovery
        // already gets. Clear the reservation: this is not a warm reuse candidate.
        if (!killSession)
        {
            agent.Status = AgentStatus.Idle;
            agent.PoolIdleSince = now;
            agent.PoolReservedForRootTaskId = null;
            agent.UpdatedAt = now;
            if (task.Workspace == WorkspaceMode.Worktree)
            {
                _logger.LogWarning(
                    "Worktree delegate '{Name}' for task {ShortId} marked Idle for retirement in {Minutes} minutes (CARD-0085 recovery; session not killed)",
                    agent.Name,
                    DelegationReportFormatter.Short(task.Id),
                    _settings.PoolIdleRetireMinutes);
            }
            return;
        }

        if (task.AgentSessionId is Guid sessionId)
        {
            try
            {
                // CARD-0319: resolve the stopper from a NEW scope so KillAsync's SaveChanges
                // cannot flush this caller's still-dirty change tracker. AgentSessionService is
                // scoped and IDelegateSessionStopper is the same instance.
                await using var killScope = _scopeFactory.CreateAsyncScope();
                await killScope.ServiceProvider
                    .GetRequiredService<IDelegateSessionStopper>()
                    .KillAsync(sessionId, ct);
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
        string? warning = null, string? drift = null, string? git = null)
    {
        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return;

        var note = DelegationReportFormatter.BuildCompletionNote(
            task, _settings, report, workspaceNote, ReplyInlineMaxChars, warning,
            await DescribeOverlappingRunningAsync(task, ct), drift,
            ReportEvidenceHeader(task.ReportEvidence), git,
            DescribeDeliverable(task));
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var queue = scope.ServiceProvider.GetRequiredService<SessionMessageQueueService>();

            // ConversationKey batches a contiguous run of same-root completions into ONE delivery,
            // so five delegates landing together produce one note, not five turns. The queue's
            // size-aware batching stops before the combined body crosses the inline ceiling.
            await queue.EnqueueAsync(
                parentSession, note.Body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"task:{task.RootTaskId:N}",
                task.Id, DelegationNoteDigest.Compute(report), note.Header);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A dead parent session must not lose the result — it is already persisted on the task.
            _logger.LogWarning(
                ex, "Could not deliver task {ShortId} report to parent session {SessionId}",
                DelegationReportFormatter.Short(task.Id), parentSession);
        }
    }

    private static DelegationReportFormatter.DeliverableNote? DescribeDeliverable(AgentTask task)
    {
        var bit = DeliverableBundleService.FormatNoteBit(task);
        if (bit is null)
            return null;
        return new DelegationReportFormatter.DeliverableNote(bit, DeliverableBundleService.ListAttachableFiles(task));
    }

    /// <summary>
    /// Short ids of tasks still running in this task's repo whose areas it touched, or null when
    /// there are none (CARD-0063 S3).
    ///
    /// <para>This is the entire merge-ordering deliverable. The server's auto-merge has met zero
    /// conflicts in production because 216 of 246 merge-backs are LeftForHuman: the operator merges
    /// by hand, by design. Naming the live task that shares this one's ground lets them pick an
    /// order in which the second rebase is trivial - which is worth far more than a merge queue
    /// nothing would use.</para>
    ///
    /// <para>Declared-scope intersections only. D3's "two shared writers in one checkout" is a
    /// dispatch-safety rule, not a statement about which files this task's diff touches, so it has
    /// no business in a merge-order hint. Never throws: an observability line must not be able to
    /// stop a settlement (the same contract as the catch this method sits beside).</para>
    /// </summary>
    private async Task<string?> DescribeOverlappingRunningAsync(AgentTask task, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(task.Scope))
            return null;

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var map = scope.ServiceProvider.GetService<AreaMapLoader>()?.Load(task.RepoPath)
                ?? AreaMap.Empty;
            var mine = ScopeResolver.Resolve(task.Scope, map);
            var key = ScopeResolver.KeyFor(task.RepoPath, task.WorkingDirectory);

            var running = await db.AgentTasks.AsNoTracking()
                .Where(t => t.Id != task.Id
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working)
                    && t.Scope != null
                    && t.Workspace != WorkspaceMode.ReadOnly
                    && t.Role != AgentTaskRole.Check)
                .Select(t => new { t.Id, t.WorkingDirectory, t.RepoPath, t.Scope })
                .ToListAsync(ct);

            var ids = running
                .Where(t => string.Equals(
                    DelegationWorkspaceResolver.NormalizeSeparators(
                        ScopeResolver.KeyFor(t.RepoPath, t.WorkingDirectory)),
                    DelegationWorkspaceResolver.NormalizeSeparators(key),
                    StringComparison.OrdinalIgnoreCase))
                .Where(t => ScopeResolver.Intersects(ScopeResolver.Resolve(t.Scope, map), mine))
                .Select(t => DelegationReportFormatter.Short(t.Id))
                .ToList();

            return ids.Count == 0 ? null : string.Join(",", ids);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not list running tasks overlapping task {ShortId}.",
                DelegationReportFormatter.Short(task.Id));
            return null;
        }
    }

    /// <summary>
    /// The TurnEnd ExtractMarkedTurnAsync resolved (CARD-0248). Sequence is the settle-anyway
    /// identity; CreatedAt is the store time, never the record's backdated Timestamp.
    /// </summary>
    private readonly record struct BoundaryFacts(long Sequence, DateTime CreatedAt);

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
    /// Non-null when the marked turn was ENDED by an API-error stub (CARD-0071 S3 / CARD-0072
    /// S5a-3): the API killed the turn and the stub's error string is not a report. Outranks
    /// every other verdict — retryable classes defer; terminal classes fail. The error text is
    /// never stored as Result.
    /// </param>
    /// <param name="Boundary">
    /// The TurnEnd this outcome was extracted from (CARD-0248). Sequence is the settle-anyway
    /// identity; CreatedAt is the store time (never the record's backdated Timestamp) so the
    /// delivery gate can require the boundary to post-date the nudge's SentAt. Null on the
    /// static Nothing / Deferred values, which never reach ClassifyReportAsync.
    /// </param>
    private readonly record struct TurnOutcome(
        string? Report,
        bool UncorrelatedReport,
        bool DeferredForFinalMessage = false,
        bool FinalMessageMissing = false,
        int NarrationDiscardedChars = 0,
        int AbandonedSubagents = 0,
        ApiErrorStubFacts? ApiErrorStub = null,
        InterruptedFacts? Interrupted = null,
        BoundaryFacts? Boundary = null)
    {
        public static readonly TurnOutcome Nothing = new(null, false);
        public static readonly TurnOutcome Deferred = new(null, false, DeferredForFinalMessage: true);
    }

    /// <summary>
    /// A <c>TurnEnd</c> that is an idle boundary but never a report (CARD-0159) — currently only
    /// Grok's measured <c>stop_reason=cancelled</c>.
    /// </summary>
    private readonly record struct InterruptedFacts(long Sequence, string? Uuid, string? StopReason);

    /// <summary>
    /// What the stub itself carried (S1's three fields plus its error string) — everything the
    /// fail arm needs to classify and to name the death, straight off the turn-ending row.
    /// </summary>
    private readonly record struct ApiErrorStubFacts(
        string? ErrorClass, int? ErrorStatus, string? ErrorText, long Sequence, string? Uuid);

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

        // CARD-0159: a cancelled boundary is idle (the queue already flushed) but never a report.
        // Checked before the walk-back so we cannot settle on the interrupted turn's narration.
        if (!TranscriptKinds.IsReportBoundary(end.Kind, end.StopReason))
            return new TurnOutcome(
                null, false, Interrupted: new InterruptedFacts(end.Sequence, end.Uuid, end.StopReason));

        var turnEnd = end.Sequence;

        var span = await LoadPromptsInSpanAsync(db, sessionId, task.DispatchedAt, ct);
        // CARD-0135: the walk-back (and the nextPrompt cap below) may land on a QueuedUserPrompt.
        // A drained queued_command has no accompanying user record, so this is the only prompt
        // row a queued brief's turn has — and the only reason that report can settle at all.
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
            // Codex stamps the diagnostic on the TurnEnd itself (no synthetic AssistantText stub).
            if (stubText.Length == 0 && !string.IsNullOrWhiteSpace(end.Text))
                stubText = end.Text.Trim();
            return new TurnOutcome(
                null, false,
                ApiErrorStub: new ApiErrorStubFacts(
                    end.ApiErrorClass, end.ApiErrorStatus,
                    stubText.Length > 0 ? stubText : null,
                    end.Sequence, end.Uuid));
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
                    ? new TurnOutcome(
                        null, false, FinalMessageMissing: true,
                        Boundary: new BoundaryFacts(end.Sequence, end.CreatedAt))
                    : new TurnOutcome(
                        joined, false, FinalMessageMissing: true, AbandonedSubagents: abandonedSubagents,
                        Boundary: new BoundaryFacts(end.Sequence, end.CreatedAt));
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
                AbandonedSubagents: abandonedSubagents,
                Boundary: new BoundaryFacts(end.Sequence, end.CreatedAt));
        }

        return joined.Length == 0
            ? TurnOutcome.Nothing
            : new TurnOutcome(
                joined, false, AbandonedSubagents: abandonedSubagents,
                Boundary: new BoundaryFacts(end.Sequence, end.CreatedAt));
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
        AppDbContext db, Guid sessionId, long promptSequence, long? cap, TranscriptPromptSpan.Result span,
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

    /// <summary>
    /// The prompts a turn of THIS task could have answered. Owned by
    /// <see cref="TranscriptPromptSpan"/> so settlement and the delivery watchdog cannot disagree
    /// on the four-way housekeeping filter (CARD-0077) or on whether a drained
    /// <c>queued_command</c> counts (CARD-0135).
    /// </summary>
    private static Task<TranscriptPromptSpan.Result> LoadPromptsInSpanAsync(
        AppDbContext db, Guid sessionId, DateTime? dispatchedAt, CancellationToken ct) =>
        TranscriptPromptSpan.LoadAsync(db, sessionId, dispatchedAt, ct);

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

    private static readonly Regex DeliverablePathPattern = new(
        "`?(?<path>docs/[\\w./-]+\\.md)`?", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// The report is immutable at settlement, so this is a one-time pointer extraction rather than
    /// a second, drifting index of the workspace. Disk wins: a merged plan remains readable after
    /// its transient worktree branch has gone. Only a branch-only hit retains its ref.
    /// </summary>
    /// <summary>
    /// CARD-0230: an enrichment of the report, not a requirement of it — a harness/host missing
    /// <see cref="GitWorkspaceService"/> (or any other failure here) must never abort settlement
    /// before <c>SaveChangesAsync</c>, the same contract <see cref="RecordScopeDriftAsync"/> already
    /// holds itself to for the same reason.
    /// </summary>
    private async Task<(string? Path, string? Ref)> ResolveDeliverableAsync(
        IServiceProvider services, AgentTask task, string report, CancellationToken ct)
    {
        try
        {
            var git = services.GetRequiredService<GitWorkspaceService>();
            foreach (Match match in DeliverablePathPattern.Matches(report))
            {
                var relative = match.Groups["path"].Value;
                if (string.IsNullOrWhiteSpace(relative))
                    continue;

                foreach (var root in new[] { task.WorkingDirectory, task.RepoPath }.Where(root => !string.IsNullOrWhiteSpace(root)).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (File.Exists(Path.Combine(root!, relative.Replace('/', Path.DirectorySeparatorChar))))
                        return (relative, null);
                }

                if (task.Workspace == WorkspaceMode.Worktree && !string.IsNullOrWhiteSpace(task.WorktreeBranch))
                {
                    var repository = task.RepoPath ?? task.WorkingDirectory;
                    if (await git.GetContentAtAsync(repository, relative, task.WorktreeBranch, ct) is not null)
                        return (relative, task.WorktreeBranch);
                }
            }

            return (null, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not resolve a deliverable pointer for task {ShortId}.",
                DelegationReportFormatter.Short(task.Id));
            return (null, null);
        }
    }

    /// <summary>
    /// CARD-0337: enrichment, same never-abort contract as <see cref="ResolveDeliverableAsync"/>.
    /// A missing <see cref="DeliverableBundleService"/> (test harness) is a no-op.
    /// </summary>
    private async Task TryBuildDeliverableBundleAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, string report, CancellationToken ct)
    {
        try
        {
            var bundles = services.GetService<DeliverableBundleService>();
            if (bundles is null)
                return;
            await bundles.TryBuildAsync(task, report, db, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not build a deliverable bundle for task {ShortId}.",
                DelegationReportFormatter.Short(task.Id));
        }
    }

    /// <summary>
    /// CARD-0286: an explicit <c>done</c> from a Code Worktree task is Succeeded only when the
    /// isolated worktree has a post-dispatch commit or changed/untracked content. Fail open when
    /// the probe is missing or git evidence is unavailable — this is a proof of zero work, not a
    /// reason to punish a failed probe. Shared workspaces and non-Code roles are out of scope.
    /// </summary>
    private async Task<(AgentTaskStatus Status, AgentTaskReportEvidence Evidence, string Body, string? FailureReason)?>
        TryClassifyCompletedWithoutProgressAsync(
            IServiceProvider services, AgentTask task, string body, CancellationToken ct)
    {
        if (task.Role != AgentTaskRole.Code
            || task.Workspace != WorkspaceMode.Worktree
            || task.DispatchedAt is not DateTime dispatchedAt
            || string.IsNullOrWhiteSpace(task.WorktreePath))
            return null;

        IWorkspaceProgressProbe? probe;
        try
        {
            probe = services.GetService<IWorkspaceProgressProbe>();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Worktree progress probe unavailable for task {ShortId}; failing open",
                DelegationReportFormatter.Short(task.Id));
            return null;
        }

        if (probe is null)
            return null;

        WorkspaceProgressArm arm;
        try
        {
            arm = await probe.ProbeProgressAsync(
                task.WorktreePath, dispatchedAt, sharedCheckout: false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Worktree progress probe failed for task {ShortId} at {Path}; failing open",
                DelegationReportFormatter.Short(task.Id), task.WorktreePath);
            return null;
        }

        if (!arm.Available)
            return null;
        if (arm.LastCommitAt is not null || arm.LastFileChangeAt is not null)
            return null;

        var reason =
            "The delegate reported completion but Antiphon observed no post-dispatch worktree "
            + $"progress at {task.WorktreePath}: 0 commits after dispatch and 0 changed or "
            + "untracked files.";
        task.FailureCode = AgentTaskFailureCode.CompletedWithoutProgress;
        return (AgentTaskStatus.Failed, AgentTaskReportEvidence.Marked, body, reason);
    }

    /// <summary>
    /// CARD-0159 S2 / CARD-0248: a closing verdict line settles immediately; an unmarked live
    /// session is nudged once; a second unmarked end after the delivered nudge (or a dead
    /// session, or a Check role) settles with the evidence class recorded. Returns null when
    /// the task was nudged and must NOT settle — including when the same boundary re-enters,
    /// the nudge has not been typed, or a text-less post-nudge boundary is still inside the
    /// response window.
    /// </summary>
    private async Task<(AgentTaskStatus Status, AgentTaskReportEvidence Evidence, string Body, string? FailureReason)?>
        ClassifyReportAsync(
            IServiceProvider services, AppDbContext db, AgentTask task, string body, string verdict,
            TurnOutcome turn, DateTime now, CancellationToken ct)
    {
        // CARD-0302: a Check reading is the deliverable. Classify the role before the generic
        // blocked/question arms so LOOKS STUCK + a `blocked` token (or a trailing `?`) cannot
        // mark the interpreter's own row Blocked. Do not parse LOOKS STUCK as English.
        if (task.Role == AgentTaskRole.Check)
            return ClassifyCheckReport(body, verdict);

        if (verdict == "done")
        {
            if (await TryClassifyCompletedWithoutProgressAsync(services, task, body, ct) is { } noProgress)
                return noProgress;
            return (AgentTaskStatus.Succeeded, AgentTaskReportEvidence.Marked, body, null);
        }
        if (verdict == "blocked")
            return (AgentTaskStatus.Blocked, AgentTaskReportEvidence.Marked, body, null);
        if (verdict == "failed")
        {
            var reason = FirstLine(body);
            return (AgentTaskStatus.Failed, AgentTaskReportEvidence.Marked, body,
                string.IsNullOrWhiteSpace(reason) ? "Delegate reported failed." : reason);
        }

        if (LooksLikeAQuestion(body))
            return (AgentTaskStatus.Blocked, AgentTaskReportEvidence.QuestionHeuristic, body, null);

        var sessionLive = task.AgentSessionId is Guid sid && await IsSessionLiveAsync(db, sid, ct);
        if (sessionLive)
        {
            if (task.ReportNudgedAt is null)
            {
                await NudgeForClosingLineAsync(services, db, task, turn, now, ct);
                return null;
            }

            // CARD-0248: "asked once and it ended ANOTHER turn unmarked" — enforced literally.
            // Legacy carve-out: pre-CARD-0248 nudges with both new columns null skip these gates
            // so in-flight nudges at deploy cannot strand. Decays to dead code later.
            var isLegacyNudge = task.ReportNudgedSequence is null && task.ReportNudgeMessageId is null;
            if (!isLegacyNudge)
            {
                if (turn.Boundary is not { } boundary)
                {
                    _logger.LogWarning(
                        "Task {ShortId}: settle-anyway reached with no boundary identity — staying Working",
                        DelegationReportFormatter.Short(task.Id));
                    return null;
                }

                var sentAt = await LoadNudgeSentAtAsync(db, task, ct);
                if (sentAt is not DateTime deliveredAt)
                    return null; // the ask has not happened yet
                if (boundary.Sequence <= task.ReportNudgedSequence
                    || boundary.CreatedAt <= deliveredAt)
                    return null; // same boundary, or one that predates the ask
                if (turn.FinalMessageMissing
                    && now < deliveredAt + TimeSpan.FromSeconds(_settings.ReportNudgeResponseSeconds))
                    return null; // text-less boundary: give the answer time to land
            }
        }

        var evidence = turn.FinalMessageMissing
            ? AgentTaskReportEvidence.FinalMessageMissing
            : AgentTaskReportEvidence.UnmarkedAfterNudge;
        return (AgentTaskStatus.Succeeded, evidence, body, null);
    }

    /// <summary>
    /// CARD-0302: Check-role status is "did the interpreter finish a reading", never "what did it
    /// think of the delegate." <c>failed</c> is the interpreter saying it could not produce one.
    /// A <c>blocked</c> token is the wrong vocabulary (Exempt, not Marked) so surfaces that print
    /// <c>report=</c> do not claim a successful use of the generic contract.
    /// </summary>
    private static (AgentTaskStatus Status, AgentTaskReportEvidence Evidence, string Body, string? FailureReason)
        ClassifyCheckReport(string body, string verdict)
    {
        if (verdict == "failed")
        {
            var reason = FirstLine(body);
            return (AgentTaskStatus.Failed, AgentTaskReportEvidence.Marked, body,
                string.IsNullOrWhiteSpace(reason) ? "Delegate reported failed." : reason);
        }

        if (verdict == "done")
            return (AgentTaskStatus.Succeeded, AgentTaskReportEvidence.Marked, body, null);

        return (AgentTaskStatus.Succeeded, AgentTaskReportEvidence.Exempt, body, null);
    }

    /// <summary>
    /// SentAt of the queued nudge row, or null when the id was never recorded / the row is gone /
    /// it has not been typed yet (CARD-0248).
    /// </summary>
    private static async Task<DateTime?> LoadNudgeSentAtAsync(
        AppDbContext db, AgentTask task, CancellationToken ct)
    {
        if (task.ReportNudgeMessageId is not Guid messageId)
            return null;
        return await db.SessionQueuedMessages.AsNoTracking()
            .Where(m => m.Id == messageId)
            .Select(m => m.SentAt)
            .FirstOrDefaultAsync(ct);
    }

    private static string FirstLine(string text)
    {
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }
        return string.Empty;
    }

    private async Task<bool> IsSessionLiveAsync(AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var session = await db.AgentSessions.AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => new { s.Status, s.EndedAt })
            .FirstOrDefaultAsync(ct);
        if (session is null || session.EndedAt is not null)
            return false;
        return session.Status is SessionStatus.Created or SessionStatus.Starting or SessionStatus.Running;
    }

    private async Task NudgeForClosingLineAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, TurnOutcome turn, DateTime now,
        CancellationToken ct)
    {
        if (task.AgentSessionId is not Guid sessionId)
            return;

        var shortId = DelegationReportFormatter.Short(task.Id);
        var done = DelegationReportFormatter.ReportToken(task.Id, "done");
        var body =
            $"{DelegationReportFormatter.TaskMarker(task.Id)} Your turn ended without the closing report line. "
            + $"If the work is finished, send the report now, ending with `{done}` (or `blocked` / `failed`). "
            + "If it is not finished, continue.";

        task.ReportNudgedAt = now;
        // Record the boundary first, before enqueue: if enqueue throws, the recorded nudge still
        // prevents a 5 s re-nudge storm. ReportNudgeMessageId stays null in that case, which
        // settle-anyway reads as "never delivered" (CARD-0248).
        if (turn.Boundary is { } boundary)
            task.ReportNudgedSequence = boundary.Sequence;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(
            task.Id, AgentTaskEventType.Warning,
            "Turn ended without the closing report line — asked once for `[antiphon-report:"
            + $"{shortId} done|blocked|failed]`. The task stays Working.",
            now));
        await db.SaveChangesAsync(ct);

        var queue = services.GetRequiredService<SessionMessageQueueService>();
        Guid? createdId = null;
        try
        {
            await queue.EnqueueAsync(
                sessionId, body, MessageSendMode.WhenIdle, ct, QueuedMessageOrigin.Delegation,
                onCreated: id => createdId = id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // CARD-0336: onCreated can fire before a later GetQueue/Publish throw. The row is
            // real; settle-anyway needs its id.
            _logger.LogWarning(
                ex, "Task {ShortId}: closing-line nudge enqueue failed after recording the ask", shortId);
        }

        try
        {
            // Isolated save so a caller's still-dirty tracker cannot swallow the id
            // (CARD-0319 leftover, CARD-0336).
            await StampNudgeRowAsync(db, task, createdId, now, turn.Boundary?.Sequence, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Task {ShortId}: could not stamp ReportNudgeMessageId after the closing-line enqueue", shortId);
        }

        _logger.LogInformation(
            "Task {ShortId}: nudged once for the closing report line", shortId);

        await EnqueueParentWaitingNoteAsync(services, task, shortId, ct);
    }

    private async Task StampNudgeRowAsync(
        AppDbContext callerDb, AgentTask task, Guid? createdId, DateTime now, long? boundarySequence,
        CancellationToken ct)
    {
        var options = callerDb.GetService<IDbContextOptions>() as DbContextOptions<AppDbContext>
            ?? throw new InvalidOperationException(
                "AppDbContext is not configured with DbContextOptions<AppDbContext>.");
        await using var isolated = new AppDbContext(options);
        var row = await isolated.AgentTasks.SingleAsync(t => t.Id == task.Id, ct);
        row.ReportNudgedAt ??= now;
        if (boundarySequence is long seq)
            row.ReportNudgedSequence ??= seq;
        if (createdId is null)
        {
            createdId = await isolated.SessionQueuedMessages.AsNoTracking()
                .Where(m => m.AgentSessionId == task.AgentSessionId
                    && m.Origin == QueuedMessageOrigin.Delegation)
                .OrderByDescending(m => m.CreatedAt)
                .Select(m => (Guid?)m.Id)
                .FirstOrDefaultAsync(ct);
        }

        if (createdId is Guid messageId)
        {
            row.ReportNudgeMessageId = messageId;
            task.ReportNudgeMessageId = messageId;
        }

        row.ConcurrencyToken = Guid.NewGuid();
        task.ReportNudgedAt = row.ReportNudgedAt;
        task.ReportNudgedSequence = row.ReportNudgedSequence;
        await isolated.SaveChangesAsync(ct);
    }

    /// <summary>
    /// CARD-0294 S3: the parent hears the wait at the first child-nudge (T+0), not only when
    /// the sweep Blocks five minutes later. Distinct ConversationKey so a wait-signal cannot
    /// batch into a sibling's completion note.
    /// </summary>
    private async Task EnqueueParentWaitingNoteAsync(
        IServiceProvider services, AgentTask task, string shortId, CancellationToken ct)
    {
        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return;

        var done = DelegationReportFormatter.ReportToken(task.Id, "done");
        var body =
            $"[task {shortId} waiting] Child ended a turn without the closing report line; asked once for "
            + $"`{done}` (or `blocked` / `failed`). Session is idle. Reply after it Blocks, or Refine now.";
        try
        {
            var queue = services.GetRequiredService<SessionMessageQueueService>();
            await queue.EnqueueAsync(
                parentSession, body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Delegation, $"task-wait:{task.Id:N}",
                task.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not deliver task {ShortId} waiting note to parent session {SessionId}",
                shortId, parentSession);
        }
    }

    /// <summary>
    /// CARD-0294 S1: the one closing-line nudge was issued and the session stayed idle on that
    /// same unmarked boundary past <see cref="DelegationSettings.UnmarkedWaitingMinutes"/>.
    /// Blocks; does not Succeed. Does not go through <see cref="OnTurnEndAsync"/> — that
    /// method's same-boundary gate would no-op or, on a later boundary, settle-anyway.
    /// </summary>
    public async Task BlockUnmarkedWaitingAsync(Guid sessionId, CancellationToken ct)
    {
        if (_settings.UnmarkedWaitingMinutes <= 0)
            return;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<AppDbContext>();

        var task = await db.AgentTasks.FirstOrDefaultAsync(
            t => t.AgentSessionId == sessionId
                && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working),
            ct);
        if (task is null || task.Role == AgentTaskRole.Check)
            return;
        if (task.ReportNudgedAt is not DateTime nudgedAt)
            return;

        var now = UtcNow();
        if (now - nudgedAt < TimeSpan.FromMinutes(_settings.UnmarkedWaitingMinutes))
            return;
        if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
            return;

        var tokenTexts = await db.TranscriptEntries.AsNoTracking()
            .Where(e => e.AgentSessionId == sessionId
                && e.Kind == TranscriptKinds.AssistantText
                && e.Text != null
                && e.Text.Contains("[antiphon-report:"))
            .Select(e => e.Text!)
            .ToListAsync(ct);
        if (tokenTexts.Any(text => DelegationReportFormatter.TryFindReportToken(task.Id, text, out _)))
            return;

        var turn = await ExtractMarkedTurnAsync(db, sessionId, task, ct);
        if (turn.Interrupted is not null
            || turn.ApiErrorStub is not null
            || turn.DeferredForFinalMessage
            || turn.UncorrelatedReport)
            return;
        if (turn.Report is null && turn.Boundary is null)
            return;
        if (task.ReportNudgedSequence is long nudgedSeq
            && turn.Boundary is { } boundary
            && boundary.Sequence != nudgedSeq)
            return;

        var body = turn.Report ?? string.Empty;
        var shortId = DelegationReportFormatter.Short(task.Id);
        task.Status = AgentTaskStatus.Blocked;
        task.ReportEvidence = AgentTaskReportEvidence.UnmarkedWaiting;
        task.Result = body;
        task.CompletedAt = now;
        task.ConcurrencyToken = Guid.NewGuid();
        db.AgentTaskEvents.Add(NewEvent(
            task.Id, AgentTaskEventType.Blocked,
            "Turn ended without `[antiphon-report:…]`; asked once and the session stayed idle. Waiting on a human.",
            now));
        await db.SaveChangesAsync(ct);

        if (task.ReportNudgeMessageId is Guid nudgeId)
        {
            try
            {
                var queue = services.GetRequiredService<SessionMessageQueueService>();
                await queue.CancelAsync(sessionId, nudgeId, ct);
            }
            catch (Exception ex) when (ex is NotFoundException or ConflictException)
            {
                _logger.LogDebug(
                    ex, "Task {ShortId}: closing-line nudge {MessageId} was already gone at Block",
                    shortId, nudgeId);
            }
        }

        var (gitHeader, _) = await TryDescribeGitAsync(services, db, task, body, now, ct);
        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Task {ShortId} blocked as UnmarkedWaiting after {Minutes}m idle past the closing-line nudge",
            shortId, _settings.UnmarkedWaitingMinutes);

        await DeliverToParentAsync(task, body, ct, git: gitHeader);
        await PublishAsync(task, ct);
    }

    private static string DescribeReported(
        int chars, TurnOutcome turn, AgentTaskReportEvidence evidence, string? markedVerdict)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("Delegate reported ").Append(chars.ToString("N0")).Append(" characters");
        if (turn.NarrationDiscardedChars > 0)
        {
            sb.Append(" (final message; ")
              .Append(turn.NarrationDiscardedChars.ToString("N0"))
              .Append(" characters of mid-turn narration not included)");
        }

        if (evidence == AgentTaskReportEvidence.Marked && markedVerdict is { Length: > 0 })
            sb.Append(" (verdict: ").Append(markedVerdict).Append(").");
        else if (evidence == AgentTaskReportEvidence.UnmarkedAfterNudge)
            sb.Append(" (no closing line; settled after one nudge).");
        else
            sb.Append('.');
        return sb.ToString();
    }

    private static string? ReportEvidenceHeader(AgentTaskReportEvidence evidence) => evidence switch
    {
        AgentTaskReportEvidence.Marked => "marked",
        AgentTaskReportEvidence.UnmarkedAfterNudge
            or AgentTaskReportEvidence.QuestionHeuristic
            or AgentTaskReportEvidence.FinalMessageMissing
            or AgentTaskReportEvidence.UnmarkedWaiting => "unmarked",
        AgentTaskReportEvidence.Exempt => "exempt",
        _ => null,
    };

    private async Task<(string? Header, string? Warning)> TryDescribeGitAsync(
        IServiceProvider services, AppDbContext db, AgentTask task, string report, DateTime now,
        CancellationToken ct)
    {
        try
        {
            var git = services.GetService<GitWorkspaceService>();
            if (git is null)
                return (null, null);

            if (task.Workspace is WorkspaceMode.Shared or WorkspaceMode.ReadOnly)
            {
                var repository = task.RepoPath ?? task.WorkingDirectory;
                if (string.IsNullOrWhiteSpace(repository) || !Directory.Exists(repository))
                    return ("unattributable", null);

                var reportedPaths = ExtractReportedRepositoryPaths(report, repository);
                if (reportedPaths.Count == 0)
                    return ("unattributable", null);

                var dirtyPaths = await git.GetDirtyPathsAsync(repository, reportedPaths, ct);
                if (dirtyPaths is null)
                    return ("unattributable", null);
                if (dirtyPaths.Count == 0)
                    return ("landed", null);

                var detail = $"Report names {dirtyPaths.Count} file(s) still uncommitted in the shared checkout: "
                    + string.Join(", ", dirtyPaths) + ".";
                var sharedWarning =
                    $"The report names {dirtyPaths.Count} file(s) that are still uncommitted in the shared checkout "
                    + "— the work has not landed. Commit before building on it.";
                db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Warning, detail, now));
                return ($"uncommitted:{dirtyPaths.Count}", sharedWarning);
            }

            var directory = task.WorktreePath is { Length: > 0 } wt && Directory.Exists(wt)
                ? wt
                : task.RepoPath ?? task.WorkingDirectory;
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return (DelegationGitFacts.ResolveBase(task) is null ? "base unknown" : null, null);

            var gitBase = DelegationGitFacts.ResolveBase(task);
            if (string.IsNullOrWhiteSpace(gitBase))
                return ("base unknown", null);

            var (commits, files) = await git.CountRangeAsync(directory, gitBase, "HEAD", ct);
            if (commits is null)
                return ("base unknown", null);

            var header = DelegationGitFacts.FormatHeader(commits.Value, files ?? 0);
            string? warning = null;
            if (task.Status == AgentTaskStatus.Succeeded
                && task.Workspace == WorkspaceMode.Worktree
                && DelegationGitFacts.IsCodeProducing(task.Role)
                && commits.Value == 0
                && !DelegationGitFacts.MentionsNoChanges(report))
            {
                var branch = task.WorktreeBranch ?? "the task branch";
                warning =
                    $"This Worktree task in a code role produced no commits on `{branch}`. Verify before merging.";
                db.AgentTaskEvents.Add(NewEvent(task.Id, AgentTaskEventType.Warning, warning, now));
            }

            return (header, warning);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex, "Could not gather git facts for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
            return (null, null);
        }
    }

    private static readonly Regex ReportedBacktickPathPattern = new(
        "`(?<path>[^`\\r\\n]+)`", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReportedWindowsPathPattern = new(
        "(?<![A-Za-z0-9])(?<path>[A-Za-z]:[\\\\/][^\\s`\\\"'<>|]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ReportedRelativePathPattern = new(
        "(?<![A-Za-z0-9_.-])(?<path>(?:[A-Za-z0-9_.-]+/)+[A-Za-z0-9_.-]+\\.[A-Za-z0-9]+)(?![A-Za-z0-9_.-])",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Paths the delegate named in its own report, normalized to repo-relative forward-slash form.
    /// This is intentionally a small mechanical extractor, not an attempt to interpret prose: the
    /// settlement check only asks git whether these claimed paths are dirty, never reads a diff.
    /// </summary>
    internal static IReadOnlyList<string> ExtractReportedRepositoryPaths(string report, string repoPath)
    {
        if (string.IsNullOrWhiteSpace(report) || string.IsNullOrWhiteSpace(repoPath))
            return [];

        var paths = new List<string>(20);
        foreach (var pattern in new[]
                 {
                     ReportedBacktickPathPattern,
                     ReportedWindowsPathPattern,
                     ReportedRelativePathPattern,
                 })
        {
            foreach (Match match in pattern.Matches(report))
            {
                if (paths.Count == 20)
                    return paths;

                var candidate = match.Groups["path"].Value.Trim()
                    .Trim('`', '\"', '\'')
                    .TrimEnd('.', ',', ';', ':', '!', '?', ')', ']', '}');
                if (!LooksLikePath(candidate) || !TryMakeRepoRelative(candidate, repoPath, out var relative))
                    continue;
                if (!paths.Contains(relative, StringComparer.OrdinalIgnoreCase))
                    paths.Add(relative);
            }
        }

        return paths;
    }

    private static bool LooksLikePath(string candidate) =>
        candidate.Contains('/') || candidate.Contains('\\') || Path.HasExtension(candidate);

    private static bool TryMakeRepoRelative(string candidate, string repoPath, out string relative)
    {
        relative = string.Empty;
        try
        {
            var root = Path.GetFullPath(repoPath);
            var full = Path.IsPathRooted(candidate)
                ? Path.GetFullPath(candidate)
                : Path.GetFullPath(candidate, root);
            var fromRoot = Path.GetRelativePath(root, full);
            if (Path.IsPathRooted(fromRoot)
                || fromRoot.Equals("..", StringComparison.Ordinal)
                || fromRoot.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || fromRoot.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
            {
                return false;
            }

            relative = fromRoot.Replace('\\', '/');
            return relative.Length > 0 && relative != ".";
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    /// <summary>
    /// A delegate that ends its turn asking something needs an ANSWER, not a retry. Deliberately
    /// conservative: only a question mark in the last couple of lines counts, so a report that
    /// merely mentions a question mid-text still reads as finished.
    /// </summary>
    internal static bool LooksLikeAQuestion(string report) =>
        BlockedQuestion.TryExtract(report, out _, out _);

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
