# CARD-1514 S1 round 5 (task 527c6cd5)

Outcome: The three round-4 defects are closed on Linux. The rc planning success write is witnessed and M8d kills it. Setup stamps stay ISO-8601 UTC under `TZ=UTC`, `America/New_York`, and `Asia/Kolkata`. The OS-witness inventory is re-anchored at slice base `8edda999623a473be2d1970727c172bfb5088d59` and `scripts/check-os-witnesses.ps1` exits 0. CP-1 is green at 10/10. CP-5 is not green: 25 passed and 1 Windows skip. The checkpoint tool verdict is RED, exit 3, only because of that skip. Native execution was not run. A4 stays unwired. No csproj or props file changed.

Code and plan tip verified by the checkpoint: `5599dedda8d468df473f530c5938a749c3078e59` on `feat/card-task-527c6cd5`.
This report commit is markdown only and sits on that tip.
Round start: `db282ed7a7827feb38af67887a45f3399d999a1c`.
Slice base: `8edda999623a473be2d1970727c172bfb5088d59`.
Prior review: `.antiphon/task-a45b2ddc.md`.

Standing authority used: the rc-run timing question. No extra approval was requested.

## Status

1. RED `e253106eb5336801a822cce41c5f6b3f3f7a28ce`: the stamp witness fails on the pre-fix tip with `stamp-not-iso`.
2. Witness `27462c3272c18006ccbe82e6ba17add0457d0d9a`: rc planning success path on its own timing root, and the owner-exit AST check. The success write at `nightly-tests-impl.ps1:1251` already existed, so this witness is green on the tip. It was not made red by deleting production code. M8d kills it.
3. FIX `f7b9d231ec1dfccd19f08bb3eac31d80e8b35ebc`: rewritten setup stamps stay ISO-8601 UTC, and setup accounting returns the first row's UTC ticks.
4. Plan `5599dedda8d468df473f530c5938a749c3078e59`: D-21 anchors and D-22.
5. Mutations M1-M10, N1-N3, N7, N8, M7a/b/c, M8a/b/c/d, plus X4, X5, X6: each killed its witness. Scratch restored.
6. Checkpoint run `20261030-013917-cd62`: CP-1 green 10/10; CP-5 25 passed, 1 skipped. Tool RED exit 3.
7. OS checker exit 0 against base `8edda999623a473be2d1970727c172bfb5088d59` and head `5599dedda8d468df473f530c5938a749c3078e59`. JSON sha256 `a3bfd61c8289d5fb1dfebefa8c613dd6d03e259ccde5cbca01743a975d71bb7f`.

## What changed

D-1. `ProductionCallSitesPersistDiscoveryAndSetup` now runs a fourth `Invoke-AntiphonNightlyTests`: profile `rc`, suite `antiphon`, the existing supplemental seam, and its own timing root. It requires a `planning-antiphon` row with seconds greater than 0. That is the healthy success write at `scripts/lib/nightly-tests-impl.ps1:1251`. The catch write at line 1263 stays covered by the existing planning path.

D-2. Ignored JSON `.antiphon/os-witnesses/card-1514-s1.json` is regenerated at slice base `8edda999623a473be2d1970727c172bfb5088d59`. Round-4 anchors at `6394a4e53e36fabe95878b4c9e4b4e739d5a5784` lines 80 and 25 are historical only. Plan D-21 records the new anchors. The JSON is gitignored and is not in this commit. `headSha` and both `nativeObligation.sourceSha` values are the plan-and-code tip `5599dedda8d468df473f530c5938a749c3078e59`, which is the revision the checker hashed against. This report commit does not move those lines.

D-3. `Write-QualificationRunPhase` in `scripts/lib/test-phase-evidence.ps1` keeps `startedAt` and `endedAt` through `ConvertTo-QualificationStampText`, so a pwsh 7 `ConvertFrom-Json` DateTime is written back as ISO-8601 UTC with fractional seconds and `Z`. `Get-QualificationSetupAccounting` reads those instants with `ConvertTo-QualificationUtcTicks` and returns `firstStartUtcTicks` of the first setup row. The witness writes two direct phases into its own root and checks the raw file text, overlap, and ticks under `TZ=UTC`, `America/New_York`, and `Asia/Kolkata` via `ProcessStartInfo.Environment` before the process starts. The same ConvertFrom-Json file-rewrite hazard in the S1 diff was this writer. `Get-QualificationTimingReport` casts sidecar stamps in memory. `Write-NightlyMeasuredPhase` still casts `[datetime]` and calls `ToString('o')`, which can emit a local offset; that path was left unchanged because the stamp witness calls `Write-QualificationRunPhase` directly and the green three-zone run still passes prerequisite overlap accounting.

Advisory. `NativeOwnerExitWitnessIsUnconditional` now parses the PowerShell AST. The `C1039_DescendantExit` call is a direct statement, and the assertions for `DescendantImages`, `RootExitedAtUtc`, `MissedDescendantPids`, and `JobEmptyObserved` are reachable throws. X4, X5, and X6 kill it.

A4 stays out. `scripts/lib/qualification-host-stats.ps1` is on disk and is not called by production qualification.

## Plan sentences

D-21 (S1 round 5; anchors supersede the round-4 base): The S1 OS witness is `Invoke-RcWindowsOwnerExitWitness` in `scripts/test-rc-speed-os-witness.ps1`, a direct statement of case `C1039_DescendantExit` with reachable assertions for `DescendantImages`, `RootExitedAtUtc`, `MissedDescendantPids`, and `JobEmptyObserved`. It calls `NativeProcessOwner.Start`. `Invoke-RcWindowsNativeProcessOwner` remains the packaged-root refusal helper and still runs only when a Store-packaged pwsh is present. Native execution of `C1039_DescendantExit` stays pending on Windows. Commit `ac4bebc11` over-claimed a live witness. Commit `faea17c7a` bound the scripts-suite discovery null and the prerequisite setup call, not the supplemental discovery fallback or the `planning-<suite>` writes. The checker inventory is anchored at slice base `8edda999623a473be2d1970727c172bfb5088d59`: base candidates are `scripts/lib/nightly-owned-process.cs:74` and `scripts/lib/nightly-tests-impl.ps1:22`. Round 4 recorded those files at round base `6394a4e53e36fabe95878b4c9e4b4e739d5a5784` on lines 80 and 25; that base is historical only.

D-22 (S1 round 5): `Write-QualificationRunPhase` keeps rewritten setup stamps as ISO-8601 UTC, and `Get-QualificationSetupAccounting` reads those instants as UTC ticks. The rc planning success write is witnessed by the supplemental seam on its own timing root. `NativeOwnerExitWitnessIsUnconditional` checks the owner-exit call and its throws with the PowerShell AST.

## Checkpoints

Tool build: `scripts/build-slot.ps1 -Label r5cp-tool`, lease `2a6455aa-f5c0-416c-aae8-9f5ab7fe078a`, waited 0s, held 6s, `UseAppHost=false`, `OutputPath=bin-r5cp/`. The run was `dotnet exec` of `tools/Antiphon.Checkpoints/bin-r5cp/Antiphon.Checkpoints.dll`. It was not wrapped in a second slot. The driver took its own slot and waited 0s.

Import wrote `.antiphon/rcspeed/5599dedda8d468df473f530c5938a749c3078e59/linux/manifest.yaml` (19 rows). After import: `slotWaitMinutes` 10, `timeouts.totalMinutes` 54, CP-1 `timeoutMinutes` 16, CP-5 `timeoutMinutes` 8. The manifest path was the positional argument to `run`. `--expected-source-sha 5599dedda8d468df473f530c5938a749c3078e59`. `--rows CP-1,CP-5`. `--max-wait 25m`.

Run `20261030-013917-cd62`. Tool verdict RED, exit 3, because CP-5 skipped. Do not read this as a CP-1 failure. `unlisted: none`. Wall 6m15s. Sequential-equivalent 1m21s. Builds 8. Max-concurrent-builds 1. Rows: 1 green, 1 red, 0 skipped by the tool. `sourceState=clean`, `buildSource=verified`, commit `5599dedda8d468df473f530c5938a749c3078e59`. Budget wall 375.533s, execution 375.531s, admissionOnly 0.002s. `refused: still-present` because a red row keeps `bin-rcspeed-a1/`. Evidence: `.antiphon/checkpoints/20261030-013917-cd62/report.md`.

| Row | Result | Counts | Notes |
|---|---|---|---|
| CP-1 | green | executed 10, passed 10, failed 0, skipped 0 | `/*/Antiphon.Tests.Scripts/RcFullTimingContractTests/*`. build=ok. waited 0s. executed 51.047s. build phase 261.503s. testsWall 45.113s. |
| CP-5 | owed native | executed 25, passed 25, failed 0, skipped 1 | Skip is `TestPhaseSummaryTests.C1514T_PS51AndRerun`, outcome `NotExecuted`: "Skipped: CARD-1514: actual nightly native custody and PowerShell 5.1 rerun require Windows; Linux cannot qualify this owner." Plan expects 26 with 0 skipped. This row is not green. build=reused. executed 30.621s. `C1514T_PS51TokenScan` passed. |

## Mutations

Scratch worktree `/tmp/r5mut` detached at `f7b9d231ec1dfccd19f08bb3eac31d80e8b35ebc` (the code fix; the later plan commit is docs only). Isolated test output copied to `tests/Antiphon.Tests/bin-r5mut` so `RepoRoot()` resolved inside the scratch. Each edit was restored and the scratch tree ended `git diff --quiet`. Baseline of `RcFullTimingContractTests/*`: exit 0, total 10, failed 0, succeeded 10, skipped 0. Slot lease `7d464195-bb9f-4a81-bf81-d0ec8ce6c6fe`, waited 0s, held 225s. Consoles under `/tmp/r5mut-out/`. Every mutation row is total 1, failed 1, succeeded 0.

| Id | Witness | Result | Text |
|---|---|---|---|
| M1 | OwnerObservationIsPersisted | killed | owner sidecar missing; owner-not-persisted; retained-image-missing; blank-image-not-unknown; missed-live-image-not-unknown; exited-before-capture-dropped; missed-list-dropped-live; root-start-missing; run-attempt-missing; rerun-overwrote-owner |
| M2 | OwnerObservationIsPersisted | killed | retained-image-missing; blank-image-not-unknown; rerun-overwrote-owner |
| M3 | OwnerObservationIsPersisted | killed | missed-live-image-not-unknown |
| M4 | HostSamplesPreserveUnknownAndStop | killed | loader-fault-escaped |
| M5 | CountsAndJoinsPreserveEvidence | killed | late rows dropped |
| M6 | PlansRetainEverySuite | killed | cross-suite-stale-not-joined; index-digest-missing; index-assembly-mismatch |
| M7a | DiscoveryIsNotCopiedFromSuite | killed | discovery-copied-from-suite; ordinary-chunk-copied-suite-constant |
| M7b | ProductionCallSitesPersistDiscoveryAndSetup | killed | discovery-not-null |
| M7c | ProductionCallSitesPersistDiscoveryAndSetup | killed | supplemental-not-marker |
| M8a | SetupPhasesAreNonoverlapping | killed | UTC counted-more-than-once; setup-seconds=0; per-chunk-build-sum=0 |
| M8b | ProductionCallSitesPersistDiscoveryAndSetup | killed | missing-npm-ci; missing-npm-build; missing-lint; missing-dotnet-build |
| M8c | ProductionCallSitesPersistDiscoveryAndSetup | killed | planning-suite-missing; planning-seconds-zero; planning-success-missing; planning-success-seconds-zero |
| M8d | ProductionCallSitesPersistDiscoveryAndSetup | killed | planning-success-missing; planning-success-seconds-zero |
| M9 | OwnerObservationIsPersisted | killed | same owner-missing chain as M1 |
| M10 | CountsAndJoinsPreserveEvidence | killed | orphan-outcome=Passed; orphan-not-incomplete |
| N1 | HostSamplesPreserveUnknownAndStop | killed | loader-fault-escaped |
| N2 | OwnerObservationIsPersisted | killed | same owner-missing chain as M1 |
| N3 | OwnerObservationIsPersisted | killed | same owner-missing chain as M1 |
| N7 | SummaryReReadSharesOneBudget | killed | second-shard-reread; summary-spent-zero=0 |
| N8 | PlansRetainEverySuite | killed | cross-suite-stale-not-joined |
| X4 | NativeOwnerExitWitnessIsUnconditional | killed | case-still-conditional |
| X5 | NativeOwnerExitWitnessIsUnconditional | killed | images-missing; missed-pids-missing; job-empty-missing |
| X6 | NativeOwnerExitWitnessIsUnconditional | killed | start-missing |

N4, N5, and N6 stay withdrawn with A4. M4's kill text this round is `loader-fault-escaped`. The witness still died. An empty `MissedDescendantPids` array does not kill a mutation that drops the assignment, because the property default is already empty. A non-empty missed-pid kill is owed on Windows.

## OS witness

Ignored JSON: `.antiphon/os-witnesses/card-1514-s1.json`
sha256 `a3bfd61c8289d5fb1dfebefa8c613dd6d03e259ccde5cbca01743a975d71bb7f`
Checker: `pwsh -NoProfile -File scripts/check-os-witnesses.ps1 -BaseRef 8edda999623a473be2d1970727c172bfb5088d59 -HeadRef 5599dedda8d468df473f530c5938a749c3078e59 -EvidencePath .antiphon/os-witnesses/card-1514-s1.json -Plan docs/superpowers/plans/2026-10-29-card-1514-rc-full-speedups-plan.md`
Exit 0: `OS-WITNESS complete syntax/inventory; execution and runtime identity require Review`.
`NATIVE pending W-A1-owner`. `NATIVE pending restartImpact`.

Candidates the checker printed:

- base `scripts/lib/nightly-owned-process.cs:74` / head `:80` (witness `W-A1-owner`)
- base `scripts/lib/nightly-tests-impl.ps1:22` / head `:21` (non-behavior)
- base `scripts/test-nightly-native.ps1:7` / head `:7` (non-behavior; the file exists at the slice base)

`qualification-host-stats.ps1`, `test-phase-evidence.ps1`, `test-rc-speed-os-witness.ps1`, and `RcFullTimingContractTests.cs` contributed no OS candidates. Docs are excluded by the checker. `restartImpact.required` is true. Restart paths are `scripts/lib/nightly-owned-process.cs`, `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/qualification-host-stats.ps1`, and `scripts/lib/test-phase-evidence.ps1`. `additionalPaths` is empty. Execution and runtime identity remain pending.

The round-4 digest `2b6c5e58b16f510cfef2144d44c3b3d62bebdc1ad65dddc72c1a20e103922622` was anchored at `6394a4e53e36fabe95878b4c9e4b4e739d5a5784` and is superseded.

## Project files

`git diff --name-only 8edda999623a473be2d1970727c172bfb5088d59..5599dedda8d468df473f530c5938a749c3078e59` contains no `*.csproj` and no `*.props`. This report commit adds only `.antiphon/task-527c6cd5.md`.

Changed paths in that range, net: the seven prior task reports under `.antiphon/`, `docs/investigations/2026-10-29-rc-speedups-timing-census.md`, the speedups plan, `scripts/lib/nightly-owned-process.cs`, `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/qualification-host-stats.ps1` (added, unwired), `scripts/lib/test-phase-evidence.ps1`, `scripts/test-nightly-native.ps1`, `scripts/test-rc-speed-os-witness.ps1`, and `tests/Antiphon.Tests/Scripts/RcFullTimingContractTests.cs`.

Watchdogs, the coverage ledger, exclusion rows, frozen-rc custody, and `tests/test-execution-policy.json` were not changed. No tags, no publish, no force-push, no rebase.

## Merge trees

Fetched `origin/master` `0e7dd390520a43ab330396a72e9085a8691ac332` and `origin/rc/2026-10-27` `ade54222543381e2e1b31ad79d8ae33a4035f70b`. Nothing was merged.

Against `5599dedda8d468df473f530c5938a749c3078e59`, before this report commit:

- Range replay `--merge-base=8edda999623a473be2d1970727c172bfb5088d59` onto `origin/master`: exit 0, tree `a239c5d15d75de5ff557874a9372f473f70bda95`.
- Range replay onto `origin/rc/2026-10-27`: exit 0, tree `e262ca2c40f22f6348962b5745cd57293b641a44`.
- Whole-branch merge into `origin/rc/2026-10-27`: exit 0, same tree `e262ca2c40f22f6348962b5745cd57293b641a44`.
- Whole-branch merge into `origin/master`: exit 1. Conflicts were not resolved. Paths: `.antiphon/task-af2b78c8.md` (add/add), `docs/investigations/2026-10-25-card-1584-e2e-launch-bounds.md` (add/add), `scripts/lib/nightly-tests-impl.ps1`, `scripts/lib/test-phase-evidence.ps1`, `server/Application/Services/CapacityRecoveryService.cs`, `tests/Antiphon.Tests/Application/CapacityLockOrderTests.cs` (add/add). These are the inherited paths named by review a45b2ddc.

Publish the slice by replaying `8edda999623a473be2d1970727c172bfb5088d59..HEAD` onto rc and onto master. Do not whole-merge this branch into master.

## Owed native

- CP-5 `C1514T_PS51AndRerun` (`NotExecuted` here). The row is not green.
- CP-2 `RcFullNativeTimingTests` is not authored. `NightlyNativeOwnershipTests` skips `C1039_DescendantExit` off Windows. `scripts/test-nightly-native.ps1` was not run.
- `Invoke-RcWindowsOwnerExitWitness` execution, including a non-empty `MissedDescendantPids` kill.
- CP-3 and CP-4 are not authored. CP-6 stays deferred with A4.
- `C1210_Windows_linux_only_outcomes_are_exact`.
- OS-witness execution and `restartImpact`. The checker result is syntax and inventory only.

## Carry-over, not fixed here

Supplemental discovery still stores suite discovery plus the supplemental launch in one `discoverySeconds` number, so a suite that also wrote `planning-<suite>` can count that discovery twice. The 200 ms inline read has no shared aggregate. A corrupt planned index still throws, which drops the diagnostic summary. The Windows per-chunk persistence cost against the plan's 2 percent ceiling was not measured on this host.

## Commits

Round 5, parent `db282ed7a7827feb38af67887a45f3399d999a1c`:

- `e253106eb5336801a822cce41c5f6b3f3f7a28ce` test: fail when setup phase stamps lose UTC
- `27462c3272c18006ccbe82e6ba17add0457d0d9a` test: witness the rc planning success path and owner-exit AST
- `f7b9d231ec1dfccd19f08bb3eac31d80e8b35ebc` fix: keep setup phase stamps in UTC when rows are rewritten
- `5599dedda8d468df473f530c5938a749c3078e59` docs: anchor the S1 OS witness inventory at the slice base
- this report commit, child of `5599dedda8d468df473f530c5938a749c3078e59`

Slice range for rc and for master, oldest first, from `8edda999623a473be2d1970727c172bfb5088d59` (exclusive) through the report tip:

- `c4001e86cc41c72d88d6f578758cf842f3ebd07e` docs: carry the reviewed CARD-1514 rc speed-up plan and timing census
- `7e9db99dd3748356b5bc5b47b7e3e7f3ce0aa2d3` test: fail rc timing contracts until instrumentation persists
- `440b282af70d31b9d08163ec76d9e2bd6fb0b1b1` feat: persist rc timing observations without changing verdicts
- `9e485d2d1bf398025e473842bf63ec4d4f88785d` docs: review CARD-1514 S1 instrumentation at 440b282af
- `77c53cb04787b3e94f618954294895485b94fde0` test: fail rc timing contracts until production paths persist
- `eef03be7d50151a2ef14aa10b1514af5fc2cbd53` feat: persist rc timing observations without changing verdicts
- `5bda525ca411fe4f0c108dface94f3a16c361fae` test: declare pending Windows OS witnesses for rc timing
- `d2ec4e6fbf19f28576ffd6b3aba12e7c44eb4272` docs: record CARD-1514 S1 round-2 repair
- `6394a4e53e36fabe95878b4c9e4b4e739d5a5784` docs: review CARD-1514 S1 round 2 at d2ec4e6fb
- `63f0b85299d918f6527b98774fb10e233a5bad44` test: pin Windows descendant image after process exit
- `3e8c8ca1899649e8054e00147412d83866cb3f8f` fix: retain native descendant image before Windows exit
- `4a1b675a3d8b2322208e2fcf31934b8b2ac6ae3b` test: fail when each summary shard gets its own re-read budget
- `6ae4c942608ff7fe1a392ff33dbbd39fd9093207` fix: share one summary re-read budget across shards
- `faea17c7aed5ef4596c5d5e58fab1101a062dc1c` feat: drop unwired A4 and bind discovery and setup call sites
- `b2eb632dec21d4886ba24b7ff1c9e382f8eaf897` docs: defer A4 and correct the S1 round-3 claims
- `ac4bebc112272f04300c344615b2cedc05af9344` fix: call the Windows process owner from a live witness
- `e885398117c035156d33d5011584538c7adb1ac5` test: fail the counts witness when late or orphan evidence is lost
- `d12ccc80c3d75b9bc5d235a3dc44dcc9f12faf9d` docs: record the S1 round-3 checkpoint and mutation results
- `43a6441a9d635155d5f4092bd95d100ec833800e` docs: review CARD-1514 S1 round 3 at d12ccc80c
- `6867b995c203fa72a3da4e08dd64176e2f44d5c0` test: fail when planning, supplemental discovery, and owner exit are unwitnessed
- `ffa12a9c177c97d53e8d1b672d5d66ac93e98574` fix: witness supplemental discovery, planning phases, and owner exit
- `9f9a5aaa73c8dc80b57420ff6b129d33684a6088` docs: record the S1 round-4 checkpoint and mutation results
- `db282ed7a7827feb38af67887a45f3399d999a1c` docs: review CARD-1514 S1 round 4 at 9f9a5aaa7
- `e253106eb5336801a822cce41c5f6b3f3f7a28ce` test: fail when setup phase stamps lose UTC
- `27462c3272c18006ccbe82e6ba17add0457d0d9a` test: witness the rc planning success path and owner-exit AST
- `f7b9d231ec1dfccd19f08bb3eac31d80e8b35ebc` fix: keep setup phase stamps in UTC when rows are rewritten
- `5599dedda8d468df473f530c5938a749c3078e59` docs: anchor the S1 OS witness inventory at the slice base
- this report commit

## Cleanup

Owned `bin-r5red`, `bin-r5cp`, and `tests/Antiphon.Tests/bin-rcspeed-a1` directories are removed by literal path after this report is committed. Checkpoint evidence stays at `.antiphon/checkpoints/20261030-013917-cd62/`. Scratch worktree `/tmp/r5mut` is removed with `git worktree remove`. `/tmp/owner-*.json` is not touched.

--- next stage ---
next: merge
handoff: Replay 8edda999623a473be2d1970727c172bfb5088d59..HEAD onto origin/rc/2026-10-27 (ade54222543381e2e1b31ad79d8ae33a4035f70b) and onto origin/master (0e7dd390520a43ab330396a72e9085a8691ac332). Range replay of the code tip is clean on both. Whole-branch merge into master conflicts on the six inherited paths above and was not merged. Native rows stay pending; CP-5 and OS execution are not green.
artifact: .antiphon/task-527c6cd5.md
