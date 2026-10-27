# Capacity lock order and admission timing

Debug round for the claim/reroute lock order and launch-time redemption. The table is the closed verification list for this round. TUnit rejects a slash inside a parenthesized OR operand, so the two new classes share one class-level OR and each regression method is its own row.

## Verification design

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-b095c160-cp/` | lock-admission | `/*/Antiphon.Tests.Application/(CapacityLockOrderTests*)|(CapacityAdmissionTimingTests*)/*` | F1, F2, F3 | 5 passed, 0 failed | 5 | 8 |
| CP-2 | all | CP-1 | stale-claim | `/*/*/AgentTaskDispatchBaseGuardTests/Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*` | F2 | 1 passed, 0 failed | 1 | 2 |
| CP-3 | all | CP-1 | rewalk | `/*/*/WallRerouteDispatchTests/Dispatch_time_rewalk_supersedes_the_old_alias_wait*` | F1 | 1 passed, 0 failed | 1 | 2 |
| CP-4 | all | CP-1 | redeem-rollback | `/*/*/CapacityRecoveryAcceptanceTests/Dispatch_redemption_rolls_back_with_the_claim_and_does_not_leak_tracked_changes*` | F2 | 1 passed, 0 failed | 1 | 2 |

The counts in Expect were produced by a build-slot `--no-build` host of `tests/Antiphon.Tests/bin-b095c160-fix/Antiphon.Tests.dll`. `Seal-CheckpointFreshOutput` on that directory returns `output-inventory-failed`: the DLL is 68,995,584 bytes and `Read-CheckpointBytes` stops at 67,108,864. `bin-b095c160-cp/` was not built. This round has no checkpoint-tool receipt, so `reviewedSourceClean` stays false.
