using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Application.Interfaces;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// CARD-0826 D-2. This type has no remover, repository lease or write dependency. Execute mode
/// never changes a worktree row's meaning or sends a retirement request.
/// </summary>
public sealed class HostCleanupWorktreeInventory(
    WorktreeIgnoredContentClassifier contentClassifier,
    TimeProvider clock)
{
    public HostCleanupWorktreeInventoryRow Classify(HostCleanupWorktreeFacts facts)
    {
        var content = contentClassifier.Classify(facts.IgnoredPaths);
        HostCleanupWorktreeInventoryRow Row(HostCleanupWorktreeDisposition disposition, string reason) =>
            new(facts, disposition, reason, content);

        if (!facts.ScanComplete || string.IsNullOrWhiteSpace(facts.Path) ||
            string.IsNullOrWhiteSpace(facts.StorageId) ||
            string.IsNullOrWhiteSpace(facts.OwnerGeneration) || facts.TaskId is null ||
            facts.AllocatedBytes is null or < 0 || facts.NewestWriteUtc is null)
            return Row(HostCleanupWorktreeDisposition.Unknown, "inventory_incomplete");
        if (facts.Kind == HostCleanupWorktreeKind.Unregistered)
            return Row(HostCleanupWorktreeDisposition.Unknown, "worktree_registration_unknown");
        if (facts.Kind == HostCleanupWorktreeKind.Slot)
            return Row(HostCleanupWorktreeDisposition.Keep, "slot_owned");
        if (content.Protected.Length > 0)
            return Row(HostCleanupWorktreeDisposition.Keep, "protected_content");
        if (facts.NewestWriteUtc >= clock.GetUtcNow() - TimeSpan.FromHours(24))
            return Row(HostCleanupWorktreeDisposition.Keep, "recent_write");
        if (facts.IsDirty == true || facts.HasUntrackedSource == true)
            return Row(HostCleanupWorktreeDisposition.Keep, "dirty_source");
        if (facts.IsTaskActive == true)
            return Row(HostCleanupWorktreeDisposition.Keep, "task_active");
        if (facts.HasLiveSession == true)
            return Row(HostCleanupWorktreeDisposition.Keep, "session_live");
        if (facts.HasPendingLandOrRecovery == true)
            return Row(HostCleanupWorktreeDisposition.Keep, "land_or_recovery_pending");
        if (facts.IsPushed == false)
            return Row(HostCleanupWorktreeDisposition.Keep, "unpushed");
        if (facts.IsCodeOrSourceLanding)
            return Row(HostCleanupWorktreeDisposition.Keep, "source_landing_owner");
        if (facts.IsDirty is null || facts.HasUntrackedSource is null ||
            facts.IsTaskActive is null || facts.HasLiveSession is null ||
            facts.HasPendingLandOrRecovery is null || facts.IsPushed is null ||
            facts.IsContainedInPushedBranch is null || facts.HasRelease is null ||
            facts.EvidencePreserved is null)
            return Row(HostCleanupWorktreeDisposition.Unknown, "safety_observation_unknown");
        if (facts.IsContainedInPushedBranch == false)
            return Row(HostCleanupWorktreeDisposition.Unknown, "branch_reachability_unknown");
        if (facts.HasRelease == false)
            return Row(HostCleanupWorktreeDisposition.Keep, "release_required");
        if (facts.EvidencePreserved == false || content.Evidence.Length > 0 && !facts.EvidencePreserved.Value)
            return Row(HostCleanupWorktreeDisposition.Keep, "evidence_required");
        return Row(HostCleanupWorktreeDisposition.WouldRemove, "eligible_existing_owner_only");
    }

    public async Task<HostCleanupWorktreeInventoryResult> ReadAsync(
        string hostId, IHostCleanupWorktreeProbe probe, IHostCleanupExistingOwnerStatusProbe owners,
        CancellationToken cancellationToken = default)
    {
        const int pageSize = 200;
        const int maxPages = 100;
        var rows = new List<HostCleanupWorktreeInventoryRow>();
        var ownerReasons = new Dictionary<string, string?>(StringComparer.Ordinal);
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        for (var pageNumber = 0; pageNumber < maxPages; pageNumber++)
        {
            var page = await probe.ReadPageAsync(hostId, cursor, pageSize, cancellationToken);
            foreach (var facts in page.Items)
            {
                if (rows.Count >= 10_000) break;
                var row = Classify(facts);
                if (!ownerReasons.TryGetValue(facts.ExistingOwner, out var reason))
                {
                    try { reason = await owners.ReadAvailabilityReasonAsync(facts.ExistingOwner, cancellationToken); }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    { reason = "owner_unavailable"; }
                    ownerReasons.Add(facts.ExistingOwner, reason);
                }
                rows.Add(row with { OwnerAvailabilityReason = reason });
            }
            if (rows.Count >= 10_000)
                return new(rows, false, "candidate_cap");
            if (!page.Complete)
                return new(rows, false, page.IncompleteReason ?? "page_incomplete");
            if (page.NextCursor is null) return new(rows, true, null);
            if (!seenCursors.Add(page.NextCursor))
                return new(rows, false, "cursor_repeated");
            cursor = page.NextCursor;
        }
        return new(rows, false, "page_cap");
    }

    public static HostCleanupWorktreeInventorySummary Summarize(
        IEnumerable<HostCleanupWorktreeInventoryRow> rows)
    {
        var materialized = rows.ToArray();
        var eligible = materialized.Where(row => row.Disposition == HostCleanupWorktreeDisposition.WouldRemove).ToArray();
        return new(
            eligible.Length,
            eligible.Sum(row => row.Facts.AllocatedBytes ?? 0),
            materialized.Count(row => row.Disposition == HostCleanupWorktreeDisposition.Keep),
            materialized.Count(row => row.Disposition == HostCleanupWorktreeDisposition.Unknown),
            eligible.GroupBy(row => row.Facts.ExistingOwner, StringComparer.Ordinal)
                .ToDictionary(group => group.Key,
                    group => (group.Count(), group.Sum(row => row.Facts.AllocatedBytes ?? 0)),
                    StringComparer.Ordinal));
    }
}
