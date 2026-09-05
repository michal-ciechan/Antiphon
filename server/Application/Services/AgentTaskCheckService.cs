using System.Globalization;
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
    internal readonly record struct Supersession(bool Settled, AgentTaskStatus Status, DateTime SettledAt);

    /// <summary>
    /// How much of the digest the task's timeline entry keeps. Raised from 900 to 1800 with
    /// CARD-0089: dated/collapsed digest lines are longer, and <c>CardThreadService</c> shows the
    /// last 6 lines of this stored head — leaving 900 would move which section the card-thread
    /// tail lands in.
    /// </summary>
    private const int EventDetailChars = 1800;

    /// <summary>
    /// How much of the interpreter's reading the timeline entry keeps (CARD-0035 slice 5). Its own
    /// budget, deliberately: sharing the digest's 1800 would mean a long reading ate the evidence it
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
    private readonly SpecialistTaskRunner _runner;

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
        IAlertService? alerts = null,
        SpecialistTaskRunner? runner = null)
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
        _runner = runner ?? new SpecialistTaskRunner(db, timeProvider, logger, alerts);
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

        /// <summary>The task settled after capture; its digest was retained on the task timeline.</summary>
        SupersededBeforeDelivery = 5,
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

        // Interpreter window (CARD-0132): retain the digest on the task, but do not type an
        // inert check at the caller once its completion note is available.
        string? supersededBanner = null;
        var supersession = await EvaluateAsync(_db, task.Id, ct);
        var suppress = false;
        if (supersession is { Settled: true } settled)
        {
            supersededBanner = SupersededBanner(settled.Status, settled.SettledAt, facts.At);
            suppress = await WaitForCompletionNoteAsync(parentSession, task.RootTaskId, ct);
        }

        var body = BuildNote(
            task, facts, digest, interpretation.Text, interpretation.DegradedReason, supersededBanner);

        if (!suppress)
        {
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
            Detail = ComposeEventDetail(
                interpretation.Text, interpretation.EventLine,
                supersededBanner is null ? digest : $"{supersededBanner}\n\n{digest}"),
            At = _timeProvider.GetUtcNow().UtcDateTime,
        });
        await _db.SaveChangesAsync(ct);

        await _eventBus.PublishToAllAsync(
            "AgentTaskChanged", new { taskId = task.Id, rootId = task.RootTaskId }, ct);
        _logger.LogInformation(
            "Check #{Number} on task {ShortId} {Outcome} for session {SessionId}",
            facts.Task.CheckNumber, DelegationReportFormatter.Short(task.Id),
            suppress ? "suppressed" : "delivered", parentSession);
        return suppress ? CheckOutcome.SupersededBeforeDelivery : CheckOutcome.Delivered;
    }

    /// <summary>Returns the current settled state for every check-delivery race window.</summary>
    internal static async Task<Supersession?> EvaluateAsync(AppDbContext db, Guid taskId, CancellationToken ct)
    {
        var task = await db.AgentTasks.AsNoTracking()
            .Where(t => t.Id == taskId)
            .Select(t => new { t.Status, t.CompletedAt })
            .FirstOrDefaultAsync(ct);
        if (task is null || !AgentTaskService.IsSettled(task.Status))
            return null;

        return new Supersession(true, task.Status, task.CompletedAt ?? DateTime.UtcNow);
    }

    internal static Task<bool> HasCompletionNoteAsync(
        AppDbContext db, Guid parentSessionId, Guid rootTaskId, CancellationToken ct) =>
        db.SessionQueuedMessages.AsNoTracking().AnyAsync(
            m => m.AgentSessionId == parentSessionId
                && m.Origin == QueuedMessageOrigin.Delegation
                && m.ConversationKey == $"task:{rootTaskId:N}"
                && m.Status != QueuedMessageStatus.Canceled, ct);

    private async Task<bool> WaitForCompletionNoteAsync(Guid parentSessionId, Guid rootTaskId, CancellationToken ct)
    {
        var deadline = _timeProvider.GetUtcNow() + TimeSpan.FromSeconds(Math.Max(0, _settings.CompletionNoteGraceSeconds));
        do
        {
            if (await HasCompletionNoteAsync(_db, parentSessionId, rootTaskId, ct))
                return true;
            if (_timeProvider.GetUtcNow() >= deadline)
                return false;
            await Task.Delay(TimeSpan.FromMilliseconds(100), _timeProvider, ct);
        } while (true);
    }

    /// <summary>One conversation per task, so a check never coalesces with anything else.</summary>
    public static string ConversationKey(Guid taskId) => $"check:{taskId:N}";

    /// <summary>Inverse of <see cref="ConversationKey"/> — the format has one definition.</summary>
    public static bool TryParseCheckConversationKey(string? conversationKey, out Guid taskId)
    {
        taskId = default;
        const string prefix = "check:";
        if (conversationKey is null || conversationKey.Length != prefix.Length + 32)
            return false;
        if (!conversationKey.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        return Guid.TryParseExact(conversationKey.AsSpan(prefix.Length), "N", out taskId);
    }

    /// <summary>
    /// What a <see cref="AgentTaskEventType.Check"/> event stores (CARD-0035 slice 5): the cost line,
    /// then the interpreter's reading, then the digest.
    ///
    /// <para><b>Why the reading is stored at all.</b> The specialist's one-line judgement was built for
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

        var spec = CheckInterpreterProvisioner.Spec(_settings);
        var wait = TimeSpan.FromSeconds(Math.Max(1, _settings.CheckInterpreterWaitSeconds));
        var run = await _runner.RunAsync(
            spec,
            CheckInterpretation.BuildTitle(task, facts.Task.CheckNumber),
            CheckInterpretation.BuildGoal(task, facts.Task.CheckNumber, digest),
            wait,
            _settings.CheckInterpreterMaxBacklog,
            _interpreter.EnsureAsync,
            ct,
            createdDetail: $"Interpretation of check #{facts.Task.CheckNumber} on task "
                + $"{DelegationReportFormatter.Short(task.Id)}.");

        var shortId = run.RunTaskId is Guid id ? DelegationReportFormatter.Short(id) : null;
        var line = shortId is null ? null : $"interpreter: task {shortId}, ${run.CostUsd:0.0000}";

        return run.Outcome switch
        {
            SpecialistRunOutcome.Disabled => Interpretation.NotWiredIn,
            SpecialistRunOutcome.Busy => Interpretation.Degraded("interpreter busy"),
            SpecialistRunOutcome.ProvisionFailed => Interpretation.Degraded(
                "interpreter unavailable: could not be provisioned"),
            SpecialistRunOutcome.QueueFailed => Interpretation.Degraded(
                "interpreter unavailable: the interpretation could not be queued"),
            SpecialistRunOutcome.Timeout => Interpretation.Degraded(
                $"interpreter unavailable: no reading within {_settings.CheckInterpreterWaitSeconds}s",
                shortId is null ? null : $"interpreter: task {shortId}, timed out"),
            SpecialistRunOutcome.Failed => Interpretation.Degraded(
                "interpreter unavailable: the interpretation failed", line),
            SpecialistRunOutcome.Empty => Interpretation.Degraded(
                "interpreter unavailable: the interpretation was empty", line),
            SpecialistRunOutcome.Succeeded => new Interpretation(run.Result, null, line),
            _ => Interpretation.Degraded("interpreter unavailable: the interpretation failed", line),
        };
    }

    /// <summary>
    /// The incident + alert behind CARD-0079 slice 2. Same shape as
    /// <c>AgentTaskReplyService.RecordUncorrelatedReportAsync</c>: write the timeline row, then
    /// optionally raise <see cref="IAlertService"/> — a host without alerting still records the
    /// incident. Observability must never be able to break the check; the digest still ships.
    /// </summary>
    internal static string InterpreterUnavailableDedupKey(Guid agentId) =>
        SpecialistTaskRunner.UnavailableDedupKey(AgentIncidentKind.CheckInterpreterUnavailable, agentId);

    /// <summary>
    /// CARD-0302 S3: a Check-role row that already has a reading must not sit <c>Blocked</c>.
    /// The interpretation is the deliverable — remap to <c>Succeeded</c> / <c>Exempt</c> and
    /// leave the standing interpreter session untouched. Empty-Result Blocked Check rows are
    /// left Blocked (they are not readings). Idempotent: a second pass matches nothing.
    /// </summary>
    public static async Task<int> RemapBlockedInterpretationsAsync(
        AppDbContext db, TimeProvider time, CancellationToken ct)
    {
        var rows = await db.AgentTasks
            .Where(t => t.Role == AgentTaskRole.Check
                && t.Status == AgentTaskStatus.Blocked
                && t.Result != null
                && t.Result != "")
            .ToListAsync(ct);
        if (rows.Count == 0)
            return 0;

        var now = time.GetUtcNow().UtcDateTime;
        const string detail =
            "CARD-0302: Check-role interpretation is the deliverable; remapped from Blocked.";
        foreach (var row in rows)
        {
            row.Status = AgentTaskStatus.Succeeded;
            row.ReportEvidence = AgentTaskReportEvidence.Exempt;
            row.ConcurrencyToken = Guid.NewGuid();
            db.AgentTaskEvents.Add(new AgentTaskEvent
            {
                Id = Guid.NewGuid(),
                AgentTaskId = row.Id,
                Type = AgentTaskEventType.Completed,
                Detail = detail,
                At = now,
            });
        }

        await db.SaveChangesAsync(ct);
        return rows.Count;
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
    /// Opening token of a check note whose task settled after the digest was captured (CARD-0074).
    /// Used for idempotency in the queue-window sweep — a second pass must not prepend twice.
    /// </summary>
    public const string SupersededMarker = "SUPERSEDED";

    /// <summary>
    /// The one place the superseded banner is built, so the interpreter-window prepend and the
    /// queue-window amend cannot drift. Names the three destructive reactions the card forbids.
    /// </summary>
    internal static string SupersededBanner(
        AgentTaskStatus status, DateTime settledAt, DateTime capturedAt) =>
        $"{SupersededMarker} — captured {DelegateCheckProbe.Stamp(capturedAt)}, but this task SETTLED at {DelegateCheckProbe.Stamp(settledAt)}, after\n"
        + $"capture. It is now {status}. Every status/working/elapsed line below is historical. The\n"
        + "completion note is the current answer — do not chase, cancel or re-dispatch this task.";

    /// <summary>
    /// The capture stamp printed into the digest / header, when it is still sitting in the body.
    /// The queue-window sweep uses this so the banner names the same moment the digest does.
    /// </summary>
    internal static bool TryReadCapturedAt(string? body, out DateTime capturedAt)
    {
        capturedAt = default;
        if (string.IsNullOrEmpty(body))
            return false;

        foreach (var token in new[] { "CAPTURED ", "captured " })
        {
            var at = body.IndexOf(token, StringComparison.Ordinal);
            if (at < 0)
                continue;
            var start = at + token.Length;
            if (start + 20 > body.Length)
                continue;
            if (DateTime.TryParseExact(
                    body.AsSpan(start, 20),
                    "yyyy-MM-dd'T'HH:mm:ss'Z'",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                    out capturedAt))
                return true;
        }

        return false;
    }

    /// <summary>
    /// One-line identity budget for the check header (CARD-0350 S1). The ellipsis is inside the
    /// 64 characters, not added on top. The whole-note pty ceiling remains a separate transport
    /// guard; this is the header-length policy.
    /// </summary>
    internal const int HeaderTitleMaxChars = 64;

    internal const string HeaderTitleEllipsis = "...";

    /// <summary>What a blank or whitespace-only task title renders as on the check header.</summary>
    internal const string DefaultHeaderTitle = "Delegated task";

    /// <summary>
    /// Flatten a task title to one line and clip it at a word boundary so the check header cannot
    /// dump a 300-character Goal excerpt (or a multi-line Title) into the caller's composer.
    /// </summary>
    internal static string ClipHeaderTitle(string? title)
    {
        var oneLine = string.Join(" ", (title ?? string.Empty)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (oneLine.Length == 0)
            return DefaultHeaderTitle;
        if (oneLine.Length <= HeaderTitleMaxChars)
            return oneLine;

        var budget = HeaderTitleMaxChars - HeaderTitleEllipsis.Length;
        var head = oneLine[..budget];
        if (budget < oneLine.Length && !char.IsWhiteSpace(oneLine[budget]))
        {
            var lastWs = head.LastIndexOfAny([' ', '\t']);
            if (lastWs > 0)
                head = head[..lastWs];
        }

        head = head.TrimEnd();
        if (head.Length == 0)
            head = oneLine[..budget];
        if (head.Length > 0 && char.IsHighSurrogate(head[^1]))
            head = head[..^1];
        return head + HeaderTitleEllipsis;
    }

    /// <summary>
    /// CARD-0350 S3 display identity, formatted only from probe facts:
    /// bound alias, else bound identifier plus clipped title, else clipped title, else
    /// <see cref="DefaultHeaderTitle"/>.
    /// </summary>
    internal static string FormatHeaderIdentity(DelegateCheckProbe.CheckFacts facts)
    {
        var clippedTitle = ClipHeaderTitle(facts.Task.Title);
        var identifier = facts.Card?.Identifier?.Trim();
        if (string.IsNullOrEmpty(identifier))
            return clippedTitle;

        var alias = facts.Card!.Alias?.Trim();
        if (!string.IsNullOrEmpty(alias))
            return $"{identifier}: {alias}";

        return $"{identifier}: {clippedTitle}";
    }

    /// <summary>
    /// The note as the caller sees it. The first line has to be unmistakable at a glance: a check
    /// is an OBSERVATION about work still in flight, and a caller that read it as a completion
    /// would move on from a task that has not finished.
    ///
    /// <para>So the envelope is fixed: it opens with <see cref="HeaderPrefix"/>, never with
    /// <c>[task </c>, its status vocabulary is the session's (running/idle/working) and never the
    /// completion vocabulary (done/failed), and it carries NO task marker of any task — see
    /// <see cref="ScrubTaskMarkers"/>, which matters because the transcript tail legitimately
    /// contains the delegate's own brief, marker and all. Identity is formatted from gathered
    /// facts (<see cref="FormatHeaderIdentity"/>): a bound card alias, else the card identifier
    /// plus a clipped title, else the clipped title, never a live card query and never the raw
    /// <see cref="AgentTask.Title"/>.</para>
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
    /// <param name="supersededBanner">
    /// CARD-0074: when the task settled after capture, this banner is prepended BEFORE the pty
    /// ceiling fit so the marker survives and the digest tail is what gets trimmed.
    /// </param>
    internal string BuildNote(
        AgentTask task,
        DelegateCheckProbe.CheckFacts facts,
        string digest,
        string? interpretation = null,
        string? degradedReason = null,
        string? supersededBanner = null)
    {
        var header = new StringBuilder();
        header.Append(HeaderPrefix)
              .Append(DelegationReportFormatter.Short(task.Id))
              .Append(" #").Append(facts.Task.CheckNumber).Append(']');

        var bits = new List<string>();
        if (degradedReason is { Length: > 0 } down
            && down.StartsWith("interpreter unavailable", StringComparison.Ordinal))
            bits.Add(InterpreterDownMarker);
        bits.Add(FormatHeaderIdentity(facts));
        bits.Add($"elapsed {FormatAge(facts.Task.Age)}/{facts.Task.ExpectedDurationMinutes}m");
        if (facts.Task.RepliedAt > facts.Task.DispatchedAt)
            bits.Add($"after reply; dispatched {FormatAge(facts.At - facts.Task.DispatchedAt!.Value)} ago");
        if (facts.Session is { } session)
        {
            bits.Add($"{session.Status.ToString().ToLowerInvariant()}/{(session.Working ? "working" : "idle")}");
            bits.Add($"activity {(session.SinceLastEntry is { } quiet ? $"{FormatAge(quiet)} ago" : "never")}");
        }
        else
        {
            bits.Add("no session");
        }
        if (task.NextCheckAt is null)
            bits.Add($"final check - the {_settings.CheckMaxCount}-check budget is spent");
        header.Append(' ').Append(string.Join(" | ", bits));

        // The body is the interpretation when there is one, the digest otherwise — and the degraded
        // prefix rides ABOVE the digest, never above an interpretation.
        var body = interpretation is { Length: > 0 } read
            ? read.Trim()
            : degradedReason is { Length: > 0 } reason
                ? $"(unverified digest — {reason})\n\n{digest}"
                : digest;

        // Banner first so a skim of line 1 sees SUPERSEDED, then the existing header. The fit
        // below treats this whole prefix as unsacrificeable — the digest tail yields first.
        var prefix = string.IsNullOrEmpty(supersededBanner)
            ? header.ToString()
            : supersededBanner.TrimEnd() + "\n\n" + header;

        var note = $"{prefix}\n\n{ScrubTaskMarkers(body)}".ReplaceLineEndings("\n");

        // A digest is a few KB and the pty it is typed into has a measured ceiling (CARD-0037). Fit
        // it the same way a report is fitted rather than letting every check trip the oversize
        // tripwire — the excerpt banner names where to read the rest.
        var ceiling = _ptyProfile?.Ceilings.ReplyInlineMaxChars ?? _settings.ReplyInlineMaxChars;
        if (note.Length <= ceiling)
            return note;

        // Floored: a pathological title (300 chars) against a small configured ceiling could
        // otherwise leave the body a negative budget, and the excerpt arithmetic would throw on the
        // one delivery that most needed to survive.
        var bodyBudget = Math.Max(400, ceiling - prefix.Length - 2);
        var (fitted, _) = DelegationReportFormatter.FitReport(
            ScrubTaskMarkers(body), task, _settings, bodyBudget);
        var result = $"{prefix}\n\n{fitted}".ReplaceLineEndings("\n");
        // FitReport's excerpt banner can overshoot the budget by a few hundred characters.
        // The prefix (banner + header) is at the front, so a tail cut keeps it.
        return result.Length <= ceiling ? result : result[..ceiling];
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
