namespace Antiphon.HostCleanup;

public sealed class CleanupPlanner
{
    private readonly CleanupFamilyRegistry registry;
    private readonly CleanupKeepList keepList;
    private readonly CleanupLimits limits;
    private readonly TimeProvider clock;

    public CleanupPlanner(CleanupFamilyRegistry registry, CleanupKeepList keepList,
        CleanupLimits limits, TimeProvider clock)
    {
        limits.Validate();
        this.registry = registry;
        this.keepList = keepList;
        this.limits = limits;
        this.clock = clock;
    }

    public CleanupDecision Decide(CleanupCandidateFacts facts)
    {
        var now = clock.GetUtcNow();
        if (!CleanupPath.IsSafe(facts.Path)) return CleanupDecision.Unknown(facts, "path_escape");
        var root = registry.FindRoot(facts.Path);
        if (root is null) return CleanupDecision.Unknown(facts, "root_unknown");
        if (facts.Identity is null || !StringComparer.Ordinal.Equals(facts.Identity.StorageId, root.StorageId))
            return CleanupDecision.Unknown(facts, "storage_identity_mismatch");
        if (registry.IsDenied(facts.Path) || facts.Entries.Any(entry => registry.IsDenied(entry.Path)))
            return CleanupDecision.Keep(facts, "protected_runtime");
        if (facts.IsWorktree) return CleanupDecision.Keep(facts, "worktree_inventory_only");
        if (facts.IsRetainedEvidence) return CleanupDecision.Keep(facts, "retained_evidence");
        if (!registry.IsFamilyShape(root, facts.Path))
            return CleanupDecision.Unknown(facts, "family_unknown");
        if (root.Family == CleanupFamily.PrivateCache && !facts.PrivateCachePairOwned)
            return CleanupDecision.Keep(facts, "private_cache_pair_required");
        if (!facts.Complete || facts.TraversedCount > limits.MaxDescendantsPerCandidate ||
            facts.Entries.Count > limits.MaxDescendantsPerCandidate ||
            facts.Entries.Any(entry => !entry.IsReadable))
            return CleanupDecision.Unknown(facts, "scan_incomplete");
        if (facts.Entries.Any(entry => !CleanupPath.IsSafe(entry.Path) ||
            !CleanupPath.IsSameOrChild(entry.Path, facts.Path)))
            return CleanupDecision.Unknown(facts, "path_escape");
        if (facts.Entries.Any(entry => entry.IsLink))
            return CleanupDecision.Unknown(facts, "linked_entry");
        if (facts.Entries.Any(entry => entry.IsMountBoundary))
            return CleanupDecision.Unknown(facts, "mount_crossing");
        var held = keepList.Veto(root.StorageId, facts.Path, facts.Identity.OwnerGeneration, now);
        if (held is not null) return CleanupDecision.Keep(facts, held);
        var custody = CleanupOwnership.Veto(facts);
        if (custody is not null) return CleanupDecision.Keep(facts, custody);
        if (facts.CreatedUtc is null) return CleanupDecision.Unknown(facts, "creation_unknown");

        var newest = facts.CreatedUtc.Value;
        long logical = 0, allocated = 0;
        foreach (var entry in facts.Entries.Where(item => !item.IsDirectory))
        {
            if (entry.LastWriteUtc is null || entry.LogicalBytes is null || entry.AllocatedBytes is null ||
                entry.LogicalBytes < 0 || entry.AllocatedBytes < 0)
                return CleanupDecision.Unknown(facts, "size_or_write_unknown");
            if (entry.LastWriteUtc > newest) newest = entry.LastWriteUtc.Value;
            try
            {
                logical = checked(logical + entry.LogicalBytes.Value);
                allocated = checked(allocated + entry.AllocatedBytes.Value);
            }
            catch (OverflowException)
            {
                return CleanupDecision.Unknown(facts, "size_overflow");
            }
        }
        if (facts.Entries.All(item => item.IsDirectory) && !facts.HasValidMarker)
            return CleanupDecision.Keep(facts, "empty_unmarked");
        if (newest >= now - TimeSpan.FromHours(24))
            return CleanupDecision.Keep(facts, "recent_write");
        return new CleanupDecision(facts.Path, CleanupDisposition.Eligible, "eligible",
            facts.Identity, newest, Math.Max(logical, allocated));
    }

    public CleanupPlan Plan(Guid runId, string storageId, IEnumerable<CleanupCandidateFacts> candidates,
        string? cursor = null)
    {
        var ordered = candidates.OrderBy(candidate => candidate.Path, StringComparer.Ordinal).ToArray();
        if (cursor is not null)
        {
            var after = ordered.Where(candidate => StringComparer.Ordinal.Compare(candidate.Path, cursor) > 0);
            var before = ordered.Where(candidate => StringComparer.Ordinal.Compare(candidate.Path, cursor) <= 0);
            ordered = after.Concat(before).ToArray();
        }
        var selected = ordered.Take(limits.MaxCandidates).ToArray();
        var decisions = selected.Select(Decide).ToList();
        foreach (var candidate in ordered.Skip(limits.MaxCandidates))
            decisions.Add(CleanupDecision.Unknown(candidate, "candidate_cap"));
        return new CleanupPlan(runId, storageId, clock.GetUtcNow(), decisions,
            selected.LastOrDefault()?.Path);
    }
}
