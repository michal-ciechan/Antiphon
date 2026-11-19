# CARD-2005 summary writer OOM, checkpoints

Code task `dc5cf8e8`, base master `95d43ee27da2272a34eca5ae51d6e5b98743df2a`. Branch `feat/card-task-dc5cf8e8`. Worktree `/work/worktrees/ws-03-d2`.

The Full-run `summary.json` writer builds one `ConvertTo-Json -Depth 8` string and walks every chunk row twice (`suites` and `dispatchProgress.suites` share the census exclusion objects). On the 1,274-chunk run that string throws `Insufficient memory to continue the execution of the program.` A missing summary forces `coverageComplete=false` and the report refuses, so the run exits 1.

## Design

1. Stream the summary. `scripts/lib/nightly-summary-json.ps1` writes UTF-8 with a 64 KB buffer. It is C# 5 via `Add-Type` and the default reference set, so the same file compiles on Windows PowerShell 5.1 and pwsh 7. `System.Text.Json` is not in .NET Framework, Newtonsoft is not loaded, and `JavaScriptSerializer` still builds one string. An explicit `-ReferencedAssemblies` list hides `System.Text`, so the writer does not pass one.
2. Write full chunk rows once, under `suites`. `dispatchProgress.suites` keeps only `id`, `chunk`, `timedOut`, `result`, `exitCode`, `skipped`, and `coverageComplete` when the source row has them, and only rows with a chunk. Root key order is unchanged, so `SummaryPrefix.CutBeforeBulk` still cuts at `timingChunks`.
3. The nonzero-exit rewrite in `nightly-run-impl.ps1` uses the same writer at depth 8. `Write-NightlyAtomicJson` and `ConvertTo-NightlyCanonicalJson` stay as they are. `timing/summary.json`, dispatch-schedule.json, active-chunk.json, and the prior-failures export stay on `ConvertTo-Json`: they are not the 850 MB run summary.

No positive controls. Mutation checks are waived (operator 2026-11-12). Windows PowerShell 5.1 execution is unobserved on this Linux runner. The production hop is pwsh (`nightly-run-impl.ps1`).

## Verification design

- V-1. The run summary and the nonzero-exit rewrite stream. `dispatchProgress.suites` is the thin projection (`C2005_SummaryWriterStreamsWithinABoundedHeap`, structural markers).
- V-2. Under `DOTNET_GCHeapHardLimit=4000000` (64 MB), `ConvertTo-Json -Depth 8` of 40 chunk rows with a shared 200,000-character exclusion reason throws `OutOfMemoryException` (`OLD-OOM`). The streaming writer finishes (`NEW-BOUNDED`) and the file stays between 10 MB and 25 MB.
- V-3. The streamed file parses. Each exclusion uid appears once. Progress rows omit `exclusions`, `evidenceReasons`, `trx`, and `detail`. The prefix cut is `"timingChunks":` and still contains `runId`, `sha`, `timingRoot`, `startedAt`, and `completedAt`. `nightly-report.ps1 -PassThru -DryRun` accepts the green scheduled `origin/master` summary with an empty card census (`REPORT-ACCEPTED`).
- R-1. The existing `Nightly*` test classes pass.
- R-2. `TestClassificationPolicyTests` runs once.
- R-3. `DerivedImpactIndexTests` runs alone, after the index regeneration commit.
- R-4. `scripts/audit-land-verification-graph.ps1 -Check` approves.
- R-5. `scripts/check-evidence-diff.ps1` over base..HEAD, and `git merge-tree --write-tree origin/master HEAD` after `git fetch origin master`, are clean.
- R-6. The protected-input digest and the policy hash are recomputed. No protected input changes, so the pin is not resealed.

No PC-n is named. Mutation execution is waived and is not discharged.

The guard union beyond R-2 and R-3 is deferred to rc under the short profile: `TestClassificationGuardTests`, `SlowTestRegistryMergeTests`, `SlowTestTripwireTests`, `NativeLinuxPolicyWitnessTests`, `MechanicalPcContractTests`, `TaskPlatformGuidanceTests`, `C1729LayeringGuardTests`, `TestLaneCategoryGuardTests`, and `WindowsUnitPlatformAdmissionTests`. C1729's violation count is unobserved here.

Red confirmation is the unlisted run of `scripts/test-nightly-summary-writer.ps1` with `scripts/lib/nightly-summary-json.ps1` absent. It exited 1 and printed:

```
STRUCTURAL-RED tests-impl-convertto-json,run-impl-atomic-json,tests-impl-not-streaming,run-impl-not-streaming,tests-impl-not-dotted,tests-impl-not-thin,tests-impl-duplicate-rows,writer-missing
OLD-OOM System.OutOfMemoryException
NEW-MISSING
```

It is not a checkpoint row. This table runs on the fix, where that witness passes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial | Environment |
|---|---|---|---|---|---|---|---|---|---:|---|---|
| CP-1 | green | tests/Antiphon.Tests -> bin-c2005-summary/ | c2005-witness | `/*/*/NightlySummaryWriterTests/*` | V-1, V-2, V-3 | 1 case, 0 failed | 1 | 15 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-2 | green | CP-1 (-NoBuild) | nightly-fast | `/*/*/(NightlyDiscoveryParserTests*)\|(NightlyReclaimRaceTests*)\|(NightlyProbeSupervisorTests*)\|(NightlyCoreTests*)\|(NightlyPrerequisiteTests*)\|(NightlyDeadOwnerLockTests*)/*` | R-1 | 16 cases, 0 failed | 16 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-3 | green | CP-1 (-NoBuild) | nightly-native | `/*/*/(NightlyExecutionDiagnosticTests*)\|(NightlyNativeOwnershipTests*)/*` | R-1 | 7 linux / 16 windows, 0 failed | 7 linux / 16 windows | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-4 | green | CP-1 (-NoBuild) | nightly-watchdog | `/*/*/NightlyWatchdogCoreTests/*` | R-1 | 14 cases, 0 failed | 14 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-5 | green | CP-1 (-NoBuild) | nightly-c544 | `/*/*/NightlyVerificationContractTests/(C544_*)` | R-1 | 23 cases, 0 failed | 23 | 9 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-6 | green | CP-1 (-NoBuild) | nightly-c545 | `/*/*/NightlyVerificationContractTests/(C545_*)` | R-1 | 15 cases, 0 failed | 15 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-7 | green | CP-1 (-NoBuild) | nightly-scripts | `/*/*/NightlyScriptsTests/*` | R-1 | 7 cases, 0 failed | 7 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-8 | green | CP-1 (-NoBuild) | classification | `/*/*/TestClassificationPolicyTests/*` | R-2 | 28 cases, 0 failed | 28 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-9 | green | CP-1 (-NoBuild) | derived-alone | `/*/*/DerivedImpactIndexTests/*` | R-3 | 27 cases, 0 failed | 27 | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-10 | green | n/a | land-graph | `export ANTIPHON_LINUX_COMPILER_POLICY=0 && pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-4 | exit 0, approved | n/a | 8 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-11 | green | n/a | evidence-diff | `export ANTIPHON_LINUX_COMPILER_POLICY=0 && pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 95d43ee27da2272a34eca5ae51d6e5b98743df2a -HeadRef HEAD` | R-5 | exit 0 | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-12 | green | n/a | merge-tree | `export ANTIPHON_LINUX_COMPILER_POLICY=0 && git fetch origin master && git merge-tree --write-tree origin/master HEAD` | R-5 | exit 0 and a tree id | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |
| CP-13 | green | n/a | pins | `export ANTIPHON_LINUX_COMPILER_POLICY=0 && pwsh -NoProfile -File scripts/test-live-lane-run.ps1 -PrintInputDigest && pwsh -NoProfile -Command '. ./scripts/lib/nightly-common.ps1; . ./scripts/lib/nightly-policy.ps1; $r = Read-NightlyExecutionPolicy -RepoRoot (Get-Location); Write-Output ("POLICY-HASH " + $r.Hash)'` | R-6 | exit 0, digest and policy hash printed | n/a | 3 | true | ANTIPHON_LINUX_COMPILER_POLICY=0 |

### Cost

Ordinary floor is 15 + 8 + 8 + 8 + 9 + 8 + 8 + 8 + 8 + 8 + 3 + 3 + 3 = 97 minutes of budget. CP-1 is the cold `Antiphon.Tests` build plus the witness. Later test rows reuse it. No repeat proof after green. CP-3 counts 16 methods. All nine NightlyNativeOwnershipTests methods skip on Linux (Windows job and OwnedChild.exe), so the executed floor is 7 on Linux and 16 on Windows.
