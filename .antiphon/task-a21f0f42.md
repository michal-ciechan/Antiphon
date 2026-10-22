# CARD-1494 Final Review a21f0f42 of Code task 5db5df66

Subject `feat/card-task-5db5df66` at `019885916ea30f7e9be7bd9c71c56d8e947e5b28`, base `cc22379400b5a1bd79034bed0307a8b57f0e1b78`.
Verdict: clean. No regression, no delete outside the owned root, no orphan, no new red at the tip, and no test this change made vacuous. Disclosures and one Backlog suggestion follow.

## Receipt (own run 20261022-163342-bcdd, `start --plan ... --after S2`, server2, 4/4 slots occupied)

- CHECKPOINT CP-1 build=ok executed=3 passed=3 failed=0 skipped=0 sourceState=clean buildSource=verified (slot waited 225 s)
- CHECKPOINT CP-2 build=ok executed=20 passed=20 failed=0 skipped=0 repeat=5 repetitions=5/5, each repetition 4/4 (slot waited 855 s, startup 68 s, testsWall 674 s)
- CHECKPOINT CP-3 build=reused executed=9 passed=9 failed=0 skipped=0 (testsWall 236 s)
- `unlisted: none`, outputs deleted bin-c1494/ and bin-c1494-rep/, `verdict: GREEN exit=0`, `cleanupRefusals []`.
- `validate-checkpoint-receipt.ps1 -Rows CP-2 -ExpectedRepeat 5`: `CHECKPOINT SOURCE VALID ... rows=1`. `-Rows CP-1,CP-3`: `VALID ... rows=2`. A whole-run validate without -Rows gives `INVALID reason=repeat_requires_expected_repeat`, the tool limitation the Code task disclosed.
- The crash-worker cases are included. The CP-2 TRX has 5 each of Legacy_produced(False), Legacy_produced(True), Legacy_captured and Legacy_note_recovery, all 20 Passed.
- After the run there were no `/tmp/antiphon-c79-crash-*` roots, no `.antiphon/acceptance` under tests/, and no crash-worker children left.

## (1) Child lifecycle and the owned-root guard

- Disposal order is correct: the `await using` worker is disposed before its root. `StopAsync` writes stop, waits StopGrace (10 s), then `DrainAsync` kills the process tree and waits. `_drained` makes a second disposal a no-op. Every call site in H5 and CheckCompactionCrashTests takes its workers with `await using`.
- The only recursive delete is `CrashRoot.DisposeAsync`. It deletes the parent's own `Directory.CreateTempSubdirectory` result (mkdtemp, 0700), and that path never comes from the request. The child never deletes anything. So no traversal, symlink or sibling-prefix input can reach a delete.
- Guard (child-side write confinement): `GetFullPath` normalises `..`, so traversal is refused. A sibling such as `/tmp/antiphon-c79-crash-evil` is accepted, by design: the owned prefix is the mkdtemp name prefix, not a directory. A symlinked root under that prefix with an `owner` file would also be accepted. Both affect only where the child writes, and only someone who can set the child's env var can supply them. Disclosure only.
- Parent SIGKILL: my attempt raced the witness's own disposal (stop had already been written), so it is inconclusive. It is a disclosure, not a finding. The child is bounded by its own ChildBudget of 180 s: the CTS cancels every scenario loop, then `RunAndExitAsync` calls `Environment.Exit`. On a SIGKILL the /tmp root leaks, but it is outside any sealed output. Before this change the bound was 90 s, so this is not a regression.

## (2) Determinism

20/20 under heavy load: slot waits of 14-18 min and 4/4 occupied. The ready bound of 90 s is at least 3.5x the Debug's measured 11-25.6 s child start. The prompt bound is 60 s after ready, and the child budget of 180 s covers ready plus prompt (150 s). The bounds are adequate.

## (3) busy=True

Confirmed. At the base and at the tip, the TurnEnd is inserted 500 ms after the scanner is spawned, which is before the child host attaches (8-26 s). The child never sees a Working scan. The `MatchingPrompts == 0` check at 500 ms is trivially true in both versions. That check was already vacuous at the base, and this change kept the same order (e14737f69). Disclosure plus one Backlog card. The final assertions (exactly one prompt, unchanged run and check counts) are still meaningful.

## (4) TestClassificationGuardTests

`Registry_matches_compiled_metadata` was run once from each of two scratch builds: one at the base (bin-a21b) and one at the tip (bin-a21t). Both were 1/1 failed with the same message: missing or unregistered `BlockedReplyReportRetentionTests`, `DispatcherLeaseSpanTests` and `TestSchemaSnapshotTests`. Neither mentions CheckCompaction. The red is inherited, and neither the new allowlist row nor the new classes affect it.

## (5) Mutations (scratch worktree at the tip, one build bin-a21m, both mutations in different files)

- Kill branch of `DrainAsync` removed, and the guard condition wrapped in `false && (...)`. CP-1 filter: 3/3 green, slot held 272 s. **Both mutations survived.**
  - Kill branch: `DrainAsync` then awaits an unbounded `Process.WaitForExitAsync()`, so disposal blocks until the held child's own 180 s budget runs out. The witness checks `IsAlive` only after disposal returns, so it cannot tell the kill from budget expiry. No orphan results, because disposal still waits for the exit. Under the policy this is not a finding. Backlog suggestion: bound disposal time in `Held_crash_worker_is_killed_when_the_wait_fails` (disposal must return within StopGrace + about 15 s).
  - Guard: no witness gives the child an unowned root. The guard is not on a delete path (see 1), so this is a disclosure. The same Backlog card can add one Unit case that hands the child an unowned root.
- The scratch worktrees were removed and the mutations discarded.

## (6) Scope and landability

- `git diff base..tip -- server src client`: 0 files. Test-only change.
- `git merge-tree --write-tree origin/master 019885916` (master 445df8a80): exit 0, clean.
- rc `origin/rc/2026-10-20` 0714a9a89: the four touched files are byte-identical on the base, rc and master. A sequential cherry-pick of 4613d97db, 74d562b8f, dbf1345e4, 4c4db690b and e14737f69 onto rc is clean. The six resulting test files are byte-identical to the tip. Note: 4c4db690b also adds the plan doc to rc. The rc port should drop it, because rc carries no plan docs.
- `check-evidence-diff.ps1 -Repository . -BaseRef cc2237940 -HeadRef 019885916`: commits=7, violations=0.

## Runs beyond the CP table, all build-slot gated, each with its reason

- Checkpoint tool build (bin-a21rv): needed to run the tool.
- Base and tip Antiphon.Tests builds, plus one TestClassificationGuardTests run each: brief item 4.
- Mutation build and CP-1-filter run: required by the brief.
- One lifetime-witness run for the parent-SIGKILL scenario: brief item 1. The result was inconclusive, as described above.

All bin-* outputs I created have been deleted, and the leaked /tmp root from the SIGKILL attempt was removed with a prefix-confined guard.
