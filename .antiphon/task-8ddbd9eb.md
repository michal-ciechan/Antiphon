# Review 8ddbd9eb: C1514T_Overhead paired ratio gate (CARD-1598 A6)

Subject task 6a35d842, branch feat/card-task-6a35d842, reviewed tip 5260cd5444a8899bab376c6a0a199f7d3f78f446 (code dc1bdd4c580e2a116d6d799f7d9fc56bb64c8446), base rc/2026-10-27 aa6dfd24a51db7c02e8722a05a21817bf4df407a. Host server2 (Linux, 24 cores). Verification round Final, Interim scope: the whole Unit lane was not run.

Outcome: the ratio gate is stable and can still fail, but C1514T_Overhead still flakes on loaded hosts. It timed out at the unchanged 300 s limit in 9 of 40 runs. The ratio statistics passed in every one of those runs.

## Runs

One build through the build-slot gate: `dotnet build tests/Antiphon.Tests --property:OutputPath=bin-r8dd/ -p:UseAppHost=false` (lease 0c8eea5e, 142 s). Every run was `dotnet exec --no-build` through `scripts/build-slot.ps1`.

### Witness set (lease 23b0c43e)

Filter `/*/*/(PairedOverheadStatisticsTests*)|(TestTimingRecorderTests*)|(TestTimingLifecycleTests*)|(TestPhaseSummaryTests*)|(C1514LandingSplitBudgetTests*)|(RcRosterRepairTests*)/*`.

- Result: total 42, 41 passed, 0 failed, 1 skipped (C1514T_PS51AndRerun, Windows-only), 2m29s.
- PairedOverheadStatisticsTests 8/8.
- TestTimingRecorderTests 18 (1 skip).
- TestTimingLifecycleTests 1.
- TestPhaseSummaryTests 7.
- C1514LandingSplitBudgetTests 5/5: C1404, C1577, C1584, C1410 and one more case.
- RcRosterRepairTests 3/3.

### Matrix

Each run used filter `/*/*/TestTimingChokePointTests/(C1514T_Overhead*)|(C1514T_SlowRecorder*)`. 20 runs were alone and 20 had 4 extra `while :;` burners. The two conditions alternated. Background host load was 5 to 46.

| Condition | C1514T_Overhead passed | Failures | SlowRecorderExceedsOverhead |
|---|---|---|---|
| Alone | 16/20 | 4 timeouts (runs 03, 08, 10, 20) | 20/20 passed |
| 4 burners | 15/20 | 5 timeouts (runs 05, 08, 14, 17, 18) | 20/20 passed |

- Every failure was `TimeoutException ... timed out after 00:05:00`. None was a ratio assertion.
- Passing durations were 3m40s to 4m59.8s. Run 17-alone passed with 0.2 s to spare.
- Arm walls stretched from 10 s to as much as 24 s. Run 03-alone finished all 9 pairs with medians 0.0074 wall and -0.0016 CPU, and still timed out.

Paired-ratio distribution (344 pairs, overhead-raw.json):

| Condition | Pair wall p05 / p50 / p95 / max | Pair CPU p50 / p95 / max |
|---|---|---|
| Alone | -0.036 / 0.0019 / 0.0405 / 0.078 | 0.0013 / 0.027 / 0.043 |
| Burners | -0.035 / 0.0004 / 0.0405 / 0.098 | 0.0004 / 0.017 / 0.036 |

- Over 32 complete runs, the worst wall median was 0.0209 and the worst CPU median was 0.0081.
- The worst empty-scope ON cost was 5.5 us per scope.
- The old gate (wall median and bootstrap upper both under 0.02) would have failed 3 of these 32 runs: 16-alone (upper 0.038), 04-alone (upper 0.0219) and 19-burn (upper exactly 0.0200). The new gate fails none of them on ratios.

## Defect

D-1. A flake remains: C1514T_Overhead times out under host load.
- Where: `tests/Antiphon.Tests/TestHelpers/TestTimingChokePointTests.cs:114`, `[Timeout(300_000)]`, together with the fixed repetition count from `reps` at line 131.
- Failure: 9 of 40 runs timed out at 300 s, 4 alone and 5 under burners. Even the alone runs were at host load 18 to 31.
- Why: `reps` is calibrated from one hot repetition, and 10 s is only a minimum per arm. Under load each repetition slows, so 10 pairs (1 warm plus 9) of two arms each overrun 300 s.
- Inherited: the diff does not touch the workload, the calibration or the timeout. The fault is still inside A6, whose aim was to stop this test flaking on loaded hosts.
- Fix (choose one):
  - Set the timeout to about 2x the measured loaded p95, around 600 s, per test-performance-standards. Update the nightly class cost to match.
  - Or make the arm length bounded: stop each pair at equal repetitions once both arms reach 10 s, with an upper cap, and amend the V-13 protocol to say so.
- Re-witness with the same 20 alone plus 20 under 4 burners. Accept only 0 timeouts.

## Answers to the brief

### 1. Is 0.20 still a gate?

It still fails a gross regression: the slow witness passed 40 of 40, including under burners. It is no longer a "negligible overhead" gate.

The workload runs about 59 repetitions of about 170 ms on each 10 s arm. Each repetition opens 8 phases, so there is one phase per about 21 ms of work. At that density:

| Bound | Passes undetected |
|---|---|
| Wall median under 0.20 | Up to about 4 ms of blocking per phase (for example a per-event fsync or lock wait), about 2 s on a 10 s arm |
| CPU median under 0.05 | Up to about 1 ms of CPU per phase, which is 200 to 900 times the measured 1.1 to 5.5 us per scope |
| Old wall median under 0.02 | Up to about 0.4 ms per phase |

The brief's "100 ms per phase is 10%" case does not arise here: 100 ms per phase is about +480% and is rejected. The case that does pass is about 2 ms per phase at about 10%. On real millisecond-scale tests with several phases each, that cost is not negligible.

The pairing is real. Within each pair, OFF and ON alternate repetition by repetition at about 170 ms granularity. Arm order alternates between pairs. Only the single IO step on each arm and the ON-only final flush run back to back. The median of 9 is robust here: per-pair noise was about ±4% at p95.

### 2. Matrix

The matrix and pass counts are above. The ratios are now stable: 0 ratio failures in 40 runs. The test as a whole is not stable: 9 of 40 runs timed out (D-1).

The slow-recorder witness failed the recorder in every run, alone and under burners. It runs outside the real C1514T_Overhead harness and uses one repetition per arm, so its effect is about +240%, roughly 12 times the bound. It does not test sensitivity near the bound.

### 3. Assertion honesty

- Removed: `upper.ShouldBeLessThan(0.02d)`. The bootstrap upper bound is now only recorded.
- Relaxed: the wall median bound went from 0.02 to 0.20.
- Added: CPU median under 0.05, and `Accepts` must return true.
- No other timing test file changed.
- Equality at both bounds is tested.
- Not unit-tested: fewer than 9 pairs, mismatched pair counts, a zero baseline (`Ratio` returns +Inf), NaN and negative ratios. A mutation that drops the `Count < MinimumPairs` guard survives the unit class.
- NaN sorts lowest in `Median`, so a NaN pair counts as the best case. That direction fails open, though it cannot occur with the present inputs.

### 4. Roster

There is no new class or file. TestTimingChokePointTests gains one node, C1514T_SlowRecorderExceedsOverhead, which takes 7 to 11 s. PairedOverheadStatisticsTests gains four tests. The census cases and RcRosterRepairTests are green.

## Advisories

- **A-1.** Set the wall median bound to 0.10 instead of 0.20. That is 1.5 times the worst wall median ever recorded (0.067 at load 33) and 5 times the worst here (0.021). It halves the blocking that passes undetected, to about 2 ms per phase. Keep the CPU median bound at 0.05: the worst observed is 0.0197.
- **A-2.** Gate the absolute empty-scope cost, for example `emptyOnScopeNs < 50_000`. The observed maximum was 5.5 us under load, so that bound leaves about 9 times headroom. It is independent of load to about that factor, and it catches any per-event cost of 0.05 ms or more. That is the test that matters for "negligible" on fast tests. Today that value is recorded but not asserted.
- **A-3.** Add a witness near the bound that goes through the real paired harness: a per-phase delay that gives about 1.5 times the bound, about 6 ms at 0.20 or about 3 ms at 0.10. Also add unit edges for fewer than 9 pairs, mismatched counts, zero baseline and NaN.
- **A-4.** Two older tests are now misnamed:
  - `Sustained_overhead_above_two_percent_is_rejected_despite_a_fast_on_outlier`
  - `Uncertain_upper_bound_is_rejected_even_when_the_median_is_small`

  Both describe rejections that the gate no longer makes; `Accepts` would accept both inputs. Rename them, or assert `Accepts` explicitly.
- **A-5.** Several places now disagree with the new gate:
  - The plan's V-13 and overhead protocol (master `docs/superpowers/plans/2026-10-23-card-1514-test-timing-instrumentation-plan.md`) still say the median and upper bound must be under 2%, and that a noisy result is inconclusive.
  - The plan's Windows CP-8 expects 28 results; the `C1514T_*` filter now selects 29.
  - `scripts/lib/nightly-class-costs.json` lists TestTimingChokePointTests with 3 nodes and 240 s.

  Correct all three in a Docs/Code follow-up.
- **A-6.** The CPU ratio uses whole-process `TotalProcessorTime`. In a nightly chunk, other tests running in the same process add CPU noise. Co-run was not measured here.
- **A-7.** The author's comment and report say a 50 ms sleep adds "about 400 ms on a 10 s arm". In fact it adds about 400 ms per repetition, which is about +240%.

## Not done

- No checkpoint-tool receipts. The brief asked for build-slot runs with `dotnet exec`.
- No whole Unit lane.
- No merge-tree: the brief forbids `--write-tree`.
- The red parent was not rerun.
- No PCs were run; they stay pending.
- `bin-r8dd` output directories and `/tmp/c1514-*` evidence roots are left in place, as the brief said to delete nothing.
- `check-evidence-diff.ps1` over base..tip: 5 commits, 1 entry, 0 violations.
