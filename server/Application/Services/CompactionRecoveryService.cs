using System.Collections.Concurrent;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        using (var rulesScope = _scopeFactory.CreateScope())
        {
            var rulesDb = rulesScope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (await rulesDb.AgentSessions.AnyAsync(s => s.Id == sessionId && s.AgentKind == AgentKind.Grok && s.GrokRulesGeneration != null, ct))
            {
                await rulesScope.ServiceProvider.GetRequiredService<GrokRulesRefreshService>().ReconcileAsync(sessionId, ct);
                return;
            }
        }
        // Cheap same-process dedupe; the durable check below is the one that matters.
        var seen = _handledSequences.GetOrAdd(sessionId, 0);
        if (sequence <= seen)
            return;
        await using var scope = _scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var session = await db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.CompactionRecoveryWatermark >= sequence) return;
        var sessionText = sessionId.ToString("D");
        var agent = await db.Agents.AsNoTracking().FirstOrDefaultAsync(a => a.PersistentSessionId == sessionText, ct);
        var recoveryBody = string.IsNullOrWhiteSpace(agent?.SystemPromptAppend) ? null : ChannelPreamble.RecoveryNoteBody;
        var compactionBody = session.AgentKind == AgentKind.ClaudeCode ? null
            : await CompactionBodyAsync(scope.ServiceProvider, db, session, ct);

        // The transcript action remains the durable obligation until these stable queue keys
        // exist. An interrupted second enqueue retries without duplicating the first note.
        if (recoveryBody is not null)
            await _queue.EnqueueAsync(sessionId, recoveryBody, MessageSendMode.WhenIdle, ct,
                origin: QueuedMessageOrigin.System, deliverIfIdle: false,
                runtimeActionKey: $"compaction:{sequence}:recovery");
        if (compactionBody is not null)
            await _queue.EnqueueAsync(sessionId, compactionBody, MessageSendMode.WhenIdle, ct,
                origin: QueuedMessageOrigin.System, deliverIfIdle: false,
                runtimeActionKey: $"compaction:{sequence}:instructions");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var advanced = await db.AgentSessions.Where(s => s.Id == sessionId
            && (s.CompactionRecoveryWatermark == null || s.CompactionRecoveryWatermark < sequence))
            .ExecuteUpdateAsync(s => s.SetProperty(s => s.CompactionRecoveryWatermark, sequence), ct);
        if (advanced > 0 && agent is not null)
        {
            var supervisor = scope.ServiceProvider.GetRequiredService<AgentSupervisorService>();
            await supervisor.RecordIncidentAsync(agent.Id, sessionId, AgentIncidentKind.ContextCompacted,
                AlertSeverity.Info, $"Context compacted (transcript boundary seq {sequence}).", raiseAlert: false, ct: ct);
            await db.SaveChangesAsync(ct);
        }
        await tx.CommitAsync(ct);
        _handledSequences.AddOrUpdate(sessionId, sequence, (_, prior) => Math.Max(prior, sequence));
    }
    private static async Task<string?> CompactionBodyAsync(
        IServiceProvider services, AppDbContext db, AgentSession session, CancellationToken ct)
    {
        var settings = services.GetRequiredService<IOptions<DelegationSettings>>().Value;
        if (!settings.OrchestratorInstructions.Enabled)
            return null;

        var population = await OrchestratorInstructionsService.LoadPopulationAsync(db, ct);
        var chosen = OrchestratorInstructionsRecipients.Select(
            population.Agents,
            population.Sessions,
            population.Tasks,
            currentVersion: null,
            OrchestratorInstructionsNotify.All);
        if (!chosen.Contains(session.Id))
            return null;

        var path = AntiphonDataPaths.ResolveOrchestratorInstructionsPath(
            OrchestratorInstructionsPaths.FromCurrentProcess(),
            settings.OrchestratorInstructions.Path);
        var url = settings.ApiBaseUrl.TrimEnd('/') + "/api/orchestrator-instructions";
        return ChannelPreamble.OrchestratorInstructionsCompactionBody(path, url);
    }
}
