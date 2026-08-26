# CARD-0034 — review what an agent produced and react in place: investigation + slice plan

- **Status**: Proposed (planning only — task `b4e6383d`; nothing here is implemented)
- **Card**: CARD-0034
- **Design**: [docs/features/012-review-and-react/proposal.md](../../features/012-review-and-react/proposal.md)
  holds the full "what exists today, verified against the code" section, the design, what it
  deliberately does not do, and the failure states. This file is the slice list and the
  one-paragraph verdict; read the proposal first.

## Verdict

The card's measure — select a passage, say "change this", get a delegated task carrying the
passage — **works today** in the rendered view of `FilesReviewPanel`
(`client/src/features/agents/SelectionDelegate.tsx:25` wrapper, `:88` composer, mounted at
`FilesReviewPanel.tsx:863`/`:908`; `buildSelectionGoal` at `selectionGoal.ts:5`; pinned by
`SelectionDelegate.test.tsx`). Nothing new is needed for the gesture.

The gaps are exactly where the card said: **around** it.

| Gap | Root cause (file:line) |
|---|---|
| Nothing says a deliverable is waiting | `/api/attention` is stuck-only by contract (`client/src/api/attention.ts:9–13`); home header shows only `NeedsAttentionBadge` (`HomePage.tsx:327`); `ProjectTasksPanel.tsx` Done rows have no read state |
| Cannot find it without knowing the agent | every review primitive is `agentId`-keyed and cascade-deletes with the pool agent (`AgentTaskService.cs:687–696`, `AppDbContext.cs:69–125`); `GetFilesAsync` → null on a deleted agent (`AgentFilesService.cs:150`); `TaskDrawer.tsx:189` *Files* link is dead for every settled pool task; home files pane needs an agent (`HomePage.tsx:222`) |
| The plan catalog misses the plans | `PlanCatalogService.cs:55–56` scans `specs/` + `features/*/proposal.md`; **not** `docs/superpowers/plans/` (101 files) and disk-only (unmerged `feat/card-task-<id>` branches invisible) although `GitWorkspaceService.GetContentAtAsync` (`:144`) exists |
| No passage-level react on the two surfaces that show a settled deliverable | `PlanReaderPage.tsx:263–293` renders sections with no `SelectionDelegate`; `TaskDrawer.tsx:209–214` renders the report as pre-wrap text; `HandBackButton` (`CardThreadPanel.tsx:426`) is coarse (identifier + path, no passage) |

## Slices (each independently landable; S1→S2→S3 is the dependency chain, S4 is parallel)

| # | Slice | Files | Tests | Effort |
|---|---|---|---|---|
| **S1** | Plan catalog: third root `docs/superpowers/plans/` (`PlanKind.Plan = 2`), `?ref=` on `/api/plans/content` via `GetContentAtAsync`; unknown ref → 404, refusal boundary unchanged | `PlanCatalogService.cs`, `PlanDtos.cs`, `PlanEndpoints.cs`, `client/src/api/plans.ts` | `PlanCatalogServiceTests` (+4) | Codex terra, ~1h |
| **S2** | Task deliverable + read stamp: `AgentTask.ReadAt/DeliverablePath/DeliverableRef` + migration; derive at settle beside `AgentTaskReplyService.cs:417`; `POST /api/agent-tasks/{id}/read`; fields on `AgentTaskSummaryDto`/detail | `AgentTask.cs`, `AgentTaskReplyService.cs`, `AgentTaskEndpoints.cs`, `AgentTaskDtos.cs`, `client/src/api/agentTasks.ts` | `AgentTaskReplyServiceTests` deliverable derivation (+4), endpoint idempotence (+1) | Grok/Codex terra, ~2h (stop the server for the migration — AGENTS.md rule) |
| **S3** | Reader reacts: `SelectionDelegate` + `SelectionComposer` on `PlanReaderPage` (composer outside the section scroll), goal context line (`buildSelectionGoal` optional 4th arg), `?task=` stamps read, `HandBackButton` in the sticky header | `PlanReaderPage.tsx`, `selectionGoal.ts`, `SelectionDelegate.tsx` (prop only) | `PlanReaderPage.test.tsx` (+3), `SelectionDelegate.test.tsx` (+1, existing 3 untouched) | Codex terra, ~2h |
| **S4** | Home + drawer: `ToReadBadge` beside `NeedsAttentionBadge` (nothing at zero), unread-first Done rows with dot + **Read** link in `ProjectTasksPanel`, `Plans` anchor in the header, `TaskDrawer` report via `RenderedMarkdown` inside `SelectionDelegate`, *Files* link hidden once settled, `CardThreadPanel` task rows get the Read link | `HomePage.tsx`, `ProjectTasksPanel.tsx`, `TaskDrawer.tsx`, `CardThreadPanel.tsx` | `HomePage.test.tsx` (+2), `TaskDrawer.test.tsx` (+2), `CardThreadPanel.test.tsx` (+1) | Codex terra, ~2h |
| **S5** | Live walkthrough (browser-harness): dispatch a Plan delegate → settle → badge → Read → select → queue → badge clears. Record in `.antiphon/card-0034-live.md` | — | — | Codex luna, ~30m |

Run client tests through `pwsh -File scripts/test-client.ps1`; server tests with
`--property:OutputPath=bin-review/` (forward slash) and delete the `bin-review` directories after.

## Decisions taken in the design (yours to overturn)

1. **Reader, not a task-scoped `FilesReviewPanel`.** Re-keying four review tables and nine
   endpoints by task for one consumer is the expensive road; the reader is already
   agent-independent. Cost: no hash-anchored section marks on plans. Stated in proposal §5.
2. **Client-computed badge, no `/api/review/inbox`.** All inputs are on the polled summary DTO.
3. **Attention stays stuck-only.** No `AwaitingReview` kind — its own contract forbids it.
4. **`DeliverablePath` stored at settle, derived from the immutable report.** Not a live fact, so
   not a second store.
5. **No nav entry for Plans** — a header anchor scoped to the selected project.
6. When feature 010 lands, the badge folds into its *Needs you* group, consuming these fields.

## Not verified in a browser

The gesture's behaviour was established from the code and its tests and the API was queried live
(`/api/plans`, `/api/attention`, `/api/agent-tasks` on 17202); no browser walkthrough was run in
this investigation. S5 is that walkthrough.
