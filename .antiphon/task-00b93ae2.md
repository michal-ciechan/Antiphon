# CARD-1514 S1 round 4 (task 00b93ae2)

Outcome: The two surviving call sites now kill a witness, and the Windows owner-exit witness is unconditional in source. CP-1 is green at 10/10. CP-5 stays an owed native skip. Native execution of `NativeProcessOwner` was not run on this Linux host. A4 stays out of S1.

Code tip verified by the checkpoint: `ffa12a9c177c97d53e8d1b672d5d66ac93e98574` on `feat/card-task-00b93ae2`.
This report commit is docs-only and sits on that tip.
Round start: `43a6441a9d635155d5f4092bd95d100ec833800e`.
Round base: `6394a4e53e36fabe95878b4c9e4b4e739d5a5784`.
Slice base: `8edda999623a473be2d1970727c172bfb5088d59`.

Standing authority used: the rc-run timing question. No extra approval was requested.

## What changed

D-1. `ProductionCallSitesPersistDiscoveryAndSetup` now drives three seams. The scripts suite still covers ordinary discovery null and the four prerequisite phases. Profile `rc` on suite `antiphon` with host platform `linux` takes the planning path: list-tests sleeps 250 ms and returns timed out, and the catch writes `planning-antiphon` with nonzero seconds. Profile `nightly` on suite `antiphon` takes the supplemental fallback: a TRX is present, no per-chunk discovery file is written, list-tests sleeps 400 ms and writes a parseable diagnostic, and `discoverySeconds` is the marker (greater than 0.2), which is suite discovery plus the supplemental launch.

D-2. `Invoke-RcWindowsOwnerExitWitness` in `scripts/test-rc-speed-os-witness.ps1` calls `Antiphon.Nightly.NativeProcessOwner.Start` on the descendant fixture, holds the child 300 ms, then releases it. It asserts `DescendantImages` contains the fixture image, `RootExitedAtUtc` is non-empty, `MissedDescendantPids` is present, and `JobEmptyObserved` is true. `C1039_DescendantExit` calls it before any packaged-PowerShell condition. `Invoke-RcWindowsNativeProcessOwner` remains the packaged-root helper and stays inside `if ($packaged.Count)` in `C1039_PowerShellDescendantCustody`. `NativeOwnerExitWitnessIsUnconditional` checks that shape on Linux. It does not execute `NativeProcessOwner`.

D-3. Plan D-21 names the unconditional witness and records that `faea17c7a` bound only the scripts-suite discovery null and the prerequisite setup call, and that `ac4bebc11` over-claimed a live native run. Those commits were not rewritten. CP-1 Expect and Min are 10.

The planning harness drives the catch write at `nightly-tests-impl.ps1` line 1263. M8c deletes both planning writes (lines 1251 and 1263), so the witness dies. A mutation that deleted only the success write at line 1251 would still pass this harness.

## Red, then fix

RED `6867b995c203fa72a3da4e08dd64176e2f44d5c0` failed on the pre-fix tip for the stated reasons: `planning-suite-missing`, `planning-seconds-zero`, `supplemental-not-marker`, and `function-missing; case-does-not-call-witness`.

FIX `ffa12a9c177c97d53e8d1b672d5d66ac93e98574` passed both witnesses: 2 executed, 2 passed, 0 failed, 0 skipped.

## Checkpoints

Tool build: `scripts/build-slot.ps1 -Label r4cp-tool`, lease `86c50e68-2327-42fd-b558-7fce804dcfe7`, waited 0s, held 7s, `UseAppHost=false`, `OutputPath=bin-r4cp/`. The run was `dotnet exec` of that DLL, not a second slot.

Run `20261030-002741-1e28`. Tool verdict RED, exit 3, because CP-5 skipped. `unlisted: none`. Wall 6m33s. `sourceState=clean`, `buildSource=verified`, commit `ffa12a9c177c97d53e8d1b672d5d66ac93e98574`.

| Row | Result | Counts | Notes |
|---|---|---|---|
| CP-1 | green | executed 10, passed 10, failed 0, skipped 0 | `RcFullTimingContractTests/*`. Build 284s, tests wall 40s. Slot waited 0s. |
| CP-5 | owed native | executed 25, passed 25, failed 0, skipped 1 | Skip is `C1514T_PS51AndRerun` (`NotExecuted`). Plan expects 0 skipped. This row is not green. Build reused. |

Owned `bin-r4red`, `bin-r4cp`, and `bin-rcspeed-a1` directories were removed by literal path after the run. Checkpoint evidence remains under `.antiphon/checkpoints/20261030-002741-1e28/`.

## Mutations

Scratch archive of `ffa12a9c177c97d53e8d1b672d5d66ac93e98574` plus the isolated `bin-r4red` test output copied to `bin-r4mut`, so `RepoRoot()` resolved inside the copy. Each edit was restored with `git checkout` and the scratch tree ended `git diff --quiet`. Baseline of the eight witness methods: exit 0, total 8, failed 0, skipped 0. Slot lease `e7c751a5-7b85-4a79-a2d6-5c6b9beabd27`, waited 0s, held 170s.

| Id | Witness | Result | Text |
|---|---|---|---|
| M1 | OwnerObservationIsPersisted | killed | owner sidecar missing |
| M2 | OwnerObservationIsPersisted | killed | retained-image-missing; blank-image-not-unknown; rerun-overwrote-owner |
| M3 | OwnerObservationIsPersisted | killed | missed-live-image-not-unknown |
| M4 | HostSamplesPreserveUnknownAndStop | killed | desktopLoaderCalled was True |
| M5 | CountsAndJoinsPreserveEvidence | killed | late rows dropped |
| M6 | PlansRetainEverySuite | killed | cross-suite-stale-not-joined; index-digest-missing; index-assembly-mismatch |
| M7a | DiscoveryIsNotCopiedFromSuite | killed | marked chunk 21.377, expected 1.5 |
| M7b | ProductionCallSitesPersistDiscoveryAndSetup | killed | discovery-not-null |
| M7c | ProductionCallSitesPersistDiscoveryAndSetup | killed | supplemental-not-marker |
| M8a | SetupPhasesAreNonoverlapping | killed | counted-more-than-once; setup-seconds=0 |
| M8b | ProductionCallSitesPersistDiscoveryAndSetup | killed | missing-npm-ci; missing-npm-build; missing-lint; missing-dotnet-build |
| M8c | ProductionCallSitesPersistDiscoveryAndSetup | killed | planning-suite-missing; planning-seconds-zero |
| M9 | OwnerObservationIsPersisted | killed | owner sidecar missing |
| M10 | CountsAndJoinsPreserveEvidence | killed | orphan-outcome=Passed; orphan-not-incomplete |
| N1 | HostSamplesPreserveUnknownAndStop | killed | loader-fault-escaped |
| N2 | OwnerObservationIsPersisted | killed | owner sidecar missing |
| N3 | OwnerObservationIsPersisted | killed | owner sidecar missing; rerun-overwrote-owner |
| N7 | SummaryReReadSharesOneBudget | killed | second-shard-reread |
| N8 | PlansRetainEverySuite | killed | cross-suite-stale-not-joined |

N4, N5, and N6 stay withdrawn with A4. The reviewer X-series was already killed in round 3 and was not part of this round's required set.

M7c's failure text is only `supplemental-not-marker`. M8c's failure text is only the two planning problems. Those are the post-fix kills. The pre-fix red was the same three strings together because neither path was reached.

## OS witness

Ignored JSON: `.antiphon/os-witnesses/card-1514-s1.json`
sha256 `2b6c5e58b16f510cfef2144d44c3b3d62bebdc1ad65dddc72c1a20e103922622`
Checker: `pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef 6394a4e53e36fabe95878b4c9e4b4e739d5a5784 -HeadRef ffa12a9c177c97d53e8d1b672d5d66ac93e98574 -EvidencePath .antiphon/os-witnesses/card-1514-s1.json -Plan docs/superpowers/plans/2026-10-29-card-1514-rc-full-speedups-plan.md`
Exit 0: `OS-WITNESS complete syntax/inventory; execution and runtime identity require Review`.
`NATIVE pending W-A1-owner`. `NATIVE pending restartImpact`.
The declaration base is the round base `6394a4e53e36fabe95878b4c9e4b4e739d5a5784`. This inventory does not cover changes between slice base `8edda999623a473be2d1970727c172bfb5088d59` and that round base. The earlier digest `38f8e7291387687b4321edc7cdf1408ca9da7c8f6a0576e5a19fa358d9a44614` is stale.

An empty `MissedDescendantPids` array does not kill a mutation that drops the assignment, because the property default is already empty. The portable witness asserts the property is present. A non-empty missed-pid kill is owed on Windows.

## Commits

- `6867b995c203fa72a3da4e08dd64176e2f44d5c0` test: fail when planning, supplemental discovery, and owner exit are unwitnessed
- `ffa12a9c177c97d53e8d1b672d5d66ac93e98574` fix: witness supplemental discovery, planning phases, and owner exit

## Landability

`git fetch origin master rc/2026-10-27` then `git merge-tree --write-tree origin/master HEAD` exited 1 against `origin/master` `0e7dd390520a43ab330396a72e9085a8691ac332`. Conflicts were not resolved. `origin/rc/2026-10-27` is `ade54222543381e2e1b31ad79d8ae33a4035f70b`.

Conflict paths:

- `.antiphon/task-af2b78c8.md` (add/add)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add)
- `scripts/lib/nightly-tests-impl.ps1`
- `scripts/lib/test-phase-evidence.ps1`
- `server/Application/Services/CapacityRecoveryService.cs`
- `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` (add/add)

## Owed native

- CP-5 `C1514T_PS51AndRerun` (`NotExecuted` here). The row is not green.
- CP-2 (`RcFullNativeTimingTests` is not authored). `NightlyNativeOwnershipTests` skips `C1039_DescendantExit` off Windows. `scripts/test-nightly-native.ps1` was not run.
- `Invoke-RcWindowsOwnerExitWitness` execution, including a non-empty `MissedDescendantPids` kill.
- CP-3 and CP-4 are not authored. CP-6 stays deferred with A4.
- `C1210_Windows_linux_only_outcomes_are_exact`.
- OS witness execution and restart activation. Checker status is pending.

Watchdogs, the coverage ledger, exclusion rows, frozen-rc custody, and `tests/test-execution-policy.json` were not changed. No tags, no publish, no force-push, no rebase.

## Carry-over, not fixed here

Supplemental discovery still stores suite discovery plus the supplemental launch in one `discoverySeconds` number, so a suite that also wrote `planning-<suite>` can count that discovery twice. The 200 ms inline read has no shared aggregate. A corrupt planned index still throws, which drops the diagnostic summary. The Windows per-chunk persistence cost against the plan's 2 percent ceiling was not measured on this host.
