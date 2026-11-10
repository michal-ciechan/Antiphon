# CARD-1823 S1 Code report (task 26c1eaa0)

Outcome: slice S1 is implemented and green on Linux pwsh. `scripts/merge-nightly-seeds.ps1` unions stopped-run seeds. The seven new `RcStoppedRunSeedTests` methods failed while that script was absent, then passed after it landed.

Landing owner: original Code task `26c1eaa0`.
Branch: `feat/card-task-26c1eaa0`.
Worktree: `/work/worktrees/task-26c1eaa0`.
Plan: `docs/superpowers/plans/2026-11-09-card-1823-cumulative-failure-ledger.md` at base `58b6819afdefed9c450affeb54a3397b6b757419`.
Restart: none.

## SHAs

- Red harness (script absent): `d0f147de0cd71a76c861816d26766b79b00f6857` `test(card-1823): red witnesses for cumulative seed merge`
- Fix and tested code: `7d8e651854185cbc4c8ab004e0a5b085283e4125` `feat(card-1823): merge stopped-run seeds into one cumulative report`
- Files: `scripts/merge-nightly-seeds.ps1` (new), `scripts/test-nightly-stopped-seed.ps1`, `tests/Antiphon.Tests/Scripts/RcStoppedRunSeedTests.cs`
- Unchanged on purpose: `scripts/nightly-run.ps1`, the orderer, launch defaults, exporter default path, watch scripts, `tests/impacted-classes.json`, `tests/test-execution-policy.json` (no policyHash refresh).

## Checkpoints

Red CP-1 at the harness-only commit (run `20261109-235630-0a33`). Every failure text is `merge-nightly-seeds.ps1 missing` or the missing-file read. TRX counters total=7 executed=7 passed=0 failed=7. Slot for that row: granted, waited=0s. Tool bootstrap build before it: `card-1823-checkpoints-tool` slot=granted waited=0s held=7s, 0 errors. The tool was then `dotnet exec` because `UseAppHost=false` writes no apphost.

CHECKPOINT CP-1 commit=d0f147de0cd71a76c861816d26766b79b00f6857 build=ok filter=/*/*/RcStoppedRunSeedTests/(CumulativeMergeUnion*)|(CumulativeMergeProvenDrop*)|(CumulativeMergeUnfixedGreen*)|(CumulativeMergeCherryPick*)|(CumulativeMergeExplicitProven*)|(CumulativeMergeDigestStable*)|(CumulativeMergeLaunchDefaults*) executed=7 passed=0 failed=7 skipped=0 trx=/work/worktrees/task-26c1eaa0/.antiphon/checkpoints/20261109-235630-0a33/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=d0f147de0cd71a76c861816d26766b79b00f6857 sourceState=clean buildSource=verified

Green CP-1 and CP-2 at the fix (run `20261110-000555-221f`). Fresh TRX names all seven CumulativeMerge methods Passed, plus FailedMethodBands and AcknowledgementRequired Passed. Counters match the lines below.

CHECKPOINT CP-1 commit=7d8e651854185cbc4c8ab004e0a5b085283e4125 build=ok filter=/*/*/RcStoppedRunSeedTests/(CumulativeMergeUnion*)|(CumulativeMergeProvenDrop*)|(CumulativeMergeUnfixedGreen*)|(CumulativeMergeCherryPick*)|(CumulativeMergeExplicitProven*)|(CumulativeMergeDigestStable*)|(CumulativeMergeLaunchDefaults*) executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/task-26c1eaa0/.antiphon/checkpoints/20261110-000555-221f/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=7d8e651854185cbc4c8ab004e0a5b085283e4125 sourceState=clean buildSource=verified

CHECKPOINT CP-2 commit=7d8e651854185cbc4c8ab004e0a5b085283e4125 build=reused filter=/*/*/RcStoppedRunSeedTests/(FailedMethodBands*)|(AcknowledgementRequired*) executed=2 passed=2 failed=0 skipped=0 trx=/work/worktrees/task-26c1eaa0/.antiphon/checkpoints/20261110-000555-221f/rows/CP-2/run.trx slot=granted waited=30s dirty=0 source=7d8e651854185cbc4c8ab004e0a5b085283e4125 sourceState=clean buildSource=verified

CP-1 green timing: testsWall=111.003s, startup=17.358s. CP-2 green timing: testsWall=16.406s, slotWait=30s. Green run deleted owned `bin-c1823-s1/`. Red run kept its output; that project directory was removed by exact path before the green rerun so the tool would not refuse `pre-existing-output`. Evidence under `.antiphon/checkpoints/` was kept.

## V and R

- V-1 CumulativeMergeUnion: pass (CP-1 green). Older failures stay in the union; the newest chunks and evidence are copied; a bad membership seed exits 2 and writes nothing; `-Strict` exits 3 with files written.
- V-2 CumulativeMergeProvenDrop: pass. Ancestor containment marks the green identity proven and the incomplete sibling fixed-pending-proof.
- V-3 CumulativeMergeUnfixedGreen: pass. A later green whose source SHA does not contain the fix stays open.
- V-4 CumulativeMergeCherryPick: pass. The exact trailer proves and records the cherry-pick SHA. A prose mention of the SHA does not.
- V-5 CumulativeMergeExplicitProven: pass. `--Proven` on a later green clears the identity with no repo and no decision file. Unknown, not-later, and not-green refuse with exit 2 and write nothing.
- V-6 CumulativeMergeDigestStable: pass. Two runs of the same inputs match byte for byte, including the content hash, with no wall-clock stamp.
- V-7 CumulativeMergeLaunchDefaults: pass. The script is ASCII and contains the D-3 sentence once. `nightly-run.ps1` still declares the four prior-failure parameters and does not name the merger.
- R-1 FailedMethodBands and AcknowledgementRequired: pass (CP-2, 2 executed, 2 passed).

## Other verification

- Guard batch, one leased run after a leased default-output build (`UseAppHost=false`, no `-o`/`OutputPath`). Build slot=granted waited=0s held=211s, 0 errors. Run slot=granted waited=0s held=129s. Filter `/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/*`. Result: 78 passed, 0 failed, 0 skipped. TRX `.antiphon/c1823-guards/guards.trx`.
- `scripts/impacted-classes.ps1 -Range 58b6819af..HEAD` printed an empty filter. No extra class run.
- `scripts/check-evidence-diff.ps1` over `58b6819afdefed9c450affeb54a3397b6b757419..7d8e651854185cbc4c8ab004e0a5b085283e4125`: commits=2 entries=0 violations=0.
- `git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `446a2553b77f35a0e06b1109ccc078de906b4534`, master `4a600e9af54c297d8527f472e47fdbc1e9b6f652`.

## Unlisted runs

Development invocations of `scripts/test-nightly-stopped-seed.ps1` for the seven cases, before either checkpoint. Reason: the fast contract witness while the script and harness were still being corrected. They are not the official rows. The official red is CP-1 at `d0f147de0` and the official green is CP-1 and CP-2 at `7d8e65185`.

Checkpoint-tool bootstrap build, reason above. Guard build and guard run, reason: the default Code guard batch is outside the plan's checkpoint table.

## Positive controls pending for Mutation

This Code stage did not plant or restore mutants.

- Union: drop older failures, or union chunk rows instead of copying the newest seed.
- ProvenDrop: remove the ancestor check. This fixture proves only by ancestor, with no `--Proven` and no trailer, so the row must stay failed.
- UnfixedGreen: treat any later green as proof when the fix is not contained.
- CherryPick: drop the trailer check, or treat any mention of the SHA as containment.
- ExplicitProven: ignore `--Proven` so the identity stays failed. No repo and no decision file.
- DigestStable: stamp `UtcNow` into the merged seed so the second digest changes.
- LaunchDefaults: delete the D-3 sentence.

## Owed

Windows-native proof of CP-1 and CP-2 under Windows PowerShell 5.1 and under pwsh, on a private tree. This runner is Debian, pwsh 7.5.4. CP-3 through CP-7 belong to later slices and were not run.

Repeated `-Seed` and `-Proven` are parsed from `$args` because this host's `pwsh -File` rejects a repeated `[string[]]` parameter. The operator command is unchanged. An empty `git log` (no cherry-pick trailer) is an automation null; the merger treats that as not contained instead of calling `Trim` on it.
