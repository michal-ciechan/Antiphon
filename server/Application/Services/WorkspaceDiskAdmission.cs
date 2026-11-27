using Antiphon.Server.Application.Settings;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class WorkspaceDiskAdmission(
    HostStatsCache hostStats, IOptions<WorkspaceDiskAlarmSettings> settings, TimeProvider time,
    ILogger<WorkspaceDiskAdmission> logger)
{
    public const int MinimumFreeGiB = 20;
    private readonly object _diagnosticGate = new();
    private readonly Dictionary<(string Filesystem, string Reason), DateTimeOffset> _lastDiagnostic = [];

    public string? HoldReason(string runnerId)
    {
        var configuration = settings.Value;
        string? Volume(string route) => configuration.AdmissionRunners.GetValueOrDefault(route)
            ?? configuration.Runners.GetValueOrDefault(route);

        if (Volume(runnerId) is not { } path)
            return null;

        var filesystem = configuration.AdmissionFilesystems.GetValueOrDefault(runnerId);
        var routes = filesystem is null
            ? [runnerId]
            : configuration.AdmissionFilesystems.Where(pair => pair.Value == filesystem).Select(pair => pair.Key).ToArray();
        var now = time.GetUtcNow();
        var freshness = TimeSpan.FromMinutes(configuration.SampleFreshMinutes);
        var samples = hostStats.LatestFor(routes);
        var fresh = samples.Where(pair => now - pair.Value.ObservedAt <= freshness).ToArray();
        var measurement = fresh
            .Select(pair => (pair.Value.ObservedAt, Disk: FindDisk(pair.Value, Volume(pair.Key))))
            .Where(pair => pair.Disk is { TotalBytes: > 0, FreeBytes: >= 0 } && pair.Disk.FreeBytes <= pair.Disk.TotalBytes)
            .OrderByDescending(pair => pair.ObservedAt)
            // Concurrent samples can have the same timestamp. Use the lower free-space reading.
            .ThenBy(pair => pair.Disk!.FreeBytes)
            .Select(pair => pair.Disk)
            .FirstOrDefault();

        if (measurement is null)
        {
            var reason = fresh.Length > 0 ? "invalid" : samples.Count > 0 ? "stale" : "missing";
            ReportDegraded(runnerId, filesystem ?? runnerId, path, reason, now, freshness);
            return null;
        }

        return measurement.FreeBytes >= (long)MinimumFreeGiB << 30
            ? null
            : $"workspace_disk_low: {path} has less than {MinimumFreeGiB} GiB available; free space to resume dispatch";
    }

    private static RunnerHostDiskDto? FindDisk(HostStatsSample sample, string? path) =>
        sample.Snapshot.Current?.Disks?.FirstOrDefault(disk => string.Equals(disk.Path, path, StringComparison.Ordinal));

    private void ReportDegraded(string runnerId, string filesystem, string path, string reason, DateTimeOffset now, TimeSpan interval)
    {
        lock (_diagnosticGate)
        {
            var key = (filesystem, reason);
            if (_lastDiagnostic.TryGetValue(key, out var previous) && now - previous < interval)
                return;
            _lastDiagnostic[key] = now;
        }
        logger.LogWarning("workspace_disk_sample_unavailable: {RunnerId} {WorkspacePath} filesystem {Filesystem} sample is {Reason}; disk admission cannot assess free space",
            runnerId, path, filesystem, reason);
    }
}
