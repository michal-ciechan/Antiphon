using Antiphon.Server.Application.Settings;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class WorkspaceDiskAdmission(
    HostStatsCache hostStats, IOptions<WorkspaceDiskAlarmSettings> settings, TimeProvider time,
    ILogger<WorkspaceDiskAdmission> logger)
{
    public const int MinimumFreeGiB = 20;
    private readonly object _diagnosticGate = new();
    private readonly Dictionary<(string Runner, string Reason), DateTimeOffset> _lastDiagnostic = [];

    public string? HoldReason(string runnerId)
    {
        var configuration = settings.Value;
        if (!configuration.AdmissionRunners.TryGetValue(runnerId, out var path)
            && !configuration.Runners.TryGetValue(runnerId, out path))
            return null;

        var now = time.GetUtcNow();
        var freshness = TimeSpan.FromMinutes(configuration.SampleFreshMinutes);
        if (hostStats.Latest(runnerId) is not { } sample)
            return ReportDegraded(runnerId, path, "missing", now, freshness);
        if (now - sample.ObservedAt > freshness)
            return ReportDegraded(runnerId, path, "stale", now, freshness);

        var disk = sample.Snapshot.Current?.Disks?
            .FirstOrDefault(disk => string.Equals(disk.Path, path, StringComparison.Ordinal));
        if (disk is not { TotalBytes: > 0, FreeBytes: >= 0 } || disk.FreeBytes > disk.TotalBytes)
            return ReportDegraded(runnerId, path, "invalid", now, freshness);
        if (disk.FreeBytes >= (long)MinimumFreeGiB << 30)
            return null;

        return $"workspace_disk_low: {path} has less than {MinimumFreeGiB} GiB available; free space to resume dispatch";
    }

    private string? ReportDegraded(string runnerId, string path, string reason, DateTimeOffset now, TimeSpan interval)
    {
        lock (_diagnosticGate)
        {
            var key = (runnerId, reason);
            if (_lastDiagnostic.TryGetValue(key, out var previous) && now - previous < interval)
                return null;
            _lastDiagnostic[key] = now;
        }
        logger.LogWarning("workspace_disk_sample_unavailable: {RunnerId} {WorkspacePath} sample is {Reason}; disk admission cannot assess free space",
            runnerId, path, reason);
        return null;
    }
}
