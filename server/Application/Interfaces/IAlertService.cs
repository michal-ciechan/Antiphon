using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Interfaces;

/// <summary>Raise request; DedupKey defaults to "source:title" when omitted.</summary>
public sealed record AlertRaise(
    AlertSeverity Severity,
    string Source,
    string Title,
    string? Detail = null,
    string? DedupKey = null,
    Guid? AgentId = null,
    Guid? SessionId = null);

/// <summary>
/// Fire-and-forget alert intake: persists, publishes SignalR `AlertRaised`, hands to the router.
/// MUST never throw into the caller — a broken alert pipeline cannot be allowed to take down the
/// thing that was trying to report a problem.
/// </summary>
public interface IAlertService
{
    Task RaiseAsync(AlertRaise alert, CancellationToken ct);
}

/// <summary>Delivery half (sinks + throttling). Slice 4 ships a no-op; slice 5 the channel router.</summary>
public interface IAlertRouter
{
    Task RouteAsync(Guid alertId, CancellationToken ct);
}
