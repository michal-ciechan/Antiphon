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
    /// <summary>CARD-1677 S1 hardening: a path or name holds a control character (or U+2028/U+2029); never a candidate.</summary>
    public const string NameControlCharacter = "name_control_character";
}

/// <summary>
/// CARD-1677 S1 hardening (review d0b57b9c A-3). A retention name or path holding a control character
/// (<see cref="char.IsControl(char)"/>, which covers LF, CR, TAB, NUL, DEL and U+0085) or U+2028/U+2029 is
/// never a candidate: the runner and the server refuse it before any name pattern sees it, and every name
/// pattern ends in <c>\z</c> (in .NET <c>$</c> also matches just before a final newline).
/// </summary>
public static class WorkspaceRetentionNames
{
    public static bool HasControlCharacter(string? name) => (name ?? "").Any(IsControl);

    /// <summary>The name for a report line, each refused character spelled as <c>\uXXXX</c>.</summary>
    public static string Escape(string name) => string.Concat(name.Select(c => IsControl(c)
        ? "\\u" + ((int)c).ToString("x4", System.Globalization.CultureInfo.InvariantCulture)
        : c.ToString()));

    private static bool IsControl(char c) => char.IsControl(c) || c is '\u2028' or '\u2029';
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
