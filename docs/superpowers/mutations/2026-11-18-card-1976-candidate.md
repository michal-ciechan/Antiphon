# CARD-1976 unbound mutation candidate

Landing owner: `051a8c28-409b-455e-aae7-7b9900079401`.
Plan: [checkpoints](../plans/2026-11-18-card-1976-checkpoints.md).
Deliberate mutations are waived in Code by the operator's 2026-11-12 instruction.
Every variant below is pending; this is not a SourceLanding binding or receipt.

| PC | Precise method filter | Variants | Mutation | Expected failure |
|---|---|---|---|---|
| PC-1 | `/*/*/C1960WorkspaceSlotsScriptTests/Token_file_errors_exit_2_before_any_request` | release/missing, release/empty, release/whitespace, cold-reset/missing, cold-reset/empty, cold-reset/whitespace | Restore original terminating token-file diagnostics and null-unsafe Trim | exit 1 instead of 2 in each variant |
| PC-2 | `/*/*/C1960QuarantineReleaseRouteTests/Release_log_waits_for_runner_and_records_actual_outcome` | removed, refused, removal_failed | Restore the original pre-await released log | beforeAnswer contains a false completion Warning |

Pre-fix evidence: CP-1 at `9e449c42382210c094c14f86289cfda8fb67ccef`,
run `20261118-061828-88e7`: 9 executed, 0 passed, 9 failed, 0 skipped.
All failures reached the intended assertions. Empty files exposed null Trim;
missing/whitespace files exposed terminating Write-Error; all three runner outcomes
exposed the premature released Warning. Ordinary base red is not mutant-cycle credit.
Mutation owns method-scoped red/restore/green, missing-control discovery and any
additional variants. Caller commissions it after SourceLanding, if required.
