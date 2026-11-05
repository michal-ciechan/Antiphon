using System.Globalization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AgentTaskDispatcher
{
    /// <summary>
    /// CARD-1610. One Warning incident per pooled session generation while that generation is
    /// Working, taskless, and older than the pool TTL. Later sweeps update the observation.
    /// A known turn younger than the TTL does not create one. Unknown turn start is the first
    /// observation, never a backdate from <see cref="Agent.PoolIdleSince"/>.
    /// </summary>
    private async Task<int> RecordPooledWorkingPastTtlAsync(
        IReadOnlyList<Agent> pool,
        IReadOnlyDictionary<Guid, DateTime> sessionStartedAt,
        IReadOnlySet<Guid> liveSessionIds,
        IReadOnlyDictionary<Guid, bool> workingSessions,
        IReadOnlySet<Guid> busyAgentIds,
        IReadOnlySet<Guid> failedLiveSessionIds,
        IReadOnlyDictionary<Guid, DateTime> lastTranscript,
        DateTime now,
        CancellationToken ct)
    {
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _settings.PoolIdleRetireMinutes));
        var candidates = new List<(Agent Agent, Guid SessionId, DateTime StartedAt)>();
        foreach (var agent in pool)
        {
            if (busyAgentIds.Contains(agent.Id))
                continue;
            if (!Guid.TryParse(agent.PersistentSessionId, out var sessionId))
                continue;
            if (!liveSessionIds.Contains(sessionId) || !workingSessions.GetValueOrDefault(sessionId))
                continue;
            if (failedLiveSessionIds.Contains(sessionId))
                continue;
            if (!sessionStartedAt.TryGetValue(sessionId, out var startedAt))
                continue;
            candidates.Add((agent, sessionId, startedAt));
        }

        if (candidates.Count == 0)
            return 0;

        var turns = await ReadCurrentTurnStartsAsync(candidates.Select(c => c.SessionId).ToList(), ct);
        var acted = 0;
        foreach (var (agent, sessionId, startedAt) in candidates)
        {
            turns.TryGetValue(sessionId, out var turn);
            var knownYoung = turn is DateTime start && AsUtc(now) - AsUtc(start) < ttl;
            var prefix = PooledWorkingEpisodePrefix(sessionId, startedAt);
            var existing = await FindPooledWorkingIncidentAsync(sessionId, prefix, ct);
            if (knownYoung && existing is null)
                continue;

            var message = PooledWorkingMessage(agent, sessionId, startedAt, turn, existing?.CreatedAt ?? now,
                lastTranscript, now);
            var reason = prefix + "|turn=" + (turn is DateTime known
                ? AsUtc(known).ToString("O", CultureInfo.InvariantCulture)
                : "unknown");
            var clippedReason = ColumnText.Clip(reason, AgentIncident.FailureReasonMaxLength);
            if (existing is null)
            {
                _db.AgentIncidents.Add(new AgentIncident
                {
                    Id = Guid.NewGuid(),
                    AgentId = agent.Id,
                    SessionId = sessionId,
                    Kind = AgentIncidentKind.PooledSessionWorkingPastTtl,
                    Severity = AlertSeverity.Warning,
                    Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength),
                    FailureReason = clippedReason,
                    CreatedAt = now,
                });
            }
            else if (!string.Equals(existing.FailureReason, clippedReason, StringComparison.Ordinal))
            {
                existing.Message = ColumnText.Clip(message, AgentIncident.MessageMaxLength);
                existing.FailureReason = clippedReason;
                existing.Severity = AlertSeverity.Warning;
            }
            else
            {
                continue;
            }

            acted++;
        }

        return acted;
    }

    private async Task<AgentIncident?> FindPooledWorkingIncidentAsync(
        Guid sessionId, string prefix, CancellationToken ct)
    {
        var marker = prefix + "|";
        var local = _db.AgentIncidents.Local.FirstOrDefault(incident =>
            incident.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl
            && incident.SessionId == sessionId
            && incident.FailureReason != null
            && incident.FailureReason.StartsWith(marker, StringComparison.Ordinal));
        if (local is not null)
            return local;
        return await _db.AgentIncidents.FirstOrDefaultAsync(incident =>
            incident.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl
            && incident.SessionId == sessionId
            && incident.FailureReason != null
            && incident.FailureReason.StartsWith(marker), ct);
    }

    private async Task<Dictionary<Guid, DateTime?>> ReadCurrentTurnStartsAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken ct)
    {
        var result = new Dictionary<Guid, DateTime?>();
        if (sessionIds.Count == 0)
            return result;
        var rows = await _db.TranscriptEntries.AsNoTracking()
            .Where(entry => sessionIds.Contains(entry.AgentSessionId))
            .Select(entry => new { entry.AgentSessionId, entry.Sequence, entry.Kind, entry.CreatedAt, entry.Text })
            .ToListAsync(ct);
        foreach (var group in rows.GroupBy(row => row.AgentSessionId))
        {
            long endSequence = 0;
            foreach (var row in group.OrderBy(row => row.Sequence))
            {
                if (IsTurnBoundary(row.Kind, row.Text))
                    endSequence = row.Sequence;
            }

            DateTime? start = null;
            foreach (var row in group.Where(row => row.Sequence > endSequence && IsTurnActivity(row.Kind, row.Text))
                         .OrderBy(row => row.CreatedAt))
            {
                start = row.CreatedAt;
                break;
            }

            result[group.Key] = start;
        }

        return result;
    }

    internal static string PooledWorkingEpisodePrefix(Guid sessionId, DateTime startedAt)
    {
        var generation = startedAt == default
            ? "unproved"
            : SessionGeneration.Normalize(startedAt).ToString("O", CultureInfo.InvariantCulture);
        return $"pooled-working:{sessionId:N}:{generation}";
    }

    private static string PooledWorkingMessage(
        Agent agent, Guid sessionId, DateTime startedAt, DateTime? turn, DateTime firstObserved,
        IReadOnlyDictionary<Guid, DateTime> lastTranscript, DateTime now)
    {
        var generation = startedAt == default
            ? "unproved"
            : SessionGeneration.Normalize(startedAt).ToString("O", CultureInfo.InvariantCulture);
        var turnText = turn is DateTime known
            ? AsUtc(known).ToString("O", CultureInfo.InvariantCulture)
            : "unknown";
        var age = turn is DateTime started ? AsUtc(now) - AsUtc(started) : AsUtc(now) - AsUtc(firstObserved);
        var activity = lastTranscript.TryGetValue(sessionId, out var at)
            ? AsUtc(at).ToString("O", CultureInfo.InvariantCulture)
            : "unknown";
        return "Pooled session working past TTL. Detection only; the session is never killed. "
            + $"agent={agent.Id:D} session={sessionId:D} name={agent.Name} generation={generation} "
            + $"turn={turnText} first={AsUtc(firstObserved):O} last={AsUtc(now):O} lastActivity={activity} "
            + $"age={age.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture)}min "
            + "Custody stays with the pool row until the session is proved idle, an open owner appears, "
            + "or the session authoritatively exits.";
    }

    private static bool IsTurnBoundary(string kind, string? text)
    {
        if (kind is TranscriptKinds.TurnEnd or TranscriptKinds.SessionRestartBoundary)
            return true;
        if (kind == TranscriptKinds.CompactBoundary
            && text is not null
            && text.Contains("(manual)", StringComparison.Ordinal))
            return true;
        return kind == TranscriptKinds.UserPrompt
            && text is not null
            && text.StartsWith("[Request interrupted", StringComparison.Ordinal);
    }

    private static bool IsTurnActivity(string kind, string? text)
    {
        if (kind is TranscriptKinds.TurnEnd or TranscriptKinds.TurnTitle or TranscriptKinds.SessionRestartBoundary
            or TranscriptKinds.QueuedUserPrompt or TranscriptKinds.QueueEnqueue or TranscriptKinds.QueueDequeue
            or TranscriptKinds.QueueRemove or TranscriptKinds.CompactBoundary)
            return false;
        if (kind != TranscriptKinds.UserPrompt || text is null)
            return true;
        return !text.StartsWith("<command-name>", StringComparison.Ordinal)
            && !text.StartsWith("<local-command-stdout>", StringComparison.Ordinal)
            && !text.StartsWith("This session is being continued from a previous conversation", StringComparison.Ordinal)
            && !text.StartsWith("[Request interrupted", StringComparison.Ordinal);
    }

    private static DateTime AsUtc(DateTime value) => FailedTaskSessionFacts.AsUtc(value);

    /// <summary>
    /// CARD-1719. Release a Failed seat whose recorded mirror is clean and equal to origin.
    /// The qualified coordinator, when it owns the task, has already taken the agent out of
    /// the pool; OwnsAutomaticPath is the second fence and a hold there is not force-released.
    /// A runner that cannot be released is kept. This never calls KillAsync.
    /// </summary>
    private async Task<int> ReleaseCleanGhostSeatsAsync(
        List<(Agent Agent, Guid? SessionId)> parsed,
        IReadOnlyDictionary<Guid, FailedTaskSessionOwnership.Binding> failedOwners,
        IReadOnlyDictionary<Guid, SessionStatus> sessionStatus,
        IReadOnlyDictionary<Guid, string?> sessionRunnerIds,
        IReadOnlyDictionary<Guid, DateTime> sessionStartedAt,
        IReadOnlySet<Guid> uncertainSessionIds,
        IReadOnlySet<Guid> missingTurnBoundary,
        IReadOnlyDictionary<Guid, bool> workingSessions,
        IReadOnlySet<Guid> busyAgentIds,
        IReadOnlySet<Guid> sourcedOwnerIds,
        HashSet<Agent> retire,
        DateTime now,
        CancellationToken ct)
    {
        if (failedOwners.Count == 0 || _runners is null)
            return 0;

        var ownerIds = failedOwners.Values.Select(owner => owner.Id).ToList();
        var tasks = await _db.AgentTasks.AsNoTracking()
            .Where(task => ownerIds.Contains(task.Id))
            .ToDictionaryAsync(task => task.Id, ct);
        var released = 0;
        var attempts = 0;
        foreach (var row in parsed)
        {
            if (attempts >= GhostSeatRelease.MaxReleaseAttemptsPerSweep)
                break;
            if (row.SessionId is not Guid sessionId || !failedOwners.TryGetValue(sessionId, out var owner))
                continue;
            if (!tasks.TryGetValue(owner.Id, out var task) || task.Status != AgentTaskStatus.Failed)
                continue;
            if (busyAgentIds.Contains(row.Agent.Id) || sourcedOwnerIds.Contains(row.Agent.Id))
                continue;
            if (workingSessions.GetValueOrDefault(sessionId)
                || uncertainSessionIds.Contains(sessionId)
                || missingTurnBoundary.Contains(sessionId))
                continue;
            if (row.Agent.Status != AgentStatus.Idle || row.Agent.PoolIdleSince is null)
                continue;
            if (sessionStatus.GetValueOrDefault(sessionId) != SessionStatus.Running)
                continue;
            if (_terminalSeatRelease?.OwnsAutomaticPath(task) == true)
                continue;

            var disposition = GhostSeatRelease.Read(
                TaskProgressJson.TryReadEvidence(task.CompletionProgressEvidenceJson)?.RemoteSync);
            if (disposition != GhostSeatRelease.MirrorDisposition.CleanEqual)
                continue;
            if (!GhostSeatRelease.IdleElapsed(task.CompletedAt ?? row.Agent.PoolIdleSince, now))
                continue;

            // The candidate snapshot is not release authority. Pull missed transcript activity,
            // then re-read custody and activity before the final settlement-record read.
            if (_runtime is not null)
                await _runtime.CatchUpTranscriptAsync(sessionId, ct);
            var session = await _db.AgentSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
            if (session is null || session.Status != SessionStatus.Running
                || !SessionGeneration.Equal(session.StartedAt, sessionStartedAt.GetValueOrDefault(sessionId))
                || session.RunnerId != sessionRunnerIds.GetValueOrDefault(sessionId)
                || _runtime?.RemoteInventoryPending(session.RunnerId) == true)
                continue;
            var agent = await _db.Agents.AsNoTracking().SingleOrDefaultAsync(a => a.Id == row.Agent.Id, ct);
            if (agent is null || !PoolDelegateRelease.CanRelease(agent)
                || agent.Status != AgentStatus.Idle || agent.PoolIdleSince is null
                || !Guid.TryParse(agent.PersistentSessionId, out var boundSession) || boundSession != sessionId
                || await PoolDelegateRelease.Reservations(_db).AnyAsync(p => p.AgentId == agent.Id, ct))
                continue;
            if (await _db.AgentTasks.AsNoTracking().AnyAsync(t =>
                (t.AgentId == agent.Id || t.AgentSessionId == sessionId)
                && (t.SourceLandingOperationId != null || t.Status == AgentTaskStatus.Queued
                    || t.Status == AgentTaskStatus.Dispatched || t.Status == AgentTaskStatus.Working
                    || t.Status == AgentTaskStatus.Blocked), ct))
                continue;
            var currentOwners = await FailedTaskSessionOwnership.CurrentOwnersAsync(_db,
                new Dictionary<Guid, (SessionStatus Status, DateTime StartedAt)>
                {
                    [sessionId] = (session.Status, session.StartedAt),
                }, ct);
            if (!currentOwners.TryGetValue(sessionId, out var currentOwner) || currentOwner.Id != task.Id)
                continue;
            var working = await SessionMessageQueueService.IsWorkingBatchAsync(_db, [sessionId], ct);
            if (working.GetValueOrDefault(sessionId)
                || (await SessionsWithoutTurnBoundaryAsync([sessionId], ct)).Contains(sessionId))
                continue;
            var activity = await _db.AgentSessions.AsNoTracking()
                .Where(s => s.Id == sessionId && s.Status == SessionStatus.Running
                    && s.StartedAt == session.StartedAt && s.RunnerId == session.RunnerId
                    && s.RunnerStoreId == session.RunnerStoreId)
                .Select(s => new
                {
                    s.LastSeenAt, s.CodexTokenCountAt,
                    LastTranscript = _db.TranscriptEntries.Where(t => t.AgentSessionId == s.Id)
                        .Max(t => (DateTime?)t.CreatedAt),
                }).SingleOrDefaultAsync(ct);
            if (activity?.LastTranscript is null)
                continue;

            var current = await _db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == task.Id, ct);
            if (current is null || current.Status != AgentTaskStatus.Failed || current.Attempt != task.Attempt
                || current.AgentId != agent.Id || current.AgentSessionId != sessionId
                || current.SourceLandingOperationId is not null
                || _terminalSeatRelease?.OwnsAutomaticPath(current) == true
                || GhostSeatRelease.Read(TaskProgressJson.TryReadEvidence(current.CompletionProgressEvidenceJson)?.RemoteSync)
                    != GhostSeatRelease.MirrorDisposition.CleanEqual)
                continue;
            // PoolIdleSince is only the fallback for an absent completion. The protected TTL
            // loop can refresh it, so taking max(completion, PoolIdleSince) would postpone forever.
            var since = new DateTime?[] { current.CompletedAt ?? agent.PoolIdleSince,
                activity.LastTranscript, activity.LastSeenAt, activity.CodexTokenCountAt }.Max();
            if (!GhostSeatRelease.IdleElapsed(since, UtcNow()))
                continue;

            var runnerId = session.RunnerId;
            if (string.IsNullOrWhiteSpace(runnerId))
                runnerId = current.RunnerId;
            if (string.IsNullOrWhiteSpace(runnerId))
                continue;

            var reason =
                $"Failed task {DelegationReportFormatter.Short(task.Id)} mirror clean and equal to origin, idle since {since:O}.";
            attempts++; // Failed runner calls also consume the per-sweep API budget.
            try
            {
                await RunnerSlotService.ReleaseAsync(_runners, _db, runnerId, sessionId, reason, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex,
                    "Ghost seat release kept session {SessionId} for Failed task {TaskId}", sessionId, task.Id);
                continue;
            }

            retire.Remove(row.Agent);
            released++;
        }

        return released;
    }
}
