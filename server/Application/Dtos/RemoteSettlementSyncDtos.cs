namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// CARD-0657 D-1. What preparing a runner-bound Worktree task's canonical desktop checkout found.
/// Only <see cref="Synchronized"/> and <see cref="NoPushedProgress"/> carry a confirmed desktop
/// commit; every other state is either not this flow's business or an explicit refusal/uncertainty.
/// </summary>
public enum RemoteSettlementSyncState
{
    /// <summary>Not an ordinary runner-bound Worktree task: no Git was touched.</summary>
    NotApplicable = 0,

    /// <summary>The desktop checkout was confirmed at the task branch's pushed commit.</summary>
    Synchronized = 1,

    /// <summary>The exact branch is missing on origin, or still names the captured baseline.</summary>
    NoPushedProgress = 2,

    /// <summary>A safety rule refused the sync; the desktop checkout was left exactly as found.</summary>
    Refused = 3,

    /// <summary>The answer could not be established (I/O, timeout, lease, missing wiring).</summary>
    Unavailable = 4,
}

/// <summary>
/// CARD-0657 D-1. One typed preparation result. A success always carries the full observed and
/// confirmed commit; a null <see cref="DesktopAfterSha"/> is never a success.
/// </summary>
public sealed record RemoteSettlementSyncResult(
    RemoteSettlementSyncState State,
    string? Reason = null,
    string? FullRef = null,
    string? BaselineSha = null,
    string? RemoteSha = null,
    string? DesktopBeforeSha = null,
    string? DesktopAfterSha = null,
    string? ObservationRef = null,
    string? EndpointFingerprint = null)
{
    public bool Confirmed => State is RemoteSettlementSyncState.Synchronized or RemoteSettlementSyncState.NoPushedProgress
        && DesktopAfterSha is not null;

    public static RemoteSettlementSyncResult NotApplicable { get; } = new(RemoteSettlementSyncState.NotApplicable);
}

/// <summary>CARD-0657 D-5. Stable reason codes; none carries an endpoint, path or raw Git output.</summary>
public static class RemoteSettlementSyncReasons
{
    public const string NoPushedProgress = "runner_no_pushed_progress";
    public const string BranchNotPushed = "runner_branch_not_pushed";
    public const string ReportedCommitNotPushed = "runner_reported_commit_not_pushed";
    public const string Dirty = "runner_sync_dirty";
    public const string Sequencer = "runner_sync_sequencer_active";
    public const string IdentityMismatch = "runner_sync_identity_mismatch";
    public const string BranchMismatch = "runner_sync_branch_mismatch";
    public const string Diverged = "runner_sync_diverged";
    public const string Rewound = "runner_sync_rewound";
    public const string LocalAhead = "runner_sync_local_ahead";
    public const string BaselineUnavailable = "runner_sync_baseline_unavailable";
    public const string EndpointChanged = "runner_sync_endpoint_changed";
    public const string EndpointAmbiguous = "runner_sync_endpoint_ambiguous";
    public const string FetchUnavailable = "runner_sync_fetch_unavailable";
    public const string Timeout = "runner_sync_timeout";
    public const string LeaseBusy = "runner_sync_lease_busy";

    /// <summary>
    /// Not a verdict: the lease was still busy when this attempt's wait slice ran out, and the
    /// cumulative wait since the sync first found it busy is still inside the budget. Settlement
    /// leaves the task open for the next sweep's attempt; only <see cref="LeaseBusy"/> blocks.
    /// </summary>
    public const string LeaseWaiting = "runner_sync_lease_waiting";
    public const string RetirementReserved = "runner_sync_retirement_reserved";
    public const string InspectionUnavailable = "runner_sync_inspection_unavailable";
    public const string ChangedDuringValidation = "runner_sync_changed_during_validation";
    public const string MergeFailed = "runner_sync_merge_failed";
    public const string PostconditionUnavailable = "runner_sync_postcondition_unavailable";
    public const string DependencyUnavailable = "runner_sync_dependency_unavailable";

    /// <summary>Bind-refusal recovery: origin's tip is not a commit the recovered evidence names.</summary>
    public const string TipNotReported = "runner_sync_tip_not_reported";
}
