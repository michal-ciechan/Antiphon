# 010 — Unified Tasks section on the home rail

**Status: proposed** (design only — nothing here is implemented)
**Replaces**: the flat delegations-only row list in `client/src/features/home/ProjectTasksPanel.tsx`
**Card**: CARD-0002

## Summary

The home page gets one card-based **Tasks** section in the left rail, below the agents
(agents ≈ one third of the rail height, tasks ≈ two thirds). A task card represents
**either** a board Card (`CARD-nnnn`) **or** a delegated AgentTask, with a chip saying
which. Cards are grouped by what a human scanning the rail actually asks: *what needs
me*, *what is running*, *what is next*, *what just finished*. When an agent is actively
working an item, the agent renders directly below that item's card. Detail lives in a
modal opened from the card; a kebab menu carries the per-item actions.

**Recommendation on the central question: (a) a unified READ view** — a new server-side
projection endpoint (`GET /api/home/tasks`) over both tables, with each pipeline keeping
its own storage, state machine, retry and watchdog semantics. Section 3 justifies this
and prices option (b).

---

## 1. What exists today (verified against the code, 2026-08-09)

### Two disjoint record systems

| | Board Cards | Delegated AgentTasks |
|---|---|---|
| Entity | `Card` + `RunAttempt` + `RetrySchedule` + card-bound `AgentSession` (`AgentSession.CardId`) | `AgentTask` (+ `AgentTaskEvent`); sessions created with `CardId = null` |
| Status enum | `CardStatus`: `Backlog, InProgress, Review, Done, Blocked, Canceled` — **derived from the board column** (`BoardColumn.CardStatus` copied on every move) | `AgentTaskStatus`: `Queued, Dispatched, Working, Blocked, Succeeded, Failed, Canceled` — set directly |
| Workflow overlay | `CardWorkflowRunStatus` (`Queued, Running, WaitingForHumanReview, Completed, Failed, Canceled`) + `CurrentStage.Name` on the active run | none |
| Dispatch | `CardService.SpawnAsync` → `OrchestratorService.TryClaimCardAsync` (claims via `OwnerSessionId`) → `AgentSessionLaunchQueue` | `AgentTaskDispatcher.TickAsync` (5s poll): concurrency cap, scope-glob leases, warm-pool reuse, per-root cost ceiling |
| Retry | `RetrySchedule` entity, `MaxAttempts = 3`, exponential backoff via `RetryScheduler` | in-place `Attempt++` on the same row (`AgentTaskService.RequeueAsync`), `MaxAttempts = 2`, prior `Result`/`FailureReason` kept as handoff |
| Stall handling | `RunAttemptStallDetector` (`LastEventAt` idle → `RunPhase.Stalled`, session Failed) + `WatchdogService` (screen-regex auto-respond) + `OrchestratorService.ReconcileAsync` | `AutoEscalateStalledAsync` (transcript recency → tier bump), `FailNeverStartedAsync` (delivery-evidence watchdog — never escalates) |
| Who ran it | `Card.OwnerSessionId`, `Card.AssignedAgentId`, `Agent.CurrentCardId`, `RunAttempt.AgentSessionId` | `AgentTask.AgentId/AgentSessionId` (plain columns, no navs) + `AgentName` denormalised snapshot (ephemeral agent rows are deleted on settle) |
| Rendered by | board view (`client/src/features/board/*`), `CardModal` | delegations view (`client/src/features/delegations/*`), `TaskDrawer` |
| Needs-human state | `CardStatus.Review`, `CardStatus.Blocked`, `CardWorkflowRunStatus.WaitingForHumanReview` | `AgentTaskStatus.Blocked` ("the delegate asked a question — it needs an answer, not a retry"; the question is in `Result`, detected by `AgentTaskReplyService.LooksLikeAQuestion`) |

**No server code joins the two.** `AgentTasks` is touched only by `AgentTaskService`,
`AgentTaskReplyService`, `AgentTaskDispatcher`, `AgentTaskEndpoints` and migrations.
The only client-side bridge is directory-keyed grouping in
`client/src/features/home/projectGrouping.ts` — and it covers agents + AgentTasks only,
not cards.

### The surface being replaced

`ProjectTasksPanel.tsx` is mounted in the **right dock's "Tasks" tab** on
`client/src/features/home/HomePage.tsx` (not the left rail). It fetches only
`GET /api/agent-tasks`, filters by the selected project's directory keys, and renders
flat rows in two sections ("In flight" / "Done"). Cards are absent entirely. The left
rail today is a 240px `Paper` holding only `AgentRail`.

### Relevant API surface

- Cards have **no flat list endpoint** — reads go through `GET /api/boards/{id}`
  (`BoardDetailDto` → `columns[].cards[]`). The client's "all cards" view does
  `Promise.all` over every board (`useAllBoardDetails`).
- AgentTasks have `GET /api/agent-tasks` → `AgentTaskSummaryDto[]` (no `Goal`/`Result`
  on the summary; detail via `GET /api/agent-tasks/{id}`).
- Agents: `GET /api/agents` → `AgentSummaryDto[]` incl. `currentCardId`, `working`,
  `liveSession`, `queueLength` — already polled every 5s by `useAgentList()`.

### Related work in flight

`feat/card-task-df922860` (commit `ff7227d`, **not yet on master**) fixes the
card-sits-in-queue-after-Review respawn loop: `CardLifecycleTransitions.DequeueFinishedCardAsync`
dequeues a finished card at every transition site, and `AgentControlService.ResolveStartCardAsync`
skips non-spawnable cards defensively. Two consequences for this design:
it is fresh evidence of what the duplication costs (section 3), and it means the
projection here must **not** treat a queue row as proof of outstanding work — group
membership below is computed from `CardStatus`, never from `AgentQueuePosition`.

---

## 2. UX design

### 2.1 Layout

The left rail widens from 240px to **300px** (card content with chips, a kebab and an
agent sub-row does not fit legibly in 240; the center `FilesReviewPanel` is `flexGrow: 1`
and absorbs the difference). The rail becomes a flex column of two `Paper`s:

```
┌─ left rail (300px) ─────────────┐
│ AGENTS                  manage  │  ~1/3 height:
│  ● antiphon-opus   [Working]    │  style={{ flex: '0 0 auto', maxHeight: '33%',
│  ● antiphon-fable  [Review]     │           overflowY: 'auto' }}
│  ○ scratch                      │
├─────────────────────────────────┤
│ TASKS                    board→ │  ~2/3 height:
│  NEEDS YOU (2)                  │  style={{ flex: 1, minHeight: 0 }}
│  ┌───────────────────────────┐  │  inner Stack overflowY: 'auto'
│  │ …task cards…              │  │
└─────────────────────────────────┘
```

The right dock loses its "Tasks" tab (the panel it hosted is replaced by this section);
the dock becomes the chat panel alone. `ProjectTasksPanel.tsx` is **deleted**.

The section is scoped exactly as the old panel was: items are filtered client-side by
the selected project's `dirKeys` (main checkout + workspaces), using the existing
`normalizeDir` from `projectGrouping.ts`.

### 2.2 Groups

Four groups, rendered in this order, each with the existing uppercase-label + count-badge
section style. Empty groups other than **Needs you** and **Running** are collapsed to
nothing; those two always render (with an empty-state line) because their absence is
itself information.

| Group | Board Card qualifies when | AgentTask qualifies when |
|---|---|---|
| **Needs you** | `status == Review`, or `workflowRunStatus == WaitingForHumanReview`, or `status == Blocked` | `status == Blocked` |
| **Running** | `status == InProgress && (ownerSessionId != null \|\| workflowRunStatus == Running)` | `status ∈ {Dispatched, Working}` |
| **Up next** | `status == Backlog`, or `status == InProgress` with no live claim and no running workflow | `status == Queued` |
| **Done** | `status ∈ {Done, Canceled}` (recent only — see §4.3) | `status ∈ {Succeeded, Failed, Canceled}` (recent only) |

**Needs you** gets visual weight the other groups don't: a `warning`-colored left border
on each card (3px, matching the `ownerSessionId` green-border convention in
`BoardCard.tsx`) and a `warning` badge naming the reason (`Review` / `Gate` / `Blocked` /
`Question`). This is the one group where the UI is allowed to shout.

### 2.3 Task card anatomy

```
┌──────────────────────────────────────┐
│ CARD-0002  [Card]        [Review] ⋮  │  ← identifier, source chip, state, kebab
│ Unified Tasks section on the home…   │  ← title, lineClamp 2
│ stage: implement       $1.23         │  ← workflow stage (cards) / cost (tasks)
│ └ ● antiphon-opus  [Working]         │  ← active agent, only when one is on it
└──────────────────────────────────────┘
```

- **Source chip**: `Badge size="xs" variant="outline"` — `Card` (color `active`) or
  `Task` (color `violet`, matching the delegation tier palette). This is the
  discriminator made visible.
- **State**: the native status verbatim (`Review`, `Working`, `Queued`, …) as a
  `Badge variant="light"` colored by the merged `STATE_COLOR` map (§5.3) — the same
  colors `taskVisuals.STATUS_COLOR` uses today so nothing changes meaning.
- **Cards** additionally show `currentWorkflowStageName` when set (the card requirement
  names this field explicitly); `workflowRunStatus` supplies the `Gate` needs-you reason
  but is otherwise not rendered (it is rendered nowhere in the client today either).
- **Tasks** additionally show the tier badge (`TierBadge` from
  `client/src/features/delegations/TaskChip.tsx`, already exported) and
  `formatCost(costUsd)`.
- **Blocked tasks** show the first line of the delegate's question (`question` field,
  §4.2) in `Text size="xs" lineClamp={1}` — the rail should answer "what is it asking"
  without opening the modal.
- **Active agent sub-row**: when an agent is actively on the item, a compact row nested
  under the card body — `TbTerminal2` icon + agent name + the working/review badge. The
  join is **client-side** against `useAgentList()` data (already fresh at 5s):
  `agents.find(a => a.currentCardId === item.id)` for cards, `a.id === item.agentId`
  for tasks with a live status. Clicking it selects that agent in the rail above
  (same `onSelect` the `AgentRail` uses), so "what is this agent doing" and "who is on
  this task" become the same gesture.

### 2.4 Kebab menu

`Menu` + absolutely-positioned `ActionIcon` with `TbDotsVertical`, copied structurally
from the one existing kebab (`AgentsPage.tsx:170-206` — menu positioned *outside* the
card's `UnstyledButton` so opening it never triggers the card click).

| Item | Card source | Task source |
|---|---|---|
| Open | ✓ (opens modal — same as clicking the card) | ✓ |
| Open board / Open delegations | `/boards/{boardId}?card={id}` | `/orchestrator?tab=delegations&task={id}` |
| Spawn session | ✓ when spawnable (`ownerSessionId == null` and status not terminal) — `POST /api/cards/{id}/spawn` | — |
| Answer… | — | ✓ when `Blocked` (opens modal with the answer box focused) |
| Retry | — | ✓ (disabled when `Queued`) |
| Escalate | — | ✓ (disabled at `Frontier`) |
| Cancel | — | ✓ (disabled when settled) |

Card-side write actions beyond spawn (move column, approve) stay in the modal — the
board's move semantics need the column picker and concurrency token, which a menu row
can't carry honestly.

### 2.5 Modal

Clicking a card opens a **modal** (per the card's requirement), routed by source:

- **Card** → the existing `CardModal` (`client/src/features/board/CardModal.tsx`),
  unchanged. It needs `boardId` + a `CardDto`; the opener fetches `useBoard(boardId)`
  lazily on open and finds the card (the modal already handles `card: null` by
  rendering nothing while loading).
- **Task** → a new `DelegationTaskModal` (`Modal size="xl"`), whose body is the
  **extracted** content of today's `TaskDrawer`. Refactor: move the drawer's body into
  an exported `TaskDetailBody({ taskId, onClose })` in
  `client/src/features/delegations/TaskDetailBody.tsx`; `TaskDrawer` becomes a thin
  `Drawer` wrapper around it (delegations board keeps its drawer UX), and the new modal
  wraps the same body. All actions (Retry/Escalate/Cancel/Answer) come along for free,
  including the "The delegate asked" + "Answer it" flow for Blocked.

---

## 3. The central question: unified read view vs storage merge

### Recommendation: **(a) unified READ view.** Confidence: high.

Ship a projection — a new read-only endpoint that maps both tables into one presentation
model — and change **zero** rows, state machines, or watchdogs.

**Why:**

1. **The feature is a presentation problem.** Every requirement on CARD-0002 (grouping,
   chips, agent-below-card, needs-human visibility, kebab, modal) is satisfiable from
   data both pipelines already expose. Nothing in the requirements needs the two record
   systems to share storage.

2. **The disjointness is load-bearing, not accidental.** `AgentTask`'s class doc states
   the design intent: *"Deliberately NOT a `Card`: cards carry board columns, tracker
   sync, workflow definitions and a 1:1 worktree, which is far too much for 'run the
   test suite'. Tasks are cheap, nest, and can be created in bulk."* Feature 007 (the
   delegation design of record) chose this on purpose. A merge reverses a deliberate
   architectural decision as a side effect of a UI feature.

3. **The semantics genuinely differ — a merge must either break one side or carry both.**
   Concretely, a single record system would have to reconcile:
   - `CardStatus` is *derived from board-column membership* (`ApplyColumnMove` copies
     `BoardColumn.CardStatus`); AgentTasks have no board. A merged entity needs optional
     column membership, which unpicks `CardService.MoveAsync`, the `CardStateMachine`
     table, board rendering, and drag/drop.
   - `RunAttempt.CardId` is **required**; delegation retries are in-place `Attempt++`
     with no attempt rows. Either the dispatcher starts writing RunAttempts (and the
     stall detector, retry scheduler, and orchestrator reconcile must learn to ignore
     them or handle them), or cards drop RunAttempt (rewriting the orchestrator, the
     stall detector, and the diff/review surfaces that hang off attempts).
   - Two retry systems with different semantics: exponential-backoff `RetrySchedule`
     (max 3) vs in-place requeue that deliberately preserves `Result` as the next
     attempt's handoff (max 2, human retry raises the cap).
   - Two stall regimes with different *philosophies*: card stalls fail the attempt and
     back off; task stalls escalate the model tier, and delivery failures explicitly
     never escalate ("a bigger model can't fix an undelivered brief").
   - Ephemeral agents + warm-pool reuse vs durable agents with queues.
   - Status vocabularies that don't inject into each other: cards have no
     `Dispatched`, tasks have no `Review`; task `Blocked` means "asked a question",
     card `Blocked` means "stuck".

4. **Fresh evidence prices the alternative honestly — in both directions.** The
   df922860 respawn-loop fix is exactly the class of bug the duplication causes
   (two notions of "finished" drifting apart), which is the argument *for* an eventual
   merge. But look at the fix: ~400 lines, six transition sites, one batching subtlety,
   232 lines of tests — inside **one** pipeline, changing **no** storage. A storage
   merge is dozens of such seams at once, on the two most load-bearing state machines
   in the product, with a live-data migration underneath. That is a multi-week project
   with real regression risk, and its user-visible payoff over (a) for this feature is
   zero.

5. **(a) is not a dead end — it is the first step of (b) if (b) ever earns its cost.**
   The projection defines the unified vocabulary (source, group, needs-human reason,
   who-ran-it) as a *contract*. If a future storage merge happens, `HomeTaskService`
   swaps its query and the client does not change. Strangler pattern, not a fork.

### What (b) would cost, itemised

Schema: either Cards gain delegation columns (tier, escalation, scope glob, root/parent,
token hash, reply-to, cost roll-up…) or AgentTasks gain board columns — plus a migration
over live data and a backfill for the other side's history. Code: `CardService`,
`CardStateMachine`, `CardLifecycleTransitions`, `OrchestratorService`,
`AgentSessionLaunchQueue`, `RetryScheduler`, `RunAttemptStallDetector`,
`AgentTaskDispatcher`, `AgentTaskService`, `AgentTaskReplyService`, both endpoint
groups, both DTO families, the SignalR event set, both client feature areas, tracker
sync (`ExternalTrackerSyncService`), and the in-flight card-task-file-sync spec
(`docs/superpowers/specs/2026-08-09-card-task-file-sync.md`). Tests: essentially every
integration suite named in §7. Estimate: 3–6 weeks of focused work before the UI even
starts, against ~3–5 days total for (a).

### What (a) costs (accepted)

- Grouping/needs-human mapping lives in one more place (`HomeTaskService`) and must be
  extended when either status enum grows. Mitigated: the mapping functions switch
  exhaustively over the enums, so a new member is a compile error, not a silent gap.
- Actions still fan out to per-pipeline endpoints. Accepted — the chip tells the user
  which system they're in, and the kebab/modal differ by source anyway.
- The duplication itself (two watchdogs, two retries) remains. True — and out of scope
  for a UI card. df922860 shows those seams are fixable incrementally where they hurt.

---

## 4. Server design

### 4.1 Endpoint

```
GET /api/home/tasks  →  200 IReadOnlyList<HomeTaskItemDto>
```

New files:
- `server/Api/Endpoints/HomeTaskEndpoints.cs` — route group `/api/home`, tag `"Home"`;
  registered in `server/Program.cs` beside the other `Map*Endpoints` calls.
- `server/Application/Services/HomeTaskService.cs` — the projection.
- `server/Application/Dtos/HomeTaskDtos.cs` — the DTOs below.

No parameters in v1. The list is global (all projects); the client filters by the
selected project's directory keys exactly as `ProjectTasksPanel` does today. Rationale:
the project⇄directory grouping (worktree merging, normalisation) lives client-side in
`projectGrouping.ts` and duplicating it server-side buys nothing at current data sizes
(the Done window is capped, Cards and AgentTasks number in the dozens).

### 4.2 DTOs (C#)

```csharp
public enum HomeTaskSource { Card = 0, Delegation = 1 }
public enum HomeTaskGroup { NeedsHuman = 0, Running = 1, Next = 2, Done = 3 }

/// <summary>
/// One unified work item for the home Tasks section: a projection of EITHER a board
/// Card OR a delegated AgentTask. Read-only by design — actions go to the owning
/// pipeline's endpoints, discriminated by Source.
/// </summary>
public sealed record HomeTaskItemDto(
    // "card:{id:N}" or "task:{id:N}" — globally unique, stable React key.
    string Key,
    HomeTaskSource Source,
    Guid Id,
    // CARD-nnnn for cards; 8-char short id for delegations.
    string Identifier,
    string Title,
    HomeTaskGroup Group,
    // The native status name verbatim ("Review", "Working", …) — never remapped,
    // so the UI vocabulary stays identical to the board and delegations views.
    string State,
    bool NeedsHuman,
    // "Review" | "Gate" | "Blocked" | "Question" — the badge text; null unless NeedsHuman.
    string? NeedsHumanReason,
    // First line of the delegate's question (trimmed, ≤200 chars) when a delegation
    // is Blocked-with-question; null otherwise.
    string? Question,
    // Cards only: the active workflow run's stage/status.
    string? StageName,
    CardWorkflowRunStatus? WorkflowRunStatus,
    // Delegations only.
    AgentModelLevel? ModelLevel,
    AgentModelLevel? EscalatedFrom,
    decimal? CostUsd,
    // Cards only.
    int? Priority,
    Guid? BoardId,
    // Who is (or was) on it. AgentName survives ephemeral-agent deletion (snapshot).
    Guid? AgentId,
    string? AgentName,
    Guid? AgentSessionId,
    // Directory linkage for the client's project filter (same fields the delegation
    // summary exposes; for cards, derived — see §4.4).
    string? WorkingDirectory,
    string? RepoPath,
    string? WorktreePath,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);
```

`State` stays a string on purpose: the alternative — a merged status enum — is exactly
the vocabulary collision §3 avoids. The typed fields the UI branches on are `Source`,
`Group`, `NeedsHuman` and `WorkflowRunStatus`.

### 4.3 Projection rules (the normative spec)

**Cards** — query `db.Cards` with `Select` projection (no tracking, no heavy includes):
`ActiveWorkflowRun.Status`, `ActiveWorkflowRun.CurrentStage.Name`, `AssignedAgent`
(id/name/workingDirectory), `CurrentWorktree.Path`, and the latest session's `Cwd`
(`AgentSessions.OrderByDescending(s => s.CreatedAt).Select(s => (Guid?)s.Id / s.Cwd).FirstOrDefault()`).

Group mapping (evaluate top to bottom, first hit wins):

```
Review                                   → NeedsHuman, reason "Review"
WorkflowRunStatus == WaitingForHumanReview → NeedsHuman, reason "Gate"
Blocked                                  → NeedsHuman, reason "Blocked"
InProgress && (OwnerSessionId != null
               || WorkflowRunStatus == Running) → Running
Backlog, or InProgress otherwise         → Next
Done, Canceled                           → Done
```

`AgentQueuePosition` is deliberately not consulted (a queue row is not proof of
outstanding work — see df922860).

**Delegations** — query `db.AgentTasks` directly (same shape `AgentTaskService.GetAllAsync`
projects, plus `Result` for the question):

```
Blocked                → NeedsHuman, reason "Question"; Question = first non-blank line
                          of Result, trimmed to 200 chars (null if Result is null —
                          the conflict/cost-ceiling Blocked paths put text in
                          FailureReason instead; surface that as Question fallback)
Dispatched, Working    → Running
Queued                 → Next
Succeeded, Failed, Canceled → Done
```

**Done window**: only items with `CompletedAt >= now − 7 days`, most recent **20**
combined across both sources after merging. Everything older lives on the board /
delegations views. (The client shows at most 12, matching today's `DONE_LIMIT`.)

**Ordering** (the endpoint returns the list fully ordered; the client must preserve
order and only filter):

1. By `Group` ascending (NeedsHuman, Running, Next, Done).
2. Within **NeedsHuman**: waiting-since ascending — the item that has waited longest
   for a human is first. Waiting-since := `CompletedAt` for Blocked delegations (when
   the question was asked), `UpdatedAt` for cards.
3. Within **Running**: `StartedAt ?? DispatchedAt ?? CreatedAt` descending (freshest
   activity first).
4. Within **Next**: `CreatedAt` ascending (matches dispatcher order and queue intuition).
5. Within **Done**: `CompletedAt` descending.
6. Ties: `Key` ascending (stability).

### 4.4 Card directory linkage (a real gap, resolved pragmatically)

Cards carry no directory. The projection derives one:
`AssignedAgent.WorkingDirectory` → else latest session's `Cwd` → else null;
`WorktreePath = CurrentWorktree.Path`. A card that has never been assigned or run has
`WorkingDirectory = null`, and the client's project filter will drop it — meaning a
brand-new unassigned Backlog card is **not** visible on the home rail until an agent or
session touches it. Accepted for v1: such a card belongs to the board view, and the
section footer links there. (The alternative — a Project→directory mapping — is the
card-task-file-sync spec's territory; do not duplicate it here.)

### 4.5 Live updates

No new SignalR events. `HomeTaskService` computes on read; freshness comes from
invalidating the query on the existing broadcast events (§5.4). Everything that changes
an input to the projection already emits one of: `CardChanged`, `BoardChanged`,
`AgentTaskChanged`, `AgentQueueChanged`, `RunAttemptChanged`, `SessionFinished`,
`AgentChanged` (all published to **all** clients — the session-group-scoped events are
not needed).

---

## 5. Client design

### 5.1 Files

| File | Change |
|---|---|
| `client/src/api/homeTasks.ts` | **new** — types below, `homeTaskKeys`, `useHomeTasks()` (`GET /api/home/tasks`, `refetchInterval: 15_000` as the dropped-connection fallback, same as agent-tasks) |
| `client/src/features/home/tasks/TasksSection.tsx` | **new** — section container: fetch, dirKeys filter, group rendering, footer links |
| `client/src/features/home/tasks/TaskCard.tsx` | **new** — one unified card incl. kebab + agent sub-row |
| `client/src/features/home/tasks/homeTasksModel.ts` | **new** — pure presentation-model functions (filter, group split, agent attach, colors) — the unit-testable core |
| `client/src/features/home/tasks/HomeTaskModal.tsx` | **new** — source-routing modal opener (Card → `CardModal`, Delegation → `DelegationTaskModal`) |
| `client/src/features/delegations/TaskDetailBody.tsx` | **new (extracted)** — body of today's `TaskDrawer`, verbatim |
| `client/src/features/delegations/DelegationTaskModal.tsx` | **new** — `Modal size="xl"` wrapping `TaskDetailBody` |
| `client/src/features/delegations/TaskDrawer.tsx` | refactor to wrap `TaskDetailBody` (no behavior change; its tests must keep passing unmodified) |
| `client/src/features/home/HomePage.tsx` | rail 240→300; rail becomes agents (1/3) + `TasksSection` (2/3); right dock drops the Tasks tab (chat only) |
| `client/src/features/home/ProjectTasksPanel.tsx` | **deleted** |
| `client/src/hooks/useSignalRInvalidation.ts` | add `['homeTasks','list']` to the events in §5.4 |
| `client/src/features/home/HomePage.test.tsx` | update: rail contains TASKS section; dock is chat-only |

### 5.2 TypeScript presentation model (`client/src/api/homeTasks.ts`)

```ts
import type { AgentModelLevel } from './agents'
import type { CardWorkflowRunStatus } from './boards'

export type HomeTaskSource = 'Card' | 'Delegation'
export type HomeTaskGroup = 'NeedsHuman' | 'Running' | 'Next' | 'Done'
export type NeedsHumanReason = 'Review' | 'Gate' | 'Blocked' | 'Question'

export interface HomeTaskItemDto {
  key: string                       // "card:<32hex>" | "task:<32hex>" — stable React key
  source: HomeTaskSource
  id: string
  identifier: string                // CARD-nnnn | 8-char short id
  title: string
  group: HomeTaskGroup
  state: string                     // native status verbatim
  needsHuman: boolean
  needsHumanReason: NeedsHumanReason | null
  question: string | null           // Blocked delegation's question, first line
  stageName: string | null          // cards
  workflowRunStatus: CardWorkflowRunStatus | null
  modelLevel: AgentModelLevel | null   // delegations
  escalatedFrom: AgentModelLevel | null
  costUsd: number | null
  priority: number | null           // cards
  boardId: string | null
  agentId: string | null
  agentName: string | null
  agentSessionId: string | null
  workingDirectory: string | null
  repoPath: string | null
  worktreePath: string | null
  createdAt: string
  startedAt: string | null
  completedAt: string | null
}

export const homeTaskKeys = {
  list: ['homeTasks', 'list'] as const,
}
```

`homeTasksModel.ts` (pure, no React):

```ts
export interface GroupedHomeTasks {
  needsHuman: HomeTaskItemDto[]
  running: HomeTaskItemDto[]
  next: HomeTaskItemDto[]
  done: HomeTaskItemDto[]
}

/** dirKeys filter — same normalizeDir contract ProjectTasksPanel used. */
export function filterByProject(items: HomeTaskItemDto[], dirKeys: string[]): HomeTaskItemDto[]

/** Splits the (already server-ordered) list; MUST NOT re-sort. Caps done at 12. */
export function groupItems(items: HomeTaskItemDto[]): GroupedHomeTasks

/** The agent actively on an item, joined from useAgentList() data (fresh at 5s). */
export function activeAgentFor(item: HomeTaskItemDto, agents: AgentSummaryDto[]): AgentSummaryDto | null
// cards: agents.find(a => a.currentCardId === item.id)
// delegations: item.group === 'Running' ? agents.find(a => a.id === item.agentId) : null

export const SOURCE_LABEL: Record<HomeTaskSource, string> = { Card: 'Card', Delegation: 'Task' }
export const STATE_COLOR: Record<string, string>  // union of board + taskVisuals colors:
// Backlog/Queued/Canceled → gray; InProgress/Dispatched/Working → active;
// Review/Blocked → warning; Done/Succeeded → success; Failed → danger
```

### 5.3 Component contracts

```ts
// TasksSection.tsx
export function TasksSection({ dirKeys, onSelectAgent }:
  { dirKeys: string[]; onSelectAgent: (agentId: string) => void })
// - useHomeTasks() + useAgentList() (already mounted on HomePage; shared cache)
// - error → the same dimmed one-liner style ProjectTasksPanel used
// - footer: "Open the board →" (/boards) and "Open delegations →" (/orchestrator?tab=delegations)
// - owns modal state: const [openItem, setOpenItem] = useState<HomeTaskItemDto | null>(null)

// TaskCard.tsx
export function TaskCard({ item, activeAgent, onOpen, onSelectAgent }:
  { item: HomeTaskItemDto; activeAgent: AgentSummaryDto | null;
    onOpen: (item: HomeTaskItemDto) => void; onSelectAgent: (agentId: string) => void })
// Box pos="relative" wrapping UnstyledButton (whole card = open) with the kebab Menu
// positioned absolutely outside the button — the AgentsPage.tsx:170 pattern.
// aria-label={`Open ${item.identifier} ${item.title}`}; needs-human left border.

// HomeTaskModal.tsx
export function HomeTaskModal({ item, onClose }: { item: HomeTaskItemDto | null; onClose: () => void })
// item?.source === 'Card': lazy useBoard(item.boardId) → <CardModal boardId card opened onClose/>
// item?.source === 'Delegation': <DelegationTaskModal taskId={item.id} onClose/>
```

Mutations reused as-is: `useCancelAgentTask`, `useRetryAgentTask`, `useEscalateAgentTask`,
`useReplyToAgentTask` (via `TaskDetailBody`), `useSpawnCard` (kebab). No new mutations.

### 5.4 Live updates (exact wiring)

Extend `INVALIDATION_MAP` in `client/src/hooks/useSignalRInvalidation.ts` — the
following **existing** events additionally invalidate `homeTaskKeys.list`:

`CardChanged`, `BoardChanged`, `AgentTaskChanged`, `AgentQueueChanged`,
`RunAttemptChanged`, `SessionFinished`, `AgentChanged`.

The agent sub-row needs no extra wiring: it renders from `useAgentList()`, which already
combines `AgentChanged`/`SessionFinished` invalidation with a 5s poll (turn-start has no
event — the documented gap). The 15s `refetchInterval` on `useHomeTasks` covers dropped
connections, matching today's agent-tasks behavior.

---

## 6. Mockup

`mockup.html` in this directory is a static, self-contained sketch of the rail (agents
third, tasks two-thirds, all four groups populated, needs-human treatment, agent-below-
card, kebab). It is a layout communication aid, not a pixel spec — real styling is
Mantine components per §2.

## 7. Test plan

### Server (`tests/Antiphon.Tests`, TUnit + Shouldly, run via `dotnet run --project`)

New file `tests/Antiphon.Tests/Application/HomeTaskServiceIntegrationTests.cs`,
`[Category("Integration")]`. **Shared-Postgres rules apply** (CLAUDE.md): the endpoint
returns a global list, so every assertion is scoped to rows the test created —
`items.Single(i => i.Id == myCard.Id)`, never counts, never `ShouldBeEmpty()` on the
whole list. No `[NotInParallel]` group is needed (no global sweep is asserted).

Cases (each builds its own board/agent/task rows via the `BoardServiceIntegrationTests.BuildHarness`
/ `AgentTaskServiceIntegrationTests.CreateContext` patterns):

1. Card in `Review` → `Group == NeedsHuman`, `NeedsHumanReason == "Review"`, `State == "Review"`.
2. Card `InProgress` with `ActiveWorkflowRun.Status == WaitingForHumanReview` →
   `NeedsHuman`/`"Gate"`, and `StageName` carries `CurrentStage.Name`.
3. Delegation settled Blocked (Result ending in `?`) → `NeedsHuman`/`"Question"`,
   `Question` is the first line of `Result`; a conflict-Blocked task (FailureReason set,
   Result null) → `Question` falls back to `FailureReason`.
4. Card `InProgress` + `OwnerSessionId` set → `Running`; same card with claim cleared → `Next`.
5. Delegation `Queued` → `Next`; `Working` → `Running`.
6. Done window: own card completed 30 days ago is absent; completed yesterday is present
   (`items.Any(i => i.Id == old.Id).ShouldBeFalse()` — scoped, not a count).
7. Ordering, scoped: create two own NeedsHuman items with distinct waiting-since and
   assert their **relative** order (`IndexOf(a) < IndexOf(b)`), never absolute positions.
8. Directory derivation: card with assigned agent carries the agent's
   `WorkingDirectory`; card with only a session falls back to the session `Cwd`;
   untouched card has null.
9. `Key`/`Identifier` shape: `card:` and `task:` prefixes, task identifier is the 8-char
   short id.

HTTP smoke: one case in the `AntiphonWebAppFactory` style (`FileSystemBrowseApiTests`
pattern) — `GET /api/home/tasks` returns 200 and deserialises to the DTO.

Contract snapshot: add `home-tasks.json` capture to
`tests/Antiphon.E2E/ContractSnapshotTests.cs` — Storybook stories must seed from it.

### Client (Vitest + Testing Library + MSW, co-located)

1. `homeTasksModel.test.ts` — pure: `filterByProject` (normalizeDir contract incl.
   worktree paths), `groupItems` (splits without re-sorting, caps done at 12),
   `activeAgentFor` (card via `currentCardId`, delegation via `agentId` only when Running).
2. `TaskCard.test.tsx` — renders identifier + source chip (`Card`/`Task`), state badge,
   stage line for cards, tier + cost for delegations, question line for Blocked;
   needs-human border/badge; kebab opens the right items per source (and clicking the
   kebab does **not** fire `onOpen`); agent sub-row renders and clicking it fires
   `onSelectAgent`.
3. `TasksSection.test.tsx` — MSW `http.get('/api/home/tasks')` + `/api/agents`
   (factory helpers per the `HomePage.test.tsx` pattern, `server.use` per test):
   groups render in order with counts; items outside `dirKeys` are filtered; empty
   Needs-you/Running render their empty lines; error → dimmed message; card click
   opens the modal (stub `HomeTaskModal` with `vi.mock` to assert routing).
4. `HomePage.test.tsx` updates — the rail shows AGENTS and TASKS sections; the dock no
   longer has a Tasks tab; existing agent-selection tests keep passing.
5. `TaskDrawer.test.tsx` — must pass **unmodified** after the `TaskDetailBody`
   extraction (the refactor's regression gate).
6. Storybook: `TasksSection.stories.tsx` seeding `homeTaskKeys.list` +
   `agentKeys.all` from contract fixtures via `setQueryData` (the
   `DelegationsBoard.stories.tsx` decorator pattern; pin `Date.now` for elapsed times).

E2E: none required for v1 (no new write paths). If added later, remember the client
bundle staleness gate (`EnsureClientBundleIsCurrent` — run `npm run build`).

## 8. Deliberately left open

1. **Unassigned, never-run Backlog cards are invisible on the home rail** (§4.4) until
   a Project→directory mapping exists — that belongs to the card-task-file-sync work,
   not here.
2. **Rail width 300px** is a recommendation, not a measurement — adjust on sight; the
   layout degrades gracefully either way since every text is `truncate`/`lineClamp`.
3. **Card write actions in the kebab** are minimal (Spawn only). Approve/move stays in
   the modal until someone asks for one-click approve from the rail — that wants a
   confirm affordance this design doesn't include.
4. **`OrchestratorRunningSessionDto` overlap**: the orchestrator state view also shows
   running card work. This design does not touch it; if the projection proves out, the
   orchestrator page could later consume the same endpoint.

## 9. Out of scope

Any storage/schema change; any state-machine, retry, watchdog or dispatcher change;
tracker sync; the delegations board and board views themselves (both stay as-is);
answering machine/queue semantics (df922860 owns the queue policy).
