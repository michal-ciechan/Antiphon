using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AttentionService
{
    // CARD-0691: these are read-time projections, never another cleanup path.
    private async Task<List<AttentionItemDto>> BuildPoolDelegateUnreleasedItemsAsync(
        DateTime now, HashSet<Guid> remoteLive, HashSet<Guid> remoteUnknown, CancellationToken ct)
    {
        var agents = await _db.Agents.AsNoTracking()
            .Where(a => a.IsPoolDelegate && !a.AlwaysOn && a.BoardId == null
                && a.StandingSpecialistRole == null && a.StandingSpecialistOwnerId == null
                && a.Status != AgentStatus.Stopped && a.PoolIdleSince == null
                && a.PersistentSessionId != null)
            .ToListAsync(ct);
        var pointers = agents.Select(a => (Agent: a, SessionId: ParseSessionId(a.PersistentSessionId)))
            .Where(a => a.SessionId.HasValue).ToList();
        var ids = pointers.Select(a => a.SessionId!.Value).Distinct().ToList();
        if (ids.Count == 0) return [];

        var sessions = await _db.AgentSessions.AsNoTracking()
            .Where(s => ids.Contains(s.Id) && s.EndedAt == null
                && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running
                    || s.Status == SessionStatus.Stopping))
            .ToDictionaryAsync(s => s.Id, ct);
        var agentIds = agents.Select(a => a.Id).ToList();
        var tasks = (await _db.AgentTasks.AsNoTracking()
                .Where(t => t.AgentId != null && agentIds.Contains(t.AgentId.Value))
                .Select(t => new { t.Id, t.AgentId, t.Status, t.CompletedAt, t.SourceLandingOperationId })
                .ToListAsync(ct)).ToLookup(t => t.AgentId!.Value);
        var working = await SessionMessageQueueService.IsWorkingBatchAsync(_db, ids, ct);
        var cutoff = now.AddSeconds(-2d * Math.Max(0, _delegation.PoolReleaseGraceSeconds));
        var items = new List<AttentionItemDto>();
        foreach (var (agent, sessionId) in pointers)
        {
            if (!sessions.TryGetValue(sessionId!.Value, out var session)
                || !RemoteLeakConfirmed(session, remoteLive, remoteUnknown)
                || working.GetValueOrDefault(session.Id)) continue;
            var owned = tasks[agent.Id].ToList();
            if (owned.Any(t => t.SourceLandingOperationId != null || IsOpenLeakTask(t.Status))) continue;
            var newest = owned.Where(t => t.CompletedAt != null).MaxBy(t => t.CompletedAt);
            if (newest?.CompletedAt is not DateTime completed || completed >= cutoff) continue;
            items.Add(new AttentionItemDto(
                AttentionKind.PoolDelegateUnreleased, AlertSeverity.Error,
                newest.Id, session.Id, agent.Id, null, agent.Name,
                $"Pool delegate has not been released for {(now - completed).TotalMinutes:F1} minutes.",
                $"Task {newest.Id} settled {newest.Status} at {completed:O}; AgentKind={session.AgentKind}; "
                    + $"age={(now - completed).TotalSeconds:F0}s; runner={session.RunnerId ?? "local"}. "
                    + "No open task or warm-pool reservation owns this live session.",
                completed, null, [AttentionAction.OpenAgent, AttentionAction.OpenDrawer],
                ModelKind: session.AgentKind.ToString(), ConditionKey: $"pool-unreleased:{agent.Id:N}"));
        }
        return items;
    }

    private async Task<List<AttentionItemDto>> BuildSessionUnownedItemsAsync(
        DateTime now, HashSet<Guid> remoteLive, HashSet<Guid> remoteUnknown, CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-10);
        var candidates = await _db.AgentSessions.AsNoTracking()
            .Where(s => s.CardId == null && s.StandingAgentId == null && s.EndedAt == null
                && s.StartedAt < cutoff
                && (s.Status == SessionStatus.Starting || s.Status == SessionStatus.Running
                    || s.Status == SessionStatus.Stopping)
                && !_db.AgentTasks.Any(t => t.AgentSessionId == s.Id
                    && (t.Status == AgentTaskStatus.Queued || t.Status == AgentTaskStatus.Dispatched
                        || t.Status == AgentTaskStatus.Working || t.Status == AgentTaskStatus.Blocked)))
            .ToListAsync(ct);
        if (candidates.Count == 0) return [];
        // Pointers are strings; parse rather than assuming one GUID spelling/case.
        var owned = (await _db.Agents.AsNoTracking().Where(a => a.PersistentSessionId != null)
                .Select(a => a.PersistentSessionId).ToListAsync(ct))
            .Select(ParseSessionId).Where(id => id.HasValue).Select(id => id!.Value).ToHashSet();
        return candidates.Where(s => !owned.Contains(s.Id) && RemoteLeakConfirmed(s, remoteLive, remoteUnknown))
            .Select(s => new AttentionItemDto(
                AttentionKind.SessionUnowned, AlertSeverity.Warning, null, s.Id, null, null,
                $"Unowned session {s.Id.ToString("N")[..8]}",
                "Live session has no agent, card, standing owner or open task.",
                $"Session {s.Id}; AgentKind={s.AgentKind}; runner={s.RunnerId ?? "local"}; "
                    + $"started={s.StartedAt:O}; age={(now - s.StartedAt).TotalMinutes:F1} minutes.",
                s.StartedAt, null, [AttentionAction.OpenDrawer], ModelKind: s.AgentKind.ToString(),
                ConditionKey: $"session-unowned:{s.Id:N}")).ToList();
    }

    private async Task<List<AttentionItemDto>> BuildSessionStopStuckItemsAsync(
        DateTime now, IReadOnlyList<SessionRunnerSessionDto>? localInventory,
        HashSet<Guid> remoteLive, HashSet<Guid> remoteUnknown, CancellationToken ct)
    {
        var cutoff = now.AddMinutes(-5);
        var pending = await _db.AgentIncidents.AsNoTracking()
            .Where(i => i.Kind == AgentIncidentKind.RunnerSlotReleaseIntent && i.SessionId != null
                && i.FailureReason != null && i.FailureReason.StartsWith("pending:kill-generation:")
                && i.CreatedAt < cutoff)
            .OrderBy(i => i.CreatedAt).ToListAsync(ct);
        var pendingIds = pending.Select(i => i.SessionId!.Value).Distinct().ToList();
        var sessions = await _db.AgentSessions.AsNoTracking()
            .Where(s => (s.Status == SessionStatus.Stopping && s.LastSeenAt < cutoff) || pendingIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);
        if (sessions.Count == 0 && pendingIds.Count == 0) return [];
        var owners = (await _db.Agents.AsNoTracking().Where(a => a.PersistentSessionId != null)
                .Select(a => new { a.Id, a.PersistentSessionId }).ToListAsync(ct))
            .ToLookup(a => ParseSessionId(a.PersistentSessionId));
        var intents = pending.ToLookup(i => i.SessionId!.Value);
        var items = new List<AttentionItemDto>();
        foreach (var id in sessions.Keys.Concat(pendingIds).Distinct())
        {
            sessions.TryGetValue(id, out var session);
            var debt = intents[id].ToList();
            var owner = owners[id].FirstOrDefault();
            var liveness = session is null ? "unknown (no session row)"
                : session.RunnerId is not null
                    ? RemoteLeakConfirmed(session, remoteLive, remoteUnknown) ? "confirmed live (remote inventory)"
                        : _runnerDirectory is null || _runnerDirectory.RemoteInventoryPending(session.RunnerId)
                            || remoteUnknown.Contains(id) ? "unknown (remote inventory unavailable or stale)"
                            : "confirmed absent from remote live inventory"
                    : localInventory is null ? "unknown (local runner not consulted)"
                        : localInventory.Any(s => s.SessionId == id && s.Status == RunnerRunningStatus)
                            ? "confirmed live (local runner list)" : "not running in local runner list";
            var since = debt.Count > 0 ? debt[0].CreatedAt : session!.LastSeenAt;
            if (session?.Status == SessionStatus.Stopping && session.LastSeenAt < since) since = session.LastSeenAt;
            items.Add(new AttentionItemDto(
                AttentionKind.SessionStopStuck, AlertSeverity.Error, null, id, owner?.Id, null,
                $"Session {id.ToString("N")[..8]}",
                debt.Count > 0 ? $"Deferred kill still pending ({debt.Count} intent(s))." : "Session has been Stopping for over five minutes.",
                $"Runner liveness: {liveness}; runner={session?.RunnerId ?? "local/unknown"}; "
                    + $"AgentKind={session?.AgentKind.ToString() ?? "unknown"}. "
                    + $"{Excerpt(session?.FailureReason)} "
                    + string.Join("; ", debt.Take(5).Select(i => $"{i.FailureReason}: {Excerpt(i.Message)}")),
                since, null, owner is null ? [AttentionAction.OpenDrawer] : [AttentionAction.OpenAgent, AttentionAction.OpenDrawer],
                ModelKind: session?.AgentKind.ToString(), ConditionKey: $"session-stop-stuck:{id:N}"));
        }
        return items;
    }

    private async Task<List<AttentionItemDto>> BuildZombieCensusItemsAsync(CancellationToken ct)
    {
        var result = _censusState?.Latest;
        if (result is null) return [];
        var groups = result.Candidates.Where(r => r.IsCandidate && r.Class is ZombieCensusClass.PoolExpired
                or ZombieCensusClass.EndedButAlive or ZombieCensusClass.Unclaimed)
            .GroupBy(r => r.Class).ToList();
        var ids = groups.SelectMany(g => g).Where(r => r.SessionId.HasValue)
            .Select(r => r.SessionId!.Value).Distinct().ToList();
        var kinds = await _db.AgentSessions.AsNoTracking().Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, s => s.AgentKind.ToString(), ct);
        return groups.Select(group =>
        {
            var modelKinds = group.Select(r => r.SessionId is Guid id ? kinds.GetValueOrDefault(id) : null).Distinct().ToList();
            return new AttentionItemDto(
                AttentionKind.ZombieCensusReport, AlertSeverity.Warning, null, null, null, null,
                $"Zombie census: {group.Key}", $"{group.Count()} {group.Key} candidate(s) in the last local OS census.",
                $"GeneratedAtUtc={result.GeneratedAtUtc:O}. "
                    + string.Join("; ", group.Take(5).Select(r => $"pid={r.Pid}/agent={r.AgentName}/session={r.SessionId?.ToString() ?? "unknown"}")),
                result.GeneratedAtUtc.UtcDateTime, null, [AttentionAction.OpenDrawer],
                ModelKind: modelKinds.Count == 1 ? modelKinds[0] : null,
                ConditionKey: $"zombie-census:{group.Key}");
        }).ToList();
    }

    private bool RemoteLeakConfirmed(AgentSession session, HashSet<Guid> live, HashSet<Guid> unknown) =>
        session.RunnerId is null || (_runnerDirectory is not null
            && !_runnerDirectory.RemoteInventoryPending(session.RunnerId)
            && !unknown.Contains(session.Id) && live.Contains(session.Id));

    private static Guid? ParseSessionId(string? value) => Guid.TryParse(value, out var id) ? id : null;

    private static bool IsOpenLeakTask(AgentTaskStatus status) => status is AgentTaskStatus.Queued
        or AgentTaskStatus.Dispatched or AgentTaskStatus.Working or AgentTaskStatus.Blocked;
}
