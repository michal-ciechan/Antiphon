# CARD-0998 Code report (timeboxed partial)

Implementation fixed and Final ordinary verification passed; the two explicitly required Code scratch spot-checks remain unrun at the 45-minute limit. Next: Code, then Review once those checks are complete. No land/deploy/restart.

## Source and ownership

Original Code / landing owner: 3eceba1b-7262-430f-8ca4-dbc22934a207.
Branch: feat/card-task-3eceba1b. Worktree: /work/worktrees/task-3eceba1b.
Start SHA: c6d5d56b5b4c565d36157e21de9c85029cc45b53.
Verified implementation SHA: 39ae60c11965d9a1e9f66a502f4671a161dc7bfa.
The later report/evidence commit contains only this report and archived receipts; it does not alter implementation. Its full SHA is in the final response and branch receipt.
Plan: /work/worktrees/task-3eceba1b/docs/superpowers/plans/2026-10-03-card-0998-checkpoint-census-plan.md.
Durable evidence: /work/worktrees/task-3eceba1b/.antiphon/c998-evidence/ (committed, including compressed original TRX and SHA-256 index).

## Acceptance and decision

Card Ask (quoted): "find which landings added the first 27 cases without moving the census, set the census to the compiled count at the time of the fix (337 once CARD-0891 lands), and consider deriving it from `CheckpointRoster.CompiledCases` instead of a literal, or putting the guard in a lane that Code stages actually run so additions to `Antiphon.Tests.Checkpoints` cannot drift silently."

Keep an independent literal plus an ordinary-lane guard: comments and Namespace/Full admission use the census to reject incomplete selection rosters. Deriving it from the roster being tested would remove that independent check. Unchanged start compiled 348 vs literal 290; the new Full-floor test contributes one, so current literal and compiled count are 349. Linux declared checkpoint OS skips are 18, giving execution floor 331.

First 27 cases: CARD-0835 added 14 (CheckpointSourceStateTests 8, CheckpointSourceExecutionTests 6); CARD-0885 added 10 (configuration 5, repeat host 3, build isolation 1, documentation 1); CARD-0833 follow-ups added 3 (slot contract 1, executor 2). Last census update was 0358bc1affbe048ddab0dbfb956b8189558695b8 (272 to 290). Source/validator additions include 3b7d8ec0b, e3c739dd8, e8be88145, 290c49bc9; repeat additions include aa49df370, fe047f3d2, f0d121051, e55cfc418; three wait additions are in 8331a9cf1. Subsequent PlanCoverage landings added 31 current cases, yielding 348 at the start SHA (not 342 inferred from the abbreviated brief).

Files changed: scripts/lib/checkpoint-usage.ps1; tests/Antiphon.Tests/Checkpoints/CheckpointTempUsageTests.cs; docs/testing-and-build.md; the plan; archived evidence/report. Coverage implementation was not touched. The existing census assertion was moved unchanged into CheckpointNamespaceCensusUsageTests (Unit); CheckpointTempUsageTests retains its eight Integration tests. Both classes carry ProcessSpawnLimit. No shared helper/registry changed and no assertions/timeouts were relaxed. A new Full acceptance test checks a supplied real compiled roster plus 1000 ordinary records, a missing execution and a truncated roster.

## Ordinary outcomes and limitations

V-1 PASS: base guard red (348 expected, 290 actual); S1 guard red (349 vs 290); final guard green and present in the fresh whole-Unit TRX under the separate Unit class. Every declared OS skip name resolves.
V-2 PASS: Namespace native evidence accepted at 349; malformed skip/short selections rejected.
V-3 PASS: new Full-floor test genuinely red on unchanged library: full roster checkpoint cases=349 expected=290. Final positive evidence accepted; missing execution and truncated roster rejected at exact assertions.
R-1 PASS: final full CheckpointTempUsageTests 8/8 and CheckpointNamespaceCensusUsageTests 1/1, zero skips/failures, verified SHA above.
R-2 whole Linux Unit run COMPLETE: 3911 passed, 0 failed, 52 skipped (3963 terminal records). Skipped cases are not passed: full class/method list is unit-skips.json and reasons are in the archived Unit console/TRX. Skips include Windows-native, unavailable executable and missing jq prerequisites. No Windows or jq-qualified coverage is claimed.
Required Code manual checks M-1 (literal +1 -> Unit guard red/restore/green) and M-2 (one extra compiled case in scratch copy -> Unit guard red/restore/green): NOT RUN, pending next Code round due time box. No deliberate mutants were applied, so no restoration claim is needed; source was clean throughout ordinary runs.

The first method-OR CP-1 filter selected zero tests; that receipt is invalid test proof. The manifest was corrected and CP-1 rerun with three actual failures, then green at S2. S2 whole Unit exposed an introduced lane-XOR defect (Integration class declaring Unit method); it was fixed by a separate Unit class and the identical whole Unit selection rerun in CP-5. Fresh CP-5 confirms every_test_class_is_tagged_unit_xor_integration passed. No failure is attributed to pre-existing red.

All ordinary source receipts have dirty=0, sourceState=clean, buildSource=verified. Source validator accepted final CP-4 for exact implementation SHA. CP-5 has declared skips and is not represented as a zero-skip certificate. Every intended class/method and nonzero count was inspected in fresh TRX. CP-1 was rerun after zero selection and after its expected red; CP-3's failing whole-Unit selection was rerun as CP-5 after repair. No extra unchanged proof repetitions or loaded repetitions.

## Checkpoint receipts (unaltered)

```text
CHECKPOINT C998-RedBase commit=c6d5d56b5b4c565d36157e21de9c85029cc45b53 build=ok filter=/*/*/CheckpointTempUsageTests*/namespace_census_matches_compiled_checkpoint_cases executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-red-base/C998-RedBase-20261003-102424-4ffd/run.trx slot=granted waited=0s dirty=0 source=c6d5d56b5b4c565d36157e21de9c85029cc45b53 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=03618bb0336fab72fb21ed255f9eacee157f94da build=ok filter=/*/*/CheckpointTempUsageTests*/(namespace_census_matches_compiled_checkpoint_cases)|(full_suite_checkpoint_floor_uses_the_current_census) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-102905-6a07/rows/CP-1/run.trx slot=granted waited=15s dirty=0 source=03618bb0336fab72fb21ed255f9eacee157f94da sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=76d7cf19780bdfccae858d718290d440a18b69d2 build=ok filter=/*/*/CheckpointTempUsageTests*/*census* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-103722-1aeb/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=76d7cf19780bdfccae858d718290d440a18b69d2 sourceState=clean buildSource=verified
CHECKPOINT CP-1 commit=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 build=ok filter=/*/*/CheckpointTempUsageTests*/*census* executed=3 passed=3 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 build=ok filter=/*/*/CheckpointTempUsageTests*/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 build=ok filter=/*/*/*/*[Category=Unit] executed=3911 passed=3910 failed=1 skipped=52 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-104136-bfe4/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=af66fa2949cccf3d5c9fdb863c74b4eb122ab2f9 sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=39ae60c11965d9a1e9f66a502f4671a161dc7bfa build=ok filter=/*/*/Checkpoint*UsageTests*/* executed=9 passed=9 failed=0 skipped=0 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-105757-9594/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=39ae60c11965d9a1e9f66a502f4671a161dc7bfa sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=39ae60c11965d9a1e9f66a502f4671a161dc7bfa build=ok filter=/*/*/*/*[Category=Unit] executed=3911 passed=3911 failed=0 skipped=52 trx=/work/worktrees/task-3eceba1b/.antiphon/c998-checkpoints/20261003-105757-9594/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=39ae60c11965d9a1e9f66a502f4671a161dc7bfa sourceState=clean buildSource=verified
```

All eight builds and their test selections were foreground awaited and used granted host slots. All waits are printed above; the invalid zero-test row waited 15s, other listed rows waited 0s. Unlisted build/test: C998-RedBase only, explicitly required by the brief at unchanged start SHA; its tool binary was reused to launch all checkpoint groups, so no extra bootstrap build occurred. A mistyped expected SHA was refused before any build/test; an initially relative wait path read no run state and was corrected to the absolute run path. Neither launched additional verification. Whole Unit was required by the Final profile despite the narrower body rule permitting it only for shared-helper changes.

No full-assembly run: invariant is independent namespace case count and checkpoint execution floor, with no unbounded shared production impact. Original ordinary cost floor 16 minutes; repair rows increased cumulative manifest floor to 26 minutes. Full usage evidence uses supplied records, not a full-assembly execution. Source remained frozen during runs. Platform defaults and session runners were read over authenticated HTTP; no location was embedded or runner setting changed.

## Pending Mutation

Every PC/variant remains pending for method-scoped SourceLanding Mutation: PC-1 census +1; PC-2 extra compiled case; PC-3 bypass Full execution floor; PC-4 bypass Full checkpoint roster count. The unrun Code spot-checks do not discharge any PC. Mutation also owns missing-control discovery.

## FOLLOW-UPS

Board search 'checkpoint namespace census' with -Board Antiphon -All returned CARD-0998 only. No new structural defect remains to file; the introduced lane-XOR failure was repaired. Continue this card with Code to perform M-1/M-2, retaining the original landing owner; then Review at the final branch SHA. Do not repeat green ordinary selections without a new change/failure. Restart: none. Caller owns eventual landing and SourceLanding Mutation.

Rerun repair ordinary selection via the checkpoint tool with plan above, --rows CP-4,CP-5 and --expected-source-sha <full current committed HEAD>. Bootstrap any missing tool binary through scripts/build-slot.ps1; do not build or test outside the slot gate. TRX archives can be expanded with gzip before validating their roster; index.json records original byte digests.

Cleanup: checkpoint tool removed its five declared alternate output paths throughout the project graph; exact base bin-c998-red outputs were also removed. cleanup.json records the base inventory. All owned commands finished; no source mutations or pending child runs remain.
