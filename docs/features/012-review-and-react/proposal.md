# 012 — Review what an agent produced and react without leaving the page

**Status: proposed** (design only — nothing here is implemented). **Card:** CARD-0034 (story S4 in
`docs/product/user-stories.md`). Written 2026-08-26 against commit `1b1b667`.

## Summary

The measure on the card — *select a passage, say "change this", get a delegated task carrying the
passage* — **already works today**, in one place: the rendered view of `FilesReviewPanel`, for a
file belonging to an agent that still exists (`SelectionDelegate` + `SelectionComposer`, feature
008). Nothing in this design touches that mechanism.

What is missing is everything that gets a person *to* that gesture when they did not produce the
work and do not know who did:

1. **Nothing says something is waiting.** The attention projection is, by its own contract, a list
   of what is *stuck*, never of what is *done and unread*. A Plan delegate succeeds and the only
   trace is a "Done" row in a list nobody is looking at.
2. **The deliverable is keyed by an agent that no longer exists.** Every review primitive — files
   listing, marks, section marks, threads, checkpoints — hangs off `agentId`, and a pool delegate's
   agent row is deleted the moment its task settles, cascading all of that away. The plan it wrote
   is reachable only by prose in its report and by a branch name.
3. **The two surfaces that do show a settled deliverable have no react verb at passage level.** The
   plan reader (`/plans`) and the task drawer's report both render text you cannot select into a
   task; the card thread's *Hand back* is coarse (identifier + path, no passage).

The design is therefore **three small additions and zero new review or delegation machinery**: a
`ReadAt` stamp on tasks plus a "To read" badge that reuses the Needs-attention badge's rules; a
task→deliverable pointer so a settled task's plan is one click away regardless of the agent; and
the existing selection wrapper mounted on the plan reader and the drawer report.

---

## 1. What exists today (verified against the code, 2026-08-26)

### 1.1 The reading surface (features 008 + 009) — shipped and good

| Capability | Where | Verified by |
|---|---|---|
| File tree + diff/raw/rendered viewer, file-level Viewed/Reviewed marks, "unviewed" filter and count | `client/src/features/agents/FilesReviewPanel.tsx` (count at `:194`, filter `:153–166`) | `FilesReviewPanel.test.tsx` |
| Baselines: HEAD / latest checkpoint / explicit commit | `POST /api/agents/{id}/review/checkpoint` (`server/Api/Endpoints/ReviewEndpoints.cs:96`), `AgentReviewCheckpointService.cs` | — |
| Section marks (hash-anchored, auto-collapse, stale badge), rendered diff modes | `client/src/features/agents/RenderedMarkdownReview.tsx`, `GET/POST /api/agents/{id}/review/sections` (`ReviewEndpoints.cs:82–94`) | `RenderedMarkdownReview.test.tsx` |
| Line-anchored threads, dispatched into the agent's persistent session, replies routed back | `ReviewThreadService.cs` (dispatch `:140`, requires the agent's persistent session `Running` at `:149–155`), `ReviewReplyDispatcher.cs` | — |

Mounted three times: embedded on `/agents` (`AgentsPage.tsx:207` links out), full-screen at
`/agents/:id/files` (`AgentFilesPage.tsx:107`, URL-backed file selection via
`useFilesViewUrlState.ts`), and as the centre pane of the desktop home (`HomePage.tsx:223`, sidebar
layout, no URL state).

### 1.2 The react gesture — shipped, and it is the card's measure

`client/src/features/agents/SelectionDelegate.tsx`:

- `SelectionDelegate` (`:25`) wraps the rendered view (`FilesReviewPanel.tsx:863`). A mouse-up with
  a non-empty selection floats a **Send to agents** button at the selection's end (`:66–82`).
- `SelectionComposer` (`:88`) shows the passage as a quote, an instruction box, and Docs/Code/Plan
  role chips (Docs preselected for markdown, `FilesReviewPanel.tsx:912`). Submit calls
  `POST /api/agent-tasks` with `goal = buildSelectionGoal(path, selection, instruction)`
  (`selectionGoal.ts:5` — the passage quoted line by line), `kind: Worker`, `workspace: null` (the
  pool's pickup path), `workingDirectory` = the agent's workspace root, `scopeGlob` = the file
  (`:110–119`). **More options…** opens `DelegateModal` with the same goal prefilled (`:198–206`).
- Pinned by `SelectionDelegate.test.tsx` (goal quoting; queue with quoted goal / null workspace /
  path scope; role chips change the tier; empty instruction refused) and
  `DelegateModal.test.tsx:155` (path prefilled as goal and scope lease).

So: for a **standing agent** whose files you are looking at, the loop is closed today — read, mark
sections, comment-and-dispatch a thread, or select-and-queue a task. Verified by reading the code
and its tests; not walked through in a browser in this investigation.

### 1.3 Coarse react and approve — shipped (CARD-0035 M-slices, mobile-thread spec)

`client/src/features/thread/CardThreadPanel.tsx`, mounted in `CardModal.tsx:213` (the *Thread*
tab; default tab on mobile, `CardModal.tsx:175`) and full-screen at `/thread/:cardId`:

- `HandBackButton` (`:426`) opens `DelegateModal` with goal
  `"<CARD-nnnn> — change requested on plan <path>: "` (`:436–439`). No passage, no scope glob.
  Shown on subject plans (`:248`) and on a settled task's report (`:621`). Test
  `CardThreadPanel.test.tsx:260`.
- `ApprovePlanButton` (`:319`) — a card move with `reason: "plan approved: <file>"`.

### 1.4 The plan reader — shipped, agent-independent, unlinked on desktop

- `PlanCatalogService.cs` scans **`docs/superpowers/specs/*.md` and `docs/features/*/proposal.md`
  only** (`:55–56`). It does **not** scan `docs/superpowers/plans/` — 101 files today versus 25 in
  `specs/`, and the directory every Plan-role delegate is briefed to write into (this card's own
  brief included). Confirmed live: `GET /api/plans?path=C:\src\Antiphon` returns specs and
  proposals, no `plans/` entries.
- Reads from **disk only** (`ReadAsync`), so a plan on an unmerged `feat/card-task-<id>` branch is
  invisible (the spec's §9 item 3 accepted this). `GitWorkspaceService.GetContentAtAsync(root,
  relativePath, ref)` (`GitWorkspaceService.cs:144`) already exists and is unused here.
- `client/src/features/plans/PlanReaderPage.tsx`: catalog list → ToC-first reader
  (`RenderedMarkdown` per section, `:263–293`). **No selection wrapper, no marks, no verbs.**
- Route `/plans` is registered (`App.tsx:130`) but nothing on the desktop links to it: the nav is
  Home/Workflows/Boards/Agents/Channels/Orchestrator/Settings (`shared/Layout.tsx:71–77`); the
  only inbound links are the mobile away-band (`MobileHomePage.tsx:387`) and the card thread.

### 1.5 Discovery signals — none of them mean "produced and unread"

- `GET /api/attention` (`AttentionService.cs`, kinds in `AttentionDtos.cs:13`): 13 conditions, all
  variants of stuck/broken. Its file header (`client/src/api/attention.ts:9–13`) forbids the client
  widening membership, and the server's non-membership rule is the feature. **It must not grow a
  "waiting for review" kind** — that would make every quiet day look stuck.
- Desktop home shows only `NeedsAttentionBadge` (`HomePage.tsx:327`, renders nothing at zero).
- The mobile away-band lists plans whose `modifiedAt` falls in the window (`awayDelta.ts`
  `newPlans`, spec §D3) — the one existing "new thing to read" signal, mobile-only, and blind to
  `plans/` for the reason in §1.4.
- `unviewedCount` (`FilesReviewPanel.tsx:194`) is client-side, per selected agent, visible only
  inside the panel. `AgentSummaryDto` carries no review counts (`AgentDtos.cs`).
- Task rows: `ProjectTasksPanel.tsx` (home *Tasks* tab) lists Done tasks with no read/unread state;
  `TaskDrawer.tsx:209–219` renders `result` as pre-wrap text and `resultFilePath` as inert `<Code>`.
  Its *Files* link (`:189`) opens `/agents/{agentId}/files` — see §1.6 for why that is dead for a
  settled pool task.

### 1.6 Why a settled delegate's work is unreachable — the structural gap

- `AgentTaskService.RemoveEphemeralAgentAsync` (`AgentTaskService.cs:687–696`) deletes the pool
  agent row on settle/cancel/escalate (`:506`, `:650`; `AgentTaskDispatcher.cs:592`, `:1198`).
- `FileReviewStates`, `FileSectionReviews`, `AgentReviewCheckpoints`, `ReviewThreads` all have
  `HasOne(Agent).OnDelete(Cascade)` (`AppDbContext.cs:69–125`). Marks and threads on a delegate's
  output die with it.
- `AgentFilesService.GetFilesAsync` returns null (→ 404) when the agent row is gone
  (`AgentFilesService.cs:150–152`). `TaskDrawer`'s *Files* link and `/agents/{id}/files` are
  therefore dead links for every settled pool task.
- Home's files pane requires a selected agent (`HomePage.tsx:222–244`); a worktree "nobody works
  in" is switchable but shows only an empty state (`HomePage.test.tsx:332`).
- Checkpoints are captured automatically only on **card** completion (`CardService.cs:305`) —
  never for a delegated task, so a self-committing Shared delegate leaves nothing to diff against.
- What a settled Plan task *does* leave: `Result` (prose that consistently names the plan path —
  both live samples checked, tasks `e4e4e442` and `17c504bb`), `ResultFilePath` (spill file),
  `WorktreeBranch` (`feat/card-task-<id>`; 95 such remote branches today), and, once the
  orchestrator merges, a file under `docs/superpowers/plans/` that the catalog does not scan.

### 1.7 A third review surface, noted and left alone

Board cards have their own diff review (`client/src/features/board/DiffReview.tsx`,
`CardReviewService.cs`, localStorage `useReviewedFiles.ts`) for card-bound branches. It is
card-scoped, not agent-scoped, and predates 008. This design does not touch it; §5 says why.

---

## 2. What this design adds

### 2.1 A "To read" signal — one badge, same rules as Needs attention

**Surface:** the desktop home header, immediately left of `NeedsAttentionBadge` (`HomePage.tsx:168`).
Reads `To read (n)`, colour `violet` (the delegation colour used by *Delegate work* and *Send to
agents*), renders **nothing at zero** — the same rule and the same justification as the attention
badge's doc comment (`HomePage.tsx:319–322`). Links to the home *Tasks* tab with the Done group
scrolled into view (a `?tab=tasks` on `/` is a new but trivial param; no new page).

**Membership (client-computed from `useAgentTasks()`, which home already polls):** a task with
`status == Succeeded`, `readAt == null`, `completedAt` within the last 7 days, scoped to the
selected project's `dirKeys` exactly as `ProjectTasksPanel` scopes rows. Failed/Canceled are
excluded — those are attention's territory or nobody's. Check-interpretation tasks are excluded
the way `awayDelta.ts` excludes them.

**Why a client computation and not a projection endpoint:** every input is already on the summary
DTO the page polls every 5 s; a `GET /api/review/inbox` would be a second derivation of the same
rows (the mobile spec's IA claim, §D7: consume existing projections, do not add a third).

**Reading order inside the Tasks tab, and why:** *In flight* stays first — S1's order puts "what is
being worked on" above "what is waiting for review", and a person who opens the tab to find a plan
still benefits from seeing what is running before they queue more. Within *Done*, unread rows sort
first, then by `completedAt` desc. An unread row carries a filled violet dot before its title and
a **Read** link (§2.2); a read row has neither. No count on the *Done* label — the badge is the
count.

### 2.2 The task carries a pointer to its deliverable

**Server:** two nullable columns on `AgentTask`, both set once at settle in
`AgentTaskReplyService` next to `ResultFilePath` (`AgentTaskReplyService.cs:417`), and surfaced on
`AgentTaskSummaryDto` and the detail DTO:

- `DeliverablePath` — the first repo-relative markdown path in the report that resolves: on disk
  under `WorkingDirectory`/`RepoPath`, or via `GetContentAtAsync(repo, path, WorktreeBranch)` on
  a worktree task. Matcher: `` `?(docs/[\w./-]+\.md)`? `` over `Result`, first hit that resolves;
  null otherwise. The input (the report) is immutable, so storing the parse result is a cache of a
  frozen string, not a second store of a live fact.
- `DeliverableRef` — `WorktreeBranch` when the file resolved only on the branch; null when it is
  on disk (merged, or a Shared task).
- `ReadAt` — stamped by `POST /api/agent-tasks/{id}/read` (idempotent; first stamp wins). Nothing
  else writes it. One operator system: "read" means read by anyone.

**Client:** a settled task row (`ProjectTasksPanel.tsx`, `CardThreadPanel.tsx` task rows,
`TaskDrawer.tsx`) with a `deliverablePath` gets a **Read** link →
`/plans?file=<path>&ref=<ref>&task=<id>`; with none, the existing drawer/report is the reading
surface and the drawer's *Report* section stamps `read` on open. The *Files* link in `TaskDrawer`
(`:189`) is shown only while the agent row exists (`agentId` non-null **and** the task not
settled) — today it is a dead link on every settled pool task.

**Why the plan reader and not a task-scoped `FilesReviewPanel`:** re-keying the four review tables
plus their nine endpoints by task is a schema and API change across the whole review surface for
one consumer, and it would still leave the marks meaningless after the worktree is pruned. The
reader is already agent-independent, already ToC-first (which is how a 20 000-char plan is read
anyway), and needs only §2.3 to close the loop. The cost — no hash-anchored section marks on a
*plan* — is real and stated in §5.

### 2.3 The plan reader learns to read from a branch, and to react

**Server (`PlanCatalogService` / `PlanEndpoints`):**

- Third root: `docs/superpowers/plans/` → `PlanKind.Plan = 2` (append, never renumber — same rule
  as the attention enum). The reader row label reads "plan" as it reads "proposal" today.
- `GET /api/plans/content?path=&file=&ref=`: when `ref` is given, resolve the file name exactly as
  today (the 422 refusal boundary is unchanged — `ref` never widens which paths are readable), then
  read via `GetContentAtAsync(root, file, ref)` instead of disk. A ref that does not resolve is a
  **404** ("not on `<ref>`"), distinct from the 422. The catalog list stays disk-only: listing every
  branch's plans is a different feature and would put 95 half-finished branches in the list.

**Client (`PlanReaderPage.tsx`):**

- Wrap the section bodies (`:263–293`) in `SelectionDelegate` and host a `SelectionComposer`
  **outside** the section scroll — the exact placement rule the component documents (`:20–23`).
  `workingDirectory` = the catalog root, `scopeGlob` = the file, `defaultRole` = `Docs` for a
  plan/spec, `Plan` when `?task=` names a Plan-role task (re-planning is the common hand-back).
- `buildSelectionGoal` gains an optional leading context line — `Re CARD-nnnn (task <shortId>):`
  when the reader was opened with `?task=` (or the plan's `cards[0]`) — so the delegate can find the
  card and the prior task. Existing callers pass nothing and are byte-identical (extend
  `SelectionDelegate.test.tsx`, do not change its three assertions).
- Opening a plan with `?task=` calls `POST /api/agent-tasks/{id}/read` once.
- Sticky header gains the existing `HandBackButton` when the plan has a subject card (coarse verb
  beside the fine one; both open the same modal family).

**Entry points on desktop** (finding without knowing the agent):

- The Read links of §2.2.
- A dimmed `Plans` anchor in the home header next to *Delegations board* (`HomePage.tsx:169`),
  passing `?path=<selected project path>` so the catalog is this project's. **Not** a nav entry —
  the nav is seven items and the reader is a project-scoped surface, not a global one.

### 2.4 The task drawer's report becomes selectable

`TaskDrawer.tsx:209–214`: render `detail.result` through `RenderedMarkdown` (it is markdown — every
report in the live sample uses headings and bold) inside `SelectionDelegate`, composer below the
section, `workingDirectory` = `summary.workingDirectory`, no `scopeGlob` (a report is about the
work, not a file). Same goal context line as §2.3. This covers Debug/Test/Code deliverables that
have no file — the report *is* the deliverable.

---

## 3. What it deliberately does NOT do

- **No "waiting for review" attention kind.** See §1.5. Attention is stuck-only and its worth is
  that rule.
- **No unviewed-file counts in the agent rail or on `AgentSummaryDto`.** Computing git status per
  agent inside the 5 s agent poll is the cost; the count exists where the files are.
- **No inclusion of review threads (`AwaitingHuman`) or board cards in `Review` in the badge.**
  Threads are agent-scoped and live in the panel that owns them; cards in Review are feature 010's
  *Needs you* group. When 010 lands, the §2.1 items belong in that group and the header badge
  folds into it — 010 should consume `readAt`/`deliverablePath` from the summary DTO, not
  re-derive them.
- **No section marks on the plan reader.** Marks are agent-keyed and the reader has no agent. A
  plan is read whole, ToC-first; "read" is the task-level stamp.
- **No listing of plans on unmerged branches in the catalog.** Reading one by exact ref is enough;
  the merge rule ("plans land on master fast") stays the way plans become findable by browsing.
- **No new notification channel** (toast, Telegram, SignalR). The badge is a pull signal on the
  page the person lands on; the mobile away-band already covers "since you were away".
- **No change to `SelectionDelegate`/`SelectionComposer`/`DelegateModal` behaviour** — only new
  mount points and an optional goal prefix.
- **No task-scoped `FilesReviewPanel`, no re-keying of review tables.** §2.2 says why.
- **No `ignoreSubscriptionQuota` on the composer.** A CARD-0136 409 surfaces as the composer's
  existing error toast; *More options…* → the full modal is the recovery today and stays so.

---

## 4. Server endpoints and projections (all of it)

| Change | Kind | File |
|---|---|---|
| `AgentTask.ReadAt`, `DeliverablePath`, `DeliverableRef` | 3 nullable columns + migration | `server/Domain/Entities/AgentTask.cs`, `Migrations/` |
| Set `DeliverablePath`/`DeliverableRef` at settle | logic | `AgentTaskReplyService.cs` beside `:417` |
| `POST /api/agent-tasks/{id}/read` | endpoint (idempotent) | `AgentTaskEndpoints.cs` |
| `readAt`, `deliverablePath`, `deliverableRef` on summary + detail DTOs | projection | `AgentTaskDtos.cs:70` |
| `docs/superpowers/plans/` root, `PlanKind.Plan = 2` | catalog | `PlanCatalogService.cs:55`, `PlanDtos.cs:8` |
| `?ref=` on `/api/plans/content` via `GetContentAtAsync` | endpoint param | `PlanEndpoints.cs:32`, `PlanCatalogService.ReadAsync` |

No new read projection endpoint. No change to any `/api/agents/{id}/review/*` route.

---

## 5. What this costs the surfaces it shares screen with

- **Home header** gains up to two small things: a violet badge (absent when zero) and a dimmed
  *Plans* anchor. The header already holds the two switchers, the attention badge, the board link
  and *Delegate work*; on a narrow desktop the switchers' `truncate` widths absorb it. The anchor
  is the one permanent addition and it is the same weight as *Delegations board*.
- **Home Tasks tab** gains a dot and a link per unread Done row — no new group, no new list. Until
  feature 010 replaces this panel, this is the only list that changes; 010 inherits the fields.
- **Plan reader** gains a floating button on selection (nothing at rest) and a composer panel
  below the sections when one is open. On a phone the composer is the same width as the page;
  selection on touch is workable but the coarse *Hand back* remains the phone verb (mobile spec
  §D6), so the reader's header button is not hidden below `48em`.
- **Task drawer** grows: a rendered report is taller than pre-wrap text of the same content, and
  the composer adds a panel under it. The `ScrollArea.Autosize mah={320}` cap stays, so the
  drawer's height budget is unchanged.
- **What is given up:** hash-anchored section marks and rendered diff for plans read through the
  reader. A re-planned plan is re-read whole. If that proves to be the dominant loop, the next step
  is marks keyed by `(repoRoot, path)` rather than agent — a different card.

---

## 6. Failure and empty states

| State | What shows |
|---|---|
| No unread Done tasks | No badge. No dot on any row. The Done group looks exactly as today. |
| Report names no file, or the path never resolved | No **Read** link; the row opens the drawer as today; the drawer's report is the reading surface and stamps `read`. |
| `deliverableRef` branch gone (merged and pruned) but file on disk | Server resolves to disk first, so this reads fine; `deliverableRef` was null at settle if the file was already on disk. |
| Branch gone **and** file not on disk (renamed, or branch deleted unmerged) | Reader shows the existing not-found state ("not on `<ref>`") with a link back to the task drawer; the row keeps its dot until the drawer is opened. |
| `?file=` outside the three roots | Existing 422 refusal, unchanged wording. |
| Selection send refused (root not in `Delegation:AllowedRoots`, quota 409, validation) | The composer's existing error toast, body from the problem details. Nothing is stamped. |
| Selection made in a diff-tinted or collapsed section of the reader | Only expanded section bodies are inside the wrapper; a selection that spans two sections still yields one quote (the wrapper reads `selection.toString()`). |
| Task settled while the drawer is open | The summary poll updates `readAt`/`deliverablePath`; the Read link appears on the next render. |
| Plan reader opened with `?task=` for a task the caller cannot read (404) | Stamp fails silently (logged at Debug); the plan still renders. |

---

## 7. Verification

- Server (TUnit, `--property:OutputPath=bin-review/`): `PlanCatalogServiceTests` — `plans/` root
  listed with kind `Plan`, `?ref=` reads a file committed only on a branch, unknown ref → 404,
  refusal boundary unchanged with `ref` set. `AgentTaskReplyServiceTests` — deliverable derivation:
  disk hit, branch-only hit sets `DeliverableRef`, no hit → nulls, first-resolving-wins. Endpoint
  test: `read` is idempotent and stamps once.
- Client (vitest via `pwsh -File scripts/test-client.ps1`): `HomePage.test.tsx` — badge absent at
  zero, count matches project-scoped unread Succeeded; `ProjectTasksPanel` unread-first order and
  Read link; `PlanReaderPage.test.tsx` — selection floats the button, composer queues with root
  dir + file scope + context line, `?task=` stamps read once; `TaskDrawer.test.tsx` — report
  rendered as markdown, selection composer present; `SelectionDelegate.test.tsx` — the three
  existing assertions unchanged, one new for the context line.
- Live: browser-harness walkthrough — dispatch a Plan delegate, wait for settle, see the badge,
  follow Read to the branch-resident plan, select a paragraph, queue a task, see it in the Tasks
  tab and the badge drop to nothing.
