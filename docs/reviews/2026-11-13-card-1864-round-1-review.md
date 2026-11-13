# CARD-1864 round 1 Final review: CLEAN

Subject: `feat/card-task-5e62de4f` tip `d46a294ad1808a5457e08f3213e360bb8fd14e9b`
(base `ab7af12c699039270af7e22a5d7fd794035bc325`, 3 commits). Code task `5e62de4f`.
Reviewer: Review task `87d367d8` (Claude). Master at review: `76f14191eed23fe17a5840f7cf210b78fe1f195f`.

Verdict: **CLEAN**, with no blockers or regressions. Land next.

## Scope checked

1. **Guards still prove the same things.** The test diff only adds things: the `PerClass`
   `ClassDataSource`, a constructor, the C1864 witness, a routing `RunProbeAsync` and
   `BuildOrdinaryProbeAsync`. No test, argument row or assertion was removed, merged,
   narrowed or loosened. All 41 original cases run (42 executed with the witness).
   - Only the `(includeMethodSlow: false, inspect: null)` inputs route to the shared lazy.
   - G065, the method-level Slow probe with live assembly inspection, still takes a fresh build.
   - `ProbeResult` is an immutable record of strings and ints. It lives in an in-process `Lazy<Task<>>`
     and is never persisted, so a faulted setup stays faulted.
   - **Order independence.** Each ordinary consumer was run alone in its own process: G066, both G067 rows,
     both G069 rows, and C1864. Each one paid the cold setup and passed.
   - In this review's full-class run, `C487_G067(inherits)` took the cold setup (9.36 s). In the author's run
     the witness took it. Both orders are green.
2. **Lifecycle.** `[ParallelLimiter<ProcessSpawnLimit>]` stays on the class. The cold build and
   launches run inside the first consumer's test body, under the limiter. The temp-directory
   wrapper (`StartRefGit.DeleteDirectory`) is unchanged. `ProcessSpawnLimitTests*` and
   `TestLaneCategoryGuardTests*` are 58/58 green.
3. **The report's numbers are honest.** Same-host re-measure on server2 (load ~18-24):
   - Pre-fix witness at `b07b31a59` (detached worktree, build-slot run): 16.216 s console, 16.379 s TRX.
     It failed at the intended `ShouldBeSameAs` assertion after both real probes completed.
   - Tip cold witness alone (CP-14): 9.442 s TRX.
   - The report says 19.441 -> 13.991 s. This review measured 16.38 -> 9.44 s. Direction and magnitude
     agree for single loaded observations.
   - The code claims are true:
     - The broker budget is 4.
     - `RunScheduler` holds the build lease across confinement/admission checks.
     - Output cleanup (`Program.cs` `cleanup.Remove`) runs after rows finish.
     - `CheckpointOutputLedger.DeleteRecorded` calls `SafeWrite.Preflight` for each entry.
4. **No new deletion code.** The diff introduces no recursive delete. The shared path reuses the existing
   `WithTempDirectoryAsync` teardown.
5. **Guard union, evidence diff and merge-tree.**
   - The guard union is 53/53 green.
   - `check-evidence-diff.ps1 -Repository . -BaseRef ab7af12c6 -HeadRef d46a294ad`: commits=3,
     entries=0, violations=0.
   - `git merge-tree --write-tree origin/master d46a294ad` is clean (tree `df150d659c29`). The base is an ancestor
     of master, which is 25 commits ahead.

## Checkpoint evidence (tool, review-only plan extracts under ignored `.antiphon/`)

The first run, `20261113-062908-5af1`, had verdict RED exit=1. Seven rows were green. CP-17
`DerivedImpactIndexTests` was 26/27: `regenerate_script_reports_changed_then_unchanged` failed with
"The application to execute does not exist ... Antiphon.DerivedImpactIndex.dll".

That is the known environment precondition. The driver project is outside the sln, and the author
built it as a declared prerequisite. After a build-slot driver build (3.9 s), that method passed in
the receipt run. The red run's `tests/Antiphon.Tests/bin-r1864/` was kept by the tool and is left for
mirror retirement.

```
CHECKPOINT CP-2 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=ok filter=/*/*/TestClassificationPolicyTests/* executed=42 passed=42 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-062908-5af1/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
CHECKPOINT CP-12 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=reused filter=/*/*/TestClassificationPolicyTests/C487_G067 executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-062908-5af1/rows/CP-12/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
CHECKPOINT CP-13 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=reused filter=/*/*/TestClassificationPolicyTests/C487_G069 executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-062908-5af1/rows/CP-13/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
CHECKPOINT CP-14 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=reused filter=/*/*/TestClassificationPolicyTests/C1864_ordinary_probe_is_shared_after_its_directory_is_removed executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-062908-5af1/rows/CP-14/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
CHECKPOINT CP-17 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=reused filter=/*/*/DerivedImpactIndexTests/* executed=27 passed=26 failed=1 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-062908-5af1/rows/CP-17/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
```

CP-11 (G066 alone) was 1/1 green in the same run.

Receipt run `20261113-064738-7e7a`: verdict GREEN exit=0. Outputs deleted. `validate` printed
`CHECKPOINT SOURCE VALID source=d46a294ad1808a5457e08f3213e360bb8fd14e9b rows=4`.

```
CHECKPOINT CP-2 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=ok filter=/*/*/TestClassificationPolicyTests/* executed=42 passed=42 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-064738-7e7a/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
CHECKPOINT CP-15 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=reused filter=/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)|(NativeLinuxPolicyWitnessTests*)/* executed=53 passed=53 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-064738-7e7a/rows/CP-15/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
CHECKPOINT CP-16 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=reused filter=/*/*/(ProcessSpawnLimitTests*)|(TestLaneCategoryGuardTests*)/* executed=58 passed=58 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-064738-7e7a/rows/CP-16/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
CHECKPOINT CP-18 commit=d46a294ad1808a5457e08f3213e360bb8fd14e9b build=reused filter=/*/*/DerivedImpactIndexTests/regenerate_script_reports_changed_then_unchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-87d367d8/.antiphon/checkpoints/20261113-064738-7e7a/rows/CP-18/run.trx slot=granted waited=0s dirty=0 source=d46a294ad1808a5457e08f3213e360bb8fd14e9b sourceState=clean buildSource=verified
```

Every build and test driver ran through the slot gate:

- the tool build
- the base `Antiphon.Tests` build
- the base witness run
- the index driver build
- the two checkpoint runs, each with one build and `unlisted: none`

Mutation checks were waived by the operator (2026-11-12). The plan has no PC rows.

## Observations (non-blocking, no card needed)

- The witness's assertion message says "per test session", but the lazy is per class and process.
  Its elapsed-time line is warm whenever another consumer populated the lazy first. The report already
  says this, and the identity assertion still makes the witness able to go red.
- `C487_G066`'s `Directory.Exists(probe.Dir)` now checks the shared setup's directory, not a
  per-case directory. Temp cleanup is still proven by the cold setup and by the two direct
  `WithTempDirectoryAsync` tests.
