using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class WorkspaceDiskAdmission(
    HostStatsCache hostStats, IOptions<WorkspaceDiskAlarmSettings> settings, TimeProvider time)
{
    public const int MinimumFreeGiB = 20;

    public string? HoldReason(string runnerId)
    {
        var configuration = settings.Value;
        if (!configuration.Runners.TryGetValue(runnerId, out var path)
            || hostStats.Latest(runnerId) is not { } sample
            || time.GetUtcNow() - sample.ObservedAt > TimeSpan.FromMinutes(configuration.SampleFreshMinutes))
            return null;

        var disk = sample.Snapshot.Current?.Disks?
            .FirstOrDefault(disk => string.Equals(disk.Path, path, StringComparison.Ordinal));
        if (disk is not { TotalBytes: > 0, FreeBytes: >= 0 } || disk.FreeBytes > disk.TotalBytes
            || disk.FreeBytes >= (long)MinimumFreeGiB << 30)
            return null;

        return $"workspace_disk_low: {path} has less than {MinimumFreeGiB} GiB available; free space to resume dispatch";
    }
}
