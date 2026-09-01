# Usage-limit and API-error resilience — when a turn is killed by something outside our control

- **Status**: Proposed (planning only — task `0320dca6`; nothing here is implemented)
- **Date**: 2026-08-17
- **Cards reconciled**: CARD-0022, CARD-0071, CARD-0072 (verdict table in §2 — none silently merged)
- **Evidence base** (measured, not re-derived here): 23 real API-error stubs across 14 sessions
  all-time on this machine — 18× 429 rate-limit (78%), 1× 529, 2× auth-expired, 1× connection-drop;
  9 benign `"No response requested."` synthetics and 1 test-fixture 404 excluded. Only 2 of the 23
  ever reached `TranscriptEntries`. $6.28 burned in one afternoon by two limit-killed delegates
  (`ee0a18a5` $4.489/nothing produced; `27e20988` $1.793/dirty shared checkout), both of which
  **reported as completed tasks**. Frequency sweep lives on CARD-0072 — do not redo it.

## 0. What exists today (verified against the code, 2026-08-17)

### The record and what the pipeline does with it

Claude Code writes a dead turn as ONE synthetic assistant JSONL line: `message.model ==
"<synthetic>"`, top-level `error` (class: `rate_limit` / `server_error` /
`authentication_failed` / `model_not_found`), `isApiErrorMessage: true`, `apiErrorStatus`
(numeric when present), `stop_reason: "stop_sequence"`, and the error string as an ordinary
`text` content block. `TranscriptNormalizer.FromAssistant`
(`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:87-123`) emits an ordinary `AssistantText`
part plus a `TurnEnd` part and **discards `error`, `isApiErrorMessage`, `apiErrorStatus`** —
no field exists for them on `TranscriptPart` (`TranscriptNormalizer.cs:13`) or
`RunnerTranscriptEvent` (`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs:77`).
By the time we see the record at all, Claude Code's own retry is exhausted (measured 209s on the
529); Antiphon-side is the only place left to act.

### How the dead turn propagates — three triggers, none discriminating (verified)

- **Working/idle reads idle, correctly.** `SessionMessageQueueService.IsWorkingAsync`
  (`server/Application/Services/SessionMessageQueueService.cs:1224`) counts **any** `TurnEnd` as
  a turn end regardless of stop reason; the stub's own `TurnEnd` outranks its `AssistantText` by
  sequence. All three lockstep implementations agree. This is why a human typing `Continue`
  works immediately — and why nothing needs to change in the working rule (§D2).
- **The queue-flush boundary does NOT fire.** `AgentSessionRuntime.IsTurnBoundary`
  (`server/Application/Services/AgentSessionRuntime.cs:261`) requires `StopReason ==
  "end_turn"`; the stub is `stop_sequence`. So no "Agent finished" toast, no boundary flush.
  Pending `WhenIdle` messages already queued before the death sit until the *next* enqueue's
  deliver-if-idle check — a soft strand, previously undocumented, resolved for free by §D4's
  resume prompt (its own enqueue flushes the queue in FIFO order behind it).
- **Channel dispatch and task settlement DO fire, undiscriminating.** The error string is
  `AssistantText`, and `AgentSessionRuntime.ObserveTranscriptAsync:219` triggers
  `DispatchChannelRepliesAsync` on every `AssistantText` — which fans out to
  `ChannelReplyDispatcher.OnTurnEndAsync`, `ReviewReplyDispatcher`, and
  `AgentTaskReplyService.OnTurnEndAsync`; the `AgentTaskDispatcher` sweep (`:673-716`) re-triggers
  settlement on stored `TurnEnd` rows with no stop-reason filter. This **confirms CARD-0071's
  unverified item 2** in its corrected form: the hazard path is the AssistantText trigger plus
  the sweep, not the boundary flush.
- **The reply hazard is real as written.** `ChannelReplyDispatcher.ExtractTurnResponseAsync`
  (`server/Application/Services/ChannelReplyDispatcher.cs:651-675`) gathers `Kind ==
  AssistantText` between prompt and next prompt and filters only `IsNullOrWhiteSpace`. On a 529
  the error string is the only candidate text; dispatch stamps `ChannelReplySettledAt` before the
  produce (CARD-0067), so publishing it also **cancels** the genuine answer.
- **Settlement records the error as the delegate's verdict.** `AgentTaskReplyService.SettleAsync`
  (`server/Application/Services/AgentTaskReplyService.cs:240`) stores the turn-ending response's
  text as `task.Result` and marks `Succeeded`. Both of today's limit-killed delegates settled
  exactly this way: the limit message *was* the report.
- **Prior art for every mechanism this spec needs already exists**: structural-marker predicates
  (`TranscriptKinds.IsInterruptPrompt` / `IsManualCompactBoundary` /
  `IsCompactionContinuationPrompt`, `SessionRunnerContracts.cs:107-296`); detect-marker→queue-
  WhenIdle-continue (`AgentSessionSettings.ResumeAutoContinue` / `ResumeContinuePrompt`,
  `AgentSessionService.EnqueueResumeContinueAsync:1215`); pure static schedule class
  (`CheckSchedule`); queue + hosted service scaffold (`AgentTaskCheckQueue` /
  `AgentTaskCheckHostedService`); park-and-raise-Critical-when-channel-bound (CARD-0055,
  `ChannelReplyLost` = `AgentIncidentKind` 21); the attention projection (`AttentionService`,
  kinds 0–9, shipped) with the mobile home band 1 consuming it (mobile spec §D3).

### The ingestion boundary, stated up front

Detection acts on **ingested transcripts only**. Of the 23 historical stubs, only 2 ever reached
`TranscriptEntries` — the rest died in subagent transcripts (never tailed by design) and
unmanaged sessions. Sessions this spec can protect: Antiphon-managed sessions with a bound
transcript — orchestrators, always-on agents, channel-bound agents, and delegate task sessions.
Subagents fanned out by a delegate's own `Task` tool remain invisible; their parent's next turn
(which IS ingested) is where their failure surfaces, and that is out of scope here. **One
verification step rides slice S1**: today's two delegate deaths (`ee0a18a5`, `27e20988`) were
managed sessions — confirm their stubs are present in `TranscriptEntries`, because if a managed
delegate's stub can fail to ingest, that is an ingestion gap to fix before any detection built on
ingestion can be trusted.

## 1. Decisions

### D1. Detection is structural, and it travels the contract — a sized boundary change, not a text match

Carry three new optional fields end to end: `IsApiError` (bool?), `ApiErrorClass` (string? — the
raw `error` value), `ApiErrorStatus` (int?). They ride `TranscriptPart`,
`RunnerTranscriptEvent`, and `TranscriptEntry` (one EF migration, three nullable columns), and
`TranscriptNormalizer.FromAssistant` stamps them on both parts of the stub line. The honest size:
this is a **contract change across the runner boundary** — but a purely additive one. Optional
record parameters with defaults keep both serialization directions compatible (old runner → new
server: nulls; new runner → old server: unknown JSON members ignored), so no lockstep deploy is
required and the detached pty-hosts' shadow-copied binaries can lag harmlessly.

The predicate joins the family: `TranscriptKinds.IsApiErrorStub(kind, isApiError)` — but unlike
its three siblings it is **structural, not textual**, because for once the raw record hands us
real fields. Text matching ("API Error:", "You've hit your session limit") is rejected as the
primary signal: an agent legitimately *writing about* these errors (as this very spec does) must
not trip it. `StopReason == "stop_sequence"` is also rejected: 35 stored `stop_sequence` rows
against 2 known stubs proves it is not a synonym, and what else produces it is unestablished.

Alongside the predicate, a pure classifier: `ApiErrorClassifier.Classify(class, status, text)` →
one of `Wall` (rate_limit with a parseable reset), `Transient` (server_error / 5xx /
connection-drop), `NeedsHuman` (authentication_failed, model_not_found), `Unknown` (anything
else — treated as Transient with a 3-attempt cap, conservative).

**Retroactivity**: none, deliberately. Rows persisted before S1 lack the fields and stay
undetected. Exactly 2 such rows exist, both in a non-channel-bound orchestrator session; a text
fallback for two dead rows is complexity with no beneficiary.

### D2. Not lockstep — server-side only, and here is why that is safe

The three sibling predicates are lockstep across server `IsWorkingAsync`, client `isWorking()`,
and runner `TranscriptWorkingState` because each marks a record that ends or fails to end a turn
**without a TurnEnd** — miss one implementation and working/idle diverges. The API-error stub is
the opposite case: **it carries its own ordinary `TurnEnd`**, so all three implementations
already read idle correctly with zero changes. The predicate is not a working-rule input; it is
a consumer-side discriminator for settlement, reply dispatch, retry, and display. It therefore
lives server-side (plus the runner merely *carrying* the fields, no runner logic), and widening
the lockstep surface for it would add a fourth thing to keep in sync with no consumer. The
client receives the fields in the transcript DTO for optional rendering (S7), which is cosmetic
and may lag freely.

### D3. Three classes, three responses — the single highest-leverage decision, per CARD-0072's own measurement

| Class | Members (measured share) | Response |
|---|---|---|
| **Wall** | 429 `rate_limit`, session/account limit with `resets HH:MM (Zone)` in the text (18/23 = 78%) | Parse the reset instant. **One** resume scheduled at reset + 2-minute margin. Fleet-wide dispatch pause until then (§D7). No ladder — blind retry against a clock wall is guaranteed to fail and wastes a launch. |
| **Transient** | 529/5xx `server_error`, connection-drop (2/23) | Per-session retry ladder **1, 3, 5, 10, 30, 60 min, then hourly** (operator-requested), as a new pure `ApiErrorRetrySchedule` class — separate from `CheckSchedule`, whose 5-minute rounding floor swallows the 1- and 3-minute rungs and whose problem (backing off a possibly-busy delegate) is not this one (retrying a session structurally proven dead-idle). |
| **NeedsHuman** | `authentication_failed`, `model_not_found` (3/23 incl. the excluded fixture) | **Never retried.** Incident immediately: auth-expired is **Critical fleet-wide** (one shared Claude account — every agent is broken at once, not just this session); model-not-found Critical on the session. |

**Reset parsing** (`UsageLimitResetParser`, pure): `resets 6:10pm (Europe/London)` → next
occurrence of that wall-clock time in that **named** zone at-or-after now, DST-correct via
`TimeZoneInfo.FindSystemTimeZoneById` (IANA ids resolve on .NET 8+/ICU; add `TimeZoneConverter`
only if a canary proves otherwise). **Parse the zone; never assume the host's** — a UTC-read-as-
local mistake already cost a wasted investigation this week, and the tests must include the trap
case (a reset time that differs by an hour between London and UTC). A wall stub whose reset text
**fails to parse** (Claude Code rewording is a real risk — the time lives only in text, unlike
the structural fields) degrades to the Transient ladder **entering at the 30-minute rung** (fast
rungs against a wall are known-wasted) plus a Warning incident quoting the unparsed text, so a
format change is discovered on its first occurrence, not silently.

### D4. The resume: what proves the turn is dead, and why no new typing path exists

The CARD-0055/0056 hazard — re-prompting a session that is not actually dead types into a live
composer — is confronted head-on, and the answer is that this marker is **categorically unlike**
the ones those cards guard against. There, a screen went quiet and we *inferred* death; here,
**Claude Code itself wrote a persisted transcript record declaring the turn over** (the stub's
own `TurnEnd`, the composer returned to prompt — proven live by every measured revival: a human
typed `Continue` into exactly this state and it worked immediately, six times across the sweep).
The positive evidence is the stub row itself, already in `TranscriptEntries`.

Mechanically the resume creates **no new typing path**: it calls
`SessionMessageQueueService.EnqueueAsync(sessionId, prompt, MessageSendMode.WhenIdle, …)` — the
identical shape to `EnqueueResumeContinueAsync` — so every existing guard applies unchanged:
deliver-only-when-idle (the shared working rule), CARD-0055 transcript-confirmed submission with
Enter-only retries, CARD-0056's evidence-phase rules, parking at `MaxDeliveryAttempts`. If the
session somehow *is* mid-turn at fire time (a human revived it first), WhenIdle holds the prompt;
staleness is handled below.

**Dedup and staleness**, so a resume never double-fires or fires into a revived session: one
scheduled resume per stub row (keyed on the stub's transcript `Uuid`); at fire time, skip
entirely if any `UserPrompt` with `Sequence` greater than the stub's exists (someone or something
already continued this conversation), and skip if the session is no longer `Running`
(`SessionRestartBoundary` machinery owns relaunches — this spec only ever continues a live idle
session, never starts one).

**Prompt content is class-specific.** Transient: the `ResumeContinuePrompt` shape ("your
previous turn was killed by a transient API error — review where you got to and continue").
Wall, for a session running a delegated task: additionally **"commit any in-progress work to a
branch first, then continue"** — which is this spec's answer to the dirty-shared-checkout
question (§D6): the session that made the mess still holds the context to attribute and commit
it, which no external process can safely do.

### D5. The channel-reply guard is independent of everything and lands first (CARD-0071's ask)

In `ChannelReplyDispatcher`, everywhere turn text is gathered (`ExtractTurnResponseAsync`, the
follow-up path at `:600`), exclude rows where `IsApiError == true` from the join — and when the
turn's window *contains* a stub, **withhold dispatch for that turn entirely**, even if other
text exists (a multi-call turn can produce real text before a later API call dies; publishing
the fragment would settle the correlation against half an answer). The correlation stays
**owed, never settled**: the resumed turn's genuine answer routes by the same stored
prompt-match (CARD-0067 machinery, untouched), and if no resume ever lands, the existing
`PendingReplyTtlMinutes` sweep raises `ChannelReplyLost` — the designed backstop, already
Critical. Silence plus an owed correlation is defensible; publishing "API Error: 529
Overloaded" to a family chat is not, and consuming the correlation with it is worse.

This slice needs only S1's fields and nothing from retry/resume — **sequenced immediately after
carriage, before any recovery work**, exactly as CARD-0071 requests.

### D6. A limit-killed task must never settle as done

In `AgentTaskReplyService.OnTurnEndAsync`: when the marked turn's turn-ending response is an
API-error stub, the error text is **not a report**. New arm, in the family of
`FailUnreportedTurnAsync`:

- Record incident **`AgentIncidentKind.ApiErrorTurnDied = 22`** (next free value): Warning
  normally; **Critical when the agent is channel-bound** (a human is waiting on a dead line —
  the CARD-0055/0067 severity rule) and for the NeedsHuman class.
- **While a resume is scheduled** (S5 landed and the class retries): the task **stays
  `Working`**, with an `AgentTaskEvent` naming the class and the scheduled fire time ("turn
  killed by rate_limit — resume scheduled 18:12 Europe/London"). The resumed turn's real report
  settles it through the normal path; cost rollup is unaffected (its window is
  `DispatchedAt`→settlement).
- **With no resume coming** (S5 not yet landed, class is NeedsHuman, or retries exhausted): the
  task **Fails** with `FailureReason` naming the class and the error text — visibly dead beats
  invisibly "Succeeded". Never, in any arm, is the error text stored as `Result`.

The check-in ladder needs no change: `DelegateCheckProbe` reads the shared working verdict and
already reads these sessions correctly as idle; the incident and task event give its interpreter
strictly more to see.

**Dirty shared checkout**: automatic salvage-committing is **rejected** for v1 — on a shared
checkout the dirt cannot be safely attributed to the dead task (the operator's own edits live
there too; the salvage of `27e20988` was safe only because a human judged the files). The
resume-prompt commit-first instruction (§D4) covers the measured case by letting the only party
with attribution context — the session itself — do the committing. The incident carries
`git status --short` of the task's working directory when it is the shared checkout, so a human
deciding about a parked task sees the exposure. Auto-salvage is priced as an open question
(§6.4).

### D7. Fleet-wide pause and visibility — CARD-0022's surface

**Superseded 2026-09-01.** One `UsageLimitState` row pausing the whole fleet is the wrong grain:
Fable 5 exhausted while Sonnet 5, Haiku 4.5, and every Grok session stayed healthy. CARD-0022
ships a per-(kind, model-alias) `ModelAvailabilityHold` instead — see
`docs/superpowers/plans/2026-09-01-card-0022-per-model-usage-limit-pause-plan.md`. S1–S3 and S5a
below remain the detection/Transient history.

The subscription is **one shared account**: a wall hit by any session is a wall for the fleet.
New entity `UsageLimitState` (single active row: `HitAt`, `ResetAt`, `RawText`, `SourceSessionId`,
`ClearedAt`), written on any Wall-class detection, cleared lazily when `now >= ResetAt`.
**(Struck — do not implement. The hold table is keyed `(Kind, ModelAlias)`.)**

- **Dispatch pause**: `AgentTaskDispatcher` skips dispatching new tasks while a `UsageLimitState`
  is active (new `SkippedUsageLimit` counter beside `SkippedGlobalConcurrency`), resuming
  automatically at reset — queued tasks dispatch then, rather than launching sessions doomed to
  stall. `Check`-role tasks are also skipped (a check interpreter launched into a wall burns a
  launch to learn nothing).
- **Visibility**: new **`AttentionKind.UsageLimitExhausted = 10`**, severity **Error** while
  active — headline "Usage limit hit 17:42 — resets 18:10 Europe/London (in 23m); dispatch
  paused", evidence carrying the raw message text and the source session. It clears at reset **by
  construction** (derived from `UsageLimitState`, no ack needed — the recency-is-lifecycle rule
  the attention view already follows). The shipped mobile home band 1 and the Orchestrator
  attention tab consume it with **zero client-side work beyond the enum member**, which is the
  reason attention is the home rather than a new banner surface. "What's left" (remaining quota
  %) is **not shown in v1** — nothing measured says it is readable; §6.1 keeps CARD-0022's
  investigation open rather than inventing a number.

### D8. Give-up policy — retry is bounded by escalation, not by silence

- **Wall**: one resume per reset. If the resumed turn dies on a wall again (a longer-period cap
  behind the 5-hour window — CARD-0022's window-semantics worry), each death re-schedules
  against its own stated reset; **3 consecutive wall deaths** on one session escalates the
  incident to Critical and parks the session's resume (stop scheduling; the attention row and
  the parked state are the human's cue) — the same cap-then-escalate shape as CARD-0056's
  re-adoption cap.
- **Transient**: the ladder runs to hourly **indefinitely** (operator's explicit request; an
  hourly nudge through the fully-guarded queue is cheap) — but visibility is mandatory: a
  Warning incident when total dead time crosses 2 hours, upgraded to **Critical when
  channel-bound** (the az-care shape: 14.5h of silence at a real human). Not a new incident kind
  — the same `ApiErrorTurnDied` escalated, so the timeline reads as one story.
- **NeedsHuman**: parked immediately at Critical (fleet-wide for auth), zero retries — retrying
  an expired login forever is a new failure mode, not a fix.

## 2. The three cards, reconciled

| Card | Verdict | What it keeps | What moves |
|---|---|---|---|
| **CARD-0022** | **Survives, narrowed** — the operator's ask, and the owner of the Wall class end to end: visibility (S6), fleet pause (S6), resume-at-stated-reset (S4+S5 wall arm). | "Show when it resets", pause dispatch, resume after; the open investigation of remaining-quota readability and window semantics (§6.1, §6.5). Its "capture the exact marker" investigation is **done** — the sweep measured it; fixtures replace the proposed headed canary (§3, testing). | Nothing out; **absorbs** CARD-0072's late "account session limit" addendum, which is this card's subject and is struck from 0072's scope. |
| **CARD-0071** | **Survives, narrowest, first to close** — what the pipeline does with a dead turn it can already see. Closes when S2 + S3 land. | The reply-publication guard (S2) and the settlement guard (S3); its "queue flush probably fires too" item is settled by §0 (boundary flush does NOT fire; AssistantText trigger + sweep DO). | Its "where the distinction is preserved / lockstep" open questions are answered here (§D1, §D2) rather than on the card. |
| **CARD-0072** | **Survives, narrowed** — detection carriage and the Transient class: the fields (S1), the retry ladder + give-up (S5). Its frequency sweep remains the evidence record cited by all three. | `IsApiErrorStub`, the classifier, the 1/3/5/10/30/60/hourly ladder, escalation thresholds. | The **"account session limit" addendum transfers to CARD-0022** (duplication resolved: 0072 filed it before noticing 0022 existed); the reply-hazard half was already explicitly divided out to 0071 by 0071's own correction note, which stands. |

**Net**: no card closes as a pure duplicate today. 0071 closes first (S2+S3); 0022 closes when
S4+S5+S6 land; 0072 closes when S1+S5 land. The one true duplication — the wall-resume text on
0072 — is transferred, not merged silently.

## 3. Server design

- **`src/Antiphon.SessionRunner.Contracts/SessionRunnerContracts.cs`** — three optional
  parameters on `RunnerTranscriptEvent`; `TranscriptKinds.IsApiErrorStub(string? kind, bool?
  isApiError)`; XML-doc naming the measured shape and the 2026-08-17 misses.
- **`src/Antiphon.SessionRunner/TranscriptNormalizer.cs`** — `TranscriptPart` gains the three
  fields; `FromAssistant` reads top-level `error`/`isApiErrorMessage`/`apiErrorStatus` and stamps
  them on the stub's `AssistantText` and `TurnEnd` parts.
- **`server/Domain/Entities/TranscriptEntry.cs`** + migration — three nullable columns; the
  transcript DTO mapping passes them through.
- **`server/Application/Services/ApiErrorClassifier.cs`** (new, pure) and
  **`UsageLimitResetParser.cs`** (new, pure) — no clock, no DB; the parser takes `now` and
  returns `DateTimeOffset?`.
- **`server/Domain/Entities/UsageLimitState.cs`** (new) + migration;
  **`server/Application/Services/ApiErrorRecoveryService.cs`** (new) — observes stub rows at
  persist time (hooked where `AgentSessionRuntime.ObserveTranscriptAsync` already fans out,
  keyed on the persisted row, replay-safe via the stored-uuid dedup that guards turn boundaries),
  classifies, writes `UsageLimitState` (Wall), schedules resumes into
  **`ApiErrorRetryQueue`/`ApiErrorRetryHostedService`** (new, the `AgentTaskCheckQueue` scaffold
  shape), raises `ApiErrorTurnDied`. Fire-time staleness checks per §D4.
- **`server/Application/Services/ChannelReplyDispatcher.cs`** — stub exclusion + withhold (S2).
- **`server/Application/Services/AgentTaskReplyService.cs`** — settlement arm (S3).
- **`server/Application/Services/AgentTaskDispatcher.cs`** — `SkippedUsageLimit` gate (S6).
- **`server/Application/Services/AttentionService.cs`** + `AttentionDtos.cs` + client
  `attention.ts` enum — `UsageLimitExhausted` row (S6).
- **`server/Domain/Enums/AgentIncidentKind.cs`** — `ApiErrorTurnDied = 22`.

**Testing stance**: the stub shapes are pinned from the 23 measured records as **fixtures**
(fakeclaude emits them via `FakeClaudeContractTests`, per the CARD-0030/0037 pattern of modeling
measured behavior) — a headed canary that *hits a real limit on purpose* is neither reproducible
nor affordable, so unlike the interrupt/compaction canaries there is no live-Claude pin; the
mitigation for shape drift is D3's fail-safe parse degrade. Integration tests scope every
assertion to their own rows (shared-Postgres rule), and the dispatcher-gate test must not assert
global counts (the `OrchestratorServiceIntegrationTests` shape warning).

## 4. Slices (each independently landable; every one leaves the app shippable)

| # | Slice | Contents | Tests | Tier | Depends on |
|---|---|---|---|---|---|
| **S1** | Carry the discarded fields | Contracts + normalizer + entity + migration + `IsApiErrorStub` + `ApiErrorClassifier` (pure) + fakeclaude stub fixtures; **verify today's two delegate stubs are in `TranscriptEntries`** (ingestion-gap check, §0) | `TranscriptNormalizerTests` (real-JSONL fixtures incl. the benign synthetic and the 404 fixture as negatives), `FakeClaudeContractTests`, classifier unit tests | opus (boundary change) | — |
| **S2** | Channel-reply guard | `ChannelReplyDispatcher` stub exclusion + whole-turn withhold, correlation left owed | `ChannelReplyDurabilityTests` additions: stub-only turn publishes nothing and stays owed; mixed turn withholds; TTL still raises `ChannelReplyLost`; **no Kafka produce in any stub case** | sonnet + review | S1 |
| **S3** | Settlement guard + incident | `AgentTaskReplyService` arm, `ApiErrorTurnDied = 22`; Fail-with-reason arm (defer-to-resume arm activates when S5 lands) | `AgentTaskReplyServiceTests`: stub turn never settles Succeeded, error text never stored as `Result`, incident severity by channel-binding | opus | S1 |
| **S4** | Wall state + parser | `UsageLimitResetParser`, `UsageLimitState` + migration, written on Wall detection (no consumer yet — state + log only is still shippable) | Parser: pm/24h forms, **zone-vs-host trap**, DST edge, midnight wrap, unparseable→null; state write/clear | sonnet | S1 |
| **S5** | The resume itself | `ApiErrorRecoveryService` + retry queue/hosted service + `ApiErrorRetrySchedule` (pure) + class prompts (wall prompt carries commit-first) + dedup/staleness + give-up (§D8) + S3's defer arm | Schedule arithmetic (pure); recovery integration: fires at reset, skips on later `UserPrompt`, parks at caps, NeedsHuman never schedules; queue-delivery path untouched (enqueue-only — assert no new send path) | opus (the hazardous slice) | S1, S3, S4 |
| **S6** | Fleet pause + visibility | Dispatcher `SkippedUsageLimit` gate; `AttentionKind.UsageLimitExhausted` + service condition + client enum/row | Dispatcher skips while active and resumes after reset (row-scoped assertions); attention row present-while-active/absent-after, severity, headline wording | sonnet | S4 |
| **S7** | Transcript rendering (optional) | Client: stub rows render as an error chip, not agent speech | Component test | haiku/sonnet | S1 |

Order: **S1 → S2 → S3** (the CARD-0071 hazard is dead after S3, before any recovery exists),
then S4 → S5 and S6 in either order; S7 anytime after S1. S2 before S3 because a published wrong
reply is worse than a wrong board state.

## 5. Collision map (files not to touch blind)

- **`ChannelReplyDispatcher.cs`** — CARD-0055 slice 7 (`DispatchFollowUpAsync`'s
  `latestPromptSeq` bail) is explicitly still open per CARD-0067's note and may be picked up
  concurrently; **rebase-check before S2**, and S2's withhold must apply to the follow-up path
  too (it does — §D5 names it).
- **`SessionMessageQueueService.cs`** — owned by CARD-0035 s4–6 (DTO mapping) per the mobile
  spec §8. **S5 must not edit this file** — it only calls `EnqueueAsync`, which is the design
  anyway (§D4).
- **`AttentionService.cs` / `AttentionDtos.cs` / client `attention.ts`** — CARD-0035 s4–6 are in
  flight around the attention surface (panel, badge). S6 adds one enum member + one condition
  builder; rebase-check and keep the addition append-only.
- **`AgentTaskDispatcher.cs`** — carries the CARD-0046 sweep and the CARD-0047 listen gate;
  active area. S6's gate is a new early `continue` beside `SkippedGlobalConcurrency`.
- **`SessionRunnerContracts.cs`** — shared by everything including the pty-host shadow copies;
  S1 must stay purely additive (optional params with defaults only).
- **`AgentIncidentKind.cs` / `AttentionKind`** — if another in-flight card claims 22 or 10
  first, take the next value; the numbers in this spec are reservations, not requirements.

## 6. What I could not determine

1. **Whether remaining quota is readable at all** (CARD-0022's "show what's left"). `/usage`
   renders a TUI screen; whether it writes a parseable JSONL record is unmeasured, and polling it
   into live sessions writes local-command records (excluded from working/idle, but still noise).
   Needs a cheap headed probe; until then v1 shows the wall + reset only. **Stays open on
   CARD-0022.**
2. **What else produces `stop_sequence`** (35 rows vs 2 stubs). Moot for this design — detection
   keys on `isApiErrorMessage` — but worth a one-off query before anyone ever leans on
   `StopReason` again.
3. **Stability of the limit-message text across Claude Code versions.** The structural fields
   look stable (they are Claude Code's own error plumbing); the reset **time** lives only in
   text. D3's degrade path (30-min ladder + Warning naming the unparsed text) is the designed
   answer to drift; there is no way to pin a future version's wording in advance.
4. **Auto-salvage of a dirty shared checkout** at give-up time (not at death — §D6 rejects that).
   Priced: a `git stash push` or branch commit scoped to the task's `ScopeGlob` could bound the
   attribution problem, but `ScopeGlob` is optional and the operator's own edits still overlap.
   Needs an operator decision, not archaeology.
5. **The longer-period cap's message shape** (weekly limit behind the 5-hour window — CARD-0022's
   window-semantics question). Never observed on this machine; assumed to state its own reset the
   same way. D8's wall arm handles it if so; if it arrives with no reset text, D3's degrade
   catches it.
6. **Whether the 9 benign `"No response requested."` synthetics can ever co-occur with
   `isApiErrorMessage: true`.** Assumed disjoint (measured disjoint in all 32 hits); S1's
   normalizer test pins the benign shape as a negative so a collision would fail loudly.
