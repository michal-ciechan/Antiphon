# CARD-0824 S5b-1 performance Debug: the run over 30 minutes and what Code owes

Debug task 5ae98d31 (R1 of [test-performance-standards](../test-performance-standards.md)),
commissioned for Code task f8ac5aa5. This is a read-only analysis. No build, test, product
or test-source change was made. Evidence was read from the Code task's retained receipts on
server2 (`/work/worktrees/task-f8ac5aa5/.antiphon/checkpoints/<run>/`), its report
`.antiphon/task-f8ac5aa5.md`, the TestDesign
`docs/superpowers/testdesign/2026-11-05-card-0824-s5b-1-testdesign.md`, the Plan's
`## Performance budget`, and other tasks' guard-row TRX on the same host.

Subject: branch `feat/card-task-f8ac5aa5`, base `0ad07cad8eae97c0eb390994ee968f9faa7a527e`,
tip `0626344fbf1739809a4b079237b40c4f6b3028fa`. Last production commit
`089b47ab065868b13556b9091ce1c24d225b12f8`. Later commits change tests only.

## Verdict

Only one selection went over 30 minutes: run `20261105-130833-11b7` (CP-3, CP-4 and CP-5 at
`7973604a2908febc17d80220937f9232da4b2054`), at **30m49s** wall. Tests account for very
little of that time. Admission to the build slot took 45% of the wall time and one cold
`Antiphon.Tests` build took 44%. The three test hosts together took 6.8%, and the test
bodies themselves took 3.1% (57 s). No test, fixture or filter is slow. The overrun comes
from three things together:

1. Every row takes a new build-slot lease. Each new lease goes to the back of a FIFO queue
   that already had 2-11 waiters ahead of it, on a broker with 4 slots and all 4 held.
2. A cold, isolated checkpoint build ran on a host that was already over-subscribed: 24
   cores, 15-minute load 29.35, `-maxcpucount:6`.
3. The run had three serial rows, so it paid four separate admissions.

Code can do nothing in this slice to make the build-slot host faster. What Code can control
is how many runs, builds and lease acquisitions it uses, and how its budgets are written
down. Those obligations are listed under [Obligations for Code](#obligations-for-code).

## The over-30-minute run, phase by phase

Receipt: `.../20261105-130833-11b7/report.json`. Its verdict is RED, as intended: it was the
assertion-red run before fix 089b47ab. sourceState=clean, buildSource=verified. Host at start:
Debian 12, 24 cores, loadAvg 12.03 / 20.52 / 29.35, broker `http://build-slots:8080`, budget
4, maxcpucount 6. Manifest: `build.slotWaitMinutes: 12`, `timeouts.rowMinutes: 20`, every row
`timeoutMinutes: 8`, total timeout 46m, hard wall 90m.

| UTC (executor.log) | Phase | Seconds | Note |
|---|---|---:|---|
| 13:08:36 -> 13:09:18 | tool preparation before admission (ledger stamp, source capture) | 41.5 | counted as execution, but has no named phase |
| 13:09:18 -> 13:11:03 | build admission | 105 | 4/4 occupied, queue position 5 -> 2 |
| 13:11:03 -> 13:24:12 | cold build lease held | 789 | `PHASES build=817.1s` includes the post-lease seal |
| 13:24:12 -> 13:25:03 | post-build seal/verification, then hand-off to the row | 51 | about 28 s is inside the build phase; about 23 s is tool remainder |
| 13:25:03 -> 13:31:48 | CP-3 admission | **405** | joined the queue at position 7 |
| 13:31:48 -> 13:32:38 | CP-3 host: startup 19.7, tests 18.7, teardown 9.0 | 49.5 | |
| 13:32:38 -> 13:36:23 | CP-4 admission | **225** | joined the queue at position 11 |
| 13:36:23 -> 13:37:01 | CP-4 host: startup 10.4, tests 22.6, teardown 3.7 | 37.9 | |
| 13:37:02 -> 13:38:47 | CP-5 admission | **105** | joined the queue at position 7 |
| 13:38:47 -> 13:39:25 | CP-5 host: startup 13.6, tests 15.8, teardown 7.3 | 37.5 | |
| 13:39:25 | finalization | 0.4 | |

Totals: wall 1,849.2 s = admission 840.2 s + execution 1,009.0 s. Execution breaks down as
build 817.1 + row hosts 124.9 + about 67 s of tool remainder. Admission was 2.2 times the
total test-host time, and the three row admissions alone (735 s) were 5.9 times the
row-host time (125 s). The report's `sequential-equivalent: 14m20s` is the run without
admission.

## All 14 runs of the Code task

| Run | Rows | Load (1/5/15) | Build s | Admission s | Wall s |
|---|---|---|---:|---:|---:|
| 105314-ccb2 | CP-1 | 9.8/17.0/19.6 | 431.6 | 0 | 488 |
| 111130-748d | CP-1 | 11.5/25.4/27.8 | 608.4 | 0 | 707 |
| 113402-c649 | CP-2 | 19.7/14.0/16.9 | 204.7 **failed** | 0 | 253 |
| 114029-b416 | CP-2 | 24.1/21.3/19.3 | 448.2 | 195 | 781 |
| 115628-5b20 | CP-2 | 20.3/30.4/29.6 | 361.3 | 0 | 454 |
| 121648-bd84 | CP-1, CP-2 | 34.1/25.9/27.4 | 480.7 | 345 | 1,045 |
| 123656-6222 | CP-3..5 | 24.3/25.3/28.7 | 473.4 **failed** | 180 | 698 |
| 125314-1718 | CP-3..5 | 31.8/34.4/35.0 | 421.5 **failed** | 255 | 745 |
| **130833-11b7** | CP-3..5 | 12.0/20.5/29.4 | **817.1** | **840** | **1,849** |
| 134550-2a55 | CP-3..5 | 29.8/27.7/32.0 | 639.9 | 0 | 836 |
| 140227-83f8 | CP-1..5 | 2.1/10.1/20.8 | 303.5 | 0 | 509 |
| 141345-df99 | CP-2, 4, 5 | 1.5/6.2/14.3 | 245.5 | 0 | 455 |
| 142332-6ae7 | CP-2, 4, 5 | 16.0/15.5/15.8 | 221.8 | 0 | 410 |
| 143500-90e9 | CP-2, CP-4 | 0.8/5.9/11.9 | 223.8 | 0 | 406 |

Across all 14 runs:

- **Total wall:** 9,634 s (160.6 min).
- **Builds:** 5,881 s (61%). The three compile failures account for 1,099 s of this.
- **Admission:** 1,815 s (19%).
- **Row hosts:** 1,205 s (12.5%), split as test bodies 615 s, startup 435 s and teardown 129 s.
- **Tool remainder:** about 733 s (7.6%), which is ledger stamps, verification and
  finalization; the last run's finalization was 63.9 s.

Measured distributions (server2, 2026-11-05, Code-task source SHAs; the samples are small,
so these are **provisional**):

- **Successful cold `Antiphon.Tests` checkpoint build:** n=11, p50 431.6 s, p95 = max
  817.1 s, min 221.8 s. Build time follows load: with load1 under 17 it was 222-304 s
  (n=4); with load1 of 20 or more it was 361-817 s. The Plan's comparator (loaded p50
  326 s / p95 579 s, n=117) is lower. Today's p95 is 1.41 times that comparator's p95.
  The 20-minute build cap still holds (2 x 579 s rounds up to it, and 817 s is under it).
- **Lease acquisition wait:** n=39 acquisitions, p50 0 s, mean 46.5 s, p95 255 s, max 405 s.
  The waits come in bursts: every non-zero wait fell between 11:40 and 13:40 UTC. The broker
  snapshot taken during this Debug (about 19:23 UTC) showed the same pattern again: 4/4
  occupied, waiters queued, load 29.4.
- **Row execution, host included** (from CHECKPOINT-TIMING `executed`; red runs included
  because their hosts did the same work):

  | Row | Samples (n) | Max execution | Current cap |
  |---|---:|---:|---:|
  | CP-1 | 4 | 85.0 s | 8 min |
  | CP-2 | 7 | 75.2 s | 8 min |
  | CP-3 | 3 | 49.5 s | 10 min |
  | CP-4 | 6 | 42.3 s | 8 min |
  | CP-5 | 5 | 75.5 s (a 60 s test timeout) | 8 min |

  Test-host startup was 10-22 s. Under load (748d, bd84) the CP-1 startup took 51-67 s.
- **Per-method maxima** (TRX, every attempt):

  | Methods | Max | Declared timeout |
  |---|---:|---|
  | Identity | 10.5 s | 60 s class timeout |
  | Gate | 2.2 s (the reload case) | 2 s methods / 15 s class |
  | L1..L5 | 19.2 s green, 24.2 s red | 60 s |
  | L6 | **39.6 s green** (n=1); red hit the timeout at 61.1 s | 60 s |
  | C1 | 21.3 s | 60 s |
  | C2 | 15.7 s green, 22.6 s red | 60 s |
  | C3 | 5.3 s | 60 s |

- **Guard row and CP-6** (none run by the Code task). The established filter
  `/*/*/(TestClassificationPolicy*)|(TestClassificationGuard*)|(SlowTestRegistryMerge*)|(SlowTestTripwire*)/*`
  executes 47 tests: 39 TestClassificationPolicy cases, 5 SlowTestRegistryMerge,
  2 SlowTestTripwire and 1 TestClassificationGuard. Comparable single-purpose rows by other
  server2 tasks on 2026-11-05 ran in 94.1 s, 123.9 s and 181.0 s of execution at load 12.8,
  25.2 and 28.7 (task-8a2a0e71 runs 1339 and c20e, task-c1e2a660 run 84c9). The two C508
  methods together take about 0.3 s of body time (task-dc12c438 land TRX), so CP-6 is
  essentially one host startup and teardown.

## Why, by candidate cause

| Candidate | Finding |
|---|---|
| Slot waits on a CPU-saturated host | **Primary.** It caused 840 of the 1,849 s, and every row re-entered the queue at positions 7-11. The broker has 4 slots. The standing caller authority keeps about 7 tasks on server2, so whenever 5 or more of them build or test at once, queueing is structural. |
| Slow class | **No.** The slowest class takes 18-45 s of body time, and the slowest method (L6) takes 40 s. |
| Missing setup share (R3) | **No defect.** The DB fixture and loopback host are shared once per test host. The only repeated cost is one host startup per row (10-22 s, up to 67 s under load). That is a consequence of the frozen packing of one filter per row, not a fixture defect. |
| Bad filter | **No.** Every row's TRX roster had exact identities with non-zero counts. No row expanded beyond its frozen methods. |
| Cold build cost | **Secondary.** Every checkpoint run builds into a fresh isolated output, taking 222-817 s depending on load. The 14 runs spent 98 minutes on builds, 18 of them on three builds that ended in compile errors. Each compile error was found only after a full cold build and its admission (25.6 min with admission). |
| Tool overhead | Minor: about 67 s per run (41.5 s before admission, about 23 s after the build). It has no named phase and appears as unattributed remainder. |

## Obligations for Code

These are **MUST** unless marked SHOULD. They turn this Debug's findings into the packing
and budgets the TestDesign deferred to it ("single-row staging is preferred if that
disposition requires it").

**P1. Do not re-pay greens for performance reasons.** Production has not changed since
089b47ab. After each row's green SHA, test sources changed only in CompletedCardMirrorFixture
(used by C2), the C2 body, the L2/L3 bodies and the L6 delivery and receipt files. CP-2 and
CP-4 were re-run green at the tip. CP-1/CP-3 (green at 5f36fcec) and CP-5 (green at 5a65f936)
have no source change in their own methods since those greens. Whether Review requires every
row at the exact HEAD is a caller/Review decision. This Debug does not require it. If it is
required, use P3's packing.

**P2. Remaining ordinary rows: one run, one build, two rows, at the exact committed HEAD.**

- Rows:
  - CP-6 with its frozen filter (2 methods).
  - The guard row with the filter above (expect executed=47; confirm against the TRX roster).
- Manifest settings:
  - `build.slotWaitMinutes: 12`.
  - `timeouts.rowMinutes: 20`, which keeps the build cap at 20 min under
    `RunScheduler.BuildDeadlineMinutes`.
  - Row `timeoutMinutes` CP-6 **2** and guard **6** (2 x 181 s rounds to 6, n=4,
    provisional CARD-0824).
  - All rows Serial. `TEMP=/tmp`. `UseAppHost=false`.
- Command budgets: `--total-timeout 28m` (20 build + 2 + 6) and `--hard-wall-timeout 64m`
  (28 + 3 acquisitions x 12).
- Expected wall time:
  - **About 15 min** at p50: build 432 s, 3 x 47 s admission, rows about 1 + 2.5 min, tool
    about 1.5 min.
  - **About 32 min** in an 11b7-like burst: build 817 s, 3 x 255 s admission, rows about
    4 min, tool about 1.5 min.

  Report admission separately from execution, as R5 and CARD-1531 require.
- Do not use `-NoSlot`, and do not hold one lease across rows. One lease per driver
  invocation remains the rule.

**P3. Never schedule a seven-acquisition serial run (a build plus CP-1..CP-6 plus guard).**
With the 11b7 queue (mean 245 s per row admission), the six row-admissions alone would take
about 29 min, before the build. If the caller or Review requires one at the exact HEAD,
choose one of these:

- **Preferred:** a caller-commissioned TestDesign amendment packs the 20 new methods into one
  row with
  `/*/Antiphon.Tests.Application/(C0824RefOnlyIdentityTests*)|(C0824RefOnlyGateTests*)|(C0824RefOnlyLandingTests*)|(C0824RefOnlyCleanupTests*)/*`.
  - The four classes contain exactly 7 + 4 + 6 + 3 = 20 methods, and no other class has
    these prefixes. That gives min/expect 20, one host startup, and an estimated 2.5-4 min of
    execution.
  - Add CP-6 and the guard row to the same run. That is four acquisitions in total.
  - The first such run measures whether L6 (60 s cap) stays within budget beside the other
    classes. If it times out, that is a red to report, not a cap to widen.
- Without the amendment: no run may contain more than a build plus three rows. Accept the
  extra cold build, which costs about 7 min at p50.

**P4 (SHOULD). Compile-check before a cold checkpoint build.**

- Three of the 14 runs (c649, 6222, 1718) lost 1,099 s of build and 435 s of admission to
  compile errors.
- After a source edit that has not been compiled, run one native incremental build first:
  `pwsh -NoProfile -File scripts/build-slot.ps1 -Label c0824-compile -- dotnet build tests/Antiphon.Tests -p:UseAppHost=false`.
  It uses native default output and one lease, and the CARD-1677 warm comparator is 143 s.
  Report it as unlisted, with this reason.
- Do this only when the build is not already proven by the run's own build.

**P5. R5 timeout calibration: record the measurements now; change source only with the next
needed source change.**

- Manifest row caps take the measured values above, which need no source change: CP-1 3,
  CP-2 3, CP-3 2, CP-4 2, CP-5 3, CP-6 2 and guard 6 minutes. This applies only if those rows
  run again.
- Per-test timeouts above about 2x their measured maximum:

  | Timeout | Measured max | Ratio to max | Tighten to |
  |---|---:|---:|---:|
  | Identity 60 s | 10.5 s | 5.7x | 30 s |
  | L1..L5 60 s | 19.2 s | 3.1x | 40 s |
  | Cleanup 60 s | 22.6 s | 2.7x | (about 2x; acceptable) |

  Each must either be tightened to the value shown or carry a recorded provisional exception:
  CARD-0824, samples n ≤ 7 from one host, this document as the measurement reference, and
  collection from the SourceLanding Mutation baseline greens as the bounded collection plan.
  Recording the exception is compliant and avoids a rebuild-and-rerun for an attribute edit.
- The Gate reload case at 15 s keeps its existing recorded owner-fixture exception (10 s
  poll/debounce).
- **L6 is the opposite risk.** Its 60 s cap is only 1.5x its only green (39.6 s at load 16).
  Do not widen it now. Collect at least two more green samples, from P3's run or the
  Mutation baselines. If a fresh green body exceeds 45 s, report the L6 timing as an R5
  finding for a measured re-budget. Never add a retry or widen the cap to turn a red green.

**P6. R4 recording.**

- Keep the CARD-1514 phase recorder on for every row, with `ANTIPHON_TEST_PHASE_GROUP_ID`
  equal to that row's CP id. The early runs mislabelled every row as CP-2; correct it at
  manifest import, never afterwards.
- Report per run:
  - wall, admission and execution separately;
  - each acquisition's wait and queue position, from executor.log;
  - build seconds with load at start;
  - host startup, tests and teardown for each row;
  - tool remainder as remainder.
- DB container/readiness/migration/clone and Git/HTTP/DB call sub-phases stay **unknown**
  wherever the recorder has no complete record. Never record them as 0 or as measured.

**P7. If P2's run still goes over 30 min.** Report it with its run id, admission/execution
split and the broker queue lines, and cite this Debug. R1 says any run observed over 30
minutes needs a Debug. It does not exempt runs where admission caused the overrun, so do not
claim an exemption. The caller decides whether to commission another selection Debug or to
route repeated admission-only overruns to host capacity (CARD-1531 for admission versus
execution, and the broker budget).

## Decisions for the caller

1. Whether Review needs CP-1/CP-3/CP-5 re-run at the exact HEAD (P1). If it does, commission
   the P3 TestDesign amendment rather than running seven acquisitions.
2. **Fleet capacity.** Seven concurrent server2 tasks share a 4-slot broker running at
   `-maxcpucount:6` on 24 cores. On 2026-11-05 this produced queues 11 deep and load above 40.
   Admission, not test execution, is now the main cost of every short Code row. Changing the
   broker budget or the 7-task target is an operator setting. The Code stage cannot repair it.
3. **Mutation stage.** The TestDesign reserves 439 lease acquisitions for 104 PCs. At today's
   mean wait of 46.5 s that is about 5.7 h of admission alone, and at p95 (255 s) about 31 h.
   SourceLanding Mutation must be planned as many bounded runs (one control's red and green
   per run, using exact-method filters). The CARD-0824 31-hour floor should be re-estimated
   from these build and admission samples before dispatch.
4. **Outside performance scope; reported only.**
   - Shared fixtures changed by this slice have existing consumers outside the C0824 files:
     LandingSafetyHarness (74 files), LandingGitFixture (46), CompletedCardCleanupFixture (5)
     and CompletedCardMirrorFixture (2). The Code report records no run for them.
   - Selecting them would be its own large batch, which needs its own bounded packing (R1/R7)
     if Review requires it.
