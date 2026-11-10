# Merge batch 161

Master fast-forwarded from b067f912ae01234d744c5862cc90285b5859dc1a to ac6d00022e940ee3b90584c7d313567b4491c498. First push accepted. No rc ref was pushed. No subject was excluded. No migration. No land-graph refresh. Pins were not edited.

Source: origin/feat/card-task-741fb04b-rebased tip ebd2b2bdad0a1aab831c65970700adeefa2c9aae, forked from 6707838f22ff70359c7867309d1644e97567dae1. git merge-tree --write-tree origin/master that tip exited 0 with tree b97e9f5e31695521b89c1428051175df3fa3fc27, and HEAD^{tree} is that same tree. Cherry-picks -x applied with no conflict.

## Commits

| Landed | Source | Patch-id | Subject |
|---|---|---|---|
| 21d07912a1521c0b75f6381cc524129695ed5235 | 6a81b7ff071ab422a9e75b51e65506c1737f3ea7 | 80a2c5809f31aae4e3d0b58333c6bc5c2dc99f8d | red witness for a green chunk that outlives total ran |
| c4389a1c97e3cd79c6defa0e2691c0f7855aae3d | 43d1dff4f4e6b3bd2f8f93b541a65250ef380ed5 | 461855bfe8ae169ac1ae58620b78df8150b270f9 | record a completed hang and capture the leftover tree |
| 0351944941622158da5339b68926f6e4dd17eb79 | 15f39320b4c9bcf779d59dc5f95e0b55ddb78818 | 083a50f993acae0b1fc615b0d3e75a3ff8655a77 | why the session-runner group-4 hang was a generic timeout |
| 3a81fc86f71fd5c10f060cf6aa2dc2fcd3924259 | 9a062894e26ca4d6ffa2b1b0bdb5daf965c94a01 | 6d757f9dad5dfd3439d5777b815342d7d8e2e058 | red witness for the native trx summary reader |
| ac6d00022e940ee3b90584c7d313567b4491c498 | ebd2b2bdad0a1aab831c65970700adeefa2c9aae | dbad43352933e4d12820a053fd22a49398672c0d | match the opening trx summary tag and arm the grace |

Evidence b067f912ae01234d744c5862cc90285b5859dc1a..ac6d00022e940ee3b90584c7d313567b4491c498 was commits=5 entries=0 violations=0. Paths: scripts/lib/nightly-owned-process.cs, scripts/lib/nightly-tests-impl.ps1, scripts/lib/nightly-watchdog-admission.ps1, scripts/test-nightly-tests.ps1, docs/investigations/2026-11-10-why-missed-session-runner-group4-post-completion-hang.md.

## Pins

Recomputed on the landed tree immediately before the push. Both match the stored values, so nothing was rewritten.

- policyHash stored=computed fdc043758617a5e8c21735126539c559faf9dad95abe5f1122a6d5780ee9df4d
- PinnedInputDigest stored=computed 4bc09f3840a39436be6ca4035ea0946c01f96ae1883d0a7b4e5513132dbf74ec

The protected-input list does not include the five paths this batch changed. The older pair quoted in the brief (50c33c11… / 8510514a…) is not the pair stored on b067f912.

## Checks

Antiphon.sln build through scripts/build-slot.ps1, -m:1, UseAppHost=false, TEMP=/tmp: 0 errors, 2207 warnings, 3m 44s. Lease waited 0s.

One Antiphon.Tests run, --no-build, same UseAppHost=false, 40m timeout: 142 passed, 0 failed, 0 skipped, 7m 46s. Filter was the nine readers of the changed nightly scripts (NightlyDiscoveryParserTests, RcFullWatchdogHarnessTests, NightlyExecutionDiagnosticTests, C1514LandingSplitBudgetTests, NightlyScriptsTests, ToolchainFloorWarningTests, NightlyPrerequisiteTests, RcFullTimingContractTests, TestPhaseSupervisorTests) plus ExpectedPlatformSkipRosterTests, NightlyCoreTests, LiveLaneRunnerTests, MechanicalPcContractTests, TaskPlatformGuidanceTests, C1729LayeringGuardTests, TestLaneCategoryGuardTests, ProcessSpawnLimitTests. C1729 stdout in the TRX: VIOLATIONS 0 LISTED 168 STALE 0 AMBIGUOUS 3.

Policy guard batch, own process: TestClassificationPolicyTests, TestClassificationGuardTests, SlowTestRegistryMergeTests, SlowTestTripwireTests, NativeLinuxPolicyWitnessTests: 52 passed, 0 failed, 0 skipped, 1m 51s.

A5 pair ReleaseGatePlatformTests C1686_LinuxOnlyCensus and LinuxOnlyWindowsExclusionPolicy: 2 passed, 0 failed, 0 skipped.

ContractSnapshotTests on tests/Antiphon.E2E: 5 passed, 0 failed, 0 skipped. Broker connection-refused lines were printed; the run still passed.

Script cases, each its own build-slot lease:

- Group4CompletedHang: 25 passed, 0 failed, exit 0
- Group4NativeTrxReader: 27 passed, 0 failed, exit 0
- test-expected-platform-skips.ps1 -Case Verdict: 14 passed, 0 failed, exit 0
- test-nightly-core.ps1 -Case CoreMatchesFallback: 18 passed, 0 failed, exit 0

The whole test-nightly-tests.ps1 was not run. The five known Linux cases were run singly on the landed tree and on a detached worktree at b067f912ae01234d744c5862cc90285b5859dc1a:

- G039: both 5 passed, 1 failed. Same failure: G039 entry missing expanded uid (cov=False), unmatched c487-probe and c1375-probe projects.
- G040: both 16 passed, 2 failed. Same failures: partial selection not complete; production discovery generated.
- G044: both exit 1 with Nightly native custody requires Windows, from nightly-owned-process.ps1:39, before any assertion row.
- G051: both 4 passed, 1 failed, same assertion (chunk assigned Ordinary does not require SlowCases) and the same extra-class list. The trx-diagnostic-mismatch token order differs (landed tree lists inherited before rows; master lists rows before inherited). The pairs and counts are the same.
- G144: both 1 passed, 1 failed. Same failure: production native argv writes TRX via results-directory.

impacted-classes.ps1 -MergeSubset -Range b067f912..HEAD: migrations=False, kinds=nightly-live-lane, no windowsFilter lines. The tests class row adds LiveLaneRunnerTests and NightlyCoreTests to the fixed subset. Those two ran inside the 142. import --plan of the six documented Merge rows (including the messaging row) wrote .antiphon/checkpoints.yaml, exit 0, no warning lines.

land-graph -Check exit 0, approved digest 3ED4A38CAA52947D313A1BAD14BA15203DD7F261A219BBC20704CB13E329EB46 count=75. The tool rebuilt because it was missing (11s). No -Update.

## Owed

Windows still owes the native Job Object hang capture and handle list. Linux proved the reader, the grace classification, and the source markers. The five Linux script results above stay red and are not this batch's regression. The imported land-notification rows (34 and 104 floor) and the fixed AppDbContext/receipt/timing row were not executed. No positive controls. CARD-1546 S3, CARD-1820, and the desktop trace were not in this batch.
