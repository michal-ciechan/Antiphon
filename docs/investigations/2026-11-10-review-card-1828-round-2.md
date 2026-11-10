# CARD-1828 Final Review, round 2

The pin defect from round 1 is fixed. `$script:PinnedInputDigest` is `2177a43b57be9261694453ca01c935403f0746755e681f899cd1aafced8e24a5`, equal to `-PrintInputDigest` on this tree. `policyHash` is still `568aae96ca07d5fb5cb5c54e62a5a4a02b1159ae6fd287de18ca9e8f2c0f2a52`. The commissioned re-run is green: 82 passed, 0 failed, 0 skipped. No product file was edited. Positive controls stay pending. Code owner `8008020c-9195-4dca-85b1-a1f81888c75f`. Reviewed source `6b87cacb200a372f387443c844324d61f2d61281`. Diff base `beefaa1ae02ad164ada6208e7663dcca5689a3aa`.

Round 1 is `docs/investigations/2026-11-10-review-card-1828.md` on `origin/feat/card-task-4a65b072` at `08216f76b46b69eef1a09b284a946ed1ef936fb2`. Its only blocker was the stale pin `08fb1f1eb83e4e8ef3a17a8ea98246745c79e318d4c4cdd48b7f79cea1ce5c4a`.

## Diff

`6b87cacb` changes one line in `scripts/test-live-lane-run.ps1`: the pinned digest. `tests/test-execution-policy.json` is not in the diff. `Read-NightlyExecutionPolicy` accepts the stated hash.

`pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest` printed `2177a43b57be9261694453ca01c935403f0746755e681f899cd1aafced8e24a5`.

## Verification

This branch still has no CARD-1828 plan with a `### Checkpoints` table. The commissioned selection ran as one checkpoint-tool row at the reviewed SHA. The tool itself was built once through the slot so the run could use `dotnet exec` without a second lease. Native test output is the row's cold `bin-c1828-r2/`, deleted on green. `UseAppHost=false` on the tool build. `TEMP=/tmp` on the row.

| Command | Result |
|---|---|
| `dotnet build tools/Antiphon.Checkpoints -p:UseAppHost=false` | 0 errors, 11 warnings, 12.97s. Slot `7ea6c660-31d5-4d1a-80a5-30f158893303`, waited 0s, maxcpucount 6, held 14s |
| Checkpoint run `20261110-195647-81e5`, CP-1 | 82 executed, 82 passed, 0 failed, 0 skipped. Exit 0. `scripts/validate-checkpoint-receipt.ps1` printed `CHECKPOINT SOURCE VALID` for `6b87cacb200a372f387443c844324d61f2d61281`, rows=1 |

Per class, all passed: `LiveLaneRunnerTests` 4, `TestClassificationPolicyTests` 41, `TestClassificationGuardTests` 1, `SlowTestRegistryMergeTests` 5, `SlowTestTripwireTests` 2, `NativeLinuxPolicyWitnessTests` 3, `MechanicalPcContractTests` 11, `TaskPlatformGuidanceTests` 5, `C1729LayeringGuardTests` 9, `TestLaneCategoryGuardTests` 1. `WrapperOnlyPaidManifest` passed, so `full-inputs` and `crlf-inputs` ran against the new pin. The brief named the live-lane class and the first five guard classes. The same row also ran the rest of the documented guard batch: mechanical contract, platform guidance, layering, and the lane-category census.

`scripts/impacted-classes.ps1 -Range beefaa1ae02ad164ada6208e7663dcca5689a3aa..6b87cacb200a372f387443c844324d61f2d61281 -MergeSubset` exited 0 with `migrations=False` and `kinds=` empty. Downgrade-selector count is 0. No migration file is in the diff. The always-printed names are the standing land set and were not executed.

Product-range evidence check at the reviewed SHA: `EVIDENCE result commits=3 entries=0 violations=0`. `git fetch origin master` resolved `84bc17576287fb43cf2dc04395970a8bc52e2d76` (the brief named `a69789c77865b2db502500ebd26d58f72a8013c0`, which is an ancestor). `git merge-tree --write-tree origin/master HEAD` exited 0 with tree `095dfa424b410a71b19560bb835b775aca325d07`. The six protected inputs in that tree match HEAD. Those inputs are also unchanged between `a69789c77865b2db502500ebd26d58f72a8013c0` and current `origin/master`. `f077518c` is not in this clone. A later batch that edits a protected input would recompute the digest; this merge does not.

No changed input wait. Parking stays disabled, so there is no automatic release deadline (CARD-1083).

## OS branch witnesses

The only changed line is the digest literal. It adds no OS token and does not change a `scripts/lib` startup reader. The restart note from round 1 still belongs to the earlier `scripts/lib` commits, not to this pin.

## Advisories

Round 1's advisories stand. They do not hold the land.

## Positive controls

Every positive control stays pending for method-scoped SourceLanding Mutation. This review ran none. The red witness for the pin is round 1's leased failure of `WrapperOnlyPaidManifest` at `beefaa1ae02ad164ada6208e7663dcca5689a3aa`.

## Evidence

--- review evidence ---
subjectTaskId: 8008020c-9195-4dca-85b1-a1f81888c75f
reviewedSourceSha: 6b87cacb200a372f387443c844324d61f2d61281
reviewedSourceClean: true
ordinaryScopeCompleted: Full
reviewRound: 2
reviewedRange: beefaa1ae02ad164ada6208e7663dcca5689a3aa..6b87cacb200a372f387443c844324d61f2d61281

CHECKPOINT CP-1 commit=6b87cacb200a372f387443c844324d61f2d61281 build=ok filter=/*/*/(LiveLaneRunnerTests*)|(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=82 passed=82 failed=0 skipped=0 trx=/work/worktrees/task-447993ea/.antiphon/card-1828-r2-checkpoints/20261110-195647-81e5/rows/CP-1/run.trx slot=granted waited=150s dirty=0 source=6b87cacb200a372f387443c844324d61f2d61281 sourceState=clean buildSource=verified
