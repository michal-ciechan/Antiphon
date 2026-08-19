using System.Text;
using System.Text.RegularExpressions;
using Antiphon.Agents.Pty;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Runs one scheduled check-in and delivers it to the delegate's CALLER (CARD-0047 §1.3-§1.4).
///
/// <para>It gathers facts through <see cref="DelegateCheckProbe"/> — which cannot write — and
/// delivers a note through the caller's own message queue. It never touches the delegate: not its
/// queue, not its session, not its transcript, not its status. That is the whole read-only
/// guarantee, and the only reason a check is safe to run against work in flight.</para>
///
/// <para><b>A check cannot settle anything, structurally.</b> Settlement of the DELEGATE's task
/// reads the DELEGATE's transcript, and nothing here writes there. Settlement of the PARENT's own
/// task (when the caller is itself a delegate) is gated on the parent's task marker appearing in
/// the prompt that opened the turn — and a check note is an ordinary unmarked prompt, so the
/// parent's reaction turn fails that gate. This is the same shape completion notes already have,
/// and it is load-bearing: check notes must NEVER be classified as housekeeping prompts in the
/// walk-back, or the walk-back would skip past them to the parent's marked brief and let the
/// parent settle its own task on its reaction to a note about someone else's.</para>
/// </summary>
public sealed class AgentTaskCheckService
{
    /// <summary>How much of the digest the task's timeline entry keeps.</summary>
    private const int EventDetailChars = 900;

    /// <summary>
    /// How much of the interpreter's reading the timeline entry keeps (CARD-0035 slice 5). Its own
    /// budget, deliberately: sharing the digest's 900 would mean a long reading ate the evidence it
    /// is a reading OF, and the digest is the part that is reviewable.
    /// </summary>
    private const int InterpretationDetailChars = 600;

    /// <summary>
    /// The line a <see cref="AgentTaskEventType.Check"/> event's detail puts above the interpreter's
    /// reading, and the one below it that opens the digest (CARD-0035 slice 5).
    ///
    /// <para>They are a parsing contract, not decoration: <c>AttentionService</c> reads the reading
    /// back out of the stored detail with <see cref="TryReadInterpretation"/>, so the boundary has to
    /// be findable in text nobody controls. Both headings are scrubbed OUT of the reading before it
    /// is written, so the first <see cref="DigestHeading"/> after the reading is always the real one.</para>
    /// </summary>
    public const string ReadingHeading = "READING (check interpreter):";

    /// <inheritdoc cref="ReadingHeading"/>
    public const string DigestHeading = "DIGEST:";

    private readonly AppDbContext _db;
    private readonly DelegateCheckProbe _probe;
    private readonly SessionMessageQueueService _queue;
    private readonly DelegationSettings _settings;
    private readonly IEventBus _eventBus;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AgentTaskCheckService> _logger;
    private readonly PtyDeliveryProfile? _ptyProfile;
    private readonly CheckInterpreterProvisioner? _interpreter;
    private readonly IAlertService? _alerts;

    /// <summary>
    /// A burst of due checks against a dead specialist is one outage, not one incident per
    /// check (CARD-0079). The window is the "one minute" the plan names.
    /// </summary>
    private static readonly TimeSpan InterpreterUnavailableDedupWindow = TimeSpan.FromMinutes(1);

    public AgentTaskCheckService(
        AppDbContext db,
        DelegateCheckProbe probe,
        SessionMessageQueueService queue,
        IOptions<DelegationSettings> settings,
        IEventBus eventBus,
        TimeProvider timeProvider,
        ILogger<AgentTaskCheckService> logger,
        PtyDeliveryProfile? ptyProfile = null,
        // Optional, and its ABSENCE is not a degraded interpretation — it is a host that never wired
        // the specialist in at all (every harness that predates CARD-0047 slice 4). Such a host gets
        // exactly the slice-3 note: digest, no prefix, no interpretation task.
        CheckInterpreterProvisioner? interpreter = null,
        // Optional so a harness that wires no alerting still delivers the digest. The incident is
        // the record; the alert is what reaches someone.
        IAlertService? alerts = null)
    {
        _db = db;
        _probe = probe;
        _queue = queue;
        _settings = settings.Value;
        _eventBus = eventBus;
        _timeProvider = timeProvider;
        _logger = logger;
        _ptyProfile = ptyProfile;
        _interpreter = interpreter;
        _alerts = alerts;
    }

    /// <summary>What one check did — for the worker's logging and for the tests.</summary>
    public enum CheckOutcome
    {
        /// <summary>The task row is gone.</summary>
        TaskMissing = 0,

        /// <summary>It settled between the claim and now; the completion note is the caller's answer.</summary>
        AlreadySettled = 1,

        /// <summary>There is no caller session to deliver to.</summary>
        NoRecipient = 2,

        Delivered = 3,

        /// <summary>Facts were gathered but the note could not be queued (transport).</summary>
        DeliveryFailed = 4,
    }

    public async Task<CheckOutcome> RunCheckAsync(Guid taskId, CancellationToken ct)
    {
        var task = await _db.AgentTasks.FirstOrDefaultAsync(t => t.Id == taskId, ct);
        if (task is null)
            return CheckOutcome.TaskMissing;

        // Claimed while running, settled before we got here. The completion note is already on its
        // way and says everything a check would; a second note would only be noise.
        if (AgentTaskService.IsSettled(task.Status))
        {
            _logger.LogDebug(
                "Check on task {ShortId} skipped — it settled between the claim and the run",
                DelegationReportFormatter.Short(task.Id));
            return CheckOutcome.AlreadySettled;
        }

        if (task.ReplyTo != AgentTaskReplyTo.Session || task.ParentSessionId is not Guid parentSession)
            return CheckOutcome.NoRecipient;

        var facts = await _probe.GatherAsync(task, ct);
        var digest = DelegateCheckProbe.RenderDigest(facts);

        // The ONE new path (CARD-0047 slice 4C). Everything it can return other than an
        // interpretation is today's note plus a prefix naming why — see Interpretation.
        var interpretation = await InterpretAsync(task, facts, digest, ct);
        var body = BuildNote(task, facts, digest, interpretation.Text, interpretation.DegradedReason);

        try
        {
            await _queue.EnqueueAsync(
                parentSession, body, MessageSendMode.WhenIdle, ct,
                QueuedMessageOrigin.Check, ConversationKey(task.Id));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A check is advisory. A caller session that is momentarily unreachable must never turn
            // an observation into an error path — the schedule has already moved on.
            _logger.LogWarning(
                ex, "Check note for task {ShortId} could not be queued to session {SessionId}",
                DelegationReportFormatter.Short(task.Id), parentSession);
            return CheckOutcome.DeliveryFailed;
        }

        // The timeline keeps the DIGEST whatever the note carried — it is the evidence, and an
        // interpretation of facts nobody recorded is not reviewable. What the interpreter cost is
        // recorded HERE as well as on the interpretation task's own row, so the question "what did
        // watching this task cost" is answerable from the timeline without a join (§1.6).
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = task.Id,
            Type = AgentTaskEventType.Check,
            Detail = ComposeEventDetail(interpretation.Text, interpretation.EventLine, digest),
            At = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        _logger.LogInformation(
            "Check #{Number} on task {ShortId} delivered to session {SessionId}",
            facts.Task.CheckNumber, DelegationReportFormatter.Short(task.Id), parentSession);
        return CheckOutcome.Delivered;
    }

    /// <summary>One conversation per task, so a check never coalesces with anything else.</summary>
    public static string ConversationKey(Guid taskId) => $"check:{taskId:N}";

    /// <summary>
    /// What a <see cref="AgentTaskEventType.Check"/> event stores (CARD-0035 slice 5): the cost line,
    /// then the interpreter's reading, then the digest.
    ///
    /// <para><b>Why the reading is stored at all.</b> The specialist's 3-5 lines were built for
    /// exactly the altitude a human reads a stuck task at, and until now the system threw the best
    /// explanation it produces away: the reading reached the caller's note (a message body nothing
    /// can query) and the interpretation task's own <c>Result</c> row (correlated to the checked task
    /// by TITLE TEXT, no FK), so no surface could answer "what did the interpreter make of THIS
    /// task". Storing it here needs no table and no key — the event already belongs to the task.</para>
    ///
    /// <para><b>The digest keeps its own budget and stays below the reading.</b> The reading is a
    /// judgement; the digest is the evidence for it, and a reader who distrusts the first wants the
    /// second intact rather than squeezed. Truncation therefore only ever eats the digest's tail,
    /// exactly as it did before this slice.</para>
    /// </summary>
    internal static string ComposeEventDetail(string? reading, string? eventLine, string digest)
    {
        var body = digest.Length <= EventDetailChars ? digest : digest[..EventDetailChars] + "…";
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(eventLine))
            parts.Add(eventLine!.Trim());

        if (Fit(reading) is { } read)
        {
            parts.Add($"{ReadingHeading}\n{read}");
            // Only labelled when there is something above it to be told apart FROM. A degraded check
            // stores exactly what it stored before this slice, which is what makes the change
            // retroactively harmless: every pre-slice event still parses as digest-only.
            parts.Add($"{DigestHeading}\n{body}");
        }
        else
        {
            parts.Add(body);
        }

        return string.Join("\n\n", parts).ReplaceLineEndings("\n");
    }

    /// <summary>
    /// The interpreter's reading, read back out of a stored <see cref="AgentTaskEventType.Check"/>
    /// detail — null when that check ran before this slice, or degraded to the digest.
    ///
    /// <para>Null is the honest answer for both, and the caller falls back to the digest tail rather
    /// than showing an empty explanation column.</para>
    /// </summary>
    public static string? TryReadInterpretation(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return null;

        var text = detail.ReplaceLineEndings("\n");
        var start = text.IndexOf(ReadingHeading, StringComparison.Ordinal);
        if (start < 0)
            return null;

        start += ReadingHeading.Length;
        var end = text.IndexOf($"\n{DigestHeading}", start, StringComparison.Ordinal);
        var reading = (end < 0 ? text[start..] : text[start..end]).Trim();
        return reading.Length == 0 ? null : reading;
    }

    /// <summary>
    /// The reading, trimmed to its own budget and with both headings scrubbed out of it — a reading
    /// that happened to contain the digest heading would otherwise cut its own read-back short.
    /// </summary>
    private static string? Fit(string? reading)
    {
        if (string.IsNullOrWhiteSpace(reading))
            return null;

        var text = reading.ReplaceLineEndings("\n").Trim()
            .Replace(ReadingHeading, "READING:", StringComparison.Ordinal)
            .Replace(DigestHeading, "digest:", StringComparison.Ordinal);
        return text.Length <= InterpretationDetailChars
            ? text
            : text[..InterpretationDetailChars] + "…";
    }

    /// <summary>How often the wait re-reads the interpretation task's row (CARD-0047 §1.1).</summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <param name="Text">The specialist's reading, or null — in which case the digest ships.</param>
    /// <param name="DegradedReason">
    /// Rendered into the note as <c>(unverified digest — &lt;reason&gt;)</c>. Null AND a null
    /// <paramref name="Text"/> means the specialist is not wired into this host at all, which
    /// produces the pre-slice-4 note byte for byte.
    /// </param>
    /// <param name="EventLine">The interpreter line for the checked task's timeline, if one ran.</param>
    private readonly record struct Interpretation(string? Text, string? DegradedReason, string? EventLine)
    {
        public static Interpretation NotWiredIn { get; } = new(null, null, null);

        public static Interpretation Degraded(string reason, string? eventLine = null) =>
            new(null, reason, eventLine);
    }

    /// <summary>
    /// Hand this check's bundle to the standing specialist and wait, briefly, for its reading
    /// (CARD-0047 slice 4 amendment §1.1).
    ///
    /// <para><b>Every path out of here that is not a successful interpretation is today's digest
    /// with a prefix.</b> That is the contract of the whole slice, not a convenience: the
    /// deterministic digest ships and always delivers, and the specialist is garnish on top of it.
    /// Disabled, unprovisioned, busy, uncreatable, slow, failed, or answering with nothing — the
    /// caller still hears about the delegate, and hears why the reading is missing.</para>
    ///
    /// <para>The work reaches the specialist as a pinned <see cref="AgentTaskRole.Check"/> task and
    /// the answer comes back on that task's own <see cref="AgentTask.Result"/>, through the settlement
    /// path — so delivery confirmation is the TRANSCRIPT (settlement only fires on a marked turn that
    /// actually happened), never the message queue's Sent flag, which CARD-0055 proved does not mean
    /// what it says.</para>
    /// </summary>
    private async Task<Interpretation> InterpretAsync(
        AgentTask task, DelegateCheckProbe.CheckFacts facts, string digest, CancellationToken ct)
    {
        if (_interpreter is null || !_settings.CheckInterpreterEnabled)
            return Interpretation.NotWiredIn;

        Domain.Entities.Agent? specialist;
        try
        {
            specialist = await _interpreter.EnsureAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not provision the check interpreter for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
            const string reason = "interpreter unavailable: could not be provisioned";
            await RaiseInterpreterUnavailableAsync(specialist: null, reason, ct);
            return Interpretation.Degraded(reason);
        }

        if (specialist is null)
            return Interpretation.NotWiredIn;

        // Depth policy. There is ONE specialist and many delegates can come due together; past this
        // bound a check degrades IMMEDIATELY rather than waiting its full budget behind a pile.
        var backlog = await _db.AgentTasks.CountAsync(
            t => t.AgentId == specialist.Id
                && t.Role == AgentTaskRole.Check
                && (t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched
                    || t.Status == AgentTaskStatus.Working),
            ct);
        if (backlog >= Math.Max(1, _settings.CheckInterpreterMaxBacklog))
        {
            _logger.LogInformation(
                "Check on task {ShortId} degraded: {Backlog} interpretation(s) already pending",
                DelegationReportFormatter.Short(task.Id), backlog);
            return Interpretation.Degraded("interpreter busy");
        }

        AgentTask interpretation;
        try
        {
            interpretation = await CreateInterpretationTaskAsync(task, specialist, facts, digest, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not queue an interpretation for task {ShortId}",
                DelegationReportFormatter.Short(task.Id));
            const string reason = "interpreter unavailable: the interpretation could not be queued";
            await RaiseInterpreterUnavailableAsync(specialist, reason, ct);
            return Interpretation.Degraded(reason);
        }

        var shortId = DelegationReportFormatter.Short(interpretation.Id);
        var settled = await WaitForInterpretationAsync(interpretation.Id, ct);

        if (settled is null)
        {
            // Out of budget. Still Queued means it never reached the specialist and never will be
            // worth anything — cancel it. Already Dispatched means the spend is committed: let it
            // finish and settle onto its OWN row, where its late text is recorded and never
            // delivered as a second note about a check the caller has already read.
            await CancelIfStillQueuedAsync(interpretation.Id, ct);
            _logger.LogInformation(
                "Check on task {ShortId} degraded: interpretation {InterpretationId} did not settle "
                + "within {Seconds}s", DelegationReportFormatter.Short(task.Id), shortId,
                _settings.CheckInterpreterWaitSeconds);
            var timeoutReason =
                $"interpreter unavailable: no reading within {_settings.CheckInterpreterWaitSeconds}s";
            await RaiseInterpreterUnavailableAsync(specialist, timeoutReason, ct);
            return Interpretation.Degraded(
                timeoutReason,
                $"interpreter: task {shortId}, timed out");
        }

        var line = $"interpreter: task {shortId}, ${settled.CostUsd:0.0000}";

        if (settled.Status is AgentTaskStatus.Failed or AgentTaskStatus.Canceled)
        {
            const string failedReason = "interpreter unavailable: the interpretation failed";
            await RaiseInterpreterUnavailableAsync(specialist, failedReason, ct);
            return Interpretation.Degraded(failedReason, line);
        }

        if (string.IsNullOrWhiteSpace(settled.Result))
        {
            const string emptyReason = "interpreter unavailable: the interpretation was empty";
            await RaiseInterpreterUnavailableAsync(specialist, emptyReason, ct);
            return Interpretation.Degraded(emptyReason, line);
        }

        return new Interpretation(settled.Result, null, line);
    }

    /// <summary>
    /// The incident + alert behind CARD-0079 slice 2. Same shape as
    /// <c>AgentTaskReplyService.RecordUncorrelatedReportAsync</c>: write the timeline row, then
    /// optionally raise <see cref="IAlertService"/> — a host without alerting still records the
    /// incident. Observability must never be able to break the check; the digest still ships.
    /// </summary>
    internal static string InterpreterUnavailableDedupKey(Guid agentId) =>
        $"delegation:{AgentIncidentKind.CheckInterpreterUnavailable}:{agentId}";

    private async Task RaiseInterpreterUnavailableAsync(
        Domain.Entities.Agent? specialist, string reason, CancellationToken ct)
    {
        try
        {
            var agent = specialist;
            if (agent is null)
            {
                var slug = CheckInterpreterProvisioner.Slug(_settings);
                agent = await _db.Agents.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Slug == slug, ct);
            }

            if (agent is null)
                return;

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var windowStart = now - InterpreterUnavailableDedupWindow;
            var already = await _db.AgentIncidents.AnyAsync(
                i => i.AgentId == agent.Id
                    && i.Kind == AgentIncidentKind.CheckInterpreterUnavailable
                    && i.CreatedAt >= windowStart,
                ct);
            if (already)
                return;

            Guid? sessionId = Guid.TryParse(agent.PersistentSessionId, out var parsed)
                ? parsed
                : null;
            var message =
                $"Check interpreter '{agent.Slug}' could not read a check ({reason}). "
                + "The caller received the deterministic digest instead.";

            _db.AgentIncidents.Add(new AgentIncident
            {
                Id = Guid.NewGuid(),
                AgentId = agent.Id,
                SessionId = sessionId,
                Kind = AgentIncidentKind.CheckInterpreterUnavailable,
                Severity = AlertSeverity.Warning,
                Message = message,
                CreatedAt = now,
            });
            await _db.SaveChangesAsync(ct);

            if (_alerts is null)
                return;

            await _alerts.RaiseAsync(
                new AlertRaise(
                    AlertSeverity.Warning,
                    Source: "delegation",
                    Title: $"Check interpreter unavailable ({agent.Slug})",
                    Detail: message,
                    DedupKey: InterpreterUnavailableDedupKey(agent.Id),
                    AgentId: agent.Id,
                    SessionId: sessionId),
                ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Could not record a check-interpreter-unavailable incident");
        }
    }

    /// <summary>
    /// The interpretation task row, built directly rather than through <c>AgentTaskService.CreateAsync</c>:
    /// there is no delegate caller to authorise, no allowed-root to resolve, and no fan-out budget
    /// this should consume.
    ///
    /// <para>It is its OWN root (<c>RootTaskId = Id</c>, no parent, depth 0) so its cost sums into
    /// nobody's tree and the per-root ceiling keeps meaning "what the delegated work cost". Nesting
    /// it under the checked task was considered and rejected: it would need a role carve-out inside
    /// the budget query, and a carve-out inside a spending ceiling is the kind of exception that
    /// rots (§1.6).</para>
    ///
    /// <para><c>ReplyTo = None</c> is load-bearing three ways: no completion note is delivered
    /// anywhere, no check is armed on it, and the check sweep's filter never sees it.</para>
    /// </summary>
    private async Task<AgentTask> CreateInterpretationTaskAsync(
        AgentTask task, Domain.Entities.Agent specialist, DelegateCheckProbe.CheckFacts facts,
        string digest, CancellationToken ct)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var id = Guid.NewGuid();
        var interpretation = new AgentTask
        {
            Id = id,
            RootTaskId = id,
            ParentTaskId = null,
            ParentSessionId = null,
            Depth = 0,
            Title = CheckInterpretation.BuildTitle(task, facts.Task.CheckNumber),
            Goal = CheckInterpretation.BuildGoal(task, facts.Task.CheckNumber, digest),
            Kind = AgentTaskKind.Worker,
            Role = AgentTaskRole.Check,
            ModelLevel = AgentModelLevel.Low,
            Workspace = WorkspaceMode.Shared,
            WorkingDirectory = specialist.WorkingDirectory,
            AgentId = specialist.Id,
            AgentName = specialist.Name,
            // Not ephemeral: the ephemeral flag is what deletes an agent row when its task settles,
            // and deleting the standing specialist after one check would be the opposite of standing.
            Ephemeral = false,
            ReplyTo = AgentTaskReplyTo.None,
            Status = AgentTaskStatus.Queued,
            CreatedAt = now,
        };
        _db.AgentTasks.Add(interpretation);
        _db.AgentTaskEvents.Add(new AgentTaskEvent
        {
            Id = Guid.NewGuid(),
            AgentTaskId = id,
            Type = AgentTaskEventType.Created,
            ModelLevel = AgentModelLevel.Low,
            Detail = $"Interpretation of check #{facts.Task.CheckNumber} on task "
                + $"{DelegationReportFormatter.Short(task.Id)}.",
            At = now,
        });
        await _db.SaveChangesAsync(ct);
        return interpretation;
    }

    /// <summary>
    /// Poll the interpretation task's row until it settles or the budget runs out. Null means the
    /// budget ran out (or the row vanished) — the caller degrades.
    ///
    /// <para><c>Blocked</c> counts as an answer: it means the settlement path's question-detector
    /// read a trailing question mark in the specialist's prose, which is a plausible way for a
    /// perfectly good "AMBIGUOUS — the bundle does not say whether..." to end. The text is there;
    /// throwing it away over punctuation would be worse than delivering it.</para>
    /// </summary>
    private async Task<AgentTask?> WaitForInterpretationAsync(Guid interpretationId, CancellationToken ct)
    {
        var deadline = _timeProvider.GetUtcNow()
            + TimeSpan.FromSeconds(Math.Max(1, _settings.CheckInterpreterWaitSeconds));

        while (true)
        {
            // AsNoTracking on purpose: the dispatcher and the settlement path write this row from
            // OTHER scopes, and a tracked read would keep handing back the snapshot this context
            // added — the poll would then never see it settle.
            var row = await _db.AgentTasks.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == interpretationId, ct);
            if (row is null)
                return null;
            if (AgentTaskService.IsSettled(row.Status) || row.Status == AgentTaskStatus.Blocked)
                return row;

            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
                return null;

            await Task.Delay(remaining < PollInterval ? remaining : PollInterval, _timeProvider, ct);
        }
    }

    /// <summary>
    /// Withdraw an interpretation nobody will read — but only while it is still Queued. A Dispatched
    /// one has already been typed at the specialist, and cancelling that would stop a session
    /// mid-turn for a note that has already gone out.
    /// </summary>
    private async Task CancelIfStillQueuedAsync(Guid interpretationId, CancellationToken ct)
    {
        try
        {
            var now = _timeProvider.GetUtcNow().UtcDateTime;
            var rows = await _db.AgentTasks
                .Where(t => t.Id == interpretationId && t.Status == AgentTaskStatus.Queued)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(t => t.Status, AgentTaskStatus.Canceled)
                          .SetProperty(t => t.CompletedAt, now)
                          .SetProperty(t => t.FailureReason, "The check that asked for it stopped waiting.")
                          .SetProperty(t => t.ConcurrencyToken, Guid.NewGuid()),
                    ct);
            if (rows > 0)
            {
                _logger.LogDebug(
                    "Interpretation {ShortId} cancelled — it never left the queue",
                    DelegationReportFormatter.Short(interpretationId));
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Tidying, not correctness: an un-cancelled queued interpretation dispatches, answers
            // into its own row, and is never delivered. The note has already gone out either way.
            _logger.LogWarning(
                ex, "Could not cancel the timed-out interpretation {ShortId}",
                DelegationReportFormatter.Short(interpretationId));
        }
    }

    /// <summary>The prefix every check note starts with — nothing else in the system emits it.</summary>
    public const string HeaderPrefix = "[check ";

    /// <summary>
    /// First-line marker when the specialist could not be reached (CARD-0079). Rides the header
    /// so a skim of line 1 is enough; the <c>(unverified digest — …)</c> body line is unchanged.
    /// Not used for "interpreter busy" — that is load, not a dead specialist.
    /// </summary>
    public const string InterpreterDownMarker = "INTERPRETER DOWN";

    /// <summary>
    /// The note as the caller sees it. The first line has to be unmistakable at a glance: a check
    /// is an OBSERVATION about work still in flight, and a caller that read it as a completion
    /// would move on from a task that has not finished.
    ///
    /// <para>So the envelope is fixed: it opens with <see cref="HeaderPrefix"/>, never with
    /// <c>[task </c>, its status vocabulary is the session's (Running/idle/working) and never the
    /// completion vocabulary (done/failed), and it carries NO task marker of any task — see
    /// <see cref="ScrubTaskMarkers"/>, which matters because the transcript tail legitimately
    /// contains the delegate's own brief, marker and all.</para>
    /// </summary>
    /// <param name="interpretation">
    /// The specialist's reading, which REPLACES the digest in the note when there is one (the digest
    /// stays on the timeline). Null falls through to the digest, which is the guaranteed floor.
    /// </param>
    /// <param name="degradedReason">
    /// Why there is no interpretation, rendered as a prefix line under the header so the caller can
    /// tell an unread digest from a read one at a glance. Null when the specialist is not wired in
    /// at all, which is not a degradation — it is the pre-slice-4 note, unchanged.
    /// </param>
    internal string BuildNote(
        AgentTask task,
        DelegateCheckProbe.CheckFacts facts,
        string digest,
        string? interpretation = null,
        string? degradedReason = null)
    {
        var header = new StringBuilder();
        header.Append(HeaderPrefix)
              .Append(DelegationReportFormatter.Short(task.Id))
              .Append(" #").Append(facts.Task.CheckNumber).Append(']');

        var bits = new List<string>();
        if (degradedReason is { Length: > 0 } down
            && down.StartsWith("interpreter unavailable", StringComparison.Ordinal))
            bits.Add(InterpreterDownMarker);
        if (!string.IsNullOrWhiteSpace(task.Title))
            bits.Add(task.Title.Trim().ReplaceLineEndings(" "));
        bits.Add($"{FormatAge(facts.Task.Age)} elapsed (expected {facts.Task.ExpectedDurationMinutes}m)");
        bits.Add(facts.Session is { } session
            ? $"session {session.Status} · {(session.Working ? "working" : "idle")}"
            : "no session");
        if (task.NextCheckAt is null)
            bits.Add($"final check — the {_settings.CheckMaxCount}-check budget is spent");
        header.Append(' ').Append(string.Join(" · ", bits));

        // The body is the interpretation when there is one, the digest otherwise — and the degraded
        // prefix rides ABOVE the digest, never above an interpretation.
        var body = interpretation is { Length: > 0 } read
            ? read.Trim()
            : degradedReason is { Length: > 0 } reason
                ? $"(unverified digest — {reason})\n\n{digest}"
                : digest;

        var note = $"{header}\n\n{ScrubTaskMarkers(body)}".ReplaceLineEndings("\n");

        // A digest is a few KB and the pty it is typed into has a measured ceiling (CARD-0037). Fit
        // it the same way a report is fitted rather than letting every check trip the oversize
        // tripwire — the excerpt banner names where to read the rest.
        var ceiling = _ptyProfile?.Ceilings.ReplyInlineMaxChars ?? _settings.ReplyInlineMaxChars;
        if (note.Length <= ceiling)
            return note;

        // Floored: a pathological title (300 chars) against a small configured ceiling could
        // otherwise leave the body a negative budget, and the excerpt arithmetic would throw on the
        // one delivery that most needed to survive.
        var bodyBudget = Math.Max(400, ceiling - header.Length - 2);
        var (fitted, _) = DelegationReportFormatter.FitReport(
            ScrubTaskMarkers(body), task, _settings, bodyBudget);
        return $"{header}\n\n{fitted}".ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Strip every task marker from the body. The transcript tail quotes the delegate's brief, which
    /// OPENS with <c>[antiphon-task:xxxxxxxx]</c> — so a note that simply pasted the digest would
    /// carry a live-looking marker into someone else's session. Nothing downstream should have to
    /// reason about whose marker it was.
    /// </summary>
    internal static string ScrubTaskMarkers(string body) =>
        TaskMarkerPattern.Replace(body, "[task-marker removed]");

    private static readonly Regex TaskMarkerPattern = new(
        @"\[antiphon-task:[0-9a-fA-F]{8}\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static string FormatAge(TimeSpan? age)
    {
        if (age is not { } value || value < TimeSpan.Zero)
            return "unknown";
        return value.TotalHours >= 1
            ? $"{(int)value.TotalHours}h{value.Minutes:00}m"
            : $"{(int)value.TotalMinutes}m";
    }
}
