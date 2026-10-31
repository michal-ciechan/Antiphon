# Stopped-run seed builder: sibling timing root, speed, per-chunk tolerance

Outcome: D-1, D-2, and D-3 are fixed on `feat/card-task-d63efd0a` tip `ac2dd388850d336818850609537f088b289a99dc`. The seed-chain checkpoint rows CP-1, CP-2, and CP-3 are green on that commit (125/125, 1/1, 1/1, `RUN_EXIT:0`). The six-row table is not all green: CP-4, CP-5, and CP-6 failed on an earlier commit for inherited NativeLinuxPolicyWitness reverse-coverage and were left as recorded.

Code tip: `ac2dd388850d336818850609537f088b289a99dc`
Branch: `feat/card-task-d63efd0a`
Base: `a00f1b46753542a8721a111fb4c26ed1d542cf2a`
Live run `d47848cc` was not opened, stopped, or exported.

## What the builder does now

D-1. `Export-NightlyStoppedRunSeed` reads `timingRoot` from `summary.json` with a streaming prefix reader that stops before the bulk keys `timingChunks`, `suiteTimeline`, and `suites`. A nested decoy such as `"selection":{"timingChunks":"decoy"}` is ignored. The folder must be a sibling named `timing-<runId>`, under the same logs parent, and must not be a symlink. If `summary.json` is missing, an explicit `-TimingRoot` is accepted, and otherwise the builder discovers the sibling `timing-<runId>` (or the single parent directory matching `timing-<last 8 of the run directory name>` plus a hex tail). Point the export at the run directory. When `summary.json` is present, omit `-TimingRoot`.

D-2. The summary is streamed in 64KB blocks. `Antiphon.NightlyHistory.SummaryPrefix.CutBeforeBulk` hashes only that prefix. The recheck reads exactly that length. The cap is 32MB (`seed-summary-header` when no bulk key appears and the file continues or exceeds 32MB). `summary.json` is not in the full-file hash set. Link checks lstat. Ancestors above the parent are cached by device and inode. The path and its immediate parent are lstat'd on every call (`$recheck = ($i -le 1)`). Only directories are cached, so rewriting a regular file (new inode) stays content. A directory whose inode or directory-ness changes, a symlink, or a non-directory with `nlink>1` (Linux) or `LinkType` (Windows) is `evidence-link`, and a cached directory inode is not refreshed. The remaining window is lstat-to-open.

D-3. An unreadable or changing `timing.json` or TRX, or a TRX that is still missing `</TestRun>`, marks that chunk unfinished and the export still succeeds. Run-wide identity failures still refuse the export. A timing failure does not strip failures from a TRX that is still good. `trx-result-open` does not force `completed` false. Completion stays on `$closed` together with the finish, row-count, and outcome checks. Chunk start is lstat existence, so a mode-000 file still counts as started.

## Commits

| SHA | Subject |
|---|---|
| `905592fc05dfa26d3932f81683608b03366fd672` | test: require sibling timing roots and per-chunk seed tolerance |
| `414a658053749eca8f2fcc186b75334abd9843c0` | fix: discover sibling timing roots and tolerate per-chunk seed faults |
| `492302404a4c84850fef3ec0423dd1d4b4c342e1` | test: expect a vanished proc stat to count as process gone |
| `ecd0b58b3d9a0934ab7ddc9f02396181918fe175` | fix: treat a vanished proc stat IOException as process gone |
| `9222d590211317e4ce0fa110e40d98ecf2ab3ef1` | docs: add stopped-run seed layout checkpoint plan |
| `ac2dd388850d336818850609537f088b289a99dc` | fix: treat a rewritten history file as content, not a link |

Files from base to the code tip:

- `docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-layout-plan.md`
- `scripts/export-nightly-stopped-run-seed.ps1`
- `scripts/lib/nightly-failure-history.ps1`
- `scripts/lib/nightly-stopped-run-seed.ps1`
- `scripts/test-nightly-stopped-seed.ps1`
- `tests/Antiphon.Tests/Scripts/LinuxScriptHarnessProcess.cs`
- `tests/Antiphon.Tests/Scripts/RcStoppedRunSeedTests.cs`
- `tests/Antiphon.Tests/Scripts/ScriptHarnessProcessContractTests.cs`

The four PowerShell files above are ASCII. `tests/test-execution-policy.json` is unchanged.

## Mutants, shown red and restored

M9, dropping `$closed -and` from the chunk completion expression: `ChunkUnfinishedTolerance` failed with `open=completed=True` while the locked chunk stayed `timing-unreadable`, the changed chunk stayed `trx-changed` with kept failures 0, the denied chunk stayed `timing-unreadable`, and the tamper stayed `seed-timing-run-mismatch`. Restored. `$closed` remains in the expression, and `trx-result-open` stays non-forcing.

Cache bypass, `$recheck = ($i -eq 0)`: `LinkCacheRefusesReplacement` failed with an empty `swap=` result. The parent re-lstat is what refuses a directory replaced by a symlink. Restored. `$recheck = ($i -le 1)` remains.

Neither mutant is in the tree.

## Proc-stat race

`tests/Antiphon.Tests/Scripts/LinuxScriptHarnessProcess.cs` `ConfirmDeadAsync`. A vanished `/proc/<pid>/stat` throws `IOException` "No such process".

- Red witness at `492302404a4c84850fef3ec0423dd1d4b4c342e1`: `Vanished_proc_stat_is_process_gone` executed 1, failed 1, succeeded 0, `TEST_EXIT:2`.
- Green catch at `ecd0b58b3d9a0934ab7ddc9f02396181918fe175`: same method executed 1, succeeded 1, 676ms, `TEST_EXIT:0`. `IsVanishedProc` treats that `IOException` ("No such process" or "No such file") as process gone. `UnauthorizedAccessException` still propagates.

The green catch is the tip. The witness is inside CP-1 and passed again in the green rerun.

## Speed

Synthetic layout measured after the sibling-timing fix: run directory 3298 files, timing directory 1699 files, `summary.json` 246415360 bytes. `SETUP_SECONDS` 3.478. `EXPORT_SECONDS` 8.559. Seed digest `5282b8d1e36a82739eb2bf38b02530a47691a850e6377a5469535012c543f901`. Under the 60 second target. The measurement stub was `/tmp/d63-seed-measure` and is deleted.

`SiblingTimingFromSummary` invokes the export with `-RunDirectory` set to the run directory, `-SourceCandidateId rc-synthetic-old`, `-SourceSha` forty `a` characters, and no `-TimingRoot`. It passed. Seed SHA256 `839f8516bfd8ca0f3b86d8ced45cc9168ca9bad2ee08a98bd771c58ff4ce45e4`.

Script cases under build slot `d63-link-r2` (lease `423c5b56-78e7-46a7-a9e4-4a3c8bd2c94c`, waited 45s, `SLOT_EXIT:0`), all `CASE_EXIT:0`: `RefusesIncompleteEvidence`, `BuilderReadOnly`, `FailedFirstMapping`, `AcknowledgementRequired`, `HashMustVerify`, `TimeoutIsFailure`, `ScheduleConservesAllChunks`, `InvalidSeedFallsBack`, `PublicHopsForwardAcknowledgement`, `SiblingTimingFromSummary`, `StoppedTimingRootAndRefusals`, `ChunkUnfinishedTolerance`, `LinkCacheRefusesReplacement`.

## Checkpoints

Plan: `docs/superpowers/plans/2026-10-31-card-1514-stopped-run-seed-layout-plan.md`. Import warned that CP-1's derived row timeout of 120 minutes exceeds the 45 minute advisory. The runs passed `--row-timeout 70m`, which is the deadline that was used.

### First six-row run, red

Run `20261031-144658-1d70`. Commit `9222d590211317e4ce0fa110e40d98ecf2ab3ef1`. `dirty=0`, `buildSource=verified`. Wall 3871.843s, execution 2836.634s, admission-only 1035.209s. Verdict `RED exit=1`. Rows: 2 green, 4 red.

```
CHECKPOINT CP-1 commit=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 build=ok filter=/*/*/(RcStoppedRunSeedTests*)|(RcFailedFirstPlannerTests*)|(RcFailedFirstExecutionTests*)|(RcFailureHistoryTests*)|(RcFullWatchdogHarnessTests*)|(C1514LandingSplitBudgetTests*)|(RcRosterRepairTests*)|(ScriptHarnessProcessContractTests*)|(ProcessSpawnLimitTests*)|(SlowTestTripwireTests*)|(TestLaneCategoryGuardTests*)|(TestClassificationPolicyTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=125 passed=124 failed=1 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-144658-1d70/rows/CP-1/run.trx slot=granted waited=210s dirty=0 source=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 sourceState=clean buildSource=verified
```

Sole CP-1 failure: `RcFailureHistoryTests.RefusesIncompleteEvidence`. The script row was 37 passed, 10 failed. The ten rows (`provenance-sourceRunId`, `provenance-sourceSha`, `provenance-policyHash`, `provenance-profile`, `provenance-platform`, `provenance-sourceAttempt`, `duplicateUid`, `invalidMapping`, `ambiguous`, `boundPlan`) threw `evidence-link` because rewriting `timing/groups-planned-antiphon.json` changed that file's inode and the cache treated the new inode as a link. Fixed by `ac2dd388850d336818850609537f088b289a99dc`.

```
CHECKPOINT CP-2 commit=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogTests/CapFitsAdmittedPlannedCost executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-144658-1d70/rows/CP-2/run.trx slot=granted waited=165s dirty=0 source=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 build=reused filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-144658-1d70/rows/CP-3/run.trx slot=granted waited=225s dirty=0 source=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 sourceState=clean buildSource=verified
```

CP-4, CP-5, and CP-6, same commit, each failed the same two methods: `NativeLinuxPolicyWitnessTests.Markers_do_not_expand_policy_exclusions` and `Policy_rows_require_pinned_attribute`.

```
CHECKPOINT CP-4 commit=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 build=ok filter=/*/*/(ProcessSpawnLimitTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=5 passed=3 failed=2 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-144658-1d70/rows/CP-4/run.trx slot=granted waited=45s dirty=0 source=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 build=ok filter=/*/Antiphon.TestSupport/NativeLinuxPolicyWitnessTests/* executed=3 passed=1 failed=2 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-144658-1d70/rows/CP-5/run.trx slot=granted waited=15s dirty=0 source=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 build=ok filter=/*/*/(ProcessSpawnLimitTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=5 passed=3 failed=2 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-144658-1d70/rows/CP-6/run.trx slot=granted waited=60s dirty=0 source=9222d590211317e4ce0fa110e40d98ecf2ab3ef1 sourceState=clean buildSource=verified
```

Reverse-coverage suites: CP-4 `session-runner` rows `Card1619LinuxCustodyStopIntentTests`, `Card1619LinuxStopIntentTests`, `Card1619RunnerExitTests`. CP-5 `pty-host` rows `Card1619LinuxHostExitTests`, `Card1619StopIntentTests`. CP-6 `agents-pty` row `Card1619UnixExitTests`. The Antiphon.Tests witness in CP-1 did not fail. This branch does not edit those classes or `tests/test-execution-policy.json`. These three rows were not re-run after the link-cache fix.

### Discarded rerun

Run `20261031-155822-4d22` on `ac2dd388850d336818850609537f088b289a99dc` exited 2 in 14s. CP-1, CP-2, and CP-3 were `refused: pre-existing-build-source` because `tests/Antiphon.Tests/bin-d63-seed1/checkpoint-build-source.json` was still present. That directory was removed by its literal path and the run below replaced it. It is not a product failure.

### Green rerun of CP-1, CP-2, CP-3

Run `20261031-155917-a21c`. Commit `ac2dd388850d336818850609537f088b289a99dc`. `state=clean`, `buildSource=verified`, `dirty=0`. Wall 4156.231s, execution 3090.824s, admission-only 1065.407s. Sequential-equivalent 59m37s. Builds 4, max-concurrent-builds 1. Rows: 3 green, 0 red. Verdict `GREEN exit=0`. `RUN_EXIT:0`. The tool deleted `bin-d63-seed1/`.

```
CHECKPOINT CP-1 commit=ac2dd388850d336818850609537f088b289a99dc build=ok filter=/*/*/(RcStoppedRunSeedTests*)|(RcFailedFirstPlannerTests*)|(RcFailedFirstExecutionTests*)|(RcFailureHistoryTests*)|(RcFullWatchdogHarnessTests*)|(C1514LandingSplitBudgetTests*)|(RcRosterRepairTests*)|(ScriptHarnessProcessContractTests*)|(ProcessSpawnLimitTests*)|(SlowTestTripwireTests*)|(TestLaneCategoryGuardTests*)|(TestClassificationPolicyTests*)|(NativeLinuxPolicyWitnessTests*)/* executed=125 passed=125 failed=0 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-155917-a21c/rows/CP-1/run.trx slot=granted waited=75s dirty=0 source=ac2dd388850d336818850609537f088b289a99dc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=75s executed=2365.664s wall=2440.910s
CHECKPOINT CP-2 commit=ac2dd388850d336818850609537f088b289a99dc build=reused filter=/*/Antiphon.Tests.Scripts/RcFullWatchdogTests/CapFitsAdmittedPlannedCost executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-155917-a21c/rows/CP-2/run.trx slot=granted waited=480s dirty=0 source=ac2dd388850d336818850609537f088b289a99dc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=480s executed=12.348s wall=492.630s
CHECKPOINT CP-3 commit=ac2dd388850d336818850609537f088b289a99dc build=reused filter=/*/Antiphon.Tests.TestHelpers/RcManifestIntegrationTests/LedgerAndReceiptBytesAreUnchanged executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-d63efd0a/.antiphon/checkpoints/20261031-155917-a21c/rows/CP-3/run.trx slot=granted waited=510s dirty=0 source=ac2dd388850d336818850609537f088b289a99dc sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=510s executed=133.889s wall=644.229s
```

Slow classes on this run: `RcFailedFirstPlannerTests` 946s (2), `RcFailedFirstExecutionTests` 885s (2), `RcStoppedRunSeedTests` 135s (12), `RcFailureHistoryTests` 130s (2), `TestClassificationPolicyTests` 118s (39), `C1514LandingSplitBudgetTests` 92s (5), `RcManifestIntegrationTests` 124s (1).

## Land graph, evidence, merge-tree

`scripts/audit-land-verification-graph.ps1 -Check` (no `-Update`) after a slotted Release build of `tools/Antiphon.LandGraphAudit` to `bin-land-audit/Release/net10.0/`:

```
digest=E7C6EDAFA83B544A42E186899267267FD73E6FDD2FB7DD2DF36C233F2C405806 count=69
phase=pass2
approved
```

`AUDIT_EXIT:0`. No definition file changed, so the recorded digest is unchanged.

`scripts/check-evidence-diff.ps1 -BaseRef a00f1b46753542a8721a111fb4c26ed1d542cf2a -HeadRef HEAD`: `EVIDENCE result commits=6 entries=0 violations=0`, exit 0.

`git fetch origin master` then `git merge-tree --write-tree origin/master HEAD`:

- `origin/master` is `2051d327123a5e98524b4bd1ecf31c6e516bdb4e`.
- Exit 1. The command printed tree `f675ad467412d9c55c3fb57282a72dc1deb00061` and 97 conflict paths.
- The same 97 paths conflict against the base `a00f1b46753542a8721a111fb4c26ed1d542cf2a`. This branch added no conflict path and removed none.
- Two files this branch edits are already in that inherited set: `scripts/lib/nightly-failure-history.ps1` (`CONFLICT (add/add)`) and `tests/Antiphon.Tests/Scripts/ScriptHarnessProcessContractTests.cs` (`CONFLICT (content)`). `tests/Antiphon.Tests/Scripts/LinuxScriptHarnessProcess.cs` auto-merged.
- Master land is therefore still conflicted. This tip was not merged.

`git merge-tree --write-tree origin/rc/2026-10-27 HEAD`:

- `origin/rc/2026-10-27` is `e0d49d221513b467f8c853f47587e688e04255e2`.
- Exit 0. Merged tree `30fc2bf947713a84985471ebd488e35880ff547d`.

## Operator commands

Run these from a reviewed checkout after land. They point at the run directory. `summary.json` already names the timing sibling, so `-TimingRoot` is omitted. Do not run them against the live tree from this worktree; the live run stays read-only here. `-TimingRoot` remains on the export script for a stopped run that has no `summary.json`.

```powershell
$launcher = 'C:\Antiphon\worktrees\card-task-d63efd0a'  # or the reviewed checkout after land
$seed = Join-Path 'C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638' 'ordering-seeds\d47848cc.json'
pwsh -NoProfile -File "$launcher\scripts\export-nightly-stopped-run-seed.ps1" `
  -RunDirectory 'C:\Antiphon\releases\candidates\rc-2026-10-27-cde8def41638\logs\2026-10-30-0824-d47848cc' `
  -OutputPath $seed `
  -SourceCandidateId 'rc-2026-10-27-cde8def41638' `
  -SourceSha 'cde8def4163829aa5b867df6ec625433be2522cb'
# Then nightly-run.ps1 with:
#   -PriorFailuresPath $seed
#   -PriorFailuresDigest <sha256 of $seed>
#   -AcceptCrossCandidateSeed
#   -SeedSourceCandidateId 'rc-2026-10-27-cde8def41638'
```

Real identity, for that export only: full run id `d47848cce896471db1cc8ca346707cad`, short prefix `d47848cc`, candidate `rc-2026-10-27-cde8def41638`, source sha `cde8def4163829aa5b867df6ec625433be2522cb`. The candidate id is an operator argument. It is not a `summary.json` field.

## Cleanup

Alternate outputs removed by literal path: `bin-d63-tool` (Checkpoints and HostCleanup), `bin-d63-seed1`, `bin-d63-runner1`, `bin-d63-pty1`, `bin-d63-agents1`, and `bin-land-audit` (LandGraphAudit and LandGraphAudit.Core), including the checkpoint `builds/bin-d63-*` markers. Task scratch under `/tmp/d63-*` is removed. A later `find` printed no remaining matches.

--- next stage ---
next: review
handoff: short Final review of D-1, D-2, and D-3, then merge with failed-first as rc top-up 52
artifact: /work/worktrees/task-d63efd0a/.antiphon/task-d63efd0a.md
