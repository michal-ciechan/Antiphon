namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-0710. One row of GET /api/session-runners. No secrets or workspace roots.</summary>
public sealed record SessionRunnerCatalogueEntryDto(
    string RunnerId,
    string DisplayName,
    string? Platform,
    DateTimeOffset? PlatformObservedAt,
    bool Available,
    bool DispatchEligible,
    string? UnavailableReason,
    int? Capacity,
    int? Occupied,
    string CapacityKind,
    DateTimeOffset? CapacityObservedAt,
    bool Stale,
    IReadOnlyList<string> Features,
    bool Draining = false,
    bool AcceptingNewWork = false);
