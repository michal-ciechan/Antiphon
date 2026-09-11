namespace Antiphon.SessionRunner.Contracts;

public sealed record HerdrPaneDisposalPreviewRequest(
    string PaneId, Guid? ExpectedSessionId = null, Guid? ExpectedNativeSessionId = null);

public sealed record HerdrPaneDisposalRequest(Guid OperationId, Guid PreviewId, string Reason, string? GuardMode = null);

/// <summary>Locator evidence only. Neither a token nor a historical claim authorizes termination.</summary>
public sealed record HerdrPaneDisposalClaim(Guid SessionId, string Source, string? Origin, bool Live,
    string? AgentKind = null, int? ChildPid = null, DateTime? ChildStartedAtUtc = null);

public sealed record HerdrPaneDisposalProcess(int Pid, string? ExecutableName,
    DateTime? StartedAtUtc = null, int? ParentPid = null, IReadOnlyList<Guid>? NativeSessionIds = null);

/// <summary>
/// A reviewed observation, not an atomic authorization at the Herdr backend. Antiphon checks
/// again immediately before pane.close; external activity after that check can still race close.
/// </summary>
public sealed record HerdrPaneDisposalPreview(
    Guid PreviewId, DateTimeOffset ExpiresAtUtc, string PaneId,
    Guid? ExpectedSessionId, Guid? ExpectedNativeSessionId,
    string WorkspaceId, string TabId, string? TerminalId,
    string? WorkspaceLabel, string? TabLabel, string? PaneLabel,
    string BackendVersion, int BackendProtocol,
    int? ShellPid, IReadOnlyList<HerdrPaneDisposalProcess>? Foreground,
    IReadOnlyList<HerdrPaneDisposalClaim> Claims,
    bool? WouldLeaveTabEmpty, bool Eligible, bool GuardAvailable,
    bool ProcessInventoryComplete, IReadOnlyList<string> Blockers,
    string? BackendInstanceId = null, HerdrPaneDisposalProcess? Shell = null,
    IReadOnlyList<HerdrPaneDisposalProcess>? AffectedProcesses = null,
    string GuardMode = "antiphon-best-effort",
    bool AtomicClose = false,
    IReadOnlyList<int>? PlannedTerminationPids = null);

/// <summary>Durable result; Unknown never authorizes a destructive retry.</summary>
public sealed record HerdrPaneDisposalReceipt(
    Guid OperationId, Guid PreviewId, string PaneId, string Outcome, string Code,
    DateTimeOffset RecordedAtUtc, bool? PaneLeftOpen, bool CleanupPending,
    bool? ReplacementPresent = null, string? TerminalId = null,
    Guid? ExpectedSessionId = null, Guid? ExpectedNativeSessionId = null);

public static class HerdrPaneDisposalCodes
{
    public const string Capability = "herdr-pane-disposal";
    public const string BestEffortCapability = "herdr-pane-disposal-best-effort-v1";
    public const string GuardUnavailable = "herdr_disposal_guard_unavailable";
    public const string IdentityUnproven = "herdr_pane_identity_unproven";
    public const string PreviewInvalid = "herdr_disposal_preview_invalid";
    public const string PreviewExpired = "herdr_disposal_preview_expired";
    public const string OperationConflict = "herdr_disposal_operation_conflict";
    public const string Unknown = "herdr_disposal_outcome_unknown";
    public const string PreviewConsumed = "herdr_disposal_preview_consumed";
}
