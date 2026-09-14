namespace Antiphon.SessionRunner.Contracts;

/// <summary>
/// CARD-0514 D-8: generation-and-sequence-conditional maintenance write.
/// Distinct from unguarded <c>POST /sessions/{id}/input</c>. Older runners that lack
/// <see cref="RunnerCapabilityFeatures.ConditionalMaintenanceInputV1"/> must never receive
/// a fallback raw input from automatic RC.
/// </summary>
public static class ConditionalInputOutcomes
{
    public const string Written = "written";
    public const string StaleObservation = "stale-observation";
    public const string GenerationMismatch = "generation-mismatch";
    public const string Missing = "missing";
    public const string Exited = "exited";
    public const string Unsupported = "unsupported";
    public const string Unknown = "unknown";
}

public sealed record RunnerConditionalInputRequest(
    DateTime ExpectedAcceptedStartedAt,
    long ExpectedLastSequence,
    string Input);

public sealed record RunnerConditionalInputResult(
    Guid SessionId,
    string Outcome,
    DateTime? AcceptedStartedAt = null,
    long? LastSequence = null);
