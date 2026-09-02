# CARD-0031 — "What is happening right now": the rail already answers it; close the two half-answers

**Date:** 2026-09-02 (Plan pass, task 97e3885c — design only; no production code changed)
**Card:** CARD-0031 "UX: a project status view that answers what is happening right now"
(story S1, `docs/product/user-stories.md`)
**Supersedes:** the card's own scope note ("feature 010 is designed but unbuilt") — see the verdict.
Nothing else. The surface's owner docs stay `docs/features/010-home-tasks-section/proposal.md`
(with its errata block) and
[`2026-09-02-card-0002-home-tasks-section-plan.md`](2026-09-02-card-0002-home-tasks-section-plan.md);
this plan is an addendum to that one, not a third design.

**Sources (verified this pass):** CARD-0031, CARD-0002 (Done today: `c13c8520` S3 … `f2ee3580`
S7, head `dfd9511f`) and its plan, CARD-0300 (glance), CARD-0304 (pipeline endpoint, Review) and
its plan, CARD-0301 (Backlog, held), CARD-0288 (Review), CARD-0035/0036 (Done), CARD-0094
(Backlog), `client/src/features/home/tasks/{TasksSection,TaskCard,HomeTaskModal}.tsx`,
`homeTasksModel.ts`, `client/src/api/{homeTasks,attention,agentTasks,agents}.ts`, `HomePage.tsx`,
`AgentRail.tsx`, `MobileHomePage.tsx`, `workLineFormat.ts`, `attentionVisuals.ts`,
`AttentionGlance.tsx`, `useSignalRInvalidation.ts`, `server/Application/Services/{HomeTaskService,
AttentionService,AgentTaskPipelineStatusService,AgentTaskDispatcher,AgentTaskLiveness,
TaskProgressPolicy,SharedWriterLeaseProjection}.cs`, `HomeTaskDtos.cs`, `AttentionDtos.cs`,
`AgentTaskPipelineDtos.cs`, `DelegationSettings.cs`, `AgentTask.cs`, `Card.cs`,
`docs/agent-card-lifecycle.md`, `docs/features/008-home-workspace/proposal.md` §3.2, and the live
server at 17202 (`/api/home/tasks`, `/api/attention`, `/api/agent-tasks/pipeline`,
`/api/cards?boardId=`) on 2026-09-02.

---

## Verdict up front

**The card's premise is stale and its question is answered: this is "010 extended".** Feature
010 shipped in full today as CARD-0002 — a per-project Tasks section on the home rail with five
groups in exactly this card's order (Needs you · Running · To review · Up next · Done), one item
per Card or unbound delegation, a bound delegation nested under its card as the worker line, and
the question text read from the attention feed. The header already carries CARD-0300's glance
(Blocked / Broken / Review counts → `/attention`), so the reading order the story asks for —
"what needs me" before everything else — is the page's reading order today.

**Not fully satisfied, though.** Two of the five points are half-answered, and both halves are
the ones the card names as the hard part:

| Card point | Shipped by CARD-0002 / CARD-0300 | What is missing |
|---|---|---|
| 1 **What needs me?** blocked delegates, failed sessions, incidents | Rail *Needs you*: `NeedsDecision` cards, `Blocked` delegates (bound or not), workflow gates, ranked Decision < Question < Gate, question line from `/attention`. Header glance: failed sessions and incidents (`DeadSession`, `RecentCriticalIncident`, `FailureUnacknowledged`, …) as counts. | Nothing. A *Failed* delegation lands in *Done* by design (CARD-0002 decision 6: the glance owns broken work; the rail marks needs-human per item from that item's own status). |
| 2 **What is being worked on?** which agents, what, *how long*, *genuinely progressing* | Rail *Running*: card + worker line (agent name, role, `Working ⋯` spinner when the agent is mid-turn, else the raw task status); unbound running delegations as items. | **"How long"** is not rendered anywhere — `worker.dispatchedAt` / `item.startedAt` are on the DTO and used only to sort. **"Progressing"** is the agent-level mid-turn spinner and nothing else: a task that is `Dispatched` with a dead or silent session shows a `Dispatched` badge in *Running* — the exact 2026-08-11 shape the card cites. The verdicts that distinguish it (`DeadSession`, `NeverStarted`, `BriefUndelivered`, `ReportUnsettled`, `UnmarkedWaiting`, `PastExpectedIdle`, `ProgressStalled`, `Overdue`, `ChecksSpent`, `UncorrelatedReport`) all exist as `taskId`-keyed rows on `/attention`, which the section already fetches for the question line, and `lastActivityAt` exists per in-flight task on `/api/agent-tasks/pipeline`. The rail reads neither. |
| 3 **What is waiting for review?** | Rail *To review*: `Review` cards, longest-waiting first, cap 8 + "+N more → board"; *Done* delegations carry the unread dot + `Read` deep link; header *To read* badge scrolls there. | Nothing. |
| 4 **What is queued?** what starts next and *why it has not* | Rail *Up next*: Backlog cards by priority (72 live, cap 8), In-Progress cards nobody is on, `Queued` unbound delegations, a queued bound task as the worker line "Queued". | **"Why not"** is not rendered anywhere. CARD-0304's pipeline projection already computes it per queued task: `queueReason` ∈ {`sharedCheckoutLease` (+ `heldBy` naming the holder), `routingPinNotBefore`, `awaitingDispatch`} and `ready` (a landed Plan waiting for a Code slot, with the deliverable path). CARD-0002 left exactly this open ("adopt it here only after CARD-0301 fixes its shape"). The **concurrency cap** is not a reason anywhere — the dispatcher counts `skippedConcurrency` per tick and logs it (`AgentTaskDispatcher.cs:331-336`); the **budget ceiling** is not a queue state at all — the dispatcher *Blocks* the task with "Run cost ceiling reached" (`:445-449`), so it already sits in *Needs you* with that sentence as its question, which is the right place; a **model hold** writes a `Held` event on the task (`:415-437`) and a fleet `ModelAvailabilityHold` row keyed by the *source* task. |
| 5 **What finished?** recently, *with what they produced* | Rail *Done*: cards and delegations completed ≤ 7 d, newest first, cap 12; delegations show the deliverable link. | Done **cards** say nothing about what they produced; `Card.TerminalReason` (the close verdict, written on every terminal move) is not on the home DTO. Minor. |

So: **do not close the card as already-done, and do not design a third surface.** Extend the
rail with joins of two projections that already exist, plus two small additive server fields.
No new endpoint, no new attention kind, no new group, no storage.

One live quirk worth recording, not fixing here: CARD-0032 is `Done` and sits in *Needs you*
because its Deploy delegate is `Blocked` — CARD-0002's rule "an open bound Blocked task beats the
card's status" is doing what it says (someone must answer or cancel that delegate); the `Done`
state badge beside a `Question` badge just reads oddly. Left open below.

---

## Decision

1. **This card is 010 extended.** Its deliverable lives with 010's successor (this plan beside the
   CARD-0002 plan) and the 010 proposal's errata block gets one line pointing here. No
   `docs/features/012-…` — the card's own rule is "do not design a third thing in parallel by
   accident", and the surface, layout, groups, DTO and modal are all CARD-0002's.

2. **"Progressing" is read from the attention feed per Running item, the way the question line
   already is — never re-derived.** `livenessFor(item, attentionItems)` in `homeTasksModel.ts`
   beside `questionFor`: the first row whose `taskId` is the item's own id (delegation) or its
   `worker.taskId` (card) and whose `kind` is in a pinned `LIVENESS_KINDS` list of ten (the
   task-progress conditions in the table above). Rendered as a badge from
   `ATTENTION_VISUALS[kind]` — label, colour, icon, tooltip hint all exist — in the worker line's
   status slot (and on a Running delegation card's header). The item **stays in Running**: this
   is the CARD-0300 boundary restated (a stuck row never changes group), and it is why the badge
   is the feed's own vocabulary rather than a rail-only word like "stalled". The `Working ⋯`
   spinner is a separate fact (mid-turn) and keeps rendering alongside a mid-turn verdict such as
   `ProgressStalled` or `Overdue`. New kinds are *ignored* here, deliberately the opposite of
   `ATTENTION_VISUALS`' totality: a rail badge must mean "this task's progress", and the pinned
   list is that contract.

3. **"How long" is arithmetic on fields the DTO already carries.** Running: elapsed since
   `worker.dispatchedAt ?? item.startedAt ?? item.createdAt`, printed as `2h 14m` with
   `taskVisuals.formatDuration` (exists, `taskVisuals.ts:104`), re-rendered on a 60 s tick.
   Beside it, when the pipeline row exists, `active 3m ago` from the in-flight row's
   `lastActivityAt` (the newest transcript timestamp after dispatch,
   `AgentTaskPipelineStatusService.ToInFlight`) — the raw, uninterpreted "is it doing anything"
   signal; the badge in decision 2 is the interpreted one. **No threshold is applied on the
   rail**: "quiet for 40 minutes" becomes a verdict only when `AttentionService` says so.

4. **"Why not started" is read from CARD-0304's pipeline projection, consumed as-is, plus one
   additive reason.** A new `usePipeline()` hook over `GET /api/agent-tasks/pipeline` (nobody on
   the client consumes it today; the 0304 plan §S4 said the first consumer owns the hook and the
   fixture — CARD-0301 is held in Backlog, so that consumer is this card and 0301 inherits the
   hook). `queueReasonFor(item, pipeline)` finds the queued row by `item.id` or `worker.taskId`
   and yields a dimmed line under the item:

   | `queueReason` | Line |
   |---|---|
   | `sharedCheckoutLease` | `waiting: shared checkout held by task-1a2b3c4d — <holder title>` (first holder; `+N` when more) |
   | `concurrencyCap` (**new**, S1) | `waiting: 6 of 6 task slots in use` |
   | `routingPinNotBefore` | `waiting: not before 14:00 (routing pin)` |
   | `awaitingDispatch` | `queued — next dispatch tick` |

   and `readinessFor(item, pipeline)` marks an Up-next **card** that the pipeline lists as
   `ready` with `plan landed 3d ago — ready for Code` and the deliverable link (same `/plans?…`
   shape as the Done `Read` link). **Server change, one and small:** `ToQueued` gains
   `concurrencyCap`, reported when the task is not lease- or pin-held and the fleet's in-flight
   count against the cap (`inFlightRows.Count`, which already excludes Check — the same predicate
   as the dispatcher's `active` query at `AgentTaskDispatcher.cs:260-262`) is ≥
   `DelegationSettings.MaxConcurrentTasks`; and `AgentTaskPipelineDto` gains top-level
   `maxConcurrentTasks` + `inFlightAgainstCap` so the line can say "6 of 6". Precedence stays the
   dispatcher's (lease → pin → cap), so a task behind a checkout reports the checkout. Budget and
   model hold: no change (see the table; model hold is left open).

5. **"With what they produced" for Done cards is `TerminalReason`'s first line.** Additive
   `TerminalReason` on `HomeTaskItemDto` (cards only, null otherwise), projected in
   `HomeTaskService.LoadCardsAsync`, rendered like the question line (`lineClamp 1`, dimmed) on
   Done cards. Done delegations already carry the unread dot and `Read` link; nothing added. This
   is the one slice the caller may drop without affecting the others (S2).

6. **What this costs the surfaces it shares the screen with — and what it deliberately does not
   show.** Rail width, group order, caps and the modal are untouched. Every addition is one extra
   `xs` line or badge on items in Running, Up next or Done, so a calm rail is pixel-identical.
   Nothing is added to the header, the glance, the dock, the mobile bands or the board. No reply
   box, no bucket count, no retry from the rail beyond the kebab that exists. One more 15 s poll
   on Home (`/api/agent-tasks/pipeline`, bounded by open tasks — two on the live fleet) must stay
   inside CARD-0216's sub-second budget; S6 measures it. The section never blocks on an enrichment:
   a failed pipeline fetch drops the reason/ready/active lines and nothing else; a failed
   attention fetch drops the verdict badges and the question line (already the behaviour);
   elapsed needs no fetch and always renders.

7. **Not CARD-0301 and not CARD-0094.** CARD-0301 is the fleet-wide, stage-major phone view over
   the same pipeline endpoint; this is per-project and item-major and only borrows the hook.
   CARD-0094 (Backlog-by-priority on the orchestrator page) is a different page; *Up next* keeps
   its cap of 8 and its "+N more → board" link.

---

## Ground truth (checked, not guessed)

Line numbers as of `dfd9511f`.

| Claim in the card / story | Today | Consequence |
|---|---|---|
| "Feature 010 (Tasks rail, CARD-0002) is designed but unbuilt" | Built and Done: `HomeTaskService` (`GET /api/home/tasks`), `TasksSection.tsx`, `TaskCard.tsx`, `HomeTaskModal.tsx`, `homeTasksModel.ts`, story + screenshot `docs/ui-screenshots/home-taskssection--rail.png`, 34 client tests + `HomeTaskServiceIntegrationTests` | The scope note is answered: 010 extended |
| "Cards and AgentTasks are two disjoint record systems" | Still two tables, but `AgentTask.CardId` binds them and `CardWorkTransitionService` moves cards from bound tasks (CARD-0040); the home projection nests a bound task under its card | The unification the card worried about is done at the read side |
| "'Needs me' spans blocked tasks, agent incidents and review threads — three places" | Rail *Needs you* (statuses) + glance (incidents) + *To review* / `Read` links (review). Same page, top to bottom | Satisfied by composition; nothing merged |
| "A task read `Dispatched` for nine hours while its session had finished" | Five sweeps since: dead-session fail (CARD-0021, `AgentTaskLiveness`), never-started fail (CARD-0003), report re-hand (CARD-0288, `ReportUnsettled`), unmarked-waiting → Block (CARD-0294), phase deadline (CARD-0020). Each also has an attention row. | The record heals itself within a tick or two; what remains is that the *rail* shows the interim `Dispatched` with no verdict beside it |
| Running items show duration | No. `TaskCard.tsx` renders `stage:` / tier·role·cost / question / worker line; `formatRelativeAgo` is used only for a *settled* worker (`plan · done 2h ago`) | Decision 3 |
| Running items show liveness | Only `agent.working` → `Working ⋯` (`TaskCard.tsx` `WorkerLine`, via `useAgentList()`); else `STATUS_COLOR[worker.status]` badge | Decision 2 |
| Up next shows a reason | No. `ClassifyTask(Queued) → Next`; the worker line prints `Queued` | Decision 4 |
| A queue reason exists server-side | `AgentTaskPipelineQueuedDto.QueueReason` + `HeldBy` (`AgentTaskPipelineDtos.cs:41-50`), computed in `AgentTaskPipelineStatusService.ToQueued` (`:260-309`) from `SharedWriterLeaseProjection.SerialisingHolders` and the routing pins; `Ready` rows on the Code stage (`BuildReady`) | Consume; do not recompute |
| Concurrency cap is visible somewhere | No row, no event. `TickResult.SkippedConcurrency` (`AgentTaskDispatcher.cs:169`) is a log counter | S1 adds it to the pipeline |
| Budget ceiling is a queue reason | No — `RootIsOverBudgetAsync` → `BlockAsync(task, "Run cost ceiling reached ($50.00).")` (`:445-449`); `MaxCostUsdPerRoot` is per root (`DelegationSettings.cs:44`) | Already in *Needs you* as a Question with that evidence; no change |
| Model hold is a queue reason | `Held` event on the task (`:415-437`) + `AttentionKind.ModelAvailabilityHold` keyed by `hold.SourceTaskId` (`AttentionService.cs:1461-1468`), not by the held task | Left open |
| Last activity exists | `AgentTaskPipelineInFlightDto.LastActivityAt` = newest transcript timestamp after dispatch, else dispatch/created (`ToInFlight`, `:312-332`) | Decision 3 |
| `TerminalReason` reaches Home | `Card.TerminalReason` (`Card.cs:26`) is on `CardDto` (`BoardDtos.cs:70`), not on `HomeTaskItemDto` | S2 |
| Attention rows are task-keyed | `AttentionItemDto.TaskId` (`AttentionDtos.cs:270`); client mirror `attention.ts:136` | The join is `taskId === item.id || taskId === item.worker?.taskId`, exactly `questionFor`'s |
| Pipeline has a client consumer | None (`grep usePipeline\|agent-tasks/pipeline client/src` → nothing); no contract fixture | S3 owns both |
| Mobile | `MobileHomePage.tsx` three bands; band 2 "In motion" uses `WorkLine` (`check 13:02 · $1.12`) | Untouched |

**Live data, 2026-09-02 04:15 (17202):** 163 home items; *Needs you* 1 (CARD-0032, Done +
Blocked Deploy worker); *Running* 1 (CARD-0031, `Dispatched Plan`, this task); *To review* 31;
*Up next* dozens of Backlog cards; pipeline: Plan in-flight 1 (`lastActivityAt` 4 min after
dispatch), Code `ready` 2 (CARD-0033 since 08-26, CARD-0251 since 08-30 — six and three days
waiting for a Code slot, visible nowhere on Home today), Deploy blocked 1; attention 49 rows.

---

## Item anatomy after this card

```
┌──────────────────────────────────────────────┐
│ CARD-0002  [Card]          [In progress]  ⋮  │
│ Tasks section on the home rail               │
│ stage: code                                  │
│ └ ▮ task-e627ab84  code  [Working ⋯] [Overdue]│  ← verdict badge from ATTENTION_VISUALS (S5)
│     2h 14m · active 3m ago                   │  ← elapsed + last activity (S5)
└──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
│ CARD-0033  [Card]              [Backlog]  ⋮  │
│ Answer a blocked delegate in place           │
│ stage: plan                                  │
│ plan · done 6d ago                           │
│ plan landed 6d ago — ready for Code   Read   │  ← readinessFor (S5)
└──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
│ 7c1d2e3f  [Task]                [Queued]  ⋮  │
│ CARD-0251 S2 scope tooling                   │
│ [opus] code $0.00                            │
│ waiting: shared checkout held by task-1a2b…  │  ← queueReasonFor (S5)
└──────────────────────────────────────────────┘
┌──────────────────────────────────────────────┐
│ CARD-0300  [Card]                 [Done]  ⋮  │
│ Home screen: compact stats summary           │
│ Fixed and merged to master (0abeeed), inde…  │  ← terminalReason first line (S2 + S5)
└──────────────────────────────────────────────┘
```

---

## Slices

### S1 — Pipeline: `concurrencyCap` reason and the cap fields

**Files:** `server/Application/Dtos/AgentTaskPipelineDtos.cs` (`AgentTaskPipelineDto` gains
`int MaxConcurrentTasks, int InFlightAgainstCap` after `RecommendationsAreAdvisory`),
`server/Application/Services/AgentTaskPipelineStatusService.cs` (`QueueReasonConcurrencyCap =
"concurrencyCap"`; `ToQueued` takes the in-flight count; applied only when the reason is still
`awaitingDispatch` after lease and pin, mirroring the dispatcher's order), `docs/antiphon-api.md`
(the pipeline row: one sentence naming the fourth reason).

**Tests:** `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs` — extend
`queued_work_is_awaiting_dispatch_unless_a_live_lease_holds_it`'s neighbourhood with:
(a) `MaxConcurrentTasks = 1` + one own Working task + one own Queued task in a different repo →
`concurrencyCap`, `InFlightAgainstCap == 1`; (b) the same with a lease hold → still
`sharedCheckoutLease` (precedence); (c) a Check-role Working task does not count. Assertions on
own rows only (the shared Postgres rule).

**Verify:** `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0031/ -- --treenode-filter "/*/Antiphon.Tests.Application/AgentTaskPipelineStatusTests/*"`

### S2 — Home projection: `TerminalReason` on Done cards (droppable)

**Files:** `server/Application/Dtos/HomeTaskDtos.cs` (`string? TerminalReason` after `Title`;
delegations null), `server/Application/Services/HomeTaskService.cs` (`CardRow` + `LoadCardsAsync`
select + `RankCard`), `client/src/api/homeTasks.ts` (`terminalReason: string | null`),
`client/src/test/fixtures/contract/home-tasks.json` (recaptured by `ContractSnapshotTests` —
the delegation scenario already closes a card; assert the field is present).

**Tests:** `HomeTaskServiceIntegrationTests` — one case: own card closed with a reason carries it;
own open card carries null.

**Verify:** `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0031/ -- --treenode-filter "/*/Antiphon.Tests.Application/HomeTaskServiceIntegrationTests/*"` and
`dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0031/ -- --treenode-filter "/*/*/ContractSnapshotTests/*"`

### S3 — Client API: `usePipeline()`, fixture, invalidation

**Files:** `client/src/api/agentTasks.ts` — TS mirror of `AgentTaskPipelineDto` and its five row
types (names as the C# records, `AgentTaskPipelineQueueReason = 'sharedCheckoutLease' |
'concurrencyCap' | 'routingPinNotBefore' | 'awaitingDispatch'`), `agentTaskKeys.pipeline()`,
`usePipeline(enabled = true)` at 15 s like `useHomeTasks`; `client/src/hooks/useSignalRInvalidation.ts`
— `AgentTaskChanged`, `AgentQueueChanged`, `SessionFinished` additionally invalidate
`['agentTasks', 'pipeline']`; `tests/Antiphon.E2E/ContractSnapshotTests.cs` — a `pipeline.json`
capture in the delegation scenario (one queued task behind a lease, one in-flight, one ready
card), written to `client/src/test/fixtures/contract/pipeline.json`.

**Tests:** the snapshot test (first run captures, later runs drift-guard);
`useSignalRInvalidation.test.ts` (if present) pins the three new keys.

**Verify:** the E2E filter above; `pwsh -File scripts/test-client.ps1 useSignalRInvalidation`.

### S4 — Presentation model

**Files:** `client/src/features/home/tasks/homeTasksModel.ts` — pure additions:
`LIVENESS_KINDS` (the ten kinds in decision 2, as a `ReadonlySet<AttentionKind>`);
`livenessFor(item, attentionItems): AttentionItemDto | null`; `runningSince(item): string | null`
(`worker.dispatchedAt ?? startedAt ?? createdAt` for Running items, null otherwise);
`pipelineRowFor(item, pipeline)` (in-flight / queued / ready lookup by `item.id` or
`worker.taskId`, cards matched to `ready` by `card.id`); `queueReasonFor(item, pipeline):
{ reason, line, holders } | null`; `readinessFor(item, pipeline): { since, deliverablePath,
deliverableRef, sourcePlanShortId } | null`; `QUEUE_REASON_LABEL`; `formatRelativeAgo` moved here
from `TaskCard.tsx` (exported, `now` injectable) beside a new `formatElapsed(fromIso, now)` that
delegates to `taskVisuals.formatDuration`.

**Tests:** `homeTasksModel.test.ts` — `livenessFor` (delegation by own id; card by worker id;
`BlockedQuestion` and `CardStalled` rows are *not* liveness even when task-keyed; first match
wins; null when absent); `runningSince` (three fallbacks; null off Running); `queueReasonFor`
(each of the four reasons produces its line; holder naming; null for a card whose worker is not
queued); `readinessFor` (ready card found by card id; a Done/NeedsDecision card is never asked);
`formatElapsed` pinned at `2026-02-03T09:14:00Z` like the story.

**Verify:** `pwsh -File scripts/test-client.ps1 homeTasksModel`

### S5 — `TaskCard` rendering

**Files:** `client/src/features/home/tasks/TaskCard.tsx` — new props `liveness`, `pipelineRow`
(both optional, null-safe) and `now`; worker line: verdict `Badge` from
`ATTENTION_VISUALS[liveness.kind]` (label, colour, icon, `Tooltip` hint) in the status slot when
present, the spinner untouched; a second `xs` dimmed line under an *open* worker or a Running
delegation: `formatElapsed(runningSince) · active {formatRelativeAgo(lastActivityAt)}` (the
`active` half only when the pipeline row exists); Up next: `queueReasonFor` line, and for a
ready card the `plan landed … — ready for Code` line with a `Read` `Anchor` to
`/plans?file=…&ref=…&task=<sourcePlanTaskId>` (`stopPropagation`, as the Done link); Done cards:
`terminalReason` first line (`lineClamp 1`, dimmed). A 60 s `useInterval` in `TasksSection`
(not per card) supplies `now`.

**Tests:** `TaskCard.test.tsx` — verdict badge renders the visual's label and not the raw status;
spinner and `Overdue` coexist; elapsed line on a Running card with a Dispatched worker and on a
Running delegation, absent on Up next / Done; `active Xm ago` only with a pipeline row; each
queue-reason line; ready line + `Read` link does not fire `onOpen`; terminalReason line on a Done
card only; a Needs-you item is visually unchanged (snapshot-free: assert the absence of the new
lines).

**Verify:** `pwsh -File scripts/test-client.ps1 TaskCard`

### S6 — `TasksSection` wiring, story, browser, docs, close

**Files:** `client/src/features/home/tasks/TasksSection.tsx` — `usePipeline()`, `useInterval`
for `now`, pass `liveness={livenessFor(item, attentionItems)}` and
`pipelineRow={pipelineRowFor(item, pipeline.data)}` to `TaskCard`; a pipeline error is
swallowed into "no enrichment" (no new error line — the section's one error line stays for the
projection itself). `TasksSection.stories.tsx` — seed `agentTaskKeys.pipeline()` from
`pipeline.json` and `attentionKeys.all` from a small hand-shaped `attention` payload **only if**
`ContractSnapshotTests` can capture one with an `Overdue`/`ProgressStalled` row (it can force
`PastExpectedIdle` with `ExpectedDurationMinutes = 0` and the test clock); else the story shows
the elapsed and queue lines and the screenshot note says why no verdict badge appears.
`npm run screenshots -- taskssection` → `docs/ui-screenshots/home-taskssection--rail.png`
(same name; the id filter is `taskssection`, the 0002 plan's `tassection` never matched).
`docs/features/010-home-tasks-section/proposal.md`: the addendum paragraph pointing here was
added by this plan pass — no further edit. `docs/features/008-home-workspace/proposal.md` §3.2:
extend the rail sentence by one clause.
`docs/ops-http.md`: no change (both routes are already in the table).

**Tests:** `TasksSection.test.tsx` — the pipeline handler is part of `seed()`; a Running item
with a `DeadSession` attention row shows the `Dead session` badge and stays under *Running*
(group counts unchanged — the CARD-0300 boundary pinned); a queued delegation shows its lease
line naming the holder; a ready card shows its line; pipeline 500 → rail renders every group,
no reason lines, no error text; attention 500 → no badges, no question line, rail intact.
`HomePage.test.tsx` — `seed()` gains the pipeline handler; nothing else changes.

**Browser** (user rule: UI work is not done on tests alone; `client-mode.ps1 -Status` before
trusting 17203): desktop ≥ 1280 on the Antiphon project — the Running item shows `Nh Nm ·
active …`; the two `ready` cards (CARD-0033, CARD-0251) show their ready line under *Up next*
with a working `Read`; a Done card shows its verdict line; the rail is still 300 px and the
*Needs you* item is unchanged; header, dock and glance unchanged; 375 × 667 unchanged. Measure
Home's load with the extra poll (CARD-0216's target: < 1 s); if the pipeline call is the one
that breaks it, gate `usePipeline` on `grouped.Running.length + grouped.Next.length > 0`.

**Close:** move CARD-0031 with the verdict "010 extended — CARD-0002 built the surface; this card
added the progress verdict, elapsed/last-activity, queue reason and terminal reason lines
(commits …)". The card's own text keeps its stale scope note (history); this plan is the
correction.

**Verify:** `pwsh -File scripts/test-client.ps1 features/home` and `HomePage`; the screenshot
script; the browser pass above.

---

## What this card does not do

- A new page, route, group, endpoint, attention kind, SignalR event, or storage column.
- Re-deriving stuckness, thresholds, or "stalled" on the client — `AttentionService` and
  `TaskProgressPolicy` own every verdict; the rail prints their rows.
- Moving a Failed delegation out of *Done*, or any change to which items land in *Needs you*.
- Budget ceiling as a queue reason (it is a Blocked state and already a Needs-you question);
  model hold as a per-task queue reason (left open).
- CARD-0301's stage-major phone view, CARD-0094's backlog-by-priority, CARD-0300's glance, the
  mobile bands, the board, the delegations board.
- Cost on the rail beyond what delegations already print (`formatCost`); spend-per-root is
  CARD-0036's and the report's.
- Card write verbs beyond the existing kebab.

## Left open, deliberately

1. **Model hold as a queue reason.** Needs `ResolveDispatchAliasAsync` + `IsHeldAsync` per queued
   task inside the pipeline projection (async, per row) or a `HeldBy`-style pointer from the
   `ModelAvailabilityHold` row to the tasks it holds. The `Held` event is in the drawer today.
2. **The Done-with-Blocked-worker badge pair** (CARD-0032 live): the state badge could yield to
   the reason badge when a card's group is *Needs you* by worker. One line in `TaskCard`; not
   this card's question.
3. **Per-item cost on Running cards** (the worker's `costUsd` is on the DTO, unrendered).
4. **Gating `usePipeline`** on non-empty Running/Up next, if the load measurement in S6 says so.
5. **Mobile "In motion"** could print the same elapsed/verdict pair through `workLineFormat` — a
   later card against the mobile spec, not a side effect here.

## Test matrix

| Layer | Test | Command |
|---|---|---|
| Server | `AgentTaskPipelineStatusTests` (+3 cases) | `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0031/ -- --treenode-filter "/*/Antiphon.Tests.Application/AgentTaskPipelineStatusTests/*"` |
| Server | `HomeTaskServiceIntegrationTests` (+1 case) | same, `HomeTaskServiceIntegrationTests` |
| Contract | `ContractSnapshotTests` — `pipeline.json` new, `home-tasks.json` recaptured | `dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0031/ -- --treenode-filter "/*/*/ContractSnapshotTests/*"` |
| Client pure | `homeTasksModel.test.ts` | `pwsh -File scripts/test-client.ps1 homeTasksModel` |
| Client components | `TaskCard`, `TasksSection`, `HomeTaskModal` (unchanged, must still pass) | `pwsh -File scripts/test-client.ps1 features/home/tasks` |
| Page | `HomePage.test.tsx` | `pwsh -File scripts/test-client.ps1 HomePage` |
| Visual | `TasksSection.stories.tsx` | `npm run screenshots -- taskssection` |

Build to an alternate output path (`--property:OutputPath=bin-card0031/`, forward slash) while the
daemons hold `bin/`; delete the `bin-card0031` directories before finishing. Do not run the whole
`Antiphon.Tests` assembly for these two classes.

## Sequencing and risks

**Order:** S1 ∥ S2 (server, independent) → S3 (needs S1's DTO for the fixture) → S4 (pure; can
start in parallel with S3 against the DTO shape) → S5 → S6. S1+S2+S3 is one landing (server +
fixtures), S4+S5 a second, S6 last. Docs-and-client shape; a Shared workspace is fine for one
worker at a time — the fixture recapture touches `client/src/test/fixtures/contract/`, which
another worker's E2E run would also write, so do not run two of these concurrently.

| Risk | Disposition |
|---|---|
| A third Home poll pushes load past CARD-0216's budget | Measure in S6; gate `usePipeline` on non-empty Running/Up next (Left open 4) |
| A verdict badge makes an item look like it belongs in *Needs you* | It stays in Running by construction; the badge colour is the feed's severity colour (danger/warning), never the Needs-you border. `TasksSection.test` pins the group count |
| `LIVENESS_KINDS` drifts from `AttentionKind` | Deliberate: a new kind is silently not a rail badge until someone adds it; the test enumerates the ten and asserts `BlockedQuestion`/`CardStalled` are excluded |
| Pipeline `ready` names a card the project filter dropped | Then the card is not on the rail either; consistent |
| Fixture recapture changes `home-tasks.json` unrelated fields | It is a capture; review the diff, it should be the one new key |
| `formatRelativeAgo` move breaks the settled-worker line | Same function, exported; `TaskCard.test` "worker line" case covers it |
| `concurrencyCap` disagrees with the dispatcher's count | Both use `Role != Check && Status ∈ {Dispatched, Working}`; the S1 test with a Check-role Working task pins it |

## Execution notes

- The join key is always the **task**: `item.id` for a delegation, `item.worker.taskId` for a
  card. Never join attention or pipeline rows by card id except `ready` (which is card-keyed by
  design).
- Do not put a `liveness` or `queueReason` field on `HomeTaskItemDto` "for convenience" —
  CARD-0002 decision 4 (read the feed, never copy it) is the CARD-0300 boundary and the reason
  the rail cannot drift from `/attention`.
- `ATTENTION_VISUALS` is total over `AttentionKind`; reuse it, do not add a rail-local label map.
- Elapsed is `Date.now()`-based; inject `now` in tests and pin it in the story.
- `terminalReason` is a first-line read like the question line (`split(/\r?\n/)`, first non-blank).
- When touching `TasksSection.tsx`, keep the one existing error line for the projection; enrichments
  fail silent.
