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
/// The delivery owner supplies the native binding and file-order floor captured before the
/// current generation's task prompt. These are expected evidence, never a caller-supplied age.
/// An unknown floor/binding cannot qualify. Server ownership and delivery checks remain required.
/// </summary>
public sealed record TerminalSeatObservationRequest(
    Guid ExpectedRunnerStoreId,
    DateTime ExpectedAcceptedStartedAt,
    string PromptBindingIdentity,
    long PromptFloorRevision);

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
