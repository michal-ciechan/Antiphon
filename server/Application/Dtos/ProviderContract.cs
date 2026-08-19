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
    BlockingStartupModalContract BlockingStartupModal);

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
