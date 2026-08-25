using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Turns the session runner's transcript-binding events into agent incidents (CARD-0006).
///
/// The runner refuses to bind a transcript it cannot prove belongs to the session, which is the
/// safe outcome — the alternative bound an agent to the human operator's own Claude conversation
/// on 2026-08-09 — but a session running with NO transcript is badly degraded and used to be
/// announced by a single WRN line in the runner log: nothing is ingested, working/idle reads
/// permanently idle, and channel reply dispatch is dead.
///
/// Split out of <c>SessionRunnerEventPump</c> so the incident/severity decision is testable
/// without standing up an SSE stream; the pump is a thin caller.
/// </summary>
public sealed class TranscriptBindingIncidentService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TranscriptBindingIncidentService> _logger;
    private readonly TranscriptBindingSettings _settings;

    public TranscriptBindingIncidentService(
        IServiceScopeFactory scopeFactory,
        ILogger<TranscriptBindingIncidentService> logger,
        IOptions<TranscriptBindingSettings>? settings = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _settings = settings?.Value ?? new TranscriptBindingSettings();
    }

    /// <summary>
    /// Records a <see cref="AgentIncidentKind.TranscriptBindFailed"/> incident, <b>Critical when
    /// the agent has a channel binding</b> and Warning otherwise: a channel-bound agent with no
    /// transcript cannot answer its channel at all, and a WRONGLY bound one is the privacy incident
    /// this card exists for. Critical reaches Telegram through the normal alert pipeline.
    /// </summary>
    public async Task OnTranscriptFaultAsync(SessionRunnerTranscriptFaultEvent fault, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agentId = await SessionOwnerLookup.ResolveOwningAgentIdAsync(db, fault.SessionId, ct);
            if (agentId is not Guid owner)
            {
                // CARD-0101: this used to be a bare log-and-return, and it is the one path by which
                // the incident stream can go quiet while the runner log keeps reporting the fault —
                // exactly the "zero new incidents therefore solved" misreading the investigation
                // made. An AgentIncident row needs an AgentId, so there is still no incident to
                // write; a standalone alert carrying the session id is (same shape as an unclaimed
                // AutoCompactFailed / ChannelReplyLost: notify nobody in particular, but never swallow).
                _logger.LogError(
                    "Session {SessionId} has no transcript ({Kind}: {Detail}, unbound {Unbound:F0}s, "
                    + "report #{Repeat}). No agent owns the session — either nothing ever claimed it, "
                    + "or its delegate agent has since been deleted and AgentTasks.AgentId keeps no "
                    + "foreign key to notice (CARD-0195) — so the fault was raised as a "
                    + "standalone alert rather than an incident.",
                    fault.SessionId, fault.Kind, fault.Detail, fault.UnboundSeconds, fault.Repeat);

                var unownedAlerts = scope.ServiceProvider.GetService<IAlertService>();
                if (unownedAlerts is not null)
                {
                    await unownedAlerts.RaiseAsync(
                        new AlertRaise(
                            IsStuck(fault) ? AlertSeverity.Critical : AlertSeverity.Warning,
                            Source: "supervisor",
                            Title: $"{AgentIncidentKind.TranscriptBindFailed}: unclaimed session",
                            Detail: $"Session {fault.SessionId:D} is running with no transcript and no "
                                + $"owning agent. {fault.Kind}: {fault.Detail}",
                            DedupKey: UnownedFaultDedupKey(fault.SessionId),
                            AgentId: null,
                            SessionId: fault.SessionId),
                        ct);
                }
                return;
            }

            var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == owner, ct);
            var detail = fault.CandidatePath is { } candidate
                ? $"{fault.Kind}: {fault.Detail} (candidate {candidate})"
                : $"{fault.Kind}: {fault.Detail}";

            // RecordIncidentAsync does NOT save; this scope's SaveChanges commits the row.
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            if (fault.Kind == TranscriptFaultKinds.ClaimRevoked)
            {
                await supervisor.RecordIncidentAsync(
                    owner,
                    fault.SessionId,
                    AgentIncidentKind.TranscriptClaimRevoked,
                    channelBound ? AlertSeverity.Critical : AlertSeverity.Warning,
                    "This session had been reading the transcript of another session as its own; "
                    + "that file has been handed back. Nothing ingested from it belonged to this session. "
                    + detail,
                    failureReason: fault.Kind,
                    ct: ct);
                await db.SaveChangesAsync(ct);
                return;
            }

            await supervisor.RecordIncidentAsync(
                owner,
                fault.SessionId,
                AgentIncidentKind.TranscriptBindFailed,
                channelBound ? AlertSeverity.Critical : AlertSeverity.Warning,
                channelBound
                    ? $"No transcript is bound to this session, so channel replies cannot be dispatched. {detail}"
                    : $"No transcript is bound to this session. {detail}",
                failureReason: fault.Kind,
                ct: ct);

            await MaybeEscalateStuckAsync(db, supervisor, fault, owner, channelBound, detail, ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await ReportWriteFailureAsync(fault, ex, ct);
        }
    }

    /// <summary>
    /// CARD-0195: this catch used to be a bare <c>LogWarning</c> whose message named neither the
    /// cause nor the consequence — "Recording a transcript fault for session X failed" — and it
    /// fired seven times across two days on a failure with a perfectly nameable cause (Postgres
    /// 23503 on <c>FK_AgentIncidents_Agents_AgentId</c>: the delegate's agent row was already
    /// gone). Two things are wrong with swallowing it, and both are fixed here.
    ///
    /// <para>First, the cause: AGENTS.md's own rule is never to report a DB failure without the
    /// DB's own message, so a <see cref="DbUpdateException"/> is described through
    /// <see cref="AgentService.DescribeDbFailure"/> — the same naming CARD-0056 gave
    /// <c>ConflictException</c> — and logged at Error, because an unrecorded fault is exactly the
    /// "zero new incidents therefore solved" blind spot CARD-0101 exists to prevent.</para>
    ///
    /// <para>Second, the consequence: the fault itself is real whether or not a row could be
    /// written for it, so it degrades to the same standalone alert the unowned branch raises
    /// rather than disappearing. The alert is raised in a FRESH scope — the scope that threw may
    /// be holding a poisoned change tracker — and its own failure is swallowed, since a backstop
    /// that can throw is not a backstop.</para>
    /// </summary>
    private async Task ReportWriteFailureAsync(
        SessionRunnerTranscriptFaultEvent fault, Exception ex, CancellationToken ct)
    {
        var cause = ex is DbUpdateException dbEx
            ? AgentService.DescribeDbFailure(dbEx)
            : ex.GetBaseException().Message;

        _logger.LogError(
            ex,
            "Recording a transcript fault for session {SessionId} failed ({Cause}). The session still "
            + "has no transcript ({Kind}: {Detail}); raising it as a standalone alert so the fault is "
            + "not lost with the write.",
            fault.SessionId, cause, fault.Kind, fault.Detail);

        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            if (scope.ServiceProvider.GetService<IAlertService>() is not { } alerts)
                return;

            await alerts.RaiseAsync(
                new AlertRaise(
                    IsStuck(fault) ? AlertSeverity.Critical : AlertSeverity.Warning,
                    Source: "supervisor",
                    Title: $"{AgentIncidentKind.TranscriptBindFailed}: incident could not be recorded",
                    // Clipped: Alerts.Detail is varchar(4000) too, and a backstop that dies on the
                    // same oversize value that broke the incident is not a backstop.
                    Detail: Clip(
                        $"Session {fault.SessionId:D} is running with no transcript and the incident "
                        + $"row could not be written ({cause}). {fault.Kind}: {fault.Detail}",
                        3900),
                    DedupKey: WriteFailureDedupKey(fault.SessionId),
                    AgentId: null,
                    SessionId: fault.SessionId),
                ct);
        }
        catch (Exception alertEx) when (alertEx is not OperationCanceledException)
        {
            _logger.LogError(
                alertEx,
                "The standalone alert for session {SessionId}'s unrecordable transcript fault also failed",
                fault.SessionId);
        }
    }

    /// <summary>
    /// CARD-0101's escalation, layered ON TOP of the existing five-minute repeat rather than
    /// replacing it — the repeats are the proof the fault is still live and removing them would
    /// trade one blind spot for another. What is added is a distinct, Critical
    /// <see cref="AgentIncidentKind.TranscriptBindStuck"/> once the refusal has been CONTINUOUS for
    /// <c>StuckAfterMinutes</c>, independent of channel binding.
    ///
    /// <para>Independent of channel binding is the whole point: <see cref="OnTranscriptFaultAsync"/>
    /// reserves Critical for channel-bound agents, and a delegate task agent is never channel-bound,
    /// so every one of the 2026-08-20 cascade's ~250 incidents was Warning. Eleven agent-hours ran
    /// unreadable and the only thing that would have surfaced it was somebody querying the database.</para>
    ///
    /// <para>Re-fire is gated on the DB, not on in-memory state, so it survives a server restart and
    /// cannot be reset by the reconnect that re-subscribes to the runner's event stream. Same shape
    /// as <c>ContextCompactionService.HasAutoCompactFailedSinceAsync</c>.</para>
    /// </summary>
    private async Task MaybeEscalateStuckAsync(
        AppDbContext db,
        AgentSupervisorService supervisor,
        SessionRunnerTranscriptFaultEvent fault,
        Guid owner,
        bool channelBound,
        string detail,
        CancellationToken ct)
    {
        if (!_settings.EscalationEnabled || !IsStuck(fault))
            return;

        var since = DateTime.UtcNow - TimeSpan.FromMinutes(Math.Max(1, _settings.StuckRepeatMinutes));
        var alreadyEscalated = await db.AgentIncidents.AsNoTracking()
            .AnyAsync(i => i.SessionId == fault.SessionId
                && i.Kind == AgentIncidentKind.TranscriptBindStuck
                && i.CreatedAt >= since, ct);
        if (alreadyEscalated)
            return;

        var hours = TimeSpan.FromSeconds(fault.UnboundSeconds);
        var message =
            $"STILL unbound after {Describe(hours)} of continuous refusal ({fault.Repeat} report(s)). "
            + $"Nothing has been ingested for this session for that entire period: working/idle reads "
            + $"permanently idle, channel replies cannot dispatch, and any delegated task on it will "
            + $"settle on a watchdog timeout rather than its own report. {detail}"
            + (channelBound ? " This agent is channel-bound — a human may be waiting on a dead line." : "");

        await supervisor.RecordIncidentAsync(
            owner,
            fault.SessionId,
            AgentIncidentKind.TranscriptBindStuck,
            AlertSeverity.Critical,
            message,
            failureReason: fault.Kind,
            ct: ct);

        _logger.LogError(
            "Session {SessionId} has been unbound for {Seconds:F0}s ({Repeat} reports) — escalated to "
            + "{Kind}/Critical. {Detail}",
            fault.SessionId, fault.UnboundSeconds, fault.Repeat,
            AgentIncidentKind.TranscriptBindStuck, detail);
    }

    private bool IsStuck(SessionRunnerTranscriptFaultEvent fault) =>
        fault.UnboundSeconds >= TimeSpan.FromMinutes(Math.Max(1, _settings.StuckAfterMinutes)).TotalSeconds;

    internal static string UnownedFaultDedupKey(Guid sessionId) =>
        $"supervisor:{AgentIncidentKind.TranscriptBindFailed}:unclaimed:{sessionId:D}";

    private static string Clip(string text, int max) =>
        text.Length <= max ? text : text[..max] + "…";

    /// <summary>
    /// Kept distinct from <see cref="UnownedFaultDedupKey"/> so "nobody owns this session" and
    /// "the write itself broke" never dedup each other away — they need different fixes.
    /// </summary>
    internal static string WriteFailureDedupKey(Guid sessionId) =>
        $"supervisor:{AgentIncidentKind.TranscriptBindFailed}:unwritable:{sessionId:D}";

    private static string Describe(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{span.TotalHours:0.#}h"
        : span.TotalMinutes >= 1 ? $"{span.TotalMinutes:0}m"
        : $"{span.TotalSeconds:0}s";

    /// <summary>
    /// A transcript was bound by heuristic (cwd discovery, a mid-session fork, or the restart
    /// migration shim) rather than by the exact <c>&lt;session-id&gt;.jsonl</c> filename. Info-level
    /// timeline row with NO alert, mirroring <see cref="AgentIncidentKind.ContextCompacted"/>: the
    /// bind passed every adoption rule, but which file an agent reads from belongs on the record.
    /// </summary>
    public async Task OnHeuristicBindAsync(SessionRunnerTranscriptBoundEvent bound, CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var agentId = await SessionOwnerLookup.ResolveOwningAgentIdAsync(db, bound.SessionId, ct);
            if (agentId is not Guid owner)
            {
                _logger.LogError(
                    "Transcript bound by {How} at {Path} for session {SessionId}, but no agent owns the session, "
                    + "so the bind was not recorded as an incident.",
                    bound.How, bound.TranscriptPath, bound.SessionId);
                return;
            }

            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(
                owner,
                bound.SessionId,
                AgentIncidentKind.TranscriptBoundByDiscovery,
                AlertSeverity.Info,
                $"Transcript bound by {bound.How}: {bound.TranscriptPath}",
                raiseAlert: false,
                ct: ct);
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Same CARD-0195 rule, one severity down: this row is an Info timeline entry with no
            // alert of its own, so losing it degrades the record rather than hiding a live fault.
            // It still says what the database said instead of only "failed".
            var cause = ex is DbUpdateException dbEx
                ? AgentService.DescribeDbFailure(dbEx)
                : ex.GetBaseException().Message;
            _logger.LogWarning(
                ex,
                "Recording a heuristic transcript bind for session {SessionId} failed ({Cause})",
                bound.SessionId, cause);
        }
    }

}
