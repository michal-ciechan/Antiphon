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
/// CARD-0292 S4: the swallowed-input watchdog. Once a minute (from
/// <c>AgentSupervisorHostedService</c>, the CARD-0067 global-sweep precedent), for every live
/// session whose LATEST <see cref="TranscriptKinds.QueueEnqueue"/> row has no later conversion —
/// no <c>UserPrompt</c>, <c>QueuedUserPrompt</c>, <c>QueueDequeue</c> or <c>QueueRemove</c> above
/// it — while the session reads idle and the enqueue is older than
/// <see cref="QueuedInputWatchSettings.StuckMinutes"/>: raise
/// <see cref="AgentIncidentKind.QueuedInputNeverConverted"/>.
///
/// <para>This is the universal detector for the CARD-0292 wedge: input arriving via the RC
/// bridge, Herdr, or an operator terminal has no delivery-verification layer at all, and the only
/// place every source converges is the transcript. Sequence-window closure, deliberately no text
/// matching — the incident's "Hi" (2 chars) is far below any match floor, so a text gate could
/// never close here.</para>
///
/// <para><b>Detection only</b> (CARD-0153's rule, verbatim): never kills, never types, never
/// Escs — the sweep's evidence is rows, and rows cannot see the screen. Warning at the threshold;
/// Error (Critical when channel-bound) once the same episode passes
/// <see cref="QueuedInputWatchSettings.EscalateToErrorAfterMinutes"/> — the
/// <c>TaskProgressStalled</c> ladder. Deduped per (session, enqueue sequence) episode via
/// <c>FailureReason</c>; closure resets the episode and a new enqueue is a new one. Survives a
/// server restart (no in-memory state).</para>
/// </summary>
public sealed class QueuedInputWatchdogService
{
    private static readonly SessionStatus[] LiveSessionStatuses =
        [SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping];

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SupervisionSettings _settings;
    private readonly TimeProvider _time;
    private readonly ILogger<QueuedInputWatchdogService> _logger;

    public QueuedInputWatchdogService(
        IServiceScopeFactory scopeFactory,
        IOptions<SupervisionSettings> settings,
        TimeProvider time,
        ILogger<QueuedInputWatchdogService> logger)
    {
        _scopeFactory = scopeFactory;
        _settings = settings.Value;
        _time = time;
        _logger = logger;
    }

    /// <summary>Returns how many NEW incidents (or severity escalations) this pass raised.</summary>
    public async Task<int> SweepAsync(CancellationToken ct)
    {
        if (!_settings.QueuedInputWatch.Enabled)
            return 0;

        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = _time.GetUtcNow().UtcDateTime;

        var liveIds = await db.AgentSessions.AsNoTracking()
            .Where(s => LiveSessionStatuses.Contains(s.Status))
            .Select(s => s.Id)
            .ToListAsync(ct);
        if (liveIds.Count == 0)
            return 0;

        // Latest enqueue per live session. Kind gating is implicit: only Claude transcripts
        // produce queue-operation rows, and only sessions that have any are visited at all.
        var latestEnqueues = await db.TranscriptEntries.AsNoTracking()
            .Where(t => liveIds.Contains(t.AgentSessionId) && t.Kind == TranscriptKinds.QueueEnqueue)
            .GroupBy(t => t.AgentSessionId)
            .Select(g => new { SessionId = g.Key, Sequence = g.Max(t => t.Sequence) })
            .ToListAsync(ct);

        var raised = 0;
        foreach (var enqueue in latestEnqueues)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (await EvaluateSessionAsync(db, enqueue.SessionId, enqueue.Sequence, now, ct))
                    raised++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Queued-input sweep failed for session {SessionId}; the sweep continues",
                    enqueue.SessionId);
            }
        }

        return raised;
    }

    private async Task<bool> EvaluateSessionAsync(
        AppDbContext db, Guid sessionId, long enqueueSequence, DateTime now, CancellationToken ct)
    {
        if (await IsEpisodeClosedAsync(db, sessionId, enqueueSequence, ct))
            return false;

        var enqueue = await db.TranscriptEntries.AsNoTracking()
            .Where(t => t.AgentSessionId == sessionId
                && t.Sequence == enqueueSequence
                && t.Kind == TranscriptKinds.QueueEnqueue)
            .Select(t => new { t.Timestamp, t.CreatedAt, t.Text })
            .FirstOrDefaultAsync(ct);
        if (enqueue is null)
            return false;

        // The record's own timestamp is enqueue time; a null stamp falls back to ingestion time.
        var enqueuedAt = enqueue.Timestamp ?? enqueue.CreatedAt;
        var stuckFor = now - enqueuedAt;
        if (stuckFor < TimeSpan.FromMinutes(Math.Max(1, _settings.QueuedInputWatch.StuckMinutes)))
            return false;

        // A mid-turn session legitimately holds queued input for the length of the turn; the
        // wedge shape reads idle (the preamble's renames are local-command records, excluded
        // from activity).
        if (await SessionMessageQueueService.IsWorkingAsync(db, sessionId, ct))
            return false;

        var key = EpisodeKey(enqueueSequence);
        var latest = await db.AgentIncidents.AsNoTracking()
            .Where(i => i.SessionId == sessionId
                && i.Kind == AgentIncidentKind.QueuedInputNeverConverted
                && i.FailureReason == key)
            .OrderByDescending(i => i.CreatedAt)
            .Select(i => (AlertSeverity?)i.Severity)
            .FirstOrDefaultAsync(ct);

        var severity = Severity(
            stuckFor,
            alreadyRaised: latest is not null,
            channelBound: await IsChannelBoundAsync(db, sessionId, ct));
        if (latest is not null && latest.Value >= severity)
            return false;

        var head = enqueue.Text is { Length: > 0 } text
            ? (text.Length > 120 ? text[..120] + "…" : text)
            : "(empty)";
        var owner = await SessionOwnerLookup.ResolveOwningAgentIdAsync(db, sessionId, ct);
        var message =
            $"Input was accepted into the TUI's own composer queue {Math.Round(stuckFor.TotalMinutes)}m ago "
            + $"and never became a prompt (enqueue seq {enqueueSequence}: \"{head}\"). The session reads "
            + "idle, so nothing is draining it — the usual cause is a blocking modal that swallowed the "
            + "input. Detection only: nothing was killed or typed; an operator Esc (or a restart) clears "
            + "a standing modal.";
        db.AgentIncidents.Add(new AgentIncident
        {
            Id = Guid.NewGuid(),
            AgentId = owner,
            SessionId = sessionId,
            Kind = AgentIncidentKind.QueuedInputNeverConverted,
            Severity = severity,
            Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength),
            FailureReason = key,
            CreatedAt = now,
        });
        await db.SaveChangesAsync(ct);
        _logger.LogWarning(
            "Queued input never converted on session {SessionId} ({Severity}): enqueue seq "
            + "{Sequence} stuck for {Minutes}m",
            sessionId, severity, enqueueSequence, Math.Round(stuckFor.TotalMinutes));
        return true;
    }

    /// <summary>
    /// Closed = any conversion or drain activity after the last enqueue: the TUI queue is moving.
    /// Shared with the attention projection so the feed and the sweep cannot disagree.
    /// </summary>
    internal static async Task<bool> IsEpisodeClosedAsync(
        AppDbContext db, Guid sessionId, long enqueueSequence, CancellationToken ct) =>
        await db.TranscriptEntries.AsNoTracking().AnyAsync(
            t => t.AgentSessionId == sessionId
                && t.Sequence > enqueueSequence
                && (t.Kind == TranscriptKinds.UserPrompt
                    || t.Kind == TranscriptKinds.QueuedUserPrompt
                    || t.Kind == TranscriptKinds.QueueDequeue
                    || t.Kind == TranscriptKinds.QueueRemove),
            ct);

    internal static string EpisodeKey(long enqueueSequence) => $"enqueueSeq={enqueueSequence}";

    internal static bool TryParseEpisodeKey(string? failureReason, out long enqueueSequence)
    {
        enqueueSequence = 0;
        const string prefix = "enqueueSeq=";
        return failureReason is not null
            && failureReason.StartsWith(prefix, StringComparison.Ordinal)
            && long.TryParse(failureReason.AsSpan(prefix.Length), out enqueueSequence);
    }

    private AlertSeverity Severity(TimeSpan stuckFor, bool alreadyRaised, bool channelBound)
    {
        var errorAfter = _settings.QueuedInputWatch.EscalateToErrorAfterMinutes;
        var atError = alreadyRaised
            && errorAfter > 0
            && stuckFor >= TimeSpan.FromMinutes(errorAfter);
        if (!atError)
            return AlertSeverity.Warning;
        return channelBound ? AlertSeverity.Critical : AlertSeverity.Error;
    }

    /// <summary>Same shape as <c>ApiErrorRecoveryService.IsChannelBoundAsync</c>.</summary>
    private static async Task<bool> IsChannelBoundAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var sessionIdText = sessionId.ToString("D");
        var agentId = await db.Agents
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (agentId is not Guid id)
            return false;
        return await db.ChatChannels.AsNoTracking().AnyAsync(c => c.AgentId == id, ct);
    }
}
