using System.Globalization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Antiphon.Server.Application.Services;

public sealed partial class AttentionService
{
    private List<AttentionItemDto> BuildSeatIdleItems(SeatOccupancySnapshot? snapshot, DateTime now)
    {
        if (snapshot is null) return [];
        var items = new List<AttentionItemDto>();
        foreach (var host in snapshot.Hosts)
        {
            if (!IsRunnerHost(host)) continue;
            foreach (var seat in host.Seats)
            {
                if (!seat.Occupies || !IsIdle(seat.Class)) continue;
                var age = now - seat.IdleSince;
                var severity = SeatOccupancyProjection.Severity(age, _attention);
                if (severity is null) continue;
                var status = seat.TaskStatus?.ToString() ?? "none";
                items.Add(new AttentionItemDto(
                    AttentionKind.SeatIdle, severity.Value,
                    seat.TaskId, seat.SessionId, seat.AgentId, null,
                    $"Idle seat on {seat.RunnerId}",
                    $"{status} for {AgeText(age)} min",
                    SeatEvidence(host, seat, snapshot, age),
                    seat.IdleSince, null, ActionsFor(seat),
                    CardId: seat.CardId, BoardId: seat.BoardId,
                    ConditionKey: SeatIdleKey(seat)));
            }
        }
        return items;
    }

    private List<AttentionItemDto> BuildOccupancyDivergenceItems(SeatOccupancySnapshot? snapshot, DateTime now)
    {
        if (snapshot is null) return [];
        var items = new List<AttentionItemDto>();
        foreach (var host in snapshot.Hosts)
        {
            if (!IsRunnerHost(host)) continue;
            var divergence = SeatOccupancyProjection.Divergence(host.InFlight, host.DispatchedWorking);
            if (divergence <= 0) continue;
            // Desktop idle count, not the seat list: an unavailable inventory still has no seats.
            if (host.IdleSeats < 1) continue;
            if (host.OldestIdleSince is not DateTime since) continue;
            var severity = SeatOccupancyProjection.Severity(now - since, _attention);
            if (severity is null) continue;
            items.Add(new AttentionItemDto(
                AttentionKind.OccupancyDivergence, severity.Value,
                null, null, null, null,
                $"Seats above Working on {host.HostId}",
                $"{host.InFlight} in flight, {host.DispatchedWorking} Dispatched/Working",
                DivergenceEvidence(host, snapshot),
                since, null, [AttentionAction.OpenDrawer],
                ConditionKey: DivergenceKey(host)));
        }
        return items;
    }

    private static List<AttentionItemDto> BuildSlotOrphanItems(SeatOccupancySnapshot? snapshot, DateTime now)
    {
        if (snapshot is null) return [];
        var items = new List<AttentionItemDto>();
        foreach (var host in snapshot.Hosts)
        {
            if (!IsRunnerHost(host)) continue;
            foreach (var seat in host.Seats)
            {
                if (!seat.Orphan || !seat.Occupies) continue;
                var since = IsIdle(seat.Class) ? seat.IdleSince : seat.StartedAt;
                var status = seat.TaskStatus?.ToString() ?? "none";
                items.Add(new AttentionItemDto(
                    AttentionKind.SlotOrphan, AlertSeverity.Warning,
                    seat.TaskId, seat.SessionId, seat.AgentId, null,
                    $"Orphan slot on {seat.RunnerId}",
                    $"Occupying orphan, {status}",
                    OrphanEvidence(host, seat, snapshot, now),
                    since, null, ActionsFor(seat),
                    CardId: seat.CardId, BoardId: seat.BoardId,
                    ConditionKey: SlotOrphanKey(seat)));
            }
        }
        return items;
    }

    private static bool IsRunnerHost(HostOccupancyObservation host) =>
        !string.Equals(host.Kind, "local", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(host.HostId, "local", StringComparison.OrdinalIgnoreCase);

    private static bool IsIdle(SeatClass seat) =>
        seat is SeatClass.IdleBlocked or SeatClass.IdleTerminal or SeatClass.IdleUnbound;

    private static string SeatIdleKey(SeatObservation seat) =>
        $"seat-idle:{seat.RunnerId}:{seat.SessionId:N}";

    private static string SlotOrphanKey(SeatObservation seat) =>
        $"slot-orphan:{seat.RunnerId}:{seat.SessionId:N}";

    private static string DivergenceKey(HostOccupancyObservation host) =>
        $"occupancy-divergence:{host.HostId}";

    private static AttentionAction[] ActionsFor(SeatObservation seat)
    {
        var actions = new List<AttentionAction>();
        if (seat.TaskStatus == AgentTaskStatus.Blocked)
        {
            actions.Add(AttentionAction.Reply);
            actions.Add(AttentionAction.Cancel);
        }
        actions.Add(AttentionAction.OpenDrawer);
        if (seat.AgentId is not null)
            actions.Add(AttentionAction.OpenAgent);
        return actions.ToArray();
    }

    private static string AgeText(TimeSpan age) =>
        age.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture);

    private static string AgeField(TimeSpan age) => $"age={AgeText(age)}min";

    private static string SeatEvidence(
        HostOccupancyObservation host, SeatObservation seat, SeatOccupancySnapshot snapshot, TimeSpan age) =>
        $"runner={seat.RunnerId}; session={seat.SessionId:D}; task={seat.TaskId?.ToString("D") ?? "unknown"}; "
        + $"attempt={seat.Attempt?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; "
        + $"status={seat.TaskStatus?.ToString() ?? "none"}; {AgeField(age)}; pushed={seat.Pushed}; "
        + $"observedAt={snapshot.GeneratedAt:O}; "
        + $"slotOrphan={SlotOrphanKey(seat)}; divergence={DivergenceKey(host)}; {ParkField(seat)}";

    private static string OrphanEvidence(
        HostOccupancyObservation host, SeatObservation seat, SeatOccupancySnapshot snapshot, DateTime now)
    {
        var age = now - (IsIdle(seat.Class) ? seat.IdleSince : seat.StartedAt);
        return $"runner={seat.RunnerId}; session={seat.SessionId:D}; task={seat.TaskId?.ToString("D") ?? "unknown"}; "
            + $"attempt={seat.Attempt?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; "
            + $"status={seat.TaskStatus?.ToString() ?? "none"}; {AgeField(age)}; pushed={seat.Pushed}; "
            + $"observedAt={snapshot.GeneratedAt:O}; "
            + $"seatIdle={SeatIdleKey(seat)}; divergence={DivergenceKey(host)}; {ParkField(seat)}";
    }

    private static string ParkField(SeatObservation seat)
    {
        var state = string.IsNullOrEmpty(seat.ParkState) ? "none" : seat.ParkState;
        if (state == "none" || string.IsNullOrEmpty(seat.ParkReason))
            return $"park={state}";
        return $"park={state}:{seat.ParkReason}";
    }

    private static string DivergenceEvidence(HostOccupancyObservation host, SeatOccupancySnapshot snapshot)
    {
        var lines = new List<string>
        {
            $"host={host.HostId}; inventory={host.InventoryState}; inFlight={host.InFlight}; "
            + $"dispatchedWorking={host.DispatchedWorking}; sessions={host.Sessions}; "
            + $"pendingLaunch={host.PendingLaunch}; mirrors={host.InFlightMirrors}; "
            + $"idleSeats={host.IdleSeats}; observedAt={snapshot.GeneratedAt:O}",
        };
        foreach (var seat in host.Seats.OrderBy(s => s.IdleSince).ThenBy(s => s.SessionId).Take(10))
        {
            lines.Add(
                $"seat={seat.SessionId:D}; class={seat.Class}; runner={seat.RunnerId}; "
                + $"idleSince={seat.IdleSince:O}; seatIdle={SeatIdleKey(seat)}; slotOrphan={SlotOrphanKey(seat)}");
        }
        if (host.Seats.Count > 10)
            lines.Add($"moreSeats={host.Seats.Count - 10}");
        return string.Join("\n", lines);
    }

    /// <summary>
    /// CARD-1610 S1. Detection only: a Failed task with an unreleased session, and a pooled
    /// Working session past the pool TTL. This projection never writes the incident.
    /// </summary>
    private async Task<List<AttentionItemDto>> BuildSupervisionVisibilityItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var items = new List<AttentionItemDto>();
        items.AddRange(await BuildFailedTaskSessionHeldItemsAsync(now, ct));
        items.AddRange(await BuildPooledWorkingPastTtlItemsAsync(now, ct));
        return items;
    }

    private async Task<List<AttentionItemDto>> BuildFailedTaskSessionHeldItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var held = new[]
        {
            SessionStatus.Created, SessionStatus.Starting, SessionStatus.Running, SessionStatus.Stopping,
        };
        // Bound by unfinished sessions that still have a Failed task. A global Take could hide
        // the current owner. Later tasks on those sessions and their agents are loaded so a
        // historical Failed row is not the owner.
        var sessionIds = await (
            from task in _db.AgentTasks.AsNoTracking()
            join session in _db.AgentSessions.AsNoTracking() on task.AgentSessionId equals session.Id
            where task.Status == AgentTaskStatus.Failed
                && task.AgentSessionId != null
                && held.Contains(session.Status)
                && session.StartedAt != default
            select session.Id).Distinct().ToListAsync(ct);
        if (sessionIds.Count == 0)
            return [];

        var sessions = await _db.AgentSessions.AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id))
            .ToDictionaryAsync(session => session.Id, ct);
        var candidates = sessions.ToDictionary(
            pair => pair.Key,
            pair => (pair.Value.Status, pair.Value.StartedAt));
        var owners = await FailedTaskSessionOwnership.CurrentOwnersAsync(_db, candidates, ct);
        if (owners.Count == 0)
            return [];

        var ownerIds = owners.Values.Select(owner => owner.Id).ToList();
        var tasks = await _db.AgentTasks.AsNoTracking()
            .Where(task => ownerIds.Contains(task.Id))
            .ToDictionaryAsync(task => task.Id, ct);
        var working = await SessionMessageQueueService.IsWorkingBatchAsync(
            _db, owners.Keys.ToList(), ct);
        var notices = await _db.TaskSupervisionNotices.AsNoTracking().Where(n => ownerIds.Contains(n.TaskId)
            && n.Purpose == TaskSupervisionNoticePurpose.FailureDelegate).ToListAsync(ct);
        var snapshot = _seats?.Current;
        var items = new List<AttentionItemDto>(owners.Count);
        foreach (var (sessionId, owner) in owners)
        {
            if (!sessions.TryGetValue(sessionId, out var session) || !tasks.TryGetValue(owner.Id, out var task))
                continue;
            var facts = FailedTaskSessionFacts.Describe(
                task, session, working.GetValueOrDefault(sessionId), snapshot, now,
                notices.Where(n => n.TaskId == task.Id && n.Attempt == task.Attempt)
                    .OrderByDescending(n => n.CreatedAt).FirstOrDefault()?.State.ToString());
            var actions = new List<AttentionAction> { AttentionAction.OpenDrawer };
            if (task.AgentId is not null)
                actions.Add(AttentionAction.OpenAgent);
            items.Add(new AttentionItemDto(
                AttentionKind.FailedTaskSessionHeld,
                AlertSeverity.Warning,
                task.Id,
                session.Id,
                task.AgentId,
                null,
                facts.Summary,
                facts.Activity,
                $"notice={facts.NoticeState ?? "none"}; failure=" + (task.FailureReason ?? "unknown")
                    + $"; activity={facts.Activity}; session={session.Id:D}; "
                    + $"agent={task.AgentId?.ToString("D") ?? "none"}; "
                    + "custody=held until authoritative exit or new ownership. Detection only; the session is never killed.",
                task.CompletedAt ?? session.StartedAt,
                null,
                actions,
                CardId: task.CardId));
        }

        return items;
    }

    private async Task<List<AttentionItemDto>> BuildPooledWorkingPastTtlItemsAsync(
        DateTime now, CancellationToken ct)
    {
        var incidents = await _db.AgentIncidents.AsNoTracking()
            .Where(incident => incident.Kind == AgentIncidentKind.PooledSessionWorkingPastTtl
                && incident.SessionId != null)
            .ToListAsync(ct);
        if (incidents.Count == 0)
            return [];

        var sessionIds = incidents.Select(incident => incident.SessionId!.Value).Distinct().ToList();
        var sessions = await _db.AgentSessions.AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id))
            .ToDictionaryAsync(session => session.Id, ct);
        var working = await SessionMessageQueueService.IsWorkingBatchAsync(_db, sessionIds, ct);
        var open = (await _db.AgentTasks.AsNoTracking()
            .Where(task => task.AgentSessionId != null
                && sessionIds.Contains(task.AgentSessionId.Value)
                && (task.Status == AgentTaskStatus.Queued
                    || task.Status == AgentTaskStatus.Dispatched
                    || task.Status == AgentTaskStatus.Working
                    || task.Status == AgentTaskStatus.Blocked))
            .Select(task => task.AgentSessionId!.Value)
            .ToListAsync(ct)).ToHashSet();
        var failedCandidates = sessions
            .Where(pair => pair.Value.Status is SessionStatus.Created or SessionStatus.Starting
                or SessionStatus.Running or SessionStatus.Stopping)
            .ToDictionary(pair => pair.Key, pair => (pair.Value.Status, pair.Value.StartedAt));
        var failed = (await FailedTaskSessionOwnership.CurrentOwnersAsync(_db, failedCandidates, ct))
            .Keys.ToHashSet();
        var ttl = TimeSpan.FromMinutes(Math.Max(1, _delegation.PoolIdleRetireMinutes));
        var items = new List<AttentionItemDto>();
        foreach (var incident in incidents)
        {
            var sessionId = incident.SessionId!.Value;
            if (!sessions.TryGetValue(sessionId, out var session))
                continue;
            if (session.Status is not (SessionStatus.Created or SessionStatus.Starting
                or SessionStatus.Running or SessionStatus.Stopping))
                continue;
            if (!PooledGenerationStillMatches(incident.FailureReason, session))
                continue;
            if (open.Contains(sessionId) || failed.Contains(sessionId))
                continue;
            if (!working.GetValueOrDefault(sessionId))
                continue;
            if (PooledWorkingAge(incident, now) < ttl)
                continue;

            var actions = new List<AttentionAction> { AttentionAction.OpenDrawer };
            if (incident.AgentId is not null)
                actions.Add(AttentionAction.OpenAgent);
            items.Add(new AttentionItemDto(
                AttentionKind.PooledSessionWorkingPastTtl,
                AlertSeverity.Warning,
                null,
                sessionId,
                incident.AgentId,
                null,
                "Pooled session working past TTL",
                "Working",
                incident.Message,
                incident.CreatedAt,
                null,
                actions));
        }

        return items;
    }

    private static bool PooledGenerationStillMatches(string? failureReason, AgentSession session)
    {
        if (!TryReadPooledGeneration(failureReason, out var generation))
            return true;
        if (generation is null)
            return session.StartedAt == default;
        return SessionGeneration.Equal(generation, session.StartedAt);
    }

    private static bool TryReadPooledGeneration(string? failureReason, out DateTime? generation)
    {
        generation = null;
        if (string.IsNullOrEmpty(failureReason)
            || !failureReason.StartsWith("pooled-working:", StringComparison.Ordinal))
            return false;
        var head = failureReason;
        var turnAt = head.IndexOf("|turn=", StringComparison.Ordinal);
        if (turnAt >= 0)
            head = head[..turnAt];
        var rest = head["pooled-working:".Length..];
        var split = rest.IndexOf(':');
        if (split != 32)
            return false;
        var token = rest[(split + 1)..];
        if (token == "unproved")
            return true;
        if (!DateTime.TryParse(token, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return false;
        generation = FailedTaskSessionFacts.AsUtc(parsed);
        return true;
    }

    private static TimeSpan PooledWorkingAge(AgentIncident incident, DateTime now)
    {
        if (TryReadPooledTurn(incident.FailureReason, out var turn) && turn is DateTime known)
            return FailedTaskSessionFacts.AsUtc(now) - FailedTaskSessionFacts.AsUtc(known);
        return FailedTaskSessionFacts.AsUtc(now) - FailedTaskSessionFacts.AsUtc(incident.CreatedAt);
    }

    private static bool TryReadPooledTurn(string? failureReason, out DateTime? turn)
    {
        turn = null;
        if (string.IsNullOrEmpty(failureReason))
            return false;
        const string marker = "|turn=";
        var at = failureReason.LastIndexOf(marker, StringComparison.Ordinal);
        if (at < 0)
            return false;
        var value = failureReason[(at + marker.Length)..];
        if (value == "unknown")
            return true;
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return false;
        turn = FailedTaskSessionFacts.AsUtc(parsed);
        return true;
    }
}
