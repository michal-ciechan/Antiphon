using Antiphon.Server.Domain.Enums;

namespace Antiphon.Server.Application.Dtos;

/// <summary>
/// Kind-static operational capability declaration for one <see cref="AgentKind"/>. Resolved by
/// <c>ProviderContractCatalog.For</c>. This is not an extension of
/// <c>IAgentProtocolAdapter</c> — the consumers that need it hold a DB row's kind, in paths
/// where no adapter instance exists.
/// </summary>
/// <remarks>
/// Degradation is the contract's first-class outcome:
/// <list type="number">
/// <item>
/// <description>
/// Every axis is always declared. <see cref="AgentTuiCapabilityState.Unsupported"/> and
/// <see cref="AgentTuiCapabilityState.Unknown"/> plus a reason are valid, complete answers.
/// No adapter fakes support; nothing defaults to <see cref="AgentTuiCapabilityState.Supported"/>.
/// </description>
/// </item>
/// <item>
/// <description>
/// Consumers branch on the declared state and own a defined fallback per state. The fallback is
/// the feature degrading (blind delivery, quiet-time turn ends, no usage-limit detection), never
/// the session failing. <see cref="AgentTuiCapabilityState.Unknown"/> behaves as
/// <see cref="AgentTuiCapabilityState.Unsupported"/> for enabling machinery — but is distinct
/// on the surface, because Unknown is survey debt and Unsupported is a settled fact.
/// </description>
/// </item>
/// <item>
/// <description>
/// <see cref="AgentTuiCapabilityState.Degraded"/> means "works with a weaker guarantee" and the
/// reason must name the weakness, so a reader of any surface sees what they're trusting.
/// </description>
/// </item>
/// </list>
/// The contract is an upper bound: it states what a provider can do, never that this
/// session/deployment is doing it. Runtime evidence (bind failure, backend downgrade, zero
/// transcript rows) stays with its existing owners.
/// </remarks>
public sealed record ProviderContract(
    AgentKind Kind,
    TranscriptContract Transcript,
    TurnCompletionContract TurnCompletion,
    DeliveryVerificationContract DeliveryVerification,
    SessionResumeContract SessionResume,
    ContextWindowUsageContract ContextWindowUsage,
    UsageLimitSignalContract UsageLimitSignal,
    CompactionContract Compaction,
    BlockingStartupModalContract BlockingStartupModal,
    SubscriptionUsagePollContract SubscriptionUsagePoll,
    TerminalOverlayContract TerminalOverlay,
    LocalCommandContract LocalCommands);

/// <summary>
/// Structured-transcript axis. <see cref="Format"/> is a <c>TranscriptFormats</c> value when
/// <see cref="State"/> is <see cref="AgentTuiCapabilityState.Supported"/>; null otherwise.
/// </summary>
public sealed record TranscriptContract(
    AgentTuiCapabilityState State,
    string Reason,
    string? Format,
    TranscriptDiscovery Discovery);

public sealed record TurnCompletionContract(
    AgentTuiCapabilityState State,
    string Reason,
    TurnCompletionSignal Signal,
    bool HasScreenFallback);

public sealed record DeliveryVerificationContract(
    AgentTuiCapabilityState State,
    string Reason);

public sealed record SessionResumeContract(
    AgentTuiCapabilityState State,
    string Reason);

public sealed record ContextWindowUsageContract(
    AgentTuiCapabilityState State,
    string Reason,
    ContextWindowCeilingSource CeilingSource);

public sealed record UsageLimitSignalContract(
    AgentTuiCapabilityState State,
    string Reason,
    UsageLimitSignalForm Form,
    bool? StatesResetTime);

public sealed record CompactionContract(
    AgentTuiCapabilityState State,
    string Reason,
    CompactionMarking Marking);

public sealed record BlockingStartupModalContract(
    AgentTuiCapabilityState State,
    string Reason,
    BlockingStartupModalKind Kind,
    BlockingStartupModalScope PerScope);

/// <summary>
/// Kind-static poll of a TUI subscription-usage panel (CARD-0143).
/// <see cref="Command"/> is the ONLY body this feature may type for this kind, and is null
/// unless <see cref="State"/> is Supported or Degraded. Bodies that must never be typed live
/// on <see cref="LocalCommandContract.Forbidden"/> so every typing path reads one list.
/// </summary>
public sealed record SubscriptionUsagePollContract(
    AgentTuiCapabilityState State,
    string Reason,
    /// <summary>The ONLY body this code may type for this kind. Null unless State is Supported/Degraded.</summary>
    string? Command,
    /// <summary>Keys to press after the command to reach the quota view, in order. Empty = renders directly.</summary>
    IReadOnlyList<string> Navigation,
    /// <summary>Whether the command opens a focus-stealing overlay (CARD-0137) that must be Esc'd closed.</summary>
    bool OpensOverlay);

/// <summary>
/// Mid-life overlay handling (CARD-0137). NOT the launch-time modal contract
/// (<see cref="BlockingStartupModalContract"/>) — this is about a modal a live, idle session is sitting behind.
/// </summary>
public sealed record TerminalOverlayContract(
    AgentTuiCapabilityState State,
    string Reason,
    /// <summary>Key sequence measured to dismiss an overlay AND to be a no-op on an idle empty
    /// composer. Null unless State is Supported. Sent at most ONCE per delivery.</summary>
    string? DismissKey,
    /// <summary>Screen fragments that positively identify a MEASURED overlay for this kind,
    /// matched by ComposerDeliveryEvidence.FragmentIsVisible. Empty = no proactive detector.</summary>
    IReadOnlyList<string> DetectFragments);

/// <summary>
/// Exact TUI-local command bodies for this kind, and what each is measured to do. The key is the
/// first whitespace-delimited token of the body, lowercased. Absence is not a claim of absence —
/// an undeclared /-prefixed body keeps the ordinary prompt-delivery path unchanged.
/// </summary>
public sealed record LocalCommandContract(
    AgentTuiCapabilityState State,
    string Reason,
    IReadOnlyDictionary<string, LocalCommandFact> Commands,
    /// <summary>Bodies nothing may ever type for this kind, with the reason. Moved here from
    /// SubscriptionUsagePollContract so there is ONE list and it governs every typing path.
    /// Enforced by test AND at runtime.</summary>
    IReadOnlyDictionary<string, string> Forbidden);

/// <param name="WritesUserPrompt">Does submitting it produce a UserPrompt transcript row carrying
/// the typed text? This is the ONLY thing that decides whether CARD-0055's confirm can be used.</param>
public sealed record LocalCommandFact(bool OpensOverlay, bool WritesUserPrompt, string Evidence);
