# Capacity lock order and admission timing

Debug round for the claim/reroute lock order and launch-time redemption. The table is the closed verification list for this round.

## Verification design

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | all | `tests/Antiphon.Tests -> bin-b095c160-cp/` | capacity-lock-admission | `/*/*/(CapacityLockOrderTests/Claim_and_queued_reroute_share_one_lock_order*)\|(CapacityAdmissionTimingTests/Preparation_restart_and_dispatch_spend_one_admission*)\|(AgentTaskDispatchBaseGuardTests/Stale_claim_preserves_capacity_grant_for_the_fresh_inspection*)\|(WallRerouteDispatchTests/Dispatch_time_rewalk_supersedes_the_old_alias_wait*)\|(CapacityRecoveryAcceptanceTests/Dispatch_redemption_rolls_back_with_the_claim_and_does_not_leak_tracked_changes*)` | F1, F2, F3 | all listed, 0 failed | 8 | 15 |
