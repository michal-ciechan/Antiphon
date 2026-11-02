namespace Antiphon.Server.Application.Interfaces;

/// <summary>
/// CARD-0589 S4: the server-side counterpart of <c>scripts/lib/build-slot.ps1</c>, with the same
/// wait, timeout and unreachable rules (D-6). Every <c>BUILD SLOT ...</c> line goes to
/// <paramref name="report"/> so the landing evidence shows a budgeted run from an unbudgeted one.
/// </summary>
public interface IBuildSlotGate
{
    Task<BuildSlotHold> AcquireAsync(string label, Action<string> report, CancellationToken ct);

    /// <summary>
    /// CARD-1470/1467: as above, and <paramref name="memoryWait"/> sees each memory-floor refusal
    /// as it arrives, so the land status can say "waiting for memory" while the verifier waits.
    /// </summary>
    Task<BuildSlotHold> AcquireAsync(string label, Action<string> report, Action<BuildSlotMemoryWait>? memoryWait, CancellationToken ct)
        => AcquireAsync(label, report, ct);

    /// <summary>Releases a granted lease; a no-op for any other outcome. Never throws for a runner fault.</summary>
    Task ReleaseAsync(BuildSlotHold hold, Action<string> report, CancellationToken ct);
}

public enum BuildSlotHoldOutcome
{
    Granted,
    Unlimited,
    Unleased,
    Timeout,
}

/// <summary>
/// <see cref="MaxCpuCount"/> is the <c>-maxcpucount</c> to build with (0 on a timeout).
/// <see cref="MemoryWait"/> is set when the runner refused with its memory floor at least once
/// (CARD-1470), including the memory-floor fallback build.
/// </summary>
public sealed record BuildSlotHold(
    BuildSlotHoldOutcome Outcome,
    Guid? LeaseId,
    int MaxCpuCount,
    TimeSpan Waited,
    int QueuePosition = 0,
    long GrantedTimestamp = 0,
    BuildSlotMemoryWait? MemoryWait = null);

/// <summary>
/// CARD-1470/1467: the verifier's wait on the runner's memory floor. <see cref="Waited"/> is the
/// total time spent between memory-floor refusals and the next answer; <see cref="Fallback"/> marks
/// the unleased memory-floor fallback build at <see cref="MaxCpuCount"/>.
/// </summary>
public sealed record BuildSlotMemoryWait(long AvailableMb, long FloorMb, TimeSpan Waited, bool Fallback = false, int MaxCpuCount = 0)
{
    /// <summary>The land-record form, recorded on the land operation after <c>build-slot=</c>.</summary>
    public string Describe() => Fallback
        ? $"memory_floor_fallback maxcpucount={MaxCpuCount} available={AvailableMb}MB floor={FloorMb}MB waited={(int)Waited.TotalSeconds}s"
        : $"memory_floor_wait available={AvailableMb}MB floor={FloorMb}MB waited={(int)Waited.TotalSeconds}s";
}
