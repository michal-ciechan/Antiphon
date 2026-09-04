# CARD-0378 — Nightly red 2026-09-04: three client failures, plus the build break behind them

**Date:** 2026-09-04

**Plan task:** 4e128ad5 (Frontier, plan only; the fix below was spiked in this worktree to verify it and then reverted — no production code is committed by this pass)

**Card:** CARD-0378 — Nightly red 2026-09-04: client 3 failed (filed automatically by CARD-0124's first live run, 17:48 Europe/London, at `829e516a`)

**Related:** CARD-0033 (question-first `BlockedQuestionCard`, `blocked` on the detail DTO), CARD-0294 (structured blocked notes; touched `BlockedQuestionCard` only, not these tests), CARD-0076 (the lint gate), CARD-0301 (`PipelineStagesPanel`, the build break in §2.4), CARD-0124 (the nightly that will verify this).

---

## 1. Verdict up front

**All three failures are stale tests and unlinted new code, not a regressed feature. Fix the tests and the four lint sites; nothing in the product changed for the worse.** One more red is waiting behind them: `tsc -b` (the first half of `npm run build`) fails on `origin/master` since CARD-0301 S4 landed at 20:46, three hours after the nightly cloned `829e516a`. Tomorrow's 00:30 run would report RED (BUILD) and skip the tests entirely, so this card cannot go green by the nightly without that one-line fix. It is included below.

The whole change is 6 files, 59 insertions, 12 deletions. It was applied here and verified: eslint 8 problems → 0, `tsc -b` 1 error → 0, the two failing tests pass, and every test file that exercises a touched component passes (§5). One code slice, Codex-terra tier is enough.

## 2. Root cause, one per failure

### 2.1 `lint.gate.test.ts` — eslint reports 8 problems (7 errors, 1 warning) in 3 files

`npx eslint . --max-warnings 0` in `client/` gives, at `3f347938` (identical list in the nightly's `client-tests.log`):

| File | Line | Rule | Introduced by |
|---|---|---|---|
| `features/delegations/TaskDetailBody.tsx` | 103:23, 105:5 (×4 references) | `react-hooks/refs` — ref read/written during render | `25dbf35a` CARD-0033 S3, 09-03 |
| `features/home/tasks/TaskCard.tsx` | 63:9 | `react-hooks/purity` — `now = Date.now()` default parameter | `bbe0224d` CARD-0031 S4–S5, 09-02 |
| `features/orchestrator/BacklogSection.tsx` | 35:25 | `react-hooks/preserve-manual-memoization` — `useMemo(() => groupBacklog(list), [cards.data?.cards])` reads `list`, declares `cards.data?.cards` | `d70c557a` CARD-0094 S1, 09-02 |
| same | 35:51 | `react-hooks/exhaustive-deps` (warning) — same line | same |

Nothing about the tooling moved: `eslint.config.js` and `package-lock.json` last changed 2026-08-28 (`eslint-plugin-react-hooks` 7.0.1, whose `recommended` flat config already carries the React Compiler rules `refs`, `purity`, `preserve-manual-memoization`). Every flagged line was written on 09-02 or 09-03 by a delegate that did not run the gate. So the test's expectation is not stale — the gate is doing exactly what CARD-0076 built it for, and the fix is the code, not the rule. No suppressions are needed; each site has an idiomatic rewrite (§3).

### 2.2 `DelegationsBoard.test.tsx` › opens the drawer on a chip, with the delegate's own words

Not a split-text problem. The question is **not in the DOM at all**. Since CARD-0033 S3 (`25dbf35a`), `TaskDetailBody` renders a Blocked task from `detail.blocked` (`BlockedContextDto`, question-first card) and explicitly hides `detail.result` when the status is Blocked (`TaskDetailBody.tsx:242`: `detail.result && summary.status !== 'Blocked'`). The test's `detailFor()` fixture still supplies the question the pre-CARD-0033 way — `result: 'Should I keep the old cmd examples alongside?'` with no `blocked` — so `BlockedQuestionCard` returns `null` and the drawer shows a title and nothing else. CARD-0033's plan §9 listed the tests S3 had to update (`TaskDrawer.test`, `BlockedReplyRow.test`, `AttentionPanel.test`) and missed this file and the next one; the S3 delegate did not run the whole suite.

### 2.3 `DelegationTaskModal.test.tsx` › opens with a Blocked detail and shows the question plus the answer box

Same cause, twice over. The fixture has `result` but no `blocked`, so nothing Blocked renders; and the heading it waits for, `The delegate asked`, was deleted by the same commit (the `Section title={... 'The delegate asked' : 'Report'}` variant went with the reorder; the string survives only in doc comments). The modal wraps the same `TaskDetailBody` as the drawer, so the same fixture shape fixes it. The placeholder it also asserts, `e.g. yes, accept negatives`, still exists on the new reply box, unchanged.

### 2.4 Found while verifying: `tsc -b` fails on `origin/master` (not on the card yet)

```
src/features/orchestrator/PipelineStagesPanel.tsx(182,40): error TS2339: Property 'drawer' does not exist on type '{ drawer: string; } | { to: string; }'.
```

Reproduced at clean `3f347938` with the spike removed. `PipelineRow` narrows `row.target` with `if ('to' in row.target) return …`, then reads `row.target.drawer` inside the `onClick` closure. TypeScript does not carry narrowing of a property-of-a-parameter into a callback, so the union is back to unnarrowed there. Vite's dev/build bundling does not typecheck, which is why the running app and CARD-0301's screenshots were fine and nobody saw it. `npm run build` is `tsc -b && vite build`, so the nightly's build step will fail on it. Introduced by `9ab66a51` (CARD-0301 S4, 2026-09-04 20:46), already on `origin/master`.

## 3. Fix approach, per site

1. **`TaskDetailBody.tsx` — `wasBlocked` ref → guarded state.** Replace `useRef` + write-in-render with `useState(Boolean(detail.blocked))` and `if (detail.blocked && !wasBlocked) setWasBlocked(true)`. That is React's documented "storing information from previous renders" pattern; `react-hooks/set-state-in-render` accepts the conditional form (verified: the gate is clean with it). Semantics are unchanged: the "Answered via … — the delegate is working" line still appears only when this mounted body saw the task Blocked. (It also still does not reset if the same mounted body is pointed at a different task — the ref had that too; out of scope.)
2. **`TaskCard.tsx` — `now = Date.now()` default → mount-time fallback.** Take the prop as `now: nowProp = null`, add `const [mountedAt] = useState(() => Date.now())` and `const now = nowProp ?? mountedAt`. The lazy initializer is where the impure call is allowed (same pattern `BacklogSection` already uses for `useState(() => new Date())`). The only production caller (`TasksSection.tsx:138`) already passes `now`; the 13 `TaskCard.test.tsx` renders that omit it keep compiling because the prop stays optional.
3. **`BacklogSection.tsx` — memo deps.** Memoize `list` itself on `cards.data?.cards`, then `useMemo(() => groupBacklog(list), [list])`. Declared and inferred dependencies now match, and `list` stops being a fresh `[]` every render while loading.
4. **`PipelineStagesPanel.tsx` — const narrowing.** `const target = row.target` before the `in` check; use `target.to` / `target.drawer`. A `const` binding keeps its narrowing inside closures.
5. **Both test fixtures — supply `blocked`.** Add a `BlockedContextDto` (`kind: 'Question'`, the same question string, `progress: null`, `priorRounds: []`) to `detailFor()` in `DelegationsBoard.test.tsx` when the status is Blocked, and to the modal test's `detail(...)` call. Keep `result` as well — the server sends both. Replace the modal's dead `findByText('The delegate asked')` with `findByTestId('blocked-question')` (the question `Paper` in the full card) and keep its other two assertions as they are. No flexible matcher is needed anywhere: the question renders in one `Text` node, and so does the goal.

Do not: relax anything in `eslint.config.js`, add `eslint-disable`, or widen a timeout. A shared `blocked()` fixture builder (`BlockedQuestionCard.test.tsx` and `TaskDrawer.test.tsx` each inline one) would be tidy but is not this card.

## 4. The verified diff

Applied in worktree `card-task-4e128ad5` on top of `3f347938`, verified as in §5, then reverted so this pass commits only the plan. It should apply cleanly to `origin/master` with `git apply`; if `TaskCard.tsx` or `TaskDetailBody.tsx` have moved by then, the hunks are small enough to redo by hand from §3.

```diff
diff --git a/client/src/features/delegations/DelegationTaskModal.test.tsx b/client/src/features/delegations/DelegationTaskModal.test.tsx
index 086c9778..65c5fd61 100644
--- a/client/src/features/delegations/DelegationTaskModal.test.tsx
+++ b/client/src/features/delegations/DelegationTaskModal.test.tsx
@@ -67,10 +67,31 @@ function serve(body: AgentTaskDetailDto, extra: Parameters<typeof server.use> =
 
 describe('DelegationTaskModal', () => {
   it('opens with a Blocked detail and shows the question plus the answer box', async () => {
-    serve(detail({ status: 'Blocked', completedAt: null }, { result: 'Should I accept negative inputs?' }))
+    // CARD-0033: a Blocked detail carries the question in `blocked`, and the modal renders the
+    // question-first card from it — `result` alone renders nothing when Blocked.
+    serve(
+      detail(
+        { status: 'Blocked', completedAt: null },
+        {
+          result: 'Should I accept negative inputs?',
+          blocked: {
+            kind: 'Question',
+            round: 1,
+            blockedAt: '2026-08-07T10:12:00Z',
+            question: 'Should I accept negative inputs?',
+            context: null,
+            priorRounds: [],
+            progress: null,
+            canAnswer: true,
+            cannotAnswerReason: null,
+            mergeTaskId: null,
+          },
+        },
+      ),
+    )
     renderWithProviders(<DelegationTaskModal taskId={TASK_ID} onClose={() => {}} />)
 
-    expect(await screen.findByText('The delegate asked')).toBeInTheDocument()
+    expect(await screen.findByTestId('blocked-question')).toHaveTextContent('Should I accept negative inputs?')
     expect(screen.getByPlaceholderText('e.g. yes, accept negatives')).toBeInTheDocument()
     expect(screen.getByText('Should I accept negative inputs?')).toBeInTheDocument()
   })
diff --git a/client/src/features/delegations/DelegationsBoard.test.tsx b/client/src/features/delegations/DelegationsBoard.test.tsx
index 0a28075e..e4bfeeec 100644
--- a/client/src/features/delegations/DelegationsBoard.test.tsx
+++ b/client/src/features/delegations/DelegationsBoard.test.tsx
@@ -131,6 +131,22 @@ function detailFor(task: AgentTaskSummaryDto): AgentTaskDetailDto {
     events: [
       { type: 'Created', modelLevel: task.modelLevel, detail: 'Created.', at: task.createdAt },
     ],
+    // CARD-0033: a Blocked drawer renders from `blocked`, never from `result`.
+    blocked:
+      task.status === 'Blocked'
+        ? {
+            kind: 'Question',
+            round: 1,
+            blockedAt: task.createdAt,
+            question: 'Should I keep the old cmd examples alongside?',
+            context: null,
+            priorRounds: [],
+            progress: null,
+            canAnswer: true,
+            cannotAnswerReason: null,
+            mergeTaskId: null,
+          }
+        : null,
   }
 }
 
diff --git a/client/src/features/delegations/TaskDetailBody.tsx b/client/src/features/delegations/TaskDetailBody.tsx
index 9ca2ac2e..67ad11cb 100644
--- a/client/src/features/delegations/TaskDetailBody.tsx
+++ b/client/src/features/delegations/TaskDetailBody.tsx
@@ -99,10 +99,12 @@ function TaskDetail({ detail, onClose }: { detail: AgentTaskDetailDto; onClose:
   const [rerouteLevel, setRerouteLevel] = useState<string | null>('Frontier')
   const [expandedEvent, setExpandedEvent] = useState<number | null>(null)
   const stampedTask = useRef<string | null>(null)
-  const wasBlocked = useRef(Boolean(detail.blocked))
-  if (detail.blocked) wasBlocked.current = true
+  // "Storing information from previous renders" (react.dev): a guarded set during render, not a
+  // ref read in render, which eslint react-hooks/refs rejects (CARD-0378).
+  const [wasBlocked, setWasBlocked] = useState(Boolean(detail.blocked))
+  if (detail.blocked && !wasBlocked) setWasBlocked(true)
   const answeredElsewhere =
-    wasBlocked.current && !detail.blocked && (summary.status === 'Working' || summary.status === 'Dispatched')
+    wasBlocked && !detail.blocked && (summary.status === 'Working' || summary.status === 'Dispatched')
 
   const running = summary.status === 'Dispatched' || summary.status === 'Working'
   const settled = summary.status === 'Succeeded' || summary.status === 'Failed' || summary.status === 'Canceled'
diff --git a/client/src/features/home/tasks/TaskCard.tsx b/client/src/features/home/tasks/TaskCard.tsx
index 7307e866..acff6c20 100644
--- a/client/src/features/home/tasks/TaskCard.tsx
+++ b/client/src/features/home/tasks/TaskCard.tsx
@@ -12,6 +12,7 @@ import {
   UnstyledButton,
 } from '@mantine/core'
 import { notifications } from '@mantine/notifications'
+import { useState } from 'react'
 import {
   TbDotsVertical,
   TbTerminal2,
@@ -60,7 +61,7 @@ export function TaskCard({
   liveness = null,
   pipelineRow = null,
   pipeline = null,
-  now = Date.now(),
+  now: nowProp = null,
   onOpen,
   onOpenTask,
   onSelectAgent,
@@ -71,7 +72,7 @@ export function TaskCard({
   liveness?: AttentionItemDto | null
   pipelineRow?: HomeTaskPipelineRow | null
   pipeline?: AgentTaskPipelineDto | null
-  now?: number
+  now?: number | null
   onOpen: () => void
   onOpenTask?: (taskId: string) => void
   onSelectAgent?: (agentId: string) => void
@@ -80,6 +81,10 @@ export function TaskCard({
   const retry = useRetryAgentTask()
   const escalate = useEscalateAgentTask()
   const cancel = useCancelAgentTask()
+  // Callers that care about time pass `now`; otherwise the card's clock is its mount time. A
+  // `Date.now()` default parameter is an impure call during render (react-hooks/purity, CARD-0378).
+  const [mountedAt] = useState(() => Date.now())
+  const now = nowProp ?? mountedAt
 
   const reason = item.humanReason
   const borderColor =
diff --git a/client/src/features/orchestrator/BacklogSection.tsx b/client/src/features/orchestrator/BacklogSection.tsx
index b7e76a67..63bf56db 100644
--- a/client/src/features/orchestrator/BacklogSection.tsx
+++ b/client/src/features/orchestrator/BacklogSection.tsx
@@ -31,8 +31,8 @@ export function BacklogSection() {
   const isMobile = useMediaQuery('(max-width: 48em)') ?? false
   const [now] = useState(() => new Date())
 
-  const list = cards.data?.cards ?? []
-  const boxes = useMemo(() => groupBacklog(list), [cards.data?.cards])
+  const list = useMemo(() => cards.data?.cards ?? [], [cards.data?.cards])
+  const boxes = useMemo(() => groupBacklog(list), [list])
   const boardCount = boardsPresent(list)
   const showBoard = boardCount > 1
   const boardNameById = useMemo(
diff --git a/client/src/features/orchestrator/PipelineStagesPanel.tsx b/client/src/features/orchestrator/PipelineStagesPanel.tsx
index 28fa4c96..e16cf351 100644
--- a/client/src/features/orchestrator/PipelineStagesPanel.tsx
+++ b/client/src/features/orchestrator/PipelineStagesPanel.tsx
@@ -158,11 +158,14 @@ function PipelineRow({
     </Group>
   )
 
-  if ('to' in row.target) {
+  // A const binding: TypeScript keeps the `in` narrowing inside the onClick closure below, which it
+  // drops for `row.target` (a property of a parameter) — `tsc -b` failed on that (CARD-0378).
+  const target = row.target
+  if ('to' in target) {
     return (
       <UnstyledButton
         component={Link}
-        to={row.target.to}
+        to={target.to}
         w="100%"
         py={6}
         aria-label={row.ariaLabel}
@@ -179,7 +182,7 @@ function PipelineRow({
       py={6}
       aria-label={row.ariaLabel}
       data-testid={`pipeline-row-${row.key}`}
-      onClick={() => onOpen(row.target.drawer)}
+      onClick={() => onOpen(target.drawer)}
     >
       {body}
     </UnstyledButton>
```

## 5. Verification design

What the plan pass already ran with the diff applied (worktree `C:\Antiphon\worktrees\card-task-4e128ad5`, fresh `npm ci`, 2026-09-04 21:30–21:41):

| Check | Before | After |
|---|---|---|
| `npx eslint . --max-warnings 0` in `client/` | 8 problems, exit 1 | 0, exit 0 |
| `npx tsc -b` in `client/` | 1 error (`PipelineStagesPanel.tsx:182`), exit 2 | exit 0 |
| `scripts/test-client.ps1 DelegationTaskModal.test DelegationsBoard.test TaskDrawer.test TaskCard.test BacklogSection.test lint.gate.test` | 2 target failures + lint gate red | 56 passed, 1 failed (see next row) |
| `scripts/test-client.ps1 DelegationsBoard.test` (isolation re-run, per `docs/testing-and-build.md`) | — | 12 passed |
| `scripts/test-client.ps1 PipelineStagesPanel.test` | — | passed |

The one failure in the six-file run was `opens the drawer for a settled task named in the URL even when it is not on the board` (`findByText` timing out under a 40 s environment-setup load), and it passed in isolation. It is untouched by this diff and is not this card; if it reappears in the nightly, it wants its own card, not a wider timeout.

What the code slice must run before committing, in this order, from the repo root:

1. `cd client && npx eslint . --max-warnings 0` — exit 0. (The lint gate test runs the same thing; this is the fast read.)
2. `cd client && npx tsc -b` — exit 0. This is the half of `npm run build` the nightly will fail on without §2.4.
3. `pwsh -File scripts/test-client.ps1` with no filter — the whole client suite, ~5–7 minutes; read the `CLIENT TESTS EXIT CODE` line, never a pipeline's exit code. Expected: 722 passed, 0 failed (719 + the 3 that were red). The commit message carries those counts and the run's failures, if any, verbatim.
4. No .NET build is needed; nothing outside `client/` changes.

What closes the card: the next nightly (00:30 Europe/London, `u/lndcobra/antiphon_nightly_tests`) runs against `origin/master`. Once this lands, its report should read `npm run build | pass`, `client | pass`, and `### Fixed since last run (3)` naming the three tests; `nightly-report.ps1` then auto-closes CARD-0378 only if nobody has assigned or moved it (CARD-0124 D7), so an operator who has it in Execute closes it by hand after reading that run. Land before 00:30 for that to happen tonight; otherwise tomorrow's run is the check. A `-Land` from a worktree must also `git pull --rebase` the main checkout before any AppHost restart (CARD-0358) — although nothing here changes the served app's behaviour, so the restart is not urgent.

## 6. Deliberately not in scope

- A shared `blocked()` test fixture builder — three files now inline one; a small tidy for whenever a fourth appears.
- Resetting `wasBlocked` when a mounted `TaskDetailBody` is pointed at a different task (pre-existing in the ref version, preserved as-is).
- The load-sensitive `settled task named in the URL` test.
- Why two consecutive delegates (CARD-0033 S3, CARD-0301 S4) landed with the client gate red or the build broken: the nightly caught both within a day, which is the guard CARD-0124 was built to be. A pre-land `npm run build` + `lint` requirement for client-touching slices belongs in the orchestration docs, not here.
