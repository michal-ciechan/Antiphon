# CARD-1713 unbound Mutation candidates

Original Code/landing owner: `013b9ff7-b813-4986-8c7f-2a5b0fbdef95`.
Plan: [absolute hold bound](../plans/2026-11-05-card-1713-build-slot-max-hold.md).
These are unbound candidates. All PCs and variants remain pending for
method-scoped SourceLanding Mutation after ordinary Review and caller landing.
Code executed no deliberate mutant. Mutation owns binding, missing-control
discovery, deliberate edits and red/restore/green evidence.

| ID | Candidate defect | Exact detecting method in BuildSlotMaxHoldTests | Expected failed assertion |
|---|---|---|---|
| PC-1 | Remove the absolute grant-age branch in BuildSlotBroker.SweepLocked | V1_Default_keeps_live_holder_until_48_hours_then_reaps_once | Sweep at 48h must reap 1 |
| PC-2 | Lower BuildSlotSettings.LeaseMaxHoldHours default from 48 to 24 | V1_Default_keeps_live_holder_until_48_hours_then_reaps_once | Sweep one tick before 48h must reap 0 |
| PC-3 | Measure age from LastRenewedAtUtc instead of GrantedAtUtc | V2_Configured_bound_uses_grant_age_and_cannot_be_extended_by_reacquire_or_renew | Renew at original 72h must be false |
| PC-4a | Remove positive bound validation; input 0 | V4_Nonpositive_bound_is_rejected_by_production_options | Production options must throw InvalidOperationException |
| PC-4b | Remove positive bound validation; input -1 | V4_Nonpositive_bound_is_rejected_by_production_options | Production options must throw InvalidOperationException |
| PC-5 | Apply absolute bound only inside the expired-TTL branch | V3_Bound_applies_before_ttl_and_next_acquire_admits_a_waiter | Waiting acquire at configured 1h must succeed before 90m TTL |
| PC-6 | Restrict the absolute-bound condition to pid mode | V6_Healthy_renew_mode_holder_is_reaped_at_bound_before_ttl | Renewal at the 1h bound must be false despite a healthy renewal and later TTL |

Each phase uses the exact method filter
`/*/*/BuildSlotMaxHoldTests/ExactMethodName`, never the whole class. PC-4's
method has two argument executions; retain both variants and inspect each
assertion failure. Zero execution, fixture/build failures and skips are not red.
Do not widen deadlines, loosen assertions or combine independent controls
against the same source file. Bind against the confirmed landed source pack;
this document contains no preimage hashes or authority to mutate this branch.
