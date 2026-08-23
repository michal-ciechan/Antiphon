namespace Antiphon.Server.Domain.Enums;

/// <summary>
/// How a provider's structured transcript is located. Payload on
/// <c>ProviderContract.Transcript</c>.
/// </summary>
public enum TranscriptDiscovery
{
    None = 0,
    DeterministicPath = 1,
    DiscoveryWithClaims = 2
}

/// <summary>
/// The live signal a session of this kind uses to decide a turn has ended. Payload on
/// <c>ProviderContract.TurnCompletion</c>.
/// </summary>
public enum TurnCompletionSignal
{
    QuietTimeOnly = 0,
    ScreenMarkers = 1,
    StructuredTranscript = 2
}

/// <summary>
/// Where a provider's context-window ceiling comes from. Payload on
/// <c>ProviderContract.ContextWindowUsage</c>. SelfReported means the ceiling is the
/// provider's own figure, recorded in the catalog with evidence (CARD-0157) — not "a
/// figure we saw on the wire but do not consume".
/// </summary>
public enum ContextWindowCeilingSource
{
    None = 0,
    Configured = 1,
    SelfReported = 2
}

/// <summary>
/// How a provider's stored usage fields relate to live window occupancy.
/// Payload on <c>ProviderContract.ContextWindowUsage</c>. One declared fact
/// drives both TokensOf arithmetic and occupancy-row eligibility (CARD-0157).
/// </summary>
public enum ProviderUsageAccounting
{
    /// <summary>
    /// Cache/output fields add on top of InputTokens (Claude; the legacy default).
    /// </summary>
    AdditiveCache = 0,

    /// <summary>
    /// InputTokens is a per-user-turn loop sum that already contains cache reads (Grok).
    /// Occupancy is InputTokens of a single-call turn or a usage-bearing CompactBoundary.
    /// </summary>
    TurnSumInclusiveCache = 1
}

/// <summary>
/// Shape of a provider's usage-limit / quota-wall signal. Payload on
/// <c>ProviderContract.UsageLimitSignal</c>.
/// </summary>
public enum UsageLimitSignalForm
{
    None = 0,
    Unknown = 1,
    StructuralField = 2,
    TextOnly = 3
}

/// <summary>
/// How a provider marks compaction in its transcript. Payload on
/// <c>ProviderContract.Compaction</c>.
/// </summary>
public enum CompactionMarking
{
    None = 0,
    Marked = 1,
    UnmarkedAuto = 2
}

/// <summary>
/// Kind of blocking first-launch modal a provider can present. Payload on
/// <c>ProviderContract.BlockingStartupModal</c>.
/// </summary>
public enum BlockingStartupModalKind
{
    None = 0,
    AutoAnswerable = 1,
    FailFast = 2,
    Unknown = 3
}

/// <summary>
/// Scope of a blocking first-launch modal. Payload on
/// <c>ProviderContract.BlockingStartupModal</c>.
/// </summary>
public enum BlockingStartupModalScope
{
    None = 0,
    Cwd = 1,
    Global = 2,
    Unknown = 3
}
