namespace Antiphon.Server.Application.Dtos;

/// <summary>CARD-1124: the owner's current-attempt park episode. Display only; never release authority.</summary>
public sealed record RunnerSlotParkDto(
    Guid ParkId,
    string State,
    string ReasonCode,
    Guid? ReleaseId,
    string SyncState);

/// <summary>CARD-0653: one seat the runner still remembers.</summary>
public sealed record RunnerSlotDto(
    Guid SessionId,
    string Status,
    string? ExitReason,
    int AgeSeconds,
    bool OccupiesCapacity,
    bool Orphan,
    int? Pid,
    string? CustodyBackend,
    Guid? CustodyExecutionId,
    string? DesktopStatus,
    Guid? OpenTaskId,
    RunnerSlotParkDto? Park = null);

public sealed record RunnerSlotsDto(
    string RunnerId,
    int? DeclaredCapacity,
    int Occupied,
    IReadOnlyList<RunnerSlotDto> Slots);

public sealed record RunnerSlotReleaseRequest(string? Reason);

/// <summary>
/// <see cref="Intents"/> is set when a failed request was finished by reconciliation: each intent
/// this request recorded, with <c>released</c>, <c>failed</c> or <c>pending</c>.
/// </summary>
public sealed record RunnerSlotReleaseDto(
    int Released,
    IReadOnlyList<Guid> SessionIds,
    IReadOnlyList<RunnerSlotIntentOutcomeDto>? Intents = null,
    int? Candidates = null,
    int? Deferred = null,
    IReadOnlyList<RunnerSeatDiscoveryItem>? Dispositions = null);

public sealed record RunnerSlotIntentOutcomeDto(Guid IntentId, Guid SessionId, string Outcome);

/// <summary>Caller-retained scan position, never release authority. Inventory is fetched afresh
/// on every pass; the existing List transport is not a paged or partial absence certificate.</summary>
public sealed record RunnerSeatDiscoveryCursor(string? NextRunnerId, IReadOnlyDictionary<string, Guid> AfterSession);
public sealed record RunnerSeatDiscoveryItem(string RunnerId, Guid SessionId, Guid? ReleaseId, string Disposition);
public sealed record RunnerSeatDiscoveryResult(
    IReadOnlyList<RunnerSeatDiscoveryItem> Candidates, int Released, int InventoryCalls,
    RunnerSeatDiscoveryCursor Continuation);
