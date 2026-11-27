# rc/2026-12-07 cut gates, third pass

`PIN=bdb2790afb14d01409763a0e4741454493063e4d`

**The published pin fails the derived impact index gate.** The regenerated index is pushed on `feat/card-task-a74b5058` at `d893f5c109879e686b66c57e9290e6c67fd95aac`. `land/v2` returned HTTP 409, `Task a74b5058 must have succeeded before it can land.` Land the task branch after settlement and rerun the cut gates at the resulting published master SHA.

## Published master checks

Server2 used .NET SDK 10.0.404. The pinned checkout was clean when the graph and index commands started.

| Gate | Result at the pin |
| --- | --- |
| `land-graph-audit --check` | Both evaluation passes and the docs canary approved. Digest `FB961BDD864AF7E6301C9B9E489E985A4BEA8C66A6E8DE63F9682C4D2ACFC8A7` across 83 definitions, pins blob `5c6bebb9830976468ea54c20c640127ab452931d`. The record names authoring SDK 10.0.204. Wall time 112 seconds with cold CLI startup. Lease `e526f135-594d-4b3c-a037-f40ba4c6624b` held for 48 seconds, including the evaluator build. The canary copy budget was 77 seconds. |
| `regenerate-derived-impact-index` | Printed `derived-impact-index changed` under lease `be4511a7-0802-4df1-9524-9abe37b8e9b0` in 42 seconds. The committed SHA-256 `7ea10f7994230da21d2382b74a352a7a303dd047d66dfae0b2c152a41b052104` became `bd9dbfa848171724e710d93b35d70ce3f774083568910eb94cc1c5ec7c7dfe70`. |

The regeneration updates 44 insertions and 8 deletions in the generated index. The expanded historical migration witness added references to `CapacityRecoveryCompatibilityTests`, `PipelineSpeedIntegrationTests` and `MigrationTestDatabase`. The regenerated capped lists and counts reflect the test changes after the previous index recording at `4623cd60c558c6cde3ca32760891fc7ee70c9b68`. The repair commit changes only the index. Its regeneration printed `derived-impact-index unchanged` under lease `5b01d2f4-1a55-4d5b-9980-f1e929e173d5`.

Published master still resolved to the pin when fetched after checkpoint verification.

## Linux checkpoints on the repair branch

Run `20261126-234056-3185` used clean source `d893f5c109879e686b66c57e9290e6c67fd95aac`. These receipts cover the repair commit. The cut-gate receipt at the new published master SHA remains pending. The shared build and every row used a granted server2 slot.

| Row | Coverage | Passed | Failed | Skipped |
| --- | --- | ---: | ---: | ---: |
| CP-1 | Six Final guards and `PlanCheckpointClassSelectionGuardTests` | 106 | 0 | 1 |
| CP-2 | Whole `PlanCoverageBindingTests` | 7 | 0 | 0 |
| CP-3 | Whole `LiveLaneInventoryTests` and `ExpectedPlatformSkipRosterTests` | 8 | 0 | 0 |
| CP-4 | Whole `RcDirtyLaunchOrderTests` | 6 | 0 | 0 |
| CP-5 | `ChangeAwareReleaseTests.C1313_ModeCredit` | 1 | 0 | 0 |
| CP-6 | Whole `TaskProgressStallSweepTests` | 15 | 0 | 0 |
| CP-7 | `HistoricalMigrationSeedCoverageTests.Post_tip_columns_on_historical_seed_tables_have_coverage` | 1 | 0 | 0 |
| Total | | 144 | 0 | 1 |

CP-1 includes all 28 `DerivedImpactIndexTests` and both class selection guard methods. Its skip is `C1210_Windows_linux_only_outcomes_are_exact`. CP-2 includes `historical_row_timeout_checklist_binds_the_advisory_method` (82.549 seconds) and `historical_plans_bind_without_suppression` (340.483 seconds). CP-7 ran the catalog comparison and required-column control on disposable PostgreSQL and reported 1,153 compiled candidate methods.

The run took 25m02s, including 5m55s of output cleanup. The validator qualified CP-1 on its own and all seven rows together. The retained report SHA-256 is `ab8d7e33dc87ba9145fdddbc23056ed2fb7f06badef75a561e29b0bcec642b9f`. The selections, graph and index logs, report and per-row TRX files are under `/work/evidence/rc-cut-pass3-a74b5058/`.

```text
CHECKPOINT CP-1 commit=d893f5c109879e686b66c57e9290e6c67fd95aac build=ok filter=/*/*/(TestClassificationPolicyTests*)|(TestClassificationGuardTests*)|(DerivedImpactIndexTests*)|(C1729LayeringGuardTests*)|(WindowsUnitPlatformAdmissionTests*)|(NativeLinuxPolicyWitnessMutationTests*)|(PlanCheckpointClassSelectionGuardTests*)/* executed=106 passed=106 failed=0 skipped=1 trx=/work/worktrees/ws-03-d5/.antiphon/checkpoints/20261126-234056-3185/rows/CP-1/run.trx slot=granted waited=0s dirty=0 source=d893f5c109879e686b66c57e9290e6c67fd95aac sourceState=clean buildSource=verified
CHECKPOINT CP-2 commit=d893f5c109879e686b66c57e9290e6c67fd95aac build=reused filter=/*/Antiphon.Tests.Checkpoints/PlanCoverageBindingTests/* executed=7 passed=7 failed=0 skipped=0 trx=/work/worktrees/ws-03-d5/.antiphon/checkpoints/20261126-234056-3185/rows/CP-2/run.trx slot=granted waited=0s dirty=0 source=d893f5c109879e686b66c57e9290e6c67fd95aac sourceState=clean buildSource=verified
CHECKPOINT CP-3 commit=d893f5c109879e686b66c57e9290e6c67fd95aac build=reused filter=/*/Antiphon.Tests.Scripts/(LiveLaneInventoryTests*)|(ExpectedPlatformSkipRosterTests*)/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/ws-03-d5/.antiphon/checkpoints/20261126-234056-3185/rows/CP-3/run.trx slot=granted waited=0s dirty=0 source=d893f5c109879e686b66c57e9290e6c67fd95aac sourceState=clean buildSource=verified
CHECKPOINT CP-4 commit=d893f5c109879e686b66c57e9290e6c67fd95aac build=reused filter=/*/Antiphon.Tests.Scripts/RcDirtyLaunchOrderTests/* executed=6 passed=6 failed=0 skipped=0 trx=/work/worktrees/ws-03-d5/.antiphon/checkpoints/20261126-234056-3185/rows/CP-4/run.trx slot=granted waited=0s dirty=0 source=d893f5c109879e686b66c57e9290e6c67fd95aac sourceState=clean buildSource=verified
CHECKPOINT CP-5 commit=d893f5c109879e686b66c57e9290e6c67fd95aac build=reused filter=/*/Antiphon.Tests.Scripts/ChangeAwareReleaseTests/C1313_ModeCredit executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-03-d5/.antiphon/checkpoints/20261126-234056-3185/rows/CP-5/run.trx slot=granted waited=0s dirty=0 source=d893f5c109879e686b66c57e9290e6c67fd95aac sourceState=clean buildSource=verified
CHECKPOINT CP-6 commit=d893f5c109879e686b66c57e9290e6c67fd95aac build=reused filter=/*/Antiphon.Tests.Application/TaskProgressStallSweepTests/* executed=15 passed=15 failed=0 skipped=0 trx=/work/worktrees/ws-03-d5/.antiphon/checkpoints/20261126-234056-3185/rows/CP-6/run.trx slot=granted waited=0s dirty=0 source=d893f5c109879e686b66c57e9290e6c67fd95aac sourceState=clean buildSource=verified
CHECKPOINT CP-7 commit=d893f5c109879e686b66c57e9290e6c67fd95aac build=reused filter=/*/Antiphon.Tests.Application/HistoricalMigrationSeedCoverageTests/Post_tip_columns_on_historical_seed_tables_have_coverage executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/ws-03-d5/.antiphon/checkpoints/20261126-234056-3185/rows/CP-7/run.trx slot=granted waited=0s dirty=0 source=d893f5c109879e686b66c57e9290e6c67fd95aac sourceState=clean buildSource=verified
CHECKPOINT SOURCE VALID source=d893f5c109879e686b66c57e9290e6c67fd95aac rows=1
CHECKPOINT SOURCE VALID source=d893f5c109879e686b66c57e9290e6c67fd95aac rows=7
```

## Repair ancestry

Each master SHA passed `git merge-base --is-ancestor <sha> <pin>`. The seven branch-to-master equivalents also matched with `git patch-id --stable`.

| Repair | Brief reference | Master SHA |
| --- | --- | --- |
| Historical migration schema seed | `ea6c2abc` | `ea6c2abc77eb55d0b5995291aeb5f0dd691d0d7b` |
| Reversible task-world seed witness | `e23c48ec` | `e23c48ec9aca8fcd52bc3fd7544b30aada0b7d13` |
| Reversible landing-world seed repair | `c1b91e9d` | `c1b91e9d23e5c3ade4c23e872c12810fc17128b2` |
| Group 141 live-lane dispositions | `585258802` | `585258802ac6be0a55c6c2235cad5c5833f14d37` |
| Group 141 offline live-lane witness | `793f96e5` | `793f96e527d1942faa6c62456766fe92884a3a30` |
| Migration witness derived index | `e9f644e4` | `e9f644e4f9d13e1e3c312658c65c69977b0a196a` |
| Group 140 release cut defaults to Full | `subject` | `7c17f1f6d455fa907ba2d2a005b58c3cfc8bd446` |
| Group 131 C1313 evidence binding | `e87776a5` | `05fc25f450e3f01e0311443e27188be5a9d6c081` |
| Git-in-process unreadable directory | `790ddeac` | `905e2129d6fa8082f49dde809638d4422d16a232` |
| Group 93 stall sweep witness isolation | `13c0d882` | `bdb2790afb14d01409763a0e4741454493063e4d` |
| Group 113 CARD-2085 held-handle repair | `task b3d3001f` | `b01ab6294dcd627534d1f48c1481e6ce60aaf1c3` |
| Coverage roster repair | `86b93bf4` | `dd8026356ae28a5899d399aef565cc95a1afdd30` |
| PostgreSQL sweep witness handoff | `7f702882` | `133f943b6ffd3c7836ad81234bbe25b770a6f97d` |
| Land graph 77-second canary budget | `a4f9b519` | `07a534f5e04333670c893632b04820fe7e17dd76` |

The group 140 class-specific repair is `5290d1c4a31725bbaafcc33d107d61a6b46d029b`. The CARD-2085 repair on task `b3d3001f` is `1913261a296b463025405bed8ed40751b2e4bf15`. The pin also contains its master red witness `d503a9ca2dae1a0509fae6c30ce6ddf9d5c46cad` and the earlier group 113 binding repair `0b07453e48b7eaab1985ec0f7537b056a4b80ed3`. The PostgreSQL witness changes themselves are at `fc626a13342bec63c13684cb763d487fbb31b18f` and `0a996cd4e627b8b42f085cafa5ba955bc6a8ede4`.

## Native Windows qualification

The previous Full run `1fe763a9` left groups 141, 113, 86, 140, 131, 49, 87, 66, 89, 92, 58, 79, 76, 93, 94, 90, 82, 84 and `git-in-process:all` owed on the next integrated Windows candidate. Group 113 includes the binding class and the native warm-build sharing witness. The prior pass also left the C1210 Windows-only row, native Job Object cases and split-8 wakeup replay owed. The next Full must establish coverage completeness and report the roster dispositions. [Prior cut gate record](2026-11-26-rc-cut-gates-at-0b07453e4.md) · [Coverage gaps](2026-11-26-rc-1fe763a9-coverage-incomplete-reasons.md)
