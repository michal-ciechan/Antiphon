# CARD-0083: a provider capability contract — ACP-inspired, Antiphon-shaped

**Date:** 2026-08-18
**Status:** planned (task 9a5b93a3)
**Scope:** architecture plan. Decides the contract's shape, home, axes, and degradation rules, and
slices the work. Implements nothing.

## Verdict

Build a **separate, pure, kind-static capability declaration** (`ProviderContract`, resolved by
`ProviderContractCatalog.For(AgentKind)`) composed **alongside** `IAgentProtocolAdapter` — not an
extension of the interface, because the consumers that need it are Application services holding only
a DB row's `AgentKind`, in code paths where no adapter instance exists or ever will. Reuse
`AgentTuiCapabilityState` (Supported/Unsupported/Degraded/Unknown + reason) as the shared support
vocabulary, but make the axes **typed record members**, not the TUI catalog's string-named display
list — consumers branch on these at compile time and several axes carry structured payload (a
transcript format, a discovery mode, a usage-limit shape). Migrate today's scattered
kind-conditionals to read it mechanically (behavior-identical except two measured Grok stalenesses,
fixed deliberately). Run the **usage-limit signal survey first**, as its own slice — it is cheap,
independently valuable to CARD-0022's remaining slices, and it determines what the `UsageLimitSignal`
axis must carry rather than guessing. Five slices, ~4–5 days spread; S1 and S2 can run in parallel.

## Survey (verified against the code, 2026-08-18)

### 1. `IAgentProtocolAdapter` — what already varies per provider

The seam (`server/Application/Interfaces/IAgentProtocolAdapter.cs`) has five runner-backed
implementations (`RunnerClaudeAdapter`, `RunnerGrokAdapter`, `RunnerCodexAdapter`,
`RunnerOpenCodeAdapter`, `RunnerRawAdapter`) plus the legacy in-proc pty trio. What varies today:

| Concern | Claude | Grok | Codex | OpenCode/Raw |
|---|---|---|---|---|
| Ready detection | quiet + **trust-dialog auto-answer** (`ClaudeBlockingPromptDetector`, CARD-0047) | quiet only | quiet + **its own trust-prompt auto-accept** (`AcceptTrustPromptIfVisibleAsync`) | quiet only |
| Turn completion | idle-title OSC `✳` + `" for \d+s"` regex | **transcript `TurnEnd` row primary** (CARD-0080 S2), measured decimal-seconds regex + idle title as fallback | pure quiet time | pure quiet time |
| Prompt delivery | `VerifiedPromptSubmitter` (evidence + Enter) | `VerifiedPromptSubmitter` + transcript baseline | blind `SendLineAsync` | blind `SendLineAsync` |
| Reply extraction | `ClaudeResponseAnalyzer` screen-scrape | `AssistantText` rows, screen fallback | `CodexResponseAnalyzer` | `CodexResponseAnalyzer` |

The interface itself stays provider-neutral; every difference lives inside the implementations plus
a constellation of **kind-conditionals in Application services** (below). The doc comment's promised
extension point (`AgentTurnResult` "record so future fields… extend without breaking callers") has
never been exercised.

### 2. `AgentTuiProfile.capabilities` — the direct precedent, and its measured drift

`AgentTuiRunnerCatalog` declares seven string-named capabilities per kind
(modelArgument/modelDiscovery/structuredActivity/sessionResume/remoteControl/systemPromptAppend/
permissionBypass), each an `AgentTuiCapabilityDto(Name, AgentTuiCapabilityState, Reason)`. Two
properties matter for this design: it is **launch/config-time** behavior (one entry,
permissionBypass, even depends on the profile's arguments, not the kind), and it is
**display-shaped** — the strings feed the TUI-profile UI, nothing branches on them in code.

It has already drifted: `GrokCapabilities` still declares `structuredActivity: Degraded — "Grok ACP
session updates are not tailed"` (`AgentTuiRunnerCatalog.cs:150`), and
`AgentTuiLaunchResolver.ActivityModeFor` still maps Grok → `QuietTime` (`:403`) — both stale since
CARD-0080 S2 landed the Grok tailer (`efb2790`). Capability facts stated as prose/kind-lists in N
places drift independently; that is the disease this card treats.

### 3. `TranscriptEnabled` — the card's characterization, confirmed and slightly worse

Confirmed: one boolean, decided in one place (`SessionRunnerHttpClient.TranscriptEnabledFor:
kind is ClaudeCode or Grok`, with `TranscriptFormatFor` beside it), carried through
`RunnerLaunchRequest` → `SessionRunnerRuntime` → `PtyHostMessages`/manifest. But the *same
underlying fact* — "this provider has a structured transcript" — is **independently re-derived as a
kind-list at least five more times**:

- `SessionMessageQueueService.IsVerifiedDeliverySessionAsync` (`:1092`) — Claude|Grok inline query
  gating CARD-0055 transcript-confirmed delivery.
- `AgentSessionService:695` — resume allowed only for Claude|Grok.
- `AgentSessionService.UsesSessionIdentityArgs` (`:922`) — Claude|Grok get `--session-id` args.
- `AgentTuiLaunchResolver.ActivityModeFor` (`:400`) — Claude=Structured, everything else QuietTime
  (stale for Grok).
- `SessionHealthService:295` — always-on health scan scoped `AgentKind == ClaudeCode` only.

Plus adjacent kind-lists encoding *other* capability facts: `AgentTaskService.DelegatableKinds`
(`:749`), the orchestrator-must-be-Claude rule (`:783`), per-kind env defaults
(`AgentRegistry:127-146`, `AgentTuiLaunchResolver:371-398`). Each is one `or NewKind` away from
being forgotten when a provider gains a capability — exactly how the two Grok stalenesses happened.

### 4. CARD-0022's line of work — what usage-limit machinery exists

Landed (resilience spec 2026-08-17, S1–S3): the structural fields
(`IsApiError`/`ApiErrorClass`/`ApiErrorStatus`) carried end-to-end,
`TranscriptKinds.IsApiErrorStub`, **`ApiErrorClassifier`** (pure: Wall/Transient/NeedsHuman/Unknown,
keyed on Claude Code's own `error` class then HTTP status), the channel-reply withhold
(`ChannelReplyDispatcher:626-713`), the settlement guard (`AgentTaskReplyService:1040-1067`),
`AgentIncidentKind.ApiErrorTurnDied = 22`. **Not yet landed**: S4–S6 (`UsageLimitResetParser`,
`UsageLimitState` + fleet pause, the resume/retry machinery, `AttentionKind.UsageLimitExhausted`).
Everything is Claude-JSONL-specific by construction: detection reads fields only Claude's
synthetic-stub record carries, and the classifier's inputs are Claude Code's error plumbing.
Grok/Codex/OpenCode usage-limit shapes are **unsurveyed** — the card's characterization holds.

### 5. CARD-0080's Grok findings — what a second data point proves

Grok is ACP-native: explicit `turn_completed` carrying `stop_reason` **and full usage/cost** per
turn; `session_recap` as an explicitly **marked** compaction analog (no Claude-style unmarked-auto
hazard); deterministic transcript path (no discovery/claims — the CARD-0006 hazard class absent);
ACP `modelState` self-reports **context sizes (500k)** — a ceiling Claude's JSONL never states
(CARD-0082 fact 3 forced Claude's ceiling into config); blocking first-launch is a **global**
device-code login (per-`GROK_HOME`), not per-cwd; usage-limit shape unknown. The lesson: providers
differ not just in *whether* they expose a signal but in *shape and richness* — sometimes richer
than Claude. A contract that models axes as booleans would already be wrong for provider #2.

## Decisions

### D1. A separate declaration type, composed alongside — not an `IAgentProtocolAdapter` extension

Recommended: **separate**. Three reasons, in order of weight:

1. **The consumers can't reach an adapter.** The gates in §3 fire at enqueue, settle, sweep, resume
   and health-scan time — Application services holding a `Guid`/`AgentKind` off a DB row. Adapter
   instances exist only inside the launch path (`AgentProtocolAdapterFactory` per session, runner
   side). An interface member would be unreachable at exactly the call sites that need it; a static
   catalog keyed on `AgentKind` is reachable from all of them, the pty-less runner paths, and tests.
2. **The facts are kind-static, adapters are per-session.** Nothing in the axes below varies per
   session instance. (What *does* vary at runtime — bind failure, backend downgrade — is
   deliberately out of the contract; D5.)
3. **Precedent.** `AgentTuiRunnerCatalog` is exactly this shape (pure static, kind-keyed, DI-free)
   and has worked; `ApiErrorClassifier`/`CheckSchedule` establish pure-static as the house pattern.

`IAgentProtocolAdapter` itself changes **zero**. If an adapter ever needs its own declaration in
scope, `ProviderContractCatalog.For(kind)` is one call away — no default interface member needed.

**Home**: `server/Application/Services/ProviderContractCatalog.cs` + the record types in
`server/Application/Dtos` or beside the catalog (follow `AgentTuiRunnerCatalog`'s layout). Not the
Contracts assembly for now — every consumer is server-side; the server remains the sole decider and
*tells* the runner what to do per launch (the `TranscriptEnabled`/`TranscriptFormat` request fields
stay the transport). Move later if the runner ever needs to decide alone.

### D2. Relationship to `AgentTuiProfile.capabilities`: same vocabulary, different axis family, one derivation

The TUI catalog answers "what can I configure/launch?" (partly argument-dependent, display-shaped).
This contract answers "what operational signals does a live session of this kind give me?"
(kind-static, branched on in code). They are different levels and stay separate — merging would
force the TUI's string-list shape onto typed consumers or vice versa.

Shared: the **state enum**. `AgentTuiCapabilityState` already lives in `Domain/Enums` and its four
states + reason-string idiom are exactly right; reuse it as-is (no rename — churn with no payoff).

One derivation, to kill the measured drift: `AgentTuiRunnerCatalog`'s `structuredActivity` entry
becomes **derived from** the new contract's `TurnCompletion`/`Transcript` axes (state mapped, reason
passed through), so the UI row and the machinery can never disagree again. The other six TUI entries
stay hand-declared — they have no counterpart in this contract, deliberately.

### D3. The axes — typed members, minimum set, with shapes not booleans

`ProviderContract` (record, one instance per kind) with one typed member per axis; every member
embeds `State` (`AgentTuiCapabilityState`) + `Reason` (string, the TUI idiom — every declaration
says *why*, which is what made the TUI catalog auditable):

| Axis | Type payload beyond State+Reason | Claude | Grok | Codex | OpenCode | Raw |
|---|---|---|---|---|---|---|
| `Transcript` | `Format` (existing `TranscriptFormats`), `Discovery` (`DeterministicPath`/`DiscoveryWithClaims`/`None`) | Supported, claims discovery (CARD-0006 C1–C4) | Supported, deterministic path | Unsupported | Unsupported | Unsupported |
| `TurnCompletion` | `Signal` (`StructuredTranscript`/`ScreenMarkers`/`QuietTimeOnly`), `HasScreenFallback` | Supported, structured (marker-predicate family) | Supported, structured (`turn_completed`) + fallback | Degraded, quiet-time | Degraded, quiet-time | Degraded, quiet-time |
| `DeliveryVerification` | — (composer echo + transcript confirm both required) | Supported | Supported (measured CARD-0080 S1) | Unsupported (blind) | Unsupported | Unsupported |
| `SessionResume` | — | Supported | Supported | Unknown | Unknown | Unsupported |
| `ContextWindowUsage` | `CeilingSource` (`Configured`/`SelfReported`/`None`) | Supported, configured ceiling (CARD-0082) | Supported, self-reported (ACP `modelState`; wiring unbuilt → Degraded until S5) | Unknown | Unknown | Unsupported |
| `UsageLimitSignal` | `Form` (`StructuralField`/`TextOnly`/`Unknown`), `StatesResetTime` (bool?) | Supported, structural + text reset (`ApiErrorClassifier`) | Unknown → S1 survey | Unknown → S1 | Unknown → S1 | Unsupported |
| `Compaction` | `Marking` (`None`/`Marked`/`UnmarkedAuto`), | Supported, manual-marked / auto-marked-mid-turn (CARD-0041 hazards) | Supported, marked (`session_recap`) | Unknown | Unknown | Unsupported |
| `BlockingStartupModal` | `Kind` (`None`/`AutoAnswerable`/`FailFast`/`Unknown`), `PerScope` (`Cwd`/`Global`) | Present, auto-answerable, per-cwd (trust dialog) | Present, fail-fast, global (device-code login) | Present, auto-answerable (Codex trust accept) | Unknown | Unknown |

Deliberately **not** axes: modelArgument/modelDiscovery/systemPromptAppend/permissionBypass/
remoteControl (the TUI catalog's, stays there); pty delivery ceilings and paste behavior (a property
of **our** pty backend and deployment, owned by `PtyDeliveryProfile`'s two-fact negotiation — the
provider didn't choose our conhost); delegation eligibility (`DelegatableKinds` is policy, not
capability — it *reads* capabilities but adds judgment, e.g. orchestrator-must-be-Claude).

Cell values above are the plan's best current knowledge, stated so S2 has a starting point; S2
verifies each against the code/measurements before pinning, and S1's survey fills the Unknowns in
`UsageLimitSignal` honestly (a survey that finds nothing declares Unknown with a dated reason, which
is itself progress — it marks survey debt instead of silence).

### D4. Declaring "I don't have this" — degradation is the contract's first-class outcome

The working pattern (`TranscriptEnabled` gating, screen-heuristic fallbacks) is codified as three
rules, stated in the contract type's doc comment and enforced by the migration slice:

1. **Every axis is always declared** — `Unsupported`/`Unknown` + reason are valid, complete answers.
   No adapter fakes support; nothing defaults to Supported.
2. **Consumers branch on the declared state and own a defined fallback per state.** The fallback is
   the *feature* degrading (blind delivery, quiet-time turn ends, no usage-limit detection), never
   the *session* failing. `Unknown` behaves as `Unsupported` for enabling machinery — but is
   distinct on the surface, because Unknown is survey debt and Unsupported is a settled fact.
3. **`Degraded` means "works with a weaker guarantee"** and the reason must name the weakness (the
   quiet-time entries above), so a reader of any surface sees what they're trusting.

### D5. Static declaration vs runtime evidence — the contract is an upper bound

The contract states what a provider **can** do; it never promises this session/deployment is doing
it. Runtime failures keep their existing owners: transcript bind failure (`TranscriptBindFailed`
incidents, running with no transcript), the runner's pty-backend downgrade (`PtyDeliveryProfile`
demanding two agreeing facts), delivery-verification degrade at zero baseline rows (CARD-0055).
Consumers therefore compose `contract-says-supported AND runtime-evidence-present` exactly as
`IsVerifiedDeliverySessionAsync` + zero-rows-degrade already do; the contract replaces only the
first conjunct. This split is what keeps the catalog pure and the CARD-0056 lesson intact: absence
of runtime evidence degrades, it never kills.

## Slices

| # | Slice | Contents | Tests | Size | Depends on |
|---|---|---|---|---|---|
| **S1** | **Usage-limit signal survey** (Grok/Codex/OpenCode) | Per provider: docs + error-path source reading + locally reproducible probes (auth-expired, malformed model id; a real quota hit is neither reproducible nor affordable — the resilience spec's testing stance). Deliverable: `docs/investigations/2026-08-XX-provider-usage-limit-shapes.md` recording measured shape or honest Unknown per provider — what record/update/screen text appears, structural vs text-only, reset time stated?, mapping onto Wall/Transient/NeedsHuman | The doc is the deliverable; any measured shapes become fixtures for S4 | S, ~1 day | — |
| **S2** | **The contract type, populated + pinned** | `ProviderContract` records + `ProviderContractCatalog.For(AgentKind)`; declarations per D3 corrected to what S2's own verification finds; XML-docs carrying D4's rules | `ProviderContractCatalogTests`: every kind declares every axis; **lockstep pins** asserting the declaration matches today's gates (`TranscriptEnabledFor`, verified-delivery kinds, resume kinds, identity-args kinds) so contract-vs-code drift fails a test from day one | S, ~1 day | — (parallel with S1) |
| **S3** | **Migrate the gate sites** | The §3 list reads the catalog: `TranscriptEnabledFor`/`TranscriptFormatFor`, `IsVerifiedDeliverySessionAsync`, `AgentSessionService:695`/`UsesSessionIdentityArgs`, `ActivityModeFor`, `SessionHealthService:295`; `AgentTuiRunnerCatalog.structuredActivity` becomes derived (D2). Behavior-identical **except two deliberate fixes**: `ActivityModeFor(Grok)` → Structured and the stale Grok catalog reason — each with its own test naming CARD-0080 S2 | Existing suites stay green (the pins from S2 now hold by construction); new tests for the two Grok deltas | M, ~1 day | S2 |
| **S4** | **Usage-limit detection keyed on the contract** | The detection seam stops being Claude-shaped: whatever S1 measured for a provider is normalized by *its* tailer onto the existing `IsApiError`/`ApiErrorClass`/`ApiErrorStatus` fields (the CARD-0072 S1 columns are already provider-neutral — only the stamping is Claude-only); `ApiErrorClassifier` grows per-provider class mappings only if S1 found shapes that don't fit the current inputs; consumers (`IsApiErrorStub` guards, incident 22) work unchanged because they read the neutral fields. Gated per provider on `UsageLimitSignal.State` | Normalizer tests per measured shape (from S1 fixtures); classifier tests for any new mapping | M, ~1–2 days, **scope shrinks to near-zero if S1 finds only Unknowns** | S1, S2; rebase-check against resilience S4–S6 |
| **S5** | **Context-window axis consumption** (optional, small) | CARD-0082's eligibility (`AgentKind == ClaudeCode`) reads `ContextWindowUsage.State` instead; Grok flips to eligible only when its usage rows actually populate the stored columns (0082 fact: the computation is already column-driven and provider-agnostic) | Eligibility unit tests | S, ~0.5 day | S2, CARD-0082 S1–S3 landed |

Order: S1 ∥ S2 → S3 → S4; S5 whenever CARD-0082's slices are in. **This is multiple cards' worth**:
recommend CARD-0083 hosts S1–S3 (the contract itself), and S4/S5 land as follow-up cards or as
amendments to CARD-0022/0082's own plans once S1's facts exist — those slices modify machinery that
other specs own, and the survey may change their shape.

## Collision map

- **Resilience spec S4–S6 (unbuilt: `UsageLimitResetParser`, `UsageLimitState`, retry/resume,
  attention row)** — owned by CARD-0022/0072. S4 here must not build them; it only makes the
  *detection* seam provider-neutral. Rebase-check before S4; if those slices land first, S4 threads
  the contract through their gates instead.
- **CARD-0082 S1–S3 (in planning)** — touches `TranscriptNormalizer`, `SessionMessageQueueService`,
  `AgentSupervisorHostedService`. S5 here explicitly waits for it. S3's migration does not touch any
  0082 file.
- **`SessionMessageQueueService.cs`** — perennially contended (CARD-0035 s4–6, 0082 S3).
  S3's change there is one query predicate swapped for a catalog call — keep it that narrow.
- **`AgentTuiRunnerCatalog.cs` / TUI DTOs** — the derivation in S3 changes one entry's provenance;
  the TUI UI contract (names/states/reasons as strings) must stay byte-compatible.
- **CARD-0084 (Grok delegate kind, landed S1)** — `AgentTaskService.DelegatableKinds` and the
  orchestrator-must-be-Claude arm are *policy* reading capabilities (D3); S3 deliberately leaves
  them as-is. A later cleanup may re-express them over the contract, on its own card.
- **`SessionRunnerContracts.cs`** — untouched by S2/S3 (the contract lives server-side). If S4 adds
  fields for a measured Grok shape, the additive-only rule (optional params with defaults) applies.

## What I could not determine

1. **Grok/Codex/OpenCode usage-limit shapes** — S1 exists to answer this; nothing here guesses.
2. **Whether Codex/OpenCode have tailable structured streams** (Codex app-server, OpenCode ACP —
   the grok-first-class plan's S4 direction). If one lands later, its integration is now: measure,
   fill the axes, flip the declarations — which is the card's success criterion working as intended.
3. **Whether OpenCode has a blocking first-launch modal** — declared Unknown; a fresh-cwd canary in
   the S1 survey pass is cheap if a probe is already being run for usage limits.
4. **Grok's `session_recap` interaction with working/idle** — CARD-0080's plan says one rule on
   `stop_reason` suffices; the `Compaction` axis records Marked either way. If the recap turns out
   to strand anything, that is a Grok-tailer bug, not a contract question.
