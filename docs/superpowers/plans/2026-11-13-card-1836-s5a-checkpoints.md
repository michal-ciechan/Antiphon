# CARD-1836 S5a verification amendment

Code task 08e79898. Base `586be574b88d1957dbf2a96c5dad771e3bcd8b37`.
The owner plan is [incremental activation](2026-11-12-card-1836-incremental-activation-plan.md), S5a / V-3 / D-3.
CP-3 and CP-90 retain its exact filters and floors. The committed diff selects the
`spawn-limit-rosters` and `test-lane-category` impacted rows; CP-90 covers the main
assembly and CP-91/92 cover the other two projects. CP-93 verifies the derived
index after adding a test source file. No whole lane is selected. No ordinary R-n
is assigned to S5a; R-1 begins at S5b.

## Performance budget

The owner plan's provisional method/IO budgets apply. One 885 MiB streamed corpus
is generated only by LargeSummaryIsBounded, with no database/service fixtures.
CP-91/92/93 estimates are provisional, including cold build and discovery. Each
project builds once; CP-93 reuses CP-3's build. Total estimate 17 minutes plus
slot admission; no repeated green proof is requested. Native Windows path/handle
proof remains owed by the caller under CP-17, not credited by Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S5a | `tests/Antiphon.Tests -> bin-c1836-s5a/` | linux-stream | `/*/*/RcHistoricalEvidenceTests/*` | V-3 | all 3 named methods, 0 failed/skipped | 3 | 8 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |
| CP-91 | S5a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s5a-pty-guards/` | linux-pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 1 |
| CP-92 | S5a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s5a-runner-guards/` | linux-runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 2 |
| CP-93 | S5a | CP-3 | linux-derived-index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | derived index freshness | exact freshness method, 0 failed/skipped | 1 | 2 |

## Measured batching correction

Initial run `20261113-022753-b7b3`, source
`7170a92387a3026bb42492ce05853e2892d5ce2b`, was stopped at 27m25s after
CP-3 passed 3/3 and CP-93 failed its existing 60s rebuild bound at 61.596s.
CP-90/91/92 were not executed and remain required. CP-3's build occupied 334s
plus 150s admission; its row waited 210s and executed 112s. The guard compile
alone took 8m04s; confinement/admission added further cost. These are loaded
observations, not measured p95. No timeout/assertion is widened.

Run the same closed table in three bounded tool invocations at the committed
source: `--rows CP-3,CP-93`, `--rows CP-90`, and `--rows CP-91,CP-92`, with
`--parallel 1 --expected-source-sha <HEAD>`. This retains every required row and
isolated build, avoids a repeat of the combined run, and targets each batch below
30 minutes. Keep all prior red/cancelled evidence. Direct harness runs are
explicit diagnostic/red-first checks for the streaming target and must be reported
separately; they do not discharge a checkpoint receipt.
