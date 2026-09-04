using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;
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
/// armed, at most once per launch, and an idle healthy session is never armed at all.</para>
///
/// <para><b>Pull before you judge.</b> On an <c>Overdue</c> reading the runner's own transcript is
/// pulled and the verdict re-evaluated before anything is recorded — the live stream is not a
/// reliable clock, and the kill that proved it wrong is what produced the records (CARD-0055,
/// session e809ce65).</para>
/// </summary>
public sealed class BootReplyWatchdogService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly DelegationSettings _delegation;
    private readonly ContextWindowSettings _contextWindow;
    private readonly AgentSessionRuntime? _runtime;
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
        AgentSessionRuntime? runtime = null)
    {
        _scopeFactory = scopeFactory;
        _delegation = delegation.Value;
        _contextWindow = contextWindow?.Value ?? new ContextWindowSettings();
        _time = time;
        _logger = logger;
        _runtime = runtime;
    }

    /// <summary>Returns how many sessions this pass judged overdue and acted on.</summary>
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
                if (await EvaluateAsync(db, scope, session, now, deadline, ct))
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
        IServiceScope scope,
        AgentSession session,
        DateTime now,
        int deadlineMinutes,
        CancellationToken ct)
    {
        // Self-heal: a session whose watch was never armed (a launch path that types outside the
        // message queue, or a row that predates this card) is re-derived from the same predicate,
        // on the same prompt-anchored clock — so a restart cannot lose a watch and an unarmed
        // launch is not unwatched.
        if (session.BootPromptSequence is null || session.BootReplyDueAt is null)
        {
            if (await BootReplyWatch.TryArmAsync(db, session.Id, deadlineMinutes, ct) is null)
                return false;
            await db.SaveChangesAsync(ct);
        }

        var status = await BootReplyWatch.EvaluateSessionAsync(db, session, now, ct);
        if (status == BootReplyWatch.Status.Answered)
        {
            await BootReplyWatch.DisarmAsync(db, session.Id, ct);
            await db.SaveChangesAsync(ct);
            return false;
        }

        if (status != BootReplyWatch.Status.Overdue)
            return false;

        // PULL BEFORE YOU JUDGE. Everything below records a failure about "the transcript does not
        // contain a model row", and the live stream is not a reliable clock (CARD-0055).
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

            status = await BootReplyWatch.EvaluateSessionAsync(db, session, now, ct);
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

        // ONE RECOVERY PER POPULATION. A session bound to an OPEN delegate task is owned by the
        // dispatcher's boot arm (CARD-0353 S2): it fails the task with ProviderUnresponsive, kills
        // the session, retries once at the same tier, tells the parent, and holds the alias on a
        // repeat. Raising here as well would be two mechanisms killing the same session for the
        // same reason — precisely the overlap CARD-0312's plan forbids. The watch stays armed so
        // that arm's own re-read is the one that judges it.
        if (await db.AgentTasks.AsNoTracking().AnyAsync(
                t => t.AgentSessionId == session.Id
                    && (t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working),
                ct))
        {
            _logger.LogDebug(
                "Session {SessionId} is boot-stalled and belongs to an open delegate task; the "
                + "overdue-deadline sweep owns the recovery",
                session.Id);
            return false;
        }

        return await RaiseAsync(db, scope, session, now, ct);
    }

    /// <summary>
    /// The diagnostic bundle and the incident. Every fact here is available today — no dependency
    /// on CARD-0311 shipping — and the message NAMES what was observed rather than asserting a
    /// diagnosis: the sequence, the wait, the context fullness, and what the composer is holding.
    /// </summary>
    private async Task<bool> RaiseAsync(
        AppDbContext db, IServiceScope scope, AgentSession session, DateTime now, CancellationToken ct)
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
        var agent = owner is Guid ownerId
            ? await db.Agents.FirstOrDefaultAsync(a => a.Id == ownerId, ct)
            : null;

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

        // Whether this is the latching third strike is decided before the message is written, so
        // the row says what actually happened rather than what was intended.
        AgentSupervisionState? state = null;
        var latching = false;
        if (agent is { AlwaysOn: true })
        {
            state = await db.AgentSupervisionStates
                .FirstOrDefaultAsync(s => s.AgentId == agent.Id, ct);
            if (state is null)
            {
                state = new AgentSupervisionState { AgentId = agent.Id, UpdatedAt = now };
                db.AgentSupervisionStates.Add(state);
            }

            latching = state.LivenessLatchedAt is not null
                || state.ConsecutiveFailures + 1 > MaxProbeDrivenRestarts;
        }

        var message =
            $"Boot prompt confirmed at sequence {sequence}; no assistant, thinking, tool or "
            + $"turn-end row in {Describe(waited)}"
            + (fullness is double f ? $"; context {f:P0}" : "; context unknown")
            + (composer is { Length: > 0 } head ? $"; composer holds: \"{head}\"" : "; composer not readable")
            + (kinds.Count > 0 ? $"; rows since: {string.Join(", ", kinds)}" : "; no rows since")
            + (session.LaunchResumedAt is DateTime resumed ? $"; launch resumed {resumed:u}" : string.Empty)
            + ". "
            + (latching
                ? "This mechanism has now stopped restarting this agent — two consecutive "
                  + "probe-driven restarts did not clear it, so a third would be the 2026-07 "
                  + "restart loop by another route. A human StartAsync clears the latch."
                : agent is { AlwaysOn: true }
                    ? "Routed to the supervisor's existing restart ladder."
                    : "Detection only for this session: it has no always-on agent to restart.");

        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = owner,
            SessionId = session.Id,
            Kind = AgentIncidentKind.LivenessProbeFailed,
            Severity = latching ? AlertSeverity.Error : AlertSeverity.Warning,
            Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength),
            FailureReason = key,
            CreatedAt = now,
        });

        if (state is not null)
        {
            if (latching)
            {
                state.LivenessLatchedAt ??= now;
            }
            else
            {
                // The EXISTING ladder: Backoff, FreshAfterResumeFailures (so the second restart is
                // a fresh conversation — the measured cure) and EscalateIfTierCrossedAsync all
                // apply with no new policy. Nothing is killed here: a session that is producing
                // output was never armed, and a working session is never restarted from a sweep.
                state.ConsecutiveFailures++;
                state.NextRestartAt = null;
                state.UpdatedAt = now;
            }
        }

        // The watch has done its job for this episode either way; leaving it armed would re-raise
        // the same silence against a session the recovery ladder now owns.
        session.BootPromptSequence = null;
        session.BootReplyDueAt = null;
        await db.SaveChangesAsync(ct);

        _logger.LogWarning(
            "Boot reply never came on session {SessionId} ({Severity}): {Message}",
            session.Id, latching ? "Error" : "Warning", message);
        return true;
    }

    /// <summary>
    /// CARD-0312 S4's hard stop: at most two consecutive probe-driven restarts per agent. The
    /// third consecutive failure latches the mechanism off for that agent rather than restarting
    /// again.
    /// </summary>
    internal const int MaxProbeDrivenRestarts = 2;

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

    private static string Describe(TimeSpan span) =>
        span.TotalMinutes >= 1
            ? $"{(int)span.TotalMinutes}m{span.Seconds:00}s"
            : $"{(int)span.TotalSeconds}s";
}
