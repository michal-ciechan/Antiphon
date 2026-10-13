# CARD-0506 S2 repair F1-F4 — Code task b5f1d7ae

Branch feat/card-task-b5f1d7ae, runner mirror /work/worktrees/task-b5f1d7ae, started from 2862292ef5d8a43bbe23d5028b0385d0e80f82a5
(the tip of Code task 2459c6e7-a8fa-409d-a7af-c3743d754d67, which owns landing). Fast-forward only, with no rebase.
Review fixed: e9c9efa5 (/tmp/c506-review-e9c9efa5/review.md on server2). Test design: docs/superpowers/plans/2026-10-13-card-0506-test-design.md.

Commits:
- 66664ba4f7660b32cdeebcf5a7c538439efc07fb: tests only (red-first).
- eabfe4f502e4de9379aaf3d39ce2f6de560b1f64: the fix, F4 test barriers and the census update (CP-2 green SHA).
- The commit that adds this report changes no source.

Files: client/src/features/settings/DispatchConcurrencyEditor.tsx, client/src/features/settings/DispatchConcurrencyEditor.test.tsx,
docs/superpowers/plans/2026-10-13-card-0506-test-design.md. S1 modules are unchanged (api/dispatchConcurrency.ts, dispatchConcurrencyModel.ts, useSignalRInvalidation.ts).

## Fixes

- **F1, read-only mode.** Save sends `overridesFromDraft({ ...draft, mode: savedDraft.mode })`. The mode is exactly what the targeted `base` revision saved, whether present or absent. This applies to both entry points (live refresh and 409 refresh) because the mode is now taken at send time, not when state is rebased.
- **F2, older query data.** `seenKey` is gone. Query data replaces `base` only when it is not older: `isOlder` = either the global or the project revision is lower, since the server writes `current + 1`. A failed or late refresh after a Save is ignored. Same-revision data replaces `base` without touching the draft, so occupancy stays fresh. Everything on screen (subtitle, mode badge, inherited, effective, occupancy, ranges, role list) now reads `base` instead of `latest`.
- **F3, edits during a submission.** I chose the preferred option: controls are disabled while a submission is pending. The single `idle` gate (`!submitting && refresh === 'idle'`) already shared by Save and Clear now also disables the limit switches, the number inputs, the No limit checkboxes, Revert and Reason. A side effect: these controls are also disabled while a conflict reload is running or has failed. The values stay visible, and "Reload saved settings" re-enables them. The section now carries `aria-busy={submitting || refresh === 'refreshing'}`.
- **F4, test helper.** I removed `flush()` (five zero-delay timer turns). Two helpers replace it:
  - `writes(queryClient)` counts the mutation cache. The click handlers call `mutateAsync` synchronously, and `MutationObserver.mutate` builds the cache entry inside that call (query-core 5.90.20), so a count read straight after a click includes that click.
  - `settled()` waits for `aria-busy="false"`. It shows that the submission's own handler chain has finished. Its comment says it proves nothing about later independent events.
  - The eight call sites changed: invalid-disabled, double-submit, V11 global/project/cancel, V12 conflict-global, refresh-fails and V13 unknown-role. All of them use these barriers. There are no sleeps or timers.

## New tests (12, all in the editor file)

- `C506 S2F1 live|conflict mode-changed|mode-added|mode-removed` (6): exact PUT body after the refresh, with the badge showing the fresh mode.
- `C506 S2F2 refresh-fails` and `refresh-pending` (the diagnostic's shape, plus a held GET).
- `C506 S2F2 newer-after-save`: a control showing that r6 after a Save is still adopted. This guards against overcorrecting the monotonic rule, and it passes on the base commit by design.
- `C506 S2F3 pending-save`, `pending-clear` and `pending-error`: controls disabled while pending and re-enabled afterwards, the attempted typing has no effect, and the values hold.

## Red first (66664ba, unchanged editor)

Run 20261013-184339-9fbb:
```
CHECKPOINT CP-2 commit=66664ba4f7660b32cdeebcf5a7c538439efc07fb build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/SettingsPage.test.tsx src/features/settings/ProjectConfig.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=1 slot=granted waited=0s dirty=0 source=66664ba4f7660b32cdeebcf5a7c538439efc07fb sourceState=clean buildSource=notApplicable
```
Console: `Tests 12 failed | 44 passed (56)`.

- **All 11 new regressions were red for the intended reasons:**
  - F1 ×6: PUT body `mode` was `"LegacyOpen"` where `"SeparateQueues"` was expected, or the mode was wrongly added or omitted.
  - F2 refresh-fails and refresh-pending: `toHaveValue(2)` received `1`.
  - F3 pending-save and pending-clear: `aria-busy` was absent.
  - F3 pending-error: `Received element is not disabled`.
- **`newer-after-save` passed:** it is a control, as expected.
- **The 12th failure was an existing case, `C506 V10 overall-queued`:** `Test timed out in 20000ms`. The host was loaded during that run (the editor file took 212 s, against 113 s in the green run). That test and its code path were not changed. It passed in the green run below. I have not shown it to be flaky and did not rerun it beyond that.

## Green (eabfe4f)

Run 20261013-185025-21a9:
```
CHECKPOINT CP-2 commit=eabfe4f502e4de9379aaf3d39ce2f6de560b1f64 build=n/a filter=pwsh -NoProfile -File scripts/test-client.ps1 src/features/settings/DispatchConcurrencyEditor.test.tsx src/features/settings/ConcurrencySettingsTab.test.tsx src/features/settings/SettingsPage.test.tsx src/features/settings/ProjectConfig.test.tsx executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a exit=0 slot=granted waited=0s dirty=0 source=eabfe4f502e4de9379aaf3d39ce2f6de560b1f64 sourceState=clean buildSource=notApplicable
```
Console: `Test Files 3 passed (3)`, `Tests 56 passed (56)`. Per file (from the source census, since a green run prints only the total): editor 40 (the existing 28 plus 12 new), SettingsPage 3, ProjectConfig 13.

The census in the test design is updated:
- CP-2 runs 56 in 3 files until S3, and the frozen full count is 61 in 4 files.
- CP-3's full-client count rises from 1,212 to 1,224.

## Sweep (same defect class)

- **Where server data meets draft state.** I checked each path:
  - Render-time adoption of query data (SignalR, focus, invalidation after a Save): monotonic, and mode-safe at send time.
  - `reloadAfterConflict`: sets `base` to the fresh refetch (necessarily newer after a 409) and keeps the draft. The mode is taken from `base` at send.
  - Save success: `base`, draft and reason come from the response. Later newer data is adopted through the render rule (`newer-after-save`). An older stale query is ignored.
  - Failed refetch after a 409: `refresh=failed` and the draft is kept (V12 refresh-fails).
  - Failed refetch after a Save: covered by F2.
  - Non-conflict errors: the draft is kept (V09 offline, V13).
- **The diff banner** does not list the mode, so no disclosure was needed: the mode can no longer differ from the saved value.
- **Controls that stayed editable while pending.** Before the fix these were the switches, inputs, No limit, Revert and Reason. All are now gated.
- **Controls I checked that are not gated:**
  - The Clear modal's Cancel, which does not change the draft.
  - "Reload saved settings", which is the recovery action.
  - The loading Retry, which is outside the form.
- **Timer flushes and over-promising comments.** I removed `flush()` and found no other timer turns in the file. I narrowed these comments:
  - "never auto-retry" to "no retry within the submission".
  - "Every control" to "One control of each kind".
  - Production comments now name the tests that prove each claim: S2F1, S2F2, S2F3 and V10 overall-queued.
- **Own-property access** is unchanged: the `own`/`withOwn` helpers and `Map` objects are still used. The prototype-name diagnostics passed at the reviewed commit, and those paths were not touched.
- **Left as is:**
  - The S1 model comment "mode … carried through untouched" (dispatchConcurrencyModel.ts:22) is still true of the model, but the editor no longer sends the draft's mode. It is in an S1 file, so I left it.
  - History shows "Could not load history." beside old rows when the history refetch fails. It is read-only and has no draft interaction.

## Unlisted runs (reasons)

- `build-slot -Label c506-s2r-npmci -- npm --prefix client ci`: fresh worktree with no `node_modules`. Granted after waiting 61 s. Afterwards I deleted `client/node_modules`.
- `build-slot -Label c506-s2r-red-start -- dotnet run --project tools/Antiphon.Checkpoints -- start ...`: tool bootstrap, granted after 0 s. It shows the existing CS8602 warning. The green start used `--no-build`, granted after 0 s. Afterwards I deleted `tools/Antiphon.Checkpoints/bin` and `obj`.
- `build-slot -Label c506-s2r-tsc-lint`:
  - `tsc -p tsconfig.app.json --noEmit` exited 0.
  - `eslint` on the 2 touched files exited 0 with 0 problems.
  - Granted after 0 s, at the pre-commit tree of eabfe4f (identical content).
- `clean --run 20261013-184339-9fbb` removed the red run's `bin-c506-e2e`. The green run deleted its own.

## Other checks

- `scripts/check-evidence-diff.ps1 -BaseRef 2862292ef -HeadRef eabfe4f`: commits=2, entries=0, violations=0.
- `git diff --check` is clean.
- `git merge-tree --write-tree origin/master HEAD` exits 0, tree d2553c07282743717a5fb4a2628668cc32dd8295 (origin/master b5653d5832aac66c5ac1ff1a0012efb33b54de79).
- **Deferred:**
  - S3–S5.
  - The full CP-2 census (needs S3's tab file).
  - CP-1 (already landed with S1).
  - CP-3 and CP-4.
  - V-15..V-18, R-1, R-4 and R-5.
- **PCs:** every PC (PC-1..PC-49, including the editor PCs PC-26..39, 42..46 and 49) stays pending for method-scoped SourceLanding Mutation. The new S2F1-F3 cases are candidates for added PCs (mode from draft, the older-data rule, the pending gate on controls). Mutation owns that discovery. Post-land Mutation was not run.
