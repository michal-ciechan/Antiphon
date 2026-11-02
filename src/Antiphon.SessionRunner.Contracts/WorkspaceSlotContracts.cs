namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-0824 S1. Typed refusal details for workspace slot operations.</summary>
public static class WorkspaceSlotCodes
{
    /// <summary>The pool is at a bound; the detail names <see cref="OverflowCap"/> or <see cref="DiskBudget"/>.</summary>
    public const string Unavailable = "workspace_slot_unavailable";
    public const string OverflowCap = "overflow_cap";
    public const string DiskBudget = "disk_budget";

    /// <summary>More quarantined directories are open than the runner admits; dispatch holds.</summary>
    public const string PoolDegraded = "workspace_slot_pool_degraded";

    /// <summary>A legacy removal path named a slot directory: nothing to remove, the pool owns it.</summary>
    public const string SlotManaged = "slot_managed";
    public const string GenerationMismatch = "slot_generation_mismatch";
    public const string TaskMismatch = "slot_task_mismatch";
    public const string UnknownSlot = "slot_unknown";
    public const string BranchCheckedOut = "branch_checked_out";
    public const string Disabled = "workspace_slots_disabled";

    public const string Overflow = "workspace_slot_overflow";
    public const string Quarantined = "workspace_slot_quarantined";
    public const string Rehomed = "workspace_slot_rehomed";
    public const string RehomeFailed = "workspace_slot_rehome_failed";
    public const string Retired = "workspace_slot_retired";
    public const string EvidenceTruncated = "evidence_truncated";

    /// <summary>D-3: a drain re-arm holds a slot-bound task until its old runner confirms the slot released.</summary>
    public const string ReleasePending = "workspace_slot_release_pending";

    /// <summary>D-3: the task's runner refused a release as <see cref="UnknownSlot"/>; retried a bounded number of times.</summary>
    public const string ReleaseUnknown = "workspace_slot_release_unknown";

    /// <summary>D-3: the bounded <see cref="ReleaseUnknown"/> retries are spent; the reconciler stops and an operator decides.</summary>
    public const string ReleaseStuck = "workspace_slot_release_stuck";

    /// <summary>Q-1: a Claude <c>--continue</c> resumes the newest conversation in the cwd, another lessee's in a slot.</summary>
    public const string ContinueInSharedWorkspace = "continue_in_shared_workspace";

    /// <summary>True when the last path segment is a slot directory (<c>ws-NN</c> or <c>ws-NN-dK</c>).</summary>
    public static bool IsSlotPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;
        var leaf = path.Replace('\\', '/').TrimEnd('/');
        leaf = leaf[(leaf.LastIndexOf('/') + 1)..];
        return System.Text.RegularExpressions.Regex.IsMatch(leaf, "^ws-[0-9]{2,}(-d[0-9]+)?$");
    }

    /// <summary>Formats the refusal message the server parses: <c>workspace_slot_unavailable:&lt;bound&gt;</c>.</summary>
    public static string UnavailableDetail(string bound) => Unavailable + ":" + bound;

    /// <summary>True for a lease refusal the server must hold on without a fallback mirror.</summary>
    public static bool TryParseUnavailable(string? message, out string bound)
    {
        bound = "";
        if (string.IsNullOrEmpty(message))
            return false;
        var index = message.IndexOf(Unavailable + ":", StringComparison.Ordinal);
        if (index < 0)
            return message.Contains(PoolDegraded, StringComparison.Ordinal) && (bound = PoolDegraded).Length > 0;
        var rest = message[(index + Unavailable.Length + 1)..];
        bound = rest.StartsWith(OverflowCap, StringComparison.Ordinal) ? OverflowCap
            : rest.StartsWith(DiskBudget, StringComparison.Ordinal) ? DiskBudget : "";
        return bound.Length > 0;
    }
}

/// <summary>CARD-0824 S1 D-B. The server reconciler's release of one leased slot.</summary>
public sealed record PhoneHomeWorkspaceSlotReleaseRequest(string SlotId, int Generation, Guid TaskId);

/// <summary><c>Outcome</c> is released, already_free, deferred or quarantined.</summary>
public sealed record PhoneHomeWorkspaceSlotReleaseResponse(string Outcome, string SlotId, int Generation, string? Detail = null);

/// <summary>CARD-0824 S1 D-C. Slots is a list of slot ids, or null with <c>All</c> true.</summary>
public sealed record PhoneHomeWorkspaceSlotColdResetRequest(
    Guid RequestId, IReadOnlyList<string>? Slots, bool All, string Requester, string Reason);

public sealed record PhoneHomeWorkspaceSlotColdResetOutcome(string SlotId, string Outcome);

public sealed record PhoneHomeWorkspaceSlotColdResetResponse(
    Guid RequestId, IReadOnlyList<PhoneHomeWorkspaceSlotColdResetOutcome> Slots);

/// <summary>CARD-0824 S1 D-D. Disposition is Pushed, AcceptLoss or Removed.</summary>
public sealed record PhoneHomeWorkspaceSlotQuarantineReleaseRequest(string DirId, string Disposition, string Reason);

public sealed record PhoneHomeWorkspaceSlotQuarantineReleaseResponse(string DirId, string Outcome, string? Detail = null);

/// <summary>
/// CARD-0824 S1 §3.4. The pure reset decision table shared by every host. A warm reset keeps exactly
/// the native build output; a cold reset keeps nothing. Paths are slash-separated, relative to the slot.
/// </summary>
public static class WorkspaceSlotResetPolicy
{
    /// <summary>The <c>git clean -e</c> exclusions of a warm reset (H-3, D-14).</summary>
    public static readonly IReadOnlyList<string> KeepList = ["**/obj/", "**/bin/", "**/node_modules/", "/client/dist/"];

    /// <summary>Sequencer state a free slot must never carry, relative to the worktree git dir.</summary>
    public static readonly IReadOnlyList<string> SequencerMarkers =
        ["rebase-merge", "rebase-apply", "MERGE_HEAD", "CHERRY_PICK_HEAD", "REVERT_HEAD", "sequencer", "BISECT_LOG"];

    /// <summary>The clean argv after <c>git</c>: <c>-ffdx</c> always, the keep-list only when warm.</summary>
    public static IReadOnlyList<string> CleanArguments(bool cold)
    {
        var args = new List<string> { "clean", "-ffdx" };
        if (!cold)
        {
            foreach (var keep in KeepList)
            {
                args.Add("-e");
                args.Add(keep);
            }
        }

        return args;
    }

    /// <summary>True when a path (as git status prints it) lies inside the warm keep-list.</summary>
    public static bool IsKeepListPath(string relativePath)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        if (path.StartsWith("client/dist/", StringComparison.Ordinal) || path == "client/dist")
            return true;
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment is "obj" or "bin" or "node_modules")
                return true;
        }

        return false;
    }
}
