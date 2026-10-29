# CARD-1514 S1 native Windows verification — b9561ea9

Fixed and witnessed a Windows descendant-image defect. CP-5 executed all 26 tests, including the Windows-only PowerShell 5.1 case. CP-2 executed nine passing existing tests but missed the five proposed tests; CP-4 executed zero tests because its proposed class is absent. S1 remains incomplete. The six requested measurements did not reproduce the historical 20-second exit delay and did not establish negligible instrumentation overhead.

## Source and changes

- Starting S1 source: 440b282af70d31b9d08163ec76d9e2bd6fb0b1b1.
- RED assertion commit: dbbd3ff0acf49578eaaeca9b2b9a447c05a118be.
- Fix and tested code commit: 22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157.
- Branch: feat/card-task-b9561ea9.
- scripts/test-nightly-native.ps1 now matches the independently retained child PID to its recorded image after exit, inside the existing C1039_DescendantExit case.
- scripts/lib/nightly-owned-process.cs caches ProcessName while the retained process generation is alive. Windows cannot retrieve an uncached name after exit. Failure to read a name still leaves custody intact and metadata unknown.

The original native fixture passed its existing 17 assertions while both retained descendant image strings were empty. The new assertion failed at the RED commit with “FAIL C1039 descendant image survives process exit” (seven prior assertions passed). At the fix commit, the same native case passed 18 assertions and recorded conhost and OwnedChild with exit timestamps. All three direct script executions used the build-slot gate. The fixture came from the successfully emitted/staged OwnedChild project in the first overall build attempt; the changed owner C# was freshly loaded by the script. The later CP-2 receipt also exercises this assertion using its completed, source-bound build.

The requested author report, origin/feat/card-task-82fa7fd1:.antiphon/task-82fa7fd1.md, was absent before and after fetching that branch. The plan, actual source, and the time-breakdown document from origin/feat/card-task-2714a65f were inspected. No missing report was treated as proof of execution.

## Checkpoint results

| Row | Source | Executed | Passed | Failed | Skipped | Required count / outcome |
|---|---|---:|---:|---:|---:|---|
| CP-2 | 22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157 | 9 | 9 | 0 | 0 | 14; roster miss, RcFullNativeTimingTests absent |
| CP-4 | 440b282af70d31b9d08163ec76d9e2bd6fb0b1b1 | 0 | 0 | 0 | 0 | 2; zero-tests-executed, RcFullE2EFixtureTimingTests absent |
| CP-5 | 22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157 | 26 | 26 | 0 | 0 | 26; tool reports green, subject to the budget deviation below |

C1514T_PS51AndRerun is Passed in CP-5's TRX, duration 00:00:28.5913151, 20:44:13.2447012Z–20:44:41.8360163Z. It executed its real native probe and PowerShell 5.1 summary/rerun assertions; it did not skip. CP-2 and CP-5 test walls were 77.569 and 74.552 seconds respectively. CP-4's empty selection took 15.122 seconds.

Run 20261029-200352-2ac6 retained the original failure: its Antiphon.Tests build exceeded the 12-minute deadline during probe staging, so CP-2 and CP-5 executed zero tests there. Its separate E2E build succeeded and CP-4 selected zero tests. Overall exit 5, wall 19m52s. No test failure was inferred from the build timeout.

Run 20261029-202746-3f6e rebuilt once at the fix SHA, then shared that build between CP-2 and CP-5. Build source and tracked source were verified clean. Overall exit 3 from CP-2's roster miss, wall 16m43s. The build phase was 785.052 seconds; MSBuild reported 11m25.81s and zero errors. CP-2/CP-5 each ran once against this completed build. All checkpoint build/row leases were granted, with zero reported queue wait.

### Execution deviations retained

1. The tool imported all 19 plan rows. Direct run --plan with --rows CP-2,CP-4,CP-5 nevertheless preflighted all eight build definitions. It expanded 461 output paths into approximately 36,574 argument characters. Windows CreateProcess returned error 206, “The filename or extension is too long,” surfaced as git-unavailable. A diagnostic invoking the same runner confirmed the native error. Execution used the imported manifest reduced to the requested rows and their build definitions; filters, expected classes and minimum counts were preserved. No wider test selection ran.
2. The initial bootstrap invocation had a PowerShell quoting error in -nodeReuse:false. Quoting that token fixed the invocation; the gated bootstrap then built with zero errors and five warnings in 22.39 seconds. The checkpoint tool source did not change during this task, so that bootstrap was reused.
3. The first run used a uniform 12-minute override. After its build timeout, the second attempt used a disclosed 15-minute override, 25-minute total execution cap and 35-minute hard wall. This was the importer's default, **not** the plan's explicit performance contract. The plan calls for a 12-minute build allowance and separate CP-2=6 / CP-5=8 minute row bounds, plus a ten-minute slot wait; the imported slot-wait default of 45 minutes was retained. Actual test rows were below their planned bounds, but the build phase exceeded twelve minutes. The retry lacked the required prior measured budget amendment. Preserve this as a task execution deviation, not a compliant Full/acceptance gate. Do not erase the initial timeout or relabel the overall run green.

The requested additional native observation and six chunk measurements were separately gated diagnostic runs. No whole lane, whole assembly, unrelated checkpoint, or release qualification ran.

## Six chunk measurements

Exact filter: /*/Antiphon.Tests.Scripts/NightlyNativeOwnershipTests/C1039_DescendantExit.

One unchanged Antiphon.Tests binary, SHA-256 E064E102674AE0A2B37CFB9201186D0DE641A9F00546145391706B27A32146C9, was used for all six launches. Each executed one passing test; no failures or skips. Order was off1, on1, on2, off2, off3, on3, serial under one host build-slot lease (78 seconds held).

The off arm loads the original owner implementation from 7e9db99dd3748356b5bc5b47b7e3e7f3ce0aa2d3 and omits the new per-chunk persistence. The on arm uses the fixed S1 owner plus Add-NightlyOwnerObservation / Complete-TestPhaseLaunch. The existing per-test recorder is enabled identically in both arms. The test body, including the inner native harness, is identical. Module loading and reader warmup occur before the chunk clock in both arms. Thus this compares S1 owner capture and per-chunk persistence, not all pre-existing timing instrumentation or the unintegrated A4 sampler.

| Pair | Off wall, seconds | On wall, seconds | On persistence, seconds |
|---|---:|---:|---:|
| 1 | 7.002615 | 10.390047 | 0.292758 |
| 2 | 8.754943 | 7.192724 | 0.462528 |
| 3 | 6.561416 | 7.769861 | 0.340216 |

Median wall: off 7.002615s, on 7.769861s; difference +0.767246s / +10.96%. These three samples are noisy and do not establish that all of the wall difference is causal. The directly timed persistence phase is nevertheless measurable: median 0.340216s, versus a near-empty control section. **“No measurable wait” is not confirmed; no <=2% overhead acceptance is claimed.** The caller requested three samples per arm, overriding the plan's five-pair count. CPU/allocation/IO attribution and controlled host-load strata were not collected, so those remain unknown.

Host: DESKTOP-KTLKPIF, Windows 10.0.19045, win-x64, eight logical processors. Measurement driver: PowerShell 7.6.9 on .NET 10.0.14. Test diagnostics identify .NET 9.0.22. TESTINGPLATFORM_TELEMETRY_OPTOUT was absent; no telemetry policy was changed. The shared desktop's background load was uncontrolled.

### Exit owner evidence

| On run | Root dotnet PID | MTP session-end to root exit | MTP session-end to last descendant exit |
|---|---:|---:|---:|
| on1 | 277276 | 0.152604s | 0.158794s |
| on2 | 75584 | 0.171173s | 0.175481s |
| on3 | 295372 | 0.170603s | 0.188388s |

For on1, MTP ended at 20:46:39.4201249Z; the root exited at 20:46:39.5727290Z and its last conhost descendant at 20:46:39.5789191Z. The pwsh descendant had already exited at 20:46:39.1605395Z. A1 persisted actual pwsh, conhost and OwnedChild images and exit stamps, with jobEmpty=true. The supplemental measurement receipt records the known root PID/image; A1's owner JSON itself does not contain those root identity fields.

**The historical approximately 20-second linger did not reproduce. Its owner and telemetry cause remain unconfirmed.** These observations must not be used to attribute the old run's 114.2 minutes or justify an S2 telemetry/teardown policy change.

## Containment and sensitive-data inspection

All 18 timing files from the six runs were under each run's explicitly supplied timing-custom root; no fallback timing directory appeared, no reparse point was found, and no inspected timing path escaped its root. On arms each persisted the owner observation and executed manifest alongside recorder files.

Inspection found zero credential-key/private-key markers and zero matches to nonempty sensitive environment values of at least eight characters. Values were compared in memory and were not printed or persisted by the audit. Owner metadata contains image basenames, PIDs and timestamps, not command lines or environment blocks. This is an observation about the inspected files, not proof that an arbitrary unknown secret can never appear.

## Remaining gate and integration work

- Implement the five RcFullNativeTimingTests methods and two RcFullE2EFixtureTimingTests methods required by CP-2/CP-4. Their absence predates this task's two-file fix.
- A4 remains unwired: qualification-host-stats.ps1 defines the provider/sampler, but scripts contains no production call to New-QualificationHostStatsProvider or Invoke-QualificationHostSampler. The native host-sampling obligation is not discharged.
- Investigate the measured persistence overhead before claiming the plan's <=2% target. Keep the historical linger cause open until it reproduces with owner evidence.
- Record a measured build-budget amendment and run the required selections under the plan's separate budgets. Current native results remain useful evidence, with the deviation above.
- OS-witness declaration lint returned four obligations: productionType and factory are absent from each declared C# wrapper test file. The actual wrapper -> script case -> Start-NightlyNativeOwner -> NativeProcessOwner chain executed natively, but the bounded declaration checker cannot certify that wrapper layout. Preserve its non-green result; the native red/green and CP-2 receipts do not turn declaration lint green. No shared-host restart was performed or claimed.
- Evidence-diff over the two code/test commits found zero violations. Against fetched master 0e7dd390520a43ab330396a72e9085a8691ac332, merge-tree returned exit 1 with six identical conflicts at both starting S1 source and the fixed code SHA. The fix introduces no additional conflict path. This branch is not landable to master as-is. No merge, rebase, tag, publication, activation, AppHost or runner change was performed.

All checkpoint executors and foreground children were joined. The four owned alternate output directories were inspected for containment/reparse points and removed: the checkpoint-tool and HostCleanup bin-b9561ea9-tool directories, Antiphon.Tests/bin-rcspeed-a1 and Antiphon.E2E/bin-rcspeed-e2e. Tool shadow copies were removed by checkpoint wait after executor death. Generated receipts remain ignored.

## Artifacts and reproduction

Evidence root: C:\Antiphon\worktrees\card-task-b9561ea9\.antiphon\b9561ea9\

- native-original.log, native-red.log, native-green.log and their per-case result.json files.
- imported.yaml, selected.yaml, owner-recorder.yaml, preflight-refusal.json.
- run-measurements.ps1, measure-chunk.ps1, baseline-owner.cs; off1/on1/on2/off2/off3/on3 contain TRX, MTP diagnostics, timing files and measurement receipts.
- measurement-summary.json and timing-file-digests.json contain the audit and immutable file digests.
- os-witness.json / os-witness.log retain all four declaration obligations.
- merge-base.log, merge-head.log and merge-summary.json retain the inherited-conflict comparison.

Checkpoint reports:

- C:\Antiphon\worktrees\card-task-b9561ea9\.antiphon\checkpoints\20261029-200352-2ac6\report.md
- C:\Antiphon\worktrees\card-task-b9561ea9\.antiphon\checkpoints\20261029-202746-3f6e\report.md

Rebuild the isolated checkpoint tool through scripts/build-slot.ps1 before reproducing; its outputs were removed. Import the committed plan, retain only CP-2/CP-4/CP-5 and their build definitions to avoid Windows error 206, and preserve exact filters/counts. Correct the budget manifest before any new acceptance run. The retained measurement scripts run through scripts/build-slot.ps1 with -AssemblyPath pointing to the newly verified Antiphon.Tests.dll; they refuse any nonpassing/non-single-test chunk. Direct native regression command: scripts/test-nightly-native.ps1 -Case C1039_DescendantExit -ResultsDirectory <fresh-owned-root> -FixtureExecutable <verified-output>/nightly-owned-child/OwnedChild.exe, through the same gate.

## Unedited checkpoint receipt lines

```text
CHECKPOINT CP-2 commit=440b282af70d31b9d08163ec76d9e2bd6fb0b1b1 build=failed filter=/*/Antiphon.Tests.Scripts/(RcFullNativeTimingTests*)|(NightlyNativeOwnershipTests*)/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-2 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
CHECKPOINT CP-5 commit=440b282af70d31b9d08163ec76d9e2bd6fb0b1b1 build=failed filter=/*/Antiphon.Tests.TestHelpers/(TestTimingRecorderTests*)|(TestPhaseSummaryTests*)/* executed=0 passed=0 failed=0 skipped=0 trx=n/a slot=granted waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT-TIMING CP-5 waited=0s executed=0.000s wall=0.000s reason=build-deadline-exceeded
CHECKPOINT CP-4 commit=440b282af70d31b9d08163ec76d9e2bd6fb0b1b1 build=ok filter=/*/Antiphon.E2E/RcFullE2EFixtureTimingTests/* executed=0 passed=0 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-b9561ea9\.antiphon\checkpoints\20261029-200352-2ac6\rows\CP-4\run.trx slot=granted waited=0s dirty=0 source=440b282af70d31b9d08163ec76d9e2bd6fb0b1b1 sourceState=clean buildSource=verified reason=zero-tests-executed
CHECKPOINT-TIMING CP-4 waited=0s executed=15.122s wall=16.362s

CHECKPOINT CP-2 commit=22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157 build=ok filter=/*/Antiphon.Tests.Scripts/(RcFullNativeTimingTests*)|(NightlyNativeOwnershipTests*)/* executed=9 passed=9 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-b9561ea9\.antiphon\checkpoints\20261029-202746-3f6e\rows\CP-2\run.trx slot=granted waited=0s dirty=0 source=22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-2 waited=0s executed=77.569s wall=79.020s
CHECKPOINT CP-5 commit=22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157 build=reused filter=/*/Antiphon.Tests.TestHelpers/(TestTimingRecorderTests*)|(TestPhaseSummaryTests*)/* executed=26 passed=26 failed=0 skipped=0 trx=C:\Antiphon\worktrees\card-task-b9561ea9\.antiphon\checkpoints\20261029-202746-3f6e\rows\CP-5\run.trx slot=granted waited=0s dirty=0 source=22ced2b4ba2f9ca07a5fceae6fb0c97c1a216157 sourceState=clean buildSource=verified
CHECKPOINT-TIMING CP-5 waited=0s executed=74.552s wall=75.508s
```
