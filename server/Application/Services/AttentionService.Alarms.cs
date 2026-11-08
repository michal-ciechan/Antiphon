using System.Globalization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Agents.SessionRunner;
using Antiphon.SessionRunner.Contracts;
using Microsoft.Extensions.Configuration;

namespace Antiphon.Server.Application.Services;

public sealed partial class AttentionService
{
    private static List<AttentionItemDto> BuildRunnerUnavailableItems(RunnerAlarmSnapshot? snapshot)
    {
        var episodes = snapshot?.Episodes;
        if (episodes is null) return [];

        return episodes.Where(episode => episode.RaisedAt is not null)
            .Select(episode => new AttentionItemDto(
                AttentionKind.RunnerUnavailable, AlertSeverity.Error,
                null, null, null, null,
                $"Runner {episode.DisplayName} unavailable",
                $"{episode.PinnedOpenTasks} open task(s), {episode.LiveSessions} live session(s) pinned to an ineligible runner.",
                $"runner={episode.RunnerId}; downSince={episode.DownSince:O}; "
                    + $"lastReason={episode.LastReason ?? "unknown"}; "
                    + $"notified={episode.NotifiedSessionIds.Count}",
                episode.DownSince.UtcDateTime, null, [AttentionAction.OpenDrawer],
                ConditionKey: $"runner-unavailable:{episode.RunnerId}"))
            .ToList();
    }

    private static List<AttentionItemDto> BuildJournalStaleItems(RunnerAlarmSnapshot? snapshot)
    {
        var findings = snapshot?.Journals;
        if (findings is null) return [];

        var items = new List<AttentionItemDto>();
        foreach (var finding in findings.Where(finding => finding.StaleCount >= 1))
        {
            var stale = finding.Records.Where(record => record.Stale)
                .OrderBy(record => record.WrittenAt).ToList();
            var oldestAge = stale.Count > 0
                ? stale[0].Age.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture)
                : "unknown";
            var states = string.Join(", ", stale.Select(record => record.State).Distinct());
            var commonKey = Path.TrimEndingDirectorySeparator(Path.GetFullPath(finding.CommonDirectory));
            var command = $"pwsh -NoProfile -File scripts/recover-repository-children.ps1 -Repository {finding.Repository}";
            var evidence = new List<string>
            {
                $"common={commonKey}; records:",
                $"Recovery: run {command} first (preview), then {command} -Execute -ConfirmDescendantsExited after confirming the descendants exited. Unknown or malformed records are retained by the script and need inspection.",
            };
            evidence.AddRange(finding.Records.OrderBy(record => record.WrittenAt).Select(record =>
                $"file={record.File}; state={record.State}; "
                    + $"ageSeconds={record.Age.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}; "
                    + $"pid={record.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; "
                    + $"writtenAt={record.WrittenAt:O}"));
            items.Add(new AttentionItemDto(
                AttentionKind.RepositoryChildJournalStale, AlertSeverity.Error,
                null, null, null, null,
                $"Repository fenced: {finding.Repository}",
                $"{finding.StaleCount} stale child-journal record(s) ({states}) fence every land and dispatch here; oldest {oldestAge} min.",
                string.Join("\n", evidence),
                stale.Count > 0 ? stale[0].WrittenAt.UtcDateTime : finding.InspectedAt.UtcDateTime,
                null, [AttentionAction.OpenDrawer],
                ConditionKey: $"journal-stale:{commonKey}"));
        }
        return items;
    }

    /// <summary>
    /// CARD-1677 S3 (plan §3.6): one Warning per allow-listed runner whose configured worktree volume
    /// is at or above <see cref="WorkspaceDiskAlarmSettings.UsedPercent"/> used in a fresh sample.
    /// Computed per read from the host-stats cache, so a recovered volume clears its row and the key
    /// keeps one row per runner. Report only: nothing here deletes anything. CARD-1794: free space under
    /// <see cref="WorkspaceDiskAlarmSettings.MinFreeGiB"/> raises the same row as an Error, even below the percentage.
    /// </summary>
    private List<AttentionItemDto> BuildWorkspaceDiskLowItems(DateTimeOffset now)
    {
        var settings = _workspaceDiskAlarm;
        if (!settings.Enabled || _hostStats is null) return [];

        var items = new List<AttentionItemDto>();
        var retention = WorkspaceRetentionMode(_configuration);
        foreach (var (runnerId, volume) in settings.Runners.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            // D-11: the desktop is never alarmed, even when listed (the validator refuses that too).
            if (string.IsNullOrWhiteSpace(runnerId)
                || string.Equals(runnerId, RunnerPlatformWire.DesktopId, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(volume))
                continue;
            if (_hostStats.Latest(runnerId) is not { } sample) continue;
            // A sample the server first saw longer ago than the bound says nothing about now.
            if (now - sample.ObservedAt > TimeSpan.FromMinutes(settings.SampleFreshMinutes)) continue;
            var disk = sample.Snapshot.Current?.Disks?
                .FirstOrDefault(entry => string.Equals(entry.Path, volume, StringComparison.Ordinal));
            if (disk is null)
                continue;
            var underFloor = IsWorkspaceDiskUnderFloor(disk.FreeBytes, disk.TotalBytes, settings.MinFreeGiB);
            if (!underFloor && !IsWorkspaceDiskLow(disk.FreeBytes, disk.TotalBytes, settings.UsedPercent))
                continue;

            var usedPercent = (int)((Int128)100 * (disk.TotalBytes - disk.FreeBytes) / disk.TotalBytes);
            var freeGiB = (disk.FreeBytes / (double)(1L << 30)).ToString("0.#", CultureInfo.InvariantCulture);
            var totalGiB = (disk.TotalBytes / (double)(1L << 30)).ToString("0.#", CultureInfo.InvariantCulture);
            var minFreeBytes = (long)settings.MinFreeGiB << 30;
            items.Add(new AttentionItemDto(
                AttentionKind.WorkspaceDiskLow, underFloor ? AlertSeverity.Error : AlertSeverity.Warning,
                null, null, null, null,
                underFloor
                    ? $"Runner {runnerId} worktree disk {usedPercent}% used, {freeGiB} GiB free"
                    : $"Runner {runnerId} worktree disk {usedPercent}% used",
                underFloor
                    ? $"{volume} has {freeGiB} GiB free of {totalGiB} GiB, below the {settings.MinFreeGiB} GiB floor (alarm at {settings.UsedPercent}%). Report only: nothing is deleted; worktree retention is {retention}."
                    : $"{volume} has {freeGiB} GiB free of {totalGiB} GiB (alarm at {settings.UsedPercent}%). Report only: nothing is deleted; worktree retention is {retention}.",
                $"runner={runnerId}; path={volume}; usedPercent={usedPercent}; threshold={settings.UsedPercent}; "
                    + $"freeBytes={disk.FreeBytes.ToString(CultureInfo.InvariantCulture)}; "
                    + $"totalBytes={disk.TotalBytes.ToString(CultureInfo.InvariantCulture)}; "
                    + $"minFreeBytes={minFreeBytes.ToString(CultureInfo.InvariantCulture)}; "
                    + $"observedAt={sample.ObservedAt:O}; retention={retention}",
                sample.ObservedAt.UtcDateTime, null, [AttentionAction.OpenDrawer],
                ConditionKey: $"workspace-disk-low:{runnerId}"));
        }
        return items;
    }

    /// <summary>
    /// S-7: integers, so 85.0 percent is exactly 85. <see cref="Int128"/> keeps <c>100 * Total</c> exact
    /// for any long. A reading that cannot be a disk (no total, free outside 0..total) raises nothing.
    /// </summary>
    internal static bool IsWorkspaceDiskLow(long freeBytes, long totalBytes, int usedPercent) =>
        totalBytes > 0 && freeBytes >= 0 && freeBytes <= totalBytes
        && (Int128)100 * (totalBytes - freeBytes) >= (Int128)usedPercent * totalBytes;

    /// <summary>
    /// CARD-1794 (5): free bytes strictly below <paramref name="minFreeGiB"/> GiB on a usable reading. 0 is off.
    /// </summary>
    internal static bool IsWorkspaceDiskUnderFloor(long freeBytes, long totalBytes, int minFreeGiB) =>
        minFreeGiB > 0 && totalBytes > 0 && freeBytes >= 0 && freeBytes <= totalBytes
        && freeBytes < (long)minFreeGiB << 30;

    /// <summary>
    /// The retention mode from configuration (test design §Slices: not from S1's settings type, so the
    /// slices stay independent). Anything but a parsed <c>true</c> for <c>Enabled</c> is Off.
    /// </summary>
    internal static string WorkspaceRetentionMode(IConfiguration? configuration)
    {
        static bool IsTrue(string? value) => bool.TryParse(value, out var parsed) && parsed;
        if (configuration is null || !IsTrue(configuration["RunnerWorktreeRetention:Enabled"])) return "Off";
        return IsTrue(configuration["RunnerWorktreeRetention:Execute"]) ? "Execute" : "DryRun";
    }
}
