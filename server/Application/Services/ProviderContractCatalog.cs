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
    private static readonly IReadOnlyDictionary<string, string> EmptyForbidden =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, LocalCommandFact> EmptyCommands =
        new Dictionary<string, LocalCommandFact>(StringComparer.OrdinalIgnoreCase);

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
            "Claude JSONL API-error stubs carry structural error class/status (ApiErrorClassifier: rate_limit/429 is a Wall). Some stubs state a reset time (session-limit); the per-model cap does not (CARD-0022).",
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
            BlockingStartupModalScope.Cwd),
        SubscriptionUsagePoll: new SubscriptionUsagePollContract(
            AgentTuiCapabilityState.Unknown,
            "No established TUI command that renders Claude's subscription-usage panel. Skip; do not guess.",
            Command: null,
            Navigation: [],
            OpensOverlay: false),
        TerminalOverlay: new TerminalOverlayContract(
            AgentTuiCapabilityState.Supported,
            "Esc is a no-op on an idle empty composer, and one Esc restores the composer after /model (CARD-0137 S1 ClaudeOverlayCanaryTests). DetectFragments stay empty until the /model chrome is captured — S6 off, S5 on.",
            DismissKey: "\u001b",
            DetectFragments: []),
        LocalCommands: new LocalCommandContract(
            AgentTuiCapabilityState.Supported,
            "Declared commands are those measured to write (or not write) a UserPrompt row. /compact writes one (CARD-0041); absence of a declaration is not a claim of absence.",
            Commands: new Dictionary<string, LocalCommandFact>(StringComparer.OrdinalIgnoreCase)
            {
                ["/compact"] = new LocalCommandFact(
                    OpensOverlay: false,
                    WritesUserPrompt: true,
                    Evidence: "CARD-0041: Claude writes the raw typed /compact text as a plain UserPrompt record in addition to the <command-name> wrapper; CARD-0082 auto-compact depends on that row."),
            },
            Forbidden: EmptyForbidden),
        RefocusCompact: new RefocusCompactContract(
            AgentTuiCapabilityState.Supported,
            "Claude records /compact as <command-name>/<local-command-stdout> wrappers plus a raw echo, all already housekeeping to IsHousekeepingPrompt; a manual CompactBoundary is a turn END that flushes the queue (CARD-0041). Measured 106 s on the CARD-0077 miss.",
            Command: "/compact"));

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
            "Composer echo measured on grok 1.0.5 (CARD-0080 S1: typed and pasted bodies render) plus transcript-confirmed delivery against ACP UserPrompt rows. A final Sent requires a matching UserPrompt or a sustained composer departure (head gone for PostEvidenceSettleMs of consecutive snapshots, still gone at the unobservable deadline). Sequence advance, startup redraw, MCP (0/2), quiet, and a raw OSC title are not submit evidence (CARD-0342). A body still visible at that deadline is NoSubmitOutput and stays retryable."),
        SessionResume: new SessionResumeContract(
            AgentTuiCapabilityState.Supported,
            "Sessions resume by conversation identity (--session-id); AgentSessionService allows resume."),
        ContextWindowUsage: new ContextWindowUsageContract(
            AgentTuiCapabilityState.Supported,
            "Occupancy is Grok's own numbers (CARD-0157): auto_compact_completed.tokens_after as a usage-bearing (auto) CompactBoundary, plus single-call turn_completed.usage.inputTokens between compactions. Multi-call loop-sums do not update the badge. Ceiling is the measured 500 000 self-reported window (stdio initialize modelState.totalContextTokens and ~/.grok/models_cache.json context_window, both 500 000, measured 2026-08-23).",
            ContextWindowCeilingSource.SelfReported,
            UsageAccounting: ProviderUsageAccounting.TurnSumInclusiveCache,
            SelfReportedCeilingTokens: 500_000),
        UsageLimitSignal: new UsageLimitSignalContract(
            AgentTuiCapabilityState.Supported,
            "Grok writes API errors on turn_completed.agent_result (measured 402 Payment Required — 'Grok Build usage balance exhausted', CARD-0281). Structural field; the 402 text states no reset time.",
            UsageLimitSignalForm.StructuralField,
            StatesResetTime: false),
        Compaction: new CompactionContract(
            AgentTuiCapabilityState.Supported,
            "Grok emits explicit compaction_checkpoint and auto_compact_completed rows (measured 1.0.5). session_recap is a recap/summary, not compaction (CARD-0080 S1). auto_compact_completed is ingested as a usage-bearing (auto) CompactBoundary (CARD-0157); compaction_checkpoint stays skipped (no token payload).",
            CompactionMarking.Marked),
        BlockingStartupModal: new BlockingStartupModalContract(
            AgentTuiCapabilityState.Supported,
            "First launch into an unseen cwd parks on 'Do you trust the contents of this directory?' (y/n). GrokTrustPromptDetector auto-answers y (CARD-0315); the decision is per-cwd in ~/.grok/trusted_folders.toml. Nested git worktrees are separate workspaces, so every fresh -Worktree path hits this. Sign-in gates trust: an unauthenticated GROK_HOME parks on Grok 1.0.13's OAuth device-approval screen ('Approve in your browser to finish signing in' / 'Waiting for approval...') or the welcome token input ('Paste your token here') — fail-fast, never auto-answered, global per GROK_HOME. GrokSignInPromptDetector (CARD-0324) types nothing and names `grok login`. A missing auth.json is the signature of Grok clearing credentials after a permanent refresh failure, not a lock-file race.",
            BlockingStartupModalKind.AutoAnswerable,
            BlockingStartupModalScope.Cwd),
        SubscriptionUsagePoll: new SubscriptionUsagePollContract(
            AgentTuiCapabilityState.Degraded,
            "Command `/usage` is measured (CARD-0136) but tab navigation to the `Usage limit` tab and the progress-bar percentage polarity are unmeasured. Weaker guarantee: the sweep holds Grok behind IncludeDegradedProviders until S5 settles both. Overlay-opening (CARD-0137).",
            Command: "/usage",
            Navigation: [],
            OpensOverlay: true),
        TerminalOverlay: new TerminalOverlayContract(
            AgentTuiCapabilityState.Supported,
            "Esc dismisses the /usage overlay and is a no-op on an idle empty composer (CARD-0137 investigation §3.1, measured twice; S1 canary GrokUsageOverlayCanaryTests).",
            DismissKey: "\u001b",
            DetectFragments: ["c copy session ID"]),
        LocalCommands: new LocalCommandContract(
            AgentTuiCapabilityState.Supported,
            "/usage is measured to open an overlay and write no UserPrompt row (CARD-0136, CARD-0137). Absence of a declaration is not a claim of absence.",
            Commands: new Dictionary<string, LocalCommandFact>(StringComparer.OrdinalIgnoreCase)
            {
                ["/usage"] = new LocalCommandFact(
                    OpensOverlay: true,
                    WritesUserPrompt: false,
                    Evidence: "CARD-0136 + CARD-0137 §3.2: /usage opens a focus-stealing overlay and writes no UserPrompt row."),
            },
            Forbidden: EmptyForbidden),
        RefocusCompact: new RefocusCompactContract(
            AgentTuiCapabilityState.Unknown,
            "Never probed. Grok auto-compacts (compaction_checkpoint / auto_compact_completed); a manual command has not been measured. Unknown behaves as Unsupported for enabling machinery.",
            Command: null));

    private static readonly ProviderContract Codex = new(
        AgentKind.Codex,
        Transcript: new TranscriptContract(
            AgentTuiCapabilityState.Supported,
            "Codex writes a rollout JSONL per session at CODEX_HOME/sessions/YYYY/MM/DD/rollout-<ts>-<uuid>.jsonl, tailed by CodexTranscriptTailer (CARD-0099 S1). There is no --session-id flag and the interactive TUI never prints its id on screen (codex exec does), so the path is DISCOVERED under the full CARD-0006 rules, not computed. The file is created lazily at the first submit (measured 0.147.0: 30s of an idle rendered composer with zero bytes written produced no file) and is held open by Codex, so every read shares write+delete.",
            TranscriptFormats.Codex,
            TranscriptDiscovery.DiscoveryWithClaims),
        TurnCompletion: new TurnCompletionContract(
            AgentTuiCapabilityState.Supported,
            "Primary signal is the tailed TurnEnd row from event_msg/task_complete, an explicit structured boundary Codex writes per turn (CARD-0099 S1), and since CARD-0108 S2 RunnerCodexAdapter.WaitForTurnCompleteAsync consumes it directly: it polls for a TurnEnd past the prompt's baseline and takes ResponseText from that window's AssistantText rows rather than scraping the screen. Codex carries no stop_reason field, so the normalizer synthesizes end_turn, which is what AgentSessionRuntime.IsTurnBoundary keys on. The screen fallback (transcript-less sessions only) is NOT quiet-time: quiet counts only after the measured Working-indicator lifecycle - a 'Working (Ns - esc to interrupt)' line that appeared and then left the screen. Bare quiet was the CARD-0108 defect: over a prompt stranded in a silent composer it certified a non-turn as complete in ~3.2s and returned the status bar as the answer. Codex renders no 'Worked for Ns' done-line (measured 0.147.0), so the indicator's disappearance is the whole screen signal, and a session where it never appears honestly reports TurnCompleted=false at max wait.",
            TurnCompletionSignal.StructuredTranscript,
            HasScreenFallback: true),
        DeliveryVerification: new DeliveryVerificationContract(
            AgentTuiCapabilityState.Supported,
            "Composer echo measured on codex-cli 0.147.0 TUI (a typed body renders in the composer row; a typed \\n is a literal newline and does not submit) plus CARD-0055 transcript-confirmed delivery against the rollout's UserMessage rows. The re-press contract holds: Enter on an empty composer was measured submitting nothing five times over. A submit is proven by the Working indicator (immediate) or an emptied composer sustained across consecutive snapshots for PostEvidenceSettleMs — a single empty/ghost poll is not evidence (CARD-0299). Sequence advance alone is the body's own render and is not evidence. A body still visible at the unobservable deadline is NoSubmitOutput. The named/card-launch boot prompt (CodexSubmitConfirmation) applies the same Working / body-still-visible look when no transcript ever binds (CARD-0133 S1b-A): Working is degraded success; body still visible on both post-Enter looks throws ComposerMayHoldBody. Bracketed-paste and large-body behaviour are still unmeasured (CARD-0099 S2), so the conservative spill policy applies."),
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
            AgentTuiCapabilityState.Supported,
            "Marked in the rollout by a top-level 'compacted' record plus event_msg/context_compacted, and measured MID-TURN on a real session (rollout 01a01193-07eb: the pair landed 66 minutes inside a turn whose own task_complete arrived hours later). Codex compaction is housekeeping that strands nothing, so unlike Claude's manual /compact (CARD-0041) it needs no turn-end treatment and the normalizer skips both records.",
            CompactionMarking.Marked),
        BlockingStartupModal: new BlockingStartupModalContract(
            AgentTuiCapabilityState.Supported,
            "Codex shows a per-directory trust prompt ('Do you trust the contents of this directory?') on the FIRST launch into any unseen cwd, even under --dangerously-bypass-approvals-and-sandbox (measured 0.147.0), plus a startup update-available modal that swallows keystrokes the same way; AcceptTrustPromptIfVisibleAsync auto-accepts the trust prompt.",
            BlockingStartupModalKind.AutoAnswerable,
            BlockingStartupModalScope.Cwd),
        SubscriptionUsagePoll: new SubscriptionUsagePollContract(
            AgentTuiCapabilityState.Supported,
            "Codex `/status` renders the weekly-limit panel directly into scrollback with no overlay (CARD-0141). `/usage` is forbidden: it opens a picker whose highlighted option redeems the account's one usage-limit reset.",
            Command: "/status",
            Navigation: [],
            OpensOverlay: false),
        TerminalOverlay: new TerminalOverlayContract(
            AgentTuiCapabilityState.Unknown,
            "Esc-on-idle and overlay-dismiss have not been measured (CARD-0137 S1).",
            DismissKey: null,
            DetectFragments: []),
        LocalCommands: new LocalCommandContract(
            AgentTuiCapabilityState.Supported,
            "/status is the usage-poll command (CARD-0141); /usage is forbidden because it opens a picker that can redeem the account's one usage-limit reset.",
            Commands: new Dictionary<string, LocalCommandFact>(StringComparer.OrdinalIgnoreCase)
            {
                ["/status"] = new LocalCommandFact(
                    OpensOverlay: false,
                    WritesUserPrompt: false,
                    Evidence: "CARD-0141: /status renders the weekly-limit panel into scrollback with no overlay and no UserPrompt row."),
            },
            Forbidden: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["/usage"] =
                    "opens a `1. Show usage` / `2. Redeem usage limit reset` picker; a `Mode:\"Now\"`-style send auto-confirms the highlighted option and can redeem the account's one usage-limit reset (CARD-0141)",
            }),
        RefocusCompact: new RefocusCompactContract(
            AgentTuiCapabilityState.Unsupported,
            "Measured 2026-08-21, session 51ee57fc seq 19 and seq 30: recorded as a plain UserMessage and answered as a work turn. Codex compaction is automatic and separately marked (compacted + event_msg/context_compacted), so nothing is lost by not asking.",
            Command: null));

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
            BlockingStartupModalScope.Unknown),
        SubscriptionUsagePoll: new SubscriptionUsagePollContract(
            AgentTuiCapabilityState.Unknown,
            "No established TUI command that renders OpenCode's subscription-usage panel. Skip; do not guess.",
            Command: null,
            Navigation: [],
            OpensOverlay: false),
        TerminalOverlay: new TerminalOverlayContract(
            AgentTuiCapabilityState.Unsupported,
            "No overlay-handling contract; OpenCode sessions are screen-only.",
            DismissKey: null,
            DetectFragments: []),
        LocalCommands: new LocalCommandContract(
            AgentTuiCapabilityState.Unknown,
            "Local TUI commands have not been probed. Absence of a declaration is not a claim of absence.",
            Commands: EmptyCommands,
            Forbidden: EmptyForbidden),
        RefocusCompact: new RefocusCompactContract(
            AgentTuiCapabilityState.Unsupported,
            "No structured transcript at all — an extra typed prompt could not be told from the brief afterwards.",
            Command: null));

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
            BlockingStartupModalScope.Unknown),
        SubscriptionUsagePoll: new SubscriptionUsagePollContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no provider subscription-usage panel to poll.",
            Command: null,
            Navigation: [],
            OpensOverlay: false),
        TerminalOverlay: new TerminalOverlayContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no overlay-handling contract.",
            DismissKey: null,
            DetectFragments: []),
        LocalCommands: new LocalCommandContract(
            AgentTuiCapabilityState.Unsupported,
            "Raw commands have no TUI-local command contract.",
            Commands: EmptyCommands,
            Forbidden: EmptyForbidden),
        RefocusCompact: new RefocusCompactContract(
            AgentTuiCapabilityState.Unsupported,
            "Not a TUI with commands.",
            Command: null));
}
