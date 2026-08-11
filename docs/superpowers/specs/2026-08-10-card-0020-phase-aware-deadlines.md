# CARD-0020 — Phase-aware deadlines for delegated tasks

**Status:** Plan (not implemented). Task d69ac19d, 2026-08-10.
**Card:** CARD-0020 "Addendum to CARD-0003: the stall backstop cannot fire, and a phase-aware deadline" (Backlog)
**Relates to:** CARD-0003 "Surface launch and delivery errors instead of leaving a task silently Dispatched" (Review)

---

## 1. How CARD-0003 and CARD-0020 relate — and what is ALREADY DONE

CARD-0020 was filed 2026-08-09 21:22Z as an addendum to CARD-0003 (a card cannot be commented on
without spawning an agent — see CARD-0019). Commit **`10d379e`** ("reliable comms between
orchestrator and delegates — four live misses closed") landed ~1h40m LATER the same evening and
implemented CARD-0003 **plus roughly half of CARD-0020**. CARD-0020's description is therefore
written against code that no longer exists; anyone picking it up cold would re-diagnose fixed
problems. The delta:

| CARD-0020 claim | Status on master today |
|---|---|
| "Zero transcript entries N min after DispatchedAt must FAIL the task with the delivery error attached" | **DONE** — `AgentTaskDispatcher.FailNeverStartedAsync` (AgentTaskDispatcher.cs:229), `DeliveryFailTimeoutMinutes = 10`. Fails loudly with the queue's own evidence, kills the session, removes the ephemeral agent, reports the death to the parent session. Verified live: it failed the stranded task fe53500d. |
| "A shell caller with no token inherits the SERVER's cwd; no reply routing — the report lands on the board" | **DONE** for real callers — session-scoped delegation tokens (`AgentSession.DelegationTokenHash`, `AgentTaskService.AuthenticateAsync` session fallback). Always-on sessions launch with `ANTIPHON_TASK_TOKEN` in env, so a shell caller inherits *its own* cwd and gets reports typed back into its conversation. The literal tokenless path (`AgentTaskEndpoints.cs:84` → server cwd) still exists but is now genuinely UI-only. |
| Stall scan only covers roles with BOTH `EscalateTo` and `EscalateAfterMinutes` (only Debug) | **Still true** — `AutoEscalateStalledAsync` (AgentTaskDispatcher.cs:158-163). Plan/Code/Review/Frontier work is never examined for mid-work stalls. |
| Stall scan skips tasks at/above the escalation target (Frontier always skipped) | **Still true** (:180). Deliberate for *escalation* — but it means no stall detection at all for top-tier work. |
| `RolePolicyEntry.TimeoutMinutes` (default 60) declared and never read | **Still true** — dead config (DelegationSettings.cs:152; only `DeliveryFailTimeoutMinutes` is read). |
| Phase-aware deadline (first-token vs tool-run) | **Not started.** This is the core remaining work. |
| Incidental: card create with >4000-char description dies as an unhandled 500 | **Still true** — `CardService.ValidateCreateRequest` (:300) checks only Title/Priority; `Card.Description` is `varchar(4000)`. |

**Verdict: neither card supersedes the other.**
- CARD-0003's three asks (fail undeliverable prompt, surface the error, backstop) plus the
  Frontier-agent-row fix are all delivered by `10d379e` → CARD-0003 can move Review → Done on its
  own merits.
- CARD-0020 stays open, **rescoped** to what this plan covers: the phase-aware deadline, a real
  hard deadline (resurrecting `TimeoutMinutes`), stall *visibility* for every role and tier, and
  the card-validation incidental. Its "zero-transcript" rule and the cwd/reply-routing silence
  should be marked done-by-`10d379e` when the card is next touched.

## 2. Design

### 2.1 The three-layer deadline model

Today's two scans stay and a third is added. Each answers a different question:

1. **`FailNeverStartedAsync`** (exists) — *"did the brief ever arrive?"* Zero transcript entries
   after 10 min → fail. Delivery-class failure; never escalates.
2. **`AutoEscalateStalledAsync`** (exists, unchanged) — *"should a cheaper tier hand this up?"*
   Stays opt-in per role (Debug), keeps its at-or-above-target skip: escalation is a tier-ladder
   move, and the "laundering a lost prompt into a billed upgrade" objection is already met because
   layer 1 and layer 3 catch the pathological cases first, for every role.
3. **`WatchPhaseDeadlinesAsync`** (NEW) — *"is the session healthy for the phase it is in?"* Scans
   **every** Dispatched/Working task with a session — no role filter, no tier filter. Two-stage
   response: **flag** on a phase breach (visible incident, task event, SSE), **fail** at the hard
   per-role deadline (`TimeoutMinutes`, finally read). Never escalates.

### 2.2 Phase classification

Phase is derived from the task session's transcript, exactly as the card proposed
(`TranscriptKinds`, SessionRunnerContracts.cs:107). Implemented as a pure static
`DelegationPhase Classify(...)` over the most recent entries so it is trivially unit-testable.

Take the latest *meaningful* entry — ordered by `Timestamp ?? CreatedAt` descending (NOT bare
`Sequence`: stored sequences are ARRIVAL-ordered and a restart backfill reorders them; record
timestamps survive reordering — same reasoning as the `IsWorkingAsync` timestamp override,
CLAUDE.md 2026-08-08). Skip non-signal kinds while walking back: `TurnTitle`, `CompactBoundary`,
and local-command records (`TranscriptKinds.IsLocalCommandRecord` — a `/model` writes a USER
record with no TurnEnd and must not read as "waiting on model").

| Latest meaningful entry | Phase | Waiting on | Deadline knob (default) |
|---|---|---|---|
| — none — | Delivery | brief delivery | layer 1 owns this (10 min) |
| `UserPrompt` / `ToolResult` | AwaitingFirstToken | model first token | `FirstTokenTimeoutMinutes` (**5**) |
| `Thinking` / `AssistantText` | Streaming | next streamed block | `StreamIdleTimeoutMinutes` (**5**) |
| `ToolCall` | ToolRunning | LOCAL tool (build/test/grep) | `ToolRunningTimeoutMinutes` (**30**) |
| `TurnEnd` / `SessionRestartBoundary` / interrupt marker (`IsInterruptPrompt`) | Idle | the delegate to settle, or a queued delivery | `IdleUnsettledTimeoutMinutes` (**15**) |

The clock for a phase is `now − (latest entry's Timestamp ?? CreatedAt)`; for Delivery it is
`now − DispatchedAt`.

Two refinements over the card's sketch:

- **First-token deadline is minutes, not the card's ~60s.** Claude Code retries overloaded/5xx
  API errors with backoff for several minutes while writing *nothing* to the JSONL; server-side
  ingestion (event pump → persist) adds its own lag, and a reconnect catch-up can deliver a burst
  late. 60s would page on every rough API patch. 5 min still catches a genuinely dead upstream
  call ~2× faster than the old wait-for-a-human, without false-positiving on a retry storm. All
  four knobs are config (`Delegation:*`), so tightening later is a config change.
- **Idle is NOT "n/a" for a delegate** (the card's table said n/a). A Dispatched task whose
  session reached TurnEnd and then sits idle without settling is a real failure class: the
  delegate finished talking but never called task-complete — or its WhenIdle brief/follow-up is
  still queued. Rule: if any Delegation-origin `SessionQueuedMessages` row for the session is
  Pending, the phase is **Delivery** (something is still owed to the session — the stranded-queue
  watchdog's territory, don't double-fire); otherwise idle-unsettled past the knob → flag.

**Warm-pool caveat (correctness-critical):** a reused warm session carries the PREVIOUS task's
transcript, so classification must only consider entries with `(Timestamp ?? CreatedAt) >
task.DispatchedAt`. Without this, the previous task's TurnEnd makes a task whose brief is still
queued read as Idle from second zero. (Layer 1 already dodges this via a different accident — a
reused session has entries, so zero-entry never fires — which also means *reused* sessions
currently have NO delivery backstop at all; the Delivery phase above closes that gap.)

### 2.3 Response ladder: flag, then fail — never escalate

**On phase breach (first time per task per phase-instance):**
- `AgentIncident` on the task's agent — new `AgentIncidentKind.DelegationStall` — message naming
  the phase, the wait, and the deadline: *"No first token for 6 min (deadline 5): last entry
  ToolResult seq 41 at 19:42:10Z"*. Incidents already surface on the agent UI; CARD-0003's
  "visible, not only in logs" requirement rides that existing rail.
- `AgentTaskEvent` (new type `AgentTaskEventType.Stalled`) so the task drawer timeline shows it.
- `AgentTaskChanged` SSE so boards refresh.
- Log at Warning.
- Deduped via the event row: don't re-raise while the latest-entry key is unchanged; a new
  transcript entry resets the phase clock and re-arms the flag.

**On hard deadline (`RolePolicyEntry.TimeoutMinutes`, measured from `DispatchedAt`):**
- Fail via the same machinery as `FailNeverStartedAsync` — extract its tail (fail + kill session +
  `RemoveEphemeralAgentAsync` + parent-session completion note + SSE) into a shared private
  helper `FailAndTearDownAsync(task, reason)`; both callers use it.
- Reason carries the phase evidence: *"Hard deadline: 60 min after dispatch, stalled in
  ToolRunning for 34 min (last entry ToolCall 'Bash' at ...)"*.
- `TimeoutMinutes` becomes nullable-with-default like its siblings? No — keep `int`, default 60,
  but honour `<= 0` as "no hard deadline" for roles that legitimately run very long. Document on
  the property that it is now read (delete the current dead-config situation either way — the
  card is right that an unread declared knob is worse than none).

Escalation is untouched: where role policy configures it (Debug), `AutoEscalateStalledAsync` will
usually fire before the hard deadline; ordering in `TickAsync` stays escalate → never-started →
NEW phase watchdog → retire-idle, so an escalatable stall escalates rather than flags first.

### 2.4 Files touched

| File | Change |
|---|---|
| `server/Application/Services/AgentTaskDispatcher.cs` | `WatchPhaseDeadlinesAsync` + `FailAndTearDownAsync` extraction; call from `TickAsync` |
| NEW `server/Application/Services/DelegationPhase.cs` (or nested) | `enum DelegationPhase` + pure `Classify(entries…)` |
| `server/Application/Settings/DelegationSettings.cs` | 4 new knobs (§2.2); doc fix on `TimeoutMinutes` |
| `server/Domain/Enums/AgentIncidentKind.cs` | `DelegationStall` |
| `server/Domain/Enums/AgentTaskEnums.cs` | `AgentTaskEventType.Stalled` |
| `server/Application/Services/CardService.cs` | §3 validation |
| `client/src/features/delegations/*` | render `Stalled` event in drawer timeline; optional amber badge on task card |
| `tests/Antiphon.Tests` | §4 |

No migration: incident/event kinds are ints/strings in existing columns; no schema change.

## 3. Incidental (same card): card create must 400, not 500, on a long description

`ValidateCreateRequest` gains: `Description` length > 4000 → `ValidationException` naming the
limit and the actual length ("Description is 5,213 characters; the limit is 4,000"). Same check in
`ValidateSpawnRequest` if `SpawnCardRequest` carries a description. One integration test. (The
deeper fix — `ExceptionMiddleware` mapping `DbUpdateException` column-truncation to a 400 with
the Npgsql detail — is out of scope here; the validation makes it unreachable for this field.)

## 4. Testing

All in `Antiphon.Tests`, following the shared-Postgres rules (scope every assertion to rows the
test created; a test driving a global dispatcher tick needs `[NotInParallel]` with NO group key —
the `AgentSupervisionTests` lesson).

1. **`DelegationPhaseTests`** (pure, no DB): one case per row of the §2.2 table; walk-back over
   TurnTitle/CompactBoundary/local-command records; interrupt marker → Idle; entries before
   `DispatchedAt` excluded (warm-reuse case); timestamp-over-sequence ordering (backfill shape).
2. **`AgentTaskPhaseWatchdogTests`** (integration, fake `TimeProvider`):
   - AwaitingFirstToken past knob → incident + Stalled event + SSE, task still Dispatched; second
     tick with no new entry does NOT duplicate; new entry re-arms.
   - ToolRunning under 30 min at a time that would breach first-token → no flag (phases are
     independent).
   - Idle-unsettled with a Pending Delegation queue row → no flag (Delivery, owed a delivery).
   - Hard deadline → task Failed, session killed, ephemeral agent removed, parent note enqueued —
     mirror the existing `FailNeverStartedAsync` assertions against the extracted helper.
   - `TimeoutMinutes = 0` → never hard-fails.
3. **Card validation**: POST 4001-char description → 400 naming the field; 4000 → 201.

## 5. Out of scope (stated so the card doesn't re-grow)

- CARD-0019 (comments on cards spawn agents) — separate card.
- Remediation beyond fail (re-typing the prompt, pressing Enter on a stuck composer) — first
  make the failure loud; recovery heuristics are a follow-up card with live evidence.
- `ExceptionMiddleware` DbUpdateException → 400 mapping (noted in §3).
- Client-side "working/stalled" derivation — the server is the single writer of stall state.
