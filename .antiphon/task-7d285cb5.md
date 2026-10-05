S6 implemented and ordinarily qualified: 121/121 tests passed; zero failures/skips. Ready for ordinary Review.

Original Code task / landing owner: 7d285cb5-092c-4b9e-a90a-90dd34e505ac.
Branch: feat/card-task-7d285cb5. Worktree: /work/worktrees/task-7d285cb5.
Task base: ea1c9598c221843bae23398305bca39902d2ff95.
Implementation commit: be96960c25689cfe8d4e229aa12f7a8bcea64f0a (verification pending at that point).
Repair / actual tested SHA: 635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1.
The final publication SHA is supplied in the settlement report/progress marker; the later commit contains only this report and the plan qualification/PC-variant note. These receipts are not relabeled to that report commit.

Plan and executable checkpoint manifest: /work/worktrees/task-7d285cb5/docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md, S6 amendment and After=S6 CP-34..CP-40.
Evidence: /work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/report.md and report.json; fresh per-row run.trx, console logs and build logs stay ignored in that run directory.
Full report: /work/worktrees/task-7d285cb5/.antiphon/task-7d285cb5.md.

Changed production files: ChannelReplyDispatcher.cs, ChannelOutboundDiscoveryService.cs, ChannelOutboundService.cs, ChannelOutboundDeliveryPump.cs under server/Application/Services.
Changed tests: new Application/ChannelOutboundTrailingRecoveryTests.cs; Application/ChannelOutboundDispatchIntegrationTests.cs updates the enabled main-silence expectation to its Suppressed root. The plan records the executable S6 selection, eight-method roster and pending controls.

Catalog-backed enabled paths reserve trailing intervals and advance their per-target root cursor in the same transaction before preparation. Discovery enumerates committed roots independently of process-local dispatch state, with fair 32-row pages / ten pages per root cycle. Event dispatch visits at most 32 roots. Main NO_REPLY owns a Suppressed root and atomically settles its members; silent/held machine tails own Suppressed intervals with null PublishedAt. Machine NO_REPLY itself remains unowned/unsettled. Next UserPrompt/submitted QueuedUserPrompt caps each original window; closing a root uses its version/cursor fence and never drops an already reserved child. Tail route/profile and authorization behavior use the existing outbound path/pump. Suppressed is terminal for ordering. Legacy/default-off and catalog-less behavior is retained.

No migration, S7-S9 implementation, operational activation or timeout/assertion relaxation. UnifiedRecoveryEnabled remains false by default. PreparationDeadlineAt still uses enqueue CreatedAt; S8 owns its loss incident. CARD-1061's hard-link gap remains unfixed.

Verification profile: Final for the explicitly assigned S6 closed scope. The brief expressly excludes whole Unit and whole Final Unit; its specific exclusion governs the generic profile text. No assembly/namespace/Unit or Linux Herdr run was performed. All named affected classes below ran in full. S6 required no operational manual acceptance. Tests use isolated PostgreSQL and a fake producer receiver; they do not claim real broker/gateway/provider or process-death evidence. Those remain at S12.

Command to rerun the S6 group (use the intended committed source SHA):

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c519-s6 -- dotnet run --no-build --project tools/Antiphon.Checkpoints -- run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --after S6 --expected-source-sha 635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 --row-timeout 15m --total-timeout 60m --serial --max-wait 50s
```

Await exit 75 with the same gated tool's wait --run <run-id> --max-wait 50s until terminal. Every owned command was awaited; final executor exited and its tool shadow was removed.

CP outcome: CP-34 8/8; CP-35 10/10; CP-36 16/16; CP-37 5/5; CP-38 38/38; CP-39 31/31 (Policy 29, Contract 2); CP-40 13/13. No failed/skipped rows. Seven actual isolated builds and seven filters, serial; maxConcurrentBuilds=1. The tool's summary says builds=40 because its manifest includes 33 unused build records; report.json confirms only the seven selected builds ran. Final wall 20m11s versus 42 estimated minutes; sum of selected build wall 808.3s; test-host wall 400.4s. All selected builds/rows slot=granted waited=0s. No repeats or known-flaky reruns.

Source receipt validation passed exit 0:

```text
CHECKPOINT SOURCE VALID source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 rows=7
```

Fresh native TRX was inspected for every intended method/class; each exact class count matched its manifest floor, including both classes in CP-39. Clean receipts have dirty=0, sourceState=clean and buildSource=verified. Generated TRX/JSON/logs remain ignored. The tool deleted manifest-owned alternate outputs; no task-owned bin-c519-* directories remain.

Unlisted setup/diagnostics: the gated checkpoint-tool bootstrap built only tools/Antiphon.Checkpoints (slot=granted waited=0s, held=9s, one existing CS8602 warning). Its first launcher invocation rejected an abbreviated SHA before launching any application build/test; it was corrected to the full SHA. Subsequent launch/wait/stop invocations used --no-build through the slot gate. They are tool lifecycle operations, not additional application test selections.

First attempt 20261005-053259-37bd at be96960c25689cfe8d4e229aa12f7a8bcea64f0a: first application build failed with 20 missing TranscriptKinds namespace references (one missing import), zero tests. CP-35's next build had begun when the run was stopped. The stop was awaited; waiter returned terminal exit 6, all owned children were gone before the repair. That stopped run is diagnostic, not qualified. One repair group added the missing import and corrected legacy-watermark comments. The full committed group was then green; no second repair round, no per-fix rebuild and no inherited-red claim.

V/R disposition, at this task's actual tested SHA:

| ID | Actual outcome / remaining scope |
|---|---|
| V-1 | Not run here; S1 schema qualification is historical, full-card requalification deferred. |
| V-2 | Capture class regression passed CP-37, 5/5; broader full-card qualification remains at owning slices. |
| V-3 | Not run here; S8 atomic-loss prerequisites pending. |
| V-4 | S6 dispatcher activation/catalog-less subset passed CP-35, 10/10; full S9 unified matrix deferred. |
| V-5 | Source discovery passed CP-36, 16/16; root discovery/restart/321-idle-root fairness passed CP-34. TTL/loss, metadata fairness, authoritative terminal-ingestion closure and full interceptor census remain deferred to S8/S9. |
| V-6 | Passed CP-34's eight methods: failed preparation reserves its interval, provider reconstruction, next-prompt caps, concurrent reservations, silent main/tail, atomic rollback, root fairness, machine/API policy. |
| V-7 | Not run here; S7 retry/fence matrix pending. |
| V-8 | Not run here; S8 atomic failure recording pending. |
| V-9 | Not run here; S9 acceptance/projection repair pending. |
| V-10 | Not run here; S10 retention pending. |
| V-11 | Not run here; S12a actual process-death/adapter matrix pending. |
| V-12 | Not run here; S12a publication-death matrix pending. |
| V-13 | Not run here; S12b real queue/broker/gateway matrix pending. |
| R-1 | Full dispatcher passed CP-35 10/10. Runtime Deferred method unchanged; prior S4 receipt only, not rerun or relabeled to this SHA. |
| R-2 | Not run here; S11a bridge/durability classes deferred. |
| R-3 | Not run here; matcher unchanged and prior S5 evidence only; full-card qualification deferred. |
| R-4 | Not run here; S11b machine/attachment classes deferred. |
| R-5 | Passed full Delivery CP-38 38/38 and Policy/Contract CP-39 31/31. |
| R-6 | Passed full Deadline CP-40 13/13. |
| R-7 | Not run here; storage unchanged, prior S3 evidence only; full-card qualification deferred. |
| R-8 | Not run here; S12b manual-recovery matrix deferred. |
| R-9 | Not run here; S12b composed transport deferred. |
| R-10 | Not run here; S11b batching deferred. |
| R-11 | Not run here; S10 retention method deferred. |
| R-12 | Not run here; S13 Windows-only bounded parity deferred. No Linux parity attempt. |

No deferred ID above is marked passed for its full-card obligation. No pending ordinary S6 work remains. No full-assembly run or unbounded class selection occurred.

Positive controls: all pending for post-land SourceLanding Mutation. No deliberate mutant, red/restore/green cycle or PC discharge occurred. New assertions observe committed database state and independent expected receiver content; the compile failure is not a positive-control red.

PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12, PC-13, PC-14, PC-15, PC-16, PC-17, PC-18, PC-19, PC-20, PC-21, PC-22, PC-23, PC-24, PC-25, PC-26, PC-27, PC-28, PC-29, PC-30, PC-31, PC-32, PC-33, PC-34, PC-35, PC-36, PC-37, PC-38, PC-39, PC-40, PC-41, PC-42, PC-43, PC-44, PC-45, PC-46, PC-47, PC-48, PC-49, PC-50, PC-51, PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-58, PC-59, PC-60, PC-61, PC-62, PC-63, PC-64, PC-65, PC-66, PC-67, PC-68, PC-69, PC-70, PC-71, PC-72, PC-73, PC-74, PC-75, PC-76, PC-77, PC-78, PC-79, PC-80, PC-81, PC-82, PC-83, PC-84, PC-85, PC-86, PC-87, PC-88, PC-89, PC-90, PC-91, PC-92, PC-93, PC-94, PC-95, PC-96, PC-97, PC-98, PC-99, PC-100 — all pending.
PC-1059-1, PC-1059-2, PC-1059-3, PC-1059-4, PC-1059-5, PC-1059-6 — all pending.
PC-S4-1 (default off), PC-S4-2 (catalog-less main), PC-S4-3 (catalog-less machine), PC-S4-4 (catalog-less trailing) — all pending.
Named S4 variants of PC-19 (main and machine activation), PC-15 (staged source), PC-22 (silence), PC-23 (origin) — all pending.
PC-S5-1 (event closure), PC-S5-2 (transactional closure), PC-S5-3 (complete machine batch), PC-S5-4 (original context time) — all pending.
PC-S6-1 (silent main root), PC-S6-2/root cursor, PC-S6-2/per-query page bound, PC-S6-2/maximum pages, PC-S6-3 (machine tail policy), PC-S6-4 (trailing API withholding), PC-S6-5 (pump terminal ordering) — all pending.
PC-11/38/39/40/41/99 have the new ordinary witnesses in the plan; original mappings and missing-control discovery remain Mutation-owned. PC-99 uses different start/end intervals so exact-start uniqueness cannot mask overlap when both lock and version guards are bypassed. PC-20's full atomic-loss witness remains later-slice pending under the S4 catalog-less contract.

Restart: none. Server/runner activation owner: caller; switch remains off. Caller commissions ordinary Review, then lands original Code task 7d285cb5 with expected pushed SHA and commissions SourceLanding Mutation. This delegate does not land or deploy.

Unedited CHECKPOINT lines from the validated report:

```text
CHECKPOINT CP-34 commit=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 build=ok filter=/*/*/ChannelOutboundTrailingRecoveryTests/* executed=8 passed=8 failed=0 skipped=0 trx=/work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/rows/CP-34/run.trx slot=granted waited=0s dirty=0 source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 sourceState=clean buildSource=verified
CHECKPOINT CP-35 commit=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 build=ok filter=/*/*/ChannelOutboundDispatchIntegrationTests/* executed=10 passed=10 failed=0 skipped=0 trx=/work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/rows/CP-35/run.trx slot=granted waited=0s dirty=0 source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 sourceState=clean buildSource=verified
CHECKPOINT CP-36 commit=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 build=ok filter=/*/*/ChannelOutboundDiscoveryTests/* executed=16 passed=16 failed=0 skipped=0 trx=/work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/rows/CP-36/run.trx slot=granted waited=0s dirty=0 source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 sourceState=clean buildSource=verified
CHECKPOINT CP-37 commit=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 build=ok filter=/*/*/ChannelOutboundCaptureTests/* executed=5 passed=5 failed=0 skipped=0 trx=/work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/rows/CP-37/run.trx slot=granted waited=0s dirty=0 source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 sourceState=clean buildSource=verified
CHECKPOINT CP-38 commit=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 build=ok filter=/*/*/ChannelOutboundDeliveryTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/rows/CP-38/run.trx slot=granted waited=0s dirty=0 source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 sourceState=clean buildSource=verified
CHECKPOINT CP-39 commit=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 build=ok filter=/*/*/(ChannelOutboundPolicyTests*)|(ChannelOutboundContractTests*)/* executed=31 passed=31 failed=0 skipped=0 trx=/work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/rows/CP-39/run.trx slot=granted waited=0s dirty=0 source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 sourceState=clean buildSource=verified
CHECKPOINT CP-40 commit=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 build=ok filter=/*/*/ChannelOutboundDeadlineTests/* executed=13 passed=13 failed=0 skipped=0 trx=/work/worktrees/task-7d285cb5/.antiphon/checkpoints/20261005-053654-b508/rows/CP-40/run.trx slot=granted waited=0s dirty=0 source=635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 sourceState=clean buildSource=verified
```

Inspected fresh native method roster:

CP-34 — ChannelOutboundTrailingRecoveryTests (8)

- C519_Pending_intervals_never_overlap — Passed
- C519_Restart_recovers_reserved_tail — Passed
- C519_Next_prompt_keeps_an_already_reserved_tail — Passed
- C519_Suppressed_tail_advances_cursor_without_publication — Passed
- C519_Concurrent_reservation_has_one_winner — Passed
- C519_Fair_root_budget_reaches_tail_behind_idle_roots — Passed
- C519_Tail_commit_failure_does_not_advance_root — Passed
- C519_Machine_tail_policy_and_api_withholding_survive_restart — Passed

CP-35 — ChannelOutboundDispatchIntegrationTests (10)

- Missing_catalog_main_publishes_once_and_settles_without_capture(False) — Passed
- Missing_catalog_main_publishes_once_and_settles_without_capture(True) — Passed
- Missing_catalog_machine_publishes_once_and_settles_without_capture(False) — Passed
- Missing_catalog_machine_publishes_once_and_settles_without_capture(True) — Passed
- Missing_catalog_trailing_publishes_once_without_capture(False) — Passed
- Missing_catalog_trailing_publishes_once_without_capture(True) — Passed
- Activation_captures_before_source_reads_and_publishes_the_staged_bytes — Passed
- Activation_preserves_machine_silence_and_origin_policy_before_capture — Passed
- Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes(False) — Passed
- Dispatcher_defers_only_the_bound_conversation_and_preserves_source_bytes(True) — Passed

CP-36 — ChannelOutboundDiscoveryTests (16)

- C519_Startup_recovers_without_signal — Passed
- C519_Timer_recovers_without_signal — Passed
- C519_Default_off_does_not_discover — Passed
- C519_Historical_match_requires_complete_prompt — Passed
- C519_Historical_match_requires_marker — Passed
- C519_Historical_match_requires_source_session — Passed
- C519_Historical_match_obeys_attempt_floors — Passed
- C519_Historical_native_time_obeys_original_attempt — Passed
- C519_Historical_withholding_does_not_hide_a_later_receipt — Passed
- C519_Historical_answer_stops_at_next_prompt — Passed
- C519_Machine_context_must_predate_injection — Passed
- C519_Discovery_closure_waits_for_complete_window — Passed
- C519_Closed_machine_source_cannot_be_captured — Passed
- C519_Complete_machine_batch_has_one_owner_for_every_member — Passed
- C519_Legacy_settled_sources_are_not_replayed — Passed
- C519_Fair_cursors_and_finite_source_budget — Passed

CP-37 — ChannelOutboundCaptureTests (5)

- C519_Capture_precedes_all_preparation — Passed
- C519_Batch_members_share_one_owner — Passed
- C519_Existing_source_owner_is_not_replaced — Passed
- C519_Growing_main_window_reuses_its_root — Passed
- C519_Reservation_and_cursor_commit_together — Passed

CP-38 — ChannelOutboundDeliveryTests (38)

- Pending_conversion_is_not_an_inbound_lost_reply — Passed
- Accepted_original_storage_failure_is_terminal_without_a_false_send(missing) — Passed
- Staging_io_failure_leaves_correlation_owed_and_same_source_retry_admits_once — Passed
- Serialized_payload_budget_includes_all_fields — Passed
- Fallback_annotation_uses_actual_wire_budget_and_overcap_keeps_stamps_null — Passed
- Expired_lease_takeover_fences_the_old_owner_before_producer_invocation — Passed
- Deferred_is_returned_only_after_intent_and_correlation_commit — Passed
- Main_trailing_and_machine_use_the_same_policy(main, unrelated-zip, MarkdownSources, False) — Passed
- Concurrent_admission_respects_max_pending_per_channel — Passed
- Two_dispatchers_preserve_source_window_and_destination_identity_under_admission_race — Passed
- Two_pumps_create_one_linked_task_and_expired_lease_takeover_fences_old_owner(before-conversion-create) — Passed
- Two_pumps_create_one_linked_task_and_expired_lease_takeover_fences_old_owner(conversion-task-committed) — Passed
- Accepted_target_is_not_retried_when_a_second_target_fails — Passed
- Source_publication_requires_all_four_members_or_a_complete_zip(False, False) — Passed
- Source_publication_requires_all_four_members_or_a_complete_zip(False, True) — Passed
- Source_publication_requires_all_four_members_or_a_complete_zip(True, False) — Passed
- Accepted_original_storage_failure_is_terminal_without_a_false_send(corrupt) — Passed
- Source_publication_requires_all_four_members_or_a_complete_zip(True, True) — Passed
- Only_acceptance_stamps_complete_actual_payload(blocked-then-accepted) — Passed
- Only_acceptance_stamps_complete_actual_payload(two-refusals-then-accepted) — Passed
- Only_acceptance_stamps_complete_actual_payload(ambiguous) — Passed
- Gates_precede_admission_and_a_matched_companion_still_converts(NO_REPLY) — Passed
- Gates_precede_admission_and_a_matched_companion_still_converts(operator) — Passed
- Gates_precede_admission_and_a_matched_companion_still_converts(api-error) — Passed
- Disallowed_plain_text_machine_turn_stays_out_of_admission — Passed
- Real_control_callers_bypass_the_selected_profile(proactive) — Passed
- Real_control_callers_bypass_the_selected_profile(digest) — Passed
- Later_prompt_thread_and_control_cannot_retarget_pending_reply — Passed
- Real_control_callers_bypass_the_selected_profile(incident) — Passed
- Main_trailing_and_machine_use_the_same_policy(main, explicit-md, MarkdownSources, True) — Passed
- Main_trailing_and_machine_use_the_same_policy(trailing, explicit-md, MarkdownSources, True) — Passed
- Main_trailing_and_machine_use_the_same_policy(machine, explicit-md, MarkdownSources, True) — Passed
- Main_trailing_and_machine_use_the_same_policy(machine, manifest-zip, MarkdownSources, True) — Passed
- Main_trailing_and_machine_use_the_same_policy(main, plain-markdown, EveryAgentReply, True) — Passed
- Main_trailing_and_machine_use_the_same_policy(main, plain-markdown, MarkdownSources, False) — Passed
- Only_acceptance_stamps_complete_actual_payload(three-refusals) — Passed
- Real_control_callers_bypass_the_selected_profile(alert) — Passed
- Main_trailing_and_machine_use_the_same_policy(main, plain-markdown, none, False) — Passed

CP-39 — ChannelOutboundContractTests (2), ChannelOutboundPolicyTests (29)

- Model_keeps_optional_binding_and_unique_delivery_links — Passed
- Server_and_gateway_keep_source_and_transport_boundaries — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(text_only, False) — Passed
- Revocation_and_rebinding_revalidate_before_launch_and_publish — Passed
- Invalid_profile_bindings_are_atomic_failures(disabled) — Passed
- Invalid_profile_bindings_are_atomic_failures(unbound) — Passed
- Invalid_profile_bindings_are_atomic_failures(inbound_project) — Passed
- Invalid_profile_bindings_are_atomic_failures(converter_project) — Passed
- Invalid_profile_bindings_are_atomic_failures(missing_converter) — Passed
- Invalid_profile_bindings_are_atomic_failures(same_agent) — Passed
- Invalid_profile_bindings_are_atomic_failures(raw_converter) — Passed
- Invalid_profile_bindings_are_atomic_failures(pool_converter) — Passed
- Invalid_profile_bindings_are_atomic_failures(always_on_converter) — Passed
- Invalid_profile_bindings_are_atomic_failures(converter_is_inbound_elsewhere) — Passed
- Invalid_profile_bindings_are_atomic_failures(missing_prompt) — Passed
- Invalid_profile_bindings_are_atomic_failures(missing_workspace) — Passed
- Invalid_profile_bindings_are_atomic_failures(escaping_prompt) — Passed
- Profile_limits_refuse_out_of_range_values — Passed
- Text_only_conversion_failure_does_not_claim_source_attachments — Passed
- Markdown_source_trigger_requires_an_attached_manifested_zip — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(inline_markdown, True) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(uppercase_markdown, True) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(missing_inline_bytes, False) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(manifested_zip, True) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(unlisted_zip, False) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(zip_without_bytes, False) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(zip_without_manifest, False) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(wrong_manifest_version, False) — Passed
- Markdown_trigger_matches_only_authorized_source_shapes(non_source_zip_member, False) — Passed
- Profile_binding_defaults_and_validation — Passed
- Same_name_prompt_edits_affect_only_new_admissions — Passed

CP-40 — ChannelOutboundDeadlineTests (13)

- Linked_worker_expires_before_selection_or_at_the_final_dispatch_claim(False) — Passed
- Linked_worker_expires_before_selection_or_at_the_final_dispatch_claim(True) — Passed
- One_converter_and_two_global_seats_hold_pending_work_until_its_original_deadline — Passed
- Missing_and_unavailable_converter_fall_back_without_creating_a_task — Passed
- Real_create_refusals_keep_the_original_without_provider_reroute(quota, subscription) — Passed
- Real_create_refusals_keep_the_original_without_provider_reroute(authentication, sign) — Passed
- Real_create_refusals_keep_the_original_without_provider_reroute(model, disabled) — Passed
- Blocked_failed_and_missing_output_keep_original_bytes_and_record_distinct_reasons — Passed
- Queued_to_working_race_preserves_the_owner_and_sends_once_at_deadline — Passed
- Resume_held_before_conversion_preserves_work_or_annotates_expired_original(False) — Passed
- Resume_held_before_conversion_preserves_work_or_annotates_expired_original(True) — Passed
- Deadline_crossing_the_final_creation_barrier_never_launches_a_worker — Passed
- Deadline_equality_cancels_only_queued_worker_and_ignores_late_success — Passed


The full task-base..final-HEAD evidence-policy guard is run after the report commit and its actual result is included in the settlement summary; the preliminary base..635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1 guard passed exit 0 with commits=2, entries=0, violations=0.

--- next stage ---
next: review
handoff: Review S6 at the pushed tip; ordinary CP-34..CP-40 passed 121/121 at 635baa68fe6d67f5a0eb0014dc9cc77ccc1999b1. Verify durable tails, silent-root ordering, default-off/catalog-less compatibility and pending controls. Caller lands original Code task 7d285cb5 after Review, then commissions SourceLanding Mutation.
artifact: docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md
