# CARD-1313 Windows rows: CP-19 red; CP-22 and CP-23 green

Test task `0d0adf42`. The three requested Windows rows were exercised: **5 initial executions, 2 passed, 3 failed, 0 skipped**. Each failed CP-19 parameter case was rerun alone once and failed again: **3 additional executions, 0 passed, 3 failed, 0 skipped**. No production or test code was changed. Windows qualification remains red because CP-19 fails.

Candidate/task base: `585303a77a4cf758d83be104bed7ad1e95082559`.
Tested source: `d66f5d643f5264d8e7de55e46d37af9efb1c7ad2`, branch `feat/card-task-0d0adf42`.
The only difference between those SHAs is the earlier report file. `git diff --exit-code 585303a77a4cf758d83be104bed7ad1e95082559 d66f5d643f5264d8e7de55e46d37af9efb1c7ad2 -- . ':!docs/investigations/2026-10-16-card-1313-windows-rows.md'` exited 0. Later report commits are not relabeled as tested source.

The initial blocked pass at `d66f5d643f5264d8e7de55e46d37af9efb1c7ad2` ran no builds/tests. The caller then explicitly authorized only the production runner's `/build-slots` broker for leases. This resumed pass used that broker; it called no other 17204 route, made no AppHost request, and performed no runner restart/stop. The main assembly confirmed its production-runner refusal guard in test output. Main and PTY execution was sequential.

## Results, exact counts and measured costs

| Activity | Executed / passed / failed / skipped | Outer wall seconds | Exit | Result |
|---|---|---:|---:|---|
| Tool bootstrap, initial invocation | No tests | 1.8134660 | 1 | PowerShell split unquoted `-nodeReuse:false`; MSB1041 |
| Tool bootstrap, corrected invocation | No tests | 5.6061343 | 0 | Build succeeded, 4 warnings |
| Full approved plan import/run request | No tests/build | 0.2527468 | 2 | Fractional estimate rejected at CP-6 before row selection |
| CP-19 checkpoint row, producing build + attempted launch | 0 / 0 / 0 / 0; no TRX | 234.8027322 | 2 | Build succeeded; missing apphost launch |
| CP-19 checkpoint row, isolated no-build retry | 0 / 0 / 0 / 0; no TRX | 39.3449499 | 2 | Same missing apphost launch |
| CP-19 exact-filter DLL fallback under build-slot gate | 3 / 0 / 3 / 0 | 36.7844741 | 2 | Three actual test failures |
| CP-22, producing PTY build + exact test | 1 / 1 / 0 / 0 | 86.9041876 | 0 | Passed |
| CP-23, reused PTY build + exact test | 1 / 1 / 0 / 0 | 10.1201491 | 0 | Passed |
| CP-19 `delete`, isolated UID rerun | 1 / 0 / 1 / 0 | 18.6199141 | 2 | Same cleanup failure |
| CP-19 `case-rename`, isolated UID rerun | 1 / 0 / 1 / 0 | 14.2552529 | 2 | Same cleanup failure |
| CP-19 `nul-paths`, isolated UID rerun | 1 / 0 / 1 / 0 | 8.4346001 | 2 | Same Git refusal |

Non-discovery driver walls above total **456.9386071 s**. This sum excludes reading/reporting, validation, discovery wrapper overhead and cleanup work; it is not total task elapsed time. Producing build logs report CP-19 MSBuild wall **171.33 s** and CP-22 **15.29 s**, already included in their row costs. Every build/execution/discovery used a granted host lease with zero reported queue wait; there was no unleased or `-NoSlot` run. No full-suite or broad execution was added.

Fresh TRX was inspected for all eight actual results. CP-19 initial test durations: `nul-paths` 5.0636226 s, `case-rename` 12.4909142 s, `delete` 16.2247431 s. Isolated rerun test durations respectively: 5.3093347 s, 11.4127896 s, 15.3126626 s. CP-22 test duration: 0.7630037 s; CP-23: 0.8749586 s. Counts refer to outer TUnit executions, not script assertion lines or discovery nodes.

## Failure table

| Name | Error and observation | Isolated rerun | Whether inherited from base |
|---|---|---|---|
| `TestSelectionTests.C1313_PathIdentity(delete)` | `IOException: ScriptHarness cleanup failed`; the script printed `PASS C1313 old-new-union` and its success trailer, then owned-path deletion failed: `Access to the path '8d0c028fbc4cf41741ee3c24b1e0dd6e035cba' is denied.` Reported at `ScriptHarnessProcess.cs:142`. | Exactly one UID, one result, failed with the same error. | Present with unchanged candidate/task-base production and test sources. This Test pass introduced no code; a separate pre-implementation baseline was not run, so no claim is made that it predates CARD-1313. |
| `TestSelectionTests.C1313_PathIdentity(case-rename)` | Same cleanup failure and Git object leaf; script assertion printed `PASS C1313 ordinal-paths` before cleanup failed. | Exactly one UID, one result, failed with the same error. | Same qualification as above. |
| `TestSelectionTests.C1313_PathIdentity(nul-paths)` | `ShouldAssertException: result.ExitCode should be 0 but was 1`; `scripts/lib/test-selection.ps1:125` throws `git-read-failed:update-index:128`. The script failed before its required success assertion. Git stderr was not exposed by this wrapper, so the precise reason for Git's exit 128 is not established here. | Exactly one UID, one result, same `git-read-failed:update-index:128`. | Same qualification as above. |
| CP-19 checkpoint launcher | `Unhandled exception: An error occurred trying to start process 'C:\Antiphon\worktrees\card-task-0d0adf42\tests\Antiphon.Tests\bin-c1313-windows\Antiphon.Tests.exe' ... The system cannot find the file specified.` The tool's build rewrite produced the DLL with `UseAppHost=false`, while the run evaluated an exe launch. Build succeeded; both attempts had no TRX. | Same exact row/filter retried alone with `--no-build`; same error, no tests. | Runner source is unchanged from task base. Not a test assertion failure or a repair made by this pass. |
| Full-plan importer | Exact error: `CP-6: EstimatedMinutes must be a positive integer with safe derived deadlines`. Full table parsing precedes `--rows`; CP-23 also contains historical estimate 0.5. | Bypassed via the existing `row` command, not by repairing source or changing the approved plan. | Already present in candidate and documented by its S1-S3 Code report. No individual row remained blocked by estimates after using `row`. |
| Bootstrap command transport | `MSB1041: Specify one or more parameters for node reuse if using the -nodeReuse switch`; effective argv had `-nodeReuse: false`. | Corrected command quoted `'-nodeReuse:false'`; build passed. | Invocation error in this pass, not a candidate defect. |

Failure cleanup retained its diagnostic temp directories. Original delete/case-rename roots include `c1313-nightly-567ca0e0b9024637bb60f87df6bcac0e` / `c1313-nightly-3c9ea8146dc648d9b8100d60fc51f628`; rerun roots include `c1313-nightly-265a0f65427343c6baa20329b282821a` / `c1313-nightly-aa3d8b62b0ae49e39bd772c26f759999`, under `C:\Users\lndco\AppData\Local\Temp`, with matching `antiphon-script-control-<suffix>` roots. They were not repaired/deleted. Complete errors and retained paths are in the respective TRX files.

## Exact execution commands

All commands ran from `C:\Antiphon\worktrees\card-task-0d0adf42`. Common bindings below spell out the repeated literal values used; they do not imply a different shell or extra driver:

```powershell
$pwsh = 'C:\Program Files\PowerShell\7\pwsh.exe'
$env:ANTIPHON_BUILD_SLOTS_URL = 'http://localhost:17204/build-slots'
$tool = 'tools/Antiphon.Checkpoints/bin-c1313-windows-tool/Antiphon.Checkpoints.dll'
$sha = 'd66f5d643f5264d8e7de55e46d37af9efb1c7ad2'
$results = '.antiphon/c1313-windows-resume'
$main = 'C:/Antiphon/worktrees/card-task-0d0adf42/tests/Antiphon.Tests/bin-c1313-windows/Antiphon.Tests.dll'

# Initial bootstrap failed before compilation; corrected retry follows.
& $pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1313-windows-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-c1313-windows-tool/ -nodeReuse:false
& $pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1313-windows-tool -- dotnet build tools/Antiphon.Checkpoints '--property:OutputPath=bin-c1313-windows-tool/' '-nodeReuse:false'

# Original plan request: rejected before scheduler/owner HTTP or any row launch.
dotnet $tool run --plan docs/superpowers/plans/2026-10-16-card-1313-change-aware-selection-plan.md --rows CP-19,CP-22,CP-23 --expected-source-sha $sha

# CP-19 checkpoint attempt, then isolated no-build launcher retry.
dotnet $tool row --name CP-19 --project tests/Antiphon.Tests --output-path bin-c1313-windows/ --filter '/*/*/TestSelectionTests/C1313_PathIdentity' --min-executed 3 --expect TestSelectionTests --expected-source-sha $sha --results-root $results
dotnet $tool row --name CP-19 --project tests/Antiphon.Tests --output-path bin-c1313-windows/ --filter '/*/*/TestSelectionTests/C1313_PathIdentity' --min-executed 3 --expect TestSelectionTests --expected-source-sha $sha --results-root $results --no-build

# Existing built DLL; same exact approved filter. Necessary additional driver
# because the checkpoint's Windows apphost launch failed. No extra build.
& $pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1313-cp19-dll -- dotnet $main --treenode-filter '/*/*/TestSelectionTests/C1313_PathIdentity' --report-trx --report-trx-filename run.trx --results-directory C:/Antiphon/worktrees/card-task-0d0adf42/.antiphon/c1313-windows-resume/cp19-dll --no-progress --no-ansi

# CP-22 needs native fakeclaude.exe; explicit apphost property is forwarded
# identically to its producing build, CP-22 execution and CP-23 reuse.
dotnet $tool row --name CP-22 --project tests/Antiphon.Agents.Pty.Tests --output-path bin-c1313-fake/ --filter '/*/*/ClaudeVerifiedDeliveryTests/Fake_evidence_appears_then_submit_completes_a_turn' --min-executed 1 --expect ClaudeVerifiedDeliveryTests --msbuild-property UseAppHost=true --expected-source-sha $sha --results-root $results

& $pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1313-pty-discovery -- $pwsh -NoProfile -File .antiphon/c1313-windows-resume/discover.ps1 -AssemblyPath tests/Antiphon.Agents.Pty.Tests/bin-c1313-fake/Antiphon.Agents.Pty.Tests.dll -Name pty -Filter '/*/*/ClaudeVerifiedDeliveryTests/*'

dotnet $tool row --name CP-23 --project tests/Antiphon.Agents.Pty.Tests --output-path bin-c1313-fake/ --filter '/*/*/ClaudeVerifiedDeliveryTests/C1313_DeliveryDiscovery' --min-executed 1 --expect ClaudeVerifiedDeliveryTests --msbuild-property UseAppHost=true --expected-source-sha $sha --results-root $results --no-build

& $pwsh -NoProfile -File scripts/build-slot.ps1 -Label c1313-main-discovery -- $pwsh -NoProfile -File .antiphon/c1313-windows-resume/discover.ps1 -AssemblyPath tests/Antiphon.Tests/bin-c1313-windows/Antiphon.Tests.dll -Name main

# Three sequential launches, each acquiring/releasing its own lease.
$cases = @(@{Name='delete';Index=1},@{Name='case-rename';Index=2},@{Name='nul-paths';Index=3})
foreach ($case in $cases) {
    $uid = 'Antiphon.Tests.Scripts.TestSelectionTests.1.1.C1313_PathIdentity(System.String).' + $case.Index + '.1.0'
    $resultPath = Join-Path $PWD ('.antiphon/c1313-windows-resume/rerun-' + $case.Name)
    & $pwsh -NoProfile -File scripts/build-slot.ps1 -Label ('c1313-cp19-rerun-' + $case.Name) -- dotnet $main --filter-uid $uid --report-trx --report-trx-filename run.trx --results-directory $resultPath --no-progress --no-ansi
}
```

Every invocation's exit/wall clock was recorded separately in `<activity>-time.txt`; the rerun loop continued after each failed child to collect all failures. No tree selector was combined with a UID selector. The `row` verb was chosen because it avoids both fractional plan import and the scheduler's task-owner status request to AppHost. The ignored normalized manifest from the first pass was **not used**. No approved plan/test/source was modified.

## Discovery and provenance

The ignored `discover.ps1` used `Invoke-NightlyOwnedProcess`, `ConvertFrom-NightlyDiagnosticLog` and `ConvertTo-NightlyDiscoveryDocument`; each native owner returned exit 0 and `CleanupComplete=true`. Its exact dotnet argument vector, absolute paths and hash are retained in each `discovery-*/summary.json`:

```text
dotnet <absolute-dll> --list-tests --no-progress --no-ansi --diagnostic --diagnostic-output-directory <owned-discovery-dir> --results-directory <owned-discovery-dir>
PTY only adds: --treenode-filter /*/*/ClaudeVerifiedDeliveryTests/*
```

| Producer | Parsed expanded discovery nodes | Relevant roster | Assembly SHA-256 | Slot-held script wall |
|---|---:|---|---|---:|
| Main CP-19 | 18453 | Three `C1313_PathIdentity` UIDs; source/diagnostic mapping: suffix `.1.1.0` delete, `.2.1.0` case-rename, `.3.1.0` nul-paths | `577C4BA3785F17595BF85732EF77EC56F6A3F0D26BFCEDBEF334979CC49491D0` | 18.7910038 s |
| PTY CP-22/23 | 761 | Diagnostic discovery emits all assembly nodes despite the class filter; selected class has six UIDs: five delivery UIDs (one fake + four real shapes) plus `C1313_DeliveryDiscovery`. | `7F73867BEE3D4EA7A677C5046F618BEF7B8A4E56C9B64F1E548FCD8CD69CD02A` | 2.8938229 s |

Discovery occurred after each producer's first execution, rather than before that execution; this ordering deviation is explicit. PTY discovery preceded CP-23; main discovery preceded the isolated UID reruns. No real-provider/TUI test was executed. Discovery is metadata, not additional test executions. Assembly hashes were rechecked after all runs and unchanged.

CP-22/23 report receipts independently validated:

```powershell
dotnet $tool validate --evidence .antiphon/c1313-windows-resume/CP-22-20261016-140735-8025/report.json --expected-source-sha $sha --rows CP-22
dotnet $tool validate --evidence .antiphon/c1313-windows-resume/CP-23-20261016-140950-488c/report.json --expected-source-sha $sha --rows CP-23
```

Both returned exit 0 and `CHECKPOINT SOURCE VALID source=d66f5d643f5264d8e7de55e46d37af9efb1c7ad2 rows=1`. CP-19's build-source stamp is verified and both checkpoint attempts recorded clean source, but their no-TRX receipts are INVALID. The DLL fallback is gated diagnostic execution with fresh TRX, not a green strict checkpoint receipt. No certificate is inferred for that red row.

## Unedited checkpoint lines

```text
CHECKPOINT CP-19 no TRX written to C:\Antiphon\worktrees\card-task-0d0adf42\.antiphon/c1313-windows-resume\CP-19-20261016-140114-cfcf\run.trx (runner exit 1)
CHECKPOINT CP-19 EXIT CODE: 2
CHECKPOINT CP-19 no TRX written to C:\Antiphon\worktrees\card-task-0d0adf42\.antiphon/c1313-windows-resume\CP-19-20261016-140617-ea3e\run.trx (runner exit 1)
CHECKPOINT CP-19 EXIT CODE: 2
CHECKPOINT CP-22 commit=d66f5d643f5264d8e7de55e46d37af9efb1c7ad2 build=ok filter=/*/*/ClaudeVerifiedDeliveryTests/Fake_evidence_appears_then_submit_completes_a_turn executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-0d0adf42\.antiphon\c1313-windows-resume\CP-22-20261016-140735-8025\run.trx slot=granted waited=0s dirty=0 source=d66f5d643f5264d8e7de55e46d37af9efb1c7ad2 sourceState=clean buildSource=verified
CHECKPOINT CP-23 commit=d66f5d643f5264d8e7de55e46d37af9efb1c7ad2 build=reused filter=/*/*/ClaudeVerifiedDeliveryTests/C1313_DeliveryDiscovery executed=1 passed=1 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-0d0adf42\.antiphon\c1313-windows-resume\CP-23-20261016-140950-488c\run.trx slot=granted waited=0s dirty=0 source=d66f5d643f5264d8e7de55e46d37af9efb1c7ad2 sourceState=clean buildSource=verified
```

## Evidence and retained outputs

Evidence root: `C:\Antiphon\worktrees\card-task-0d0adf42\.antiphon\c1313-windows-resume`. Generated TRX, discovery JSON/diagnostics, logs, time records, the ignored discovery driver and copied producing-build source stamps remain ignored. Fresh TRX directories are `cp19-dll`, `rerun-delete`, `rerun-case-rename`, `rerun-nul-paths`, `CP-22-20261016-140735-8025` and `CP-23-20261016-140950-488c`. All owned commands finished before reporting; no source files were edited during tests/builds.

Automatic approval review rejected the cleanup command for these four task-created output roots with `blocked by policy`; the command did not execute. These outputs remain, separate from the failed fixture temp residue above:

- `tools/Antiphon.Checkpoints/bin-c1313-windows-tool`
- `src/Antiphon.HostCleanup/bin-c1313-windows-tool`
- `tests/Antiphon.Tests/bin-c1313-windows`
- `tests/Antiphon.Agents.Pty.Tests/bin-c1313-fake`

The rejected command would have checked absolute containment, tracked paths and reparse points before `Remove-Item -LiteralPath ... -Recurse -Force`. No deletion or inventory is claimed; the tool rejected the command before launch. No attempt was made to evade that rejection.

--- next stage ---
next: code
handoff: Repair the three reproducible Windows path/fixture failures and the separate Windows checkpoint apphost launch problem in commissioned Code work, then repeat CP-19. CP-22/23 passed; whole-plan and live qualification remain outside this pass. Retained alternate outputs still need approved cleanup.
artifact: docs/investigations/2026-10-16-card-1313-windows-rows.md
