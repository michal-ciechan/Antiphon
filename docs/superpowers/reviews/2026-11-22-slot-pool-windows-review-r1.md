# CARD-0824 Review: Windows slot release, exited-process census

- Review task: 26427a41 (Grok). Subject author: Mike Ciechan, Codex Debug task 3a5fe0b3.
- Round: 1, Final, ordinary scope Full.
- Subject: `origin/feat/card-task-3a5fe0b3`, tip `d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4`.
- Range: `6f6a789e07f031496ae99e58ee6e563d30837a64..d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4` (merge-base of the three subject commits).
- Reviewed source SHA: `d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4`.
- Fetched `origin/master` at review: `e3806b6278227b5acb364c6cd530da45e260a031`. The brief named `70c13dfe001c5df6c2108000584da6151ce42e43`, which is an ancestor of that tip. This branch is 21 behind and 3 ahead. Merge-base is unchanged.

## Verdict: blockers

The new exit check is fail-closed for a live process. It answers "exited" only when `WaitForSingleObject` on that same handle returns 0. Two defects hold the land.

1. `Cwd_census_skips_exited_process_with_retained_handle` is Windows-only at runtime and is registered in neither `WindowsUnitSkipRoster.WindowsOnly` nor the CARD-0824 Linux execution-policy exclusion.
2. Regenerating `tests/derived-impact-index.json` at this tip adds `C0824WindowsSlotHolderTests` under `RunnerWorkspaceSlotLedger.cs`. The committed index does not.

Positive controls stay pending. Operator 2026-11-12 waived mutation execution. Advisories below do not add further holds.

## Blockers

### 1. The new Windows witness is unregistered

Where: `tests/Antiphon.SessionRunner.Tests/C0824WindowsSlotHolderTests.cs` method `Cwd_census_skips_exited_process_with_retained_handle`. The identity is absent from `WindowsUnitSkipRoster.WindowsOnly` in `tests/Antiphon.Tests/Scripts/WindowsUnitSkipRoster.cs` and from the Linux row in `tests/test-execution-policy.json`. That row, owner CARD-0824, lists only `Rm_lock_defers_release`. `tests/expected-platform-skips.json` has no row for the new method.

Failure: The method calls `NativeWindowsGate.Require` and throws `SkipTestException` off Windows. `[SupportedOSPlatform("windows")]` does not skip at runtime. CP-1 recorded `NotExecuted` with debug trace `Skipped: An exited Windows process can retain an open kernel handle after losing its cwd memory`. `scripts/validate-checkpoint-receipt.ps1` on the whole run printed `CHECKPOINT SOURCE INVALID reason=skip_identity_unlisted`. CP-2 and CP-3, which skipped nothing, printed `CHECKPOINT SOURCE VALID` for the same SHA. A Linux rc launch still selects the method. `RcChunkOutcome.Checked` then records `unexecuted-outcome` because the skip roster it consults (`expected-platform-skips.json`) does not explain that trace.

Why: The receipt validator accepts a non-Windows `NotExecuted` only when `namespace.class.method` is in `WindowsOnly` at the receipt SHA, with a matching result count. The rc chunk accepts a launched `NotExecuted` only when the platform skip entry's `reasonPrefix` equals the text after `Skipped: `. The execution policy is what keeps `Rm_lock_defers_release` from being launched on Linux. The new method has none of those three registrations.

Fix: Add the method to the existing CARD-0824 Linux exclusion so qualification does not launch it, and add a `WindowsOnly` row: namespace `Antiphon.SessionRunner.Tests`, class `C0824WindowsSlotHolderTests`, method `Cwd_census_skips_exited_process_with_retained_handle`, count 1, reason equal to that `SkipTestException` text. Re-run the holder receipt until `skip_identity_unlisted` is gone. Leave the `WaitForSingleObject` check as it is.

### 2. The derived index misses the new ledger edge

Where: `tests/derived-impact-index.json`, path `src/Antiphon.SessionRunner/RunnerWorkspaceSlotLedger.cs`.

Failure: At `d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4`, `scripts/regenerate-derived-impact-index.ps1` printed `derived-impact-index changed` and inserted one line, `C0824WindowsSlotHolderTests`, into that path's class list. The immediate `-NoBuild` run printed `derived-impact-index unchanged`. Load at start was 2.34. Lease `da4d9f48-0ead-4ab0-96af-0cb0c49a4d24`, held 72s. This review restored the committed bytes and did not commit the regen.

Why: `Persistent_holder_quarantines_after_linger_budget` reads `QuarantinedDirectories`. The generator records that reference. The fix commit did not regenerate the index.

Fix: Regenerate and commit that one-line addition.

## Exit check

`TryReadOpenProcessCwd` sets `skipped` only when `WaitForSingleObject(handle, 0)` equals `WaitObject0` (0). `GetExitCodeProcess` is not called, so exit code 259 is not a signal. A live handle returns `WAIT_TIMEOUT` (258). `skipped` stays false and the method returns false, so `CensusCwd` aborts and `Scan` stays incomplete. `WAIT_FAILED` (0xFFFFFFFF), including a handle opened without `SYNCHRONIZE`, takes the same incomplete path. The fallback `OpenProcess` without `SYNCHRONIZE` therefore cannot turn a failed cwd read into a skip.

`OpenProcess` failure still sets `skipped=true` and returns true before any wait. That is the previous access-denied behavior, the same bound as an unreadable uid on Linux. A 32-bit WOW64 process is still skipped before the wait. Neither path is a new "exited" classification.

The wait uses the handle already open for that pid. Windows does not recycle the pid while the handle is open. A terminated process stays signaled, and its cwd is already released, which is the g92 case: git pids 115144 and 110328, `ERROR_PARTIAL_COPY` (299), exit code 0, holders 0, `Detail=scan incomplete`.

`IsWow64Process` failure used to return false immediately. It now falls through to the same wait. A live process still fails the census. Only a signaled handle is skipped.

`LingerRetries` remains 3 in `PhoneHomeWorkspaceSlotSettings`. `Backoff` remains 1, 2, 4, then 5 minutes. The diff does not touch `RunnerWorkspaceSlotPool`. An incomplete scan still returns inside `SettleAsync` before unpushed and dirt quarantine. After the linger budget, `QuarantineAndRehomeAsync` records the directory in place and re-homes the slot id to a new Free path with `ReleaseRequested=false`.

`ProcWorkspaceSlotProcessProbe` is outside the three-file diff. Off Windows, `RunnerWorkspaceSlotPool` still constructs it. `WindowsWorkspaceSlotProcessProbe.Scan` with no backend still returns incomplete when `OperatingSystem.IsWindows()` is false (line 34 at both revisions).

## g94

The original g94 failure was a dirty-slot fixture that expected one quarantine and found zero before the route. That count is written only after a complete scan with zero holders. The same incomplete census that deferred g92 returns before `DirtAsync`, so a one-shot partial copy on an exited git leaves the ledger at 0. That mechanism can explain the original miss. The author recorded six passes on unchanged master (the class run plus five isolated runs) and the g94 TRX has no scan trace. This diff adds no g94 assertion. g94 remains open. It does not add a hold beyond the two blockers.

## Linux checkpoint

One checkpoint-tool run, `20261122-041608-e619`, at `d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4`, branch `feat/card-task-26427a41`, host Debian GNU/Linux 12, start load 12.10. Verdict GREEN, exit 0. Two isolated builds (`bin-c0824-r1-hold`, `bin-c0824-r1-gate`), 3 rows, 0 red, unlisted none. The tool took its own build slots (`slot=granted`, `waited=0s`). Each row has `dirty=0`, `sourceState=clean`, `buildSource=verified`.

```
CHECKPOINT CP-1 commit=d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4 build=ok filter=/*/*/(C0824WindowsSlotHolderTests*)|(C0824WorkspaceSlotProcessScanTests*)/* executed=5 passed=5 failed=0 skipped=2 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-041608-e619/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-1 waited=0s executed=9.013s wall=9.448s
PHASES CP-1 slotWait=0s build=69.2534165s startup=2.871501s testsWall=4.5119242s teardown=0.4199735s hostWall=7.8033792s
CHECKPOINT CP-2 commit=d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4 build=ok filter=/*/*/C0824WorkspaceSlotRoleGateTests/* executed=20 passed=20 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-041608-e619/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=42.231s wall=42.540s
PHASES CP-2 slotWait=0s build=245.6819887s startup=23.2568832s testsWall=13.823009s teardown=4.1730188s hostWall=41.2529111s
CHECKPOINT CP-3 commit=d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4 build=reused filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(SlowTestRegistryMergeTests*)|(SlowTestTripwireTests*)|(NativeLinuxPolicyWitnessTests*)|(MechanicalPcContractTests*)|(TaskPlatformGuidanceTests*)|(C1729LayeringGuardTests*)|(TestLaneCategoryGuardTests*)/* executed=84 passed=84 failed=0 skipped=0 trx=/work/worktrees/ws-07-d2/.antiphon/checkpoints/20261122-041608-e619/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-3 waited=0s executed=79.370s wall=79.750s
PHASES CP-3 slotWait=0s build=0s startup=18.2435129s testsWall=59.7103087s teardown=0.6226144s hostWall=78.5764362s
```

| Row | Executed | Passed | Failed | Skipped | Wall |
|---|---:|---:|---:|---:|---:|
| CP-1 | 5 | 5 | 0 | 2 | 9s |
| CP-2 | 20 | 20 | 0 | 0 | 43s |
| CP-3 | 84 | 84 | 0 | 0 | 80s |

CP-1 passed `Incomplete_scan_defers_release`, `Persistent_holder_quarantines_after_linger_budget`, `C0824_OpenFdHolderDefersThenQuarantines`, `C0824_CwdHolderDefersThenQuarantines`, and `C0824_HolderExitsBeforeRetryReleasesNormally`. Skipped: the new retained-handle witness, and the pre-existing `Rm_lock_defers_release` (`Skipped: CARD-0824 Windows Restart Manager file lock; the real probe runs on the desktop at S4`). `Persistent_holder_quarantines_after_linger_budget` advanced 1, 2, and 4 minutes and passed: one `lingering` quarantine, directory retained, replacement `Free`, `ReleaseRequested` false.

CP-3 counts: TestClassificationPolicyTests 45, TestClassificationGuardTests 1, SlowTestRegistryMergeTests 5, SlowTestTripwireTests 2, NativeLinuxPolicyWitnessTests 3, MechanicalPcContractTests 11, TaskPlatformGuidanceTests 5, C1729LayeringGuardTests 11, TestLaneCategoryGuardTests 1. `NativeLinuxPolicyWitnessTests` passed, so the pins that class asserts match this tree. No row wall exceeded 30 minutes. The tool warned that CP-2's derived 60-minute deadline exceeds the 45-minute advisory; the row finished in 43 seconds. Budget wall 885.632s.

`scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261122-041608-e619/report.json -ExpectedSourceSha d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4` refused the whole report with `skip_identity_unlisted`. The same command with `-Rows CP-2,CP-3` printed `CHECKPOINT SOURCE VALID source=d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4 rows=2`. `reviewedSourceClean` is false because the holder row's skips are unrostered.

Author Windows counts at `7f34808c72ddb5f59a923313b89ab58e21c0f033` (holder 4/4, role gate 20/20, C1960 14/14) and the red witness at `f06e42737c556cbedebb3480b77c150164980a8f` (0 passed, 1 failed) were not executed here. The desktop tree `C:\Antiphon\worktrees\card-task-3a5fe0b3` is not readable from this runner. Those counts stay unverified. The rc rerun is the Windows proof.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | all | `tests/Antiphon.SessionRunner.Tests -> bin-c0824-r1-hold/` | holder | holder and proc scan | V-linux-holder | 5 passed, 2 skipped | 5 | 10 | false | TEMP=/tmp |
| CP-2 | all | `tests/Antiphon.Tests -> bin-c0824-r1-gate/` | role-gate | role gate | V-g92 | 20 passed | 20 | 20 | true | TEMP=/tmp |
| CP-3 | all | CP-2 | guards | guard batch | V-guards | 84 passed | 60 | 8 | true | TEMP=/tmp |

## Gates

- Evidence-diff `6f6a789e07f031496ae99e58ee6e563d30837a64..d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4`: `EVIDENCE result commits=3 entries=0 violations=0`, exit 0. `scripts/check-evidence-diff.ps1` is not in this tree; the command was `antiphon-scripts evidence-diff`. Load 1.60. Lease `2078c4cc-8345-44b3-89bc-2957497e3c59`, held 23s.
- `git diff --check` on that range: exit 0.
- `git merge-tree --write-tree origin/master HEAD`: exit 0, tree `e6c3d38eb3907f5bd0c2c4ef36528a3fdbe8c674`. No conflicts. Fetched master is `e3806b6278227b5acb364c6cd530da45e260a031`.
- Land-graph `-Check` (never `-Update`), load 2.26, lease `aeefe8f2-ea83-4bd6-b5e5-00c1bd29af48`, held 33s. The audit tool was missing and rebuilt in 13s. `digest=A9BB27C595EA171463F0ED13725E198AA64385B1744B9E0BA2D211829C65A7CF count=80`, then `approved`, exit 0.
- Impacted classes `-Range 6f6a789e07f031496ae99e58ee6e563d30837a64..d7f4c3157bf9719fd7f84b6d740efbfddb41a1a4` (not `-MergeSubset`): `kinds` empty, class `C0824WindowsSlotHolderTests`, filter `/*/*/(C0824WindowsSlotHolderTests*)/*`, owed empty, capped empty. No migrations kind, so the downgrade selector count is 0. CP-1 covers that class. The three `/proc` scan methods were added to CP-1 because the Linux probe is the portability check; they are not an impacted-class hit.
- Three-dot names are `src/Antiphon.SessionRunner/WindowsWorkspaceSlotProcessProbe.cs`, `tests/Antiphon.SessionRunner.Tests/C0824WindowsSlotHolderTests.cs`, and `docs/investigations/2026-11-22-slot-pool-windows-g92-g94.md`.
- OS lexical candidates are unchanged between the merge-base and the tip: `WindowsWorkspaceSlotProcessProbe.cs:34` `if (!OperatingSystem.IsWindows())`, and `C0824WindowsSlotHolderTests.cs:41` and `:46`. The probe line is the off-Windows incomplete return. The test lines choose which assertion that method runs. The Windows exit check has no `OperatingSystem.Is*` candidate; its native execution stays pending.
- Placement was read from `GET /api/runner-defaults` (revision 2, global runner set, supported kinds Grok, ClaudeCode, Codex) and `GET /api/session-runners` (three catalogue rows: one Windows dispatch-eligible, one Linux dispatch-eligible, one unavailable). Portable rows stay unpinned. No `-Platform`.
- No input-wait or parking change. Parking disabled means no automatic release deadline (CARD-1083).

OS-witness declaration. `scripts/check-os-witnesses.ps1` against `b2d6db8c88c6be9f4b6b93fa120868bacd237d6c` (the commit that first contains this plan) printed `NATIVE pending W-exit-handle` and `OS-WITNESS complete syntax/inventory; execution and runtime identity require Review`, exit 0. Base `6f6a789e07f031496ae99e58ee6e563d30837a64`. Base is the merge-base above. Head is that commit. Inspected paths: `src/Antiphon.SessionRunner/WindowsWorkspaceSlotProcessProbe.cs` disposition `os-gated` (off-Windows `Scan` returns incomplete; the exit check lives in the same file), and `tests/Antiphon.SessionRunner.Tests/C0824WindowsSlotHolderTests.cs` disposition `test-only`. Candidates `base` and `head` line 34 of the probe are witness `W-off-windows`. Candidates `base` and `head` lines 41 and 46 of the test are `non-behavior` (they select the assertion inside `Incomplete_scan_defers_release`). Witness `W-off-windows`: real type `WindowsWorkspaceSlotProcessProbe`, factory `RunnerWorkspaceSlotPool`, test `Incomplete_scan_defers_release`, execution `portable-forced`, checkpoint CP-1, target OS Linux, native obligation `not-required` because CP-1 executed that assertion. Witness `W-exit-handle`: same source, branch anchor `WaitForSingleObject`, real type `WindowsSlotHolderQuery`, factory `RunnerWorkspaceSlotPool`, test `Cwd_census_skips_exited_process_with_retained_handle`, execution `native`, checkpoint CP-1, target OS Windows, native obligation `pending` (this host recorded NotExecuted). Restart impact `required` false, paths empty. The checker does not execute tests.

## Advisories

`Rm_lock_defers_release` is already on the Linux exclusion and was already absent from `WindowsOnly`. CP-1's receipt also fails for that pre-existing identity. A class-wide holder receipt stays invalid until that identity is rostered too. Linux rc already does not launch it.

The retained-handle witness calls `WindowsSlotHolderQuery.TryReadOpenProcessCwd` on the handle it kept open. It does not call `Scan`. There is no Linux seam that injects an exited handle versus a live handle. `Persistent_holder_quarantines_after_linger_budget` injects a completed scan that already contains a cwd holder.

There is no test that a live handle whose cwd read fails stays incomplete. The code path is the `WAIT_TIMEOUT` return described above. That gap does not show a fail-open.

Access-denied `OpenProcess` remains a skip, as it was before this diff. It is not labeled exited by the new wait.

The branch is 21 commits behind fetched master. Merge-tree is clean. Landing rebases.

Wrapped `pwsh` build-slot commands printed the CARD-1627 `maxcpucount` warning. The derived-index script still passed `-maxcpucount` from `ANTIPHON_BUILD_SLOT_MAXCPUCOUNT`. The checkpoint tool leased its own slots.

PCs were not run. Native Windows execution of the retained-handle witness, the role gate, and C1960 stays pending for the rc rerun.
