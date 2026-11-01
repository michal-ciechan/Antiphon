# Test timing stats: Windows rc run d47848cc (2026-11-01)

Operator question: "Do we have the stats on how long tests take to run somewhere?"

**Answer: yes.** Every rc and nightly qualification since CARD-1514 keeps per-chunk timing sidecars, a planned
per-chunk cost model, and TRX files with per-test durations. Nothing aggregates them across runs. This report
builds the aggregate for the last finished Windows Full run, **d47848cc** (candidate
`rc-2026-10-27-cde8def41638`, source `cde8def4163829aa5b867df6ec625433be2522cb`). The run went from
2026-10-30 08:24:42Z to 2026-10-31 10:24:14Z: 93,571 s of wall time (26.0 h), 843 chunk rows (841 TRX-backed
plus client and scripts), 22,780 TRX results.

Method: read-only. A Python streaming pass covered all 841 `.trx` files (`xml.etree.iterparse`), the 843
`*.timing.json` sidecars, the `groups-planned-<suite>.json` files, and the `suites[]` block of `summary.json`.
That 235 MB file was line-streamed, never loaded whole. Nothing from the run was copied; only aggregates appear
here. The scratch scripts were `agg.py`, `an.py`, `budget.py` and `gen.py` in the session scratchpad; they are
not committed.

## 1. Where the timing data lives

| Source | Path | What it holds | How to query it |
|---|---|---|---|
| Per-chunk sidecar (CARD-1514) | `<LogRoot>/<suite>-<chunk>.timing.json`, copied to `<LogRoot>/../timing-<RunId>/` | startedAt/completedAt, runWallSeconds, buildSeconds (the shared pre-run build, the same 525.098 s on every row), testCount, p50/p95/max, the slowest 20 tests, `why` | `jq -r '[.suite,.chunk,.runWallSeconds,.testCount,.p95Seconds]\|@tsv' <LogRoot>/*.timing.json \| sort -t$'\t' -k3 -rn \| head` |
| Run summary | `<LogRoot>/summary.json` (235 MB here) | `timingChunks[]` (the same rows as the sidecars), `suiteTimeline[]` (per-suite start and end), `suites[]` (per-chunk result, exitCode, timedOut), `reasons[]`, `builds[]` | Stream it. `sed -n '/^  "suiteTimeline"/,/^  \],/p' summary.json` gives the per-suite span. `awk '/^  "suites": \[/{f=1} f&&/^      "(id\|chunk\|result)":/' summary.json \| paste - - -` gives per-chunk results |
| Setup phases | `<TimingRoot>/setup-phases.json` | npm-ci, npm-build, lint, dotnet-build, and per-suite planning, with start, end and seconds | `jq -r '.phases[]\|[.name,.seconds]\|@tsv' setup-phases.json` |
| Planned cost model (packer output) | `<TimingRoot>/groups-planned-<suite>.json`; `groups-planned-index.json` lists the suites and their digests | per chunk: classes, plannedTestIds, `plannedSeconds`, `pricingBasis` (measured, observed-bound, conservative-bound, pace, model, mixed) | `jq -r '.groups[]\|[.chunk,.plannedSeconds,.pricingBasis,(.classes\|join(","))]\|@tsv' groups-planned-e2e.json` |
| Per-test phase timing | `<TimingRoot>/tests.csv` (23,161 rows), `summary.json` (12 MB), `summary.md` | per-test duration and groupId; phase spans (db, host, process, fs, network, fixture) only where JSONL shards joined. In d47848cc `complete=false` (torn tail, `missing-phase-unknown`) and `className` is blank | `Import-Csv tests.csv \| Sort-Object {[timespan]$_.duration} -Desc \| Select -First 20` |
| TRX (authoritative) | `<LogRoot>/<suite>-<chunk>.trx` | per test: `testName`, `duration`, `outcome`, `startTime`; `UnitTest/TestMethod@className` | `Select-Xml -Path *.trx -XPath '//t:UnitTestResult' -Namespace @{t='http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}` and sort on `duration` |
| Class cost table (the model input) | `scripts/lib/nightly-class-costs.json` (in the repo) | 771 class rows with `nodes`, `seconds`, `basis` and `source` (711 measured, 29 conservative-bound, 26 observed-bound, 5 pace) | `jq -r '.classes\|to_entries\|sort_by(-.value.seconds)[:20][]\|[.key,.value.seconds,.value.basis]\|@tsv' scripts/lib/nightly-class-costs.json` |
| Earlier rc runs | `C:\Antiphon\releases\candidates\<candidate>\logs\<run>\` | 21 candidates since 2026-10-18. Timing roots exist only for 79b742de, 28bbb58e and d47848cc; older runs have TRX only | the same queries per run dir |
| Checkpoint receipts | `<worktree>/.antiphon/checkpoints/<runId>/report.json` | `wallSeconds`, `sequentialEquivalentSeconds`, `builds[].seconds` and `waitedSeconds`, `rows[].seconds`, `slowClasses`, `timings` | `jq -c '{wallSeconds,sequentialEquivalentSeconds,rows:[.rows[]\|{id,seconds}]}' .antiphon/checkpoints/*/report.json`. These are per worktree, so they disappear with the worktree. The main checkout holds 4 old ones |
| Build-slot broker | `GET http://localhost:17204/build-slots/metrics` | acquire wait and held time (mean, p95, max) over the last 3 h | `Invoke-RestMethod http://localhost:17204/build-slots/metrics` |
| Local nightly | `C:\Antiphon\nightly\last-run.json`, `C:\Antiphon\nightly\logs\` | stale: last run 2026-09-04 (468 s, failed); 2 log dirs | `Get-Content C:\Antiphon\nightly\last-run.json` |
| Windmill nightly `u/lndcobra/antiphon_nightly_tests` | Windmill on server2 | job results | **Not queried.** Reading it needs a temporary DB token over server2 SSH, which is outside this read-only desktop brief |
| Failure history and stopped-run seed | `scripts/lib/nightly-failure-history.ps1`, `nightly-stopped-run-seed.ps1` | prior-run failures used to order the next run ("a scheduling hint, never a receipt"); the seed carries `chunks[]` and `failedTests[]` | they read prior `summary.json` files; there is no separate timing store |

**Gap:** there is no cross-run timing history file. Each run's timing root stands alone. The only durable
model is `nightly-class-costs.json`, which is refreshed by hand from TRX (its `source` field records the
provenance of each refresh).

## 2. Run-level stats

- Wall time 93,571.4 s. The chunks account for 92,648.5 s (**99.0 %**). The setup before the first chunk takes
  833.5 s (0.9 %), and the 842 gaps between chunks add up to 89.3 s (0.1 %). The median gap inside antiphon is
  0.04 s; the largest are 24.8 s before session-runner, 4.9 s before agents-pty and 4.5 s before e2e. No two
  chunks overlap (`nativeActiveMax=1`), so **the critical path is the whole run**: under the 1-parallel Windows
  target, wall time is the serial sum.
- Setup, from `setup-phases.json`: npm-ci 29.2 s, npm-build 23.9 s, lint 39.4 s, dotnet-build 432.4 s,
  planning-antiphon 27.4 s. That is 552 s; the other ~281 s of the 833.5 s is preflight and Playwright
  discovery. Planning for the other suites took 1.2 to 2.2 s each, inside the suite gaps. Every sidecar's
  `buildSeconds=525.098` is this shared build (npm + lint + dotnet), not a per-chunk build.
- Summed TRX test durations are 110,029 s against 92,648 s of chunk wall, a factor of 1.19: TUnit runs some
  tests in parallel inside a chunk.

| Lane | Chunks | Chunk wall s | Share of run wall | TRX tests | TRX test-sum s | Planned s | Span (UTC) |
|---|---|---|---|---|---|---|---|
| antiphon | 638 | 72,197 | **77.2 %** | 19,821 | 96,714 | 115,621 | 10-30 08:38 → 10-31 04:42 |
| e2e | 75 | 14,472 | **15.5 %** | 171 | 10,823 | 61,660 | 10-31 06:16 → 10:17 |
| session-runner | 53 | 3,280 | 3.5 % | 1,725 | 2,026 | 1,114 | 04:42 → 05:37 |
| pty-host | 53 | 1,317 | 1.4 % | 127 | 33 | 73 | 05:37 → 05:59 |
| agents-pty | 9 | 394 | 0.4 % | 658 | 286 | 352 | 05:59 → 06:06 |
| client | 1 | 391 | 0.4 % | (Vitest) | – | – | 06:07 → 06:14 |
| markdown-pdf | 11 | 318 | 0.3 % | 16 | 5 | 8 | 10:17 → 10:23 |
| scripts | 1 | 117 | 0.1 % | – | – | – | 06:14 → 06:16 |
| messaging | 1 | 93 | 0.1 % | 250 | 96 | – | 06:06 → 06:07 |
| observability | 1 | 70 | 0.1 % | 12 | 45 | – | 10:23 → 10:24 |

Fixed cost per chunk: on chunks that ran a single test, wall minus that test's time has a median of
agents-pty 27.8 s (n=6), antiphon 20.0 s (n=428), e2e 26.0 s (n=35), markdown-pdf 28.1 s (n=10), pty-host 24.2 s (n=49), session-runner 27.0 s (n=44). This per-launch cost is about 20 s on Windows. It is why pty-host (127 tests, 33 s of test time)
takes 1,317 s across 53 chunks.

Sidecar caveat: 45 antiphon sidecars carry `why=timing-read-refused` (the 200 ms post-exit read budget) with
`testCount=0` and null p50/p95. Their TRX files are intact, and every per-test figure in this report comes from
the TRX.

## 3. The slowest

### Top 40 chunks by measured wall (planned = the packer's `plannedSeconds`)

`tests`, p50, p95 and max come from the sidecar; `None` means the sidecar read was refused (see §2).

| # | suite/chunk | wall s | runWall s | tests | p50 | p95 | max | planned s | meas/plan | result | classes |
|---|---|---|---|---|---|---|---|---|---|---|---|
| 1 | antiphon/all-group-11 | 1516 | 1516 | 0 | None | None | None | 1662 | 0.91 | pass | PostLandMutationDeliveryTests |
| 2 | antiphon/all-group-107 | 1457 | 1457 | 0 | None | None | None | 1593 | 0.91 | pass | WorktreeGuardedCleanupTests |
| 3 | antiphon/all-group-18 | 1404 | 1404 | 0 | None | None | None | 1225 | 1.15 | pass | PostLandMutationCustodyTests |
| 4 | antiphon/all-group-9 | 1327 | 1327 | 0 | None | None | None | 1920 | 0.69 | pass | AgentTaskLandActiveSourceClaimTests |
| 5 | antiphon/all-group-17 | 1216 | 1216 | 17 | 34.292 | 159.881 | 159.881 | 1277 | 0.95 | pass | ChannelOutboundUnifiedTransportTests |
| 6 | antiphon/all-group-20 | 1192 | 1192 | 63 | 15.59 | 44.302 | 66.801 | 1068 | 1.12 | pass | BlockedTaskParkReplyAdmissionTests |
| 7 | antiphon/all-group-121 | 1117 | 1117 | 24 | 34.397 | 92.448 | 170.778 | 899.5 | 1.24 | pass | NativeLinuxPolicyMatcherParityTests,NightlyDiscoveryParserTests+3 |
| 8 | antiphon/all-group-120 | 1072 | 1072 | 30 | 1.214 | 202.844 | 283.243 | 900 | 1.19 | FAIL | ChangeAwareReleaseTests,ReleaseGatePublicationTests+2 |
| 9 | antiphon/all-group-15 | 1063 | 1062 | 0 | None | None | None | 1398 | 0.76 | pass | PostLandMutationCleanupTests |
| 10 | antiphon/all-group-103 | 1044 | 1042 | 302 | 2.566 | 17.452 | 72.686 | 899.6 | 1.16 | pass | BaselineComparerTests,CheckpointExecutorLogTests+24 |
| 11 | antiphon/all-group-43 | 1002 | 1002 | 15 | 21.544 | 277.394 | 277.394 | 900 | 1.11 | pass | CodexCliObservationTests,DelegateBranchDeliveryTests |
| 12 | antiphon/all-group-117 | 999 | 999 | 26 | 24.838 | 90.06 | 133.448 | 1420 | 0.70 | pass | RunCheckpointScriptTests |
| 13 | antiphon/all-group-33 | 972 | 972 | 0 | None | None | None | 900 | 1.08 | pass | AgentCreateRaceTests,AgentTaskLandNotificationPersistenceTests+1 |
| 14 | antiphon/all-group-58 | 958 | 957 | 65 | 12.101 | 29.591 | 47.692 | 900 | 1.06 | pass | AttentionKindWireTests,CompletionWarningDeliveryTests+3 |
| 15 | antiphon/all-group-37 | 948 | 948 | 26 | 2.992 | 130.326 | 353.842 | 900 | 1.05 | pass | AgentTaskWorktreeContinuityTests,BlockedTaskParkDeliveryTests+1 |
| 16 | antiphon/all-group-45 | 945 | 945 | 0 | None | None | None | 899.5 | 1.05 | pass | AgentPresetsTests,AgentTaskLandIndexLockTests+1 |
| 17 | antiphon/all-group-22 | 933 | 933 | 38 | 3.981 | 174.554 | 224.337 | 978 | 0.95 | pass | BlockedTaskParkReclaimTests |
| 18 | antiphon/all-group-26 | 931 | 930 | 84 | 0.829 | 61.296 | 96.942 | 899.5 | 1.03 | pass | AlarmSettingsValidatorTests,ChannelOutboundUnifiedCrashTests+1 |
| 19 | antiphon/all-group-46 | 926 | 924 | 0 | None | None | None | 900 | 1.03 | pass | CapacityRecoveryPersistenceTests,CheckpointSourceApprovalTests+1 |
| 20 | antiphon/all-group-109 | 919 | 919 | 0 | None | None | None | 899.7 | 1.02 | pass | FileSystemEvidenceRetentionTests,GrokRunnerImageContractTests+2 |
| 21 | antiphon/all-profile-1269 | 881 | 881 | 1 | 859.105 | 859.105 | 859.105 | 22 | 40.06 | pass | CheckpointReceiptSafetyTests |
| 22 | antiphon/all-group-118 | 831 | 830 | 37 | 0.888 | 104.64 | 132.846 | 900 | 0.92 | pass | DockerTestCommandTests,EvidenceDeletionGuardTests+1 |
| 23 | antiphon/all-group-27 | 807 | 807 | 16 | 4.837 | 468.409 | 468.409 | 900 | 0.90 | pass | AgentContextContractTests,BlockedTaskParkReleaseTests+1 |
| 24 | antiphon/all-group-123 | 802 | 801 | 290 | 0.914 | 8.395 | 132.496 | 573.9 | 1.40 | pass | AppHostWatchdogStateObserverScriptTests,CacheFixtureCompletionTests+28 |
| 25 | antiphon/all-group-32 | 790 | 790 | 0 | None | None | None | 900 | 0.88 | pass | AgentTaskDeliveryWatchdogTests,AgentTaskLandRecoveryTests |
| 26 | antiphon/all-group-122 | 754 | 754 | 132 | 2.009 | 29.476 | 150.778 | 899.7 | 0.84 | pass | C1514LandingSplitBudgetTests,CardListSearchScriptTests+12 |
| 27 | antiphon/all-group-24 | 740 | 740 | 0 | None | None | None | 948 | 0.78 | pass | AgentTaskWorktreeLockOutcomeTests |
| 28 | antiphon/all-group-119 | 739 | 739 | 40 | 11.472 | 42.062 | 44.164 | 899.6 | 0.82 | pass | EvidenceDiffGuardTests,NativeLinuxPolicyExecutionTests+2 |
| 29 | antiphon/all-group-110 | 729 | 728 | 0 | None | None | None | 900 | 0.81 | FAIL | DockerStackContractTests,ExpectationWatchdogJobTests+2 |
| 30 | antiphon/all-profile-1805 | 698 | 698 | 4 | 169.893 | 175.806 | 175.806 | 257.8 | 2.71 | pass | RollingVolumeRecycleScriptTests |
| 31 | antiphon/all-group-40 | 696 | 695 | 384 | 0.996 | 10.718 | 21.523 | 900 | 0.77 | pass | AgentTaskReplyIntegrationTests,AlertRoutingTests+1 |
| 32 | antiphon/all-group-31 | 649 | 648 | 64 | 3.836 | 27.578 | 54.137 | 899.5 | 0.72 | pass | CardWorkflowRunFactoryTests,ChannelOutboundRecoveryTests+1 |
| 33 | antiphon/all-group-36 | 631 | 631 | 80 | 1.377 | 29.803 | 111.79 | 899.8 | 0.70 | pass | AgentTaskLandSourceFreshnessTests,VerificationRoundDeliveryTests |
| 34 | antiphon/all-split-12 | 612 | 612 | 0 | None | None | None | 894.9 | 0.68 | pass | LandNonCodeSkipTests |
| 35 | antiphon/all-group-102 | 607 | 607 | 19 | 8.502 | 355.572 | 355.572 | 900 | 0.67 | pass | C1307CliCancellationTests,CheckpointRepeatBuildIsolationTests+3 |
| 36 | antiphon/all-group-42 | 598 | 598 | 0 | None | None | None | 900 | 0.66 | pass | AgentTaskLandDispatchYieldTests,BlockedTaskParkResumeTests+1 |
| 37 | antiphon/all-group-47 | 585 | 585 | 0 | None | None | None | 900 | 0.65 | pass | AgentTaskLandSourcePersistenceTests,AgentTaskOverdueDeadlineTests+1 |
| 38 | antiphon/all-group-41 | 579 | 578 | 0 | None | None | None | 900 | 0.64 | pass | AgentTaskLandAdmissionControlledTests,CardFilePrivacyBoundaryAcceptanceTests+1 |
| 39 | antiphon/all-split-15 | 577 | 577 | 0 | None | None | None | 894.9 | 0.65 | pass | LandNonCodeSkipTests |
| 40 | e2e/all-split-9 | 560 | 560 | 2 | 219.208 | 319.769 | 319.769 | 1200 | 0.47 | pass | AgentTaskLandDeliveryE2ETests |

**Planned against measured, slowest 20 chunks:** 21941 s measured vs 22439 s planned (ratio 0.98); 13 of 20 ran over their plan. The top of the distribution is priced well.

**The cost model is biased, and the bias depends on the pricing basis**:

| Lane | Chunks | Planned s | Measured s | Measured/planned (sums) | Per-chunk ratio p50 / p95 / max | Over plan |
|---|---|---|---|---|---|---|
| antiphon | 638 | 115,621 | 72,197 | 0.62 | 2.36 / 28.5 / 71.8 | 475 |
| e2e | 75 | 61,660 | 14,472 | **0.23** | 0.22 / 0.42 / 2.89 | 1 |
| session-runner | 53 | 1,114 | 3,280 | 2.94 | 20.9 / 42.5 / 79.7 | 52 |
| pty-host | 53 | 73 | 1,317 | 18.0 | 48.3 / 54.2 / 55.3 | 53 |
| agents-pty | 9 | 352 | 394 | 1.12 | 26.9 / 64.2 / 67.4 | 8 |
| markdown-pdf | 11 | 8 | 318 | 39.8 | 57.0 / 60.6 / 60.7 | 11 |
| **all** | 839 | **178,828** | **91,977** | **0.51** | 2.42 / 50.9 / 79.7 | 600 |

By `pricingBasis` (chunks, planned s → measured s): measured 218, 29,555 → 23,901 (0.81); observed-bound 105,
47,967 → 18,912 (0.39); conservative-bound 39, 29,860 → 5,452 (0.18); mixed 93, 55,821 → 29,460 (0.53);
pace 194, 12,967 → 7,210 (0.56); model 190, 2,657 → 7,042 (**2.65**).

Two errors partly cancel. The total is over-priced about 2x, almost all of it in the e2e and antiphon bound
rows. Small chunks are under-priced 20 to 80x, because the model prices test seconds and leaves out the ~20 s
fixed launch cost. Many chunks run over their own price, yet the total comes in at half the plan.

### Top 40 test classes (summed TRX duration of every case)

1,689 classes in all.

| # | class | suite | count | total s | mean | p50 | p95 | max |
|---|---|---|---|---|---|---|---|---|
| 1 | E2E.AgentTaskLandDeliveryE2ETests | e2e | 53 | 8106 | 152.9 | 148.1 | 227.2 | 319.8 |
| 2 | Tests.Application.LandNonCodeSkipTests | antiphon | 111 | 2032 | 18.3 | 12.4 | 33.4 | 128.7 |
| 3 | E2E.DispatchBaseWarningDeliveryE2ETests | e2e | 13 | 1927 | 148.2 | 143.6 | 227.3 | 234.3 |
| 4 | Tests.Application.AgentTaskLandPersistenceFailureTests | antiphon | 52 | 1695 | 32.6 | 35.7 | 41.1 | 43.4 |
| 5 | Tests.Application.AgentTaskLandRemovalMatrixTests | antiphon | 52 | 1444 | 27.8 | 26.9 | 34.4 | 35.3 |
| 6 | Tests.Application.PostLandMutationDeliveryTests | antiphon | 67 | 1444 | 21.6 | 21.8 | 27.4 | 48.5 |
| 7 | Tests.Application.AgentTaskLandHalfResetTests | antiphon | 56 | 1411 | 25.2 | 19.2 | 64.5 | 83.7 |
| 8 | Tests.Infrastructure.WorktreeGuardedCleanupTests | antiphon | 49 | 1365 | 27.9 | 26.7 | 35.5 | 39.3 |
| 9 | Tests.Application.PostLandMutationCustodyTests | antiphon | 56 | 1331 | 23.8 | 23.2 | 28.9 | 31.0 |
| 10 | Tests.Scripts.RollingVolumeRecycleScriptTests | antiphon | 28 | 1316 | 47.0 | 21.3 | 170.5 | 175.8 |
| 11 | Tests.Application.AgentTaskLandPublicationTests | antiphon | 64 | 1306 | 20.4 | 23.1 | 33.8 | 38.0 |
| 12 | Tests.Application.AgentTaskLandCleanupSafetyTests | antiphon | 44 | 1299 | 29.5 | 29.8 | 35.3 | 37.2 |
| 13 | Tests.Checkpoints.CheckpointReceiptSafetyTests | antiphon | 7 | 1280 | 182.9 | 59.0 | 647.4 | 859.1 |
| 14 | Tests.Application.AgentTaskLandActiveSourceClaimTests | antiphon | 16 | 1257 | 78.5 | 78.3 | 86.6 | 87.6 |
| 15 | Tests.Application.ChannelOutboundUnifiedTransportTests | antiphon | 17 | 1197 | 70.4 | 34.3 | 158.1 | 159.9 |
| 16 | Tests.Scripts.RemoteScriptContractTests | antiphon | 271 | 1190 | 4.4 | 1.0 | 27.0 | 49.2 |
| 17 | Tests.Application.BlockedTaskParkReplyAdmissionTests | antiphon | 63 | 1130 | 17.9 | 15.6 | 42.7 | 66.8 |
| 18 | Tests.Application.AgentTaskLandCheckpointMatrixTests | antiphon | 41 | 1120 | 27.3 | 23.0 | 39.2 | 39.9 |
| 19 | Tests.Application.AgentTaskDispatchBaseGuardTests | antiphon | 96 | 1117 | 11.6 | 11.4 | 15.7 | 49.7 |
| 20 | Tests.Application.PostLandMutationCleanupTests | antiphon | 46 | 1002 | 21.8 | 22.1 | 23.9 | 27.2 |
| 21 | Tests.Infrastructure.CompletedCardWorktreeRemovalTests | antiphon | 37 | 979 | 26.5 | 21.6 | 57.5 | 73.2 |
| 22 | Tests.Scripts.RunCheckpointScriptTests | antiphon | 26 | 960 | 36.9 | 25.0 | 87.8 | 133.4 |
| 23 | Tests.Application.AgentTaskLandBoundaryTests | antiphon | 38 | 895 | 23.5 | 26.5 | 36.6 | 37.3 |
| 24 | Tests.Application.BlockedTaskParkReclaimTests | antiphon | 38 | 878 | 23.1 | 4.0 | 115.6 | 224.3 |
| 25 | Tests.Application.AgentTaskLandBoundaryControlledTests | antiphon | 94 | 843 | 9.0 | 8.7 | 12.1 | 13.8 |
| 26 | Tests.Application.ChannelOutboundUnifiedCrashTests | antiphon | 21 | 824 | 39.2 | 24.9 | 88.3 | 96.9 |
| 27 | Tests.Application.BlockedTaskParkReleaseTests | antiphon | 3 | 760 | 253.4 | 188.8 | 440.5 | 468.4 |
| 28 | Tests.Application.PostLandMutationWorktreeTests | antiphon | 33 | 716 | 21.7 | 22.0 | 25.3 | 25.7 |
| 29 | Tests.Infrastructure.LandingRemovalControlTests | antiphon | 34 | 710 | 20.9 | 20.3 | 27.1 | 28.8 |
| 30 | Tests.Application.LandGraphEvaluationTests | antiphon | 136 | 709 | 5.2 | 2.0 | 17.6 | 101.8 |
| 31 | Tests.Application.AgentTaskLandBareRepositoryCleanupTests | antiphon | 15 | 702 | 46.8 | 43.9 | 64.3 | 64.6 |
| 32 | Tests.Application.AgentTaskWorktreeLockOutcomeTests | antiphon | 20 | 697 | 34.8 | 33.6 | 39.1 | 44.1 |
| 33 | Tests.Application.AgentTaskLandRecoveryTests | antiphon | 22 | 658 | 29.9 | 30.2 | 52.7 | 52.8 |
| 34 | Tests.Scripts.ChangeAwareReleaseTests | antiphon | 3 | 656 | 218.5 | 202.8 | 275.2 | 283.2 |
| 35 | Tests.Application.CodexCliObservationTests | antiphon | 8 | 581 | 72.7 | 51.3 | 192.6 | 228.9 |
| 36 | Tests.Application.BlockedTaskParkDeliveryTests | antiphon | 6 | 579 | 96.6 | 31.3 | 298.0 | 353.8 |
| 37 | Tests.Infrastructure.SettledWorktreeRemovalTests | antiphon | 78 | 576 | 7.4 | 6.5 | 12.8 | 14.1 |
| 38 | Tests.Application.AgentTaskLandAdoptionTests | antiphon | 21 | 570 | 27.2 | 21.9 | 51.1 | 53.2 |
| 39 | Tests.Application.AgentTaskReplyIntegrationTests | antiphon | 341 | 563 | 1.7 | 0.9 | 5.0 | 18.7 |
| 40 | Tests.Application.AgentTaskLandRefusedRetryTests | antiphon | 15 | 563 | 37.5 | 41.7 | 54.9 | 55.8 |

### Top 40 individual tests (by max; count = cases of that method across chunks)

| # | test | suite | count | total s | mean | p50 | p95 | max | outcomes |
|---|---|---|---|---|---|---|---|---|---|
| 1 | CheckpointReceiptSafetyTests.malformed_collection_and_string_shapes_return_named_refusals | antiphon | 1 | 859 | 859.1 | 859.1 | 859.1 | 859.1 | {'Passed': 1} |
| 2 | BlockedTaskParkReleaseTests.C1065_ReportPublicationPrecedesPhysicalRelease | antiphon | 1 | 468 | 468.4 | 468.4 | 468.4 | 468.4 | {'Passed': 1} |
| 3 | CheckpointSourceApprovalTests.recovery_and_resume_recheck_source_assertion | antiphon | 1 | 431 | 430.6 | 430.6 | 430.6 | 430.6 | {'Passed': 1} |
| 4 | CheckpointRepeatBuildIsolationTests.repeat_two_then_default_then_three_rebuilds_the_same_project | antiphon | 1 | 356 | 355.6 | 355.6 | 355.6 | 355.6 | {'Passed': 1} |
| 5 | BlockedTaskParkDeliveryTests.C1065_FullAnswerRequiresNewSessionUserPrompt | antiphon | 1 | 354 | 353.8 | 353.8 | 353.8 | 353.8 | {'Passed': 1} |
| 6 | AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch4 | e2e | 2 | 539 | 269.5 | 269.5 | 314.7 | 319.8 | {'Passed': 2} |
| 7 | BlockedTaskParkResumeTests.C1065_ResumePreservesSourceAndAdmissionRefusals | antiphon | 1 | 299 | 298.8 | 298.8 | 298.8 | 298.8 | {'Passed': 1} |
| 8 | ChangeAwareReleaseTests.C1313_BaselineAuthority | antiphon | 1 | 283 | 283.2 | 283.2 | 283.2 | 283.2 | {'Passed': 1} |
| 9 | DelegateBranchDeliveryTests.C1213_QueuedBranchRecovery | antiphon | 1 | 277 | 277.4 | 277.4 | 277.4 | 277.4 | {'Passed': 1} |
| 10 | DispatchBaseWarningDeliveryE2ETests.C540_PreEnqueueWaitSurvivesSlowMaterialization | e2e | 1 | 234 | 234.3 | 234.3 | 234.3 | 234.3 | {'Passed': 1} |
| 11 | CodexCliObservationTests.C959_Old_version_with_floor_never_refuses | antiphon | 1 | 229 | 228.9 | 228.9 | 228.9 | 228.9 | {'Passed': 1} |
| 12 | AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch3 | e2e | 2 | 425 | 212.7 | 212.7 | 226.3 | 227.8 | {'Passed': 2} |
| 13 | CodexCliObservationGapTests.C1029_Per_kind_receipts | antiphon | 1 | 228 | 227.6 | 227.6 | 227.6 | 227.6 | {'Passed': 1} |
| 14 | AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch2 | e2e | 2 | 428 | 214.1 | 214.1 | 225.9 | 227.3 | {'Passed': 2} |
| 15 | AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch1 | e2e | 2 | 425 | 212.6 | 212.6 | 225.7 | 227.2 | {'Passed': 2} |
| 16 | AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix_Batch1 | e2e | 2 | 425 | 212.4 | 212.4 | 223.3 | 224.5 | {'Passed': 2} |
| 17 | BlockedTaskParkReclaimTests.C1065_ClaimAndReplyInvalidateLegacyCandidate | antiphon | 1 | 224 | 224.3 | 224.3 | 224.3 | 224.3 | {'Passed': 1} |
| 18 | DispatchBaseWarningDeliveryE2ETests.C540_PostPromptCrashDoesNotRetype | e2e | 1 | 223 | 222.5 | 222.5 | 222.5 | 222.5 | {'Passed': 1} |
| 19 | AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix_Batch2 | e2e | 2 | 441 | 220.5 | 220.5 | 221.8 | 221.9 | {'Passed': 2} |
| 20 | AgentTaskLandDeliveryE2ETests.C488_ReviewEvidenceCrashRecovers | e2e | 1 | 221 | 220.8 | 220.8 | 220.8 | 220.8 | {'Passed': 1} |
| 21 | DispatchBaseWarningDeliveryE2ETests.C540_PreTypingCrashRecoversWarnings | e2e | 1 | 220 | 219.7 | 219.7 | 219.7 | 219.7 | {'Passed': 1} |
| 22 | AgentTaskLandDeliveryE2ETests.C488_ReviewToLandReceiptMatrix_Batch3 | e2e | 2 | 407 | 203.6 | 203.6 | 215.8 | 217.1 | {'Passed': 2} |
| 23 | AgentTaskLandDeliveryE2ETests.C488_ReviewDeliveryCrashMatrix_Batch5 | e2e | 1 | 214 | 214.1 | 214.1 | 214.1 | 214.1 | {'Passed': 1} |
| 24 | AgentTaskLandDeliveryE2ETests.C488_ApprovalReceiptSaveFailureNeverRetypes | e2e | 1 | 213 | 212.9 | 212.9 | 212.9 | 212.9 | {'Passed': 1} |
| 25 | BlockedTaskSyncRecoveryTests.C1065_SyncDebtRecoversWithoutMintingApproval | antiphon | 1 | 207 | 206.8 | 206.8 | 206.8 | 206.8 | {'Passed': 1} |
| 26 | ChangeAwareReleaseTests.C1313_ModeCredit | antiphon | 1 | 203 | 202.8 | 202.8 | 202.8 | 202.8 | {'Passed': 1} |
| 27 | AgentTaskLandDeliveryE2ETests.C467_V24_BlockedWriterThenReleaseDeliversBothNotes | e2e | 1 | 197 | 196.7 | 196.7 | 196.7 | 196.7 | {'Passed': 1} |
| 28 | BlockedTaskParkReleaseTests.C1065_EachBlockCauseUsesCurrentIdleProof | antiphon | 1 | 189 | 188.8 | 188.8 | 188.8 | 188.8 | {'Passed': 1} |
| 29 | AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence_Batch4 | e2e | 2 | 334 | 167.1 | 167.1 | 184.2 | 186.1 | {'Passed': 2} |
| 30 | AgentTaskLandDeliveryE2ETests.C488_ApprovalOutcomeReceiptMatrix | e2e | 1 | 186 | 185.7 | 185.7 | 185.7 | 185.7 | {'Passed': 1} |
| 31 | AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence_Batch1 | e2e | 2 | 334 | 166.9 | 166.9 | 183.4 | 185.2 | {'Passed': 2} |
| 32 | AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence_Batch3 | e2e | 2 | 335 | 167.6 | 167.6 | 183.1 | 184.8 | {'Passed': 2} |
| 33 | AgentTaskLandDeliveryE2ETests.C494_ReviewReceiptRejectsFalseEvidence_Batch2 | e2e | 2 | 332 | 166.2 | 166.2 | 182.1 | 183.9 | {'Passed': 2} |
| 34 | PlanCoverageBindingTests.historical_plans_bind_without_suppression | antiphon | 1 | 180 | 180.4 | 180.4 | 180.4 | 180.4 | {'Passed': 1} |
| 35 | WorkspaceSourceVerificationWireTests.C1065_LocalSourceModesVerifyFreshAtRelease | session-runner | 1 | 179 | 179.0 | 179.0 | 179.0 | 179.0 | {'Passed': 1} |
| 36 | RollingVolumeRecycleScriptTests.C1008_Legacy_rolling_and_jq_rosters_remain | antiphon | 4 | 676 | 169.0 | 170.4 | 175.1 | 175.8 | {'Passed': 4} |
| 37 | BlockedTaskParkReclaimTests.C1147_RolledBackConfirmationIsNotThisRunsRelease | antiphon | 1 | 175 | 174.6 | 174.6 | 174.6 | 174.6 | {'Passed': 1} |
| 38 | RunCheckpointSourceScriptTests.C835_DriftAndReuse | antiphon | 1 | 171 | 170.8 | 170.8 | 170.8 | 170.8 | {'Passed': 1} |
| 39 | ChangeAwareReleaseTests.C1313_ParameterHops | antiphon | 1 | 169 | 169.5 | 169.5 | 169.5 | 169.5 | {'Passed': 1} |
| 40 | ChannelOutboundUnifiedTransportTests.C519_Queue_to_adapter | antiphon | 3 | 461 | 153.7 | 151.1 | 159.0 | 159.9 | {'Passed': 3} |

## 4. Distribution

| Bucket | Tests | Share of tests | Total s | Share of time |
|---|---|---|---|---|
| < 1 s | 13,063 | 57.3 % | 2,397 | 2.2 % |
| 1 to 10 s | 7,065 | 31.0 % | 27,169 | 24.7 % |
| 10 to 60 s | 2,439 | 10.7 % | 53,596 | **48.7 %** |
| 1 to 5 min | 207 | 0.9 % | 24,081 | 21.9 % |
| > 5 min | 6 | 0.03 % | 2,787 | 2.5 % |

The 2,652 tests (11.6 %) of 10 s or more take 73 % of all test time. The six tests over 5 minutes are
CheckpointReceiptSafety malformed_collection 859 s, BlockedTaskParkRelease C1065_ReportPublication 468 s,
CheckpointSourceApproval recovery_and_resume 431 s, CheckpointRepeatBuildIsolation 356 s,
BlockedTaskParkDelivery C1065_FullAnswer 354 s, and the e2e C488_ReviewDeliveryCrashMatrix_Batch4 at 320 s
(one case).

Outcomes: 22,659 Passed, 58 Failed, 63 NotExecuted.

### e2e lane, taken separately

- 75 chunks: 14,472 s measured against 61,660 s planned (0.23). The TRX holds 171 results with a test-sum of
  10,823 s. The median chunk overhead (wall minus tests) is 29 s.
- **AgentTaskLandDeliveryE2ETests**: planned 31,800 s (53 cases × 600 s, `observed-bound`, CARD-1584) across
  36 chunks of 600 or 1,200 s. **Measured: 9,020 s of chunk wall, 8,106 s of test time, 0.28 of plan.** Per
  chunk the p50 is 221 s, the p95 460 s and the max 560 s (all-split-9). Per case the mean is 152.9 s, the p50
  148.1 s, the p95 227.2 s and the max 319.8 s (C488_ReviewDeliveryCrashMatrix_Batch4). Outcomes: 47 Passed,
  6 Failed. The 600 s per-case bound is 1.9x the slowest case seen and 4x the mean.
- The other 39 e2e chunks: 5,452 s measured against 29,860 s planned (0.18), all `conservative-bound` at
  300 s per case.

## 5. Budget fit (< 2x headroom, per docs/test-performance-standards.md)

Timeouts were read from `[Timeout(ms)]` attributes in `tests/**/*.cs` at master `87f4a868a`, which is the
rc/2026-11-01 tip: 720 method-level and 26 class-level attributes. The top hit was confirmed unchanged at
`cde8def4`. Each was compared with the method's max measured duration in d47848cc. Tests that use more than
50 % of their budget:

| Test | Max measured s | Timeout s | Used | Headroom | Chunk | Outcome |
|---|---|---|---|---|---|---|
| DelegateBranchDeliveryTests.C1213_QueuedBranchRecovery | 277.4 | 300 | **92 %** | **1.08x** | antiphon/all-group-43 | Passed |
| CheckpointRepeatBuildIsolationTests.repeat_two_then_default_then_three_rebuilds_the_same_project | 355.6 | 600 | 59 % | 1.69x | antiphon/all-group-102 | Passed |
| CheckpointLargeStampTests.C1532_second_build_and_retry_admit_large_prior_output | 68.9 | 120 | 57 % | 1.74x | antiphon/all-group-103 | Passed |
| DispatchBaseNotificationTests.C508_IntentScanIsolation | 90.6 | 180 | 50 % | 1.99x | antiphon/all-group-50 | Passed |

- **C1213_QueuedBranchRecovery is 23 s from its 300 s timeout** and is the clear flake risk. The other three
  have less than 2x headroom.
- The named cases are not in this run. `RcFailedFirstExecutionTests.JournalCustodySurvivesInterleaving`
  (`[Timeout(240_000)]` at master) and `Journal_intents_and_partial_resume_are_exact` produced no result in
  d47848cc, because they were added after `cde8def4`. Their brief figures (163 s under load against a 180 s
  budget; about 6 to 7 min against a 600 s cap) cannot be checked from this run. If they are accurate, they sit
  at 1.1x and 1.4 to 1.7x headroom.
- Chunk level: the antiphon watchdog is 3,600 s and the slowest chunk took 1,516 s (42 %). No chunk or test
  exceeded 30 minutes.
- Most of the slowest tests carry no per-test `[Timeout]`, among them the 859 s CheckpointReceiptSafety case
  and the 468 s C1065_ReportPublication case. They are bounded only by the chunk watchdog, so a budget-fit list
  built from attributes cannot see them.

## 6. Failed-first band: how soon we know whether the fixes held

`suites[]` lists **29 non-pass chunks**. 25 of them have failed TRX rows (exit 2); the other 4 are exit-8
profile chunks with no executed test (antiphon all-profile-767, -1616, -1803, -1804). That 25 is the brief's
"25 failed chunks".

| suite/chunk | result | exit | timedOut | wall s | planned s | failed tests |
|---|---|---|---|---|---|---|
| agents-pty/all-group-3 | FAIL | 2 | False | 28 | 11.5 | 1 |
| antiphon/all-group-104 | FAIL | 2 | False | 242 | 223 | 8 |
| antiphon/all-group-105 | FAIL | 2 | False | 119 | 30.5 | 1 |
| antiphon/all-group-110 | FAIL | 2 | False | 729 | 900 | 1 |
| antiphon/all-group-111 | FAIL | 2 | False | 261 | 899.9 | 1 |
| antiphon/all-group-120 | FAIL | 2 | False | 1072 | 900 | 1 |
| antiphon/all-group-124 | FAIL | 2 | False | 230 | 109 | 8 |
| antiphon/all-group-126 | FAIL | 2 | False | 328 | 835.4 | 1 |
| antiphon/all-group-128 | FAIL | 2 | False | 24 | 11.5 | 1 |
| antiphon/all-group-48 | FAIL | 2 | False | 377 | 900 | 1 |
| antiphon/all-group-79 | FAIL | 2 | False | 196 | 899.7 | 1 |
| antiphon/all-group-80 | FAIL | 2 | False | 197 | 858 | 20 |
| antiphon/all-profile-1616 | FAIL | 8 | False | 22 | 9.2 | 0 |
| antiphon/all-profile-1716 | FAIL | 2 | False | 65 | 9.2 | 1 |
| antiphon/all-profile-1762 | FAIL | 2 | False | 22 | 9.2 | 1 |
| antiphon/all-profile-1803 | FAIL | 8 | False | 23 | 64.4 | 0 |
| antiphon/all-profile-1804 | FAIL | 8 | False | 20 | 64.4 | 0 |
| antiphon/all-profile-767 | FAIL | 8 | False | 18 | 7.9 | 0 |
| e2e/all-split-1 | FAIL | 2 | False | 227 | 1200 | 1 |
| e2e/all-split-2 | FAIL | 2 | False | 212 | 1200 | 1 |
| e2e/all-split-3 | FAIL | 2 | False | 222 | 1200 | 1 |
| e2e/all-split-32 | FAIL | 2 | False | 105 | 600 | 1 |
| e2e/all-split-33 | FAIL | 2 | False | 159 | 600 | 1 |
| e2e/all-split-4 | FAIL | 2 | False | 297 | 1200 | 1 |
| e2e/all-split-63 | FAIL | 2 | False | 271 | 1500 | 1 |
| pty-host/all-group-1 | FAIL | 2 | False | 58 | 35 | 1 |
| pty-host/all-group-2 | FAIL | 2 | False | 24 | 11.5 | 1 |
| session-runner/all-group-4 | FAIL | 2 | False | 342 | 36 | 1 |
| session-runner/all-group-6 | FAIL | 2 | False | 25 | 11.5 | 1 |

- Serial re-run of the 25 chunks with failures: **5831 s (about 1 h 37 min)**. Adding the 4 exit-8 chunks
  (83 s) gives 5,914 s (about 1 h 39 min). The packer planned 14,337 s for these 29 chunks.
- Of that, the 7 e2e chunks take 1493 s. The four largest antiphon chunks (all-group-120 1,072 s,
  -110 729 s, -48 377 s, -126 328 s) dominate the rest.
- If the seed re-runs only the 58 failing tests (`failedTests[]` in the stopped-run seed) rather than whole
  chunks, the test time is 1,103 s. Adding the ~20 s launch cost for each of about 25 launches puts it near
  **30 min**.
- **Answer:** at the d47848cc pace, a failed-first band that re-runs the failed chunks gives a full verdict on
  the fixes about 1 h 40 min after the shared build. The shared build is about 14 min, so expect the verdict
  about 1 h 54 min after the run starts.

## 7. What a new rc/2026-11-01 run plans under the same cost model

Nothing was run. At rc/2026-11-01 (`87f4a868a`) `nightly-class-costs.json` differs from `cde8def4` by one row:
`Antiphon.E2E.LandDeliveryFixtureProbeTests` goes from 10 s to 60 s. The table total moves from 168,926 s to
168,976 s across 771 rows. Over the same test census, the plan would be **about 178,878 s** (d47848cc's
178,828 s plus 50 s). Of that, antiphon is 115,621 s, e2e about 61,710 s, session-runner 1,114 s,
agents-pty 352 s, pty-host 73 s and markdown-pdf 8 s.

Tests added between `cde8def4` and `87f4a868a` are priced by `pace` or `model` and are not counted in that
figure. The exact number appears only when the packer writes `groups-planned-*.json` for the new run (run
8dc9e695's log dir was still empty when this was read). On d47848cc's measured ratio (0.51), a plan of
178,878 s predicts about **91,000 to 93,000 s of chunk wall, roughly 26 h**, at 1-parallel.

## Remaining uncertainties

- Windmill nightly results were not read (see §1).
- `tests.csv` and the phase summary are `complete=false` for this run, so the per-phase split (db, process,
  fs, fixture) is not trustworthy and was not used.
- Timeouts were mapped from source by method name. Overloads with the same name and assembly-level defaults
  could hide a budget, so the §5 list is a floor.
- The 25 versus 29 failed-chunk count depends on whether exit-8 (no test executed) chunks count as failures.
  Both totals are given.

## Not done, noted

- Fix idea, one line: persist a per-run aggregate (lane totals, class totals, planned/measured) into a
  cross-run history file and refit the cost model with a per-chunk launch constant (about 20 s), so small
  suites stop being under-priced while e2e and the bound rows stop being over-priced.
