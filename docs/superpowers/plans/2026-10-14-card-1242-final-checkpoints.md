# CARD-1242 Final checkpoints (custody repair)

Closed ordinary selection for CARD-1242 at the custody-repair tip (Code e59439e1, original landing owner e79946fd-91cc-44d9-8635-eeeeec9ee93a). It replaces the task-local ten-row table that Final Review 6b4ff3b5 reran. Invariants are V-1..V-4 and R-1..R-3 of [the CARD-1199 detect/report plan](2026-10-12-card-1199-detect-report-plan.md), including both CARD-1242 V-4 amendments. One isolated `Antiphon.Tests` build is reused by every row. This table does not include the whole Unit lane.

CP-9 qualification (receipt repair task d8f2898d, 2026-10-14). Neither the tool `validate` verb nor `scripts/validate-checkpoint-receipt.ps1` has a syntax for an expected skip. Both reject a filter row with `skipped != 0` as `row_failed`. Review 6b4ff3b5 therefore could not certify the source clean on the earlier CP-9 row, which selected all of `EvidenceFolderTests` and recorded 5 passed plus 1 skip. The Linux row now excludes the Windows-only `tool_copy_removal_retries_while_a_file_is_still_held_open` by exact name. On Linux that test only calls `Skip.Test`, so the row selects the same five executing tests. The test and its Windows skip are unchanged. Its native Windows file-sharing proof is not covered by this table and remains pending under R-2. No other row changes.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-d8f2898d-final/` | output-cleanup | `/*/*/(OutputCleanupTests*)/*` | V-4 | all 42, 0 failed/skipped | 42 | 8 |
| CP-2 | all | CP-1 | owned-output | `/*/*/(CheckpointOwnedOutputTests*)/*` | V-4 | all 10, 0 failed/skipped | 10 | 2 |
| CP-3 | all | CP-1 | tool-copy | `/*/*/(CheckpointToolCopyCleanupTests*)/*` | V-4 R-1 | all 16, 0 failed/skipped | 16 | 2 |
| CP-4 | all | CP-1 | receipt | `/*/*/(CheckpointReceiptSafetyTests*)/*` | V-3 R-1 | all 8, 0 failed/skipped | 8 | 3 |
| CP-5 | all | CP-1 | source-execution | `/*/*/(CheckpointSourceExecutionTests*)/*` | V-1 V-2 V-3 | all 7, 0 failed/skipped | 7 | 2 |
| CP-6 | all | CP-1 | report-writer | `/*/*/(ReportWriterTests*)/*` | V-3 | all 6, 0 failed/skipped | 6 | 2 |
| CP-7 | all | CP-1 | wait | `/*/*/(WaitCommandTests*)/*` | V-4 R-1 | all 12, 0 failed/skipped | 12 | 2 |
| CP-8 | all | CP-1 | app | `/*/*/(CheckpointAppTests*)/*` | R-1 | all 3, 0 failed/skipped | 3 | 2 |
| CP-9 | all | CP-1 | evidence | `/*/*/(EvidenceFolderTests*)/(!tool_copy_removal_retries_while_a_file_is_still_held_open)` | V-3 V-4 | all 5 Linux-executing, 0 failed/skipped; the Windows-only sharing case is excluded by exact name and stays pending under R-2 | 5 | 2 |
| CP-10 | all | CP-1 | census | `/*/*/(CheckpointNamespaceCensusUsageTests*)/*` | R-1 census | 1 passed, 0 failed/skipped; census 554 | 1 | 2 |
