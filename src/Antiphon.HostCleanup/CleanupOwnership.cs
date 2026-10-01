namespace Antiphon.HostCleanup;

public static class CleanupOwnership
{
    public static string? Veto(CleanupCandidateFacts facts)
    {
        var owner = facts.Owner;
        if (owner is null || !facts.HasValidMarker || !facts.IsRegistered)
            return "owner_unknown";
        if (owner.SlotBound) return "slot_owned";
        if (!StringComparer.Ordinal.Equals(owner.HostBootId, facts.LocalBootId) ||
            !StringComparer.Ordinal.Equals(owner.PidNamespace, facts.LocalPidNamespace))
            return "owner_identity_foreign";
        if (owner.Pid <= 0 || owner.ProcessStart == default)
            return "owner_identity_unknown";
        if (!owner.NestedCustodyComplete) return "nested_custody_missing";
        if (owner.NestedExecutorLiveness == OwnerLiveness.Alive) return "executor_live";
        if (owner.NestedExecutorLiveness is OwnerLiveness.Unknown or OwnerLiveness.Reused)
            return "executor_liveness_unknown";
        return owner.Liveness switch
        {
            OwnerLiveness.Alive => "owner_live",
            OwnerLiveness.Reused => "owner_reused_pid",
            OwnerLiveness.Unknown => "owner_liveness_unknown",
            OwnerLiveness.Dead when !owner.Released => "release_required",
            OwnerLiveness.Dead when !owner.EvidencePreserved => "evidence_required",
            _ => null
        };
    }
}
