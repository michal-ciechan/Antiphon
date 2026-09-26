using System.Globalization;
using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Services;

public sealed partial class AttentionService
{
    private List<AttentionItemDto> BuildRunnerUnavailableItems()
    {
        var episodes = _alarms?.Current.Episodes;
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

    private List<AttentionItemDto> BuildJournalStaleItems()
    {
        var findings = _alarms?.Current.Journals;
        if (findings is null) return [];

        var items = new List<AttentionItemDto>();
        foreach (var finding in findings.Where(finding => finding.StaleCount >= 1))
        {
            var stale = finding.Records.Where(record => record.Stale)
                .OrderBy(record => record.WrittenAt).ToList();
            var commonKey = Path.TrimEndingDirectorySeparator(Path.GetFullPath(finding.CommonDirectory));
            var command = $"pwsh -NoProfile -File scripts/recover-repository-children.ps1 -Repository {finding.Repository} "
                + "-Execute -ConfirmDescendantsExited";
            var evidence = new List<string>
            {
                $"repository={finding.Repository}; commonDirectory={commonKey}",
                $"After confirming descendants have exited: {command}",
            };
            evidence.AddRange(finding.Records.OrderBy(record => record.WrittenAt).Select(record =>
                $"file={record.File}; state={record.State}; "
                    + $"ageSeconds={record.Age.TotalSeconds.ToString("F0", CultureInfo.InvariantCulture)}; "
                    + $"pid={record.ProcessId?.ToString(CultureInfo.InvariantCulture) ?? "unknown"}; "
                    + $"writtenAt={record.WrittenAt:O}"));
            items.Add(new AttentionItemDto(
                AttentionKind.RepositoryChildJournalStale, AlertSeverity.Error,
                null, null, null, null,
                $"Repository child journal stale: {finding.Repository}",
                $"{finding.StaleCount} stale child-journal record(s) fence repository mutation.",
                string.Join("\n", evidence),
                stale.Count > 0 ? stale[0].WrittenAt.UtcDateTime : finding.InspectedAt.UtcDateTime,
                null, [AttentionAction.OpenDrawer],
                ConditionKey: $"journal-stale:{commonKey}"));
        }
        return items;
    }
}
