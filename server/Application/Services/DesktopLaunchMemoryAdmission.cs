using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-1478. Snapshot backpressure for a new desktop worker. The floor reads physical
/// <see cref="RunnerHostCurrentDto.MemoryAvailableBytes"/> from the fresh cached desktop sample.
/// Windows swap fields are commit charge and limit, not available memory. A hold is not a
/// reservation and never stops a process that is already running.
/// </summary>
public sealed class DesktopLaunchMemoryAdmission(HostStatsCache cache, HostStatsSettings stats, TimeProvider time)
{
    /// <summary>
    /// Null admits. A non-null value is the stable hold detail for this sample.
    /// <paramref name="floorMb"/> of zero or less leaves admission off.
    /// </summary>
    public string? HoldDetail(int floorMb) =>
        floorMb <= 0
            ? null
            : Describe(floorMb, cache.Latest(RunnerPlatformWire.DesktopId), time.GetUtcNow(), stats.StaleAfterMs);

    internal static string? Describe(int floorMb, HostStatsSample? sample, DateTimeOffset now, int staleAfterMs)
    {
        if (floorMb <= 0)
            return null;
        if (sample is null)
            return DispatchHoldDetails.DesktopLaunchMemory("unknown", null, null, floorMb);

        var fresh = now - sample.ObservedAt <= TimeSpan.FromMilliseconds(staleAfterMs);
        if (!fresh)
            return DispatchHoldDetails.DesktopLaunchMemory("stale", sample.ObservedAt, null, floorMb);

        var available = AvailableMb(sample.Snapshot.Current?.MemoryAvailableBytes);
        if (available is null)
            return DispatchHoldDetails.DesktopLaunchMemory("fresh", sample.ObservedAt, null, floorMb);
        if (available.Value >= floorMb)
            return null;
        return DispatchHoldDetails.DesktopLaunchMemory("fresh", sample.ObservedAt, available, floorMb);
    }

    /// <summary>Whole mebibytes. Null and negative byte counts are unknown, never zero.</summary>
    private static long? AvailableMb(long? bytes) =>
        bytes is not long value || value < 0 ? null : value / (1024L * 1024L);
}
