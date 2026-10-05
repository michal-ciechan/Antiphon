namespace Antiphon.SessionRunner.Contracts;

public enum TerminalTranscriptReadStatus
{
    Success, Unbound, Unavailable, Partial, Malformed, BudgetExceeded, StaleObservation
}

public enum TerminalTranscriptVerdict { Unknown, Working, Idle }

/// <summary>
/// Read-only native transcript evidence. Success is not release authority: the runtime must
/// still qualify the current generation, delivered prompt, custody and stability interval.
/// Identities are opaque digests; no transcript text or native path crosses this boundary.
/// </summary>
public sealed record TerminalTranscriptObservation(
    TerminalTranscriptReadStatus Status,
    TerminalTranscriptVerdict Verdict,
    string? BindingIdentity = null,
    string? FileRevision = null,
    long ConsumedBytes = 0,
    long TranscriptRevision = 0,
    long? LastEndRevision = null,
    long? LastPromptRevision = null);

/// <summary>
/// Legacy callers supply the native binding and file-order floor captured before the prompt.
/// Captured mode ignores those fields and resolves the runner's current submitted capture;
/// discovery uses empty binding/-1 placeholders. Missing capture cannot qualify. Server
/// ownership and delivery checks remain required in either mode.
/// </summary>
public sealed record TerminalSeatObservationRequest(
    Guid ExpectedRunnerStoreId,
    DateTime ExpectedAcceptedStartedAt,
    string PromptBindingIdentity,
    long PromptFloorRevision,
    bool UseCapturedDeliveryEvidence = false);

public enum TerminalSeatQualificationStatus
{
    Unknown, Missing, GenerationMismatch, OldPrompt, Working, Waiting, Qualified, StaleObservation
}

/// <summary>A token permits conditional reinspection only; it never permits a force release.</summary>
public sealed record TerminalSeatObservation(
    TerminalSeatQualificationStatus Status,
    TerminalTranscriptObservation Transcript,
    string? Token = null,
    TimeSpan StableFor = default,
    DateTimeOffset? FirstObservedAt = null);

public sealed record TerminalSeatReleaseRequest(
    Guid ActionId, TerminalSeatObservationRequest Observation, string Token);

public enum TerminalSeatReleaseOutcome
{
    Released, AlreadyExited, AlreadyAbsent, Working, Unknown, PendingDelivery,
    StaleObservation, GenerationMismatch, Owned, Unsupported, Unresolved
}

public sealed record TerminalSeatReleaseResult(
    Guid SessionId, Guid ActionId, TerminalSeatReleaseOutcome Outcome,
    DateTime? AcceptedStartedAt)
{
    public bool ConfirmsExit => Outcome is TerminalSeatReleaseOutcome.Released
        or TerminalSeatReleaseOutcome.AlreadyExited or TerminalSeatReleaseOutcome.AlreadyAbsent;
}
