using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Reacts to a <c>CompactBoundary</c> transcript entry: records an Info-level
/// <see cref="AgentIncidentKind.ContextCompacted"/> incident (timeline row, NO alert — compaction
/// is normal operation) and, for agents with a channel preamble configured
/// (<c>Agent.SystemPromptAppend</c>), queues the recovery note telling the agent to re-read its
/// workspace files. The channel contract itself needs no re-injection — it lives in the system
/// prompt, which survives compaction by construction (the appended-system-prompt canary pins this).
///
/// Replay-proof: <c>TranscriptTailer</c> restarts at offset 0 on every runner restart/adoption and
/// republishes every historical event, so dedupe is a persisted per-session high-water mark
/// (<c>AgentSession.CompactionRecoveryWatermark</c>) — an incident-row check would be defeated by
/// incident pruning. The in-memory latch just keeps the common same-process replay cheap.
/// </summary>
public sealed class CompactionRecoveryService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly SessionMessageQueueService _queue;
    private readonly ILogger<CompactionRecoveryService> _logger;
    private readonly ConcurrentDictionary<Guid, long> _handledSequences = new();

    public CompactionRecoveryService(
        IServiceScopeFactory scopeFactory,
        SessionMessageQueueService queue,
        ILogger<CompactionRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _queue = queue;
        _logger = logger;
    }

    public async Task OnCompactBoundaryAsync(Guid sessionId, long sequence, CancellationToken ct)
    {
        // Cheap same-process dedupe; the durable check below is the one that matters.
        var seen = _handledSequences.GetOrAdd(sessionId, 0);
        if (sequence <= seen)
            return;
        _handledSequences[sessionId] = sequence;

        string? recoveryBody = null;
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var session = await db.AgentSessions.FirstOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session is null)
                return;
            if (session.CompactionRecoveryWatermark is long mark && sequence <= mark)
                return; // already handled (this process or a previous one)

            var sessionIdText = sessionId.ToString("D");
            var agent = await db.Agents.FirstOrDefaultAsync(a => a.PersistentSessionId == sessionIdText, ct);

            if (agent is not null)
            {
                // Scoped supervisor; RecordIncidentAsync does NOT save — this scope's save commits
                // the incident and the watermark together.
                var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
                await supervisor.RecordIncidentAsync(
                    agent.Id, sessionId, AgentIncidentKind.ContextCompacted, AlertSeverity.Info,
                    $"Context compacted (transcript boundary seq {sequence}).",
                    raiseAlert: false,
                    ct: ct);

                // Only channel-facing agents (preamble configured) get the bot-flavored note; a
                // plain dev agent compacting gets the incident row and nothing typed at it.
                if (!string.IsNullOrWhiteSpace(agent.SystemPromptAppend))
                    recoveryBody = ChannelPreamble.RecoveryNoteBody;
            }

            session.CompactionRecoveryWatermark = sequence;
            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Compaction recovery bookkeeping failed for session {SessionId}", sessionId);
            return;
        }

        if (recoveryBody is null)
            return;

        try
        {
            // WhenIdle is safe here BECAUSE the boundary kind is excluded from IsWorkingAsync
            // (PR 6's inseparable pair): an idle-but-just-compacted session takes the idle
            // fast-path immediately; a mid-work compaction waits for the next turn end.
            await _queue.EnqueueAsync(sessionId, recoveryBody, MessageSendMode.WhenIdle, ct);
            _logger.LogInformation("Compaction recovery note queued for session {SessionId} (seq {Sequence})",
                sessionId, sequence);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Compaction recovery note enqueue failed for session {SessionId}", sessionId);
        }
    }
}
