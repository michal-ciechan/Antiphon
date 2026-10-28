# Measured per-class cost table for the next qualification candidate

Task 44a6e14e, 2026-10-28, Investigate (read-only, native Windows desktop). I ran no test, build or dotnet command
and did not touch the live run, `C:\Antiphon\releases` or `native-run.lock`. Every figure below comes from stored
journals and plan files, which I parsed with streaming PowerShell (`StreamReader` plus regex). Git use was
`git show` of committed files only. The live run was read up to about 05:41Z: group 35 had finished and group 36
had just started. I did not parse group 35's journal, so it appears below from 8f055f87.

Data file: `docs/investigations/2026-10-28-measured-cost-rows.json`. It holds **702 rows** in the
`antiphon-nightly-class-costs-v1` schema (`classes: {name: {nodes, seconds, basis: "measured"}}`) and a parallel
`evidence` map with the source journal, chunk, completed/failed/in-flight counts, class span, both prices, ratio
and the other run's figure. The planner reads only `nodes` and `seconds`, so `evidence` must be dropped when
the rows are merged.

## Verdict

**Confirmed: the planner's model price (0.5 to 8 s per node) is the cause of every chunk overrun. No measured
class is large enough to beat the watchdog when it runs alone.** I recomputed the price of every class from
discovery categories, the node model (`scripts/lib/nightly-tests-impl.ps1:127-214`, identical at f84228f2 and this branch) and the candidate's
64-row cost file (`f84228f2:scripts/lib/nightly-class-costs.json`). The result reproduces all 181
`plannedSeconds` in `groups-planned.json` exactly (0 mismatches). I re-implemented the packer
(`:216-240`, `:278-314`, `:316-428`): 900 s target, first-fit largest first, at most 30 operands, a class over 2,400 s split
by method, prefix-unsafe names alone. With the candidate's prices it reproduces the live plan: 81 of 82
`group-*` chunks at an identical 41,057 s total, and all 27 `split-*` chunks at 17,454 s.

- Overruns: **group 28** (79b742de, killed at 3,608 s, priced 900), **group 30** (79b742de, killed at
  3,605 s, priced 874.5) and **group 65** (8f055f87, killed at about 3,596 s, priced 900). No other measured
  chunk passed 60% of 3,600 s. The closest were 8f055f87 group 26 at 2,091 s and 79b742de group 26 at 1,570 s.
- 702 classes measured above 1.5x their current price, or above it at all inside a chunk over 2,160 s.
  Together they total about 99,000 summed test-seconds against about 48,300 s for the whole priced plan at
  master.
- With these rows the packer makes **131 `group-*` launches instead of 87** (master) or 81 (candidate). The
  largest launch price stays 1,980 s (the existing `AgentTaskLandPersistenceFailureTests` row). No class newly
  passes the 2,400 s split threshold, and none comes near 3,600 s. Flag: none.

## Sources and method

- **(A) live run 79b742de** at `rc-2026-10-27-f84228f285dd\logs\timing-79b742de44354190bb55fd35914496f3\`:
  the `cases-*.jsonl` journals for groups 1-34, `groups-planned.json` (181 chunks with `plannedTestIds`,
  `plannedSeconds` and `pricingBasis`), and the `antiphon-all-group-N.timing.json` files (`runWallSeconds`, `why`).
- **(B) failed run 8f055f87** at `rc-2026-10-25-105792f7d22f\logs\2026-10-26-0946-8f055f87\timing\`. The brief
  said "no journals", but **this run has per-test journals**: `cases-*.jsonl` for groups 1-81, `split-1..26` and
  77 `profile-*` leaves. They are richer than TRX durations (start and end offsets per test), so I used them and
  did not need the TRX files. Its `groups-planned.json` was overwritten by the later observability suite (one
  `all` group), so 8f055f87 chunk prices are not stored. I used the 79b742de price for the chunk with the same
  id. In 33 of 48 such chunks the class set differs by 1-6 classes (marked `*` in the chunk table).
- **Seconds semantics** (same as the 9a16e2cb rows): `seconds` = Σ(endOffsetMs − startOffsetMs) over the
  class's **completed** tests in one journal, rounded. `nodes` = the completed count. 79b742de is preferred
  wherever it ran the class. 8f055f87 is used otherwise. This is summed serial test time. For classes whose
  tests overlap (span < half the summed seconds, marked `parallel`) it overstates wall time, as the existing
  rows already do.
- **Selection:** measured seconds > 1.5 × (price at master per node × completed nodes), **or** measured >
  price for a class in a chunk over 60% of 3,600 s (groups 28, 30, 65). "Price at master" is the price under
  the current `scripts/lib/nightly-class-costs.json` (76 rows, including 9a16e2cb). Rows whose measurement
  would *lower* a price are omitted, because they would only shrink packing margins.
- **Two reconstructed rows (killed mid-class):**
  - `ChannelOutboundUnifiedTransportTests`: 79b742de completed 10 of 17 (792.9 s). The same 10 took 580.6 s in
    8f055f87 (1.37x), and the other 7 took 354.4 s there. 792.9 + 354.4 × 1.37 gives **17 nodes / 1,277 s**.
  - `BlockedTaskParkReclaimTests`: 15 of 38 done (624.7 s). The same 15 took 579.0 s in 8f055f87 (1.08x), and
    the remaining 23 took 327.8 s. That gives **38 / 978 s**. Under the completed-denominator rule the row
    would read 15 / 625, which prices the class at 1,582 s because its slow cases run first.
- **Rows that replace existing rows:** `PostLandMutationCustody` 1,225 (was 1,110),
  `BlockedTaskParkDelivery` 611 (575), `RepairSourceSettlement` 520 (491), `DelegateBranchDelivery` 371
  (334), `RepairSourceDispatch` 337 (302), `SessionRuntimeActionRouting` 115 (112), and
  `BlockedTaskParkReclaim` 978 (907). All come from 79b742de, which was 2-11% slower than 8f055f87 on the same
  tests.
- **Excluded (4):** classes measured only as one-test `profile-*` leaves, where each test pays its own
  fixture start-up of about 20 s. These are `AgentTaskDispatchBaseGuardTests` (95 leaves, 1,136 s; the
  existing row is 92 / 497), `AgentTaskDispatchFailureTests`, `RunnerGrokAdapterReadyTestsPty` and
  `TestDbFixtureLazyInitializationTests` (17 of 35 failed). Exact leaves are not packed, so their price does
  not affect any chunk.
- **Failed tests:** 51 rows include tests that failed in 8f055f87, which was a failed run (groups 61-77 had
  21-95 failures each). Their `evidence.failed` is non-zero. A failing test can end early, so those rows may
  understate the class.

## (1) Rows: the 40 largest (all 702 are in the JSON)

Price at master = node model or existing row × planned nodes. Ratio = measured ÷ (price per node × completed
nodes).

| class | chunk | nodes | seconds | price at master | ratio | run | note |
|---|---|---|---|---|---|---|---|
| Application.AgentTaskLandRemovalMatrixTests | group-23 | 52 | 1415 | 416 | 3.4 | 79b742de | parallel |
| Application.AgentTaskLandCleanupSafetyTests | group-22 | 44 | 1288 | 352 | 3.66 | 79b742de | parallel |
| Application.ChannelOutboundUnifiedTransportTests | group-30 | 17 | 1277 | 26 | 52.86 | 79b742de | reconstructed |
| Application.PostLandMutationCustodyTests | group-28 | 56 | 1225 | 1110 | 1.1 | 79b742de | replaces measured row |
| Application.BlockedTaskParkReplyAdmissionTests | group-26 | 63 | 1068 | 94 | 11.3 | 79b742de |  |
| Application.BlockedTaskParkReclaimTests | group-28 | 38 | 978 | 907 | 1.74 | 79b742de | replaces measured row; reconstructed |
| Application.TerminalRunnerSeatReleaseTests | group-32 | 39 | 917 | 20 | 47.04 | 79b742de | 1 failed; parallel |
| Application.AgentTaskLandBoundaryTests | group-21 | 38 | 866 | 304 | 2.85 | 79b742de | parallel |
| Application.ChannelOutboundUnifiedCrashTests | group-30 | 21 | 837 | 32 | 26.58 | 79b742de |  |
| Application.LandGraphEvaluationTests | group-24 | 104 | 826 | 104 | 7.94 | 79b742de | 1 failed |
| Application.BlockedTaskParkReleaseTests | group-43 | 3 | 735 | 4 | 163.36 | 8f055f87 |  |
| Application.AgentTaskPipelineStatusTests | group-30 | 69 | 730 | 34 | 21.16 | 79b742de | parallel |
| Application.AgentTaskLandBareRepositoryCleanupTests | group-33 | 15 | 725 | 15 | 48.33 | 79b742de | parallel |
| Application.AgentTaskLandAdoptionTests | group-31 | 21 | 723 | 21 | 34.43 | 79b742de | parallel |
| Application.AgentTaskLandRecoveryTests | group-31 | 22 | 673 | 22 | 30.6 | 79b742de |  |
| Application.PostLandMutationWorktreeTests | group-30 | 33 | 673 | 33 | 20.38 | 79b742de |  |
| Application.AgentTaskLandRefusedRetryTests | group-33 | 15 | 638 | 15 | 42.53 | 79b742de | parallel |
| Application.BlockedTaskParkDeliveryTests | group-28 | 6 | 611 | 575 | 1.06 | 79b742de | replaces measured row |
| Scripts.ChangeAwareReleaseTests | group-77 | 3 | 569 | 2 | 379.6 | 8f055f87 | 1 failed |
| Application.AttentionServiceTests | group-19 | 167 | 569 | 250 | 2.27 | 79b742de | parallel |
| Application.AgentTaskReplyIntegrationTests | group-25 | 341 | 562 | 170 | 3.3 | 79b742de |  |
| Application.PostLandMutationAdmissionTests | group-30 | 27 | 561 | 27 | 20.79 | 79b742de |  |
| Application.BlockedTaskParkResumeTests | group-36 | 7 | 553 | 10 | 52.64 | 8f055f87 |  |
| Application.CodexCliObservationTests | group-45 | 8 | 529 | 4 | 132.18 | 8f055f87 |  |
| Application.RepairSourceSettlementTests | group-28 | 36 | 520 | 491 | 1.06 | 79b742de | replaces measured row |
| Application.CheckpointSourceApprovalTests | group-35 | 4 | 491 | 12 | 40.93 | 8f055f87 |  |
| Application.WorktreeResidueRecoveryTests | group-36 | 22 | 487 | 11 | 44.31 | 8f055f87 |  |
| Application.AgentTaskLandAdoptionConcurrencyTests | group-38 | 7 | 483 | 7 | 69.06 | 8f055f87 | parallel |
| Application.CardDoneWorktreeCleanupTests | group-36 | 20 | 477 | 10 | 47.67 | 8f055f87 | parallel |
| Application.AgentTaskLandPreparationIdentityTests | group-33 | 16 | 473 | 16 | 29.54 | 79b742de | parallel |
| Application.AgentTaskLandStageOutcomeTests | group-33 | 14 | 451 | 14 | 32.21 | 79b742de |  |
| Application.AgentTaskLandNestedRepositoryCleanupTests | group-37 | 8 | 448 | 8 | 55.94 | 8f055f87 | parallel |
| Application.AgentTaskLandTargetRaceTests | group-35 | 11 | 446 | 11 | 40.55 | 8f055f87 | parallel |
| Application.RunnerSettlementSyncTests | group-33 | 30 | 441 | 15 | 29.37 | 79b742de | parallel |
| Application.CodexCliObservationGapTests | group-36 | 7 | 439 | 10 | 41.77 | 8f055f87 | 1 failed |
| Application.RunnerTaskSettlementTests | group-29 | 32 | 424 | 48 | 8.84 | 79b742de | parallel |
| Scripts.ReleaseTagAllocatorTests | group-76 | 7 | 407 | 4 | 116.37 | 8f055f87 |  |
| Application.PipelineSpeedIntegrationTests | group-31 | 39 | 403 | 20 | 20.65 | 79b742de |  |
| Application.AgentTaskLandSourcePersistenceTests | group-30 | 35 | 395 | 35 | 11.27 | 79b742de | parallel |
| Application.AgentTaskLandIndexLockTests | group-32 | 19 | 376 | 19 | 19.76 | 79b742de |  |

Rows by chunk of origin (summed seconds): g28 10 rows / 4,377 s; g30 24 / 7,262; g31 19 / 3,648; g33 18 / 4,291;
g36 16 / 3,020; g32 14 / 2,572; g26 5 / 1,848; g29 10 / 1,863; g34 16 / 1,605; g43 17 / 1,596; g35 14 / 1,542;
g37 16 / 1,498; g23 2 / 1,425; g40 17 / 1,321; g22 1 / 1,288; g38 20 / 1,279; g77 23 / 1,279; g76 19 / 1,251;
g27 7 / 1,235; g25 4 / 1,179; g61 9 / 1,067; g21 2 / 963; g65 7 / 960; 41 other chunks under 950 s each.

Group 65 contributes only 7 rows because 9a16e2cb already priced its heavy classes
(`WorktreeGuardedCleanup`, `CompletedCardWorktreeRemoval`, `LandingRemovalControl`, `SettledWorktreeRemoval`
and others). The measured 8f055f87 figures do not exceed those rows by 1.5x.

## (2) Per-chunk table (every `group-*` chunk)

Source: 79b742de for groups 1-34, 8f055f87 for 35-82 (`*` = class set differs from the 79b742de plan). Wall is
`runWallSeconds` for 79b742de and the journal's last test end for 8f055f87, which excludes start-up of about
4 s. Summed test-s is the serial test time in that run. The "own re-pack" column packs only that chunk's
classes at the new prices: launches and the largest launch price. Verdicts: overrun = killed or ≥ 3,600 s;
at risk = over 2,160 s; ok = the rest. The 79b742de group 35 `runWallSeconds` is 688.9 s (journal not parsed).

| chunk | planned s (basis) | source | tests done/planned | wall s | summed test-s | wall/price | verdict | new price | own re-pack: launches (max price) |
|---|---|---|---|---|---|---|---|---|---|
| group-1 | 60 (model) | 79b742de | 94/94 | 74 | 66 | 1.2 | ok | 92 | 1 (max 94) |
| group-2 | 230 (model) | 79b742de | 414/414 | 42 | 132 | 0.2 | ok | 289 | 1 (max 289) |
| group-3 | 43.5 (model) | 79b742de | 87/87 | 80 | 88 | 1.8 | ok | 109 | 1 (max 109) |
| group-4 | 11 (model) | 79b742de | 22/22 | 7 | 12 | 0.6 | ok | 11 | 1 (max 11) |
| group-5 | 202 (model) | 79b742de | 294/294 | 97 | 246 | 0.5 | ok | 312 | 1 (max 312) |
| group-6 | 22 (model) | 79b742de | 44/44 | 53 | 125 | 2.4 | ok | 65 | 1 (max 132) |
| group-7 | 110 (model) | 79b742de | 188/188 | 34 | 224 | 0.3 | ok | 266 | 1 (max 273) |
| group-8 | 1980 (measured) | 79b742de | 52/52 | 429 | 1,604 | 0.2 | ok | 1980 | 1 (max 1,980) |
| group-9 | 1920 (measured) | 79b742de | 16/16 | 1,198 | 1,181 | 0.6 | ok | 1920 | 1 (max 1,920) |
| group-10 | 1808.3 (observed-bound) | 79b742de | 56/56 | 341 | 1,287 | 0.2 | ok | 1808 | 1 (max 1,808) |
| group-11 | 1662 (observed-bound) | 79b742de | 67/67 | 1,325 | 1,308 | 0.8 | ok | 1662 | 1 (max 1,662) |
| group-12 | 1560 (pace) | 79b742de | 2/2 | 91 | 79 | 0.1 | ok | 1560 | 1 (max 1,560) |
| group-13 | 1560 (pace) | 79b742de | 41/41 | 286 | 1,038 | 0.2 | ok | 1560 | 1 (max 1,560) |
| group-14 | 1398 (observed-bound) | 79b742de | 46/46 | 954 | 939 | 0.7 | ok | 1398 | 1 (max 1,398) |
| group-15 | 1218 (observed-bound) | 79b742de | 94/94 | 47 | 867 | 0 | ok | 1218 | 1 (max 1,218) |
| group-16 | 1000 (model) | 79b742de | 125/125 | 129 | 113 | 0.1 | ok | 1000 | 1 (max 1,000) |
| group-17 | 948 (observed-bound) | 79b742de | 20/20 | 691 | 674 | 0.7 | ok | 948 | 1 (max 948) |
| group-18 | 899.5 (mixed) | 79b742de | 88/88 | 369 | 440 | 0.4 | ok | 977 | 2 (max 890) |
| group-19 | 900 (mixed) | 79b742de | 232/232 | 279 | 830 | 0.3 | ok | 1230 | 2 (max 660) |
| group-20 | 900 (mixed) | 79b742de | 112/112 | 626 | 814 | 0.7 | ok | 1090 | 2 (max 878) |
| group-21 | 899.5 (mixed) | 79b742de | 91/91 | 440 | 1,316 | 0.5 | ok | 1462 | 2 (max 866) |
| group-22 | 900 (mixed) | 79b742de | 72/72 | 423 | 1,568 | 0.5 | ok | 1836 | 2 (max 1,288) |
| group-23 | 900 (mixed) | 79b742de | 154/154 | 488 | 1,772 | 0.5 | ok | 1899 | 2 (max 1,415) |
| group-24 | 899.5 (mixed) | 79b742de | 174/174 | 1,146 | 1,320 | 1.3 | ok | 1622 | 2 (max 830) |
| group-25 | 900 (mixed) | 79b742de | 524/524 | 1,397 | 1,382 | 1.6 | ok | 1596 | 2 (max 862) |
| group-26 | 899.9 (mixed) | 79b742de | 503/503 | 1,570 | 1,923 | 1.7 | ok | 2322 | 3 (max 1,068) |
| group-27 | 900 (model) | 79b742de | 480/480 | 802 | 1,578 | 0.9 | ok | 1706 | 2 (max 871) |
| group-28 | 900 (model) | 79b742de | 491/704 | 3,608 kill | 4,050 | 4 | overrun | 4811 | 5 (max 1,225) |
| group-29 | 900 (model) | 79b742de | 1037/1037 | 577 | 2,068 | 0.6 | ok | 2334 | 3 (max 890) |
| group-30 | 874.5 (model) | 79b742de | 876/954 | 3,605 kill | 6,822 | 4.1 | overrun | 7444 | 8 (max 1,277) |
| group-31 | 674.5 (model) | 79b742de | 931/931 | 1,485 | 3,767 | 2.2 | ok | 3898 | 5 (max 900) |
| group-32 | 535.5 (model) | 79b742de | 775/775 | 946 | 2,690 | 1.8 | ok | 2856 | 4 (max 917) |
| group-33 | 451.5 (model) | 79b742de | 688/688 | 1,048 | 4,345 | 2.3 | ok | 4472 | 5 (max 899) |
| group-34 | 389.5 (model) | 79b742de | 632/632 | 654 | 1,688 | 1.7 | ok | 1790 | 2 (max 900) |
| group-35 | 351.5 (model) | 8f055f87* | 555/545 (8 failed) | 632 | 1,609 | 1.8 | ok | 1730 | 2 (max 899) |
| group-36 | 302.5 (model) | 8f055f87* | 520/533 (10 failed) | 1,646 | 3,069 | 5.4 | ok | 3160 | 4 (max 899) |
| group-37 | 263 (model) | 8f055f87* | 441/434 (8 failed) | 402 | 1,520 | 1.5 | ok | 1622 | 2 (max 896) |
| group-38 | 229.5 (model) | 8f055f87* | 415/424 (1 failed) | 176 | 1,308 | 0.8 | ok | 876 | 1 (max 880) |
| group-39 | 204 (model) | 8f055f87* | 372/366 | 197 | 1,032 | 1 | ok | 1544 | 2 (max 896) |
| group-40 | 182.5 (model) | 8f055f87 | 315/315 | 522 | 1,350 | 2.9 | ok | 1396 | 2 (max 900) |
| group-41 | 167.5 (model) | 8f055f87 | 319/319 | 225 | 657 | 1.3 | ok | 720 | 1 (max 720) |
| group-42 | 152.5 (model) | 8f055f87 | 280/280 (1 failed) | 167 | 837 | 1.1 | ok | 872 | 1 (max 872) |
| group-43 | 139.5 (model) | 8f055f87 | 261/261 | 819 | 1,614 | 5.9 | ok | 1656 | 2 (max 899) |
| group-44 | 127 (model) | 8f055f87 | 246/246 | 318 | 733 | 2.5 | ok | 768 | 1 (max 774) |
| group-45 | 120 (model) | 8f055f87 | 224/224 | 632 | 874 | 5.3 | ok | 905 | 2 (max 897) |
| group-46 | 111.5 (model) | 8f055f87 | 219/219 | 104 | 523 | 0.9 | ok | 564 | 1 (max 564) |
| group-47 | 105 (model) | 8f055f87 | 210/210 | 208 | 607 | 2 | ok | 653 | 1 (max 653) |
| group-48 | 90.5 (model) | 8f055f87 | 167/167 | 79 | 411 | 0.9 | ok | 430 | 1 (max 430) |
| group-49 | 90 (model) | 8f055f87 | 154/154 (5 failed) | 373 | 738 | 4.1 | ok | 762 | 1 (max 762) |
| group-50 | 77.5 (model) | 8f055f87 | 155/155 | 91 | 306 | 1.2 | ok | 330 | 1 (max 331) |
| group-51 | 72 (model) | 8f055f87 | 142/142 (1 failed) | 107 | 575 | 1.5 | ok | 588 | 1 (max 598) |
| group-52 | 60 (model) | 8f055f87* | 114/114 | 69 | 313 | 1.2 | ok | 321 | 1 (max 326) |
| group-53 | 55.5 (model) | 8f055f87* | 108/109 | 56 | 318 | 1 | ok | 354 | 1 (max 359) |
| group-54 | 45 (model) | 8f055f87* | 88/88 (3 failed) | 76 | 300 | 1.7 | ok | 298 | 1 (max 302) |
| group-55 | 39.5 (model) | 8f055f87 | 79/79 | 96 | 326 | 2.4 | ok | 333 | 1 (max 341) |
| group-56 | 30 (model) | 8f055f87 | 54/54 | 64 | 165 | 2.1 | ok | 171 | 1 (max 174) |
| group-57 | 24.5 (model) | 8f055f87 | 48/48 (2 failed) | 74 | 279 | 3 | ok | 278 | 1 (max 285) |
| group-58 | 15 (model) | 8f055f87 | 30/30 (1 failed) | 79 | 91 | 5.3 | ok | 94 | 1 (max 96) |
| group-59 | 6.5 (model) | 8f055f87 | 13/13 | 28 | 37 | 4.3 | ok | 34 | 1 (max 39) |
| group-60 | 17.5 (model) | 8f055f87 | 35/35 | 2 | 2 | 0.1 | ok | 18 | 1 (max 18) |
| group-61 | 513.5 (model) | 8f055f87* | 444/445 (95 failed) | 785 | 1,179 | 1.5 | ok | 1298 | 2 (max 900) |
| group-62 | 157 (model) | 8f055f87* | 201/211 (26 failed) | 440 | 542 | 2.8 | ok | 674 | 1 (max 674) |
| group-63 | 46 (model) | 8f055f87* | 64/75 (21 failed) | 222 | 333 | 4.8 | ok | 442 | 1 (max 442) |
| group-64 | 30.5 (model) | 8f055f87 | 61/61 | 0 | 0 |  | ok | 30 | 1 (max 30) |
| group-65 | 900 (model) | 8f055f87 | 960/975 (14 failed) | 3,596 kill | 5,066 | 4 | overrun | 5496 | 6 (max 1,593) |
| group-66 | 194.5 (model) | 8f055f87 | 319/319 (36 failed) | 389 | 658 | 2 | ok | 756 | 1 (max 756) |
| group-67 | 49.5 (model) | 8f055f87 | 91/91 (3 failed) | 43 | 160 | 0.9 | ok | 176 | 1 (max 176) |
| group-68 | 3 (model) | 8f055f87 | 6/6 | 14 | 2 | 4.7 | ok | 4 | 1 (max 4) |
| group-69 | 27 (model) | 8f055f87 | 54/54 | 16 | 27 | 0.6 | ok | 43 | 1 (max 43) |
| group-70 | 8.5 (model) | 8f055f87 | 17/17 | 27 | 70 | 3.2 | ok | 70 | 1 (max 70) |
| group-71 | 1933.3 (observed-bound) | 8f055f87 | 30/30 (8 failed) | 1,134 | 1,134 | 0.6 | ok | 1933 | 1 (max 1,933) |
| group-72 | 1420 (observed-bound) | 8f055f87 | 26/26 | 884 | 884 | 0.6 | ok | 1420 | 1 (max 1,420) |
| group-73 | 899.8 (mixed) | 8f055f87 | 60/60 (3 failed) | 558 | 558 | 0.6 | ok | 926 | 2 (max 881) |
| group-74 | 900 (mixed) | 8f055f87 | 116/116 (43 failed) | 572 | 560 | 0.6 | ok | 927 | 2 (max 892) |
| group-75 | 900 (mixed) | 8f055f87* | 248/250 (41 failed) | 851 | 851 | 0.9 | ok | 984 | 2 (max 895) |
| group-76 | 262 (mixed) | 8f055f87 | 231/232 (22 failed) | 1,360 | 1,369 | 5.2 | ok | 1462 | 2 (max 899) |
| group-77 | 57 (model) | 8f055f87* | 107/108 (10 failed) | 1,107 | 1,280 | 19.4 | ok | 1282 | 2 (max 900) |
| group-78 | 13 (model) | 8f055f87* | 23/26 (3 failed) | 97 | 113 | 7.5 | ok | 130 | 1 (max 130) |
| group-79 | 191.5 (model) | 8f055f87* | 292/299 | 441 | 644 | 2.3 | ok | 739 | 1 (max 739) |
| group-80 | 1 (model) | 8f055f87* | 1/2 | 1 | 1 | 1 | ok | 3 | 1 (max 3) |
| group-81 | 10 (conservative-bound) | 8f055f87* | 7/1 | 1 | 3 | 0.1 | ok | 10 | 1 (max 10) |
| group-82 | 3.5 (model) | - | 0/7 | - | 0 |  | unmeasured | 4 | 1 (max 4) |

**Split chunks (by class, since 8f055f87's split numbering differs from 79b742de's):**

| class | 79b742de split ids, price | 8f055f87 measured | verdict |
|---|---|---|---|
| AgentTaskLandPublicationTests | split-1..5, 3,840 s | 64/64 tests, 1,218 s, max split wall 413 | ok, over-priced |
| DelegationDispatchRecoveryBoundaryTests | split-6..11, 3,505 s | 381/381, 1,768 s summed (parallel), max wall 34 | ok, over-priced |
| LandNonCodeSkipTests | split-12..18, 5,518 s | 104/111 done, 12 failed, 1,744 s, max wall 551 | ok, over-priced |
| RemoteScriptContractTests | split-19..27, 4,591 s (pace 9.2 s/node) | 499 done, **241 failed**; one 132-test split failed all 132 in 0.1 s | **unmeasured** |

**Profile leaves:** 66 of the 79b742de `profile-*` ids ran in 8f055f87, all 1-8 tests each. The largest wall was
64 s. Their priced total is 523 s against 1,459 s measured, which is the start-up per leaf. Verdict: ok.

The group 30 investigation flagged split-19 (`RemoteScriptContractTests`) as a medium risk at about 2,450 s.
No stored measurement supports or refutes that. The 8f055f87 split with the same 132-case size failed every
case at once. The passing RemoteScriptContract cases in 8f055f87 took 962.7 s for 258 nodes (3.7 s/node). That
is a lower bound and sits below the 9.2 s/node pace row, which stands.

## (3) Not covered by either source

- **Chunks:** `group-82` (TimeoutAuditTests, 7 tests, 3.5 s: never run under that id, but the class was
  measured in 8f055f87 group 81 at 3 s), `split-27` (RemoteScriptContractTests, see above) and `profile-1471`
  to `profile-1476` (TestDbFixtureLazyInitializationTests; its only measurement had 17 of 35 failed). Their
  prices are still unmeasured.
- **Classes never completed in either run (11, model price 267 s in total):** group 28 holds
  `LandEvidenceWarningDeliveryTests` (7 nodes, 56 s), `ChannelInboundRecoveryTests` (41, 61.5),
  `ApiErrorRecoveryServiceTests` (40, 60) and `CodexStartupDeliveryTests` (62, 62). These are queued behind
  the kill in both runs. The rest are new classes: group 62 `CheckpointWindowsReadModeTests` (6),
  `CheckpointWindowsAnchoredDeleteTests` (7); group 63 `UnsafeDeleteRecoveryTests` (2.5),
  `CheckpointRecoveryFixtureTests` (2); group 77 `ScriptsLaneRepairTests` (2.5); group 79
  `WindowsDeliveryFixtureWitnessTests` (6), `LiveFixtureTextTests` (1.5). Groups 35-81 have only the
  8f055f87 measurement (source 105792f7, many failures in 61-77); their rows carry `evidence.run = 8f055f87`.

## (4) Re-pack check

I simulated the whole `ConvertTo-NightlyProfileLaunchGroups` pack (all namespaces, operand cap, prefix-unsafe
names, 2,400 s split) at three price sets:

| price set | `group-*` launches | priced total | split launches | largest launch price |
|---|---|---|---|---|
| candidate f84228f2 (64 rows) | 81 (live plan: 82) | 41,057 s | 27 | 1,980 |
| master incl. 9a16e2cb (76 rows) | 87 | 48,319 s | 27 | 1,980 |
| master + these 702 rows | **131** | 99,054 s | 27 | 1,980 |

- Per affected chunk, the "own re-pack" column above shows the split: g30 into 8 launches (largest 1,277:
  Transport alone), g28 into 5 (largest 1,225: Custody alone), g65 into 6 (largest 1,593: the existing
  WorktreeGuardedCleanup row), g31 and g33 into 5 each, g36 and g32 into 4, and most 35-79 model chunks into 2.
- **No class alone exceeds the watchdog.** 21 launches price above 900 s, each a single class from 917 to
  1,980 s. The largest new one is `AgentTaskLandRemovalMatrixTests` at 1,415 s, which runs parallel (wall
  well below that). No row pushes a class past the 2,400 s split threshold. The largest single method
  measured is `AgentTaskLandActiveSourceClaimTests.C448_V29_ActiveSourceClaimHoldsEveryMode` at 1,181 s for
  16 cases, and the largest single test is 452 s (`BlockedTaskParkReleaseTests`). A 1.4x slow night
  (79b742de against 8f055f87 reached 1.37x) takes the largest launch to about 2,770 s. That is under 3,600 s.
  No class needs a test-filter split or a longer budget.
- The four already-split classes are priced at 2-3x their 8f055f87 measurement (RemoteScriptContract unmeasured).
- **Cost of the extra launches:** per-launch overhead in 79b742de is about 5 s. `runWallSeconds` is about 4 s
  over the journal's last end, and the next chunk starts within 1 s. 44 more launches add about 4 minutes of
  overhead. Packing by summed test-seconds also loses in-chunk parallelism. Groups 1-34 of 79b742de needed
  25,344 s of wall for 48,356 summed test-seconds (1.9x overlap). Some of that overlap is lost when parallel
  classes land in separate sequential launches. I did not estimate the resulting suite wall time.

## Remaining uncertainty

- 79b742de ran 2-37% slower than 8f055f87 on identical tests. The group 30 report found no stored cause. Rows
  taken from 8f055f87 (groups 35-81) may therefore be low for a slow night. None is close to a limit after
  re-pack.
- 8f055f87 chunk prices are not stored, so the same-id 79b742de price stands in for them.
- Classes the planner sees under a constructor-argument name (`Foo(TestDbFixture)`) were normalised to the bare
  name, as the journal and cost file use it. The exact price reproduction shows this matched the planner.

## Not done, noted

- Fix idea (not designed): merge the `classes` map of the JSON into `scripts/lib/nightly-class-costs.json`
  (replacing the 7 overlapping rows) before the next candidate is cut, then re-run the planner's dry plan to
  confirm 131 launches. RemoteScriptContractTests and the 11 never-completed classes need a clean measurement.
