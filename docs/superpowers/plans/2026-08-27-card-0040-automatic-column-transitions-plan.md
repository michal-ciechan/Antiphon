# CARD-0040 — Cards move themselves: Backlog → In Progress → Review from signals that already exist

**Date:** 2026-08-27
**Status:** planned (design only — nothing here is implemented)
**Card:** CARD-0040 (`9dc7c8cd-5db7-4e30-857b-b4dd626e054f`), board Antiphon
**Scope:** the card's own "unconditional part" only. The Review → Done conditional arm depends on
CARD-0039 (urgency/importance axes) and on a confidence signal; **CARD-0039 is still Backlog as of
2026-08-27** (`card.ps1 get CARD-0039` → `Backlog`), so that arm is a non-goal here (§4).
**Builds on:** CARD-0019 (revision history, `CardRevisionLog.AppendMove`), CARD-0051 (`MoveCardRequest.Spawn`
opt-in), CARD-0087 (`AutoDispatchHeldAt`), CARD-0122/0123 (`CardNeedsDecision` attention rows),
CARD-0021/0029/0035/0085 (task-status honesty — all four **Done**).
**Model followed:** `docs/superpowers/plans/2026-08-27-card-0123-decision-surfacing-plan.md` — what
exists verified against the code and the live database first, then the design, then cost, non-goals,
slices, operator decisions.

## Verdict, in one screen

The card's thesis is right and the fix is smaller than the card assumes, because **half of it already
ships** — for the wrong population.

| Finding | Evidence | Consequence |
|---|---|---|
| The **card-spawn path already moves cards both ways.** `SpawnAsync` moves Backlog → active on launch; a `RunAttempt` reaching `Succeeded` moves the card to Review through `CardLifecycleTransitions.TryMoveToReview`, from both the launch queue and the orchestrator's reconcile pass. | §1.1; 6 live `Move` rows read `system` / "The card's latest run attempt succeeded." | Nothing to build for card-owned sessions. The model to copy is already in the repo. |
| **Delegated tasks have no card link at all.** `AgentTask` has no `CardId`; the only tie is prose. Yet **397 of 625 task titles begin with `CARD-nnnn`**, and every one of the card's 2026-08-19 examples (CARD-0069/0082/0083) was delegate-driven. All 7 cards in In Progress today have **zero** card sessions. | §1.2, §1.7 | The missing edge is `AgentTask.CardId`. Once it exists, "work started" and "work settled" are durable rows, not inferences. |
| The move mechanism, audit trail, spawn opt-in and tick hold all exist and compose. | §1.3 | One new internal move primitive shaped like `ReopenAsync` (structurally cannot spawn); no second audit trail. |
| The trustworthy signals are **task dispatch** (a session row and brief exist) and **task settlement** (`SettleAsync` → `Succeeded`/`Blocked`, `RecoverFromBindRefusalAsync` → `Succeeded`). The 9-hour `Dispatched` lie was the *absence* of a settle, which a settle-driven rule cannot get wrong in the harmful direction. | §1.4 | Backlog → In Progress on dispatch; In Progress → Review when the **last open** bound task settles `Succeeded`. Failed/Canceled move nothing. |
| A **sweep** beats events here, but only if it is **edge-triggered on evidence newer than the card's last move**. A level-triggered sweep would fight a human who moved a card back. | §2.2–2.3 | `CardWorkTransitionService`, 60 s, idempotent, "the latest fact wins and a human move is a fact". First live sweep would move **4** cards (§1.7), all of them cards the board is currently wrong about. |
| **Stale** = In Progress ≥ 7 days with no open bound task and no live card session. Computable at read time from rows that exist. | §1.5, §2.5 | `AttentionKind.CardStalled` (Warning) in the existing feed; no storage, no alert sink, no column. |

---

## 1. What exists today (verified against the code and the live database, 2026-08-27)

### 1.1 The card-spawn path already automates both transitions — for card-owned sessions only

- **Backlog → active on spawn.** `CardService.SpawnAsync` (`server/Application/Services/CardService.cs:565-590`):
  a card not yet in an active column is moved there through `ApplyColumnMove(card, activeColumn,
  enforceStateMachine: false, reason: "Moved into an active column to spawn an agent session.",
  movedBy: SystemActor)` before the session is created. `AgentControlService.StartAsync` (`:123-137`)
  reaches this for a queue-head card.
- **Active → Review on success.** `CardLifecycleTransitions.TryMoveToReview`
  (`server/Application/Services/CardLifecycleTransitions.cs:16-52`) writes
  `CardRevisionLog.AppendMove(... reason: "The card's latest run attempt succeeded.", movedBy:
  CardService.SystemActor ...)` and lands the card in the first `CardStatus.Review` column by
  `ColumnOrder`. Called from `AgentSessionLaunchQueue.MoveCardToReviewAsync` (`:255-273`, when the
  attempt's `Phase == RunPhase.Succeeded`, `:183`) and from `OrchestratorService.ReconcileAsync`
  (`:423-426`, `card.BoardColumn.IsActive && LatestAttemptSucceeded(card)`), the latter being exactly
  the "repair cards that completed while nobody was looking" sweep this card asks for.
- **Live count:** `CardRevisions` holds **6** `Move` rows with `EditedBy = 'system'`, all with that
  reason. **58 of 596** `AgentSessions` rows carry a `CardId`. This path is real and rarely used.
- `docs/agent-card-lifecycle.md` documents this path and nothing else — it is the living reference
  to update (§2.6).

### 1.2 Delegated tasks have no card link — and delegation is how work happens

- `AgentTask` (`server/Domain/Entities/AgentTask.cs`) has **no** `CardId`. The entity comment says
  "A task MAY reference a card, but doesn't need one" (`:8`) and the worktree comment (`:100`) notes
  the card-scoped `Worktree` entity "requires a card, which a task doesn't have". `CreateAgentTaskRequest`
  (`server/Application/Dtos/AgentTaskDtos.cs:10-50`) has no card field; `scripts/delegate.ps1` has no
  `-Card` parameter. The only existing `CARD-\d+` regex on tasks is `DelegateBindRefusalRecovery.CardId`
  (`:28`), used to grep commits.
- **The convention exists anyway, in prose.** Live `AgentTasks` (627 rows): **479 titles name a
  card, 397 begin with `CARD-nnnn`**, 610 name one in title or goal; 25 titles name more than one
  (all of the shape "Build CARD-0132 S3a from the plan at …/card-0132-…" or a multi-card plan). The
  100 `Role = Check` tasks are titled "check #n on task …" and name no card. `BuildTitle`
  (`AgentTaskService.cs:1011-1020`) takes the explicit title or the goal's first line, so the
  orchestrator's habit of leading a brief with the identifier is what produced this.
- **Every failure the card cites was this shape.** CARD-0069 (a Plan task, `Succeeded` 2026-08-19),
  CARD-0082 (S1–S4 Code tasks) and CARD-0083 (S1–S3, S5) all ran as delegated tasks whose titles
  begin with the identifier, while the cards sat in Backlog. Today's In Progress column (§1.7) is
  seven cards with zero card sessions — every one was moved by hand because its work was a task.
- **Scope for resolving an identifier.** `Cards` is unique on `(BoardId, Identifier)`
  (`IX_Cards_BoardId_Identifier`), not globally: the `Antiphon` board (219 cards) and `Gym Stat`
  (11) both have CARD-0001…0011. Available scope, in order of strength: a task's parent task; the
  calling session's `AgentSession.CardId` (rare); the calling standing agent's `Agent.BoardId` —
  live, `Antiphon-Orchestrator`/`Antiphon-Fable`/`Antiphon-Opus` each have a `BoardId`, though only
  the `Antiphon` agent's is the card-bearing board; the task's `ProjectId` — **set on 1 of 627
  rows**, so useless for history and unreliable for new rows (`DeriveCallerProjectAsync`,
  `AgentTaskService.cs:352-374`, returns null for a task-authenticated caller); and `RepoPath` /
  `WorkingDirectory` (524 rows say `C:\src\Antiphon`) matched against `Project.LocalRepositoryPath`,
  which needs separator- and case-insensitive prefix matching (`C:/src/Antiphon` vs
  `C:\src\Antiphon`, and a duplicate `antiphon` project at `C:\src\antiphon` whose six boards hold no
  cards). §2.1 turns this into a rule that binds only when the identifier is unique inside the
  narrowest scope that resolves.

### 1.3 The move mechanism: five facts the design builds on, verified

1. **`ApplyColumnMove` is private and is the only correct writer** (`CardService.cs:812-861`): state
   machine check (`enforceStateMachine`, default true — every live state reaches every other,
   `CardStateMachine.cs`), `CardRevisionLog.AppendMove` (skippable only for reopen), `StartedAt ??=`
   on an active landing, `CompletedAt`/`TerminalReason` set or cleared, new `ConcurrencyToken`.
   Callers own `SaveChanges` and the `CardChanged` publish.
2. **Spawn knowledge lives in `MoveAsync` and nowhere else** (`:263-322`, CARD-0051). `ReopenAsync`
   (`:447-483`) calls `ApplyColumnMove` directly and is therefore "structurally incapable of
   spawning" — its own words. That is the shape for an automated move.
3. **A non-spawning landing in an active column must set the hold**, or the orchestrator tick picks
   the card up and spawns a card session on top of the delegate. `MoveAsync` does it (`:298-301`:
   `card.OwnerSessionId is null && !request.Spawn` → `AutoDispatchHeldAt = UtcNow()`); so does
   `ReopenAsync` (`:477-478`). `LoadEligibleCandidatesAsync` (`OrchestratorService.cs:506-553`)
   excludes held cards (`:520`), owned cards, cards with a live session, archived cards.
   `AgentControlService.IsSpawnable` (`:489-495`) separately refuses Review/NeedsDecision/terminal
   and archived — a queue-head card in In Progress is spawnable, which is unchanged by this design
   (a card is only on an agent's queue if someone put it there).
4. **A Review or terminal landing must dequeue** — `CardLifecycleTransitions.DequeueFinishedCardAsync`
   (`:60-113`), the CARD-0001 respawn-loop fix; "every status-transition path must call this".
5. **The audit row already carries what the card wants.** `CardRevision` (`server/Domain/Entities/CardRevision.cs`)
   has `FromColumnId`/`ToColumnId`, `FromStatus`/`ToStatus`, `Reason`, `EditedBy`, `CreatedAt`, one
   monotonic `RevisionNumber` per card. `EditedBy` precedent for automation: `external-tracker`
   (22 rows) and `system` (6). **234 of 262** `Move` rows have a blank `EditedBy` — that is the
   human/orchestrator via `card.ps1 move` without `-By`, which is what makes a named automation
   actor distinguishable at all.

### 1.4 The signals, and which ones are honest today

| Signal | Written by | Honest for | Not honest for |
|---|---|---|---|
| `AgentTask.Status = Dispatched`, `DispatchedAt`, `AgentSessionId` | `AgentTaskDispatcher` (`:1596-1622`): the `AgentSessions` row is created and the task claimed in the same unit of work, before the brief is typed | **"work started"** — a session exists, a brief is about to be delivered, cost is being incurred. A delivery failure fails the task (`FailAsync`, `:2205-2220`); a bind refusal with landed work recovers it to `Succeeded` (CARD-0085) | "still running" — the 2026-08-11 nine-hour `Dispatched` was a task that never settled. Irrelevant here: no rule below reads `Dispatched` as "not finished" |
| `AgentTask.Status = Succeeded` / `Blocked`, `CompletedAt` | `AgentTaskReplyService.SettleAsync` (`:383-513`): the turn-ending report correlated by task marker; `RecoverFromBindRefusalAsync` (`:521-`) on positive working-directory evidence | **"work reported"** — Review means a human has not looked yet, and a `Succeeded` with `FinalMessageMissing` or abandoned subagents is still loudly `Succeeded` (CARD-0046), i.e. exactly what Review is for | "work is correct" — that is the confidence signal the card defers, and Review is where a human checks it |
| `AgentTask.Status = Failed` / `Canceled` | `FailAsync`, `FailUnreportedTurnAsync` (`:604-616`), `FailFromStubErrorAsync` (`:678-690`), cancel | "nobody is on this any more" — an input to **staleness** (§2.5) | a column: a failed attempt does not send a card back to Backlog (the existing card-spawn rule: "Failed launches release the claim and leave the card in its current workflow column") |
| `AgentSession.Status`, `IsWorkingAsync` | runner events, transcript | card-spawn liveness (already handled by `OrchestratorService.ReconcileAsync` and `SessionReconciliationService`) | **not used by this design.** The AGENTS.md "strand" gotchas (CARD-0041/0055/0056, restart strands, interrupt markers) are all about working/idle read from the transcript; no rule here reads working/idle |

The dishonesty cards the card names are closed: CARD-0021 (task outlives dead session — the
dead-session watchdog fails it), CARD-0029 (warm-delegate swallow), CARD-0035 (the attention view),
CARD-0085 (false `Failed` recovered to `Succeeded`) are all **Done**. What remains true is that a
task can sit `Dispatched` while its work is real and landed — and the rule "Review when the last
open task settles" simply waits in that case, which is the honest reading (the board says In
Progress; the attention feed's `PastExpectedIdle`/`DeadSession` rows say why).

### 1.5 The history: what a staleness rule can and cannot read

- `CardRevisions` live: 197 `ContentEdit` (from 2026-08-14), 262 `Move` (from 2026-08-16), 1 `Reopen`,
  1 `Unarchive`. **59 of 229** unarchived cards have `RevisionCount = 0`.
- "Entered In Progress at" for a card with history = the latest `Move`/`Reopen` row with
  `ToStatus = InProgress`; without history = `Card.StartedAt` (set `??=` on the first active landing,
  so it is the *first* entry, not the latest — acceptable for a card that has never been moved since
  the history began) — falling back to `UpdatedAt`.
- "Human's last word" (the edge-trigger in §2.2) = the latest `Move`/`Reopen` row's `CreatedAt`;
  for a no-history card, `UpdatedAt` (which every move bumps, and which content edits also bump —
  conservative in the safe direction: a card someone touched after the evidence keeps its column).

### 1.6 The attention feed is the surface for "stale"

`AttentionService.GetAsync` (`server/Application/Services/AttentionService.cs:114-172`) composes
builders and sorts severity-desc then oldest-first. `BuildCardNeedsDecisionAsync` (`:233-261`) is the
model: a read-time query over `CardRevisions` + `Cards`, no storage, `Actions = [OpenCard]`,
`CardId`/`BoardId` on the row. `AttentionKind` runs 0–13 (`AttentionDtos.cs:13-99`;
`CardNeedsDecision = 13`); the client mirrors it in `client/src/api/attention.ts:41-55` and
`client/src/features/attention/attentionVisuals.ts:60-119`, whose totality is pinned by
`attentionVisuals.test.ts`. `useSignalRInvalidation.ts:69` invalidates boards on `CardChanged`.

### 1.7 The live board as it stands, and what a first sweep would do

Cards in In Progress today: CARD-0005, 0010, 0033, 0040, 0063, 0133, 0208 — **all seven with zero
`AgentSessions` rows** (every one moved by hand for delegated work). Applying §2.2's rule to the
live rows (latest non-Check task whose title leads with the identifier, vs the card's last
Move/Reopen row or `UpdatedAt`) over the Antiphon board's Backlog/In Progress/Review cards:

| Card | Column | Latest task | Evidence newer than last word? | Sweep would |
|---|---|---|---|---|
| CARD-0033 | In Progress | Succeeded 08-26 | yes | → Review |
| CARD-0099, 0110, 0124 | Backlog | Succeeded 08-20/21 | yes | → Review (the CARD-0069 shape) |
| CARD-0040, 0063, 0133 | In Progress | Dispatched 08-27 | yes | nothing (already there) |
| CARD-0208 | In Progress | Failed 08-26 | yes | nothing (Failed moves nothing; stale in 7 days) |
| CARD-0073, 0091, 0114 | Backlog | Succeeded 08-19/21 | **no** — moved/touched after | nothing |

Four moves, each into a column the evidence supports, each with a reason naming the task. CARD-0091
is the demonstration of the last-word rule: its task settled 08-19, the card was touched 08-27, the
sweep stays silent.

### 1.8 What does not exist (the build list, before design)

1. `AgentTask.CardId` and any way to set or derive it. 2. A move primitive automation can call
without a concurrency token and without a spawn path. 3. Anything that reads task dispatch/settle
and moves a card. 4. A "stale card" row anywhere. 5. `docs/agent-card-lifecycle.md` coverage of
delegated work.

---

## 2. Design

### 2.1 `AgentTask.CardId` — the missing edge

**Schema.** `AgentTask.CardId Guid?` + `Card? Card` navigation, FK `ON DELETE SET NULL` (cards are
archived, never deleted, but the FK must not make a task un-deletable by `DataRetentionService`).
Index on `CardId`. Migration `AddCardIdToAgentTasks` with a **backfill** (below).

**Setting it, in precedence order** (`AgentTaskService.CreateAsync`, next to `BuildTitle`, `:278`):

1. **Explicit.** New `CreateAgentTaskRequest.Card` (string: a card guid, or an identifier in any
   of the shapes `card.ps1` accepts — reuse `CardService.TryCanonicalIdentifier`, `:232-249`, made
   internal). `delegate.ps1 -Card CARD-0040`. An identifier that resolves to nothing in scope is a
   **422** — an explicit binding that silently fails is worse than none.
2. **Inherited.** A child task (`caller.Task is not null`) inherits its parent's `CardId`; a
   follow-up (`FollowUpOnTask`) inherits the earlier task's; an auto-spawned Merge task
   (`:791`) inherits the conflicted task's. Inheritance is overridden by rule 3 only when the title
   names a *different* card (a "CARD-0083 S2" child of a "CARD-0083 plan" orchestrator keeps 0083;
   a child titled "CARD-0084 …" under an 0083 orchestrator gets 0084 — that is the orchestrator
   working two cards, and the title is the stronger claim).
3. **Title.** The **first** `CARD-nnnn` in the title (case-insensitive). When the title names
   several distinct identifiers the first binds and a `Warning` `AgentTaskEvent` names the others
   ("Title names CARD-0178, CARD-0179, CARD-0180 as well; bound to CARD-0177 — pass -Card to
   choose"). One card per task is a deliberate v1 limit (25 of 627 titles; §4).
4. **Never** for `Role == Check` tasks (they are about a task, not a card) — they are skipped before
   any of the above.

**Resolving an identifier to a card** — the narrowest scope that resolves wins, and inside a scope
the identifier must be **unique** or nothing binds:

- scope A: the board of the inherited card (rule 2), or of the calling session's `CardId`, or the
  calling standing agent's `Agent.BoardId` (via `PersistentSessionId`, the join
  `DeriveCallerProjectAsync` already makes);
- scope B: boards of projects whose `LocalRepositoryPath`, normalised (`/`↔`\`, case-insensitive,
  trailing separator stripped), is a prefix of `RepoPath ?? WorkingDirectory`;
- scope C: every board.

Ambiguous at every level (today: CARD-0001…0011 with no scope) → unbound, `Warning` event
"Identifier CARD-0005 exists on 2 boards (Antiphon, Gym Stat); pass -Card with the card's guid".
Unbound is never an error for rules 2–4; the task runs, the card just does not move.

**Backfill** (in the migration, plain SQL, best effort): for every task with no `CardId`, `Role <> 11`
(Check), whose title leads with an identifier, bind when scope B → C resolves to exactly one card.
Measured: 399 leading-title tasks, all 399 have `WorkingDirectory`/`RepoPath` under the Antiphon
repo, 0 name an identifier that does not exist. Expected result: ~399 bound, the CARD-0001…0011
subset possibly unbound where scope B admits both projects — acceptable, and the migration logs the
count. The backfill is what lets §2.2 act on the four cards in §1.7 and lets §2.5 read history.

**Exposure.** `AgentTaskSummaryDto` gains `CardId` and `CardIdentifier` (denormalised at read time
in `ToSummary`, `:459/491/512`); the task board chip can link to the card later (not in scope).
`AgentTaskCreatedDto` gains `CardIdentifier` so `delegate.ps1` prints "bound to CARD-0040" (or the
warning) on creation — the orchestrator sees a mis-bind at dispatch, not a week later on the board.

### 2.2 The transition rule

Applied per card, per sweep, over the card's bound tasks (`Role <> Check`), skipping cards that are
archived, in `NeedsDecision`/`Done`/`Canceled`, or **owned by a card session** (`OwnerSessionId != null`
— the RunAttempt path in §1.1 owns those; two writers on one card is how flapping starts).

| Evidence (the newest task event across the card's tasks) | From | To | Reason text (persisted on the `Move` row) |
|---|---|---|---|
| a bound task is `Dispatched`/`Working`/`Blocked` (open) | Backlog, Review | **In Progress** | `Task 242a7647 (Plan, fable) dispatched against this card.` |
| the newest event is a task settling `Succeeded` **and no bound task is open** | Backlog, In Progress | **Review** | `Task 242a7647 (Plan) settled Succeeded; no other task is open against this card.` |
| the newest event is `Failed`/`Canceled` | any | — | nothing (see §1.4; §2.5 catches the abandonment) |

Three properties, each with a test in §6:

- **Straight to the supported state** (card constraint 1): a Backlog card whose only evidence is a
  settle goes Backlog → Review in one move, never via In Progress. `enforceStateMachine` stays
  **true** — the machine permits it, and if a future narrowing forbids it the sweep should fail
  loudly rather than route around.
- **The latest fact wins, and a human move is a fact.** The sweep acts only when the evidence
  event's timestamp (`DispatchedAt` for open, `CompletedAt` for settled) is **newer than the card's
  last Move/Reopen row** (§1.5 fallback: `UpdatedAt`). A human who drags a card from Review back to
  In Progress with no new task is not overruled; the next dispatch is newer and moves it again,
  which is correct. This is also what makes the sweep idempotent: its own `Move` row is newer than
  the evidence it acted on.
- **Several tasks, one card** (card constraint 5): "started" is the *first* open task; "reported" is
  the *last* open task settling. A Blocked task counts as open (the card is still being worked, by
  whoever answers). A second delegate dispatched while the first is Blocked changes nothing. A
  retry after `Failed` is a new dispatch → In Progress again.

`-> Blocked` from the card's original list is **not** mapped to `NeedsDecision` (§4).

### 2.3 Sweep, not events — `CardWorkTransitionService`

**Recommendation: a sweep, alone.** The card's own instinct ("the reconciler shape is more forgiving
and matches `SessionReconciliation`") is right, for three concrete reasons: (1) every input is a
durable row (`AgentTasks`, `CardRevisions`, `Cards`), so a sweep is exact, not approximate — it
is not polling a runner; (2) the backfill in §2.1 and every server outage are handled by the same
code with no replay logic; (3) two writers (settle-site event + sweep) is the shape that produced
CARD-0056's flap counter. Latency is one interval; the board is not a real-time surface.

- `CardWorkTransitionService` (scoped, `server/Application/Services/`), driven by
  `CardWorkTransitionHostedService` (`server/Infrastructure/Orchestration/`), a copy of
  `SessionReconciliationHostedService` (`server/Infrastructure/Agents/SessionReconciliationHostedService.cs`):
  `PeriodicTimer`, one scope per tick, `catch (OperationCanceledException) when (ct.IsCancellationRequested)`.
  Registered beside `SessionReconciliationHostedService` in `Program.cs:461`.
- `CardWorkTransitionSettings { Enabled = true, IntervalSeconds = 60, StaleAfterDays = 7 }`
  bound from `CardTransitions`. Off = the feature does not exist; nothing else changes.
- One query per sweep: cards not archived, `Status ∈ {Backlog, InProgress, Review}`,
  `OwnerSessionId == null`, with at least one bound non-Check task; the tasks; the latest
  Move/Reopen revision per card. Cards with no bound task are never loaded.
- Per decided move: `CardService.ApplyAutomatedMoveAsync` (§2.4) inside the sweep's scope; the
  sweep returns the count of moves, logs one Information line per move naming card, from → to,
  task, and Debug for "evidence older than last word, skipped". A failure on one card is caught,
  logged Warning, and the sweep continues — matching `SessionReconciliationService.ScanAsync`.
- `ScanAsync` is public and takes `CancellationToken` so tests drive it directly, the same way
  `AgentSupervisionTests` drive ticks.

### 2.4 The move primitive: `CardService.ApplyAutomatedMoveAsync`

```csharp
internal async Task<bool> ApplyAutomatedMoveAsync(
    Guid cardId, CardStatus target, string reason, string movedBy, CancellationToken ct)
```

Shape of `ReopenAsync` (`:447-483`), not `MoveAsync`: loads with `LoadCardForUpdateAsync`; refuses
archived and terminal cards and a `NeedsDecision` source (returns false — the sweep already
filtered, this is the second lock); resolves the target column as the first column with
`CardStatus == target` by `ColumnOrder` (the rule `TryMoveToReview` and `SpawnAsync` already use;
no such column → false + Warning); calls `ApplyColumnMove(card, column, enforceStateMachine: true,
reason: reason, movedBy: movedBy)`; then exactly what `MoveAsync` does after it, minus the spawn
branch: `DequeueFinishedCardAsync`, hold/clear (`targetColumn.IsActive && card.OwnerSessionId is null`
→ `AutoDispatchHeldAt = UtcNow()`; not active → `null`), the completion checkpoint is irrelevant
(never terminal), `SaveCardWriteAsync`, publish queue removal and `CardChanged`. No concurrency
token: the sweep is the writer and a concurrent human move loses or wins on the token's
`DbUpdateConcurrencyException` exactly as two humans do (the next sweep re-evaluates).

- `movedBy` = new constant `CardService.TransitionActor = "card-transitions"`, alongside
  `SystemActor` (`:52`) and the tracker's `external-tracker` — a `card.ps1 history` line reads
  `card-transitions · Task 242a7647 (Plan) settled Succeeded; …`, distinguishable from a human's
  blank actor at a glance (card constraint 3).
- Should `TryMoveToReview` fold into this? **No** — it is static, DB-free, and called from two
  paths that own their own persistence (the reason `CardRevisionLog` is static, `:13-20`). Leave it;
  the reason text differs ("latest run attempt succeeded" is the RunAttempt path's truth).

### 2.5 Stale: `AttentionKind.CardStalled = 14`

**Recommendation: yes, surface it — as a Warning attention row, nothing louder.** A card that sits
in In Progress with nobody on it is the exact thing §1.7 shows humans do not notice; it is also
not an incident, so the alert sinks (severity-routed, digest-grouped — the CARD-0171/0123 reasoning)
are the wrong path, and it is a state, so it needs no storage (the CARD-0122 precedent).

- `BuildCardStalledAsync` in `AttentionService`, after `BuildCardNeedsDecisionAsync`: cards with
  `Status == InProgress && ArchivedAt == null`, **no** bound task open, **no** `AgentSessions` row in
  `Created/Starting/Running/Stopping`, `OwnerSessionId == null`, and `enteredInProgressAt`
  (§1.5) older than `StaleAfterDays`. `Severity = Warning`; `Title = "{Identifier} — {Title}"`;
  `Headline = "In Progress for {n} days with nobody on it."`; `Evidence` = the last bound task's
  outcome ("last task 8a1b2c3d Failed on 26 Aug: <FailureReason excerpt>", or "no task has ever been
  bound to this card"); `SinceUtc = enteredInProgressAt`; `Actions = [OpenCard]`; `CardId`/`BoardId`.
- Client: one entry in `attentionVisuals.ts` (`label: 'Stalled card', color: 'warning',
  icon: TbClockPause`, hint "In Progress with no open task and no live session. Move it, or start
  something.") and the union member in `attention.ts`; `groupOf` already puts Warning in `suspect`.
- Not in the away digest (CARD-0036) in this card — a Warning row is not a "needs you now"; add it
  to a digest section only if the operator asks after living with the row (§7).

### 2.6 Docs (S4)

- `docs/agent-card-lifecycle.md`: a "Delegated work" section — the binding rules, the two
  transitions, the last-word rule, the stale row; keep the card-spawn section, note the two paths
  never touch the same card (`OwnerSessionId`).
- `AGENTS.md` gotcha, one bullet: *cards move themselves on task dispatch and settle when the task
  is bound to the card; lead the title with `CARD-nnnn` or pass `-Card`; a manual move after the
  evidence is respected; the actor on the revision is `card-transitions`.*
- `docs/orchestration-loop.md` §7: In Progress and Review are no longer yours to move; Done still is.
- `scripts/delegate.ps1` header and the `antiphon-delegate` skill: `-Card`.

---

## 3. What this costs its neighbours

- **Revision volume.** A five-stage loop (plan → code → verify → deploy → close) on one card now
  writes about 2 Move rows per stage (→ In Progress on dispatch, → Review on settle) — 8–10 rows a
  card instead of 2. That is the history being true; `CardHistory` is lazy and paginated by card.
- **`CardChanged` events.** Same count; `useSignalRInvalidation` refetches the board on each. At the
  observed dispatch rate (a few tasks an hour) this is noise-free.
- **The orchestrator tick** does not change; the hold on every automated active landing keeps it
  from spawning (§1.3 fact 3). `IsSpawnable` is unchanged.
- **The board.** In Progress will hold exactly the cards with an open task or a live session; Review
  will grow — that is the reviewer's queue becoming visible, which the `AgentRail` Review count
  already renders.
- **`DataRetentionService`** deletes settled tasks on its schedule; `CardId` FK is `SET NULL` and
  the evidence the sweep needs is only ever the *newest* task, which is the last to be retained.

## 4. Non-goals

- **Review → Done, in any form** — the card's conditional arm. It depends on CARD-0039 (Backlog) and
  on a confidence signal nobody has defined; nothing here reads priority or scores a report.
- **`-> Blocked` / `NeedsDecision` on a delegate question.** A delegate's question goes to its
  *caller* (the orchestrator, via `ReplyTo`), not to the human, and `BlockedQuestion` already
  surfaces it Critical with a Telegram ping (CARD-0036). Parking the card would double-surface one
  question and require `AnswerAsync` to un-park it. `NeedsDecision` stays a human verb (CARD-0122).
- **Many cards per task.** One `CardId`; the first identifier in the title binds, the rest are named
  in a Warning event. A join table is not worth 4 % of tasks until the orchestrator asks for it.
- **Moving cards owned by a card session** — the RunAttempt path keeps them.
- **Reading session/transcript liveness** (`IsWorkingAsync`, runner status) for any transition.
- **A task-board → card link in the UI**, and the stale row in the away digest.
- **Un-stalling anything automatically.** The stale row is detection only (CARD-0153's rule).

## 5. Acceptance: the CARD-0069 shape, replayed

1. `card.ps1 new` a card X in Backlog. `delegate.ps1 Plan -Title "X: plan" -Goal …` (or `-Card X`).
   Creation prints `bound to CARD-X`. Within 60 s of dispatch: X is **In Progress**, `card.ps1
   history X` shows `card-transitions · Task … dispatched against this card.`, `AutoDispatchHeldAt`
   set, no new `AgentSessions` row with `CardId = X`.
2. The delegate reports. Within 60 s of settle: X is **Review**, history shows the settle reason,
   `AssignedAgentId` null.
3. `card.ps1 move X -To "In Progress" -Reason "needs another pass"`. Next sweep: **no move** (human's
   word is newer). `delegate.ps1 Code -Card X …` → In Progress stays, history gains a dispatch row
   only if the card was elsewhere; settle → Review again.
4. `card.ps1 move X -To "In Progress"` and leave it. After `StaleAfterDays`: `GET /api/attention`
   lists X as `CardStalled` (Warning) with `sinceUtc` = that move. `card.ps1 close X` → row gone.
5. Live, on deploy: the four cards in §1.7 move with reasons naming their tasks; CARD-0091 does not.

## 6. Slices, tiers, tests

| Slice | What | Tier | Tests (all scoped to rows the test created — the shared-Postgres rule) |
|---|---|---|---|
| **S1** `AgentTask.CardId` | Entity, migration + backfill, `CreateAgentTaskRequest.Card`, resolution + inheritance rules, `Warning` events, DTO fields, `delegate.ps1 -Card`, `TryCanonicalIdentifier` made internal | Grok | `AgentTaskCardBindingTests` (integration): explicit guid; explicit identifier in caller-board scope; unknown explicit → 422; leading title binds; several in title → first + Warning event naming the rest; ambiguous across two boards with no scope → unbound + event; child inherits parent; child titled with another card overrides; follow-up inherits; Merge task inherits; `Role=Check` never bound; `ToSummary` exposes `CardId`/`CardIdentifier`. Backfill: assert live count in the slice report, not a test |
| **S2** the sweep | `CardWorkTransitionService` + hosted service + settings; `CardService.ApplyAutomatedMoveAsync`; `TransitionActor` | Opus | `CardWorkTransitionServiceTests` (integration, `[NotInParallel]` no key — it drives a global sweep, the `AgentSupervisionTests` lesson): dispatch on Backlog → In Progress with reason/actor/hold/no session/`CardChanged`; settle with none open → Review + dequeued; settle with a sibling open → stays; Blocked counts as open; Failed/Canceled → no move; Backlog → Review direct on a settle-only card; human move newer than evidence → silent, next dispatch → moves; NeedsDecision/Done/Canceled/archived/`OwnerSessionId`-owned untouched; Check tasks ignored; second sweep writes no row; `Enabled=false` → nothing. `CardCorrectionIntegrationTests`: an automated move's revision reads back through `GET /revisions` with `editedBy = card-transitions` |
| **S3** stale row | `AttentionKind.CardStalled`, builder, client visual/union | Grok | `AttentionServiceTests`: stale card → Warning row, `sinceUtc` = entered-InProgress revision, evidence names the last task; open bound task → absent; live card session → absent; under threshold → absent; no-history card uses `StartedAt`; Review card absent. Client: `attentionVisuals.test.ts` totality picks the kind up |
| **S4** docs | §2.6 | Codex luna | `AGENTS.md` bullet, `agent-card-lifecycle.md`, `orchestration-loop.md`, `delegate.ps1` header |

S1 → S2 → S3 are sequential (S2 needs the column; S3 needs S2's semantics for "open"); S4 last.
Build to `--property:OutputPath=bin-<name>/` while daemons hold `bin/`; the suite for S2 is
`--treenode-filter "/*/Antiphon.Tests.Application/CardWorkTransitionServiceTests/*"` plus
`CardCorrectionIntegrationTests` and `OrchestratorServiceIntegrationTests` (the hold and the tick).

## 7. Decisions that are the operator's — each with a recommendation

1. **Sweep vs event-driven.** Recommend **sweep only, 60 s** (§2.3). Event-driven adds a second
   writer for a 60 s gain on a board nobody watches in real time.
2. **First-sweep behaviour.** The backfilled history will move **four** cards on the first tick
   (§1.7). Recommend **let it** — each is a card the board is currently wrong about, each move is
   reasoned and reversible via `card.ps1 move`, and a "historical evidence floor" setting would be a
   knob whose only job is to keep the board lying a little longer. §1.7 *is* the list; if you want
   to move them by hand first, do that before deploy and the sweep will find nothing newer than
   your moves.
3. **Stale threshold.** Recommend **7 days** (`StaleAfterDays`), the number the card itself uses.
   Shorter turns every weekend into a row.
4. **Should a Failed last task send the card anywhere?** Recommend **no** (§1.4, and CARD-0085's
   lesson that Failed is sometimes wrong); the stale row is the backstop and `RecentFailure` already
   covers the first 24 h.
5. **Actor name.** Recommend `card-transitions` over reusing `system` — the card asks for a
   `movedBy` that names the automation, and `system` already means "the RunAttempt path".
6. **The 25 multi-card titles.** Recommend **first identifier binds + Warning event** over
   "bind none": one card moving is closer to the truth than none, and the event tells the
   orchestrator to pass `-Card` next time.
