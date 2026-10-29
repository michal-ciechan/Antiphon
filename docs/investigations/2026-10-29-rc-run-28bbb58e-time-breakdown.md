# rc run 28bbb58e: where the 22 h 58 m went

Run `28bbb58edca04b0ebb7afc914b74d386` (task 5906dbf2), rc `4563e525`, native Windows (DESKTOP, `nproc` 8),
Full, `nativeActiveMax` 1. 2026-10-28T10:13:24Z to 2026-10-29T09:11:30Z, `durationSeconds` 82686.4 (1378.1 min).

Sources (all read-only). `L` = `C:\Antiphon\releases\candidates\rc-2026-10-27-4563e52529b2\logs`.
- `L\2026-10-28-1013-28bbb58e\summary.json`: `timingChunks[]` (387; group 105 has no start/end), `suites[]`, `suiteTimeline[]`.
- 384 `*.trx` files: per-test `duration`, `startTime`, `endTime`.
- 385 `*-execution\*.diag` files: MTP process start, `Starting test session`, `Test session ... ended`.
- `*-tests.log` mtimes and the setup logs in the same folder.
- Timing root `L\timing-28bbb58edca04b0ebb7afc914b74d386\` (`*.timing.json`, `tests.csv`, `summary.json`).
- `C:\Antiphon\releases\task-5906dbf2\{commission,supervisor}-console.log`.
- Harness code at the run checkout `C:\Antiphon\releases\checkout-5906dbf2` (read only).

Every number below comes from these files. Estimates are marked **est.**

## Headline

- The run executed **one chunk at a time**: maximum chunk overlap is 1, reconstructed from 386 start/end pairs. There was no slot or queue wait (`queueWaitSeconds` 0, `buildSlot` `not-used` on every chunk). Wall time therefore equals the sum of setup, chunk walls and gaps.
- Antiphon.Tests took **1072 min (78 %)** of the run. Within it, 122 chunks ran their tests **strictly serially** and took **657.5 min**. Their classes carry `[ParallelLimiter<ProcessSpawnLimit>]`, and that limit is 1 (`tests/Antiphon.Tests/ProcessSpawnLimit.cs:15` at the run checkout; 347 files carry the attribute).
- **114.2 min (8.3 %)** went to processes lingering after MTP reported "Test session ended": about 20 s on each of 384 chunks.
- Group 105 hung for **52.4 min** after its last diag line, until the 60-min watchdog killed it.
- About **47 min** went to in-process setup before the first test of 190 antiphon chunks (about 14 s each).

## 1. Time budget

| Phase | Start (UTC) | Minutes | Share | Source |
|---|---|---:|---:|---|
| npm ci | 10:13:24 | 0.4 | | npm-ci.log mtime |
| npm run build | 10:13:49 | 0.3 | | npm-build.log mtime |
| client lint | 10:14:10 | 0.6 | | client-lint.log mtime |
| dotnet build Antiphon.sln | 10:14:44 | 6.2 | | dotnet-build.log `Time Elapsed 00:06:09.35`; chunk `buildSeconds` 449.8 covers npm through build |
| Antiphon.Tests profile discovery (20156 tests) | 10:20:55 | 0.4 | | discovery log/json mtimes |
| unlogged pre-run (planning; supervisor shows tree CPU busy) | 10:21:16 | 3.2 | | gap to groups-planned.json creation at 10:24:26 |
| **setup subtotal** | | **11.0** | 0.8 % | |
| 386 chunks with stamps | 10:24:26 | 1305.5 | 94.7 % | timingChunks start/end |
| group 105 (watchdog kill) | 21:06:49 | 60.2 | 4.4 % | inventory doc §1b; gap 104→106 = 3615.0 s |
| inter-chunk gaps (excl. 105) | | 1.4 | 0.1 % | 385 gaps: median 0.10 s, p90 0.24 s, max 20.0 s (antiphon→session-runner, includes suite discovery) |
| **run total** | | **1378.1** | | summary.json |
| post-run summary + report (outside `durationSeconds`) | 09:11:30 | 3.2 | | commission-console.log |

### Per project (suite)

| Suite | Chunks | Wall min | Tests (TRX) | Sum of test durations min | In-chunk parallelism (Σdur/span) |
|---|---:|---:|---:|---:|---:|
| antiphon (Antiphon.Tests) | 231 + g105 | 1012.0 + 60.2 | 19726 (+331 in g105, no TRX) | 1627.8 | 1.79 overall; 1.0 in 122 chunks, 3.33 in 109 chunks |
| e2e | 75 | 220.9 | 171 | 167.2 | 0.95 |
| session-runner | 6 | 31.5 | 1712 | 31.7 | 1.13 |
| pty-host | 50 | 18.7 | 124 | 0.5 | n/a |
| client (Vitest) | 1 | 6.3 | no TRX | n/a | n/a |
| agents-pty | 9 | 6.2 | 667 | 4.7 | |
| markdown-pdf | 11 | 5.6 | 16 | 0.1 | |
| scripts lane | 1 | 1.8 | no TRX | n/a | |
| messaging | 1 | 1.3 | 250 | 1.3 | |
| observability | 1 | 1.2 | 12 | 0.7 | |

Suite order (suiteTimeline): antiphon 10:24 to 04:17, session-runner to 04:49, pty-host to 05:07, agents-pty to 05:14,
messaging, client 05:15 to 05:21, scripts to 05:23, e2e 05:23 to 09:04, markdown-pdf, observability.

### Chunk wall decomposed (384 chunks with a diag and TRX)

| Component | Minutes | How measured |
|---|---:|---|
| launch (chunk start → first diag line) | 2.1 | median 0.2 to 0.4 s |
| host init (diag start → `Starting test session`) | 1.4 | median 0.22 s |
| test session (`Starting` → `ended`) | 1177.7 | MTP diag |
| ↳ of which antiphon pre-first-test setup (session start → first TRX `startTime`) | about 47.1 | 190 chunks at about 13.9 s each; 41 chunks about 1 s |
| **post-session linger** (`ended` → `tests.log` written) | **114.2** | median by suite: antiphon 16.7 s, e2e 25.3, pty-host 20.9, markdown-pdf 29.1, agents-pty 23.6, session-runner 24.1 |
| harness after log (verdict, timing write) | 2.0 | `tests.log` mtime → `completedAt` |

The harness's native owner loops until the root has exited **and** the job object's process count is 0
(`scripts/lib/nightly-owned-process.cs:170-180`). Only after that does it write `tests.log`
(`:200-213`). The ~20 s therefore belongs to a process that stays alive after the test session ends. The run
records neither which process it was nor whether it was the root or a descendant (see §5).
`TESTINGPLATFORM_TELEMETRY_OPTOUT` is not set; the diag shows `Send telemetry event` at exit. That is
one **unconfirmed** candidate.

### Retries and reruns

No chunk ran twice: `attempt` is "1" everywhere, and there are no duplicate chunk ids. **Rerun overhead was 0.**
Wasted time came instead from failures and the hang:

| Item | Minutes |
|---|---:|
| group 105 idle hang after its last diag line (21:14:39 → kill 22:07:03) | 52.4 |
| group 105 real work before the hang (21:06:49 → 21:14:39) | 7.8 |
| durations of the 36 TRX Failed tests (incl. `C1313_BaselineAuthority` 300.8 s and `repeat_two_then_default...` 300.0 s, both at a 5-min budget) | 25.7 |

## 2. Slowest chunks and tests

Test counts come from TRX. The sidecar `testCount` is 0 for 54 antiphon chunks that ran tests (for example group 9: sidecar 0, TRX 16).

| # | Chunk | Wall s | Min | TRX tests | Σdur/span | Notes |
|---:|---|---:|---:|---:|---:|---|
| 1 | antiphon all-group-105 | 3615 | 60.2 | 331 (diag) | n/a | hung `CheckpointExecutorLogTests.concurrent_callbacks_...`; watchdog 3600000 ms (`nightly-tests-impl.ps1:1212`) |
| 2 | antiphon all-group-103 | 1936 | 32.3 | 47 | 1.01 | one test 854.5 s, one Failed at 300.0 s |
| 3 | antiphon all-group-109 | 1360 | 22.7 | 49 | 1.00 | WorktreeGuardedCleanupTests only |
| 4 | antiphon all-group-11 | 1352 | 22.5 | 67 | 1.00 | PostLandMutationDeliveryTests |
| 5 | antiphon all-group-119 | 1257 | 20.9 | 32 | 1.00 | RollingVolumeRecycleScriptTests |
| 6 | antiphon all-group-9 | 1221 | 20.4 | 16 | 1.00 | AgentTaskLandActiveSourceClaimTests |
| 7 | antiphon all-group-17 | 1172 | 19.5 | 17 | 1.00 | ChannelOutboundUnifiedTransportTests |
| 8 | antiphon all-group-18 | 1166 | 19.4 | 56 | 1.00 | PostLandMutationCustodyTests |
| 9 | antiphon all-group-126 | 1109 | 18.5 | 299 | 1.04 | 20+ script-test classes |
| 10 | antiphon all-group-20 | 1082 | 18.0 | 63 | 1.00 | BlockedTaskParkReplyAdmissionTests |
| 11 | antiphon all-group-123 | 1078 | 18.0 | 32 | 1.00 | ChangeAwareReleaseTests et al. |
| 12 | antiphon all-group-124 | 1038 | 17.3 | 38 | 1.00 | |
| 13 | antiphon all-group-15 | 972 | 16.2 | 46 | 1.00 | PostLandMutationCleanupTests |
| 14 | antiphon all-group-59 | 971 | 16.2 | 65 | 1.00 | |
| 15 | antiphon all-group-120 | 947 | 15.8 | 26 | 1.00 | |
| 16 | antiphon all-group-44 | 938 | 15.6 | 15 | 1.00 | |
| 17 | antiphon all-group-46 | 902 | 15.0 | 62 | 1.00 | |
| 18 | antiphon all-group-47 | 891 | 14.8 | 55 | 1.07 | |
| 19 | antiphon all-group-27 | 870 | 14.5 | 153 | 1.00 | |
| 20 | antiphon all-group-111 | 823 | 13.7 | 63 | 1.14 | |
| 21 | antiphon all-group-22 | 810 | 13.5 | 38 | 1.00 | |
| 22 | antiphon all-group-34 | 805 | 13.4 | 49 | 1.00 | |
| 23 | antiphon all-group-28 | 802 | 13.4 | 16 | 1.12 | |
| 24 | antiphon all-group-42 | 801 | 13.4 | 87 | 1.00 | |
| 25 | session-runner all-group-1 | 799 | 13.3 | 1179 | 1.18 | |
| 26 | antiphon all-group-38 | 792 | 13.2 | 26 | 1.00 | |
| 27 | antiphon all-group-104 | 789 | 13.1 | 121 | 1.38 | |
| 28 | antiphon all-group-112 | 746 | 12.4 | 222 | 1.00 | |
| 29 | antiphon all-group-121 | 737 | 12.3 | 37 | 1.00 | |
| 30 | antiphon all-group-26 | 733 | 12.2 | 28 | 1.04 | |

The planner group/split id is the chunk id. The planned cost per group is **not recoverable**: `groups-planned.json` keeps
only the last suite written (observability, 1 group). See §5.

### 40 slowest tests (TRX `duration`)

| # | s | Outcome | Chunk | Class | Test |
|---:|---:|---|---|---|---|
| 1 | 854.5 | Passed | antiphon g103 | CheckpointReceiptSafetyTests | malformed_collection_and_string_shapes_return_named_refusals |
| 2 | 479.0 | Passed | antiphon g28 | BlockedTaskParkReleaseTests | C1065_ReportPublicationPrecedesPhysicalRelease |
| 3 | 419.9 | Passed | antiphon g47 | CheckpointSourceApprovalTests | recovery_and_resume_recheck_source_assertion |
| 4 | 310.5 | Passed | antiphon g38 | BlockedTaskParkDeliveryTests | C1065_FullAnswerRequiresNewSessionUserPrompt |
| 5 | 304.4 | Passed | e2e split-9 | AgentTaskLandDeliveryE2ETests | C488_ReviewDeliveryCrashMatrix_Batch4(before-typing) |
| 6 | 300.8 | Failed | antiphon g123 | ChangeAwareReleaseTests | C1313_BaselineAuthority |
| 7 | 300.0 | Failed | antiphon g103 | CheckpointRepeatBuildIsolationTests | repeat_two_then_default_then_three_rebuilds_the_same_project |
| 8 | 281.2 | Passed | antiphon g43 | BlockedTaskParkResumeTests | C1065_ResumePreservesSourceAndAdmissionRefusals |
| 9 | 264.1 | Passed | antiphon g44 | DelegateBranchDeliveryTests | C1213_QueuedBranchRecovery |
| 10 | 250.1 | Passed | antiphon g123 | ChangeAwareReleaseTests | C1313_ModeCredit |
| 11 | 237.6 | Passed | antiphon g55 | CodexCliObservationGapTests | C1029_Per_kind_receipts |
| 12 | 234.6 | Passed | e2e split-6 | AgentTaskLandDeliveryE2ETests | C488_ReviewDeliveryCrashMatrix_Batch1(settlement-before-save) |
| 13 | 234.4 | Passed | e2e split-7 | AgentTaskLandDeliveryE2ETests | C488_ReviewDeliveryCrashMatrix_Batch2(enqueue-exception) |
| 14 | 218.8 | Passed | antiphon g44 | CodexCliObservationTests | C959_Old_version_with_floor_never_refuses |
| 15 | 218.7 | Passed | e2e split-11 | AgentTaskLandDeliveryE2ETests | C488_ReviewToLandReceiptMatrix_Batch2(busy, raw) |
| 16 | 218.3 | Passed | antiphon g129 | TestTimingChokePointTests | C1514T_Overhead |
| 17 | 217.7 | Passed | e2e split-11 | AgentTaskLandDeliveryE2ETests | C488_ReviewToLandReceiptMatrix_Batch2(idle, spill) |
| 18 | 217.2 | Passed | e2e split-12 | AgentTaskLandDeliveryE2ETests | C488_ReviewToLandReceiptMatrix_Batch3(busy, Apply) |
| 19 | 216.1 | Passed | e2e split-10 | AgentTaskLandDeliveryE2ETests | C488_ReviewToLandReceiptMatrix_Batch1(idle, raw) |
| 20 | 215.6 | Passed | e2e split-53 | DispatchBaseWarningDeliveryE2ETests | C540_PreEnqueueWaitSurvivesSlowMaterialization |
| 21 | 214.5 | Passed | e2e split-36 | AgentTaskLandDeliveryE2ETests | C488_ReviewEvidenceCrashRecovers |
| 22 | 213.9 | Passed | e2e split-51 | DispatchBaseWarningDeliveryE2ETests | C540_PostPromptCrashDoesNotRetype |
| 23 | 212.9 | Passed | e2e split-35 | AgentTaskLandDeliveryE2ETests | C488_ReviewDeliveryCrashMatrix_Batch5(prompt-before-verdict) |
| 24 | 212.3 | Passed | antiphon g123 | ChangeAwareReleaseTests | C1313_ParameterHops |
| 25 | 207.1 | Passed | e2e split-54 | DispatchBaseWarningDeliveryE2ETests | C540_PreTypingCrashRecoversWarnings |
| 26 | 206.4 | Passed | e2e split-8 | AgentTaskLandDeliveryE2ETests | C488_ReviewDeliveryCrashMatrix_Batch3(queue-inserted-before-outbox-link) |
| 27 | 203.9 | Passed | antiphon g22 | BlockedTaskParkReclaimTests | C1065_ClaimAndReplyInvalidateLegacyCandidate |
| 28 | 201.3 | Passed | e2e split-9 | AgentTaskLandDeliveryE2ETests | C488_ReviewDeliveryCrashMatrix_Batch4(idle-lost-wakeup) |
| 29 | 200.9 | Passed | e2e split-6 | AgentTaskLandDeliveryE2ETests | C488_ReviewDeliveryCrashMatrix_Batch1(settlement-before-commit) |
| 30 | 196.2 | Passed | antiphon g28 | BlockedTaskParkReleaseTests | C1065_EachBlockCauseUsesCurrentIdleProof |
| 31 | 193.3 | Passed | antiphon g37 | AgentTaskLandSourceFreshnessTests | C488_DetachedFollowUpPublishesReviewedFix |
| 32 | 191.7 | Passed | antiphon g106 | PlanCoverageBindingTests | historical_plans_bind_without_suppression |
| 33 | 187.9 | Passed | session-runner g3 | WorkspaceSourceVerificationWireTests | C1065_LocalSourceModesVerifyFreshAtRelease |
| 34 | 185.8 | Passed | e2e split-14 | AgentTaskLandDeliveryE2ETests | C494_ReviewReceiptRejectsFalseEvidence_Batch2(wrong-session) |
| 35 | 185.6 | Passed | e2e split-13 | AgentTaskLandDeliveryE2ETests | C494_ReviewReceiptRejectsFalseEvidence_Batch1(Sent-only) |
| 36 | 185.0 | Passed | e2e split-16 | AgentTaskLandDeliveryE2ETests | C494_ReviewReceiptRejectsFalseEvidence_Batch4(old-sequence) |
| 37 | 184.6 | Passed | e2e split-12 | AgentTaskLandDeliveryE2ETests | C488_ReviewToLandReceiptMatrix_Batch3(busy, spill) |
| 38 | 183.8 | Passed | e2e split-15 | AgentTaskLandDeliveryE2ETests | C494_ReviewReceiptRejectsFalseEvidence_Batch3(wrong-sha) |
| 39 | 183.8 | Passed | e2e split-31 | AgentTaskLandDeliveryE2ETests | C488_ApprovalOutcomeReceiptMatrix(stale-A) |
| 40 | 181.8 | Passed | e2e split-10 | AgentTaskLandDeliveryE2ETests | C488_ReviewToLandReceiptMatrix_Batch1(idle, Apply) |

The top 40 sum to 165.4 min; the top 10 to 62.7 min. 201 tests ran ≥ 60 s (426.8 min); 2712 ran ≥ 10 s.
Classes with the most time in tests ≥ 60 s: AgentTaskLandDeliveryE2ETests 122.8 min (53 tests), DispatchBaseWarningDeliveryE2ETests 30.7,
AgentTaskLandActiveSourceClaimTests 20.1, CheckpointReceiptSafetyTests 16.8, RollingVolumeRecycleScriptTests 15.0, ChannelOutboundUnifiedTransportTests 14.8.
Clustering at round values: **45 tests ran 30.0 ± 0.6 s (22.5 min, all Passed)** and 10 ran about 60 s. Both look like fixed waits or poll budgets (not verified per test).

**Per-test timing completeness.** TRX gives a duration and start/end for every result in every suite that writes TRX: 22678 rows,
none missing. Gaps:
- **client**: Vitest, no TRX.
- **scripts lane**: pwsh scripts, no TRX.
- **group 105**: killed before TRX; the diag has per-test states, but durations were not extracted here.

`tests.csv` / timing `summary.json` (22294 rows) is **not** complete: it is marked `complete:false`, `stale-input-not-joined`, `torn-tail`, with 81 torn lines.
Its phase columns (db/host/process/fs/network/fixture) are 0 on every row, so it gives no phase attribution.

## 3. Distribution and fixed overhead

| Chunks with wall over | Count | Their minutes |
|---|---:|---:|
| 5 min | 80 | 919.6 |
| 10 min | 37 | 618.0 |
| 20 min | 6 | 179.0 (incl. g105) |
| 30 min | 2 | 92.5 (g105, g103) |

The top 10 % of chunks (39) took **637.1 min = 46.7 %** of chunk wall; the top 30 took 39.7 %.

Per-chunk fixed overhead, from the smallest chunks (1–2 tests, test span under 5 s):

| Suite | Small chunks | Wall | launch+init | post-session linger | Fixed cost per chunk |
|---|---:|---|---|---|---|
| pty-host | 49 | 19.5–20.4 s | about 0.4 s | 18.9–20.0 s | **about 20 s** |
| markdown-pdf | 10 | 26–33 s | about 0.4 s | 25–33 s | **about 29 s** |
| agents-pty | 7 | 24–32 s | about 0.4 s | 22–26 s | **about 24 s** |
| antiphon | 63 | 20–28 s | about 0.6 s + 1–14 s pre-test setup | 17–20 s | **about 20 s, or 33 s with fixture** |
| e2e | 54 | 23 s minimum | about 0.4–40 s | 23–34 s | **about 25 s** |

Assembly load and discovery inside the chunk are small: host init has a median of 0.22 s. The sidecar `discoverySeconds` is one per-suite
constant (antiphon 21.377 s on every chunk) copied from profile discovery, not a per-chunk measurement. The fixed cost is
dominated by the post-session linger. Single-test "profile" chunks pay it in full:

| Chunk kind | Chunks | Tests | Wall min | Test span min |
|---|---:|---:|---:|---:|
| antiphon all-profile | 73 | 127 | 53.3 | 14.2 |
| pty-host all-profile | 48 | 50 | 17.6 | 0.0 |
| markdown-pdf all-profile | 11 | 16 | 5.6 | 0.1 |
| agents-pty all-profile | 6 | 6 | 2.6 | 0.2 |
| e2e all-split | 64 | 105 | 206.2 | 168.6 |

## 4. Concurrency and load

- **Chunk-level concurrency was 1 for the entire run.** This was by design: `nativeActiveMax` 1. No two start/end intervals overlap.
- **In-process concurrency.**
  - Antiphon.Tests: 122 chunks run at Σdur/span < 1.15, i.e. one test at a time, and take 657.5 min. The other 109 run at 3.33×.
  - Mechanism: `ProcessSpawnLimit.Limit => 1` with `[ParallelLimiter<ProcessSpawnLimit>]` on 347 files. Also `GitProcessSpawnLimit` = clamp(ProcessorCount/2, 2, 4) on 111 files.
  - e2e runs at 0.95, i.e. serial.
- **CPU and memory.** There are no host-wide stats in the run.
  - The only proxy is the supervisor's `cpu=` counter (`supervise-rc.ps1:92`, CPU seconds of the *live* process tree, so exited processes drop out and deltas can go negative). Sampled every ~10 min, it reads **0.0–0.4 cores for most of the run**, peaking at 0.93 during agents-pty and the client lane.
  - It read 0.02 for the whole group-105 hang window (21:15–22:17), which is consistent with an idle deadlock.
  - It does not show CPU pressure. It cannot rule it out either, because each short-lived test process's CPU is lost.
  - Memory: not logged.
- **Did load slow tests?** This cannot be measured from this run. Every chunk ran alone, so there is no low- versus high-concurrency pair for the same test or class. The serial chunks' durations look like waits, not CPU work: 45 tests at exactly 30 s, 5-min budget failures, and tree CPU of about 0.1–0.4 cores.

## 5. What the instrumentation could not tell (and the smallest addition)

| Gap | Smallest addition |
|---|---|
| Which process keeps the job alive for ~20 s after session end (root exit delay versus a descendant), at a cost of 114 min | Persist `NativeProcessOwner.Observation` per chunk (`ExecutionMilliseconds`, `CleanupMilliseconds`, root-exit time, descendant image names and exit times) into `<chunk>.timing.json` |
| Planned versus actual cost per group | Write `groups-planned-<suite>.json`; today one file is overwritten per suite, and only observability survives |
| Per-chunk discovery time | Record the real per-chunk value; `discoverySeconds` is a copied suite constant |
| Sidecar `testCount` and phase attribution | `testCount` is 0 on 54 chunks; `tests.csv` is torn/unjoined with all phase columns 0. Derive counts from TRX, and fix the join |
| Fixture setup / teardown | `fixtureSetupSeconds`/`fixtureTeardownSeconds` are null everywhere; the ~14 s pre-first-test on 190 antiphon chunks is unattributed. Emit one stamp at shared-fixture start and end |
| Host CPU, memory and disk over time | Sample host-wide CPU %, available memory and disk queue every 30 s into the timing root (the repo already has `scripts/measure-host-stats-overhead.ps1`) |
| Load sensitivity | One run with `nativeActiveMax` 2 on the same SHA, or a repeat of a few serial chunks while a parallel chunk runs, then compare per-class TRX durations |
| Unlogged 3.2 min before the first chunk; setup step durations | Log start/end stamps for planning and each setup step in summary.json (`builds[]` has no durations) |
| Hung-test detection | g105 sat idle for 52 min. Record the last-progress time per chunk (diag mtime) in the heartbeat; the supervisor's `hangSuspected` needs 20 idle min of *all* files |
| client / scripts per-test times | Vitest JUnit reporter; per-script wall in scripts-all.timing.json |

## 6. Ten changes ranked by estimated savings for the next Full run

| # | Change | Est. saving (min) | Basis | Risk |
|---:|---|---:|---|---|
| 1 | Let serial `ProcessSpawnLimit` chunks overlap: either `nativeActiveMax` 2 for antiphon, or Limit 2 | **est. 150–330** | 657.5 min of serial chunks; tree CPU about 0.1–0.4 cores. The upper bound assumes perfect 2× | **High.** Limit=1 exists to stop process-spawn contention (AGENTS.md: do not co-schedule at full width). Needs the load measurement in §5 first |
| 2 | Remove the post-session linger | **est. 60–110** | 114.2 min measured; cause unconfirmed (telemetry opt-out is one cheap candidate) | Low once the cause is known; it needs the Observation persistence first |
| 3 | Run e2e splits 2-wide, or shorten the AgentTaskLandDeliveryE2E crash matrices (53 tests, 125.6 min) | **est. 60–100** | e2e 220.9 min at 0.95 parallelism | Medium–high: E2E runner and port isolation |
| 4 | Do not recur the g105 hang; lower the antiphon chunk watchdog from 60 min to about 2× the largest healthy chunk (about 35–40 min) | **52 now; ≤ 25 per future hang** | measured idle 52.4 min; largest healthy chunk 32.3 min | Low. Fix is already on rc per the inventory doc; the watchdog change risks killing a slow but healthy chunk |
| 5 | Merge single-test profile chunks (antiphon 73, pty-host 48, markdown-pdf 11, agents-pty 6) into a few chunks | **est. 35–45** | 138 chunks × about 20 s fixed overhead; the wall of 79 min versus a test span of 14.5 min | Medium: profile chunks exist for isolation, and the merge must keep per-test exactness |
| 6 | Speed up the top 10 tests (854, 479, 420, 310 s and others) | **est. 20–40** | top 10 sum to 62.7 min; the saving is unknown until each test is profiled | Medium; test-by-test work |
| 7 | Replace fixed 30 s and 60 s waits with signals | **est. 15–25** | 45 tests at 30.0 s (22.5 min) and 10 at 60 s (10 min); not verified to be waits | Low–medium |
| 8 | Fix the 36 failing tests, especially the two 5-min budget failures | **est. 10–25** | Failed durations sum to 25.7 min | Low; tracked in the inventory doc |
| 9 | Share the ~14 s antiphon pre-first-test setup across chunks, or prebuild it | **est. 20–40** | 47.1 min over 190 chunks; unattributed | Medium; needs the fixture stamps first |
| 10 | Run the client and scripts lanes beside the .NET suites | **est. 8** | client 6.3 + scripts 1.8 min | Low: different resources |

These savings overlap; for example, #1 and #5 both shrink antiphon wall, so they do not add. A conservative combined estimate,
without #1 and #3, is about **150–250 min** off 1378. **est.**

## Not done, noted

- A fix design is out of scope. One line: persist the owner observation and per-suite plan, measure the linger cause, then trial `nativeActiveMax` 2 on a Debug run before any policy change.
- Group-105 per-test durations from its diag were not extracted.
