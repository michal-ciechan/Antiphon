# CARD-0082: context-window tracking and idle auto-compaction (Claude only)

**Date:** 2026-08-18
**Status:** planned (task cdc2ca66)
**Scope:** Claude Code sessions only. Grok/Codex/OpenCode are explicitly out — CARD-0083 owns
whether a common per-provider contract should exist; nothing here generalizes.

## Verdict

Compute live context fullness **on read** from the newest usage-bearing transcript row (no new
cached state), with the per-model ceiling in **configuration** (Claude's JSONL carries no ceiling
anywhere — measured, see fact 3) and the model id **carried onto the transcript** the same additive
way CARD-0072 S1 carried the API-error fields. Host the idle+context check as a new **once-a-minute
pass in `AgentSupervisorHostedService`** — the CARD-0067 sweep precedent, and the only existing
clock whose shape fits (an idle session never ends a turn to hook on). Deliver `/compact` through
`SessionMessageQueueService.EnqueueAsync` WhenIdle with a new `Supervision` origin whose rule is
**cancel, never strand**: an auto-compact that can't deliver right now is dropped and re-derived by
a later sweep, not parked for a human. Four slices, ~3–4 days total.

## Measured facts (2026-08-18, this machine, current codebase)

1. **Usage is already parsed and stored.** `TranscriptNormalizer.GetUsage`
   (`src/Antiphon.SessionRunner/TranscriptNormalizer.cs:247`) reads
   `input_tokens/output_tokens/cache_read_input_tokens/cache_creation_input_tokens` off every
   assistant record; they flow through `TranscriptPart` → `RunnerTranscriptEvent` →
   `TranscriptEntry` (columns `InputTokens/OutputTokens/CacheReadTokens/CacheCreationTokens`,
   `TranscriptEntry.cs:51-54`). All rows of one API call share `ApiCallId` and repeat identical
   numbers. A live main-session JSONL here shows the shape:
   `{"input_tokens":2,"cache_creation_input_tokens":3744,"cache_read_input_tokens":122765,"output_tokens":7098,...}`
   — the conversation's size lives almost entirely in `cache_read_input_tokens`, which is why
   cumulative-spend arithmetic (`DelegationCost.cs`) is a different number and must not be reused.
2. **`message.model` is on every assistant record** (269 occurrences of `"model":"claude-fable-5"`
   in the newest main-session file) but is **not carried** by the normalizer today; the only stored
   model is `AgentSession.EffectiveModelId` — launch intent, which a mid-session `/model` switch
   silently invalidates.
3. **No context-window ceiling exists anywhere in the JSONL or the codebase.** The usage block has
   no limit field; grep confirms no constant. Configuration is the only honest source. One
   self-calibration hook exists — an `(auto)` compact boundary's `compactMetadata.preTokens`
   ≈ ceiling × Claude's own threshold — judged deferred (below).
4. **Sidechain records do not pollute the main file**: `grep -c '"isSidechain":true'` over the
   newest main-session JSONL = 0. Subagent transcripts are separate files, never tailed (per the
   2026-08-17 resilience spec's ingestion-boundary note). Latest-usage-row queries are safe without
   a sidechain filter; fakeclaude already stamps `isSidechain:false`.
5. **The raw-typed `/compact <instructions>` shape is already recognized**:
   `TranscriptKinds.TryReadLocalCommandName` handles both the `<command-name>` wrapper and raw
   `"/compact do the thing"` (pinned, `DelegationUnitTests.cs:847`), and CARD-0056 pinned
   `PromptSubmissionMatch` confirming against wrapper records. A `/compact` body **with
   instructions** (≥ `MinMatchChars` = 12) therefore gets CARD-0055's full text-match confirmation
   for free; a bare 8-char `/compact` would fall to the weak-match arm — so the trigger always
   sends instructions.
6. **Everything downstream of a fired compaction already works** (CARD-0041): the `(manual)`
   CompactBoundary is a turn end in all three working-rule implementations, the continuation
   prompt is excluded from activity, and the boundary fires the narrow `FlushIfIdleAsync`. This
   plan only ever *triggers* compaction.
7. **CARD-0057 has no implementation in the codebase** (zero grep hits). There is no
   scheduled-prompt-to-an-agent mechanism to reuse; the brief's pointer resolves to nothing.

## The five questions, answered

### 1. Fullness: formula, ceiling, granularity

**Formula**: `InputTokens + CacheReadTokens + CacheCreationTokens + OutputTokens` of the **newest
usage-bearing row** (`InputTokens != null`, max `Sequence`). The last call's input side is the
whole conversation *before* that call; its output joins the context for the next call, so
excluding `OutputTokens` (the card's suggested formula) undercounts by up to one turn's output —
cheap to include, so include it. API-error stubs are `<synthetic>` and carry no usage, so they
never win the query.

**Invalidation**: if any `CompactBoundary` row (either trigger) — or a `/clear`
local-command record — exists at a *later* sequence than that usage row, fullness is **unknown**
(`null`): the stored numbers describe a context that no longer exists, and no new number exists
until the next real turn's API call. Unknown never fires the sweep and renders as "compacted,
awaiting next turn" in the UI. This is also the natural anti-refire: post-compact fullness cannot
re-trip the threshold.

**Ceiling**: new `ContextWindowSettings` (registered like `SupervisionSettings`):
`DefaultContextTokens = 200_000` plus `ModelOverrides: Dictionary<string,int>` matched by
case-insensitive substring against the model id (e.g. `"[1m]" → 1_000_000` for a 1M-beta model
string). The model id comes from a new **`Model` carried on the transcript** (S1): additive
optional param on `TranscriptPart`/`RunnerTranscriptEvent`, one nullable `Model` column on
`TranscriptEntry` — the CARD-0072 S1 shape exactly, so old/new runner/server mixes stay
compatible and detached pty-hosts may lag. Fallback chain when the winning row has no model
(pre-migration rows, deliberately no backfill): `AgentSession.EffectiveModelId`, then the default.

**Staying correct**: it's config — editable without a deploy when Anthropic changes limits or the
account gains a bigger window. Two self-checks make a wrong ceiling loud instead of silent:
computed fullness **> 100%** logs a Warning naming the model and configured ceiling (only reachable
via misconfiguration), and an **`(auto)` CompactBoundary on a session we computed at < 80%** logs a
Warning too (Claude compacted where we thought there was headroom ⇒ our ceiling is too big).

**Granularity**: recomputed on read — one indexed query per session (`(AgentSessionId, Sequence)`
unique index already exists), no persisted column, no cache to go stale, retroactive by
construction. The sweep touches it once a minute for running Claude sessions; the session DTO
computes it on fetch.

### 2. Where the sweep lives

A new minute-period pass in **`AgentSupervisorHostedService`** (beside the CARD-0067
`ChannelReplySweepPeriod` pass, same `_lastXUtc` pattern), calling a new scoped
`ContextCompactionService.SweepAsync`. Why not the alternatives:

- **`AgentSupervisorService.TickAsync`** (10 s): scoped to `AlwaysOn` agents and to
  restart-ladder logic; auto-compact applies to any agent-owned session and 10 s is needless churn
  for an 8-hour condition.
- **CARD-0047's `CheckSchedule`/`AgentTaskCheckQueue`**: task-scoped — the ramp is keyed to an
  `AgentTask`'s dispatch and check count. An idle session by definition has no active task driving
  checks; forcing this shape means inventing phantom tasks.
- **The resilience spec's S5 `ApiErrorRetryQueue`/`ApiErrorRetryHostedService`** (not yet built):
  that is **one-shot event-scheduled** work — a stub row schedules a single fire at a computed
  instant. This is a **standing condition-scan** — no event creates it; the condition (idle ∧
  full) must be re-derived on a clock nobody's event owns. Different shape; sharing a queue would
  couple two unrelated lifecycles. When S5 lands they coexist as two consumers of the same hosted-
  service *pattern*, not the same mechanism.
- **CARD-0057**: does not exist in code (fact 7).

The CARD-0067 precedent is the on-point one: "a session that answered into a void may never end
another turn to be swept on" — likewise, an idle session never ends a turn, so only a global clock
can notice it.

**Eligibility**: sessions with `Status == Running`, `AgentKind == ClaudeCode`, that are some
Agent's `PersistentSessionId` (this also resolves whose per-agent overrides apply). Unclaimed
sessions — including CARD-0056 re-adopted ones, which have been the *operator's own live
conversation* — are never auto-compacted: compacting someone's context without consent is a
CARD-0056-class harm. Sessions with zero transcript rows (bind failed) read unknown on both
conditions and are skipped.

**Idle-for**: `now − max(Timestamp)` over **all** the session's transcript rows (any kind,
local-command records included) — deliberately more conservative than the working-rule exclusions:
an operator running `/status` an hour ago is presence, and the safe direction is to *delay*
compaction. Additionally `IsWorkingAsync` must be false. At an 8-hour threshold, ingestion-lag
races (the measured 45 s flush stall) are noise.

### 3. Safe delivery of the trigger

At fire time, per candidate (rare — expected ≪ 1/day/agent):

1. **Pull before acting**: `AgentSessionRuntime.CatchUpTranscriptAsync` — the 2026-08-16 rule
   that no destructive-ish action runs on "the transcript doesn't show activity" without a pull.
   Recompute idle-for, fullness, and `IsWorkingAsync` from stored rows after the pull.
2. **Enqueue, never type**: `SessionMessageQueueService.EnqueueAsync(sessionId, body,
   MessageSendMode.WhenIdle, origin: QueuedMessageOrigin.Supervision)` — a new origin enum value
   (`Supervision = 5`; Ui/Channel/System/Delegation/Check take 0–4). Zero new typing paths:
   CARD-0055 transcript-confirmed submission,
   Enter-only retries, CARD-0056 evidence rules, all inherited.
3. **Body**: `/compact Focus the summary on: current task state, key decisions and their reasons,
   file paths touched, and anything you committed or still owe.` — instructions serve two
   purposes: a better summary, and a body long enough (fact 5) that confirmation is a real text
   match (both the raw-typed record and the wrapper confirm), never the weak arm.
4. **The became-busy race**: WhenIdle's deliver-if-idle re-checks working at delivery time. The
   new rule for `Supervision`-origin rows is **cancel-not-strand / cancel-not-park**: if not
   deliverable immediately (session working), stamp `CanceledAt` instead of leaving it Pending —
   a Pending `/compact` flushed at the *next turn end* would compact a session that just became
   active, the exact wrong moment. Likewise the `MaxDeliveryAttempts` park arm: Supervision rows
   cancel with a Warning incident instead of parking (parking exists for human-owed content; a
   later sweep re-derives this condition for free). Two small arms in the enqueue/flush/failure
   paths, gated on origin.
5. **Success/failure observation**: success = a `(manual)` CompactBoundary past the submitted
   prompt's sequence — everything downstream is CARD-0041's, untouched; the boundary's timestamp
   resets the idle clock, and fullness reads unknown (question 1), so no refire. Failure = no
   boundary within `BoundaryTimeoutMinutes` (10) of confirmed submission → Warning incident, new
   `AgentIncidentKind.AutoCompactFailed = 23` (next free after `ApiErrorTurnDied = 22`;
   reservation, not requirement).
6. **Cooldown, derived not stored**: never fire when any `/compact` submission (raw or wrapper,
   via `TryReadLocalCommandName`) **or** any CompactBoundary exists within `CooldownHours` (24) —
   both are transcript rows, so the guard is restart-proof and retroactive with zero new columns.
   An in-memory per-session attempt stamp additionally covers the seconds-wide delivery window; a
   server restart losing it can at worst re-attempt a compact whose preconditions still hold —
   wasteful once, not harmful.

### 4. Settings shape and defaults

**Global** — new `ContextCompactionSettings` section (pattern of `SupervisionSettings`, with an
`IValidateOptions` validator like `AgentSessionSettingsValidator`):

```
Enabled                = true
IdleMinutes            = 480      // 8 h
ContextPercent         = 50
CooldownHours          = 24
BoundaryTimeoutMinutes = 10
DefaultContextTokens   = 200_000
ModelOverrides         = {}       // substring → tokens, e.g. "[1m]": 1000000
```

**Per-agent** — three nullable columns on `Agent` (+ migration + DTO + agent-edit UI):
`AutoCompactEnabled` (bool?), `AutoCompactIdleMinutes` (int?), `AutoCompactContextPercent` (int?);
null = global. One honesty note: the brief cites `AgentSessionSettings.ResumeAutoContinue` as the
per-agent-override precedent, but that setting is **global-only** (`IOptions`, no per-agent
surface). The actual per-agent precedent in this codebase is nullable/flag columns on `Agent`
(`RemoteControlEnabled`, `ReplyStyle`, `SystemPromptAppend`) — this plan follows that shape and
adds `AutoCompactEnabled` beyond the brief's two values because "turn it off for my pet agent" is
the first override an operator will want.

**Defaults confirmed at 8 h / 50%**, with reasoning: compacting an idle session is near-free (no
turn in flight; 8-hour-stale detail is what summaries preserve well) while *not* compacting risks
Claude's own auto-compact firing mid-turn at its ~92% threshold — which CARD-0041 established is
the worst moment (mid-request, must stay housekeeping, false-idle hazards). 50% of 200k leaves
~100k of headroom — a full heavy working day — before the built-in auto-compact could trigger.
Materially lower would churn (a compact after every active day); higher shrinks the headroom the
feature exists to protect. 8 h means "overnight", comfortably past any legitimate mid-work pause
and 640× the measured worst transcript-flush stall.

### 5. Slices

| # | Slice | Contents | Tests | Size | Depends on |
|---|---|---|---|---|---|
| **S1** | Carry `Model`, compute fullness | Additive `Model` on `TranscriptPart`/`RunnerTranscriptEvent`; normalizer stamps `message.model` on assistant parts; `TranscriptEntry.Model` column + migration (no backfill); `ContextWindowSettings`; pure `SessionContextUsage.Compute` (latest-usage selection, boundary/clear invalidation, ceiling resolution, >100% self-check); fullness on the session DTO | `TranscriptNormalizerTests` (model stamped, synthetic negative); computation unit tests (formula incl. output, invalidation, fallback chain, unknown cases); fakeclaude emits `model` | **M, ~1 day** | — |
| **S2** | Settings + per-agent overrides | `ContextCompactionSettings` + validator; three `Agent` columns + migration; agent DTOs + edit UI fields | Settings validation; override-resolution unit tests (null falls to global) | **S, ~0.5 day** | — |
| **S3** | Sweep + verified trigger | `ContextCompactionService.SweepAsync` + minute pass in `AgentSupervisorHostedService`; eligibility + idle-for; pre-fire `CatchUpTranscriptAsync`; `QueuedMessageOrigin.Supervision` + cancel-not-strand/cancel-not-park arms; `/compact` body; derived cooldown; `AutoCompactFailed = 23` incident | Integration (shared-Postgres rules: **row-scoped assertions only**, and the sweep test needs `[NotInParallel]` with no group key — it drives a global sweep): fires on idle+full, skips busy/unknown/unclaimed/cooldown, cancel on became-busy, boundary resets, incident on timeout; queue tests for the two origin arms | **M, ~1.5–2 days** | S1, S2 |
| **S4** | UI surfacing | Context-% badge on session header / agent card, "compacted — awaiting next turn" state, threshold coloring | Component test | **S, ~0.5 day** | S1 |

Order: S1 → S2 → S3; S4 anytime after S1 (S1+S4 alone already deliver the card's item 1 and are
independently shippable). S3 is the hazardous slice — it types into live sessions — and inherits
every CARD-0055/0056 guard by construction because it never leaves the queue path.

## The deferred list, judged

| Item | Verdict |
|---|---|
| Provider generalization (Grok `session_recap`, etc.) | **Out by order** — CARD-0083's question. Nothing in S1–S4 hardcodes against it: fullness is computed from stored usage columns any provider's tailer could fill. |
| Self-calibrating the ceiling from `(auto)` boundary `preTokens` | **Deferred.** Clever and measurable, but at a 50% threshold a config value is accurate enough, and the < 80%-auto-compact Warning (question 1) already detects a wrong ceiling. Revisit only if that Warning ever fires. |
| Compacting *busy* sessions pre-emptively near the ceiling | **Never** — CARD-0041 established mid-turn compaction is the hazard, not the fix. This feature exists to make that moment rare, not to recreate it. |
| `/clear` as the reclaim mechanism | **Rejected** — destroys context outright and forks the conversation file (CARD-0041's tailer-fork hazard). `/compact` preserves a summary and stays in-file. |
| Persisted/cached fullness column | **Rejected** — derivable on an indexed query; a cache is one more thing to go stale after backfill reordering. |
| Remaining-quota / usage-limit display | **Not this card** — CARD-0022's open investigation (resilience spec §6.1). |
| Per-*session* overrides (beyond per-agent) | **Deferred** until a real need; the eligibility rule already keys sessions to their owning agent. |
| Warm-pool delegate special-casing | **Not needed** — pool delegates have `Agent` rows, so the same per-agent config applies; compacting a warm delegate's stale context before reuse is a feature, not a bug. |

## Collision map

- **`SessionMessageQueueService.cs`** — S3's two origin arms touch the enqueue/flush/failure
  paths; CARD-0035 s4–6 (DTO mapping) and the resilience spec's S5 (which deliberately only
  *calls* `EnqueueAsync`) are adjacent. Rebase-check before S3; keep the arms as narrow
  origin-gated early-outs.
- **`AgentIncidentKind.cs`** — 22 is taken (`ApiErrorTurnDied`, landed); 23 is a reservation. If
  the resilience S5 or another card claims it first, take the next value.
- **`QueuedMessageOrigin`** — additive enum member (`Supervision = 5`; 0–4 are taken by
  Ui/Channel/System/Delegation/Check). Like Ui/System it must NOT batch — one `/compact` is one
  delivery.
- **`SessionRunnerContracts.cs` / `TranscriptNormalizer.cs`** — S1 must stay purely additive
  (optional params with defaults), the CARD-0072 S1 rule; shadow-copied pty-hosts may lag.
- **`AgentSupervisorHostedService.cs`** — append a third timed pass beside prune and
  channel-sweep; CARD-0067's pass is load-bearing, don't restructure.
