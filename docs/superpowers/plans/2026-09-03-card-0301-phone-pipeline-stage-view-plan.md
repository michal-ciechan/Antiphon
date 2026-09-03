# CARD-0301 — Phone-friendly pipeline-stage view: one line per card, per stage

**Date:** 2026-09-03 (Plan pass, task 4778c920 — design only; no production code changed. Held for
fable per the card's 2026-09-01 scheduling note; picked up on the named day.)
**Card:** CARD-0301 "Phone-friendly pipeline-stage view: 1 line per card, in-flight/queued/ready
per stage"
**Builds on:** [`2026-09-01-card-0304-stage-wip-pipeline-status-plan.md`](2026-09-01-card-0304-stage-wip-pipeline-status-plan.md)
(the endpoint this view renders; its §S4 named CARD-0301 as the phone consumer),
[`2026-09-02-card-0031-project-status-view-plan.md`](2026-09-02-card-0031-project-status-view-plan.md)
(`usePipeline()`, the contract fixture, queue-reason wording — Done),
[`2026-09-02-card-0093-delegations-active-board-history-plan.md`](2026-09-02-card-0093-delegations-active-board-history-plan.md)
(the board this card was told to check first — Done), and
[`../specs/2026-08-17-mobile-thread-and-plan-surfacing.md`](../specs/2026-08-17-mobile-thread-and-plan-surfacing.md)
§D3 (the phone's one-line status-row grammar).

**Sources (verified this pass, head `e56fc2d1`, live 17202 on 2026-09-03 02:54Z):** CARD-0301,
CARD-0093 (Done 2026-09-02), CARD-0304 (Review), CARD-0002 / CARD-0031 (Done), CARD-0300 (Review),
`server/Application/Services/AgentTaskPipelineStatusService.cs`, `AgentTaskPipelineDtos.cs`,
`AgentTaskEndpoints.cs:48-52`, `DelegationSettings.cs:295-311,:730`, `AgentTaskDispatcher.cs:2395-2445`
(CARD-0215 hold), `tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`,
`tests/Antiphon.E2E/ContractSnapshotTests.cs:446-465,:578-690,:828-870`, `client/src/api/agentTasks.ts`
(`usePipeline` `:451`, DTO mirrors `:222-316`), `client/src/hooks/useSignalRInvalidation.ts:111,:130,:151`,
`client/src/features/home/tasks/{homeTasksModel,TaskCard,TasksSection.stories}.ts*`,
`client/src/features/home/{HomePage,MobileHomePage,WorkLine,workLineFormat}.ts*`,
`client/src/features/orchestrator/{OrchestratorPage,OrchestratorPage.test}.tsx`,
`client/src/features/delegations/{DelegationsBoard,DelegationsHistory,TaskDrawer,TaskChip,taskVisuals}.ts*`,
`client/src/shared/{Layout,cardIdentifier}.ts*`, `client/src/stories/mobile-proposal/MobileHome.stories.tsx`,
`client/scripts/storybook-screenshots.mjs`, `docs/ui-screenshot-testing.md`, `docs/antiphon-api.md:239`,
`server/Application/Services/ModelLevelAliases.cs:51-56`, and the live `GET /api/agent-tasks/pipeline`
and `GET /api/agent-tasks?since=<7d>`.

---

## Verdict up front

**CARD-0093 is not this screen, and it is already Done.** It fixed the delegations fan-out board
(`DelegationsBoard`): active-only, a 60-minute *Just settled* lane, settled work on a History tab.
That board is **task-major** (one chip per task, fanned out under its root) and **status-major**
(lanes are Queued / Working / Blocked / Just settled). It has no stage axis, no "ready for the next
stage" concept, no phone layout (a four-lane grid, virtualised, 620 px tall), and it reads
`GET /api/agent-tasks`, which cannot say *why* a task is queued. Nothing in it should be bent into
a stage view. What CARD-0093 does settle is **where** this view lives: `/orchestrator` is the page
whose stated job is "what is the fleet doing right now", its tabs are URL-addressed
(`?tab=history` was added there by 0093 S2 in exactly this way), and the task drawer a row taps
into is already on that page.

**The endpoint is the design.** `GET /api/agent-tasks/pipeline` (CARD-0304, extended by 0031 S1
and 0305) already returns, per stage in enum order: in-flight rows with `lastActivityAt`, queued
rows with a `queueReason` and the lease `heldBy`, blocked rows, `ready` rows (a landed Plan waiting
for a Code slot, with the deliverable path), the stage's advisory `recommendedInFlight`, the
stage-wide routing pin, and the fleet cap (`inFlightAgainstCap` / `maxConcurrentTasks`). The client
already has the hook (`usePipeline()`, 15 s poll, SignalR-invalidated), the contract fixture
(`pipeline.json`), and the queue-reason wording. This card is **a client rendering of that
endpoint plus two small additive server gaps**, and nothing else:

1. **Rows do not say what they run on.** In-flight / queued / blocked rows carry `agentName`
   (`task-4778c920`) but not `agentKind` / `modelLevel` / `workspace`. The card asks for "kind/model"
   on every in-flight line, and on the live fleet it matters: last 7 days, 343 tasks were Grok 56 %,
   Claude 29 %, Codex 15 %, with a `Required` stage pin "execute with grok" on Code. Three
   additive fields per row (S1).
2. **The CARD-0215 hold is invisible.** Since `0cf8e02c` the dispatcher holds a card-bound
   Worktree task while a same-card sibling's land is in flight. The projection reports such a task
   as `awaitingDispatch` ("queued — next dispatch tick"), which under-claims — and CARD-0331 says a
   queued land can strand forever, which is exactly the case an operator on a phone needs named.
   One more `queueReason`, DB-only (S1b, droppable).

Everything else the card asks for — one compact line per card, grouped by stage, in-flight /
queued / ready / blocked distinguished, no horizontal scroll, scannable on a phone — is pure client
work over fields that exist. About one day of Execute.

---

## Decision

1. **A sixth tab on `/orchestrator`: `?tab=pipeline`, panel `PipelineStagesPanel`.** Not a new
   route (the page already answers this question and holds the drawer), not a rail on Home (0002 /
   0031 fixed Home as per-project and item-major; this is fleet-wide and stage-major — both plans
   said so and left this card the stage axis), not a variant of `DelegationsBoard` (see the
   verdict). The tab is the same component on desktop and phone: a single column, `maw` 560 px,
   centred. The desktop fan-out board keeps its job; this tab is the *glance*.

2. **Stage = `AgentTaskRole`, labelled in the operator's words, empty stages hidden.** The 0304
   plan fixed the taxonomy (roles, `Check` hidden) and said "Execute is a UI alias for Code". So:
   `Code → Execute`, `Custom → Other`, every other role its own name. **There is no Investigate
   stage** — the role enum has none and the last 7 days show investigations dispatched as Plan
   (111), Debug (28) or Custom (8); the card's "Investigate → Plan → Execute" is the orchestrator's
   prose, not a data axis. Rather than invent one, the view renders whatever stages have rows, in
   enum order, and folds the rest into one dimmed footer line (`7 idle stages`). On the live fleet
   that is 3–4 stages of the 11. Adding a real Investigate role is a taxonomy card, not this one.

3. **One line per card, per stage. Four row kinds, one grammar.** A row is a card's work *in that
   stage*; a card with a Plan in flight and a Custom queued appears under both stages, which is the
   point. Within a stage the order is **in flight → blocked → queued → ready** (the card's order,
   with Blocked pulled up beside in-flight because it is the row that needs a human). Each row is
   `[dot] #id title…` on the left, truncating, and a **fixed right cell** with the one fact that
   distinguishes the kind:

   | Kind | Dot | Right cell | Example |
   |---|---|---|---|
   | In flight (`Dispatched` / `Working`) | `active` | `<alias> <elapsed since dispatch>` | `grok-4.6 4m` |
   | Blocked | `warning` | `blocked`, or `no route` when `routingExhausted` | `blocked` |
   | Queued | `gray` | compact reason (table below) | `behind #288` |
   | Ready (Code stage only) | `success` | `ready <ago since readySince>` | `ready 3h` |

   Compact queue reasons, one or two words, phone-width:

   | `queueReason` | Right cell |
   |---|---|
   | `sharedCheckoutLease` | `behind #288` — the first holder's card citation via `citationHead`, else `behind ~1a2b3c4d`; `+N` when more holders |
   | `siblingLandInFlight` (new, S1b) | `landing #288` |
   | `routingPinNotBefore` | `after 14:00` (local clock, `formatClockTime`) |
   | `concurrencyCap` | `slots 6/6` |
   | `awaitingDispatch` | `queued` |

   No status words for in-flight rows: on the live fleet both in-flight tasks are `Dispatched` while
   visibly working (`lastActivityAt` advancing), so `Dispatched` vs `Working` is not a phone-grade
   distinction. The elapsed figure is `formatDuration(now − dispatchedAt)`; no "quiet for N min"
   threshold is applied (0031 decision 3: a threshold is `AttentionService`'s verdict, not the
   rail's). The drawer has `lastActivityAt` and everything else.

4. **The identifier is the line's anchor; the title is the card's, not the prompt's.** Task titles
   on this endpoint are the delegate's whole goal (200+ chars, `Plan CARD-0301 (phone-friendly …
   Read the card in full first…`). A row bound to a card prints `displayIdentifier(card.identifier)`
   (`#301`) and `card.title`. An unbound row prints `citationHead(title)` (`#56 rest-of-title` when
   the title cites a card, else the title) — the existing `workLineFormat` helper, so the phone
   keeps one citation grammar across Home and this tab.

5. **Stage header carries the counts and the pin; a one-line fleet strip carries the cap.**
   Header: `EXECUTE   1/1 running · 2 queued · 1 blocked · 3 ready`, right-aligned `pin grok-4.6`
   when `stage.routingPin` exists (`tierAlias(pin.modelLevel ?? 'High', pin.agentKind)`; kind only
   when level is null). `1/1` is `inFlightCount/recommendedInFlight`; `1 running` when the
   recommendation is null (Custom). `atOrAboveRecommendation` changes nothing but the number — the
   recommendation is advisory and the header says so by not colouring it. Strip above the stages:
   `2 of 6 slots · as of 12:54` (`inFlightAgainstCap`, `maxConcurrentTasks`, `asOf` local clock).
   Calm state: the strip reads `0 of 6 slots` and one line says `Nothing in the pipeline.`

6. **Tap = the surface that explains the line, one target per row.** Task rows (in flight, blocked,
   queued) open `TaskDrawer` on the same tab, and write `?task=<id>` so the link survives
   (`DelegationsBoard` / `DelegationsHistory` precedent). Ready rows link to the plan reader
   (`/plans?file=<deliverablePath>&ref=<deliverableRef>&task=<sourcePlanTaskId>` — the shape
   `TaskCard`'s `Read` link uses). No second tap target on a row; the drawer already links to the
   thread and the board.

7. **Two entry points on a phone, zero new chrome.** The nav drawer already reaches
   `/orchestrator` (Menu → Orchestrator → Pipeline tab). Additionally the mobile Home "In motion"
   band title gains a right-aligned `by stage ›` link to `/orchestrator?tab=pipeline` — one anchor
   on a `BandTitle`, no band content change (0002 / 0031 both left the bands untouched; this is a
   link, not a band). Nothing else on Home, the glance, the board or the desktop rail changes.

8. **Fix in passing, because the new line would lie otherwise: `tierAlias` gains the Codex
   ladder.** `taskVisuals.tierAlias` maps only Grok; a Codex task (15 % of the last week) reads
   `fable` / `opus` on every chip today, and its own comment says a third kind "must add its ladder
   HERE". Mirror `ModelLevelAliases.ForCodex` byte for byte (`gpt-5.6-sol` / `gpt-5.6-terra` /
   `gpt-5.6-luna`), tooltip `Frontier tier — Codex gpt-5.6-sol`. The phone right cell uses
   `compactAlias()` which drops the `gpt-5.6-` prefix (`terra 4m`); every other chip shows the full
   server string. **Consequence to state on the card:** existing delegations-board chips for Codex
   tasks change from `opus`/`fable` to the true model. The caller may drop this step; then the
   phone shows Claude names for Codex rows and the plan says so.

9. **Not a server aggregation change beyond the two gaps.** No new stage graph, no `Ready` status,
   no board / project filter (0304 decision 3: every row carries card identity; a filter is a client
   concern), no client-side join to `GET /api/agent-tasks` (the mobile page's unfiltered 815-row
   fetch is the thing CARD-0093 flagged — this tab must not add a second one).

---

## Ground truth (checked, not guessed)

| Claim | Evidence | Consequence |
|---|---|---|
| CARD-0093 is Done and is status-major / task-major | Card closed 2026-09-02 16:06 with S1–S3 shas; `taskVisuals.LANES` = queued / working / blocked / done; `DelegationsBoard` renders `buildTaskForest` roots | Not this screen; reuse the page and the drawer only |
| The pipeline endpoint already computes the stage view | `AgentTaskPipelineStatusService.GetAsync`: 11 stages in enum order, Check excluded; in-flight = Dispatched/Working with `lastActivityAt`; queued with lease/pin/cap reasons and `heldBy`; blocked separate; `ready` on Code only; stage pins; cap fields | Render; do not recompute |
| Live shape on 2026-09-03 02:54Z | cap 6, in flight 2 (Plan: this task, Code: CARD-0323 on Grok under a `Required` "execute with grok" pin), Deploy 4 blocked (3 unbound, duplicate titles), Custom 2 blocked (CARD-0039), 0 queued, 0 ready; 7 stages empty | Empty stages must fold; blocked must show; unbound rows need the citation fallback |
| Roles actually used, last 7 days (343 tasks) | Code 158 · Plan 111 · Debug 28 · Review 12 · Merge 11 · Deploy 9 · Custom 8 · Docs 6; no Investigate role exists | Decision 2 |
| Kinds actually used, last 7 days | Grok 192 · ClaudeCode 100 · Codex 51; Shared 185 · Worktree 143 · ReadOnly 15; 329 of 343 bound to a card | S1 fields are worth carrying; decision 8 |
| Rows lack kind / level / workspace | `AgentTaskPipelineInFlightDto(TaskId, ShortId, Title, Status, Card, AgentName, DispatchedAt, LastActivityAt)`; Queued and Blocked likewise; `TaskRow` already selects `Workspace` but not `AgentKind` / `ModelLevel` | S1 |
| Blocked rows have no blocked-since | `AgentTaskPipelineBlockedDto` carries `CreatedAt` only | Right cell says `blocked`, not an age; left open |
| CARD-0215 hold is not a queue reason | `AgentTaskDispatcher.IsSiblingLandInFlight` (`:2435`): latest of LandRequested / Landed / LandRefused is LandRequested, on a same-card kept Worktree branch; projection reasons are lease / pin / cap / awaiting only | S1b |
| Titles are goal prompts | Live in-flight titles 250+ chars, starting `Plan CARD-0301 (…` / `Execute the landed Plan for CARD-0323 (…` | Decision 4 |
| Client hook, key, invalidation, fixture exist | `usePipeline()` `agentTasks.ts:451`, key `['agentTasks','pipeline']` invalidated on `AgentTaskChanged` / `AgentQueueChanged` / `SessionFinished`; `client/src/test/fixtures/contract/pipeline.json` from `ContractSnapshotTests.Pipeline_status_contract` (in-flight Docs, queued Docs behind the lease, ready CARD-0031) | S2 recaptures after S1; S4's story seeds from it |
| Queue-reason wording exists | `homeTasksModel.QUEUE_REASON_LABEL` / `queueReasonLine` (rail sentences, UTC clock) | Reuse the enum, not the sentences — phone cells are two words |
| Phone grammar exists | `workLineFormat.citationHead` / `formatClockTime`; `WorkLine` = truncating line + dimmed sub + chevron, `UnstyledButton` `Link` | Reuse `citationHead`, `formatClockTime`, `displayIdentifier`; the row component is new (single line, right cell) |
| Mobile breakpoint and nav | `HomePage` switches at `(max-width: 48em)`; `Layout` drawer nav lists Orchestrator | Decision 7 |
| `/orchestrator` tabs | `OrchestratorPage.TABS = ['cards','delegations','history','attention','decisions']`, `keepMounted={false}`, tab in URL | Add `'pipeline'` |
| Mobile story convention | `MobileHome.stories.tsx`: `Box maw={390}`, `globals: { viewport: { value: 'iphone12' } }`; screenshots are a fixed 1440×900 page so the 390 px wrapper is what makes the PNG phone-shaped | S4 story |
| `tierAlias` lacks Codex | `taskVisuals.ts:44-46` (`kind === 'Grok' ? GROK_ALIASES : TIER_VISUALS.alias`); server `ModelLevelAliases.ForCodex` `:51-56` | Decision 8 |

---

## Screen anatomy (390 px, the iPhone 12 story viewport)

```
2 of 6 slots · as of 12:54
PLAN            1/1 running
● #301 Phone-friendly pipeline-stage v…   fable 2m
EXECUTE         1/1 running · 2 queued · 3 ready   pin grok-4.6
● #323 Herdr launch creates a new spac…   grok-4.6 4m
○ #239 Land queue not restart-safe        behind #288
○ #94 Backlog by priority                 slots 6/6
◆ #215 Plan branch ancestry               ready 3h
◆ #330 Output distiller seat              ready 1d
◆ #331 Land queue reconciliation          ready 2d
DEPLOY          4 blocked
! #32 Deploy pipeline: land-to-master …   blocked
! Run a real deploy through the pipeli…   blocked
OTHER           2 blocked
! #39 Provision the shared QA regressi…   blocked
7 idle stages
```

- Row: `UnstyledButton` (task rows) or `Link` (ready rows), `w="100%"`, `py 6`, `Group wrap="nowrap"`;
  left `Box minWidth:0 flex:1` → `Text size="sm" truncate` with a `ff="monospace"` dimmed identifier
  span; right `Text size="xs" c="dimmed"` `flexShrink:0` `tabular-nums`. The dot is an 8 px
  `Box` with `borderRadius:'50%'` and the kind's colour, `aria-hidden`; the row's `aria-label` is
  `Open #301 — running` / `— queued` / `— blocked` / `— ready`, so the kind is never colour-only.
- Stage header: `Text size="xs" fw={700} tt="uppercase"` (the `BandTitle` look from
  `MobileHomePage`), counts dimmed on the same line, pin right-aligned dimmed.
- One `Paper withBorder radius="md" px="xs"` per stage with `Divider` between rows (the "In motion"
  band's container), `Stack gap="sm"` between stages.
- Never `overflow-x`; every text node truncates or clamps; the right cell is at most ~14 characters
  (`gpt-5.6-terra` is why decision 8 shortens it on this line).
- Elapsed / ago figures re-render on a 60 s `useInterval` (the 0031 rail's tick), pinned in tests
  and the story by `Date.now`.

---

## Server design (S1, S1b)

**S1 — rows say what they run on.** `AgentTaskPipelineInFlightDto` and `AgentTaskPipelineQueuedDto`
gain `AgentKind AgentKind, AgentModelLevel ModelLevel, WorkspaceMode Workspace`;
`AgentTaskPipelineBlockedDto` gains `AgentKind, ModelLevel`. Append them **after** the existing
positional members (before the defaulted `RoutingExhausted` on Blocked) so every existing
constructor call and the HTTP test's field assertions stay valid. `TaskRow` gains `AgentKind` and
`ModelLevel` (its `Workspace` is already selected). `ToInFlight` / `ToQueued` / `ToBlocked` copy
them. The record's `Workspace` is the raw enum: the client owns the label (`WORKSPACE_LABEL`).

**S1b — `siblingLandInFlight` (droppable).** In `ToQueued`, after the lease check and before the
pin: when the task is card-bound and `Workspace == Worktree`, look up same-card tasks (any status,
`WorktreeBranch != null`, id ≠ this) and take, per sibling, the latest `AgentTaskEvent` of type
LandRequested / Landed / LandRefused; if that latest is LandRequested the reason is
`siblingLandInFlight` and `heldBy` names that sibling (task id, short id, title). This is the
dispatcher's `IsSiblingLandInFlight` predicate over the same events table, **without** the two Git
probes (`KeptBranchExistsAsync`, `IsAncestorOfBaseAsync`) the dispatcher adds — the projection
never contacts Git (0304 §S3). The approximation errs only in the window between a land finishing
and its `Landed` event being written, and errs *towards* naming the hold, which is the useful
direction: a stranded `LandRequested` (CARD-0331) is exactly what this reason exposes. Load the
events in one bounded query keyed by the queued rows' card ids (the same shape as
`LoadLastActivityAsync`). Precedence in `ToQueued`: lease → sibling land → pin → cap.

**Docs.** `docs/antiphon-api.md:239`: the `queueReason` list gains `siblingLandInFlight`, and a
clause "rows carry `agentKind` / `modelLevel` / `workspace`". One line in
`docs/orchestration-loop.md` where status reporting is described: the stage glance is
`/orchestrator?tab=pipeline`.

---

## Client design (S2–S5)

### Files

| File | Change |
|---|---|
| `client/src/api/agentTasks.ts` | TS mirrors: `agentKind`, `modelLevel`, `workspace` on in-flight / queued; `agentKind`, `modelLevel` on blocked; `AgentTaskPipelineQueueReason` gains `'siblingLandInFlight'` (with S1b) |
| `client/src/features/delegations/taskVisuals.ts` | `CODEX_ALIASES`; `tierAlias` / `tierTooltip` branch on `kind === 'Codex'` (decision 8) |
| `client/src/features/orchestrator/pipelineStageModel.ts` (new) | The pure half: `STAGE_LABEL`, `visibleStages(dto)` → `{ shown, idleCount }`, `stageRows(stage)` → ordered `PipelineRowView[]`, `stageCounts(stage)`, `compactQueueReason(row, stage)`, `rightCell(row, now)`, `rowLabel(row)`, `rowTarget(row)`, `compactAlias(level, kind)`, `fleetStrip(dto)` |
| `client/src/features/orchestrator/PipelineStagesPanel.tsx` (new) | The rendering: strip, stage groups, rows, drawer, empty and error states |
| `client/src/features/orchestrator/OrchestratorPage.tsx` | `TABS` gains `'pipeline'`; `Tabs.Tab value="pipeline"` with `TbTimeline`, label `Pipeline`, placed after Delegations; `Tabs.Panel` → `<PipelineStagesPanel />` |
| `client/src/features/home/MobileHomePage.tsx` | `InMotionBand` title row gains the `by stage ›` anchor (decision 7) |
| `client/src/features/orchestrator/PipelineStagesPanel.stories.tsx` (new) | `Orchestrator/Pipeline stages`, 390 px wrapper, iPhone 12 viewport, seeded from `pipeline.json` only |
| `client/src/test/fixtures/contract/pipeline.json` | Recaptured (S2) |
| `tests/Antiphon.E2E/ContractSnapshotTests.cs` | Scenario gains one Blocked Deploy row so the fixture — and therefore the story — shows all four row kinds |

### `PipelineRowView`

```ts
export type PipelineRowKind = 'inFlight' | 'blocked' | 'queued' | 'ready'
export interface PipelineRowView {
  key: string                       // taskId, or `ready:${card.id}`
  kind: PipelineRowKind
  identifier: string | null         // '#301' via displayIdentifier, null when unbound
  title: string                     // card.title, else citationHead(task.title)
  right: string                     // the right cell, already formatted against `now`
  color: string                     // STATUS_COLOR-derived: active | warning | gray | success
  target: { drawer: string } | { to: string }
  ariaLabel: string
}
```

`stageRows` keeps the server's order inside each kind (created-then-id; ready by `readySince`) and
concatenates in-flight, blocked, queued, ready. `visibleStages` drops a stage whose four
collections are all empty and counts it as idle. `rightCell` is the only place `now` enters; the
component passes one `now` per render tick.

### `PipelineStagesPanel`

- `usePipeline()`; `useInterval` 60 s for `now`; `useSearchParams` for `?task=`; `TaskDrawer`
  mounted once with `taskId` from the URL (the `DelegationsHistory` pattern).
- `isPending` → three `InlineSkeleton` rows under one header; `isError` → the dimmed
  "Couldn't load the pipeline — retrying." sentence (the mobile band's error tone); the panel
  never throws on a missing field (a server that predates S1 yields rows with no alias — render
  the elapsed alone).
- `data-testid`s: `pipeline-strip`, `pipeline-stage-<Role>`, `pipeline-row-<key>`,
  `pipeline-idle`, `pipeline-empty`.

---

## Slices

Sequential, one Execute delegate. S3 and S4 compile against the old fixture if S1/S2 lag; S4's
story needs S2's recapture to show the alias.

### S1 — Rows carry kind, level and workspace (server)

**Files:** `server/Application/Dtos/AgentTaskPipelineDtos.cs`,
`server/Application/Services/AgentTaskPipelineStatusService.cs`, `docs/antiphon-api.md`,
`tests/Antiphon.Tests/Application/AgentTaskPipelineStatusTests.cs`.

**Tests:** new `rows_carry_agent_kind_model_level_and_workspace` (one in-flight Grok/Frontier/
Worktree, one queued Codex/High/Shared, one blocked Claude/Medium — assert the three fields on each
row kind); `pipeline_route_is_literal_and_returns_the_advisory_contract` asserts
`json.ShouldContain("\"agentKind\"")` after seeding one in-flight row (or leave the empty-fleet
assertions and add the string check to the new test's HTTP twin — either, not both).

**Verify:** `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0301/ -- --treenode-filter "/*/Antiphon.Tests.Application/AgentTaskPipelineStatusTests/*"`

### S1b — `siblingLandInFlight` queue reason (server, droppable)

**Files:** `AgentTaskPipelineStatusService.cs` (`QueueReasonSiblingLandInFlight`, the events
query, the `ToQueued` arm), `docs/antiphon-api.md`.

**Tests:** `a_sibling_land_in_flight_is_the_queued_reason_after_the_lease` (same-card Worktree
sibling with latest event LandRequested → reason and `heldBy` = sibling; latest Landed → falls
through to the next reason; a lease holder present → lease wins); `a_shared_task_never_reports_a_sibling_land`.

**Verify:** the same filter as S1.

### S2 — Client API mirror, Codex alias, fixture recapture

**Files:** `client/src/api/agentTasks.ts`, `client/src/features/delegations/taskVisuals.ts`,
`taskVisuals.test.ts` (`tierAlias('Frontier','Codex') === 'gpt-5.6-sol'`, tooltip form),
`tests/Antiphon.E2E/ContractSnapshotTests.cs` (`PipelineBlockedTaskId` `dddddddd-…-0004`, Role
Deploy, Status Blocked, `PipelineQueuedAgentId`; keep it in `keepTaskIds`), then delete
`client/src/test/fixtures/contract/pipeline.json` and recapture.

**Verify:** `dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0301e2e/ -- --treenode-filter "/*/*/ContractSnapshotTests/Pipeline_status_contract"`
(Docker Desktop must be up for the Postgres testcontainer; `SharedApp` does not serve `client/dist`,
so no client build is needed for this one test). Then `pwsh -File scripts/test-client.ps1 TasksSection`
— the 0031 story and tests seed the same fixture and must still pass with the added fields and row.
Commit the new fixture with the seed change.

### S3 — The pure model

**Files:** `client/src/features/orchestrator/pipelineStageModel.ts`, `pipelineStageModel.test.ts`.

**Tests (pure, no render):** labels (`Code → Execute`, `Custom → Other`); `visibleStages` hides
empties and counts them; `stageRows` order across the four kinds and stability within one;
identifier/title for bound, unbound-with-citation, unbound-without; every queue reason's compact
cell including `+N` holders and the `~shortId` fallback; `after HH:MM` uses the injected clock;
`ready <ago>` and `<alias> <elapsed>` against a pinned `now`; `compactAlias` strips only the
`gpt-5.6-` prefix; `rowTarget` for task vs ready rows; `fleetStrip` wording; a row whose
`agentKind` is undefined (pre-S1 server) renders elapsed alone.

**Verify:** `pwsh -File scripts/test-client.ps1 pipelineStageModel`

### S4 — Panel, tab, story, screenshot

**Files:** `PipelineStagesPanel.tsx`, `PipelineStagesPanel.test.tsx`, `OrchestratorPage.tsx`,
`OrchestratorPage.test.tsx`, `PipelineStagesPanel.stories.tsx`, `docs/ui-screenshots/`.

**Tests:** panel — msw `GET /api/agent-tasks/pipeline` with a hand-shaped DTO (tests may; stories
may not): strip text, the shown stages and the idle line, a row per kind with its right cell, a
task row tap writes `?task=` and opens the drawer (mock `GET /api/agent-tasks/:id`), a ready row is
a link to `/plans?…`, 500 → the retrying sentence, empty fleet → `pipeline-empty`. Page — one case
beside the History one: `?tab=pipeline` renders the panel and does not mount Delegations.

**Story:** `Orchestrator/Pipeline stages` — `Box maw={390} mx="auto"`, `globals: { viewport: { value:
'iphone12' } }`, `Date.now` pinned to `2026-02-03T09:14:00Z` like `TasksSection.stories.tsx`,
QueryClient seeded with `agentTaskKeys.pipeline()` from `pipeline.json` and `agentTaskKeys.detail`
untouched (the drawer is closed in the story). Two stories: `Live` (the fixture: Docs in flight,
Docs queued behind the lease, Deploy blocked, CARD-0031 ready) and `Calm` (the same DTO with every
collection emptied in the story — derived from the fixture, not hand-written).

**Verify:** `pwsh -File scripts/test-client.ps1 PipelineStagesPanel`, `… OrchestratorPage`; then
`npm run screenshots -- pipelinestages` against the CDP Edge on 9222 and commit the PNGs + README.
Finally the real thing: with the stack up, open `http://localhost:17203/orchestrator?tab=pipeline`
in the browser-harness Edge at 390 × 844 and confirm no horizontal scroll, the live in-flight rows
with their aliases, and that a row tap opens the drawer.

### S5 — Entry point, docs, close

**Files:** `MobileHomePage.tsx` (+ one `MobileHomePage.test.tsx` case: the `by stage` link targets
`/orchestrator?tab=pipeline`), `docs/orchestration-loop.md` (one line), `docs/antiphon-api.md`
(if S1/S1b did not already), the card (close note with the shas, the tab URL, and the decision-8
chip change if it shipped).

**Verify:** `pwsh -File scripts/test-client.ps1 MobileHomePage`; then the full client suite once:
`pwsh -File scripts/test-client.ps1` and read the `CLIENT TESTS EXIT CODE` line.

---

## Test matrix

| Layer | Coverage |
|---|---|
| Pipeline service | Three new fields on every row kind; `siblingLandInFlight` inclusion, precedence under a lease, Landed/LandRefused fall-through, Shared never reports it |
| HTTP contract | Literal route unchanged; camel-case `agentKind` present |
| Contract fixture | Recaptured with the blocked row; 0031's `TasksSection` story/tests still green on it |
| Pure model | Labels, folding, ordering, identifier fallback, every compact reason, clock injection, alias shortening, targets, pre-S1 tolerance |
| Panel | Strip, stages, idle line, four row kinds, drawer via `?task=`, ready link, error, empty |
| Page | `?tab=pipeline` mounts the panel only |
| Mobile Home | The `by stage` link |
| Visual | Two stories at 390 px; PNGs committed; live browser check at 390 × 844 |

---

## What this card does not do

- Change `DelegationsBoard`, `DelegationsHistory`, the Home rail, the glance, the mobile bands'
  content, or the board.
- Add an Investigate role, a `Ready` status, a stage graph beyond Plan → Code, a board / project
  filter, hard WIP gating, or any dispatch behaviour (0304's out-of-scope list stands).
- Re-derive queue reasons, liveness, or stall verdicts on the client.
- Fetch `GET /api/agent-tasks` from this tab.
- Reply, retry, cancel or escalate from a row — the drawer has those.

## Left open, deliberately

1. **Blocked-since.** `AgentTaskPipelineBlockedDto` has `createdAt` only; the `Blocked` event's
   time is on the task's events. Worth a field when a phone user asks "how long has this been
   waiting" — not this card's question.
2. **Quiet-for marker on in-flight rows** (`lastActivityAt` older than N min). 0031 decision 3
   keeps thresholds in `AttentionService`; a phone dot could echo the feed's verdict via
   `livenessFor` once this tab fetches `/api/attention` — an extra poll this plan avoids.
3. **Tab list wrapping.** Six tabs wrap to two lines at 390 px. Acceptable; if it grates, the fix
   is `Tabs.List` scrolling horizontally, a page concern shared by all six.
4. **A per-card "ready" line on the Home rail's Up next group** — 0002 left it open pending this
   card's shape; the shape is now `ready <ago>` + plan link, so 0002's left-open item 1 can be
   taken as written.
5. **Model hold as a queue reason** (0031 left-open 1) — still open; the hold writes a `Held` event
   visible in the drawer.

## Estimate

| Slice | Time |
|---|---|
| S1 | 1 h |
| S1b | 1.5 h |
| S2 | 1 h (+ the E2E capture run) |
| S3 | 2 h |
| S4 | 3 h |
| S5 | 1 h |

About one working day for one Execute delegate; the client work is the bulk and is the shape Grok
has been executing under the current Code pin. Verification floor ≈ 40 min (four scoped Vitest
runs, one TUnit filter, one E2E capture, one screenshot run, one live browser check).
