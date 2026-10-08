using Antiphon.Server.Application.Interfaces;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0312 S3: the sweep that resolves the boot-reply watch, on the supervisor tick — the
/// <c>QueuedInputWatchdogService</c> / <c>HerdrStatusCorroborationService</c> precedent.
///
/// <para><b>It is a sweep, not an inline await.</b> Blocking <c>LaunchInteractiveProcessAsync</c>
/// for the deadline would hold a launch-queue slot and the caller's HTTP request, and would die
/// with the process — the CARD-0331 mistake (an in-memory queue with no boot reconciliation). The
/// launch stamps the expectation on the SESSION ROW and this resolves it, so the watch is
/// restart-safe by construction.</para>
///
/// <para><b>It is not the periodic probe, and must never become one.</b> Antiphon had a
/// round-trip liveness probe and deleted it on 2026-07-23 (<c>9e8f5a5a</c>) for spending model
/// turns on healthy idle sessions, and a TUI echo probe before that for false-positive-killing
/// them. <c>SessionHealthTests.No_probe_prompts_are_ever_sent_to_an_idle_session</c> pins that
/// absence and stays green: this sweep sends NOTHING. It only resolves a watch a launch already
/// armed, and an idle healthy session is never armed at all.</para>
///
/// <para><b>Detection only, never a stop (CARD-1156, operator decision option A).</b> A boot prompt
/// the model never answered is recorded and surfaced; this sweep never stops, restarts, latches,
/// types into, releases or re-queues a session, and writes no supervision state, whatever the
/// evidence says and however stale or missing it is. CARD-0079 remains the only automatic stop of
/// a Working session. The CARD-0312 S4 restart ladder (stop the hung session, count a failure,
/// latch after two) is retired: a hung standing session keeps its seat until an operator acts or
/// the process genuinely exits.</para>
///
/// <para><b>Pull before you judge.</b> On an <c>Overdue</c> reading the runner's own transcript is
/// pulled and the verdict re-evaluated before anything is recorded — the live stream is not a
/// reliable clock (CARD-0055, session e809ce65). The pull's result is never freshness proof and
/// its availability is never required: a failed pull leaves the stored rows to judge.</para>
/// </summary>
public sealed class BootReplyWatchdogService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _delegation;
    private readonly ContextWindowSettings _contextWindow;
    private readonly AgentSessionRuntime? _runtime;
    private readonly IEventBus? _events;
    private readonly TimeProvider _time;
    private readonly ILogger<BootReplyWatchdogService> _logger;

    public BootReplyWatchdogService(
        IServiceScopeFactory scopeFactory,
        IOptions<DelegationSettings> delegation,
        TimeProvider time,
        ILogger<BootReplyWatchdogService> logger,
        IOptions<ContextWindowSettings>? contextWindow = null,
        // Optional for the same reason the dispatcher's is: a harness without a runtime falls back
        // to whatever streamed, and the pull swallows its own failures anyway.
        AgentSessionRuntime? runtime = null,
        // Optional: the standing receipt's change notice is best effort and never delivery evidence.
        IEventBus? events = null)
    {
        _scopeFactory = scopeFactory;
        _delegation = delegation.Value;
        _contextWindow = contextWindow?.Value ?? new ContextWindowSettings();
        _time = time;
        _logger = logger;
        _runtime = runtime;
        _events = events;
    }

    /// <summary>
    /// CARD-1156 test seam: the context the standing receipt writer opens per write. Production
    /// leaves it null and builds a fresh context over the sweep scope's own options, so every
    /// interceptor registered on them sees the telemetry statements too.
    /// </summary>
    internal Func<AppDbContext>? WriterContextFactory { get; set; }

    /// <summary>Returns how many sessions this pass judged overdue and recorded a receipt for.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        var deadline = _delegation.BootModelWaitDeadlineMinutes;
        if (deadline <= 0)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _time.GetUtcNow().UtcDateTime;

        var live = await db.AgentSessions
            .Where(s => LiveSessionStatuses.Contains(s.Status))
            .ToListAsync(ct);
        if (live.Count == 0)
            return 0;

        var acted = 0;
        foreach (var session in live)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await EvaluateAsync(db, session, now, deadline, ct))
                    acted++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    ex, "Boot-reply sweep failed for session {SessionId}; the sweep continues",
                    session.Id);
            }
        }

        return acted;
    }

    private async Task<bool> EvaluateAsync(
        AppDbContext db,
        AgentSession session,
        DateTime now,
        int deadlineMinutes,
        CancellationToken ct)
    {
        // Self-heal: a session whose watch was never armed (a launch path that types outside the
        // message queue, or a row that predates this card) is re-derived from the same predicate,
        // on the same prompt-anchored clock — so a restart cannot lose a watch and an unarmed
        // launch is not unwatched.
        //
        // Cheap EXISTS first. Every healthy session after its first answer has null watch
        // columns forever; walking its whole transcript (with Text) every tick is how this
        // sweep cost ~10k rows / 4.4MB in steady state.
        if (session.BootPromptSequence is null || session.BootReplyDueAt is null)
        {
            if (await BootReplyWatch.HasModelReplySinceAsync(
                    db, session.Id, BootReplyWatch.LaunchClock(session), ct))
                return false;
            if (await BootReplyWatch.TryArmAsync(db, session.Id, deadlineMinutes, ct) is null)
                return false;
            await db.SaveChangesAsync(ct);
        }

        var (status, rows) = await EvaluateWatchAsync(db, session, now, ct);
        if (status == BootReplyWatch.Status.Answered)
        {
            await BootReplyWatch.DisarmAsync(db, session.Id, ct);
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (status != BootReplyWatch.Status.Overdue)
            return false;

        // CARD-1156: an episode whose due stage is already on record costs nothing more — no pull,
        // no owner or prompt reads — until its next stage falls due. Skipped while a newer prompt
        // row sits past the watched one: that may be a refinement, which is a new episode.
        if (!rows.Any(r => BootReplyWatch.IsPromptRow(r.Kind))
            && await StandingStageRecordedAsync(db, session, deadlineMinutes, now, ct))
            return false;

        // PULL BEFORE YOU JUDGE. Everything below records an observation about "the transcript
        // does not contain a model row", and the live stream is not a reliable clock (CARD-0055).
        if (_runtime is not null)
        {
            try
            {
                await _runtime.CatchUpTranscriptAsync(session.Id, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(
                    ex, "Boot-reply sweep could not pull the transcript for session {SessionId}",
                    session.Id);
            }

            (status, _) = await EvaluateWatchAsync(db, session, now, ct);
            if (status != BootReplyWatch.Status.Overdue)
            {
                if (status == BootReplyWatch.Status.Answered)
                {
                    await BootReplyWatch.DisarmAsync(db, session.Id, ct);
                    await db.SaveChangesAsync(ct);
                }

                _logger.LogInformation(
                    "Session {SessionId} looked boot-stalled on the stored transcript and is not on "
                    + "the runner's — the pull is what saved it",
                    session.Id);
                return false;
            }
        }

        // ONE OWNER PER POPULATION. A session bound to an ACTIVE delegate task (Dispatched or
        // Working) belongs to the dispatcher's overdue-deadline sweep, which since CARD-1151 only
        // DETECTS a boot stall: it writes BootStallDetected, then BootStallNeedsOperator, on the task
        // and derives the Overdue attention row; it never fails, stops, retries or releases the
        // session, and holds no alias. Raising here as well would be a second row for the same
        // silence. The watch stays armed so the sweep's own re-read is the one that judges it.
        var owners = await StandingBootWatchObservation.ReadOwnersAsync(db, session.Id, ct);
        var taskOwner = await StandingBootWatchObservation.ReadTaskOwnerAsync(db, session.Id, ct);
        if (taskOwner is StandingBootTaskOwner.Dispatched or StandingBootTaskOwner.Working)
        {
            _logger.LogDebug(
                "Session {SessionId} is boot-stalled and belongs to an open delegate task; the "
                + "overdue-deadline sweep owns the detection",
                session.Id);
            return false;
        }

        // No always-on agent points at this session: the existing generic diagnostic, detection only.
        if (!owners.Any(o => o.AlwaysOn))
            return await RaiseDiagnosticAsync(db, session, now, ct);

        return await ObserveStandingAsync(db, session, owners, taskOwner, now, deadlineMinutes, ct);
    }

    /// <summary>
    /// CARD-1156: a taskless AlwaysOn session. The positive whitelist decides whether a receipt is
    /// due; the writer revalidates the episode under the session row lock and records it in its own
    /// context. Nothing here, on any outcome or fault, stops, restarts, latches or types.
    /// </summary>
    private async Task<bool> ObserveStandingAsync(
        AppDbContext db,
        AgentSession session,
        IReadOnlyList<StandingBootOwner> owners,
        StandingBootTaskOwner taskOwner,
        DateTime now,
        int deadlineMinutes,
        CancellationToken ct)
    {
        var modelWait = _delegation.ModelWaitDeadlineMinutes;
        var observation = await StandingBootWatchObservation.ReadAsync(
            db, session, owners, taskOwner, session.BootPromptSequence, deadlineMinutes, modelWait, now, ct);
        var decision = StandingBootWatchPolicy.Decide(observation);
        if (decision.Stage == StandingBootWatchPolicy.Stage.None || decision.Facts is not { } facts
            || observation.OwnerAgentId is not Guid agentId)
        {
            if (decision.Reason is "identity-changed" or "prompt-missing" or "reply-observed")
            {
                // The armed watch no longer names the latest real prompt on this launch (a refinement,
                // a resume, or a reply below the watched sequence): re-derive it from the same predicate
                // so the next tick judges the current episode. Watch columns only; nothing else moves.
                await BootReplyWatch.TryArmAsync(db, session.Id, deadlineMinutes, ct);
                await db.SaveChangesAsync(ct);
            }

            _logger.LogDebug(
                "Session {SessionId}: standing boot receipt not due ({Reason}); the session keeps its seat",
                session.Id, decision.Reason);
            return false;
        }

        // The watch stays armed: a later operator stage can follow, and a model reply disarms it.
        var writer = new StandingBootWarningWriter(
            WriterContextFactory ?? (() => new AppDbContext(
                (DbContextOptions<AppDbContext>)db.GetService<IDbContextOptions>())),
            _events,
            _logger);
        var outcome = await writer.RecordAsync(
            new StandingBootWarningWriter.Episode(
                session.Id, agentId, facts, observation.PromptKind ?? TranscriptKinds.UserPrompt),
            decision.Stage, deadlineMinutes, modelWait, now, ct);
        return outcome == StandingBootWarningWriter.Outcome.Recorded;
    }

    /// <summary>
    /// The cheap pre-check from the armed columns: the prompt time is <c>BootReplyDueAt</c> minus
    /// the boot wait, the identity is the row's generation, launch clock and watched sequence. One
    /// read. A miss only costs the pull the sweep would make anyway; the writer is the dedup.
    /// </summary>
    private async Task<bool> StandingStageRecordedAsync(
        AppDbContext db, AgentSession session, int deadlineMinutes, DateTime now, CancellationToken ct)
    {
        if (session.BootPromptSequence is not long sequence || session.BootReplyDueAt is not DateTime due)
            return false;

        var facts = StandingBootWatchPolicy.Facts(
            SessionGeneration.Normalize(session.StartedAt), BootReplyWatch.LaunchClock(session), sequence,
            due.AddMinutes(-deadlineMinutes), deadlineMinutes, _delegation.ModelWaitDeadlineMinutes);
        var stage = StandingBootWatchPolicy.DueStage(facts, now);
        if (stage == StandingBootWatchPolicy.Stage.None)
            return false;

        var prefix = StandingBootWatchPolicy.EpisodePrefix(facts);
        var recorded = await db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == session.Id
                && i.Kind == AgentIncidentKind.LivenessProbeFailed
                && i.FailureReason != null
                && i.FailureReason.StartsWith(prefix))
            .Select(i => i.FailureReason)
            .ToListAsync(ct);
        return StandingBootWatchPolicy.IsRecorded(recorded, prefix, stage);
    }

    /// <summary>
    /// <see cref="BootReplyWatch.EvaluateSessionAsync"/>'s verdict, with the rows it read: the
    /// same one query, so the sweep can see a newer prompt past the watched one.
    /// </summary>
    private static async Task<(BootReplyWatch.Status Status, IReadOnlyList<BootReplyWatch.Row> Rows)> EvaluateWatchAsync(
        AppDbContext db, AgentSession session, DateTime now, CancellationToken ct)
    {
        if (session.BootPromptSequence is not long sequence || session.BootReplyDueAt is null)
            return (BootReplyWatch.Status.Disarmed, []);

        var rows = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session.Id && t.Sequence > sequence)
            .OrderBy(t => t.Sequence)
            .Select(t => new BootReplyWatch.Row(t.Sequence, t.Kind))
            .ToListAsync(ct);
        return (BootReplyWatch.Evaluate(session.BootPromptSequence, session.BootReplyDueAt, now, rows), rows);
    }

    /// <summary>
    /// The generic diagnostic for a session no always-on agent points at (CARD-0312 S3): the
    /// bundle and one <c>bootSeq=</c> incident per episode. Every fact here is available today and
    /// the message NAMES what was observed rather than asserting a diagnosis: the sequence, the
    /// wait, the context fullness, and what the composer is holding. Detection only: since
    /// CARD-1156 no branch of this sweep stops a session or touches supervision state.
    /// </summary>
    private async Task<bool> RaiseDiagnosticAsync(
        AppDbContext db, AgentSession session, DateTime now, CancellationToken ct)
    {
        var sequence = session.BootPromptSequence!.Value;
        var due = session.BootReplyDueAt!.Value;
        var key = EpisodeKey(sequence);

        // One incident per (session, boot prompt) episode. A re-arm on a later prompt is a new
        // episode and gets its own row; the same silence does not raise twice a tick.
        var already = await db.AgentIncidents.AsNoTracking().AnyAsync(
            i => i.SessionId == session.Id
                && i.Kind == AgentIncidentKind.LivenessProbeFailed
                && i.FailureReason == key, ct);
        if (already)
            return false;

        var owner = await SessionOwnerLookup.ResolveOwningAgentIdAsync(db, session.Id, ct);

        var promptAt = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session.Id && t.Sequence == sequence)
            .Select(t => (DateTime?)(t.Timestamp ?? t.CreatedAt))
            .FirstOrDefaultAsync(ct);
        var waited = promptAt is DateTime at ? now - at : now - due;
        if (waited < TimeSpan.Zero)
            waited = TimeSpan.Zero;

        var fullness = await LoadFullnessAsync(db, session, ct);
        var composer = ComposerHead(session.Id);
        var kinds = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == session.Id && t.Sequence > sequence)
            .OrderBy(t => t.Sequence)
            .Take(8)
            .Select(t => t.Kind + "@" + t.Sequence)
            .ToListAsync(ct);

        var message =
            $"Boot prompt confirmed at sequence {sequence}; no assistant, thinking, tool or "
            + $"turn-end row in {StandingBootWatchPolicy.Describe(waited)}"
            + (fullness is double f ? $"; context {f:P0}" : "; context unknown")
            + (composer is { Length: > 0 } head ? $"; composer holds: \"{head}\"" : "; composer not readable")
            + (kinds.Count > 0 ? $"; rows since: {string.Join(", ", kinds)}" : "; no rows since")
            + (session.LaunchResumedAt is DateTime resumed ? $"; launch resumed {resumed:u}" : string.Empty)
            + ". Detection only for this session: nothing was stopped, restarted or latched.";

        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = owner,
            SessionId = session.Id,
            Kind = AgentIncidentKind.LivenessProbeFailed,
            Severity = AlertSeverity.Warning,
            Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength),
            FailureReason = key,
            CreatedAt = now,
        });

        // The generic watch has done its job for this episode: one diagnostic, then disarmed.
        session.BootPromptSequence = null;
        session.BootReplyDueAt = null;
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Boot reply never came on session {SessionId} (Warning): {Message}",
            session.Id, message);
        return true;
    }

    /// <summary>The generic diagnostic's episode key. Old receipts keep this format.</summary>
    internal static string EpisodeKey(long bootPromptSequence) => $"bootSeq={bootPromptSequence}";

    private async Task<double?> LoadFullnessAsync(AppDbContext db, AgentSession session, CancellationToken ct)
    {
        try
        {
            var usage = await SessionContextUsage.LoadFullnessAsync(
                db,
                [(session.Id, session.EffectiveModelId, session.AgentKind)],
                _contextWindow,
                _logger,
                ct);
            return usage.TryGetValue(session.Id, out var snapshot) ? snapshot.Fullness : null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogDebug(ex, "Could not read context fullness for session {SessionId}", session.Id);
            return null;
        }
    }

    private string? ComposerHead(Guid sessionId)
    {
        if (_runtime is null || !_runtime.TryGetLiveSnapshot(sessionId, out var snapshot))
            return null;
        var screen = snapshot.RenderedScreen ?? string.Empty;
        var trimmed = screen.Replace("\r", " ").Replace("\n", " ").Trim();
        return trimmed.Length <= 200 ? trimmed : trimmed[^200..];
    }
}
