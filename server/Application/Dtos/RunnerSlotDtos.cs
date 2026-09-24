namespace Antiphon.Server.Application.Dtos;

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
    Guid? OpenTaskId);

public sealed record RunnerSlotsDto(
    string RunnerId,
    int? DeclaredCapacity,
    int Occupied,
    IReadOnlyList<RunnerSlotDto> Slots);

public sealed record RunnerSlotReleaseRequest(string? Reason);

public sealed record RunnerSlotReleaseDto(int Released, IReadOnlyList<Guid> SessionIds);
