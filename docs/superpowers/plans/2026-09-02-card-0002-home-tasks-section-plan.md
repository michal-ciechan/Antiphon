# CARD-0002 — Tasks section on the home rail: one list of Cards and delegations

**Date:** 2026-09-02 (Plan pass, task 032f0677 — design only; no production code changed)
**Card:** CARD-0002 "Tasks section on the home rail - unified cards for board Cards and delegations"
**Supersedes:** `docs/features/010-home-tasks-section/proposal.md` (2026-08-09). That design's
central recommendation — a read-only server projection over both tables, no storage merge — still
holds and is kept. Its data model, grouping rules, directory linkage and needs-human vocabulary are
stale against today's code and are replaced here; the errata block at the top of that file lists
exactly what changed. `mockup.html` remains a fair layout sketch (agents third, tasks two-thirds,
agent under its card, kebab) but shows the old four groups and the old `Blocked` card state.

**Sources (verified this pass):** CARD-0002, the 010 proposal + mockup, CARD-0300 (`e6c04fed`,
`2027e757`, `031d3d33`) and its plan, CARD-0304 (`3612af22`) and its plan, CARD-0301 (Backlog,
held), `HomePage.tsx`, `ProjectTasksPanel.tsx`, `MobileHomePage.tsx`, `AttentionGlance.tsx`,
`attentionVisuals.ts`, `AgentRail.tsx`, `projectGrouping.ts`, `taskReview.ts`,
`useSignalRInvalidation.ts`, `client/src/api/{boards,agentTasks,agents,attention,projects}.ts`,
`CardModal.tsx`, `TaskDrawer.tsx`, `TaskChip.tsx`, `BoardCard.tsx`, `CardRow.tsx`,
`AgentsPage.tsx` (kebab), `CardEndpoints.cs`, `CardService.GetSummaryAsync`,
`BoardService.ToSummaryDto`, `AttentionService.cs`, `AgentTaskPipelineStatusService.cs`,
`Project.cs`, `Card.cs`, `AgentTask.cs`, `CardStatus.cs`, `docs/agent-card-lifecycle.md`,
`docs/orchestration-loop.md`, `docs/ui-screenshot-testing.md`, `HomePage.test.tsx`,
`TaskDrawer.test.tsx`, `CardWorkTransitionServiceTests.cs`, `ContractSnapshotTests.cs`, and the
live server at 17202 (`/api/cards`, `/api/agent-tasks`, `/api/agents`, `/api/projects`,
`/api/boards`, `/api/attention`) on 2026-09-02.

---

## Decision

1. **A read-only projection, `GET /api/home/tasks`, over Cards and AgentTasks. No storage change.**
   Same call as the 010 proposal §3, same reasons; nothing since has weakened them and CARD-0040
   (delegations bound to cards move the card themselves) has made the two pipelines *more*
   coupled at the read side and *no less* distinct at the write side. The projection is a status
   switch plus joins; it decides nothing about stuckness (that stays `AttentionService`).

2. **A bound delegation is not a second item — it is the card's worker line.** Every open task
   with `AgentTask.CardId` set renders *under* its card ("agent shows below that card"), never as
   its own tile. Only unbound tasks (`CardId == null`) are top-level items with the `Task` chip.
   This is what "unified" means on this deployment: the live board has 2 In Progress cards, both
   worked by bound delegates; `Agent.CurrentCardId` and `Card.OwnerSessionId` are null everywhere.

3. **"Stage" is the workflow stage when a run exists, else the newest bound task's role.** The card
   requirement names `currentWorkflowStageName` / `workflowRunStatus`; both are null on every live
   card because work runs as delegations, and CARD-0304 already declared task roles the v1 stage
   taxonomy (Plan, Code, Review, …). The projection computes `stage` once; the client prints it.

4. **Needs-human comes from record status, never re-derived; the question text comes from the
   attention feed, never re-computed.** Membership: `CardStatus.NeedsDecision`, `CardStatus.Review`,
   `CardWorkflowRunStatus.WaitingForHumanReview`, `AgentTaskStatus.Blocked` (bound or unbound).
   The question shown on a `NeedsDecision` card or a `Blocked` task is the `evidence` of the
   matching `CardNeedsDecision` / `BlockedQuestion` row in `GET /api/attention`, which Home already
   fetches for `AttentionGlance`. The projection carries no question field, so the Tasks section
   cannot drift from `/attention` on what the question is, and CARD-0035's non-widening rule is
   untouched (this section never synthesises an attention row; it reads statuses).

5. **Five groups, in this order: Needs you · Running · To review · Up next · Done.** Review is
   split out of Needs-you on purpose: the live board has **31** cards in Review (Review → Done is
   not automated, `docs/agent-card-lifecycle.md`) and 0 in NeedsDecision. A single "needs you"
   group would bury Running under a wall of review verdicts; CARD-0300's own vocabulary already
   separates "Blocked — answer/decide" from "Review — look, often leave it". Needs-you and Running
   always render (empty line when empty); To review, Up next and Done render only when non-empty,
   and To review / Up next cap their visible rows with a "+N more → board" link.

6. **Compatible with CARD-0300, not competing.** `AttentionGlance` stays in the header row and
   owns fleet-wide "what is stuck" counts + the `/attention` link. The Tasks section lives in the
   left rail, is scoped to the selected project, and marks needs-human *per item* from that item's
   own status. Nothing here adds a fourth attention surface, a reply box on Home, or a bucket
   count; the mobile page (`MobileHomePage.tsx`) is untouched.

7. **Not CARD-0301.** CARD-0301 (Backlog) is a fleet-wide, *stage-major* phone view over
   `GET /api/agent-tasks/pipeline`. This is a per-project, *item-major* desktop rail. They share
   the role-as-stage vocabulary and nothing else; the pipeline endpoint's `ready` (plan landed,
   waiting for Code) is a natural later enrichment of the Up next group and is left open (§Left
   open).

8. **The right dock loses its Tasks tab; `ProjectTasksPanel.tsx` is deleted.** Its two features
   that are not in the 010 proposal — the unread-deliverable dot and the `Read` deep-link
   (`c4d7e0d7`, `taskReview.ts`) — move into the Done group unchanged. `ToReadBadge` keeps its
   count and scrolls the Done group into view instead of switching a tab that no longer exists.

9. **Rail 240 → 300 px**, agents `maxHeight: 33%`, tasks `flex: 1`. Per the 010 proposal §2.1;
   the centre `FilesReviewPanel` is `flexGrow: 1` and absorbs it.

---

## Ground truth (checked, not guessed)

What the 010 proposal got right and what moved under it. Line numbers are as of `21002401`.

| Claim in 010 (2026-08-09) | Today | Consequence |
|---|---|---|
| `CardStatus` has `Blocked` | `Backlog, InProgress, Review, Done, NeedsDecision, Canceled` (`server/Domain/Enums/CardStatus.cs`, `client/src/api/boards.ts:5`) | Needs-you reason `Blocked` → `Decision` |
| Cards have no flat list endpoint | `GET /api/cards?updatedSince\|status\|boardId` (`CardEndpoints.cs:37-50`), summary representation (`BoardService.ToSummaryDto:358` — 200-char preview, `Sessions = []`), cap `Cards:MaxListResults` 500 | Client-side composition is *possible*; rejected on payload (Antiphon board: 314 cards, 72 Backlog, 31 Review) and on the joins below |
| No task ↔ card link | `AgentTask.CardId` (`AgentTask.cs:118`), `cardId`/`cardIdentifier` on the summary DTO (`agentTasks.ts:118-121`), `CardWorkTransitionService` moves cards from bound tasks | Bound tasks nest under cards; the "agent below the card" join is task → card, not `Agent.CurrentCardId` |
| Cards carry no directory | `Project.LocalRepositoryPath` (`Project.cs`) via `Board.ProjectId`; every live project has one (mixed `C:/` and `C:\` forms — `normalizeDir` already folds both) | Card directory = project path; the 010 §4.4 "invisible unassigned Backlog card" gap is closed |
| Needs-human question lives on `Result` / nowhere for cards | `AttentionService.BuildBlockedAsync` (`:233`) and `BuildCardNeedsDecisionAsync` (`:290`, reads the latest Move/Reopen revision's `Reason`) already produce it as `evidence`; `CardModal` reads the card question from the same feed (`CardModal.tsx:59-65`) | Question text comes from `useAttention()`; no second reader |
| Home has `NeedsAttentionBadge` → orchestrator tab | `AttentionGlance` (three badges → `/attention`), `HomePage.tsx:175`; `/attention` route exists | Header row is CARD-0300's; this card touches only the rail and the dock |
| `useAgentTasks()` unbounded | `useAgentTasks(false, { since: 'default' })` rolling 7-day window on Home (`HomePage.tsx:75`); `ProjectTasksPanel` still calls the unbounded form and a test pins the second fetch (`HomePage.test.tsx:191-208`) | That test is replaced (§S6) |
| `TaskDrawer` body is inline | Still inline: `TaskDrawer:61`, `DrawerTitle:78`, `TaskDetail:93`; 11 tests in `TaskDrawer.test.tsx` | Extraction plan unchanged |
| Kebab pattern at `AgentsPage.tsx:170-206` | Now `:496-532` (Menu outside the `UnstyledButton`, `pos="absolute"`) | Copy from there |
| `CardModal` takes `boardId` + `card` | Same, plus `columns` for `MoveMenu`; `card: null` renders the **create** modal (`CardModal.tsx:25-58`) | The opener must wait for `useCard(id)` before mounting it |
| Stage renders nowhere | `CardRow.tsx:83` renders `currentWorkflowStageName` | Same badge style reused |
| Storybook/contract chain | `docs/ui-screenshot-testing.md`; fixtures in `client/src/test/fixtures/contract/` (`agent-tasks.json`, `agent-task-detail.json`, …); stories seed a QueryClient from fixtures, no MSW | New story must follow that chain |

**Live data, 2026-09-02 (17202):** 2 In Progress cards (both with a bound delegate: one
`Dispatched Plan`, one `Blocked Deploy` on the gym-stat board), 31 Review, 0 NeedsDecision,
72 Backlog; `currentWorkflowStageName`/`workflowRunStatus` null on all; `ownerSessionId` null on
all; `currentCardId` null on all 46 agents; 2 open delegations, both bound to a card. This is
why decisions 2, 3 and 5 are shaped the way they are.

**CARD-0300 boundary, stated so nobody re-litigates it in execution:** the glance answers "is
anything stuck anywhere"; the Tasks section answers "what is *this project's* work doing". A
`CardStalled` or `ProgressStalled` row on `/attention` does **not** make an item change group
here — the item stays wherever its status puts it. The one deliberate overlap is the question
text on a Needs-you item, read from the same feed rather than copied.

---

## Groups (normative)

Evaluate top to bottom, first hit wins. `openBound` = the newest task with `CardId == card.Id` and
`Status ∈ {Queued, Dispatched, Working, Blocked}`; `lastBound` = the newest bound task of any status.

| Group | Card qualifies when | Unbound task qualifies when |
|---|---|---|
| **Needs you** | `Status == NeedsDecision` → reason `Decision`; `ActiveWorkflowRun.Status == WaitingForHumanReview` → `Gate`; `openBound.Status == Blocked` → `Question` | `Status == Blocked` → `Question` |
| **Running** | `Status == InProgress && (openBound ∈ {Dispatched, Working} \|\| OwnerSessionId != null \|\| ActiveWorkflowRun.Status == Running)` | `Status ∈ {Dispatched, Working}` |
| **To review** | `Status == Review` → reason `Review` | — |
| **Up next** | `Status == Backlog`; or `Status == InProgress` with nothing above (nobody on it — attention's `CardStalled` says so after 7 days, this section does not) ; `openBound.Status == Queued` shows as the worker line "queued" | `Status == Queued` |
| **Done** | `Status ∈ {Done, Canceled}` and `CompletedAt ≥ now − 7 d` | `Status ∈ {Succeeded, Failed, Canceled}` and `CompletedAt ≥ now − 7 d` |

`AgentQueuePosition` is not consulted (a queue row is not proof of work — CARD-0001).
`Role == Check` tasks are excluded from both the item list and the bound-task join (they are
about a task, not a card — `docs/agent-card-lifecycle.md`).

**Ordering** (server returns the list fully ordered; the client filters and nests, never re-sorts):

1. Group ascending in the order above.
2. Needs you: reason rank `Decision` < `Question` < `Gate`, then waiting-since ascending
   (`CompletedAt` of the Blocked task; the card's `UpdatedAt` otherwise).
3. Running: `openBound.DispatchedAt ?? card.StartedAt ?? task.DispatchedAt ?? CreatedAt` descending.
4. To review: `UpdatedAt` ascending (longest-waiting verdict first).
5. Up next: cards by `Priority` ascending then `CreatedAt` ascending; unbound tasks by
   `CreatedAt` ascending (dispatcher order); cards before tasks.
6. Done: `CompletedAt` descending.
7. Ties: `Key` ascending.

**Caps:** server caps Done at the 7-day window and **60** most recent items combined; the client
shows at most 12 Done, 8 To review, 8 Up next per project, each with a "+N more → open board /
open delegations" link when cut.

---

## Server design

### Endpoint and files

```
GET /api/home/tasks  →  200 HomeTasksDto { generatedAt, items: HomeTaskItemDto[] }
```

- `server/Api/Endpoints/HomeEndpoints.cs` — `MapGroup("/api/home").WithTags("Home")`, one
  `MapGet("/tasks", …)`; registered in `server/Program.cs` next to `MapAttentionEndpoints()`
  (`:712`); `AddScoped<HomeTaskService>()` next to `AttentionService` (`:306`).
- `server/Application/Services/HomeTaskService.cs` — the projection.
- `server/Application/Dtos/HomeTaskDtos.cs` — DTOs below.
- No parameters in v1. Fleet-global; the client filters by the selected project's `dirKeys`
  exactly as `ProjectTasksPanel` does today (`taskReview.taskIsInProject`, `normalizeDir`). The
  project ⇄ directory folding (worktrees, subdirectories) lives in `projectGrouping.ts` and is
  not duplicated server-side.

### DTOs

```csharp
public enum HomeTaskSource { Card = 0, Delegation = 1 }
public enum HomeTaskGroup { NeedsHuman = 0, Running = 1, Review = 2, Next = 3, Done = 4 }
public enum HomeTaskHumanReason { Decision = 0, Question = 1, Gate = 2, Review = 3 }

/// The delegation currently (or most recently) working a card. Null when none is bound.
public sealed record HomeTaskWorkerDto(
    Guid TaskId,
    string ShortId,
    AgentTaskRole Role,
    AgentTaskStatus Status,          // Queued | Dispatched | Working | Blocked | settled
    AgentKind AgentKind,
    AgentModelLevel ModelLevel,
    Guid? AgentId,
    string? AgentName,               // denormalised snapshot — survives ephemeral-agent deletion
    Guid? AgentSessionId,
    decimal CostUsd,
    DateTime? DispatchedAt,
    DateTime? CompletedAt);

public sealed record HomeTaskItemDto(
    string Key,                      // "card:{id:N}" | "task:{id:N}" — stable React key
    HomeTaskSource Source,
    Guid Id,
    string Identifier,               // CARD-nnnn | 8-char short id
    string Title,
    HomeTaskGroup Group,
    string State,                    // native status name verbatim, never remapped
    HomeTaskHumanReason? HumanReason,
    // Stage: ActiveWorkflowRun.CurrentStage.Name, else the newest bound task's Role name (cards);
    // the task's own Role name (delegations). Null only for a card that has never had a bound task.
    string? Stage,
    CardWorkflowRunStatus? WorkflowRunStatus,
    // Cards only.
    int? Priority,
    Guid? BoardId,
    HomeTaskWorkerDto? Worker,       // open bound task, else the newest settled bound task, else null
    Guid? OwnerAgentId,              // OwnerSession.AgentId when the card-spawn path owns it
    // Delegations only (unbound). Bound tasks are never items.
    AgentKind? AgentKind,
    AgentModelLevel? ModelLevel,
    AgentModelLevel? EscalatedFrom,
    AgentTaskRole? Role,
    decimal? CostUsd,
    Guid? AgentId,
    string? AgentName,
    Guid? AgentSessionId,
    DateTime? ReadAt,                // for the unread-deliverable dot (taskReview.isUnreadDeliverable)
    string? DeliverablePath,
    string? DeliverableRef,
    // Directory linkage for the client project filter.
    string? WorkingDirectory,        // cards: Board.Project.LocalRepositoryPath; tasks: WorkingDirectory
    string? RepoPath,                // tasks only
    string? WorktreePath,            // cards: CurrentWorktree.Path; tasks: WorktreePath
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime UpdatedAt,
    DateTime? CompletedAt);

public sealed record HomeTasksDto(DateTime GeneratedAt, IReadOnlyList<HomeTaskItemDto> Items);
```

`State` stays a string on purpose (the 010 argument: a merged status enum is the vocabulary
collision the read-view avoids). The typed fields the client branches on are `Source`, `Group`,
`HumanReason`, `Worker.Status`.

### Projection rules

**Cards** — `db.Cards.AsNoTracking().Where(c => c.ArchivedAt == null)` with `Select` projection
(no entity includes): `Board.Project.LocalRepositoryPath`, `ActiveWorkflowRun.Status`,
`ActiveWorkflowRun.CurrentStage.Name`, `CurrentWorktree.Path`, `OwnerSession.AgentId`. Done cards
additionally filtered by `CompletedAt >= now − 7d` in the query. Bound tasks are loaded in one
second query: `db.AgentTasks.Where(t => t.CardId != null && t.Role != AgentTaskRole.Check)`
projected to `HomeTaskWorkerDto` + `CardId`, grouped in memory, newest-first by
`DispatchedAt ?? CreatedAt`. Live card counts (314 + ~40 bound tasks) make this trivially cheap;
if a board ever grows past `Cards:MaxListResults`, the Done window is what bounds it — open cards
are never cut.

**Unbound delegations** — `db.AgentTasks.Where(t => t.CardId == null && t.Role != Check)`, open
rows always, settled rows only inside the 7-day window. Same shape `AgentTaskService.ListAsync`
projects (`:888-…`) minus fields the rail never shows.

**Directory for a card**: `Board.Project.LocalRepositoryPath` → else `AssignedAgent.WorkingDirectory`
→ else `Worker.AgentName`'s task `RepoPath ?? WorkingDirectory` → else null. A project with no
`LocalRepositoryPath` and no run history yields a card the home filter drops; the board view still
has it. (All ten live projects have a path.)

### Live updates

No new SignalR event. Extend `INVALIDATION_MAP` in `client/src/hooks/useSignalRInvalidation.ts` so
these **existing** events additionally invalidate `['homeTasks']`: `CardChanged` (`:69`),
`BoardChanged` (`:59`), `AgentTaskChanged` (`:133`), `RunAttemptChanged` (`:83`), `SessionFinished`
(`:118`), `AgentChanged` (`:92`), `AgentQueueChanged` (`:101`). `useHomeTasks()` polls at 15 s as
the dropped-connection fallback, matching `useAgentTasks` / `useAttention`. The worker line's
"working" spinner joins `useAgentList()` (already 5 s fresh on Home) by `Worker.AgentId`.

---

## Client design

### Files

| File | Change |
|---|---|
| `client/src/api/homeTasks.ts` | **new** — TS mirror of the DTOs, `homeTaskKeys`, `useHomeTasks()` |
| `client/src/features/home/tasks/homeTasksModel.ts` | **new** — pure: `filterByProject`, `groupItems` (split + caps, no re-sort), `questionFor(item, attentionItems)`, `workerAgent(item, agents)`, `SOURCE_LABEL`, `STATE_COLOR`, `HUMAN_REASON_LABEL` |
| `client/src/features/home/tasks/TasksSection.tsx` | **new** — the rail section: fetch, filter, groups, footer links, modal state, `id="home-tasks-done"` anchor |
| `client/src/features/home/tasks/TaskCard.tsx` | **new** — one item: header row (identifier, source chip, state badge, kebab), title, stage/cost line, question line, worker line |
| `client/src/features/home/tasks/HomeTaskModal.tsx` | **new** — routes by source: Card → `CardModal`, Delegation → `DelegationTaskModal` |
| `client/src/features/delegations/TaskDetailBody.tsx` | **new (extracted)** — `TaskDetail` + `DrawerTitle` moved verbatim from `TaskDrawer.tsx`, exported as `TaskDetailBody({ taskId, onClose })` and `TaskDetailTitle({ detail })` |
| `client/src/features/delegations/DelegationTaskModal.tsx` | **new** — `Modal size="xl"` around `TaskDetailBody` |
| `client/src/features/delegations/TaskDrawer.tsx` | refactor to wrap `TaskDetailBody`; `TaskDrawer.test.tsx` must pass **unmodified** |
| `client/src/features/home/HomePage.tsx` | rail `w={240}`→`300` (`:212`); rail = agents block (`maxHeight: '33%'`) + `TasksSection` (`flex: 1, minHeight: 0`); dock `Tabs` (`:266-323`) collapses to the chat panel + `ReportBugButton`; `ToReadBadge` (`:334-355`) scrolls `#home-tasks-done` into view |
| `client/src/features/home/ProjectTasksPanel.tsx` | **deleted** |
| `client/src/hooks/useSignalRInvalidation.ts` | seven entries gain `['homeTasks']` |
| `client/src/features/home/HomePage.test.tsx` | `seed()` gains an `/api/home/tasks` handler; the two Tasks-tab tests (`:191-208`, `:300-331`) are rewritten against the rail |
| `client/src/features/home/tasks/TasksSection.stories.tsx` | **new** — seeded from a new `home-tasks.json` contract fixture |
| `docs/features/008-home-workspace/proposal.md` §3.2, `docs/ops-http.md` route table, `docs/antiphon-api.md` §2 "Work items" | one line each |

### Item anatomy

```
┌──────────────────────────────────────────┐
│ CARD-0002  [Card]        [In progress] ⋮ │  identifier · source chip · state · kebab
│ Tasks section on the home rail           │  title, lineClamp 2
│ stage: plan                              │  cards: stage; tasks: tier · role · cost
│ “Should validation errors block save?”   │  Needs-you only: first line of the attention evidence
│ └ ▮ task-15c3cb72  plan  [Working ⋯]     │  worker line: agent · role · status (bound task)
└──────────────────────────────────────────┘
```

- **Source chip**: `Badge size="xs" variant="outline"` — `Card` (color `active`) / `Task`
  (color `violet`, the delegation palette). The discriminator, made visible.
- **State**: `stateLabel(status)` for cards (`boardVisuals.ts` — so `NeedsDecision` prints
  "Needs decision"), the status verbatim for tasks; colour from a merged `STATE_COLOR` that is the
  union of `boardVisuals.STATE_COLORS` and `taskVisuals.STATUS_COLOR` (both already agree:
  gray / active / warning / success / danger).
- **Needs-you weight**: 3 px left border in `warning` for `Review`/`Gate`, `danger` for
  `Decision`/`Question` (the `BoardCard.tsx` border convention; `danger` matches
  `ATTENTION_VISUALS.CardNeedsDecision`), plus a badge with `HUMAN_REASON_LABEL`
  (`Decision` → "Needs decision", `Question` → "Question", `Gate` → "Gate", `Review` → "Review").
- **Question line**: `Text size="xs" lineClamp={1}` from `questionFor(item, attention.items)` —
  the first non-blank line of the matching `CardNeedsDecision` (by `cardId`) or `BlockedQuestion`
  (by `taskId`, where the task is the item itself or its `Worker`) evidence. Absent when the feed
  has no row (older feed, or a Blocked task the feed excluded); the item still shows its reason
  badge.
- **Worker line** (cards with a `Worker`): `TbTerminal2` (green when `workerAgent(...)`'s live
  session is Running, else dimmed) + `AgentName` + role badge + status badge (`Working ⋯` with the
  `AgentRail.ActivityBadge` dots when `agents.find(a => a.id === Worker.AgentId)?.working`,
  otherwise the task status). Clicking the worker line opens the **delegation** modal for that
  task; clicking the agent name selects that agent in the rail (`onSelectAgent`) when it is one of
  the workspace's agents. A settled `Worker` on a Review/Up-next card renders as
  `plan · done 2h ago` in dimmed text — that is the "what is next" signal the orchestrator reads.
- **Unread deliverable** (Done delegations): the violet `●` and the `Read` link exactly as
  `ProjectTasksPanel.tsx:100-150` renders them today (`isUnreadDeliverable`, `deliverablePath`,
  `/plans?file=…&ref=…&task=…`).
- Tasks additionally show `TierBadge` (`TaskChip.tsx:24`) and `formatCost(costUsd)`.

### Kebab

`Menu` + `ActionIcon` with `TbDotsVertical`, positioned absolutely **outside** the card's
`UnstyledButton` (the `AgentsPage.tsx:496-532` structure) so opening it never fires `onOpen`.

| Item | Card | Unbound task |
|---|---|---|
| Open | modal (same as click) | modal |
| Open thread | `/thread/card-N` | — |
| Open board / Open delegations | `/boards/{boardId}?card={id}` | `/orchestrator?tab=delegations&task={id}` |
| Open delegation | when `Worker != null` → delegation modal for `Worker.TaskId` | — |
| Answer… | when `Worker.Status == Blocked` → delegation modal (answer box is in `TaskDetailBody`) | when `Blocked` |
| Spawn session | when `OwnerAgentId == null && Worker is not open && status ∉ {Done, Canceled}` → `useSpawnCard(boardId)` | — |
| Retry / Escalate / Cancel | — | as `TaskDetailBody` offers them (disabled states identical) |

Card moves (decide, approve, move) stay in `CardModal`, which gets `columns` from
`useBoardColumns(boardId)` so `MoveMenu` works from the rail-opened modal. A one-click "Decide"
from the rail needs a confirm affordance this card does not include.

### Modal

- **Card** → `useCard(item.id)` (`boards.ts:506`) + `useBoardColumns(item.boardId)`; mount
  `<CardModal boardId card={fullCard} columns opened onClose/>` **only once `fullCard` is
  loaded** — `card: null` is the create modal (`CardModal.tsx:58`). Until then a `Loader`.
- **Delegation** → `<DelegationTaskModal taskId onClose/>` wrapping `TaskDetailBody`, so
  Retry / Escalate / Cancel / Answer and the read-stamp effect come along untouched.

### Layout

```tsx
<Paper withBorder p="xs" w={300} style={{ flexShrink: 0, display: 'flex', flexDirection: 'column', minHeight: 0 }}>
  <Box style={{ flex: '0 0 auto', maxHeight: '33%', display: 'flex', flexDirection: 'column', minHeight: 0 }}>
    {/* AGENTS header + <AgentRail/> — unchanged */}
  </Box>
  <Divider my="xs" />
  <TasksSection dirKeys={…} workspaceAgents={workspace?.agents ?? []} onSelectAgent={…}
                style={{ flex: 1, minHeight: 0 }} />
</Paper>
```

The dock keeps `data-testid="home-dock"` and becomes the chat panel with `ReportBugButton` in its
header row; `?tab=tasks` is no longer read (a stale bookmark lands on chat).

---

## Slices

### S1 — Server projection

**Files:** `server/Application/Dtos/HomeTaskDtos.cs` (new), `server/Application/Services/HomeTaskService.cs`
(new), `server/Api/Endpoints/HomeEndpoints.cs` (new), `server/Program.cs` (`:306`, `:712`),
`docs/ops-http.md` (route table, beside `GET /api/attention`), `docs/antiphon-api.md` §2 "Work items".

**Tests:** new `tests/Antiphon.Tests/Application/HomeTaskServiceIntegrationTests.cs`,
`[Category("Integration")]`, own migrated schema per test in the `CardWorkTransitionServiceTests.World`
style (`:257-280` — `SeedCardAsync`, `SeedTaskAsync`, `SeedAgentAsync`); every assertion scoped to
rows the test created (`items.Single(i => i.Id == mine.Id)`, relative order via `IndexOf`, never
counts or `ShouldBeEmpty()` on the whole list). No `[NotInParallel]` (nothing global is swept).

1. NeedsDecision card → `NeedsHuman` / `Decision` / `State == "NeedsDecision"`.
2. InProgress card + `ActiveWorkflowRun.Status == WaitingForHumanReview` with a `CurrentStage`
   → `NeedsHuman` / `Gate`, `Stage` = the stage name (workflow beats role).
3. InProgress card + bound `Blocked` task → `NeedsHuman` / `Question`, `Worker.Status == Blocked`.
4. InProgress card + bound `Working` task → `Running`, `Stage == "Code"` (role fallback),
   `Worker.AgentName` carried; same card after the task settles `Succeeded` and the sweep has not
   run → `Next` with a settled `Worker`.
5. Review card → `Review` group, reason `Review`; Backlog card → `Next` ordered by priority.
6. Unbound `Queued` → `Next`; `Working` → `Running`; `Blocked` → `NeedsHuman`; bound task of any
   status is **absent** from `Items` and present as its card's `Worker`.
7. `Role == Check` rows: never an item, never a `Worker`.
8. Done window: own card completed 30 days ago absent; completed yesterday present; same for a
   `Succeeded` task; `ReadAt`/`DeliverablePath` carried.
9. Directory: card carries its project's `LocalRepositoryPath`; project without one and no history
   → null; task carries `WorkingDirectory`/`RepoPath`/`WorktreePath`.
10. Ordering, relative: two own Needs-you items (`Decision` before `Question`), two own Running
    items (newer dispatch first).
11. HTTP smoke in the `AntiphonWebAppFactory` style: `GET /api/home/tasks` → 200, deserialises;
    enums serialise as strings (`Source: "Card"`).

### S2 — Contract fixture + client API

**Files:** `tests/Antiphon.E2E/ContractSnapshotTests.cs` (a `home-tasks.json` capture in the
delegation scenario at `:315-318`, extended with one Review card, one NeedsDecision card and one
bound Working task), `client/src/test/fixtures/contract/home-tasks.json` (captured),
`client/src/api/homeTasks.ts`.

**Tests:** the snapshot test itself (first run captures, later runs drift-guard).

### S3 — `TaskDetailBody` extraction

**Files:** `client/src/features/delegations/TaskDetailBody.tsx` (new; `TaskDetail:93-…` and
`DrawerTitle:78` moved verbatim), `TaskDrawer.tsx` (thin `Drawer` wrapper),
`DelegationTaskModal.tsx` (new).

**Tests:** `TaskDrawer.test.tsx` passes with **zero edits** — the regression gate for the move.
`DelegationTaskModal.test.tsx`: opens with a Blocked detail and shows "The delegate asked" + the
answer box; `onClose` fires on Cancel success (same MSW shapes as the drawer test).

### S4 — Presentation model + `TaskCard`

**Files:** `client/src/features/home/tasks/homeTasksModel.ts`, `TaskCard.tsx`.

**Tests:**
- `homeTasksModel.test.ts` — `filterByProject` (repo path, worktree path, mixed `C:/` and `C:\`
  casing via `normalizeDir`); `groupItems` (splits in server order, never re-sorts, caps
  Done 12 / Review 8 / Next 8 and reports `hidden` counts); `questionFor` (card by `cardId`, task
  by own id, card by `Worker.TaskId`, first non-blank line, null when absent); `workerAgent`
  (found by `Worker.AgentId`, null otherwise); `STATE_COLOR` totality over both status unions.
- `TaskCard.test.tsx` — identifier + `Card`/`Task` chip; `stateLabel` for `NeedsDecision`; stage
  line for cards, tier + cost for tasks; question line; needs-you border + reason badge; worker
  line renders and fires `onOpenTask` / `onSelectAgent`; unread dot + `Read` link on a Done task
  with `deliverablePath`; kebab items per source and per state (Answer only when Blocked, Spawn
  only when spawnable); clicking the kebab does **not** fire `onOpen`.

### S5 — `TasksSection`, `HomeTaskModal`, live updates

**Files:** `TasksSection.tsx`, `HomeTaskModal.tsx`, `useSignalRInvalidation.ts`.

**Tests:** `TasksSection.test.tsx` (MSW `server.use` per test, factories in the `HomePage.test.tsx`
style): five groups in order with counts; Needs-you and Running empty lines; To review / Up next /
Done absent when empty; "+N more" links when capped; items outside `dirKeys` filtered; error →
dimmed one-liner; card click opens the modal routed by source (stub `HomeTaskModal` with
`vi.mock`); `ToReadBadge`-driven scroll target exists (`#home-tasks-done`).
`HomeTaskModal.test.tsx`: card branch waits for `useCard` before mounting `CardModal` (no create
modal flash); delegation branch mounts `DelegationTaskModal`.

### S6 — `HomePage` composition

**Files:** `HomePage.tsx`, `ProjectTasksPanel.tsx` (delete), `HomePage.test.tsx`,
`docs/features/008-home-workspace/proposal.md` §3.2.

**Tests:** `HomePage.test.tsx` — `seed()` serves `/api/home/tasks`; the rail shows AGENTS and TASKS;
the dock has no Tasks tab and still shows the chat panel; the project-scoping test (`:300-331`)
moves from the dock to the rail (`within(rail)`); the second-fetch test (`:191-208`) is deleted
(there is no unbounded task fetch any more — note in the commit message); every existing
agent-selection / switcher test unchanged.

### S7 — Storybook + browser check

**Files:** `client/src/features/home/tasks/TasksSection.stories.tsx` seeding `homeTaskKeys.list`,
`agentKeys.all` and `attentionKeys.all` from contract fixtures via `setQueryData` (the
`DelegationsBoard.stories.tsx` decorator; pin `Date.now`), then `npm run screenshots -- tassection`
→ `docs/ui-screenshots/`.

Browser (user rule: UI work is not done on tests alone; `docs/external-site-operations.md` lane,
`client-mode.ps1 -Status` before trusting 17203): desktop ≥ 1280 — rail shows agents then the
five groups, a Review card carries the warning border, the live `Dispatched Plan` delegate renders
under CARD-0002, the kebab opens without opening the modal, a card click opens `CardModal` with a
working `MoveMenu`, a task click opens the delegation modal; `AttentionGlance` unchanged in the
header; mobile 375 × 667 unchanged (three bands).

---

## What this card does not do

- Any storage, state-machine, retry, watchdog, dispatcher or `CardWorkTransitionService` change.
- New `AttentionKind` values, a new attention surface, a reply box on Home, or bucket counts —
  CARD-0300 owns the glance and `/attention`.
- The fleet-wide stage view (CARD-0301) or any use of `GET /api/agent-tasks/pipeline`.
- The mobile home page. `MobileHomePage.tsx` and its bands are untouched; a later card can point
  its "In motion" band at this projection.
- Card write verbs beyond Spawn in the kebab (decide / approve / move stay in `CardModal`).
- The board and delegations views themselves.
- A `directory` query parameter on the endpoint (client filter, as today).

## Left open, deliberately

1. **Plan-ready cards.** `AgentTaskPipelineStatusService.IsVerifiedPlanDeliverable` /
   `CodeConsumesReadiness` (`:178`, `:193`) could stamp `ReadyFor = "Code"` on an Up-next card whose
   Plan landed. It is the pipeline's rule; adopt it here only after CARD-0301 fixes its shape.
2. **Caps (8 / 8 / 12) and rail width (300 px)** are recommendations — adjust on sight in S7.
3. **A card whose project has no `LocalRepositoryPath`** is invisible on Home until someone sets
   one (all live projects have it). Not worth a second mapping.

## Test matrix

| Layer | Test | Command |
|---|---|---|
| Server | `HomeTaskServiceIntegrationTests` (11 cases above) | `dotnet run --project tests/Antiphon.Tests -- --treenode-filter "/*/Antiphon.Tests.Application/HomeTaskServiceIntegrationTests/*"` |
| Contract | `ContractSnapshotTests` `home-tasks.json` | `dotnet run --project tests/Antiphon.E2E -- --treenode-filter "/*/*/ContractSnapshotTests/*"` |
| Client pure | `homeTasksModel.test.ts` | `pwsh -File scripts/test-client.ps1 homeTasksModel` |
| Client components | `TaskCard`, `TasksSection`, `HomeTaskModal`, `DelegationTaskModal` | `pwsh -File scripts/test-client.ps1 features/home/tasks` and `DelegationTaskModal` |
| Regression gate | `TaskDrawer.test.tsx` unmodified | `pwsh -File scripts/test-client.ps1 TaskDrawer` |
| Page | `HomePage.test.tsx` | `pwsh -File scripts/test-client.ps1 HomePage` |
| Visual | `TasksSection.stories.tsx` screenshots | `npm run screenshots -- tassection` |

Build to an alternate output path (`--property:OutputPath=bin-card0002/`, forward slash) while the
daemons hold `bin/`; delete the `bin-card0002` directories before finishing. Run `Antiphon.Tests`
chunked by namespace; after S1 re-run only the new class.

## Sequencing and risks

**Order:** S1 → S2 (fixture needs the endpoint) → S3 (independent; can run in parallel with S1
by a second worker) → S4 → S5 → S6 → S7. S1–S2 are a natural first landing; S3 a second; S4–S6 the
third; S7 last. Each slice commits and pushes with the real outcome.

| Risk | Disposition |
|---|---|
| 31 Review cards swamp the rail | Own group, capped at 8, after Running; "+N more → board" |
| Bound task nested under a card the project filter dropped (project without a path) | The task is then absent from Home too; it is still on `/orchestrator?tab=delegations`. Accepted; noted in Left open 3 |
| `CardModal` create-modal flash when `card` is null | Mount only after `useCard` resolves (S5 test pins it) |
| Question text differs from `/attention` | Impossible by construction — it *is* the attention evidence |
| `?tab=tasks` bookmarks and `ToReadBadge` link | Badge becomes a scroll-to; stale bookmark lands on chat |
| Second `TaskDetail` implementation drifts from the drawer | Extraction, not copy; `TaskDrawer.test.tsx` unmodified is the gate |
| A new `CardStatus` / `AgentTaskStatus` member | `HomeTaskService` switches exhaustively (compile error); `STATE_COLOR` totality test on the client |
| Payload growth on a busy fleet | Open items are bounded by real work; Done capped at 60 / 7 d; measure in S7 and lower the Done cap if the poll exceeds ~50 KB |
| Storybook story mocks data no fixture proves | Story seeds only from `home-tasks.json` captured by the contract test (S2) |

## Execution notes

- Do not put a question field on the DTO "for convenience" — decision 4 is the CARD-0300 boundary.
- Do not add the glance, a bucket count, or a reply box to the rail.
- `Role == Check` exclusion applies to the worker join too; a check reading is not a worker.
- Keep `Sessions` out of the projection; the rail never shows sessions and the summary card DTO
  already learned that lesson (`ToSummaryDto`).
- `stateLabel` from `boardVisuals.ts` is the only place "Needs decision" is spelled; reuse it.
- When deleting `ProjectTasksPanel.tsx`, grep for `tab=tasks` and `ProjectTasksPanel` — the
  badge link and the two HomePage tests are the only other references at `21002401`.
- Update `docs/features/008-home-workspace/proposal.md` §3.2 in S6 (right dock is chat-only; rail
  gains the Tasks section) rather than leaving the home owner doc describing a tab that is gone.
