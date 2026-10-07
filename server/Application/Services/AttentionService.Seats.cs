using System.Globalization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

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
}
