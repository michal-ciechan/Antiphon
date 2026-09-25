namespace Antiphon.SessionRunner.Contracts;

// CARD-0589 D-3: the host build-slot broker's wire contract. One lease = one build/test driver
// invocation (dotnet build | run --project tests/* | test | publish), held by the wrapper process
// that asked for it and released when it exits or reaped when it dies. Owner:
// docs/testing-and-build.md "Build slots (CARD-0589)".

/// <summary>
/// <c>POST /build-slots</c>. <paramref name="Pid"/> and <paramref name="ProcessStartUtc"/> name the
/// holder (the wrapper process), so a lease whose holder died or whose pid was recycled is reaped.
/// </summary>
public sealed record BuildSlotRequest(
    int Pid,
    DateTime? ProcessStartUtc,
    string Label,
    string? SessionId = null,
    string? TaskId = null);

/// <summary>
/// 200 answer. <see cref="Unlimited"/> (with a null <see cref="LeaseId"/>) means the broker is
/// disabled and nothing is held; <see cref="MaxCpuCount"/> is the <c>-maxcpucount</c> to apply either way.
/// </summary>
public sealed record BuildSlotGrant(
    Guid? LeaseId,
    int MaxCpuCount,
    int Occupied,
    int Budget,
    DateTime? ExpiresAtUtc,
    bool Unlimited = false);

/// <summary>409 <see cref="BuildSlotProblemTypes.Busy"/> extension members.</summary>
public sealed record BuildSlotBusy(int Occupied, int Budget, int QueuePosition, int RetryAfterMs);

/// <summary>409 <see cref="BuildSlotProblemTypes.MemoryFloor"/> extension members.</summary>
public sealed record BuildSlotMemoryFloor(long AvailableMb, long FloorMb, int QueuePosition, int RetryAfterMs);

/// <summary>Live free memory; <see cref="AvailableMb"/> is null when the host cannot say.</summary>
public sealed record BuildSlotMemory(long? AvailableMb, long FloorMb);

public sealed record BuildSlotLease(
    Guid LeaseId,
    int Pid,
    string Label,
    string? SessionId,
    string? TaskId,
    DateTime GrantedAtUtc,
    DateTime ExpiresAtUtc,
    bool HolderAlive);

public sealed record BuildSlotWaiter(int Pid, string Label, DateTime SinceUtc, int Position);

/// <summary><c>GET /build-slots</c>.</summary>
public sealed record BuildSlotListing(
    bool Enabled,
    int Budget,
    int MaxCpuCount,
    int Occupied,
    BuildSlotMemory Memory,
    IReadOnlyList<BuildSlotLease> Leases,
    IReadOnlyList<BuildSlotWaiter> Waiters);

/// <summary>Problem-details <c>type</c> codes the wrappers branch on.</summary>
public static class BuildSlotProblemTypes
{
    public const string Busy = "build_slot_busy";
    public const string MemoryFloor = "build_slot_memory_floor";
    public const string Invalid = "build_slot_invalid";
    public const string Unknown = "build_slot_unknown";
}
