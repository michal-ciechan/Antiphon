namespace Antiphon.SessionRunner.Contracts;

/// <summary>CARD-1677 S1. Request modes for <see cref="PhoneHomeOperation.WorkspaceRetain"/>.</summary>
public static class WorkspaceRetainModes
{
    /// <summary>Every check runs; nothing is moved or removed. The default the server sends.</summary>
    public const string DryRun = "DryRun";
    public const string Execute = "Execute";
}

/// <summary>CARD-1677 S1. Verdicts and reason codes of one retention request. A refusal is always a kept tree.</summary>
public static class WorkspaceRetentionCodes
{
    public const string Kept = "kept";
    public const string WouldRemove = "would_remove";
    public const string Removed = "removed";
    public const string AlreadyAbsent = "already_absent";
    public const string RetiringRefused = "retiring_refused";

    public const string OutsideScope = "outside_scope";
    public const string SlotManaged = "slot_managed";
    public const string WorkspaceInUse = "workspace_in_use";
    public const string RecentlyMirrored = "recently_mirrored";
    public const string WorkspaceRetiringOrUnknown = "workspace_retiring_or_unknown";
    public const string Locked = "locked";
    public const string LockStateUnknown = "lock_state_unknown";
    public const string NotRunnerRepository = "not_runner_repository";
    public const string BranchMismatch = "branch_mismatch";
    public const string StatusUnavailable = "status_unavailable";
    public const string Dirty = "dirty";
    public const string Unpushed = "unpushed";
    public const string UnpushedUnknown = "unpushed_unknown";
    public const string BranchAbsentOnOrigin = "branch_absent_on_origin";
    public const string OriginUnavailable = "origin_unavailable";
    public const string OriginTipUnknown = "origin_tip_unknown";
    public const string OriginDoesNotContainHead = "origin_does_not_contain_head";
    public const string LiveProcess = "live_process";
    public const string HolderScanUnknown = "holder_scan_unknown";
    public const string RetiringRootCrossDevice = "retiring_root_cross_device";
    public const string RetiringRootInvalid = "retiring_root_invalid";
    public const string RetiringMoveFailed = "retiring_move_failed";
    public const string InspectionFailed = "inspection_failed";
}

public sealed record WorkspaceRetainHolder(int Pid, string Argv0, string Kind);

/// <summary>
/// CARD-1677 S1. One candidate mirror. <see cref="Path"/> must be a direct child <c>task-&lt;8 hex&gt;</c>
/// of the runner's own worktree root whose id equals <see cref="TaskId"/>; the runner never trusts it beyond that.
/// </summary>
public sealed record PhoneHomeWorkspaceRetainRequest(
    Guid TaskId, string Path, string Branch, string Mode, Guid RequestId);

/// <summary>CARD-1677 S1. The outcome of one request plus the facts the runner checked.</summary>
public sealed record PhoneHomeWorkspaceRetainResponse(
    string Verdict,
    string? Reason = null,
    string? HeadSha = null,
    string? OriginTip = null,
    IReadOnlyList<WorkspaceRetainHolder>? Holders = null,
    int? NestedIgnoredRepos = null,
    long? PhaseAMs = null,
    long? PhaseBMs = null,
    bool RetiringResidue = false,
    string? MovedPath = null,
    string? Detail = null);

/// <summary>CARD-1677 S1. One top-level entry of the worktree root (<c>Root=worktrees</c>) or the retiring root (<c>retiring</c>).</summary>
public sealed record WorkspaceRetentionEntry(
    string Root, string Name, bool IsDirectory, bool IsSymlink, bool Registered, bool SlotLeaf, DateTime? MtimeUtc);

public sealed record PhoneHomeWorkspaceRetentionInventoryResponse(
    IReadOnlyList<WorkspaceRetentionEntry> Entries, bool Complete, string? Detail = null);

/// <summary>CARD-1677 S2. Request modes for <see cref="PhoneHomeOperation.TempSweep"/>.</summary>
public static class TempSweepModes
{
    public const string DryRun = "DryRun";
    public const string Execute = "Execute";
}

/// <summary>CARD-1677 S2. Verdicts and reason codes of one <see cref="PhoneHomeOperation.TempSweep"/> candidate. A refusal is always a kept entry.</summary>
public static class TempSweepCodes
{
    public const string Kept = "kept";
    public const string WouldRemove = "would_remove";
    public const string Removed = "removed";

    public const string Denied = "denied";
    public const string FamilyDisabled = "family_disabled";
    public const string MirrorPresent = "mirror_present";
    public const string Symlink = "symlink";
    public const string ForeignOwner = "foreign_owner";
    public const string OwnerUnknown = "owner_unknown";
    public const string InventoryLimit = "inventory_limit";
    public const string TooYoung = "too_young";
    public const string AgeUnknown = "age_unknown";
    public const string LiveProcess = "live_process";
    public const string HolderScanUnknown = "holder_scan_unknown";
    public const string TickBound = "tick_bound";
    public const string TickBudget = "tick_budget";
    public const string UnprovenContent = "unproven_content";
    public const string ChangedSinceInventory = "changed_since_inventory";
    public const string DeleteFailed = "delete_failed";
    public const string RequestInvalid = "request_invalid";
}

/// <summary>
/// CARD-1677 S2. One sweep of the runner's own temp root. Only the runner's registered families are
/// candidates; <see cref="Families"/> can switch a family off (or <c>claude-scratch</c> on), never add one.
/// </summary>
public sealed record PhoneHomeTempSweepRequest(
    string Mode, Guid RequestId, int MinAgeHours, int MaxEntries, long MaxBytes,
    IReadOnlyDictionary<string, bool>? Families = null);

/// <summary>CARD-1677 S2. One registered-family entry and what the sweep did with it.</summary>
public sealed record TempSweepCandidate(
    string Name, string Family, string Verdict, string? Reason = null, long Bytes = 0, DateTime? NewestWriteUtc = null,
    IReadOnlyList<WorkspaceRetainHolder>? Holders = null, string? Detail = null);

/// <summary>CARD-1677 S2. Names that match no registered family, by normalized prefix: reported, never touched.</summary>
public sealed record TempSweepUnregistered(string Prefix, int Count, long Bytes, bool BytesComplete);

public sealed record PhoneHomeTempSweepResponse(
    string Mode,
    IReadOnlyList<TempSweepCandidate> Candidates,
    IReadOnlyList<TempSweepUnregistered> Unregistered,
    bool Complete = true,
    bool Truncated = false,
    int Removed = 0,
    long RemovedBytes = 0,
    string? Reason = null,
    string? Detail = null,
    string? Root = null);
