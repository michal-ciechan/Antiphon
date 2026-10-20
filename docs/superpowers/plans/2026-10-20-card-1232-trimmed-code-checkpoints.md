# CARD-1232 commissioned trimmed Code verification

The Code brief for task 1bfc5855 explicitly replaces the 12-row execution scope
with one committed red witness and narrow green behavior checks, no Unit lane,
whole classes or positive controls. This is the executable selection for that
override; it does not claim the original Final profile's full scope.

The implementation decisions remain in
[the landed plan](2026-10-20-card-1232-pre-dispatch-failure-notification-plan.md).
CP-1 selects the committed-loss witness from original CP-1 (V-1).
CP-2 merges the new failure/claim/route checks from original CP-2–8.
CP-3 merges original CP-9–12, preserving their named methods.
Original V-5–8 extensive receipt/retry matrices are deferred; the shared queue
and reconciliation implementation remains unchanged. Every PC-1–35 and variant
stays pending for Mutation. No mutation payload is bound or executed here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1232-red/` | red | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_commit_keeps_the_caller_obligation*` | V-1 | one assertion red: no durable obligation after Failed commit | 1 | 4 |
| CP-2 | S2-S3 | `tests/Antiphon.Tests -> bin-c1232-green/` | new-behaviors | `/*/*/PreDispatchFailureDeliveryTests/(Pre_dispatch_failed*)\|(Preflight_failure*)\|(Pre_dispatch_excluded*)` | V-1, V-2, V-3, R-5 | all named cases, zero failures/skips | 7 | 5 |
| CP-3 | S2-S3 | CP-2 | existing-contracts | `/*/*/(RolePolicyToolBundleDispatchTests)\|(ReceiptFailureDeliveryTests)\|(AgentTaskDispatchFailureTests)\|(DelegationDispatchRecoveryBoundaryTests)/(the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt*)\|(Caller_failure_obligation_survives_persistence_cuts*)\|(a_dispatch_failure_owns_a_durable_obligation*)\|(a_lost_failure_note_is_re_sent_on_the_ramp*)\|(C1186_Recovered_failure_preserves_obligation_guidance*)` | R-1, R-2, R-3, R-4, V-4 | all 22 cases, zero failures/skips | 22 | 5 |

### Cost

Estimated 14 minutes including two isolated builds. Use checkpoint `run` with
`--row-timeout 40m`, committed `--expected-source-sha`, no host/platform pin,
and wait to terminal completion. Red receipts are retained without relabeling.
