# Review: CARD-1722 item 3, scheduler writes the build receipt before readiness (round 1)

Reviewer: Claude (Review task 37f1e778). Author: Codex (Code task edbaee26).
Original Code/landing owner: edbaee26-2ba5-4999-aa4d-17a5b60a7480.
Subject: `origin/feat/card-task-edbaee26` tip `c06daa462dd9a4bd0bc60efa83fd33d020e4a7fb`,
red witness `3caf83cb1a0befc099c8cbd271aa88d277aefaac`, base `b322d430d8e1de9b8039526c70f7edd056b71b90`.
Scope: FULL stringency for the changed code; INTERIM verification scope, as the brief directs (no
whole Unit lane: CARD-1720, Interim accepted by the operator for master-only work).

**Verdict: no blockers.** The fix is correct and minimal. The witness is red at 3caf83cb1 for the
intended reason and green at the tip. The original overlap test passed 20/20 in one host. Two
packed mutants survived. Both are witness-adequacy advisories (A-1, A-2). Neither is an
evidence or correctness hole: the row-side receipt check still fails closed.

## 1. Receipt before readiness, on every path

`RunScheduler.BuildOneAsync` (tools/Antiphon.Checkpoints/Execution/RunScheduler.cs:539-571) now
computes `succeeded` and runs the test-only gate, then `CheckpointBuildBinding.Write`
(SafeWrite receipt + `OwnedOutputLedger.Seal`). A refused write clears `succeeded`. The code
copies `Source` and publishes `progress.State = succeeded ? "ok" : "failed"` as the last step.
This is the only `"ok"` assignment for a build. Every other assignment is `building`, `failed`,
`pending`, `skipped` or `unused`.

| Reader of `BuildProgress.State` | Admits work on | After the fix |
|---|---|---|
| `RunAsync` readyExclusive (:185) | `ok` | sees `ok` only after Write+Seal returned null |
| `Pick` (:751) | not `pending`/`building`/`failed` | `building` holds through Write+Seal, so rows wait |
| `RunAsync` nextBuild / `BuildFailed` / `SkipPendingBuilds` | `pending` / `failed` | unchanged |
| `RowRunner.RunAsync` (:139, :280) | file receipt `Check()` | unchanged; fail-closed backstop |
| `CheckpointApp` report (:800, :859) | snapshot only | unchanged |

- **Crash between the two steps.** `state.json` keeps `building`, and the receipt may exist on
  disk. No path resumes a build: a new run calls `Invalidate()` and refuses
  `pre-existing-build-source` / `pre-existing-output`. So an orphan receipt can never validate a
  later run, and no run is ever `ok` without a receipt.
- **Write or Seal refusal.** The build becomes `failed` with `refused: <reason>`. A receipt that
  is written but not sealed is left on disk. Rows are failed `build-failed`, and the next run
  refuses on it.
- **Write throws.** The state stays `building`, where it was previously `ok`, and the task
  faults. `RunAsync` drains and rethrows. This is safer than before.
- **Locks, awaits and scheduling.** No lock was added. Production adds no await, because the
  gate is an internal `Func` that is null outside tests. Width, the single build lane
  (`buildTask` stays non-null until the task completes) and exclusivity are unchanged.
- **Memory visibility.** `State` is a reference store made after the file syscalls on the same
  thread, so it adds no new hazard.

## 2. Red/green witness and repetition

| Run | Commit | Row | Result |
|---|---|---|---|
| 20261105-131805-dc6f | 3caf83cb1 | CP-1 witness | executed=1 failed=1: `CP-2 ... should be "green" but was "build_source_mismatch"` (inner CP-2 EXIT 2, sentinel ran after); slot=granted waited=255s, build 431 s |
| 20261105-131803-6b2b | c06daa462 | CP-1 witness | executed=1 passed=1, buildSource=verified, waited=315s, build 414 s |
| same | c06daa462 | CP-2 RunSchedulerTests + CheckpointCallerBudgetTests + 4 guard classes | executed=79 passed=79 failed=0 skipped=0 (tests wall 107 s) |
| same | c06daa462 | CP-3 TimeoutTests + CheckpointAdmissionBudgetTests + CheckpointOrFilterTests + CheckpointImportTests (other `new RunScheduler` users) | executed=81 passed=81 failed=0 skipped=4 (Windows-only `windows_*` CheckpointOrFilter rows; validate says row_failed for skipped>0 only) |
| same | c06daa462 | CP-4 `builds_never_overlap_even_with_free_slots_and_width_two`, own build, Repeat 20 | executed=20 passed=20, repetitions=20/20, hostInvocations=1 |

`validate --expected-source-sha c06daa462…` returns VALID for CP-1,CP-2 (rows=2) and for CP-4
with `--expected-repeat 20` (rows=1). The tool ran no other build or test command, and every
slot was granted. `check-evidence-diff` b322d430d..c06daa462: commits=2, violations=0.
`git merge-tree --write-tree origin/master c06daa462` gave 0ba1a24f4 with no conflict
(origin/master 388ff4820, 3 commits ahead).

None of the author's three inherited Unit reds (the mechanical instruction contract and two
native Linux policy checks) is in these selections, so none appeared. The Unit lane (plan
CP-3/R-3) was not run in this Interim round.

## 3. Packed mutants (one build, run 20261105-131807-28e1, scratch commit b90e8841e, never pushed)

The switch `C1722_MUT` in a detached scratch copy of RunScheduler.cs. RunSchedulerTests runs
per row; M4 also runs CheckpointShapeConflictTests and CheckpointAdmissionBudgetTests.

| Mutant | Change | Result |
|---|---|---|
| control | none | 19/19 green |
| M1 reorder (PC-1) | `State = "ok"` before the gate and Write | **killed**: 1 red, the witness (`build_source_mismatch`); `builds_never_overlap…` stays green |
| M2 drop (PC-2) | skip `Write`, publish ok | **killed**: 16/19 red, the witness included |
| M3 (own) | `State = "ok"` after the gate, before Write | **survived** 19/19 |
| M4 (own) | refused Write keeps `succeeded` | **survived** 45/45 |

The scratch worktrees were removed afterwards, and no product file in the subject branch was
touched.

## Advisories (non-blocking)

- **A-1 (witness position).** RunScheduler.cs:546-548 and RunSchedulerTests.cs:45. The gate
  runs before `Write`, so the witness can only see readiness published before the gate.
  - *Scenario:* a later edit that sets `State = "ok"` between the gate and `Write` (M3) passes
    every RunSchedulerTests case, but reopens the original race window in production.
  - *Smallest fix:* add a second gate after `Write`/`Seal` returns, before `State` is assigned
    (or move the gate there). The test then asserts that the state is still `building` while
    `Check()` is already `verified`. Hand M3 to SourceLanding Mutation as PC-3.
- **A-2 (refused receipt unwitnessed).** RunScheduler.cs:549-554. No test drives a refused
  `Write`/`Seal` after a green build.
  - *Scenario:* `succeeded = false` can be dropped (M4) and the plan's "mark a refused receipt
    failed" still passes. Rows would then launch against a refused receipt. They are refused
    later by `RowRunner` (:139 when ExpectedSourceSha/Repeat is set, else :280 after the test
    host ran), so the run fails closed. However, the build records `Source=verified` and the
    rows report `build_source_mismatch` instead of `build-failed`.
  - *Smallest fix:* a scheduler test whose output root makes `Write` refuse. A pre-placed stamp
    is caught by `Invalidate` first, so use a gate-time stamp, or `Seal` refusal through a
    foreign entry. Then assert build `failed`, dependent rows `build-failed`, and no row
    driver launch. The pre-fix line had the same gap.
- **A-3 (test comment).** RunSchedulerTests.cs:58. The comment says the sentinel "proves the
  scheduler has visited CP-2 while its receipt writer is held". That holds only when the bin-a
  build completes synchronously, which is the case with today's fake driver. If it completes
  asynchronously, the sentinel can run before the hold. The test still goes red at the base
  via `heldState`, but the comment overstates. Wording only.

## Pending

PC-1 (reorder) and PC-2 (drop) remain pending for method-scoped SourceLanding Mutation, per
docs/superpowers/mutations/2026-11-05-card-1722-scheduler-receipt.md. This review's M1/M2 runs
are evidence of adequacy, not PC discharge. M3/M4 are proposed as additional controls. The
whole Unit lane (plan CP-3, R-3) stays outstanding under the CARD-1720 Interim exemption.
