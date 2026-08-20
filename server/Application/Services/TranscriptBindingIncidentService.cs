using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

    public TranscriptBindingIncidentService(
        IServiceScopeFactory scopeFactory,
        ILogger<TranscriptBindingIncidentService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
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
            var agentId = await ResolveOwningAgentIdAsync(db, fault.SessionId, ct);
            if (agentId is not Guid owner)
            {
                // Same swallow-and-Error shape as ChannelReplyDispatcher.ReportLostAsync: nothing
                // to hang an incident on, but an unbound session nobody claims is itself worth seeing.
                _logger.LogError(
                    "Session {SessionId} has no transcript ({Kind}: {Detail}). No agent owns the session, "
                    + "so the fault was not recorded as an incident.",
                    fault.SessionId, fault.Kind, fault.Detail);
                return;
            }

            var channelBound = await db.ChatChannels.AnyAsync(c => c.AgentId == owner, ct);
            var detail = fault.CandidatePath is { } candidate
                ? $"{fault.Kind}: {fault.Detail} (candidate {candidate})"
                : $"{fault.Kind}: {fault.Detail}";

            // RecordIncidentAsync does NOT save; this scope's SaveChanges commits the row.
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
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
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Recording a transcript fault for session {SessionId} failed", fault.SessionId);
        }
    }

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
            var agentId = await ResolveOwningAgentIdAsync(db, bound.SessionId, ct);
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
            _logger.LogWarning(ex, "Recording a heuristic transcript bind for session {SessionId} failed", bound.SessionId);
        }
    }

    /// <summary>
    /// Standing agents own a session via <c>PersistentSessionId</c>; a delegate task session often
    /// does not — the pool/ephemeral agent is on the task row, and a later spawn can overwrite
    /// the standing pointer. Same two-step lookup AttentionService uses for session owners; when
    /// neither hits, the caller logs at Error rather than dropping the outcome.
    /// </summary>
    private static async Task<Guid?> ResolveOwningAgentIdAsync(
        AppDbContext db, Guid sessionId, CancellationToken ct)
    {
        var sessionIdText = sessionId.ToString("D");
        var standing = await db.Agents
            .Where(a => a.PersistentSessionId == sessionIdText)
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(ct);
        if (standing is Guid standingId)
            return standingId;

        return await db.AgentTasks
            .Where(t => t.AgentSessionId == sessionId && t.AgentId != null)
            .OrderByDescending(t => t.DispatchedAt ?? t.CreatedAt)
            .Select(t => t.AgentId)
            .FirstOrDefaultAsync(ct);
    }
}
