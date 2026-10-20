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
V-5 uses two failures of a child task, two destinations and immutable payloads.
V-6 uses a header-only decoy followed by the complete actual submitted prompt.
V-7 retains canceled/missing/exhausted rows; V-8 uses a stopped destination.
The remaining original variants (including repair/baseline/credential preflights,
the extended backoff ladder, spill/receipt decoys and absent/
Failed destinations) are skipped by this commissioning override, not passed.
The shared queue and reconciliation implementation remains unchanged. Every PC-1–35 and variant
stays pending for Mutation. No mutation payload is bound or executed here.

## Verification design

| ID | Executed method | Narrow oracle |
|---|---|---|
| V-1 | `PreDispatchFailureDeliveryTests.Pre_dispatch_failed_commit_keeps_the_caller_obligation` | Committed Failed/event/outbox, fresh producer recovery, complete prompt, no duplicate. |
| V-2 | `PreDispatchFailureDeliveryTests.Pre_dispatch_failed_insert_recovers_with_checks_disabled` | Eligible/busy; immediate and recovery insert faults; persisted 5-second due guard; checks disabled and zero budget. |
| V-3 | `PreDispatchFailureDeliveryTests.Preflight_failure_obligation_commits_with_its_claim` | Existing-agent Worktree claim rollback and commit, then recovery after commit. |
| V-4 | `RolePolicyToolBundleDispatchTests.the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt` | Committed queue row reused after pre-type interruption. R-2 supplies the other existing handoff cuts. |
| V-5 | `PreDispatchFailureDeliveryTests.Pre_dispatch_snapshot_survives_task_retry` | Two real failures preserve distinct event identities and immutable payloads, with a different root ID. |
| V-6 | `PreDispatchFailureDeliveryTests.Pre_dispatch_receipt_requires_complete_user_prompt` | Header-only decoy cannot confirm; the actual full submitted prompt confirms. |
| V-7 | `PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_retains_authoritative_row` | Canceled/missing/exhausted row is not replaced; same-row fixture restoration delivers once. |
| V-8 | `PreDispatchFailureDeliveryTests.Pre_dispatch_recovery_waits_for_destination` | Stopped parent remains owed for five minutes, then recovery delivers. |
| R-1 | `RolePolicyToolBundleDispatchTests.the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt` | Four existing paths preserve full recipient delivery and keyed ownership. |
| R-2 | `ReceiptFailureDeliveryTests.Caller_failure_obligation_survives_persistence_cuts` | Twelve existing watchdog cuts retain explicit notification IDs and complete receipt. |
| R-3 | `AgentTaskDispatchFailureTests.a_dispatch_failure_owns_a_durable_obligation` | New failure owns an outbox row and no legacy reminder. |
| R-3 | `AgentTaskDispatchFailureTests.a_lost_failure_note_is_re_sent_on_the_ramp` | Historical no-outbox row retains its reminder. |
| R-4 | `DelegationDispatchRecoveryBoundaryTests.C1186_Recovered_failure_preserves_obligation_guidance` | Post-dispatch guidance survives direct and recovered delivery, with no new outbox row. |
| R-5 | `PreDispatchFailureDeliveryTests.Pre_dispatch_excluded_routes_create_no_obligation` | None with a parent and Session without a parent create no caller obligation. |

Plan correction: AgentTaskReplyTo has only None and Session. None means board-only.
The original design's separate Board variant is not an available product route;
R-5 therefore has two results, not three. No enum or routing behavior is added.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes |
|---|---|---|---|---|---|---|---:|---:|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1232-red/` | red | `/*/*/PreDispatchFailureDeliveryTests/Pre_dispatch_failed_commit_keeps_the_caller_obligation*` | V-1 | one assertion red: no durable obligation after Failed commit | 1 | 4 |
| CP-2 | S2-S3 | `tests/Antiphon.Tests -> bin-c1232-green/` | new-behaviors | `/*/*/PreDispatchFailureDeliveryTests/(Pre_dispatch_failed*)\|(Preflight_failure_obligation_commits_with_its_claim*)\|(Pre_dispatch_excluded_routes_create_no_obligation*)\|(Pre_dispatch_snapshot_survives_task_retry*)\|(Pre_dispatch_receipt_requires_complete_user_prompt*)\|(Pre_dispatch_recovery_retains_authoritative_row*)\|(Pre_dispatch_recovery_waits_for_destination*)` | V-1, V-2, V-3, V-5, V-6, V-7, V-8, R-5 | all 13 cases, zero failures/skips | 13 | 5 |
| CP-3 | S2-S3 | CP-2 | existing-contracts | `/*/*/(RolePolicyToolBundleDispatchTests*)\|(ReceiptFailureDeliveryTests*)\|(AgentTaskDispatchFailureTests*)\|(DelegationDispatchRecoveryBoundaryTests*)\|(TestClassificationGuardTests*)/(the_dispatcher_refusal_reaches_the_caller_as_one_complete_user_prompt*)\|(Caller_failure_obligation_survives_persistence_cuts*)\|(a_dispatch_failure_owns_a_durable_obligation*)\|(a_lost_failure_note_is_re_sent_on_the_ramp*)\|(C1186_Recovered_failure_preserves_obligation_guidance*)\|(Registry_matches_compiled_metadata*)` | R-1, R-2, R-3, R-4, V-4 | all 23 cases, zero failures/skips | 23 | 5 |

The added Slow class owns a reason-adjacent allowlist row. CP-3 also runs the
exact metadata registry guard (one Unit method, not the Unit lane) to validate
that new classification. This is the reason for its additional 23rd result.

### Cost

Estimated 14 minutes including two isolated builds. Use checkpoint `run` with
`--row-timeout 40m`, committed `--expected-source-sha`, no host/platform pin,
and wait to terminal completion. Red receipts are retained without relabeling.
