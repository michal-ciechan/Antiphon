# Code d8a8fabb: CARD-0506 conflict-reload flake (Review 45570e2d F1)

Outcome: the load-sensitive cost in `C506 S2R3 conflict-reload-controls` and its sibling tests is gone. The editor file passed 520/520 under artificial load (the master copy failed 1 of 520 with a 20.66 s timeout), and CP-2 is green at 77/77. The 3x margin is met for the named test in 10/10 runs. Measured by the slowest test in the whole file, it is met in 6/10 runs, so the target is only partly met; see "Margin" below.

- Branch `feat/card-task-d8a8fabb`, worktree `/work/worktrees/task-d8a8fabb` (runner mirror), Code task d8a8fabb.
- Base `6131aed14930473704e887774767ab17f08dc25a` (= origin/master at start). Fix commit `6392bf09c0e1a53f0773570237ed1039d0554f6a`.

## Profile (server2, host load ~28 on 24 cores, single test, step timings)

| Step | ms |
|---|---:|
| render + `loaded()` | 226 + 1069 |
| `typeReason('reload control')` (per-character typing) | **2325 / 2537** |
| same reason via click + `userEvent.paste` | 108 |
| `num('Code max parallel')`, page-wide `getByRole` (first call) | 113 |
| the same, scoped `within(row)` | 12 |
| `sw('Code max parallel override')`, page-wide | 350 |
| click Save / click Reload | 345 / 476 |
| `waitFor(gets==2)`, `settled()` | 2 / 5 |

The waits and the conflict-reload round trips were cheap. Per-character typing of the reason accounted for about half of the test's cost: each keystroke re-renders the whole editor. Page-wide role scans came second.

## Change (test files only; no assertion or timeout changed)

- `DispatchConcurrencyEditor.test.tsx`:
  - `typeReason` now clicks the reason input and pastes the reason with `userEvent.paste`. No test exercises per-character typing of the reason; the disabled-input typing in `S2F3 pending-*` still uses `userEvent.type`.
  - `sw`/`num`/a new `noLimit` query inside the limit row (`dispatch-concurrency-row-<role|overall>`), found from the first word of the control name.
  - The row Revert buttons are scoped to their row.
  - "Reload saved settings" is scoped to the `dispatch-concurrency-refresh-failed` alert.
- `ConcurrencySettingsTab.test.tsx`: a local `typeReason` (click the reason test id, then paste) replaces the per-character `userEvent.type` calls in `V15 scope-selector` and `S3F1 active-tab-click`.
- One side effect to note: a row-scoped query no longer implicitly asserts page-wide uniqueness of that control name.

## Load proof (10 runs, before and after under identical load)

Each run was one `scripts/test-client.ps1 --reporter=verbose` invocation of four files in parallel workers: the master copies (`ZzBefore*.test.tsx`, untracked, since deleted) and the fixed files. It was pinned with `taskset -c 0-2` beside a `node` busy-loop on the same CPUs, inside one build slot (`c506-flake-load10`, waited=0s, held=2440s).

| Run | before editor slowest ms | after editor slowest ms | before conflict-reload ms | after conflict-reload ms | before tab slowest | after tab slowest | before/after editor total s |
|---|---:|---:|---:|---:|---:|---:|---|
| 1 | 17183 | 5937 | 10225 | 1632 | 8037 | 5704 | 295 / 130 |
| 2 | 17110 | 8552 | 3698 | 4970 | 18047 | 6482 | 290 / 170 |
| 3 | 7034 | 2607 | 3084 | 1499 | 7474 | 5493 | 177 / 81 |
| 4 | 10250 | 2772 | 4099 | 1764 | 15637 | 5478 | 196 / 85 |
| 5 | **20659 FAIL** (V09 trim-reason timeout) | 10682 | 4499 | 1646 | 7620 | 5562 | 231 / 124 |
| 6 | 17099 | 7526 | 3784 | 1807 | 9078 | 17690 | 289 / 186 |
| 7 | 11043 | 7377 | 4395 | 1829 | 8704 | 5841 | 208 / 128 |
| 8 | 6395 | 2393 | 3506 | 1595 | 7386 | 5457 | 171 / 80 |
| 9 | 6847 | 2363 | 3371 | 1343 | 7610 | 5532 | 172 / 79 |
| 10 | 6632 | 2538 | 3685 | 1764 | 7454 | 5938 | 177 / 83 |

Pass counts:

| File | Before | After |
|---|---|---|
| Editor | 519/520, 1 timeout | 520/520 |
| Tab | 60/60 | 60/60 |

Total test time in the editor file is 1.9x lower: median 202 s before, 104.5 s after (202 / 104.5 = 1.93; ten-run sums 2,206 s / 1,146 s = 1.92).

### Margin

The target is a slowest test of at most 6.67 s, which is 3x below 20 s.

- **conflict-reload-controls:** after-max 4.97 s (4.0x below 20 s), so the target is met in 10/10 runs.
- **Whole editor file, slowest test:** met in 6/10 runs. Runs 2, 5, 6 and 7 reached 7.4–10.7 s in different tests (S2F3 pending-clear, V10 overall-queued, V09 offline/invalid-disabled). The same runs were the slow ones for the master copy, so these are host load spikes. The remaining cost is component render plus userEvent clicks, which these changes do not touch.
- **Tab file:** met in 9/10 runs. Run 6 had `V15 scope-selector` at 17.7 s.

## Checks

- Before the load runs, in the same slot:
  - `tsc -b`: exit 0.
  - `eslint --max-warnings 0` on both touched files: exit 0.
- `src/lint.gate.test.ts` via test-client.ps1: exit 0.
- **CP-2, first run (checkpoint tool, run 20261014-025349-bfe2):** RED, 76/77. The failure was `SettingsPage.test.tsx > C506 V16 controlled-tab`: `findByTestId('dispatch-concurrency-section')` gave up at the 1 s default after `history.forward()`, line 202. That file is byte-identical to base, and the change touches only two test files that it does not import, so this is inherited load sensitivity and not caused by this change. It is a candidate for a separate card.
  ```
  CHECKPOINT CP-2 commit=6392bf09c0e1a53f0773570237ed1039d0554f6a build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/SettingsPage.test.tsx src/features/settings/ProjectConfig.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=105s dirty=0 source=6392bf09c0e1a53f0773570237ed1039d0554f6a sourceState=clean buildSource=notApplicable
  ```
- **CP-2 rerun (run 20261014-025842-73ca):** GREEN, 4 files, 77/77 (`CLIENT TESTS EXIT CODE: 0`).
  ```
  CHECKPOINT CP-2 commit=6392bf09c0e1a53f0773570237ed1039d0554f6a build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/SettingsPage.test.tsx src/features/settings/ProjectConfig.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=45s dirty=0 source=6392bf09c0e1a53f0773570237ed1039d0554f6a sourceState=clean buildSource=notApplicable
  ```
- **Checkpoint tool exit code:** both runs printed their report, then exited 2 with `refused: receipt-unreadable: JsonException: receipt-shape: outputPaths required for outputNames` (`outputs: refused/kept bin-c506-e2e/`). This is a tool receipt-shape defect around the plan's CP-4 output name; no `bin-c506-e2e` directory exists.
- **Unlisted runs, all through build-slot.ps1:**
  - one baseline editor-file run (52/52, waited=466s);
  - two single-test profiling runs of a temporary `ZzProfile.test.tsx` (since deleted; waited 525s and 1035s);
  - the load loop above.
- The full client suite (CP-3) was not run. It is the Final-round full lane for the card; this repair touches only two test files.
- `scripts/check-evidence-diff.ps1 -BaseRef 6131aed14 -HeadRef HEAD`: commits=1 entries=0 violations=0, exit 0.
- `git merge-tree --write-tree origin/master(bb1e12e41) HEAD`: exit 0.
- Mutation was not run; every PC stays pending.
- Budget: 45 min planned; about 95 min actual, mostly build-slot waits (8–17 min each) and the 40-minute load loop.

## Corrections (Code 5eb3de29, Review 276af1a5 F1 follow-up)

- Editor-file total time: the ten-run column gives medians 202 s before and 104.5 s after, a 1.93x ratio (sums 2,206 / 1,146 s = 1.92x). The earlier "2.2x lower" was wrong. No executions were added.
- (Code 48f9ed83, Review d7a23abb F1 sweep) Removed test-internal counts that the tables do not prove: "14 keystrokes", "one `userEvent.paste`", and "three ... `userEvent.type` calls (×2)". No executions were added.
