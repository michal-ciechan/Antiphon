using Antiphon.Server.Application.Dtos;
using Antiphon.Server.Domain.Enums;
using Antiphon.SessionRunner.Contracts;

namespace Antiphon.Server.Application.Services;

/// <summary>
/// Kind-static lookup for <see cref="ProviderContract"/>. Pure, DI-free, one entry per
/// <see cref="AgentKind"/> — no silent default. Facts here are what a live session of this kind
/// can signal; they are not launch/config capabilities (those stay on
/// <see cref="AgentTuiRunnerCatalog"/>).
/// </summary>
public static class ProviderContractCatalog
{
    public static ProviderContract For(AgentKind kind) => kind switch
    {
        AgentKind.ClaudeCode => Claude,
        AgentKind.Grok => Grok,
        AgentKind.Codex => Codex,
        AgentKind.OpenCode => OpenCode,
        AgentKind.Raw => Raw,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown agent kind.")
    };

    private static readonly ProviderContract Claude = new(
        AgentKind.ClaudeCode,
        Transcript: new TranscriptContract(
            AgentTuiCapabilityState.Supported,
            "Claude writes per-cwd JSONL under ~/.claude/projects; the tailer binds only with C1–C4 claims (CARD-0006). Transport still sends a null format so a new server in front of an old runner keeps the pre-Grok default.",
            TranscriptFormats.Claude,
            TranscriptDiscovery.DiscoveryWithClaims),
        TurnCompletion: new TurnCompletionContract(
            AgentTuiCapabilityState.Supported,
            "Working/idle and the turn-end queue flush read structured transcript markers (TurnEnd, interrupt prompt, manual CompactBoundary). Adapter WaitForTurnCompleteAsync still uses the OSC idle-title and ' for Ns' screen regex as a live wait.",
            TurnCompletionSignal.StructuredTranscript,
            HasScreenFallback: true),
        DeliveryVerification: new DeliveryVerificationContract(
            AgentTuiCapabilityState.Supported,
            "Composer echo plus CARD-0055 transcript-confirmed delivery (VerifiedPromptSubmitter at the adapter; IsVerifiedDeliverySessionAsync at the queue)."),
        SessionResume: new SessionResumeContract(
            AgentTuiCapabilityState.Supported,
            "Sessions resume by conversation identity (--resume / --session-id); AgentSessionService allows resume."),
        ContextWindowUsage: new ContextWindowUsageContract(
            AgentTuiCapabilityState.Supported,
            "JSONL usage tokens are present on assistant/TurnEnd parts; the context-window ceiling is not in the JSONL and is configured (CARD-0082).",
            ContextWindowCeilingSource.Configured),
        UsageLimitSignal: new UsageLimitSignalContract(
            AgentTuiCapabilityState.Supported,
            "Claude JSONL API-error stubs carry structural error class/status (ApiErrorClassifier: rate_limit/429 is a Wall). Wall stub text states the reset time.",
            UsageLimitSignalForm.StructuralField,
            StatesResetTime: true),
        Compaction: new CompactionContract(
            AgentTuiCapabilityState.Supported,
            "CompactBoundary marks both manual (turn end) and auto (mid-turn) compaction. CARD-0041 hazards remain: the raw typed /compact user record and the unmarked continuation prompt are not themselves CompactBoundary.",
            CompactionMarking.UnmarkedAuto),
        BlockingStartupModal: new BlockingStartupModalContract(
            AgentTuiCapabilityState.Supported,
            "First launch into an unseen cwd parks on the trust dialog. ClaudeBlockingPromptDetector auto-answers it (CARD-0047); the decision is per-cwd in ~/.claude.json.",
            BlockingStartupModalKind.AutoAnswerable,
            BlockingStartupModalScope.Cwd));

    private static readonly ProviderContract Grok = new(
        AgentKind.Grok,
        Transcript: new TranscriptContract(
            AgentTuiCapabilityState.Supported,
            "Grok persists the ACP update stream to GROK_HOME/sessions/<url-enc-cwd>/<session-id>/updates.jsonl. The path is deterministic because --session-id is honoured (measured 1.0.5, CARD-0080); none of the Claude discovery/claim machinery applies.",
            TranscriptFormats.Grok,
            TranscriptDiscovery.DeterministicPath),
        TurnCompletion: new TurnCompletionContract(
            AgentTuiCapabilityState.Supported,
            "Primary signal is the tailed TurnEnd row from ACP turn_completed (CARD-0080 S2). Screen done-line, idle title, and quiet-time remain the fallback when no transcript rows exist.",
            TurnCompletionSignal.StructuredTranscript,
            HasScreenFallback: true),
        DeliveryVerification: new DeliveryVerificationContract(
            AgentTuiCapabilityState.Supported,
            "Composer echo measured on grok 1.0.5 (CARD-0080 S1: typed and pasted bodies render) plus transcript-confirmed delivery against ACP UserPrompt rows."),
        SessionResume: new SessionResumeContract(
            AgentTuiCapabilityState.Supported,
            "Sessions resume by conversation identity (--session-id); AgentSessionService allows resume."),
        ContextWindowUsage: new ContextWindowUsageContract(
            AgentTuiCapabilityState.Degraded,
            "ACP modelState self-reports context sizes (500k) but that initialize payload is not consumed; turn_completed usage is already stored. Weaker guarantee: fullness cannot be computed until the ceiling is wired (CARD-0083 S5 opened the eligibility gate; wiring the ceiling/modelState is separate, unscoped follow-up work).",
            ContextWindowCeilingSource.SelfReported),
        UsageLimitSignal: new UsageLimitSignalContract(
            AgentTuiCapabilityState.Unknown,
            "pending CARD-0083 S1 survey",
            UsageLimitSignalForm.Unknown,
            StatesResetTime: null),
        Compaction: new CompactionContract(
            AgentTuiCapabilityState.Supported,
            "Grok emits explicit compaction_checkpoint and auto_compact_completed rows (measured 1.0.5). session_recap is a recap/summary, not compaction (CARD-0080 S1). The tailer currently skips these rows because a Grok compaction writes no user chunk and no turn_completed — nothing to strand.",
            CompactionMarking.Marked),
        BlockingStartupModal: new BlockingStartupModalContract(
            AgentTuiCapabilityState.Supported,
            "An unauthenticated GROK_HOME parks on a device-code login that swallows input (measured 1.0.5). Fail-fast — do not auto-answer. Scope is global (per GROK_HOME), not per-cwd. A fresh cwd with existing auth does not block.",
            BlockingStartupModalKind.FailFast,
            BlockingStartupModalScope.Global));

    private static readonly ProviderContract Codex = new(
        AgentKind.Codex,
        Transcript: new TranscriptContract(
            AgentTuiCapabilityState.Unsupported,
            "No structured transcript is tailed; Codex sessions stay screen-only.",
            Format: null,
            TranscriptDiscovery.None),
        TurnCompletion: new TurnCompletionContract(
            AgentTuiCapabilityState.Degraded,
            "PTY quiet-time detection is not a structured turn end — a weaker guarantee than a TurnEnd row or screen done-marker.",
            TurnCompletionSignal.QuietTimeOnly,
            HasScreenFallback: false),
        DeliveryVerification: new DeliveryVerificationContract(
            AgentTuiCapabilityState.Unsupported,
            "Blind SendLineAsync; no composer evidence and no transcript to confirm against."),
        SessionResume: new SessionResumeContract(
            AgentTuiCapabilityState.Unknown,
            "Installed-client resume support has not been probed."),
        ContextWindowUsage: new ContextWindowUsageContract(
            AgentTuiCapabilityState.Unknown,
            "Context-window usage has not been probed.",
            ContextWindowCeilingSource.None),
        UsageLimitSignal: new UsageLimitSignalContract(
            AgentTuiCapabilityState.Unknown,
            "pending CARD-0083 S1 survey",
            UsageLimitSignalForm.Unknown,
            StatesResetTime: null),
        Compaction: new CompactionContract(
            AgentTuiCapabilityState.Unknown,
            "Compaction signalling has not been probed.",
            CompactionMarking.None),
        BlockingStartupModal: new BlockingStartupModalContract(
            AgentTuiCapabilityState.Supported,
            "Codex shows a per-directory trust prompt ('Do you trust the contents of this directory?'); AcceptTrustPromptIfVisibleAsync auto-accepts it.",
            BlockingStartupModalKind.AutoAnswerable,
            BlockingStartupModalScope.Cwd));

    private static readonly ProviderContract OpenCode = new(
        AgentKind.OpenCode,
        Transcript: new TranscriptContract(
            AgentTuiCapabilityState.Unsupported,
            "No structured transcript is tailed; OpenCode sessions stay screen-only (ACP/event integration is not active).",
            Format: null,
            TranscriptDiscovery.None),
        TurnCompletion: new TurnCompletionContract(
            AgentTuiCapabilityState.Degraded,
            "PTY quiet-time fallback; ACP/event integration not active — a weaker guarantee than a structured turn end.",
            TurnCompletionSignal.QuietTimeOnly,
            HasScreenFallback: false),
        DeliveryVerification: new DeliveryVerificationContract(
            AgentTuiCapabilityState.Unsupported,
            "Blind SendLineAsync; no composer evidence and no transcript to confirm against."),
        SessionResume: new SessionResumeContract(
            AgentTuiCapabilityState.Unknown,
            "Installed OpenCode session-resume support has not been established."),
        ContextWindowUsage: new ContextWindowUsageContract(
            AgentTuiCapabilityState.Unknown,
            "Context-window usage has not been probed.",
            ContextWindowCeilingSource.None),
        UsageLimitSignal: new UsageLimitSignalContract(
            AgentTuiCapabilityState.Unknown,
            "pending CARD-0083 S1 survey",
            UsageLimitSignalForm.Unknown,
            StatesResetTime: null),
        Compaction: new CompactionContract(
            AgentTuiCapabilityState.Unknown,
            "Compaction signalling has not been probed.",
            CompactionMarking.None),
        BlockingStartupModal: new BlockingStartupModalContract(
            AgentTuiCapabilityState.Unknown,
            "A blocking first-launch modal has not been probed.",
            BlockingStartupModalKind.Unknown,
            BlockingStartupModalScope.Unknown));

    private static readonly ProviderContract Raw = new(
        AgentKind.Raw,
        Transcript: new TranscriptContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no structured transcript; sessions stay screen-only.",
            Format: null,
            TranscriptDiscovery.None),
        TurnCompletion: new TurnCompletionContract(
            AgentTuiCapabilityState.Degraded,
            "PTY quiet-time detection is not a structured turn end — a weaker guarantee than a TurnEnd row or screen done-marker.",
            TurnCompletionSignal.QuietTimeOnly,
            HasScreenFallback: false),
        DeliveryVerification: new DeliveryVerificationContract(
            AgentTuiCapabilityState.Unsupported,
            "Blind SendLineAsync; raw commands have no composer-evidence or transcript-confirm contract."),
        SessionResume: new SessionResumeContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no session-identity args or conversation file to resume."),
        ContextWindowUsage: new ContextWindowUsageContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no usage or context-window contract.",
            ContextWindowCeilingSource.None),
        UsageLimitSignal: new UsageLimitSignalContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no provider usage-limit signal.",
            UsageLimitSignalForm.None,
            StatesResetTime: null),
        Compaction: new CompactionContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no compaction contract.",
            CompactionMarking.None),
        BlockingStartupModal: new BlockingStartupModalContract(
            AgentTuiCapabilityState.Unknown,
            "A blocking first-launch modal has not been probed.",
            BlockingStartupModalKind.Unknown,
            BlockingStartupModalScope.Unknown));
}
