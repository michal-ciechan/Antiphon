# CARD-0096 — Batch control: "process N cards, P at a time", counted on start, with an honest in-flight box

**Date:** 2026-09-03 (Plan pass, task f1fdbf81 — design only; no production code changed, no tests run)
**Card:** CARD-0096 "Orchestrator batch control: process N top-priority cards with P running in parallel,
countdown on start not completion, accurate in-flight count"
**Builds on:** [`2026-09-03-card-0098-within-tier-position-plan.md`](2026-09-03-card-0098-within-tier-position-plan.md)
(Decision 1, `CardRanking.OrderKey` — the one order this card's "top N" reads),
[`2026-09-02-card-0092-running-sessions-visibility-plan.md`](2026-09-02-card-0092-running-sessions-visibility-plan.md)
(what "running" means on `/orchestrator`, and why the state endpoint stays a session snapshot),
[`2026-09-02-card-0094-backlog-by-quadrant-plan.md`](2026-09-02-card-0094-backlog-by-quadrant-plan.md)
(the Backlog boxes on the same tab, and the page-mounted-section pattern this card copies),
[`2026-09-02-card-0057-scheduled-prompt-and-card-action-plan.md`](2026-09-02-card-0057-scheduled-prompt-and-card-action-plan.md)
(the durable one-shot action with its own row and fire history; the WhenIdle prompt fire this
card's nudge reuses), and CARD-0040's `CardWorkTransitionService` (evidence sweeps over durable rows).

**Sources (verified this pass, head `610c11d4`, live 17202 on 2026-09-03):** CARD-0096 including
the 2026-09-03 addendum from CARD-0098's plan pass; CARD-0092 (Done), CARD-0094 (Review, S1–S2
landed), CARD-0095 (Backlog, unplanned), CARD-0031 (Done), CARD-0098 (planned `610c11d4`, unbuilt),
CARD-0301 (planned, S1 in flight); `server/Application/Services/{OrchestratorService,
OrchestratorControlState,CardRanking,CardService,CardWorkTransitionService,HomeTaskService,
AgentTaskPipelineStatusService,AgentTaskDispatcher,ScheduleService,SessionMessageQueueService,
AgentTaskCheckService,AgentPresets,InstructionBundles}.cs`, `server/Application/Settings/{OrchestratorSettings,
DelegationSettings}.cs`, `server/Application/Dtos/OrchestratorDtos.cs`, `server/Api/Endpoints/{OrchestratorEndpoints,
ScheduleEndpoints}.cs`, `server/Domain/Entities/{Board,BoardColumn,Card,CardRevision,AgentTask,Agent,
AgentBundleAttachment,Schedule,ScheduleFire}.cs`, `server/Domain/Enums/{CardStatus,CardRevisionKind,
AgentTaskEnums,QueuedMessageOrigin}.cs`, `server/Infrastructure/Orchestration/OrchestratorTickHostedService.cs`,
`server/Migrations/20260903120000_AddAgentTaskLandRequest.cs`, `server/Bundles/orchestrator.md`,
`client/src/features/orchestrator/{OrchestratorPage,OrchestratorPanel,BacklogSection}.tsx` and tests,
`client/src/api/orchestrator.ts`, `client/src/hooks/useSignalRInvalidation.ts`, `scripts/{card,schedule}.ps1`,
`docs/{orchestration-loop,antiphon-api,ops-http}.md`, `tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs`,
`tests/Antiphon.E2E/{OrchestratorE2ETests,ScheduleCliE2ETests,CardCliE2ETests}.cs`; live
`GET /api/boards`, `GET /api/boards/{antiphon}/columns`, `GET /api/orchestrator/state`, `GET /api/agents`.

---

## Verdict up front

**Build a durable, one-shot `CardBatch` row per board — "start N, keep P in flight" — that counts
starts from the evidence rows the system already writes, tells the board's standing orchestrator
agent which card to start next when a slot is free, and caps the card-spawn tick by the same P.
It starts nothing itself.** The batch is an *instruction with state*, not a dispatcher: in this
system the thing that actually processes a card is the delegation loop
(`docs/orchestration-loop.md` §1 — Plan, land, Code, land, close) run by a standing orchestrator
agent (`Antiphon-Orchestrator`, always-on, live today) or by the operator, and neither the tick
nor any server code can write a brief, choose Plan versus Code, or judge a report. What the server
*can* do exactly, and what the operator cannot do from memory across a compaction, is the
bookkeeping the card asks for: which card is next by the one order, how many have started, how
many are in flight right now, when to say "start another", and when to stop.

**The card's premise is corrected in one place.** It maps "the orchestrator" onto
`OrchestratorService.PollTickAsync` and its card-spawn sessions. Live, that engine has processed
no card in weeks: `GET /api/orchestrator/state` shows `runningCardSessions: 0` against two
`Delegation` rows (this Plan and CARD-0301's Code), every card's `ownerSessionId` is null
(CARD-0092's plan re-checked it), and the tick only ever picks from **active** columns — on every
live board that is *In Progress* alone, with `Backlog` inactive. A batch that lived only inside the
tick would count and start nothing for the loop the operator actually runs. The design below is
engine-neutral: a start is "a work session began on a card that left Backlog", which both engines
produce as durable rows, and P is enforced on both (the nudge for delegates, a skip in the tick for
card-spawn).

### The card's five questions, answered

1. **Where does it live?** A **one-shot operator action with its own state**: a `CardBatches` row
   (board, N, P, optional target agent, created-by, status, entries), created through
   `POST /api/orchestrator/batches` (UI form, `scripts/batch.ps1 start`) after a preview, ended by
   its own countdown or an explicit cancel. **Not** an `OrchestratorSettings` value: a standing
   setting cannot express "and then stop", and a batch that outlives the session that asked for it
   must be a row (CARD-0057's argument for schedules, verbatim). No server-side default N or P: the
   preview shows the eligible pool and the ceilings, the form remembers the last values per browser,
   the CLI requires both. §Decision 1.
2. **Pool smaller than N?** The batch is a **budget, not a snapshot**: it starts what is eligible,
   picks up cards that become eligible while it is alive, and **closes itself as `Exhausted` the
   first time nothing is in flight or pending on the board and nothing is eligible** — a batch must
   never lurk and auto-start a card filed next week. Creation refuses an empty pool
   (422 `card_batch_pool_empty`); `N > eligible` creates with `eligibleNow` in the response so the
   operator sees "10 requested · 6 eligible now". §Decision 4.
3. **Restart?** Yes. The batch, its entries, and its last nudge key are rows; the count is a sweep
   over `AgentTasks`, `AgentSessions` and `CardRevisions` (all durable) that is idempotent and runs
   on the orchestrator tick; the nudge is a `SessionQueuedMessage` (durable, delivered on relaunch
   exactly as CARD-0057's `QueuedForRelaunch` prompts are). The first tick after boot recounts from
   the rows and arrives at the same numbers. §Decision 6.
4. **P versus `MaxConcurrentSessions`?** **P is a ceiling beneath every ceiling that already
   exists; it never raises one.** Effective parallelism is `min(P, the engine's own cap)`: for the
   card-spawn tick that is `Board.MaxConcurrentSessions` (1 on every live board) and the column
   cap; for delegates it is `Delegation:MaxConcurrentTasks` (6, fleet-wide, over *tasks*). The
   preview, the create response and the panel name the **binding** constraint out loud
   ("P 2 exceeds the board's MaxConcurrentSessions 1; card-spawn will run 1"), which is the
   answer to "silently throttled by a setting the operator forgot". §Decision 7.
5. **Where is the UI?** A new **`BatchSection`** mounted from `OrchestratorPage` between
   `OrchestratorPanel` and `BacklogSection` on the default *Cards* tab, so the tab reads: running
   sessions → retrying → **the batch and what is in flight** → the backlog it draws from. Three
   boxes — *In flight* (cards, with blocked/pending sublines and the fleet total), *Batch* (state,
   `3 of 10 started`, next card), *Remaining to start* — plus Start / Cancel / Skip next / Nudge.
   `OrchestratorPanel.tsx` and `BacklogSection.tsx` are not edited (their tests, CARD-0095's
   pending totals change, CARD-0098 S5's box drag). §Decision 9.

---

## Ground truth (checked, not guessed)

Line numbers at `610c11d4`.

| Claim in the card | Today | Consequence |
|---|---|---|
| `MaxDispatchesPerTick` caps per tick; `MaxConcurrentSessions` is steady-state | Still exactly so: `OrchestratorService.cs:101`, `:104-116`; `Board.MaxConcurrentSessions` default 1 (`Board.cs:23`), `BoardColumn.MaxConcurrentSessions` nullable | Nothing to reuse for "stop after N"; P sits beneath both |
| Selection is `OrderByDescending(c => c.Priority)` | Gone (CARD-0039). `:786-790`: `Rank`, `DueAtSortKey`, `CreatedAt`; CARD-0098 Decision 1 makes it `CardRanking.OrderKey` = `(rank, position ?? max, dueAt, createdAt)` — planned, not yet built (no `OrderKey`, no `Card.Position` at head) | The batch reads `OrderKey` and nothing else; S1 sequencing note below |
| "The orchestrator" = the tick | The tick dispatches only `BoardColumn.IsActive && !IsTerminal` cards (`:750`); Antiphon columns: Backlog inactive, In Progress active, Review/Done/Needs decision inactive (live). Card-spawn sessions: 0 running, 0 owners. The loop is run by `Antiphon-Orchestrator` (always-on, `persistentSessionId` set, board-bound) via `delegate.ps1` | The batch instructs the agent and caps the tick; it dispatches nothing |
| In-flight counting is card-spawn-only (CARD-0092) | Fixed for the *Running Sessions* table (`GetStateAsync :196-265` unions open non-Check tasks). `CountActiveSessionsByBoardAsync :793-806` (the tick's ceiling) is **still** card-spawn-only — correct for the tick, whose ceiling is about its own sessions | "In flight" here is a **card-level** projection over both engines (§Decision 3), not a reuse of either count |
| A card start is observable | Delegation: `AgentTask.DispatchedAt` on a bound non-Check task (`AgentTask.cs:221`), and the CARD-0040 sweep writes a `Move` revision Backlog→In Progress with `FromStatus`/`ToStatus` (`CardRevision.cs:58-61`) within 60 s. Card-spawn: `TryClaimCardAsync :571-627` creates an `AgentSession` with `CardId`/`StartedAt`; `SpawnAsync :803-838` moves a Backlog card into the active column first (a `Move` revision by `SystemActor`) | Start = Backlog-exit revision **and** work-session evidence, both after the batch began (§Decision 2) |
| Nothing today can tell an agent "start another" | CARD-0057 fires a WhenIdle prompt onto a standing agent's persistent session (`ScheduleService.cs:255-345`); CARD-0047 check notes use `EnqueueAsync(session, body, WhenIdle, origin: Check, conversationKey)` (`AgentTaskCheckService.cs:177-179`) | The nudge is that call with a batch origin and a supersede rule (§Decision 5) |
| Delegate concurrency | `Delegation:MaxConcurrentTasks` 6, counted over non-Check Dispatched/Working tasks (`AgentTaskDispatcher.cs:345-346`, `AgentTaskPipelineStatusService.cs:312-318`); per-role `RecommendedInFlight` is advisory | P is about *cards*; the fleet cap is about *tasks*; the preview reports both |
| Blocked delegates | Excluded from *Running Sessions* (CARD-0092 Decision 1); a card with a Blocked task stays In Progress (`CardWorkTransitionService.cs:35-36`); Home counts Queued/Dispatched/Working/Blocked as open (`HomeTaskService.cs:364-368`) | A blocked card **holds its slot** (starting another would exceed P on unblock); the box says so |
| Same screen, three other changes | CARD-0092 (Done) edited the panel; CARD-0094 (Review) mounts `BacklogSection` from the page and registered `/api/cards` in `OrchestratorPage.test.tsx` `serve()`; CARD-0301 adds `?tab=pipeline` and edits `TABS`; CARD-0095 will widen `totals` in the panel | New section, new endpoint, one line in `OrchestratorPage.tsx`; `-Worktree` if CARD-0301 S3 builds at the same time |
| Pause/Tick semantics | Pause is an in-memory flag (`OrchestratorControlState`) governing card auto-dispatch; the CARD-0092 caption says so | Pause also silences batch nudges (counting continues); stated in the caption |

Live shape on 2026-09-03: 10 boards, all `MaxConcurrentSessions = 1`, no column caps; Backlog
cards on Antiphon (51) and Gym Stat (4); delegate tasks in flight 2 of 6; two always-on
orchestrator agents (`Antiphon-Orchestrator`, `Gym Stat Orchestrator`), one per board.

---

## Decisions

1. **A batch is a row with a lifecycle, per board, one live at a time.**
   `CardBatch { Id, BoardId, TargetCount (N), Parallel (P), AgentId?, CreatedBy, Reason?, CreatedAt,
   Status: Armed | Draining | Completed | Exhausted | Cancelled, EndedAt?, EndedReason?,
   LastNudgeKey?, LastNudgeAt?, NudgeRepeats, ConcurrencyToken }` plus
   `CardBatchEntry { BatchId, CardId, Kind: Started | Skipped, At, Evidence?, By?, Reason? }` with a
   unique index on `(BatchId, CardId)` and a filtered unique index on `BoardId` where
   `Status IN (Armed, Draining)`. Creating while one is live is **409 `card_batch_active`** naming
   it. `Armed` = remaining > 0; `Draining` = remaining 0, at least one `Started` card still in
   flight or pending (the tail the user described); `Completed` = remaining 0 and no `Started`
   card in flight; `Exhausted` = §Decision 4; `Cancelled` = the operator said stop (nothing running
   is touched — "a stall is a detection/decision state, never an automatic kill" holds for a
   cancel too). Statuses are terminal except `Armed ↔ Draining`, which flips both ways (a
   `Draining` batch whose in-flight card fails and re-dispatches stays honest).

2. **A start is evidence, counted once per card, and only for cards that left Backlog.** The sweep
   marks a card `Started` when, for that card on the batch's board, both exist with a timestamp
   after `batch.CreatedAt`: (a) a `CardRevision` of kind `Move` with `FromStatus == Backlog` and
   `ToStatus` not terminal, and (b) work-session evidence — a non-Check `AgentTask` bound to the
   card with `DispatchedAt` set, **or** an `AgentSession` with `CardId == card` and `StartedAt`
   set. `At` is the later of the two. Why both: (b) alone would count a Code stage dispatched on a
   Review card (not from the pool, not "another card"); (a) alone would count a bookkeeping move
   with nothing running. Why not a snapshot of the pool at creation: a card the operator drags to
   the top mid-batch must be what starts next (CARD-0098's whole point), and a card already In
   Progress at creation must occupy a slot without being counted — both fall out of "evidence after
   `CreatedAt`" with no extra rows. Plan then Code on one card is one start; a re-dispatch after a
   failure is not a second start. `Remaining = N − count(Started)`, never below zero.

3. **"In flight" is a card-level projection over both engines, board-scoped, with two honest
   sublines.** For a board: `inFlight` = distinct unarchived cards that either own an
   `AgentSession` in `{Starting, Running, Stopping}` or have a non-Check bound `AgentTask` in
   `{Dispatched, Working, Blocked}`; `blocked` = those whose only open tasks are `Blocked`;
   `pending` = cards not in flight with a `Queued` non-Check bound task (the orchestrator has
   committed to it; nothing has started; it must not be nudged twice). **Slots used =
   `inFlight + pending`; free = `max(0, P − slotsUsed)`.** `fleetInFlight` is the same predicate
   over all boards, for the box's subline. This is neither `CountActiveSessionsByBoardAsync`
   (card-spawn only, by design of the tick's ceiling) nor `runningSessions` (sessions, Blocked
   excluded, CARD-0092 Decision 1): the question here is "how many *cards* are being worked", and a
   Blocked card is being worked by a human. Not counted: `Review` cards with no open task (waiting
   on a human's read — Home *To review* is that surface), `NeedsDecision`, Queued *un*bound tasks.

4. **Next by order, dynamically, from the same key as the tick; exhaustion closes the batch.**
   `Eligible(board, now)` = unarchived cards with `Status == Backlog`, no owner session, no open or
   queued non-Check bound task, not `Skipped` in this batch, ordered by `CardRanking.OrderKey`
   (CARD-0098 Decision 1) **and nothing else** — no re-sort, no dedupe by title, no board mixing.
   `nextByOrder` is the first `max(P, 3)` of that list; the head is what the nudge names. When
   `slotsUsed == 0` and `Eligible` is empty and `Remaining > 0`, the sweep sets `Exhausted` with
   `EndedReason = "N of M started; nothing left in Backlog"` and sends the terminal note. While
   anything is in flight or pending, the batch waits (a delegate may file follow-ups; the operator
   may drag a card in). **Sequencing note:** `OrderKey` is CARD-0098 S1's deliverable and is not at
   head. Land CARD-0098 S1 first (2–3 h) or, if this card builds first, S1 here introduces
   `CardRanking.OrderKey(importance, urgency, dueAt, position, createdAt, now)` with the CARD-0098
   signature and `position` always `null`, and switches `LoadEligibleCandidatesAsync :786-790` to
   it, so CARD-0098 S1 only fills the column in. Either way there is one order function.

5. **The nudge: one WhenIdle note per slot change to the batch's agent, superseded not stacked,
   repeated on a slow ramp, silent when paused or agentless.** After each sweep, if
   `Status == Armed`, the orchestrator is not paused and is enabled, `free > 0`, and
   `Eligible` is non-empty, compute `key = $"{started}:{slotsUsed}:{headCardId}"`. If
   `key != LastNudgeKey` (or `LastNudgeAt` is older than `Orchestrator:BatchNudgeRepeatMinutes`,
   default 30, with `NudgeRepeats < Orchestrator:BatchNudgeMaxRepeats`, default 10): cancel any
   still-pending nudge for this batch, then
   `EnqueueAsync(agent.PersistentSessionId, body, WhenIdle, origin: QueuedMessageOrigin.Batch,
   conversationKey: $"batch:{id}", noteHeader: …)`, record `LastNudgeKey/At`. Target resolution as
   `ScheduleService.cs:255-290`: no persistent session → skip with detail; session not live and
   agent not always-on → skip; not live and always-on → queue (delivered on relaunch). A batch with
   `AgentId == null` (the operator is the orchestrator, reading the panel) never enqueues. Terminal
   notes (`done` / `exhausted` / `cancelled`) go once, same channel. `QueuedMessageOrigin.Batch` is
   a new enum value; the executor greps every `QueuedMessageOrigin.Check`/`Scheduled` site (the
   stale-machine-message discard in the queue sweep, the attention `ParkedMessage` rule) and treats
   `Batch` the same way. Body, ASCII, well under `BriefInlineMaxBytes` (900):

   ```
   [batch 5f2c1a3b #4] 3 of 10 started; in flight 2 (1 blocked), pending 0; 1 slot free of 2
   next by order: CARD-0331 "Durable land request row + sweep" (rank 10)
   then: CARD-0301, CARD-0096
   Start it: pwsh -File scripts/delegate.ps1 -Card CARD-0331 ... (Plan if it has no solid plan, else Code).
   Pass it over: pwsh -File scripts/batch.ps1 skip CARD-0331 -Reason "...". Status: batch.ps1 status.
   ```

   The agent is not obliged to take the head card: any start on the board counts (Decision 2), so an
   orchestrator that knows better and starts the second card is still inside the budget. The
   recommendation is the server's; the judgement stays the agent's, as §0 of the loop doc requires.

6. **Durability and the sweep.** `CardBatchService.SweepAsync(now, nudge, ct)` runs at the top of
   `PollTickAsync` (`:74`, before the pause early-return, with `nudge = !paused && enabled`) so
   counting never stops and nudging respects Pause. It is idempotent over durable rows: entries are
   only ever added, statuses only move forward except `Armed ↔ Draining`, and a nudge is keyed. A
   crash between "entry written" and "nudge sent" costs one repeat at most; a crash between
   evidence and sweep costs one tick. No in-memory state anywhere (`OrchestratorControlState` is
   not extended). Manual `POST /api/orchestrator/tick` runs the sweep too.

7. **P beneath every ceiling; the tick honours the batch.** In `PollTickAsync`, after the board
   and column checks (`:104-116`), a candidate whose board has a live batch is skipped when
   `Remaining == 0` (Draining) or when `slotsUsed(board) + dispatchedThisTickOnBoard >= P`;
   `OrchestratorTickResult` gains `SkippedBatch`. The tick's own claim is evidence (Decision 2b) and
   is counted by the next sweep. The board and column caps are untouched: a batch cannot raise
   `MaxConcurrentSessions`, and the preview says when P is above it. For delegates the enforcement
   is the nudge (no nudge when `free == 0`); the dispatcher's fleet cap (`MaxConcurrentTasks`) is
   not consulted by the batch and not changed — the preview reports `fleetTaskSlotsFree` and
   warns when P exceeds it. `bindingCeiling` in the view names whichever is smallest:
   `parallel` | `boardMaxConcurrentSessions` | `columnMaxConcurrentSessions` | `fleetTaskSlots`.

8. **Preview before create; create is the spend decision.** `POST /api/orchestrator/batches/preview`
   returns `{ eligibleNow, willStart: CardRef[N or fewer, in order], inFlightNow, pendingNow,
   slotsFree, bindingCeiling, agent: { resolved: AgentRef | null, reason }, warnings[] }`;
   `POST /api/orchestrator/batches` takes the same body and returns 201 with the batch and the same
   preview embedded. Agent resolution: `agentId` if given (must be board-bound, not a pool delegate
   — 422 otherwise, the schedule rule); else the board's single always-on agent carrying the
   `orchestrator` bundle (`AgentBundleAttachments.BundleKey == InstructionBundles.Orchestrator`);
   else null with reason `"no orchestrator agent on this board; nudges off"`. Creating a batch is
   the operator accepting the spend it will cause through the agent — no separate `acceptSpend`
   flag, because unlike a scheduled `Spawn` nothing here launches without an agent's own
   `delegate.ps1` call or the tick's existing card-spawn rules; the preview is shown first on both
   the form and the CLI. `N` 1..200, `P` 1..20 (422 outside).

9. **UI: a page-mounted `BatchSection`, three boxes, four verbs; nothing else on the tab is
   edited.** `OrchestratorPage.tsx:84-87` becomes `<OrchestratorPanel /><BatchSection /><BacklogSection />`.
   Section header: `Title order={4}` "Batch", a board `Select` (boards with Backlog cards or a live
   batch; default the one with a live batch, else the most Backlog cards; remembered in
   `localStorage`), a refresh icon. `SimpleGrid cols={{ base: 1, sm: 3 }}` of the panel's
   `SummaryMetric` shape (copied, not imported — the panel stays byte-identical): **In flight**
   `2` / `1 blocked · 1 pending · fleet 4`; **Batch** `Armed · 3 of 10 started` / `next CARD-0331 ·
   P 2 (bound by board cap 1)`; **Remaining to start** `7` / `started 3 · skipped 1`. Below: the
   entries table (card, kind, at, current status, in flight?) capped at N rows, and the buttons:
   *Start batch* (a `Modal`: N, P, agent `Select` defaulting to the resolved one with "none" as an
   option, the live preview list "Will start: …" and warnings, Start), *Cancel* (reason), *Skip
   next* (reason), *Nudge now*. No batch: the boxes still render (in-flight is a board fact) and
   *Batch* reads "None running". Empty pool: Start is disabled with the reason. Fetch:
   `GET /api/orchestrator/batches?boardId=` with `refetchInterval: 5_000` (the panel's cadence) and
   invalidation on `CardBatchChanged`, `CardChanged`, `AgentTaskChanged`, `OrchestratorTick`.
   Loading/error live inside the section (CARD-0094 Decision 2's reason: the section's requests must
   not blank *Running Sessions*, and `OrchestratorPanel.test.tsx` registers only
   `/api/orchestrator/state`).

10. **CLI, docs, bundle.** `scripts/batch.ps1` (ASCII): `status [-Board b] [-Json]`,
    `preview -Board b -Count n -Parallel p [-Agent a | -NoAgent]`, `start -Board b -Count n
    -Parallel p [-Agent a | -NoAgent] [-Reason r | -ReasonFile p]` (prints the preview, then
    starts), `cancel [-Board b | -Id id] -Reason r | -ReasonFile p`, `skip CARD-nnnn [-Board b]
    -Reason …`, `nudge [-Board b]`. `-Board` resolves like `card.ps1` (name or guid; the checkout
    decides when omitted). `docs/antiphon-api.md` route rows + a "Batches (CARD-0096)" paragraph;
    `docs/orchestration-loop.md` §1 gains "Batches: what a `[batch …]` note asks of you" (the agent's
    contract: start the named card or another eligible one, skip with a reason, never `-Spawn` a
    card by hand to make the count move); `server/Bundles/orchestrator.md` gains the same two
    sentences (it is the canonical copy the agent actually reads); `docs/ops-http.md` "The jobs you
    have" gets one line pointing at `GET /api/orchestrator/batches`. AGENTS.md unchanged: the
    orchestration-loop doc is the owner and the safety rule ("tracker writes are explicit actions")
    is not touched — a batch writes no tracker row.

---

## Data model and wire shape

```
CardBatch
  Id                 uuid PK
  BoardId            uuid FK Boards
  TargetCount        int          -- N, 1..200
  Parallel           int          -- P, 1..20
  AgentId            uuid? FK Agents   -- nudge target; null = operator-driven
  CreatedBy          text?        -- self-reported, as EditedBy elsewhere
  Reason             text?
  CreatedAt          timestamptz
  Status             int          -- Armed 0 | Draining 1 | Completed 2 | Exhausted 3 | Cancelled 4
  EndedAt            timestamptz?
  EndedReason        text?
  LastNudgeKey       text?
  LastNudgeAt        timestamptz?
  NudgeRepeats       int
  ConcurrencyToken   uuid
  IX: (BoardId) unique WHERE Status IN (0,1); (BoardId, CreatedAt desc)

CardBatchEntry
  Id                 uuid PK
  CardBatchId        uuid FK CardBatches (cascade)
  CardId             uuid FK Cards
  Kind               int          -- Started 0 | Skipped 1
  At                 timestamptz
  Evidence           text?        -- "task 5f2c1a3b dispatched" | "session <id> started"
  By                 text?        -- Skipped: who
  Reason             text?        -- Skipped: why
  IX: (CardBatchId, CardId) unique
```

Migration `20260903xxxxxx_AddCardBatches`, hand-written in the CARD-0331 style
(`server/Migrations/20260903120000_AddAgentTaskLandRequest.cs`: attributes in the file, snapshot
updated by hand, no `Sql`, no backfill). `SessionQueuedMessages` is **not** widened: the batch's
pending nudge is found by `ConversationKey == "batch:{id}"` and `Origin == Batch`
(`SessionMessageQueueService` gains `CancelPendingByConversationKeyAsync` if no equivalent exists;
`CancelPendingBySourceScheduleAsync` is the shape).

```csharp
public enum CardBatchStatus { Armed, Draining, Completed, Exhausted, Cancelled }
public enum CardBatchEntryKind { Started, Skipped }
public enum CardBatchCeiling { Parallel, BoardMaxConcurrentSessions, ColumnMaxConcurrentSessions, FleetTaskSlots }

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CreateCardBatchRequest(
    Guid BoardId, int Count, int Parallel, Guid? AgentId = null, bool NoAgent = false,
    string? Reason = null, string? CreatedBy = null);

public sealed record CardBatchPreviewDto(
    int EligibleNow, IReadOnlyList<CardBatchCardRefDto> WillStart,
    int InFlightNow, int BlockedNow, int PendingNow, int SlotsFree,
    CardBatchCeiling BindingCeiling, int EffectiveParallel,
    CardBatchAgentDto? Agent, string? AgentReason, IReadOnlyList<string> Warnings);

public sealed record CardBatchDto(
    Guid Id, Guid BoardId, string BoardName, int TargetCount, int Parallel,
    CardBatchAgentDto? Agent, string? CreatedBy, string? Reason, DateTime CreatedAt,
    CardBatchStatus Status, DateTime? EndedAt, string? EndedReason,
    int Started, int Skipped, int Remaining,
    DateTime? LastNudgeAt, int NudgeRepeats,
    IReadOnlyList<CardBatchEntryDto> Entries, Guid ConcurrencyToken);

public sealed record CardBatchEntryDto(
    Guid CardId, string Identifier, string Title, CardBatchEntryKind Kind, DateTime At,
    string? Evidence, string? By, string? Reason, CardStatus CurrentStatus, bool InFlight);

public sealed record CardBatchInFlightDto(int Cards, int Blocked, int Pending, int Fleet);

public sealed record CardBatchViewDto(
    Guid BoardId, string BoardName, DateTime GeneratedAt, bool OrchestratorPaused,
    CardBatchInFlightDto InFlight, CardBatchDto? Active,
    IReadOnlyList<CardBatchCardRefDto> NextByOrder, int EligibleNow,
    CardBatchCeiling? BindingCeiling, int? EffectiveParallel,
    IReadOnlyList<CardBatchDto> Recent);   // last 5 ended, entries omitted

public sealed record CardBatchCardRefDto(Guid Id, string Identifier, string Title, int Rank, int? Position);
public sealed record CardBatchAgentDto(Guid Id, string Name, bool AlwaysOn, bool Live);
public sealed record CancelCardBatchRequest(string Reason, string? By = null);
public sealed record SkipCardBatchRequest(string Card, string Reason, string? By = null);  // CARD-nnnn | #n | guid
```

Routes (`OrchestratorEndpoints.cs`, group `/api/orchestrator`):

```
GET    /api/orchestrator/batches?boardId=<guid>        → CardBatchViewDto   (boardId required: 400)
GET    /api/orchestrator/batches/{id:guid}             → CardBatchDto
POST   /api/orchestrator/batches/preview               → CardBatchPreviewDto
POST   /api/orchestrator/batches                       → 201 { batch, preview }
                                                          409 card_batch_active · 422 card_batch_pool_empty
                                                          422 card_batch_agent_invalid (pool delegate / other board)
POST   /api/orchestrator/batches/{id:guid}/cancel      → CardBatchDto   (409 card_batch_not_live)
POST   /api/orchestrator/batches/{id:guid}/skip        → CardBatchDto   (422 card not eligible on this board)
POST   /api/orchestrator/batches/{id:guid}/nudge       → { sent: bool, detail }  (409 card_batch_not_live; 422 no agent)
```

Events: `CardBatchChanged { boardId, batchId }` on create / cancel / skip / any sweep that changed a
status or added an entry. `OrchestratorTickResult` gains `SkippedBatch`. Nothing on
`/api/orchestrator/state` changes (CARD-0092 Decision 3 and CARD-0094 Decision 8 both pinned it as
the session snapshot).

---

## Slices

Sequential, one worker at a time, **`-Worktree`** (CARD-0301's Code task is on master today and
CARD-0098 S1 may be dispatched into the same files). Server before client; rebuild the 17203
bundle before any browser check. Estimates are verification floor + authoring.

### S0 — Prerequisite: `CardRanking.OrderKey` (CARD-0098 S1, or its stub)

Land CARD-0098 S1 first if it is dispatched; otherwise this card's S1 adds `OrderKey` with the
CARD-0098 signature and `position: int? = null`, switches `LoadEligibleCandidatesAsync :786-790` to
it, and adds the `CardRankingOrderTests` case "OrderKey equals (Rank, DueAtSortKey, CreatedAt) when
position is null". CARD-0098 S1 then only adds the column and the non-null path. Estimate 0.5 h
inside S1 if needed.

### S1 — Domain, migration, `CardBatchService`, endpoints, API doc

- `server/Domain/Entities/{CardBatch,CardBatchEntry}.cs`, `server/Domain/Enums/{CardBatchStatus,
  CardBatchEntryKind}.cs`, `AppDbContext` mappings and the two indexes; migration + snapshot by hand.
- `server/Application/Dtos/CardBatchDtos.cs` (above).
- `server/Application/Services/CardBatchService.cs` (scoped; `AppDbContext`, `AgentRegistry`? no —
  `IOptions<OrchestratorSettings>`, `IOptions<DelegationSettings>`, `AgentSessionRuntime?`,
  `IEventBus`, `TimeProvider`, logger): `PreviewAsync`, `CreateAsync`, `GetAsync`, `GetViewAsync`,
  `CancelAsync`, `SkipAsync`, `SweepAsync(now, nudge, ct)` (Decisions 2, 3, 4, 6; nudging is S2 —
  S1 computes the key and records nothing), `ComputeInFlight(boardId)` and `Eligible(boardId, now)`
  as internal static helpers over already-loaded rows so the tests can drive them without a tick.
  Card refs resolve through `CardService.ResolveCardIdAsync` fenced to the batch's board.
- `OrchestratorService.PollTickAsync`: call `SweepAsync` at `:74` (before the pause return). The
  tick-side skip is S3.
- `OrchestratorEndpoints.cs`: the seven routes; error codes through the existing exception
  middleware shapes (`ConflictException`/`ValidationException` with the codes above).
- `docs/antiphon-api.md`: route rows under "Orchestration and workflows" and a "Batches
  (CARD-0096)" paragraph (what a start is, what in flight is, that P is beneath every cap).
- Tests, `tests/Antiphon.Tests/Application/CardBatchServiceIntegrationTests.cs`
  (`[Category("Integration")]`, own isolated schema per test in the `HomeTaskServiceIntegrationTests`
  style, `TimeProvider` faked; every assertion on rows the test created, never counts over shared
  tables):
  1. Preview on a board with 6 Backlog cards, N=10, P=2 → `EligibleNow 6`, `WillStart` is the first
     6 by `OrderKey` (seed one placed card, one earlier-created card, one `High` card to pin the
     order), warning "10 requested, 6 eligible".
  2. Create on an empty pool → 422 `card_batch_pool_empty`; second create while live → 409 naming
     the first.
  3. A bound Plan task `Dispatched` after creation + the CARD-0040 Move revision → one `Started`
     entry with `Evidence` naming the task; `Remaining` N−1; a Code task on the same card later →
     still one entry.
  4. A Code task dispatched on a card already In Progress at creation → no entry; the card counts
     in `InFlight`.
  5. A card-spawn claim (`AgentSession.CardId` + `StartedAt`, the `SystemActor` Move revision from
     `SpawnAsync`) → one entry, `Evidence` naming the session.
  6. In-flight: Blocked-only card → `InFlight 1, Blocked 1`; Queued bound task on a Backlog card →
     `Pending 1`, card absent from `Eligible`; Review card with no task → neither.
  7. `Remaining` reaches 0 with one `Started` card still Working → `Draining`; its task settles
     `Succeeded` (no session, no pending) → `Completed`, `EndedAt` set; a re-dispatch on that card
     before completion flips back to `Draining` (pin `Armed ↔ Draining` only via the Remaining rule —
     here it stays `Draining`).
  8. Pool empty, nothing in flight or pending, `Remaining 4` → `Exhausted` with the reason text;
     pool empty but one card Working → still `Armed`.
  9. `Skip` → `Skipped` entry; card absent from `NextByOrder`; skip of a non-Backlog card → 422.
  10. Cancel → `Cancelled`, entries retained, second cancel → 409; a start after cancel is not
      counted.
  11. Sweep twice with no new evidence → no new rows, no `CardBatchChanged` (idempotence); sweep
      against a fresh `DbContext` after "restart" (new scope, same DB) → identical `Remaining`.
  12. `BindingCeiling`: P 2 on a board with `MaxConcurrentSessions 1` → `BoardMaxConcurrentSessions`,
      `EffectiveParallel 1`, warning text; P 1 → `Parallel`.
  `CardBatchApiTests.cs` (the `CardCorrectionApiTests` shape): route shapes, `boardId` missing →
  400, `#n` and `CARD-nnnn` refs on skip, error codes, `Recent` capped at 5 without entries.
- Verify: `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c96/ --
  --treenode-filter "/*/Antiphon.Tests.Application/CardBatchServiceIntegrationTests/*"`, then
  `CardBatchApiTests`, then `OrchestratorServiceIntegrationTests` (the sweep call sits inside its
  tick; must stay green).
- Estimate: 5–7 h.

### S2 — Nudges and the agent contract

- `QueuedMessageOrigin.Batch`; every `Check`/`Scheduled` machine-origin site gains `Batch`
  (executor greps `QueuedMessageOrigin.` and lists the sites touched in the report).
- `SessionMessageQueueService.CancelPendingByConversationKeyAsync` (if absent).
- `CardBatchService.SweepAsync` nudge arm (Decision 5): key, repeat ramp
  (`Orchestrator:BatchNudgeRepeatMinutes` 30, `Orchestrator:BatchNudgeMaxRepeats` 10 — two new
  `OrchestratorSettings` properties, validator bounds ≥ 1), target resolution copied from
  `ScheduleService.cs:255-290`, terminal notes on `Completed`/`Exhausted`/`Cancelled`, `NudgeAsync`
  for the endpoint (`Manual` bypasses the key, not the pause).
- `server/Bundles/orchestrator.md` + `docs/orchestration-loop.md` §1 "Batches" (Decision 10 text).
- Tests (same integration class, +6): a slot opening enqueues one `SessionQueuedMessages` row with
  `Origin Batch`, `ConversationKey batch:<id>`, body naming the head card and "1 slot free of 2";
  the same state on the next sweep enqueues nothing; a head change with the pending row unsent
  cancels it and enqueues one; paused → nothing (and `LastNudgeKey` unchanged); `AgentId null` →
  nothing; agent with no persistent session → nothing and `LastNudgeAt` null; 31 minutes later with
  the key unchanged → one repeat, `NudgeRepeats 1`; `Exhausted` → one terminal note.
- Verify: the integration class; `SessionMessageQueueServiceTests` (unchanged, must stay green
  after the origin addition).
- Estimate: 3–4 h.

### S3 — The tick honours the batch

- `PollTickAsync`: load live batches by board once per tick; the two skips (Decision 7);
  `SkippedBatch` on `OrchestratorTickResult`; client mirror in `orchestrator.ts`
  (`OrchestratorTickResult.skippedBatch`) — one field, no rendering change.
- Tests, `OrchestratorServiceIntegrationTests` (+3, `[NotInParallel]` class as today): a board with
  `MaxConcurrentSessions 3`, a live batch P 1, two eligible active-column cards → one dispatched,
  `SkippedBatch 1`; batch `Draining` → zero dispatched, `SkippedBatch 2`; no batch → both dispatched
  (today's behaviour pinned); and the tick's own claim is counted as a `Started` entry on the next
  sweep (ties S1 case 5 to the real path).
- Verify: `OrchestratorServiceIntegrationTests` targeted; `docs/antiphon-api.md` tick-result note.
- Estimate: 1.5–2 h.

### S4 — Client: `BatchSection`

- `client/src/api/orchestratorBatches.ts`: DTO mirrors, `orchestratorBatchKeys.view(boardId)`,
  `useBatchView(boardId)` (5 s refetch), `usePreviewBatch`, `useCreateBatch`, `useCancelBatch`,
  `useSkipBatchCard`, `useNudgeBatch`; invalidation on success.
- `client/src/hooks/useSignalRInvalidation.ts`: `CardBatchChanged` → `['orchestrator','batches']`;
  add that key to the `CardChanged`, `AgentTaskChanged` and `OrchestratorTick` lists.
- `client/src/features/orchestrator/batchModel.ts` (pure): `boardChoices(boards, cards, view)`,
  `ceilingLabel(dto)`, `batchStatusLabel`, `slotLine(inFlight, parallel)`.
- `client/src/features/orchestrator/BatchSection.tsx` (Decision 9) and `StartBatchModal.tsx`.
- `OrchestratorPage.tsx:84-87`: mount the section between the panel and the backlog.
- Tests (`pwsh -File scripts/test-client.ps1 features/orchestrator`): `batchModel.test.ts`;
  `BatchSection.test.tsx` (MSW `/api/orchestrator/batches`, `/api/boards`, `/api/cards`): three
  boxes with the fixture's numbers; "None running" when `active` is null; Start disabled with reason
  on `eligibleNow 0`; the modal shows the preview list and a warning; Start posts the body and the
  view refetches; Cancel posts with the reason; Skip next posts the head card; the ceiling subline
  names the board cap; error → `data-testid="batch-error"` and *Running Sessions* untouched.
  `OrchestratorPage.test.tsx`: `serve()` gains the batches handler; "the cards tab renders the batch
  section between the retry queue and the backlog"; the existing deferral test still passes.
  `npm run build` for the typecheck.
- Estimate: 4–6 h.

### S5 — `scripts/batch.ps1`, CLI E2E, ops doc

- `scripts/batch.ps1` (Decision 10; ASCII; `Resolve-BoardId` and `Invoke-Antiphon` lifted from
  `card.ps1`'s shapes; long reasons through `-ReasonFile`).
- `tests/Antiphon.E2E/BatchCliE2ETests.cs` (the `ScheduleCliE2ETests` shape, isolated runner):
  `preview` → `start -NoAgent` → `status` shows `Armed 0 of 2` → `skip` → `cancel`; `-Board` by name.
- `docs/ops-http.md` "The jobs you have": one line.
- Estimate: 2–3 h.

### S6 — Rollout, browser check, close

- From the **main checkout** (worktree restarts refuse, exit 3): `dev-backup.ps1`, then
  `scripts/restart-apphost.ps1` after confirming every queued `-Land` finished (CARD-0331 memory);
  the migration applies on startup; confirm no `[ERR]`/`[FTL]` and that
  `GET /api/orchestrator/batches?boardId=<antiphon>` returns `active: null` with today's in-flight
  numbers matching the Home rail.
- Rebuild the bundle (`client-mode.ps1 -Status`), browser-check through the browser-harness lane at
  1280 and 390 px: the three boxes, the modal's preview against the live Backlog order, a
  `-NoAgent` batch of N 1 / P 1 started and cancelled from the CLI reflected on the page without
  reload. **A batch with the real `Antiphon-Orchestrator` as target is a spend and is the operator's
  call**, not this slice's.
- Close the card with the verdict: what a start is, what in flight is, that the batch instructs and
  caps but never launches.
- Estimate: 1–2 h.

Total: roughly 17–24 h of agent time across six dispatches; S1–S4 are the card's floor.

---

## What this card does not do

- **Dispatch delegates from the server.** The batch never creates an `AgentTask`; a server that
  wrote briefs and chose roles would be a second orchestrator with none of §0's judgement. If the
  operator later wants a fully unattended batch, it is a `Prompt` schedule (CARD-0057) to the
  orchestrator agent whose prompt is "work the batch", and it needs nothing here to change.
- **Kill or pause running work on cancel or exhaustion.** Cancel stops *starting*; what is running
  finishes and reports as it would have.
- **Change `MaxConcurrentSessions`, `MaxDispatchesPerTick`, `MaxConcurrentTasks` or
  `RecommendedInFlight`**, or make the tick count delegate sessions in its own ceiling (that ceiling
  is about card-spawn sessions and CARD-0092 explains why `AgentSession.CardId` must not be
  backfilled).
- **Extend `GET /api/orchestrator/state`** (pinned as the session snapshot by CARD-0092 and CARD-0094)
  or edit `OrchestratorPanel.tsx` / `BacklogSection.tsx` (CARD-0095's totals and CARD-0098 S5's drag
  are queued against those files).
- **Cross-board batches.** The order is per board and the orchestrator agent is per board; the fleet
  in-flight number is a subline, not a scope.
- **A "next" chip on the Backlog boxes.** Cheap, but it is CARD-0098 S5's file; left open.
- **An attention row or alert on batch completion.** The terminal note reaches the agent; the panel
  shows the state; the away digest is a separate card if wanted.

## Left open, deliberately

1. **Should a Blocked card release its slot after some time?** Today it holds it (Decision 3). If an
   operator routinely leaves a delegate's question unanswered for hours while wanting the batch to
   proceed, add `Orchestrator:BatchBlockedReleasesSlotAfterMinutes`; not before it is asked for.
2. **Nudging a channel-bound orchestrator.** The note lands in the agent's session like a check note;
   whether it should also reach the operator's Telegram is the away-digest question, not this card's.
3. **A per-role P** ("2 Plans and 1 Code at once"). `RecommendedInFlight` already exists per role and
   is advisory; if the batch should enforce per-role slots, it is one more column and a stage-aware
   `slotsUsed`. The user asked for one number.
4. **Backlog *next* chip** (above) and a "start this one" verb on a Backlog row that also records a
   `Skipped` for the cards it jumped — the latter is exactly CARD-0094 S2's `MoveMenu` slice plus
   one call, once both have landed.

---

## Test matrix

| Slice | Server (TUnit, `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-c96/ -- --treenode-filter …`) | Client (`pwsh -File scripts/test-client.ps1 …`) | E2E |
|---|---|---|---|
| S0/S1 | `CardRankingOrderTests` (+1 if stubbing), `CardBatchServiceIntegrationTests` (new, 12), `CardBatchApiTests` (new), `OrchestratorServiceIntegrationTests` (unchanged, green) | — | — |
| S2 | `CardBatchServiceIntegrationTests` (+6), `SessionMessageQueueServiceTests` (unchanged, green) | — | — |
| S3 | `OrchestratorServiceIntegrationTests` (+3) | — | — |
| S4 | — | `batchModel`, `BatchSection` (new), `OrchestratorPage` (+1) | — |
| S5 | — | — | `BatchCliE2ETests` (new) |
| S6 | — | — | browser check on the built bundle |

Run the named classes per slice; never the whole `Antiphon.Tests` assembly as a narrow slice's
verify (`docs/testing-and-build.md`, CARD-0307). `OrchestratorServiceIntegrationTests` is
`[NotInParallel]`; the new integration class must not join it.

## Sequencing and risks

| Risk | Disposition |
|---|---|
| `OrderKey` not at head when S1 builds | S0: stub with the CARD-0098 signature; one order function either way |
| The CARD-0040 sweep lags the dispatch by up to 60 s, so a start counts one tick late | Accepted and stated; both rows are durable, the count is exact once both exist; the nudge key includes `slotsUsed`, which the pending/dispatched task already moved, so no double nudge in the gap |
| A card moved out of Backlog by hand with nothing running | Not counted, not eligible, not in flight; the panel's entries table cannot show it — Home's rail does. Stated in the API doc |
| The agent ignores nudges (compaction, busy) | Repeat ramp (30 min × 10), then the panel shows `nudged 10× · no start` on the *Batch* box; the batch stays `Armed` for the operator to cancel or nudge |
| Two orchestrators on one board | Not the live shape (one always-on orchestrator per board); explicit `agentId` wins, else 422 `card_batch_agent_ambiguous` naming both |
| `QueuedMessageOrigin.Batch` missed at a machine-origin site | S2 lists every site touched in its report; the stale-message discard test gains a `Batch` case |
| The tick's `SkippedBatch` hides a board-cap skip | Order of checks is board → column → batch; a card skipped for the board cap increments `SkippedGlobalConcurrency`, never `SkippedBatch` |
| Panel and section disagree on "running" | They answer different questions and both captions say so: sessions (panel, Blocked excluded) versus cards (section, Blocked held); S4's caption under the boxes is one sentence |
| CARD-0301 S3 edits `OrchestratorPage.tsx` at the same time | Different lines (`TABS` and a new panel versus the `cards` panel body); `-Worktree` for whichever builds second |
| CARD-0331: land queue not restart-safe | Land each slice and confirm the land finished before any restart (memory) |

## Execution notes

- Build to `--property:OutputPath=bin-c96/` (forward slash); delete every `bin-c96` directory before
  finishing.
- A start needs **both** evidence rows after `CreatedAt` (Decision 2); a reviewer who sees a count
  taken from `AgentTask.DispatchedAt` alone should send it back.
- `slotsUsed` includes `pending` (Queued bound tasks); a nudge computed from `inFlight` alone
  double-books a slot.
- The sweep runs before the pause check; only the nudge reads the pause.
- `batch.ps1` stays ASCII; reasons through files.
- Never `card.ps1 move -Spawn` a card "to make the batch count move" — the count moves on evidence,
  and a spawn is the card-spawn engine, which the operator's loop does not use.
