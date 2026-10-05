# CARD-0519 S12 continuation — 9bbc9566

Incomplete: test-boundary repairs and new companion cases are pushed, but ordinary verification remains red. The two authorized repair rounds are exhausted. Next is Code, not Review. No production implementation changed.

Original Code task / landing owner: 5724b53e-5bef-4b72-b092-b98d36a47d07. This continuation is adopted into that owner; a later Review must name task 9bbc9566 as its subject.
Branch: feat/card-task-9bbc9566.
Worktree: /work/worktrees/task-9bbc9566.
Desktop counterpart (not accessed): C:\Antiphon\worktrees\card-task-9bbc9566.
Task base: 1011c7b5fec04157c2078500057f0c3737288a42.
Final ordinary tested source: 40fed4eaff28efdb96b1208b9dcf6616eee31028.
Plan: docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md.
The final caller response records the pushed report-only tip.

## Changes

- ChannelOutboundUnifiedTransportTests: confirmation-save injection now fences Delivered/LateConfirmed, independently observes a full exact UserPrompt and the previous unconfirmed persisted verdict, then exercises late confirmation without retyping.
- Added partial-prompt rejection/full-receipt companion and enabled loss notice through the real Kafka gateway/Slack adapter. These and actual broker-size refusal pass at the final tested source.
- Converter cuts are eight independent native results. Busy behavior is now attached to the converter's actual session after the launch restart boundary, with zero submissions and Pending/zero-attempt brief checks before its turn ends.
- Repaired duplicate source TurnEnd, delegation fixture Git graph, and converter brief identity (ExecutionTaskId).
- UnifiedOutboundTransport accepts an optional real advancing test clock; unchanged callers retain their clock behavior.
- Plan CP-26 includes all eleven native results. Final CP-19/25/26/27/28/60/61 share one isolated build under S12Final, with original exact filters and row deadlines. V-13 roster is now fourteen native results.

UnifiedRecoveryEnabled remains false by default. Restart: none; caller owns eventual server/runner activation. No deployment or live configuration change.
ANTIPHON_TASK_TOKEN was present. GET /api/runner-defaults and /api/session-runners were read; no runner/OS pin or fleet location was embedded.

## Remaining defects and continuation

1. CP-25 main/trailing: refusal-save fault targets SaveChanges, but ChannelOutboundDeliveryPump.FinishAttemptAsync uses ExecuteUpdateAsync. The expected Publishing state is actually Ready. Correct the fault at the real write boundary and independently prove it fired; retain the assertion and deadline.
2. CP-25 machine: Check remains Pending because the late NO_REPLY text makes the source session working. Complete the source turn at the correct point before attempting the machine injection. Do not seed a Sent row. Since each parameterized method stops at its first failed internal scenario, the entire 72-scenario / 108-recovery-launch matrix is unproven.
3. Converter task-committed/result-committed, idle and busy: the fixture's default 900-byte inline brief ceiling spills the task brief, while the oracle requires the full frozen goal inline. Configure the intended fake transport profile or implement the plan's complete recipient evidence without loosening the full-goal/request assertions. These fail before result settlement/publication.
4. Converter dispatch-committed, idle and busy: after real ResumeInterruptedLaunchAsync, the session is Running but there is no linked delegation brief (ExecutionTaskId). The dispatcher commits its claim before it queues the brief; resume only flushes existing rows. This production gap reproduces at base with test-only overlays.
5. Converter enqueue-refused, idle and busy: the launch sink refusal reaches AgentTaskDispatcher's generic failure path, leaving AgentTaskStatus.Failed instead of recoverable Dispatched. This also reproduces at base. Repair actual launch/brief recovery; do not seed the missing brief or suppress the failed-state assertion.

Production repairs were not attempted after the second authorized repair round. They require a new Code dispatch with the affected session/dispatcher integration coverage. Re-run red rows after repair, and CP-23/24 once if production changes. Check CP-25's full matrix cost and partition its methods/manifest if it cannot fit its existing 15-minute row cap; never widen that cap.

## Verification provenance and bounds

All build/test drivers used host build slots. There was one unlisted gated checkpoint-CLI bootstrap, needed because the assigned checkout had no built CLI. It used bin-c519-tool/. Control-only wait/validate/clean calls do not build or run tests. No whole assembly or namespace selection, deliberate mutation, loaded repetition, or reassurance rerun was used.

Initial CP-26: bbb9c9babdd18f5be0c43de191962c48fb8e6787, 11 executed / 2 passed / 9 failed. The extra TurnEnd hid the owning prompt. Repair 1: 5435e6d1300ac696b6f016e06e2f2669c8d01aba; 11 / 3 / 8, exposing missing fixture Git DI and converter failures. Repair 2: cb3cc4b55 (full SHA in commit ledger below), fixes graph/key/busy boundary. Final manifest-only grouping commit: 40fed4eaff28efdb96b1208b9dcf6616eee31028.

One initial repair-run invocation supplied a mistyped expected SHA; strict source checking refused it before any build/test. It is not evidence and was corrected to git rev-parse HEAD's actual SHA.

The brief said both "no whole-Unit" and "run CP-60", while the later Final profile and CP-60 require the whole Unit lane. A clarification was requested; absent a response, the explicit Final/CP-60 selection was used.

Final ordinary run: .antiphon/checkpoints/20261005-190535-1ae0/, all seven selected rows at the single committed SHA above, serial, row timeout 15m, total timeout narrowed to 24m to leave cleanup/reporting inside the 50-minute task budget.
Earlier runs: .antiphon/checkpoints/20261005-184958-6f3d/ and .antiphon/checkpoints/20261005-185543-e71e/.
Generated TRX, logs and JSON remain gitignored. Only this individual Markdown report is committed.

Baseline comparison: detached 1011c7b5fec04157c2078500057f0c3737288a42 with exactly two test-only overlays (UnifiedTransportTests and UnifiedOutboundTransport), no production changes. The attempted argument-specific filter matched zero tests and was rejected as evidence. Corrected precise method filter selected all eight converter cases: 0 passed / 8 failed. Actual dispatch/brief defects and the inline-brief fixture mismatch reproduce. This dirty overlay is diagnostic, never a clean certificate. The build stamp is verified; full dirty fingerprint and unedited lines follow.
Evidence: .antiphon/baseline-evidence/BASE-C519-HANDOFF-20261005-190319-d334/ and BASE-C519-HANDOFF-METHOD-20261005-190615-2a3b/.
The comparison worktree and its 28 owned bin-c519-base directories were removed after all children exited.

## V/R ledger

- V-1, V-2, V-3, V-4, V-5, V-6, V-7, V-8, V-9, V-10: assigned to prior S1–S11 slices; no new credit claimed here.
- V-11 / CP-23: predecessor observed 12/12 at 8607b06a891772d4e2588577b0bd724ca9949036; its interrupted multirow run lacked a completed schema-2 certificate. Not rerun here because production did not change, per continuation brief.
- V-12 / CP-24: predecessor observed 9/9 at that same SHA, with the same certificate limitation. Not rerun here.
- V-13 / CP-25/26: FAILED/incomplete. Partial receipt, enabled notice and size refusal companions pass, but full queue and converter matrices do not.
- R-1, R-2, R-3, R-4, R-6, R-7, R-10, R-11: prior slice ownership, not newly credited.
- R-5: affected full ChannelOutboundDeliveryTests / CP-19 passed 38/38.
- R-8: see CP-28 and full-class CP-61 final rows below.
- R-9: see CP-27 final row below.
- R-12 / CP-29: Windows S13, caller-owned and not executed here.
- Final Unit: see CP-60 below.
This is an incomplete Final round, not Interim qualification. No deferred ID is marked passed. No separate live/manual activation was authorized; S12 recipient/manual-retry acceptance is through the named ordinary tests, and V-13 remains incomplete.

## Mutation

Every plan PC and variant remains pending for method-scoped post-land SourceLanding Mutation. Ordinary fault injection and base comparisons discharge none. Mutation owns deliberate red/restore/green and missing-control discovery, including controls for the new companions. PC-89..95 retain every main/trailing/machine cut; PC-96 retains all four converter cuts x idle/busy. Full identifier/variant inventory follows.


Pending IDs: PC-1, PC-2, PC-3, PC-4, PC-5, PC-6, PC-7, PC-8, PC-9, PC-10, PC-11, PC-12, PC-13, PC-14, PC-15, PC-16, PC-17, PC-18, PC-19, PC-20, PC-21, PC-22, PC-23, PC-24, PC-25, PC-26, PC-27, PC-28, PC-29, PC-30, PC-31, PC-32, PC-33, PC-34, PC-35, PC-36, PC-37, PC-38, PC-39, PC-40, PC-41, PC-42, PC-43, PC-44, PC-45, PC-46, PC-47, PC-48, PC-49, PC-50, PC-51, PC-52, PC-53, PC-54, PC-55, PC-56, PC-57, PC-58, PC-59, PC-60, PC-61, PC-62, PC-63, PC-64, PC-65, PC-66, PC-67, PC-68, PC-69, PC-70, PC-71, PC-72, PC-73, PC-74, PC-75, PC-76, PC-77, PC-78, PC-79, PC-80, PC-81, PC-82, PC-83, PC-84, PC-85, PC-86, PC-87, PC-88, PC-89, PC-90, PC-91, PC-92, PC-93, PC-94, PC-95, PC-96, PC-97, PC-98, PC-99, PC-100, PC-1059-1, PC-1059-2, PC-1059-3, PC-1059-4, PC-1059-5, PC-1059-6, PC-S4-1, PC-S4-2, PC-S4-3, PC-S4-4, PC-S5-1, PC-S5-2, PC-S5-3, PC-S5-4, PC-S6-1, PC-S6-2, PC-S6-3, PC-S6-4, PC-S6-5, PC-S7-1, PC-S8-1, PC-S8-2, PC-S9-1, PC-S10-1a, PC-S10-1b, PC-S10-1c, PC-S10-1d, PC-S10-1e, PC-S10-2a, PC-S10-2b, PC-S10-2c, PC-S10-3a, PC-S10-3b, PC-S10-3c.

Pending variant labels: PC-1; PC-1..PC-5 (S methods; schema regeneration and fresh migration each phase); PC-10; PC-100; PC-1059-1 / roots; PC-1059-2 / traversal; PC-1059-3 / file and directory links; PC-1059-4 / pre-read budget; PC-1059-5 / Linux regular type; PC-1059-6 / Linux growing-file budget; PC-11; PC-12; PC-13; PC-14; PC-15; PC-15 / S4 staged source; PC-16; PC-17; PC-18; PC-19; PC-19 / S4 main and machine activation; PC-2; PC-20; PC-21; PC-22; PC-22 / S4 silence; PC-23; PC-23 / S4 origin; PC-24; PC-25; PC-26; PC-27; PC-28; PC-29; PC-3; PC-30; PC-31; PC-32; PC-33; PC-34; PC-35; PC-36; PC-37; PC-38; PC-39; PC-4; PC-40; PC-41; PC-42; PC-43; PC-44; PC-45; PC-46; PC-47; PC-48; PC-49; PC-5; PC-50; PC-51; PC-52; PC-53; PC-54; PC-55; PC-56; PC-57; PC-58; PC-59; PC-6; PC-6..PC-88 and PC-97..PC-100 (the remaining exact methods); PC-60; PC-61; PC-62; PC-63; PC-64; PC-65; PC-66; PC-67; PC-68; PC-69; PC-7; PC-70; PC-71; PC-72; PC-73; PC-74; PC-75; PC-76; PC-77; PC-78; PC-79; PC-8; PC-80; PC-81; PC-82; PC-83; PC-84; PC-85; PC-86; PC-87; PC-88; PC-89; PC-89..PC-95 (X methods; three path results plus internal crash cuts per phase); PC-9; PC-90; PC-91; PC-92; PC-93; PC-94; PC-95; PC-96; PC-96 (W.C519_Converter_handoff; real queue/broker/recipient per phase); PC-97; PC-98; PC-99; PC-S4-1 / default off; PC-S4-2 / catalog-less main; PC-S4-3 / catalog-less machine; PC-S4-4 / catalog-less trailing; PC-S5-1 / event closure; PC-S5-2 / transactional closure; PC-S5-3 / complete machine batch; PC-S5-4 / original context time; PC-S6-1 / silent main root; PC-S6-2 / root fair budget; PC-S6-3 / machine trailing policy; PC-S6-4 / trailing API withholding; PC-S6-5 / terminal ordering; PC-S7-1 / null accepted lease; PC-S8-1 / SentAt origin; PC-S8-2 / recording-only preparation repair. Each range/variant in the authoritative plan is pending, including all parameter values.

## Final row results and qualification limit

| Row | Executed | Passed | Failed | Skipped | Outcome |
|---|---:|---:|---:|---:|---|
| CP-19 | 38 | 38 | 0 | 0 | Observed green |
| CP-25 | 3 | 0 | 3 | 0 | Red |
| CP-26 | 11 | 3 | 8 | 0 | Red |
| CP-27 | 1 | 1 | 0 | 0 | Observed green |
| CP-28 | 4 | 4 | 0 | 0 | Observed green |
| CP-60 | unknown | unknown | unknown | unknown | Total timeout; no final TRX |
| CP-61 | not run | not run | not run | not run | Skipped after total timeout |

Final wait completed exit 5 at 24m01s; no checkpoint run remains in flight. Fresh TRX rosters were inspected for every completed row (listed below), all nonzero. CP-60 console reports two failures and Windows-only skips, but lacks final counters: no total inferred from console. CP-61 full class remains mandatory; CP-28's four cases do not replace it. R-8 full-class qualification remains incomplete; R-9 observed green.

The schema-2 final report exists, but validator for even CP-19/27/28 returned exit 2: `CHECKPOINT SOURCE INVALID reason=report_source_ineligible`. The timeout leaves aggregate buildSource=unknown. Completed rows individually record clean SHA-validated start/end and verified build provenance; nevertheless **no validated schema-2 final qualification is claimed**, and the report/receipts were not edited. A subsequent completed run must supply eligible evidence.

CP-60 failures reproduced independently at untouched clean base 1011c7b5fec04157c2078500057f0c3737288a42: TestClassificationGuardTests.Registry_matches_compiled_metadata (missing reason for DirectoryLinkFixtureWindowsTests), and SpecialistRoleContractTests.no_Check_comparison_survives_outside_the_allowlist (TerminalRunnerSeatReleasePolicy.cs:34). Each exact method executed once, failed once, skipped zero, with verified build provenance and dirty=0. These unlisted exact baseline comparisons establish inherited red; no baseline assembly rerun. The second baseline worktree and its 28 bin-c519-base-unit directories were removed after both runs exited.

Build slot evidence: all completed final rows slot=granted; CP-25 waited=75s, others waited=0s. Timeout-generated CP-60 receipt reports slot=skipped and unknown provenance; it is preserved literally, not used to claim the actually started lane was unleased. All baseline rows slot=granted waited=0s. The plan tool's builds=55 counts manifest build declarations; only one shared final build actually ran.

## Commit ledger

- bbb9c9babdd18f5be0c43de191962c48fb8e6787 — initial transport guards and companion tests; verification pending, then red.
- 5435e6d1300ac696b6f016e06e2f2669c8d01aba — repair 1 prompt ownership; CP-26 3/11 green.
- cb3cc4b556e06559d9cf477381bb41eec8dc27e0 — repair 2 graph/key/busy boundary; ordinary verification remains red.
- 40fed4eaff28efdb96b1208b9dcf6616eee31028 — final manifest grouping; actual tested SHA.

All slices pushed fast-forward. Final report-only tip is in caller response. Source remained frozen throughout all long runs. No timeout or assertion was widened/loosened. Unfinished CP-25/26/60/61 and controls are not passed.

## Unedited CHECKPOINT lines

```text
CHECKPOINT CP-26 commit=bbb9c9babdd18f5be0c43de191962c48fb8e6787 build=ok filter=/*/*/ChannelOutboundUnifiedTransportTests/(C519_Size_refusal*)|(C519_Converter_handoff*)|(C519_Partial_prompt_requires_complete_receipt*)|(C519_Enabled_loss_notice_reaches_adapter*) executed=11 passed=2 failed=9 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/checkpoints/20261005-184958-6f3d/rows/CP-26/run.trx slot=granted waited=0s dirty=0 source=bbb9c9babdd18f5be0c43de191962c48fb8e6787 sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=5435e6d1300ac696b6f016e06e2f2669c8d01aba build=ok filter=/*/*/ChannelOutboundUnifiedTransportTests/(C519_Size_refusal*)|(C519_Converter_handoff*)|(C519_Partial_prompt_requires_complete_receipt*)|(C519_Enabled_loss_notice_reaches_adapter*) executed=11 passed=3 failed=8 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/checkpoints/20261005-185543-e71e/rows/CP-26/run.trx slot=granted waited=0s dirty=0 source=5435e6d1300ac696b6f016e06e2f2669c8d01aba sourceState=clean buildSource=verified
CHECKPOINT CP-19 commit=40fed4eaff28efdb96b1208b9dcf6616eee31028 build=ok filter=/*/*/ChannelOutboundDeliveryTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/checkpoints/20261005-190535-1ae0/rows/CP-19/run.trx slot=granted waited=0s dirty=0 source=40fed4eaff28efdb96b1208b9dcf6616eee31028 sourceState=clean buildSource=verified
CHECKPOINT CP-25 commit=40fed4eaff28efdb96b1208b9dcf6616eee31028 build=reused filter=/*/*/ChannelOutboundUnifiedTransportTests/C519_Queue_to_adapter* executed=3 passed=0 failed=3 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/checkpoints/20261005-190535-1ae0/rows/CP-25/run.trx slot=granted waited=75s dirty=0 source=40fed4eaff28efdb96b1208b9dcf6616eee31028 sourceState=clean buildSource=verified
CHECKPOINT CP-26 commit=40fed4eaff28efdb96b1208b9dcf6616eee31028 build=reused filter=/*/*/ChannelOutboundUnifiedTransportTests/(C519_Size_refusal*)|(C519_Converter_handoff*)|(C519_Partial_prompt_requires_complete_receipt*)|(C519_Enabled_loss_notice_reaches_adapter*) executed=11 passed=3 failed=8 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/checkpoints/20261005-190535-1ae0/rows/CP-26/run.trx slot=granted waited=0s dirty=0 source=40fed4eaff28efdb96b1208b9dcf6616eee31028 sourceState=clean buildSource=verified
CHECKPOINT CP-27 commit=40fed4eaff28efdb96b1208b9dcf6616eee31028 build=reused filter=/*/*/ChannelOutboundComposedTransportTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/checkpoints/20261005-190535-1ae0/rows/CP-27/run.trx slot=granted waited=0s dirty=0 source=40fed4eaff28efdb96b1208b9dcf6616eee31028 sourceState=clean buildSource=verified
CHECKPOINT CP-28 commit=40fed4eaff28efdb96b1208b9dcf6616eee31028 build=reused filter=/*/*/ChannelOutboundRecoveryTests/(Expired_publishing_lease_is_uncertain_until_explicit_retry*)|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)|(Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head*)|(Broker_ack_before_process_death_remains_uncertain_without_replay*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/checkpoints/20261005-190535-1ae0/rows/CP-28/run.trx slot=granted waited=0s dirty=0 source=40fed4eaff28efdb96b1208b9dcf6616eee31028 sourceState=clean buildSource=verified
CHECKPOINT CP-60 commit=40fed4eaff28efdb96b1208b9dcf6616eee31028 build=n/a filter=/*/*/*/*[Category=Unit] executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a timeout=total slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT CP-61 commit=40fed4eaff28efdb96b1208b9dcf6616eee31028 build=n/a filter=/*/*/ChannelOutboundRecoveryTests/* executed=n/a passed=n/a failed=n/a skipped=n/a trx=n/a slot=skipped waited=0s dirty=unknown source=unknown sourceState=unknown buildSource=unknown
CHECKPOINT BASE-C519-UNIT-GUARD commit=1011c7b5fec04157c2078500057f0c3737288a42 build=ok filter=/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/baseline-evidence/BASE-C519-UNIT-GUARD-20261005-192536-5fac/run.trx slot=granted waited=0s dirty=0 source=1011c7b5fec04157c2078500057f0c3737288a42 sourceState=clean buildSource=verified
CHECKPOINT BASE-C519-ROLE-GUARD commit=1011c7b5fec04157c2078500057f0c3737288a42 build=reused filter=/*/*/SpecialistRoleContractTests/no_Check_comparison_survives_outside_the_allowlist* executed=1 passed=0 failed=1 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/baseline-evidence/BASE-C519-ROLE-GUARD-20261005-192906-a6ee/run.trx slot=granted waited=0s dirty=0 source=1011c7b5fec04157c2078500057f0c3737288a42 sourceState=clean buildSource=verified
CHECKPOINT BASE-C519-HANDOFF commit=1011c7b5fec04157c2078500057f0c3737288a42 build=ok filter=/*/*/ChannelOutboundUnifiedTransportTests/(C519_Converter_handoff*conversion-dispatched*)|(C519_Converter_handoff*enqueue-refused*) executed=0 passed=0 failed=0 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/baseline-evidence/BASE-C519-HANDOFF-20261005-190319-d334/run.trx slot=granted waited=0s dirty=2 source=1011c7b5fec04157c2078500057f0c3737288a42+dirty:15a5c72c8a7be55eff8f2b4ba90e209574776b8f30ef0ba42433eeea1280c221 sourceState=dirty buildSource=verified
CHECKPOINT BASE-C519-HANDOFF-METHOD commit=1011c7b5fec04157c2078500057f0c3737288a42 build=reused filter=/*/*/ChannelOutboundUnifiedTransportTests/C519_Converter_handoff* executed=8 passed=0 failed=8 skipped=0 trx=/work/worktrees/task-9bbc9566/.antiphon/baseline-evidence/BASE-C519-HANDOFF-METHOD-20261005-190615-2a3b/run.trx slot=granted waited=0s dirty=2 source=1011c7b5fec04157c2078500057f0c3737288a42+dirty:15a5c72c8a7be55eff8f2b4ba90e209574776b8f30ef0ba42433eeea1280c221 sourceState=dirty buildSource=verified
```

## Fresh final TRX roster

```text
CP-19	Passed	Pending_conversion_is_not_an_inbound_lost_reply
CP-19	Passed	Accepted_original_storage_failure_is_terminal_without_a_false_send(missing)
CP-19	Passed	Staging_io_failure_leaves_correlation_owed_and_same_source_retry_admits_once
CP-19	Passed	Serialized_payload_budget_includes_all_fields
CP-19	Passed	Fallback_annotation_uses_actual_wire_budget_and_overcap_keeps_stamps_null
CP-19	Passed	Expired_lease_takeover_fences_the_old_owner_before_producer_invocation
CP-19	Passed	Deferred_is_returned_only_after_intent_and_correlation_commit
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(main, unrelated-zip, MarkdownSources, False)
CP-19	Passed	Concurrent_admission_respects_max_pending_per_channel
CP-19	Passed	Two_dispatchers_preserve_source_window_and_destination_identity_under_admission_race
CP-19	Passed	Two_pumps_create_one_linked_task_and_expired_lease_takeover_fences_old_owner(before-conversion-create)
CP-19	Passed	Two_pumps_create_one_linked_task_and_expired_lease_takeover_fences_old_owner(conversion-task-committed)
CP-19	Passed	Accepted_target_is_not_retried_when_a_second_target_fails
CP-19	Passed	Source_publication_requires_all_four_members_or_a_complete_zip(False, False)
CP-19	Passed	Source_publication_requires_all_four_members_or_a_complete_zip(False, True)
CP-19	Passed	Source_publication_requires_all_four_members_or_a_complete_zip(True, False)
CP-19	Passed	Accepted_original_storage_failure_is_terminal_without_a_false_send(corrupt)
CP-19	Passed	Source_publication_requires_all_four_members_or_a_complete_zip(True, True)
CP-19	Passed	Only_acceptance_stamps_complete_actual_payload(blocked-then-accepted)
CP-19	Passed	Only_acceptance_stamps_complete_actual_payload(two-refusals-then-accepted)
CP-19	Passed	Only_acceptance_stamps_complete_actual_payload(ambiguous)
CP-19	Passed	Gates_precede_admission_and_a_matched_companion_still_converts(NO_REPLY)
CP-19	Passed	Gates_precede_admission_and_a_matched_companion_still_converts(operator)
CP-19	Passed	Gates_precede_admission_and_a_matched_companion_still_converts(api-error)
CP-19	Passed	Disallowed_plain_text_machine_turn_stays_out_of_admission
CP-19	Passed	Real_control_callers_bypass_the_selected_profile(proactive)
CP-19	Passed	Real_control_callers_bypass_the_selected_profile(digest)
CP-19	Passed	Later_prompt_thread_and_control_cannot_retarget_pending_reply
CP-19	Passed	Real_control_callers_bypass_the_selected_profile(incident)
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(main, explicit-md, MarkdownSources, True)
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(trailing, explicit-md, MarkdownSources, True)
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(machine, explicit-md, MarkdownSources, True)
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(machine, manifest-zip, MarkdownSources, True)
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(main, plain-markdown, EveryAgentReply, True)
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(main, plain-markdown, MarkdownSources, False)
CP-19	Passed	Only_acceptance_stamps_complete_actual_payload(three-refusals)
CP-19	Passed	Real_control_callers_bypass_the_selected_profile(alert)
CP-19	Passed	Main_trailing_and_machine_use_the_same_policy(main, plain-markdown, none, False)
CP-25	Failed	C519_Queue_to_adapter(main)
CP-25	Failed	C519_Queue_to_adapter(trailing)
CP-25	Failed	C519_Queue_to_adapter(machine)
CP-26	Passed	C519_Partial_prompt_requires_complete_receipt
CP-26	Passed	C519_Enabled_loss_notice_reaches_adapter
CP-26	Passed	C519_Size_refusal
CP-26	Failed	C519_Converter_handoff(False, conversion-task-committed)
CP-26	Failed	C519_Converter_handoff(True, conversion-task-committed)
CP-26	Failed	C519_Converter_handoff(False, conversion-dispatched)
CP-26	Failed	C519_Converter_handoff(True, conversion-dispatched)
CP-26	Failed	C519_Converter_handoff(False, enqueue-refused)
CP-26	Failed	C519_Converter_handoff(True, enqueue-refused)
CP-26	Failed	C519_Converter_handoff(False, result-committed)
CP-26	Failed	C519_Converter_handoff(True, result-committed)
CP-27	Passed	Sealed_four_source_pdf_crosses_pump_broker_gateway_and_fake_slack
CP-28	Passed	Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed
CP-28	Passed	Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head
CP-28	Passed	Broker_ack_before_process_death_remains_uncertain_without_replay
CP-28	Passed	Expired_publishing_lease_is_uncertain_until_explicit_retry
```

## Continuation command

After repairing and committing, use the checkpoint tool with expected SHA from committed HEAD, exact plan rows CP-19,CP-25,CP-26,CP-27,CP-28,CP-60,CP-61, row timeout 15m, and sufficient declared total budget for the whole Unit lane and complete Recovery class. Re-run CP-23/24 once if production changes. Wait until exit is not 75, inspect fresh nonzero TRX per intended method/class, validate schema-2 receipts, and preserve all unedited lines. Do not count the timed-out Unit run or skipped CP-61 as complete. Future Review subject remains this continuation lineage; original Code landing owner remains 5724b53e.

Restart: none. Server/runner activation owner: caller after adoption/landing when appropriate. All deliberate controls remain pending SourceLanding Mutation. Next: code.

Cleanup completed: checkpoint clean removed 28 final bin-c519-cp19 directories and 28 prior bin-c519-cp26 directories; bootstrap bin-c519-tool removed. No task-owned bin-c519-* directory remains. Both temporary baseline worktrees were removed after their children exited. Process census found no task test host or checkpoint executor still running. Generated evidence remains in ignored .antiphon paths. The final response records the full task-range evidence-diff result after this report commit.
