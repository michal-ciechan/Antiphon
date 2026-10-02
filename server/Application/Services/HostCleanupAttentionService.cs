using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Exceptions;
using Antiphon.Server.Application.Settings;
using Antiphon.Server.Domain.Entities;
using Antiphon.Server.Domain.Enums;
using Antiphon.Server.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Antiphon.Server.Application.Services;

public sealed class HostCleanupAttentionService(AppDbContext db, IOptions<HostCleanupSettings> settings,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<AttentionItemDto>> ReadAsync(Guid boardId, CancellationToken cancellationToken)
    {
        if (boardId == Guid.Empty) throw new ValidationException("boardId", "An explicit board is required.");
        var now = clock.GetUtcNow().UtcDateTime;
        var history = await db.HostCleanupRuns.AsNoTracking().Where(run => run.BoardId == boardId &&
            run.ReceiptDigest != null && run.FinishedAt <= now)
            .OrderBy(run => run.FinishedAt).ThenBy(run => run.Id).Take(10_001).ToListAsync(cancellationToken);
        // Refuse incomplete history rather than emit an empty projection that clears an episode.
        if (history.Count > 10_000 || history.Select(run => run.StorageId).Distinct().Count() > 100)
            throw new ConflictException("Cleanup history requires a bounded continuation.", "host_cleanup_history_incomplete");
        var result = new List<AttentionItemDto>();
        var policy = settings.Value;
        foreach (var group in history.GroupBy(run => run.StorageId, StringComparer.Ordinal))
        {
            var runs = group.ToArray();
            var latest = runs[^1];
            var stale = latest.FinishedAt < now - TimeSpan.FromDays(1);
            var eligibleCount = await db.HostCleanupCandidates.AsNoTracking()
                .CountAsync(candidate => candidate.RunId == latest.Id && candidate.Worktree &&
                    candidate.Disposition == "would-remove", cancellationToken);
            result.Add(Item(latest, AttentionKind.HostCleanupSummary, AlertSeverity.Warning,
                $"{latest.HostId}: host cleanup {latest.Status}",
                FormattableString.Invariant($"worktrees eligible but not removed by this job: {eligibleCount}, {latest.EligibleWorktreeBytes / (double)(1024L * 1024 * 1024):0.##} GiB"),
                $"status={latest.Status}; reclaimedBytes={latest.ReclaimedBytes}; worktreeReclaimedBytes=0; " +
                $"sample={(latest.SampleComplete ? "complete" : "unknown")}; " + (stale ? "report_stale" : "report_current"),
                "summary"));

            var daily = runs.Where(run => run.Daily).GroupBy(run => run.LocalDate)
                .Select(day => day.Last()).OrderBy(run => run.LocalDate).ToArray();
            var open = false;
            DateOnly? priorDay = null;
            var streak = new List<DateOnly>();
            DateOnly[] qualifyingDays = [];
            HostCleanupRun? qualified = null;
            foreach (var run in daily)
            {
                if (!run.Complete)
                {
                    streak.Clear(); priorDay = null;
                    continue;
                }
                if (run.EligibleWorktreeBytes < policy.BacklogBytes)
                {
                    open = false; streak.Clear(); priorDay = null; qualified = null;
                    continue;
                }
                if (priorDay is null || run.LocalDate.DayNumber != priorDay.Value.DayNumber + 1) streak.Clear();
                streak.Add(run.LocalDate); priorDay = run.LocalDate;
                if (streak.Count >= policy.BacklogDays)
                {
                    open = true; qualified = run;
                    qualifyingDays = streak.TakeLast(policy.BacklogDays).ToArray();
                }
            }
            if (open && qualified is not null)
            {
                var owners = await db.HostCleanupCandidates.AsNoTracking().Where(candidate =>
                    candidate.RunId == qualified.Id && candidate.Worktree && candidate.Disposition == "would-remove")
                    .Select(candidate => new { candidate.ExistingOwner, candidate.OwnerRefusalCode })
                    .Distinct().OrderBy(candidate => candidate.ExistingOwner).Take(101).ToListAsync(cancellationToken);
                if (owners.Count > 100)
                    throw new ConflictException("Cleanup owner inventory is incomplete.", "host_cleanup_history_incomplete");
                var owner = string.Join(",", owners.Select(row => row.ExistingOwner).Distinct());
                var refusal = string.Join(",", owners.Select(row => row.OwnerRefusalCode).Where(code => code is not null).Distinct());
                result.Add(Item(latest, AttentionKind.WorktreeCleanupBacklog, AlertSeverity.Warning,
                    $"{latest.HostId}: worktree cleanup backlog",
                    FormattableString.Invariant($"{qualified.EligibleWorktreeBytes / (double)(1024L * 1024 * 1024):0.##} GiB over {policy.BacklogDays} complete daily reports; owner: {owner}"),
                    $"observedDays={string.Join(',', qualifyingDays)}; owner={owner}; refusal={refusal}; worktreesInventoryOnly=true",
                    "backlog") with { HostCleanupOwner = owner, HostCleanupRefusal = string.IsNullOrEmpty(refusal) ? null : refusal });
            }

            AlertSeverity? volumePressure = null;
            AlertSeverity? diskPressure = null;
            HostCleanupRun? volumeObservation = null;
            HostCleanupRun? diskObservation = null;
            foreach (var run in runs.Where(run => run.SampleComplete))
            {
                if (run.NamespaceAllocatedBytes is { } used)
                {
                    var fraction = (double)used / policy.NamespaceBudgetBytes;
                    volumePressure = fraction >= policy.CriticalFraction ? AlertSeverity.Critical :
                        fraction >= policy.WarningFraction ? AlertSeverity.Warning : null;
                    volumeObservation = run;
                }
                if (run.FreeBytesAfter is { } free && run.DiskCapacityBytes is { } capacity)
                {
                    diskPressure = free < Math.Max(policy.MinimumFreeBytes, capacity * policy.MinimumFreeFraction)
                        ? AlertSeverity.Warning : null;
                    diskObservation = run;
                }
            }
            if (volumePressure is { } volumeSeverity && volumeObservation is { } volume)
                result.Add(Item(volume, AttentionKind.HostCleanupDiskPressure, volumeSeverity,
                    $"{latest.HostId}: namespace storage pressure", $"allocatedBytes={volume.NamespaceAllocatedBytes}",
                    $"budgetBytes={policy.NamespaceBudgetBytes}; sampledAt={volume.SampledAt:O}; limitsUnchanged=true", "namespace-pressure"));
            if (diskPressure is { } diskSeverity && diskObservation is { } disk)
                result.Add(Item(disk, AttentionKind.HostCleanupDiskPressure, diskSeverity,
                    $"{latest.HostId}: low disk free space", $"freeBytes={disk.FreeBytesAfter}",
                    $"capacityBytes={disk.DiskCapacityBytes}; sampledAt={disk.SampledAt:O}; limitsUnchanged=true", "disk-pressure"));
        }

        var holds = await db.HostCleanupHolds.AsNoTracking().Where(hold => hold.BoardId == boardId &&
            hold.DisposedAt == null && hold.ExpiresAt <= now).OrderBy(hold => hold.ExpiresAt).Take(1001)
            .ToListAsync(cancellationToken);
        if (holds.Count > 1000)
            throw new ConflictException("Cleanup hold inventory is incomplete.", "host_cleanup_history_incomplete");
        foreach (var hold in holds)
        {
            result.Add(new(AttentionKind.HostCleanupHoldExpired, AlertSeverity.Warning, null, null, null, null,
                $"{hold.HostId}: cleanup hold requires review", "Expired hold remains protected",
                "hold_expired_review_required", hold.ExpiresAt, null, [], BoardId: boardId,
                ConditionKey: $"host-cleanup-hold:{hold.Id:N}", HostCleanupHoldExpiryUtc: hold.ExpiresAt));
        }
        return result;
    }

    private static AttentionItemDto Item(HostCleanupRun run, AttentionKind kind, AlertSeverity severity,
        string title, string headline, string evidence, string condition) =>
        new(kind, severity, null, null, null, null, title, headline, evidence, run.FinishedAt, null, [],
            BoardId: run.BoardId, ConditionKey: $"host-cleanup:{run.BoardId:N}:{run.StorageId}:{condition}",
            HostCleanupRunId: run.Id);
}
