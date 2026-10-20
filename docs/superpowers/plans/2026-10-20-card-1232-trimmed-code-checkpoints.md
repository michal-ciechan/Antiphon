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
V-1 uses the eligible committed-save cut; V-2 uses eligible/busy callers and one
persisted backoff interval; V-3 uses the pinned-agent claim, with rollback and
commit; V-4 reuses R-1's committed-queue cut and R-2's persistence cuts.
V-5 uses two failures of a child task and an immutable payload on one destination.
V-6 uses a header-only decoy followed by the complete actual submitted prompt.
V-7 retains canceled/missing/exhausted rows; V-8 uses a stopped destination.
The remaining original variants (including repair/baseline/credential preflights,
the extended backoff ladder, destination change, spill/receipt decoys and absent/
Failed destinations) are skipped by this commissioning override, not passed.
The shared queue and reconciliation implementation remains unchanged. Every PC-1–35 and variant
stays pending for Mutation. No mutation payload is bound or executed here.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1232-red/` | red | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_commit_keeps_the_caller_obligation*` | V-1 | one assertion red: no durable obligation after Failed commit | 1 | 4 |
| CP-2 | S2-S3 | `tests/Antiphon.Tests -> bin-c1232-green/` | new-behaviors | `/*/*/PreDispatchFailureDeliveryTests/(Pre_dispatch_failed*)\|(Preflight_failure_obligation_commits_with_its_claim*)\|(Pre_dispatch_excluded_routes_create_no_obligation*)\|(Pre_dispatch_snapshot_survives_task_retry*)\|(Pre_dispatch_receipt_requires_complete_user_prompt*)\|(Pre_dispatch_recovery_retains_authoritative_row*)\|(Pre_dispatch_recovery_waits_for_destination*)` | V-1, V-2, V-3, V-5, V-6, V-7, V-8, R-5 | all 14 cases, zero failures/skips | 14 | 5 |
| CP-3 | S2-S3 | CP-2 | existing-contracts | `/*/*/(RolePolicyToolBundleDispatchTests)\|(ReceiptFailureDeliveryTests)\|(AgentTaskDispatchFailureTests)\|(DelegationDispatchRecoveryBoundaryTests)/(the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt*)\|(Caller_failure_obligation_survives_persistence_cuts*)\|(a_dispatch_failure_owns_a_durable_obligation*)\|(a_lost_failure_note_is_re_sent_on_the_ramp*)\|(C1186_Recovered_failure_preserves_obligation_guidance*)` | R-1, R-2, R-3, R-4, V-4 | all 22 cases, zero failures/skips | 22 | 5 |

### Cost

Estimated 14 minutes including two isolated builds. Use checkpoint `run` with
`--row-timeout 40m`, committed `--expected-source-sha`, no host/platform pin,
and wait to terminal completion. Red receipts are retained without relabeling.
