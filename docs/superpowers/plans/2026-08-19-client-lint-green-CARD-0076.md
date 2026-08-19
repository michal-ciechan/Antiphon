# CARD-0076 — client lint back to green, and gated (2026-08-19)

**Outcome: fixed in this session, not just planned.** All 31 problems are gone, `npm run lint` is
green (now with `--max-warnings 0`), and a lint gate now fails `npm test` on any regression.
Judgment call per the card: the findings clustered into three repeating patterns with a sanctioned
in-repo fix for each, so shipping beat planning — same path as CARD-0026/0015.

## The actual breakdown (deliverable 1)

Run on this worktree at master `7744b4d`:

| Rule | Count | Class |
|---|---|---|
| `react-hooks/set-state-in-effect` | 14 | **correctness** (render loops / lost input) |
| `react-refresh/only-export-components` | 14 | DX (fast refresh falls back to full reload) |
| `react-hooks/globals` | 1 | correctness (render-time side effect, test probe) |
| `@typescript-eslint/no-unused-vars` | 1 | dead code |
| `react-hooks/exhaustive-deps` | 1 (warn) | correctness-adjacent |

So: not one autofixable rule ×30, but also not 30 independent judgments — two patterns ×14 plus
three singletons.

## When it went red (deliverable 2)

`eslint.config.js` is unchanged since the project scaffold ("Story 1.1") and extends
`reactHooks.configs.flat.recommended` from **eslint-plugin-react-hooks v7**, whose compiler-derived
rules (`set-state-in-effect`, `globals`) are errors out of the box. There is no CI anywhere
(`.github/workflows/` holds only `publish-nuget.yml`), so lint has never gated: it was green at
scaffold and drifted red one un-run check at a time. "Restore green" therefore meant "make it green
for the first time since real code existed, then give it teeth."

## Fix vs suppress (deliverables 3 & 5)

**Zero suppressions were added.** Every finding got a real fix; one pre-existing
`eslint-disable-line react-hooks/exhaustive-deps` in IgnorePathModal was *removed* because the
restructure made it unnecessary.

### `react-hooks/set-state-in-effect` ×14 — three patterns

The codebase already contained the sanctioned replacement for each pattern, so the fixes follow
existing precedent rather than inventing style:

1. **"Reset form state when a modal opens / target changes"** (AgentAddWorkModal,
   IgnorePathModal ×2, WorkflowEditor, AgentTuiProfileModal, ProjectDeleteDialog, AgentsPage's
   default selection, WorkflowOutputsTab's auto-select, DiffReview + BranchDiffViewer collapse
   state): replaced with the **adjust-state-during-render** pattern (track the previous key in
   state, reset when it moves) — the exact shape `WorkflowDetailPage.tsx:81-85` already used and
   which this rule set accepts. This is React's documented "You might not need an effect"
   adjustment; it renders the reset in the same frame instead of painting stale state then
   re-rendering.
   - Trap found by the test suite: `AgentAddWorkModal` can MOUNT already-open, where a flip-adjust
     never fires — the `useState` initializer must carry `agent.boardId`. The old effect ran on
     mount and hid this. Any future conversion of an effect-reset must check the mounted-open path.
   - `AgentAddWorkModal`'s adjust is keyed on `agent.boardId` too, mirroring the old effect deps:
     the default board can arrive after the modal is open and must still pre-select.
2. **"Async compute then setState"** (MarkdownSectionTree, useReviewedFiles): state now holds the
   result *together with the input it was computed from*, and "still pending" / staleness is
   derived. MarkdownSectionTree also gained cancellation (the old code let a slow older hash
   overwrite a newer one). `useReviewedFiles` — the card's named example — now prunes stale marks
   during render and syncs localStorage in a write-only effect (sync TO an external system, which
   is what effects are for).
3. **"Transient animation flags"** (DashboardPage, WorkflowCard): detection of new/updated
   workflows moved into render (previous snapshot in state, not a ref), and only the timed
   *clearing* stays in effects — `setState` inside a `setTimeout` callback is fine, synchronous
   `setState` in the effect body was the violation. Bonus fix: the old WorkflowCard cleanup could
   cancel the fade-out timer when the parent dropped the prop, leaving the glow stuck; the timer
   is now keyed on the glow itself.

### `react-refresh/only-export-components` ×14 — helper extraction

All 14 were pure helpers exported for tests, co-located with components. Moved verbatim into
sibling non-component modules (type-only exports are allowed and stayed/re-exported where
importers rely on them):

- `features/agents/transcriptModel.ts` ← SessionTranscriptPanel's `buildTurns`, `isWorking`,
  `computeTurnMetrics`, `mergeTranscriptEntries`, `formatDuration`, `formatTokens` (+ `Turn`,
  `TurnMetrics`, `ts`, `isInterruptPrompt`). **`isWorking` here is one of the three lockstep
  working-rule implementations** (server `IsWorkingAsync`, runner `TranscriptWorkingState`) — the
  move is byte-identical logic and the file says so at the top.
- `features/agents/filesReviewModel.ts` ← FilesReviewPanel's `isUnviewed`, `mergeTreePaths`,
  `buildTree`, `viewModesFor`, `defaultViewMode` (+ `TreeNode`, `FileViewMode`,
  `FilesViewSelection`).
- `features/agents/ignorePattern.ts` ← `ignorePatternFor` + `IgnoreScope`.
- `features/settings/describeImpact.ts` ← `describeImpact` + `plural`.
- `features/board/terminalCopy.ts` ← `createTerminalCopyKeyHandler` + clipboard fallback.

### Singletons

- `react-hooks/globals` — `useFilesViewUrlState.test.tsx` reassigned a module-level variable
  during render; rewritten with `renderHook` (assertions unchanged in meaning).
- `no-unused-vars` — `WorkflowDetailPage`'s `_selectedStage` was write-only dead state (its only
  effect was a pointless re-render on pipeline-stage click); removed.
- `exhaustive-deps` warning — `WorkflowOutputsTab`'s `files` is now `useMemo`'d.

## Whether lint gates (deliverable 4)

**It gates now, through the test suite** — the only verification surface this repo actually runs
(no CI exists, and inventing CI wasn't this card's scope):

- `client/src/lint.gate.test.ts` runs ESLint programmatically and fails `npm test` on **any**
  problem, warnings included. Its failure message is the standard stylish-formatted finding list,
  plus the rule: fix it or suppress it with an individually justified, dated comment — never relax
  the rule (the card's explicit non-negotiable; the analogy is widening a test timeout).
- `npm run lint` is now `eslint . --max-warnings 0`, so the script and the gate cannot disagree
  about what "green" means (CARD-0045: a check must mean one thing regardless of who runs it).

If CI ever appears, `npm run lint` is the command it should run; the vitest gate can then be kept
(harmless, ~20s) or dropped.

## Verification

- `npx eslint . --max-warnings 0` → exit 0 (was 31 problems).
- `npx tsc -b` → clean.
- `npm test` → **448/448** (447 baseline before the change — captured green first — plus the gate).
  The one mid-work failure (AgentsPage add-work flow) was the mounted-open modal trap above, fixed
  properly, not by adjusting the test.
- Note for the next E2E run: `client/dist` is now stale relative to `client/src`;
  `EnsureClientBundleIsCurrent` will demand an `npm run build` before browser E2E means anything.

## Related

CARD-0053 (closed as dedup into this card — its three named board-feature files are among the
fixes), CARD-0065 (same defect shape server-side), CARD-0045, CARD-0069/0050.
