namespace Antiphon.SessionRunner.Contracts;

public sealed record HerdrPaneDisposalPreviewRequest(
    string PaneId, Guid? ExpectedSessionId = null, Guid? ExpectedNativeSessionId = null);

public sealed record HerdrPaneDisposalRequest(Guid OperationId, Guid PreviewId, string Reason);

/// <summary>Locator evidence only. Neither a token nor a historical claim authorizes termination.</summary>
public sealed record HerdrPaneDisposalClaim(Guid SessionId, string Source, string? Origin, bool Live);

public sealed record HerdrPaneDisposalProcess(int Pid, string? ExecutableName);

/// <summary>
/// Protocol-20 inspection increment. Eligible and GuardAvailable remain false until an audited
/// backend guard and the full identity/coordination implementation have been delivered.
/// Foreground observations are explicitly not a complete closure-affected process inventory.
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
    bool ProcessInventoryComplete, IReadOnlyList<string> Blockers);

/// <summary>A durable refusal, never evidence that a pane or process was terminated.</summary>
public sealed record HerdrPaneDisposalReceipt(
    Guid OperationId, Guid PreviewId, string PaneId, string Outcome, string Code,
    DateTimeOffset RecordedAtUtc, bool? PaneLeftOpen, bool CleanupPending);

public static class HerdrPaneDisposalCodes
{
    public const string Capability = "herdr-pane-disposal";
    public const string GuardUnavailable = "herdr_disposal_guard_unavailable";
    public const string IdentityUnproven = "herdr_pane_identity_unproven";
    public const string PreviewInvalid = "herdr_disposal_preview_invalid";
    public const string PreviewExpired = "herdr_disposal_preview_expired";
    public const string OperationConflict = "herdr_disposal_operation_conflict";
}
