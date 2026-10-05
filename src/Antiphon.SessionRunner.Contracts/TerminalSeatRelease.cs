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
