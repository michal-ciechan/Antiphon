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
project builds once; CP-93 reuses CP-90's build. Total estimate 17 minutes plus
slot admission; no repeated green proof is requested. Native Windows path/handle
proof remains owed by the caller under CP-17, not credited by Linux.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-3 | S5a | `tests/Antiphon.Tests -> bin-c1836-s5a/` | linux-stream | `/*/*/RcHistoricalEvidenceTests/*` | V-3 | all 3 named methods, 0 failed/skipped | 3 | 8 |
| CP-90 | all | `tests/Antiphon.Tests -> bin-c1836-guards/` | lane-guards | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(ProcessSpawnLimitTests*)/*` | policy, categories, spawn census | all listed, 0 failed/skipped | 79 | 4 |
| CP-91 | S5a | `tests/Antiphon.Agents.Pty.Tests -> bin-c1836-s5a-pty-guards/` | linux-pty-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 1 |
| CP-92 | S5a | `tests/Antiphon.SessionRunner.Tests -> bin-c1836-s5a-runner-guards/` | linux-runner-census | `/*/*/ProcessSpawnLimitTests/*` | impacted spawn-limit-rosters | both methods, 0 failed/skipped | 2 | 2 |
| CP-93 | S5a | CP-90 | linux-derived-index | `/*/*/DerivedImpactIndexTests/committed_index_matches_the_builder` | derived index freshness | exact freshness method, 0 failed/skipped | 1 | 2 |
