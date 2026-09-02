# CARD-0092 — Running Sessions: the rail already answers the general case; the panel it names still says 0

**Date:** 2026-09-02 (Plan pass, task 20472347 — design only; no production code changed)
**Card:** CARD-0092 "Running Sessions only shows card-spawned work by design - delegate tasks and
sub-orchestrator children are invisible while actively running"
**Builds on:** [`2026-09-02-card-0002-home-tasks-section-plan.md`](2026-09-02-card-0002-home-tasks-section-plan.md)
(Home Tasks rail, Done today) and [`2026-09-02-card-0031-project-status-view-plan.md`](2026-09-02-card-0031-project-status-view-plan.md)
(liveness, elapsed, queue reasons, Done today). Neither read CARD-0092; this plan closes the loop
the card asked for ("a future Plan pass on either card should read the other first").

**Sources (verified this pass, head `ce44ab8b`, live 17202 on 2026-09-02):** CARD-0092,
CARD-0002 (Done, `9e571e86` S1 … `f2ee3580` S7), CARD-0031 (Done, `c041c350`, `bbe0224d`,
`7e540232`), CARD-0091 (Done), CARD-0040 (Done), CARD-0301 (Backlog), CARD-0094 (Backlog),
CARD-0304 (Review), `server/Application/Services/OrchestratorService.cs`,
`server/Application/Dtos/OrchestratorDtos.cs`, `server/Api/Endpoints/OrchestratorEndpoints.cs`,
`server/Application/Services/HomeTaskService.cs`, `server/Application/Dtos/HomeTaskDtos.cs`,
`server/Application/Services/AgentTaskService.cs`, `server/Application/Services/AgentTaskDispatcher.cs`,
`server/Domain/Entities/{AgentTask,AgentSession}.cs`, `server/Domain/Enums/{AgentTaskEnums,SessionStatus}.cs`,
`client/src/features/orchestrator/{OrchestratorPanel,OrchestratorPage}.tsx` and their tests,
`client/src/api/orchestrator.ts`, `client/src/features/home/tasks/{homeTasksModel.ts,TasksSection.tsx,TaskCard.tsx}`,
`client/src/features/delegations/{DelegationsBoard,TaskTree}.tsx`, `taskVisuals.ts`,
`client/src/features/attention/DecisionsPanel.tsx`, `tests/Antiphon.Tests/Application/OrchestratorServiceIntegrationTests.cs`,
`docs/agent-card-lifecycle.md`, `docs/antiphon-api.md`, and the live endpoints
`/api/orchestrator/state`, `/api/home/tasks`, `/api/agent-tasks?since=2026-08-20`.

---

## Verdict up front

**Partially satisfied. Do not close as superseded; narrow the card to the surface it names and
build S1–S2 (about one day).** The general ask — delegate work visible while it runs, and a
distinct place for work that is waiting on a human — is answered today by CARD-0002/0031 on Home
and by the Delegations and Decisions tabs of `/orchestrator`. The specific surface the card was
filed against, the **Running Sessions table and "Running" metric on `/orchestrator`'s default
tab**, still shows nothing, and it is the first thing an operator sees when they open the page
that is named for "what is the fleet doing right now".

Measured at the same instant on 2026-09-02:

| Surface | Showed |
|---|---|
| `GET /api/home/tasks` (Home rail) | Running 3 — CARD-0092 (Plan, this task), CARD-0090 (Code), CARD-0010 (Code), each a `Dispatched` delegate under its card; Needs you 2 (two `Blocked` delegates) |
| `GET /api/orchestrator/state` (Running Sessions) | `runningSessions: 0`, `running: []`, enabled, not paused |

### The three questions the brief asked

**1. Is the ask fully satisfied by CARD-0002/0031?** No. Satisfied for Home; not for the panel.
Half two ("waiting on a human, shown distinctly") is satisfied everywhere that matters — see below.

**2. Does the Home rail show sub-orchestrator children, or only top-level tasks?** Precisely:

- **Unbound children** (`CardId == null`, any depth) are items. `HomeTaskService.LoadUnboundTasksAsync`
  (`HomeTaskService.cs:148-150`) filters on `CardId == null && Role != Check` and nothing else, so a
  depth-3 worker is a top-level `Task` tile like its root. It is **not grouped** under its parent:
  `HomeTaskItemDto` carries no `ParentTaskId`, `RootTaskId` or `Depth`, and `TasksSection`/`TaskCard`
  nest nothing.
- **Bound children** are the normal case, because a child created by a bound orchestrator inherits
  the orchestrator's card (`AgentTaskService.cs:308`, `parent?.CardId ?? followUpCardId`). Every
  bound task folds into its card's **single worker line**: `RankCard` picks `openBound =
  bound.FirstOrDefault(IsOpenBound)` over a newest-first list (`HomeTaskService.cs:189`). An
  orchestrator with three working children shows the card in *Running* with **one** worker (the
  most recently dispatched child); the orchestrator itself and the other two children are not on
  the rail at all.
- The grouped view exists only on `/orchestrator?tab=delegations` (`buildTaskForest` +
  `TaskTree`, roots expanded, sub-orchestrators collapsed).
- **Live weight of this gap: nil so far.** `GET /api/agent-tasks?since=2026-08-20` returns 564
  tasks: 564 `Worker`, 0 `Orchestrator`; 563 at depth 0 and exactly one at depth 1 (a
  merge-conflict follow-up bound to CARD-0299). Sub-orchestrator chains are schema-real and
  practice-absent. The rail gap is latent; it gets one optional, droppable slice (S3).

**3. Is this a genuinely different surface?** Yes, for half one. `OrchestratorPanel.tsx` is the
`cards` tab of `OrchestratorPage.tsx`, and `cards` is the **default** tab (`OrchestratorPage.tsx:24`,
pinned by `OrchestratorPage.test.tsx` "puts the tab in the URL"). Its table is fed by
`OrchestratorService.GetStateAsync` (`:191-323`), whose row set is
`activeStatuses.Contains(s.Status) && s.CardId != null` (`:200-202`).

### Root cause, confirmed and extended

The card blamed the `CardId != null` filter. That is the proximate cause; the structural one is a
line further down the stack: **every delegate session is created with `CardId = null`**
(`AgentTaskDispatcher.cs:2136` for the normal launch, `:2290` for the boot-wedge relaunch), whether
or not its task is bound to a card. `AgentSession.CardId` means "started by the card-spawn path
(`CardService.SpawnAsync` / the orchestrator tick)"; it is not the CARD-0040 binding. The binding
is `AgentTask.CardId`, and the task points at its session through `AgentTask.AgentSessionId`
(set at `:2160` / `:2305`). The rail is correct because it joins through the task; the panel is
empty because it joins through the session. The fix is to make the panel join the same way — not
to backfill `AgentSession.CardId` (see "What this card does not do").

### Half two — "finished but waiting on a human": satisfied, nothing to build

| Signal | Where it shows today, distinctly from Running |
|---|---|
| Card in `NeedsDecision` | Home *Needs you* (reason `Decision`, red border, question line from `/attention`); `/orchestrator?tab=decisions` (`DecisionsPanel`: board context, whole question, `MoveMenu`); tab badge count; `/attention` |
| Delegate `Blocked` (bound or not) | Home *Needs you* (reason `Question`); Delegations tab *Blocked* lane; `/attention` `BlockedQuestion` with Reply |
| Card in `Review` | Home *To review* (longest-waiting first, cap 8 → board); the board column |
| Workflow gate `WaitingForHumanReview` | Home *Needs you* (reason `Gate`) |
| Parked queued messages (CARD-0091, cited by this card) | CARD-0091 is Done |

Home's group order is Needs you · Running · To review — the "different location" the user asked
for, with the running work in between. AGENTS.md's rule ("a decision belongs on the card
move/reopen revision and attention feed, never a new column or an alert sink") means this panel
gets **no** second section; its own page already has the Decisions tab as the attention feed's
decision altitude.

---

## Decisions

1. **The Running Sessions table becomes "active sessions doing orchestrated work"**, defined as
   the union of (a) sessions with `AgentSession.CardId != null` (card-spawn, unchanged) and (b) the
   current session of every `AgentTask` with `Status ∈ {Dispatched, Working}`, `Role != Check`,
   `AgentSessionId != null`. A cardless session with no such task — a human's interactive
   terminal — stays out; that is what the original comment meant and it stays true. `Blocked`
   tasks are **excluded on purpose**: their session may be alive, but the work is waiting on a
   human, which is half two's surfaces, not "genuinely running".

2. **A delegate row shows its task's card.** For rows from (b), `cardId`/`cardIdentifier`/
   `cardTitle`/`boardId`/`boardName` come from `AgentTask.CardId` (joined to `Cards`/`Boards`), so
   a bound delegate lists under the card it is working — the user's literal observation ("a card
   currently being worked by an agent doesn't show up") is what this fixes. Unbound tasks leave
   the card fields null and the table prints the task title in the Card cell.

3. **One projection, not three.** No new endpoint, no `AgentSession.CardId` backfill, no task tree
   widget on this panel. The state endpoint stays the *session* snapshot (live pty, last
   sequence, runtime, tokens) — the one fleet-wide place with the raw "is the pty alive and
   emitting" signal, which is exactly "genuinely running". The Delegations tab stays the *task*
   tree; the Home rail stays the per-project item view.

4. **Children are grouped by ordering and indent, not by a tree control.** The server emits rows
   in family order — each root by session `StartedAt` descending, then its present descendants in
   pre-order by `StartedAt` ascending — and a `depth` that is **relative to the nearest ancestor
   present in the list** (0 when none; a child whose orchestrator is Blocked shows at depth 0).
   The client indents the Card cell by `depth` with a `└` marker. Card-spawn rows are depth 0.
   `buildTaskForest` is not reused: it takes `AgentTaskSummaryDto` and the point of this table is
   to stay flat.

5. **Pause / Resume / Tick keep their meaning and say so.** They govern the card auto-dispatch tick
   (`OrchestratorService.PollTickAsync`), not the task pipeline. Once delegates appear in the table,
   an operator could reasonably press Pause expecting them to stop. One dimmed caption under the
   header states the split; the "Running" metric gets a subline `N card · M delegate`.

6. **Scope filter must not eat the new rows.** `ApplyScope(IQueryable<AgentSession>)`
   (`OrchestratorService.cs:299-306`) tests `s.Card.Board.TrackerKind`; for a cardless session that
   is a null navigation, SQL `NULL <> 1` is not true, and the row disappears whenever
   `InternalTrackerRepositoryPathPrefix` is set (unset live; set by every `OrchestratorServiceIntegrationTests`
   case). The predicate gains `s.CardId == null ||`. For delegate rows whose *task* has a card, the
   same predicate is applied to that card in memory, so the scope means the same thing on both
   halves of the union.

7. **Home rail: one optional slice, recommended deferred.** S3 adds `OpenWorkerCount` to the card
   item and a `+N more` suffix on the worker line linking to the Delegations tree for that root.
   It is the smallest honest closure of the bound-children gap in question 2. With zero
   Orchestrator-kind tasks in two weeks, my recommendation is to ship S1–S2 now and pick S3 up
   the first time a sub-orchestrator actually runs; it is written here so nobody has to plan it
   again.

---

## Ground truth (checked, not guessed)

Line numbers as of `ce44ab8b`.

| Claim in the card (2026-08-19) | Today | Consequence |
|---|---|---|
| Running Sessions filters `CardId != null` | `OrchestratorService.cs:200-202`, unchanged | Still the proximate cause |
| Delegate sessions are "almost certainly cardless" | Certainly: `AgentTaskDispatcher.cs:2136`, `:2290` set `CardId = null` unconditionally | Join through `AgentTask.AgentSessionId`, not through `AgentSession.CardId` |
| `OrchestratorRunningSessionDto` requires card fields | `OrchestratorDtos.cs:37-59`: `Guid CardId`, `string CardIdentifier`, … non-nullable; client mirror `orchestrator.ts:19-42` | Nullable card fields + a task ref (S1) |
| Sub-orchestrator children equally invisible | Same session shape, same `CardId = null`; and `Orchestrator`-kind tasks have not run since 08-20 (564 tasks, 0 Orchestrator, 1 depth-1) | Latent; ordering + depth (S1/S2), rail count optional (S3) |
| CARD-0031 covers both halves and should decide who ships | CARD-0031 shipped on Home today and did not mention `OrchestratorPanel` | This plan decides: the panel is this card's |
| CARD-0091 parked messages "shown to nobody" | CARD-0091 Done | Cited only |
| "Review" is a distinct non-terminal place | Home *To review*; `Review → Done` is still a human move (`docs/agent-card-lifecycle.md:93`) | Nothing to build |
| Card-spawn sessions exist to show | Live: `ownerSessionId` null on every card (CARD-0002 plan, re-checked); `runningSessions: 0`; `retryQueueLength: 0` | The table has been empty in practice; after S1 it shows the delegates |
| `GetStateAsync` has a server test | No — `OrchestratorServiceIntegrationTests` never calls it (grep) | S1 adds a projection test class |
| Panel test fixture is a contract capture | No — inline `stateResponse()` in `OrchestratorPanel.test.tsx:7-66`; no `orchestrator-state.json` under `client/src/test/fixtures/contract/` | Extend the inline fixture; no snapshot recapture |
| Active session statuses | `Starting, Running, Stopping` (`OrchestratorService.cs:731-732`) | A `Dispatched` task whose session died is *not* a row — the rail shows it with a `DeadSession` badge; consistent with "genuinely running" |
| Delegate token/turn data | Task rows have no `RunAttempt`; `AgentTask.TokensIn/TokensOut/CostUsd` (`AgentTask.cs:239-248`) | Delegate rows take tokens from the task, `turnCount` 0, `attemptNumber`/`phase` null |

---

## Wire shape after S1

```csharp
public enum OrchestratorSessionSource { Card = 0, Delegation = 1 }

public sealed record OrchestratorRunningTaskDto(
    Guid TaskId,
    string ShortId,            // DelegationReportFormatter.Short
    string Title,
    AgentTaskRole Role,
    AgentTaskStatus Status,    // Dispatched | Working
    AgentTaskKind Kind,        // Worker | Orchestrator
    Guid RootTaskId,
    Guid? ParentTaskId,
    string? AgentName);

public sealed record OrchestratorRunningSessionDto(
    Guid SessionId,
    OrchestratorSessionSource Source,          // NEW
    int Depth,                                 // NEW — relative to nearest ancestor in the list
    Guid? CardId,                              // was Guid
    string? CardIdentifier,                    // was string
    string? CardTitle,                         // was string
    Guid? BoardId,                             // was Guid
    string? BoardName,                         // was string
    OrchestratorRunningTaskDto? Task,          // NEW — null on Card rows
    string DefinitionName,
    string AgentKind,
    string Status,
    Guid? RunAttemptId,
    int TurnCount,
    int? AttemptNumber,
    string? Phase,
    DateTime StartedAt,
    DateTime LastSeenAt,
    DateTime? LastEventAt,
    long RuntimeSeconds,
    long TokensIn,
    long TokensOut,
    decimal CostUsd,
    bool Live,
    long LastSequence);

public sealed record OrchestratorStateDto(
    bool Paused, bool Enabled, DateTime GeneratedAt,
    int RunningSessions,                       // now card + delegate
    int RunningCardSessions,                   // NEW
    int RunningDelegateSessions,               // NEW
    int RetryQueueLength, … unchanged …);
```

Enums serialise as strings (project default). Existing card rows are unchanged except for the two
added fields and the nullable types; the client is the only consumer (`useOrchestratorState`).

### Projection (S1)

```
openTasks   = AgentTasks.AsNoTracking()
              .Where(t => t.AgentSessionId != null && t.Role != Check
                       && (t.Status == Dispatched || t.Status == Working))
              .Select(task ref + CardId + TokensIn/TokensOut/CostUsd + AgentSessionId)
sessionIds  = openTasks.Select(t => t.AgentSessionId!.Value)
sessions    = ApplyScope(AgentSessions …includes as today…)
              .Where(s => active.Contains(s.Status) && (s.CardId != null || sessionIds.Contains(s.Id)))
cards       = Cards.Where(c => openTasks card ids).Select(id, identifier, title, boardId, board name, tracker kind, project path)
rows        = card-spawn rows as today (Source = Card, Depth 0, Task null)
            ∪ delegate rows (Source = Delegation; card fields from the task's card, scope-checked; tokens from the task)
order       = families: roots by StartedAt desc; descendants pre-order by StartedAt asc; Depth relative
```

If a session is both card-owned and a task's current session (not possible from either launch
path today) it is one row with `Source = Card` and the task ref filled — assert, do not branch.

---

## Slices

### S1 — Server: widen the projection

**Files:** `server/Application/Dtos/OrchestratorDtos.cs`, `server/Application/Services/OrchestratorService.cs`
(`GetStateAsync` `:191-323`, `ApplyScope(IQueryable<AgentSession>)` `:299-306`), `docs/antiphon-api.md:313`
(one clause: "running = card-spawn sessions plus the current session of every Dispatched/Working
non-Check task; Blocked and interactive sessions excluded").

**Tests:** new `tests/Antiphon.Tests/Application/OrchestratorStateProjectionTests.cs`,
`[Category("Integration")]`, own isolated schema per test in the `HomeTaskServiceIntegrationTests`
style — *not* added to `OrchestratorServiceIntegrationTests`, which is `[NotInParallel]` because of
the tick sweeps; a read projection does not need that. Every assertion on rows the test created
(`running.Single(r => r.SessionId == mine)`, relative order via `IndexOf`; never counts over the
whole list, never `ShouldBeEmpty()`).

1. Active session + `Working` unbound task → one row: `Source == Delegation`, card fields null,
   `Task.ShortId`/`Role`/`Kind` set, `TokensIn` from the task, `TurnCount == 0`, `Phase == null`.
2. Active session + `Dispatched` task bound to own card → card fields equal the card's; board name
   carried.
3. Active session + `Blocked` task → absent. Same session after the task returns to `Working` →
   present.
4. Active session + `Role == Check` task → absent.
5. Active cardless session with no task at all → absent (the interactive case; pins the comment).
6. Card-spawn session (`AgentSession.CardId` set, a `RunAttempt` with `TokenUsage`) → row identical
   to today's fields, `Source == Card`, `Depth == 0`, `Task == null`.
7. Orchestrator task (Working) + two children (Working), all with active sessions → parent row
   immediately followed by both children at `Depth == 1`, children by `StartedAt` ascending; a
   third child whose session is `Stopped` is absent and does not disturb the order.
8. Child Working, parent `Blocked` → child present at `Depth == 0`.
9. `InternalTrackerRepositoryPathPrefix` set → cardless delegate row present; delegate row bound
   to an Internal-tracker card whose project path is outside the prefix absent; card-spawn row on
   such a card absent (today's behaviour, now pinned).
10. `RunningSessions == RunningCardSessions + RunningDelegateSessions` for the rows the test owns
    is not assertable on a shared DB — assert instead that each own row increments the matching
    counter relative to a baseline read taken before seeding.

**Verify:**
`dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0092/ -- --treenode-filter "/*/Antiphon.Tests.Application/OrchestratorStateProjectionTests/*"`
then `…/OrchestratorServiceIntegrationTests/*` (unchanged, must stay green — the scope predicate
change touches its world).

### S2 — Client: render the union honestly

**Files:** `client/src/api/orchestrator.ts` (mirror: `source`, `depth`, nullable card fields,
`task`, the two counters), `client/src/features/orchestrator/OrchestratorPanel.tsx`
(`:147` metric, `:157-200` table), `OrchestratorPanel.test.tsx` (`stateResponse` fixture).

Rendering:

- **Source chip** in the Card cell: `Card` / `Task`, `Badge size="xs" variant="outline"`, colours
  as the rail's `SOURCE_LABEL` (`active` / `violet`). Card cell: identifier + title when a card is
  present; else the task title with the short id dimmed. Indent by `depth` (`pl = depth * 16`)
  with a leading `└ ` when `depth > 0`.
- **Board** cell: name or `—`.
- **Agent** cell: `definitionName` + `agentKind`, plus `task.agentName` on delegate rows.
- **Phase** column becomes **Phase / status**: card rows `phase ?? status` as today; delegate rows
  `role · task status` (`Working`, `Dispatched`) with `STATUS_COLOR` from `taskVisuals`.
- **Turns**: `—` when `turnCount === 0 && source === 'Delegation'`.
- The short id on a delegate row is an `Anchor` to `/orchestrator?tab=delegations&task=<taskId>`
  — the drawer opens on arrival (`DelegationsBoard` reads `?task=`).
- Header caption (`Text size="xs" c="dimmed"`): "Pause and Tick govern card auto-dispatch. Delegate
  sessions are dispatched by the task pipeline and are listed here for visibility."
- "Running" metric value unchanged; subline `N card · M delegate`.
- Empty state text unchanged.

**Tests:** extend `stateResponse()` with (a) a delegate row bound to a card, (b) an unbound
delegate row, (c) a child row at `depth: 1`; assert: `Task` chip, task title in the Card cell for
(b), `code · Working` badge, `└` indent on (c), the anchor href for (a), the metric subline, the
caption; the two existing tests pass with the fixture's card row unchanged.

**Verify:** `pwsh -File scripts/test-client.ps1 OrchestratorPanel` and `OrchestratorPage`. Browser
(user rule: UI work is not done on tests alone; `client-mode.ps1 -Status` before trusting 17203):
open `/orchestrator` while at least one delegate is dispatched — the table lists it under its card
with a live `seq N` badge; press nothing; `?tab=delegations` still opens the tree.

### S3 — Home rail: `+N more` workers (optional, droppable)

**Files:** `server/Application/Dtos/HomeTaskDtos.cs` (`int? OpenWorkerCount` after `Worker`; cards
only; number of bound non-Check tasks in `{Queued, Dispatched, Working, Blocked}`),
`HomeTaskService.RankCard` (`bound.Count(IsOpenBound)`), `client/src/api/homeTasks.ts`,
`client/src/features/home/tasks/TaskCard.tsx` (worker line suffix `+N more` as an `Anchor` to
`/orchestrator?tab=delegations&task=<worker.taskId>`, `stopPropagation`),
`client/src/test/fixtures/contract/home-tasks.json` (recaptured by `ContractSnapshotTests`; the
delegation scenario at `:186` already builds orchestrator → sub-orchestrator → worker).

**Tests:** `HomeTaskServiceIntegrationTests` — own card with one open bound task → `1`; with an
orchestrator + two working children → `3`; settled tasks not counted. `TaskCard.test.tsx` — suffix
renders only when `> 1`, link does not fire `onOpen`.

**Verify:** `…/HomeTaskServiceIntegrationTests/*`; `dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0092/ -- --treenode-filter "/*/*/ContractSnapshotTests/*"`;
`pwsh -File scripts/test-client.ps1 features/home/tasks`. Do not run this concurrently with
another worker's E2E (the fixture recapture writes `client/src/test/fixtures/contract/`).

---

## What this card does not do

- **Backfill `AgentSession.CardId` for delegate sessions.** Tempting one-liner in the dispatcher,
  wrong in three places: the tick's candidate filter drops cards with an active session
  (`OrchestratorService.cs:523`), the reconcile path reads `card.OwnerSession` (`:398-481`), and
  `OrchestratorServiceIntegrationTests` asserts `AgentSessions.Count(s => s.CardId == card) == 0`
  for cards a delegate is working. `AgentSession.CardId` means "card-spawn owns this"; the CARD-0040
  binding is `AgentTask.CardId`. Both the rail and this fix join through the task.
- A second section, column, or count for decisions on this panel (AGENTS.md; the Decisions tab
  and `/attention` exist). No new `AttentionKind`, no alert sink.
- Changing Home's groups, order, caps, or which items land in *Needs you*; changing the
  Delegations tree; changing Pause/Resume/Tick semantics.
- A tree control on the panel, a third "running" endpoint, a SignalR event (5 s poll stays), or a
  contract fixture for `/api/orchestrator/state` (the inline MSW fixture is the panel's contract
  today; leave it).
- Making the Delegations tab the default (a UX call the user has not asked for; the honest
  default tab is enough).
- CARD-0301 (stage-major phone view), CARD-0094 (backlog by priority on this page), CARD-0304.

## Left open, deliberately

1. **S3 timing** — decision 7. Ship when an Orchestrator-kind task actually runs.
2. **Rail grouping of unbound children under their root.** Needs `ParentTaskId`/`Depth` on
   `HomeTaskItemDto` and nesting in `TasksSection`; CARD-0002 chose flat items on purpose. Revisit
   with S3 if unbound fan-outs become common (today: none).
3. **`Blocked` delegate sessions on this panel.** Excluded by decision 1. If an operator wants
   "alive but waiting" here too, it is one status added to the filter plus a `Blocked` badge — but
   it duplicates half two's surfaces, so it should be asked for, not assumed.
4. **Model hold as a queue reason** — CARD-0031 Left open 1, unchanged.

## Test matrix

| Layer | Test | Command |
|---|---|---|
| Server | `OrchestratorStateProjectionTests` (10 cases) | `dotnet run --project tests/Antiphon.Tests --property:OutputPath=bin-card0092/ -- --treenode-filter "/*/Antiphon.Tests.Application/OrchestratorStateProjectionTests/*"` |
| Server regression | `OrchestratorServiceIntegrationTests` unchanged | same, `OrchestratorServiceIntegrationTests` |
| Client | `OrchestratorPanel.test.tsx`, `OrchestratorPage.test.tsx` | `pwsh -File scripts/test-client.ps1 features/orchestrator` |
| S3 server | `HomeTaskServiceIntegrationTests` (+2) | same runner, `HomeTaskServiceIntegrationTests` |
| S3 contract | `ContractSnapshotTests` `home-tasks.json` recapture | `dotnet run --project tests/Antiphon.E2E --property:OutputPath=bin-card0092/ -- --treenode-filter "/*/*/ContractSnapshotTests/*"` |
| S3 client | `TaskCard.test.tsx` | `pwsh -File scripts/test-client.ps1 TaskCard` |

Build to `--property:OutputPath=bin-card0092/` (forward slash) while the daemons hold `bin/`;
delete every `bin-card0092` directory before finishing. Do not run the whole `Antiphon.Tests`
assembly for these classes.

## Sequencing, estimate, risks

**Order:** S1 → S2, one landing, Shared workspace is fine for one worker (nothing here touches
the contract fixture directory until S3). S3 separately, if at all. **Estimate:** S1 0.5 d,
S2 0.5 d, S3 0.5 d.

| Risk | Disposition |
|---|---|
| Scope predicate silently drops the new rows when the prefix is set | Decision 6; S1 case 9 pins both halves |
| Operator presses Pause expecting listed delegates to stop | Decision 5 caption; Pause semantics unchanged |
| A stale `Dispatched` task with a live-but-idle session lists forever | It lists while the session is `Starting/Running/Stopping`, which is what "genuinely running" means here; the liveness sweeps (CARD-0021/0003/0294) settle the task and the row leaves with it; the rail's verdict badge is the interpreted view |
| `EF` translation of `sessionIds.Contains(s.Id)` with hundreds of ids | Open tasks are bounded by real work (three today, cap `MaxConcurrentTasks`); fine |
| Two rows for one session | Not reachable from either launch path; S1 asserts single-row per `SessionId` on the union |
| Client breaks on the nullable card fields somewhere else | `useOrchestratorState` has one consumer (`OrchestratorPanel.tsx`); `grep OrchestratorRunningSessionDto client/src` to confirm at build time |
| Table gets wide | `miw={820}` already scrolls inside `ScrollArea`; the chip and indent add ~40 px |

## Execution notes

- Join through `AgentTask.AgentSessionId`; never through `AgentSession.CardId` for delegate rows.
- `Depth` is *relative to rows present*, not `AgentTask.Depth`; S1 case 8 pins it.
- Keep `Role != Check` on the task side of the union, the same exclusion every other projection
  applies.
- Keep the empty-state text and the retry-queue table byte-identical; the panel tests rely on them.
- Update `docs/antiphon-api.md:313` in S1, not S2, so the API doc and the wire shape land together.
- When closing the card, record the verdict on the move revision: "narrowed to the `/orchestrator`
  Running Sessions table; Home rail (CARD-0002/0031) and the Decisions/Delegations tabs already
  cover the general ask and the waiting-on-human half."
