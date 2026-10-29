# CARD-1514 S1 round 3 (task b2f1fed3)

Outcome: A4 host sampling is out of S1. The Windows image fix is cherry-picked. Discovery, setup, summary budget, and the counts exit now die under the reviewer mutations. CP-1 is green. CP-5 stays an owed native skip. The branch is not a clean merge onto current master.

Code tip and remote tip: `e885398117c035156d33d5011584538c7adb1ac5` on `feat/card-task-b2f1fed3`.
Checkpoint run `20261029-231357-400d` verified that SHA (`sourceState=clean`, `buildSource=verified`).
Base of this round: `6394a4e53e36fabe95878b4c9e4b4e739d5a5784`.

Standing authority used: the rc-run timing question. No extra approval was requested.

## What landed

A4 is not an S1 acceptance. Production `scripts/lib/nightly-tests-impl.ps1` does not call `Start-NightlyHostSampling`, does not dot-source `qualification-host-stats.ps1`, and does not `Add-Type` a SessionRunner DLL. `scripts/lib/qualification-host-stats.ps1` remains, header marked NOT WIRED. Later-slice entry conditions, recorded in plan D-20: sample through the real SampleCount 0 path, pass TimingRoot and key rows by run and attempt, bind the probe after the qualification build from an immutable copy.

D-1: cherry-pick `-x` of `dbbd3ff0acf49578eaaeca9b2b9a447c05a118be` then `22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157`. Descendant image is cached while the process is alive. Native re-run was not done on this Linux runner.

D-5: one shared summary re-read budget (5000 ms, 8 MiB). `SummaryReReadSharesOneBudget` uses two over-budget shards and a watch seam. N7 (fresh per-shard stopwatch) dies with `second-shard-reread`.

D-6: `ProductionCallSitesPersistDiscoveryAndSetup` drives `Invoke-AntiphonNightlyTests` through `-SeamsPath` for the `scripts` suite and requires `discoverySeconds` null plus the four setup phase names. M7b and M8b die there.

D-7: `Invoke-RcWindowsNativeProcessOwner` lives in `scripts/test-rc-speed-os-witness.ps1` and is called from `scripts/test-nightly-native.ps1`. Phase-root absent, empty, and set is portable in `PhaseRootAbsentEmptyAndSet`. Checker is lexical only.

D-8: commit `77c53cb04` was not rewritten. Plan D-20 states the correction: the earlier host witness did not use the production path, M1-M10 did not all die at the call sites, and checker exit 0 did not close the round-1 OS-witness defect. This report repeats that correction.

## Checkpoints

Tool build: `scripts/build-slot.ps1 -Label rcspeed-s1-tool`, lease `34a71f4a-5da7-47a8-8281-5963bc970a69`, waited 0s, held 7s, `UseAppHost=false`, `OutputPath=bin-rcspeed-s1/`. The run itself was `dotnet exec` of that DLL, not a second slot.

Run `20261029-231357-400d`. Tool verdict RED, exit 3, because CP-5 skipped. `unlisted: none`. Wall 7m54s. Owned `bin-rcspeed-a1` and `bin-rcspeed-s1` directories were removed by literal path after the run.

| Row | Result | Counts | Notes |
|---|---|---|---|
| CP-1 | green | executed 9, passed 9, failed 0, skipped 0 | `RcFullTimingContractTests/*`. Build 380s, tests wall 42s. Slot waited 0s. |
| CP-5 | red, owed native | TRX results 26, passed 25, failed 0, NotExecuted 1 | Tool line: executed 25, passed 25, failed 0, skipped 1. Skip is `C1514T_PS51AndRerun`. Plan expects 0 skipped. This row is not green. Build reused. |

## Mutations

Scratch worktree, each edit restored with `git checkout` and `git diff --exit-code`. Eight baselines exited 0 (Owner, Plans, Host, Discovery, Setup, Counts, Budget, Call).

The first counts pass left M5 and M10 at script exit 0. The observation already failed the C# assertions (`latePresent=false` for M5; `orphanOutcome=Passed` and `orphanIncomplete=false` for M10). `e885398117c035156d33d5011584538c7adb1ac5` puts those failures on the script exit. BASE stayed exit 0. M5 and M10 were re-run after that commit.

| Id | Witness | Result | Text |
|---|---|---|---|
| M1 | OwnerObservationIsPersisted | killed | owner sidecar missing |
| M2 | Owner | killed | retained-image-missing; blank-image-not-unknown |
| M3 | Owner | killed | missed-live-image-not-unknown |
| M4 | HostSamples | killed | loader-fault-escaped |
| M5 | CountsAndJoinsPreserveEvidence | killed | late rows dropped |
| M6 | Plans | killed | cross-suite-stale-not-joined |
| M7a | Discovery | killed | discovery-copied-from-suite |
| M7b | ProductionCallSites | killed | discovery-not-null |
| M8a | Setup | killed | counted-more-than-once; setup-seconds=0 |
| M8b | ProductionCallSites | killed | missing-npm-ci; missing-npm-build; missing-lint; missing-dotnet-build |
| M9 | Owner | killed | owner sidecar missing |
| M10 | Counts | killed | orphan-outcome=Passed; orphan-not-incomplete |
| N1 | Host | killed | loader-fault-escaped |
| N2 | Owner | killed | owner sidecar missing |
| N3 | Owner | killed | owner sidecar missing |
| N7 | SummaryReReadSharesOneBudget | killed | second-shard-reread |
| N8 | Plans | killed | cross-suite-stale-not-joined |

N4, N5, and N6 are withdrawn with A4. `Start-NightlyHostSampling`, `Stop-NightlyHostSampling`, `qualification-host-stats.ps1`, and `Antiphon.SessionRunner.dll` are absent from `nightly-tests-impl.ps1` (`present=False`). They are not green survivors.

## OS witness

Ignored JSON: `.antiphon/os-witnesses/card-1514-s1.json`
sha256 `38f8e7291387687b4321edc7cdf1408ca9da7c8f6a0576e5a19fa358d9a44614`
Checker command used `-BaseRef 6394a4e53e36fabe95878b4c9e4b4e739d5a5784 -HeadRef e885398117c035156d33d5011584538c7adb1ac5`.
Exit 0: `OS-WITNESS complete syntax/inventory; execution and runtime identity require Review`.
`NATIVE pending W-A1-owner`. `NATIVE pending restartImpact`.
This exit 0 is not native execution.

## Commits

- `63f0b85299d918f6527b98774fb10e233a5bad44` test: pin Windows descendant image after process exit
- `3e8c8ca1899649e8054e00147412d83866cb3f8f` fix: retain native descendant image before Windows exit
- `4a1b675a3d8b2322208e2fcf31934b8b2ac6ae3b` test: fail when each summary shard gets its own re-read budget
- `6ae4c942608ff7fe1a392ff33dbbd39fd9093207` fix: share one summary re-read budget across shards
- `faea17c7aed5ef4596c5d5e58fab1101a062dc1c` feat: drop unwired A4 and bind discovery and setup call sites
- `b2eb632dec21d4886ba24b7ff1c9e382f8eaf897` docs: defer A4 and correct the S1 round-3 claims
- `ac4bebc112272f04300c344615b2cedc05af9344` fix: call the Windows process owner from a live witness
- `e885398117c035156d33d5011584538c7adb1ac5` test: fail the counts witness when late or orphan evidence is lost

## Landability

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD` exited 1 against `origin/master` `0e7dd390520a43ab330396a72e9085a8691ac332`. Conflicts were not resolved.

This slice:
- `scripts/lib/nightly-tests-impl.ps1`
- `scripts/lib/test-phase-evidence.ps1`

Already on the branch before this round (unchanged since `6394a4e53e36fabe95878b4c9e4b4e739d5a5784`):
- `.antiphon/task-af2b78c8.md` (add/add)
- `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add)
- `server/Application/Services/CapacityRecoveryService.cs`
- `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` (add/add)

## Owed native

- CP-5 `C1514T_PS51AndRerun` (NotExecuted here). The row is not green.
- CP-2 (`RcFullNativeTimingTests` and `NativeHostSampleStops` are not authored). `NightlyNativeOwnershipTests` was not run.
- Descendant-image assertion and packaged-root refusal in `scripts/test-nightly-native.ps1`.
- CP-3, CP-4, and CP-6. CP-6 is "not authored in S1" and is not an A4 acceptance.
- `C1210_Windows_linux_only_outcomes_are_exact`.
- OS witness execution and restart activation. Checker status is pending.

Watchdogs, the coverage ledger, exclusion rows, frozen-rc custody, and `tests/test-execution-policy.json` were not changed. No tags, no publish, no force-push, no rebase.
