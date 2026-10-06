# CARD-0519 S12e — Code task 11c3a54a

S12e passed: CP-61, CP-67, CP-68 and CP-69 executed 61 results, all passed,
zero failed/skipped. No production, test, configuration or plan edits were needed.

Original Code task / landing owner: `11c3a54a-ec63-4ce9-b05d-8238465931ce`.
Branch: `feat/card-task-11c3a54a`.
Worktree: `/work/worktrees/task-11c3a54a`.
Task base and actual tested source: `b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4`.
The later task-report commit is evidence only; these receipts are not relabelled
as tests of that report commit. The frozen S12 final source remains the task base.

Plan: `docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md`,
section `S12 split selection (Plan 02c16198, 2026-10-05)` and its main
`### Checkpoints` table, as present at the tested source.

## Scope and outcome

| Checkpoint | Required invariant | Executed | Passed | Failed | Skipped | Test-host time |
|---|---|---:|---:|---:|---:|---:|
| CP-61 | R-8, full ChannelOutboundRecoveryTests | 18 | 18 | 0 | 0 | 398.06s |
| CP-67 | R-5, full ChannelOutboundDeliveryTests across its partial files | 38 | 38 | 0 | 0 | 80.08s |
| CP-68 | R-9, full ChannelOutboundComposedTransportTests | 1 | 1 | 0 | 0 | 46.97s |
| CP-69 | R-8, four named explicit recovery cases | 4 | 4 | 0 | 0 | 93.50s |

R-5's S12e delivery-class selection passed; this does not relabel its policy and
contract classes from earlier slices as rerun here. R-8 passed the full class and
the four required manual-recovery scenarios: explicit retry after expired lease,
held-head binding repair, two native Slack routes after restart, and broker
acceptance followed by process death without automatic replay. R-9 passed the
four-source/PDF transport to fake Slack. These are automated recovery-action
acceptance cases, not a live production activation. No additional V-n or live
manual acceptance is assigned to S12e.

Round: Final profile v1, scoped to this checkpoint-only group by the explicit
brief and D-S12-7/D-S12-9. The overall release qualification is not complete here.
Outstanding outside this dispatch: S12f CP-70/77/71 (V-13), CP-72 (affected Unit
guards), CP-73 (dispatch/resume compatibility); S12g CP-74/75 (V-11/V-12);
S13 CP-29 (R-12, Windows) and documentation; caller-owned whole-Unit qualification.
Those rows are not reported as passed by this task. Earlier V/R rows retain their
existing evidence and were not rerun. No full assembly, baseline sweep, or whole
Unit run was made. D-S12-10 assigns the red-first baselines to S12c/S12d, not this
unchanged checkpoint group; no deliberate mutant was introduced here.

`ChannelOutboundSettings.UnifiedRecoveryEnabled` still has its default false
value; server/AppHost source contains no enabling override. No deployed
configuration was changed. Restart: **none**. Caller/orchestrator owns any later
server activation after all D-S12-8 gates, Review, landing and sourced Mutation.

## Execution and provenance

Read both `GET /api/runner-defaults` and `GET /api/session-runners` before running.
Defaults revision 2 named server2; the catalogue showed desktop and both Linux
entries, with server2 draining and server2-temp accepting new work. These were
observations, not embedded placement settings. No Runner or Platform pin was used.

The only additional build was the required checkpoint-tool bootstrap:

```sh
pwsh -NoProfile -File scripts/build-slot.ps1 -Label c519-s12e-tool -- dotnet build tools/Antiphon.Checkpoints --property:OutputPath=bin-s12e-tool/ --nologo
```

It passed with zero errors and one CS8602 warning in existing TaskOwnerGuard.cs:170.
`slot=granted waited=0s`, maxcpucount=6, lease held 6s. The manifest build also had
`slot=granted waited=0s`, maxcpucount=6; it took 136.2492232s.

```sh
dotnet tools/Antiphon.Checkpoints/bin-s12e-tool/Antiphon.Checkpoints.dll run --plan docs/superpowers/plans/2026-10-04-card-0519-unified-outbound-recovery-plan.md --after S12e --expected-source-sha b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 --row-timeout 15m --total-timeout 60m --serial --max-wait 50s
dotnet tools/Antiphon.Checkpoints/bin-s12e-tool/Antiphon.Checkpoints.dll wait --run 20261006-013435-aa9b --max-wait 50s
```

Wait was repeated after exit 75 until final exit 0. There was one actual isolated
manifest build (`bin-c519-cp61/`) and four serial rows, each with
`TUNIT_MAX_PARALLEL_TESTS=1`. The report's `builds: 64` counts imported manifest
entries: 63 were `unused`, and only CP-61's build ran. No timeout, assertion,
retry policy or filter was changed. Reruns=0 and repair rounds=0. CP-69 repeats
four CP-61 cases because the closed manifest requires both rows; no extra proof
repetition was added. Total checkpoint wall time: 12m38s.

Fresh TRX counters and TestDefinitions/TestMethod class/method rosters were
inspected for every row, with nonzero counts and no unexpected class. All four
CHECKPOINT lines have `dirty=0 sourceState=clean buildSource=verified` and the
exact committed expected SHA. Independent receipt validation returned exit 0:

```text
CHECKPOINT SOURCE VALID source=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 rows=4
```

Validation command:

```sh
pwsh -NoProfile -File scripts/validate-checkpoint-receipt.ps1 -Evidence .antiphon/checkpoints/20261006-013435-aa9b/report.json -ExpectedSourceSha b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 -Rows CP-61,CP-67,CP-68,CP-69
```

Raw evidence root:
`/work/worktrees/task-11c3a54a/.antiphon/checkpoints/20261006-013435-aa9b/`.
It contains report.json/report.md, executor.log, build.log, and each row's run.trx
and console.log. Generated evidence remains ignored. Only this individual
Markdown report is committed. The checkpoint tool removed its own test outputs
and shadow copy after green; the separately owned bootstrap output was removed
after validation. The build-log directory under the evidence root is retained.

Full task history guard, including the report-only commit, must be checked with:

```sh
pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 -HeadRef HEAD
```

Its final actual outcome and the pushed report SHA are reported in the closing
task response. Review should qualify the frozen tested source above and inspect
the later Markdown-only diff separately, preserving the receipt's actual SHA.

## Unedited checkpoint lines

```text
CHECKPOINT CP-61 commit=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 build=ok filter=/*/*/ChannelOutboundRecoveryTests/* executed=18 passed=18 failed=0 skipped=0 trx=/work/worktrees/task-11c3a54a/.antiphon/checkpoints/20261006-013435-aa9b/rows/CP-61/run.trx slot=granted waited=0s dirty=0 source=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 sourceState=clean buildSource=verified
CHECKPOINT CP-67 commit=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 build=reused filter=/*/*/ChannelOutboundDeliveryTests/* executed=38 passed=38 failed=0 skipped=0 trx=/work/worktrees/task-11c3a54a/.antiphon/checkpoints/20261006-013435-aa9b/rows/CP-67/run.trx slot=granted waited=0s dirty=0 source=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 sourceState=clean buildSource=verified
CHECKPOINT CP-68 commit=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 build=reused filter=/*/*/ChannelOutboundComposedTransportTests/* executed=1 passed=1 failed=0 skipped=0 trx=/work/worktrees/task-11c3a54a/.antiphon/checkpoints/20261006-013435-aa9b/rows/CP-68/run.trx slot=granted waited=0s dirty=0 source=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 sourceState=clean buildSource=verified
CHECKPOINT CP-69 commit=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 build=reused filter=/*/*/ChannelOutboundRecoveryTests/(Expired_publishing_lease_is_uncertain_until_explicit_retry*)|(Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed*)|(Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head*)|(Broker_ack_before_process_death_remains_uncertain_without_replay*) executed=4 passed=4 failed=0 skipped=0 trx=/work/worktrees/task-11c3a54a/.antiphon/checkpoints/20261006-013435-aa9b/rows/CP-69/run.trx slot=granted waited=0s dirty=0 source=b4be3d4665ea989da3c3c0e4a5fc414bda7cd7c4 sourceState=clean buildSource=verified
```

## Inspected TRX roster

### CP-61

- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Broker_ack_before_process_death_remains_uncertain_without_replay — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Definite_queue_refusal_retries_sealed_payload_but_ambiguous_failure_does_not — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Expired_publishing_lease_is_uncertain_until_explicit_retry — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Process_death_preserves_ownership_at_each_boundary — 5 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Recovery_preserves_settled_worker_and_publication_boundaries — 6 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Resume_held_after_conversion_returns_to_ready — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Running_converter_reconciles_after_dispatcher_death — 1 passed results.

### CP-67

- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Accepted_original_storage_failure_is_terminal_without_a_false_send — 2 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Accepted_target_is_not_retried_when_a_second_target_fails — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Concurrent_admission_respects_max_pending_per_channel — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Deferred_is_returned_only_after_intent_and_correlation_commit — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Disallowed_plain_text_machine_turn_stays_out_of_admission — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Expired_lease_takeover_fences_the_old_owner_before_producer_invocation — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Fallback_annotation_uses_actual_wire_budget_and_overcap_keeps_stamps_null — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Gates_precede_admission_and_a_matched_companion_still_converts — 3 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Later_prompt_thread_and_control_cannot_retarget_pending_reply — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Main_trailing_and_machine_use_the_same_policy — 8 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Only_acceptance_stamps_complete_actual_payload — 4 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Pending_conversion_is_not_an_inbound_lost_reply — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Real_control_callers_bypass_the_selected_profile — 4 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Serialized_payload_budget_includes_all_fields — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Source_publication_requires_all_four_members_or_a_complete_zip — 4 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Staging_io_failure_leaves_correlation_owed_and_same_source_retry_admits_once — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Two_dispatchers_preserve_source_window_and_destination_identity_under_admission_race — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundDeliveryTests.Two_pumps_create_one_linked_task_and_expired_lease_takeover_fences_old_owner — 2 passed results.

### CP-68

- Antiphon.Tests.Application.ChannelOutboundComposedTransportTests.Sealed_four_source_pdf_crosses_pump_broker_gateway_and_fake_slack — 1 passed results.

### CP-69

- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Broker_ack_before_process_death_remains_uncertain_without_replay — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Expired_publishing_lease_is_uncertain_until_explicit_retry — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Held_head_blocks_later_reply_until_original_binding_is_repaired_and_resumed — 1 passed results.
- Antiphon.Tests.Application.ChannelOutboundRecoveryTests.Restart_preserves_two_inbound_slack_routes_behind_an_uncertain_head — 1 passed results.

## Positive controls pending SourceLanding Mutation

Every PC below and every variant/cut/argument named by its plan row remains pending. This ordinary green run discharges no positive control. Mutation owns deliberate compiling defects, assertion-red proof, restoration, fresh-build green and missing-control discovery. In particular PC-S12-5 needs separate idle and busy method cycles for every kind; PC-89..95 retain all crash cuts and path variants, and PC-96 all converter handoff cuts and busy/idle variants. S4 additions to PC-15/19/22/23 are independently pending as listed.

| PC / variant | Outcome |
|---|---|
| PC-1 | Pending SourceLanding Mutation |
| PC-2 | Pending SourceLanding Mutation |
| PC-3 | Pending SourceLanding Mutation |
| PC-4 | Pending SourceLanding Mutation |
| PC-5 | Pending SourceLanding Mutation |
| PC-6 | Pending SourceLanding Mutation |
| PC-7 | Pending SourceLanding Mutation |
| PC-8 | Pending SourceLanding Mutation |
| PC-9 | Pending SourceLanding Mutation |
| PC-10 | Pending SourceLanding Mutation |
| PC-11 | Pending SourceLanding Mutation |
| PC-12 | Pending SourceLanding Mutation |
| PC-13 | Pending SourceLanding Mutation |
| PC-14 | Pending SourceLanding Mutation |
| PC-15 | Pending SourceLanding Mutation |
| PC-16 | Pending SourceLanding Mutation |
| PC-17 | Pending SourceLanding Mutation |
| PC-18 | Pending SourceLanding Mutation |
| PC-19 | Pending SourceLanding Mutation |
| PC-20 | Pending SourceLanding Mutation |
| PC-21 | Pending SourceLanding Mutation |
| PC-22 | Pending SourceLanding Mutation |
| PC-23 | Pending SourceLanding Mutation |
| PC-24 | Pending SourceLanding Mutation |
| PC-25 | Pending SourceLanding Mutation |
| PC-26 | Pending SourceLanding Mutation |
| PC-27 | Pending SourceLanding Mutation |
| PC-28 | Pending SourceLanding Mutation |
| PC-29 | Pending SourceLanding Mutation |
| PC-30 | Pending SourceLanding Mutation |
| PC-31 | Pending SourceLanding Mutation |
| PC-32 | Pending SourceLanding Mutation |
| PC-33 | Pending SourceLanding Mutation |
| PC-34 | Pending SourceLanding Mutation |
| PC-35 | Pending SourceLanding Mutation |
| PC-36 | Pending SourceLanding Mutation |
| PC-37 | Pending SourceLanding Mutation |
| PC-38 | Pending SourceLanding Mutation |
| PC-39 | Pending SourceLanding Mutation |
| PC-40 | Pending SourceLanding Mutation |
| PC-41 | Pending SourceLanding Mutation |
| PC-42 | Pending SourceLanding Mutation |
| PC-43 | Pending SourceLanding Mutation |
| PC-44 | Pending SourceLanding Mutation |
| PC-45 | Pending SourceLanding Mutation |
| PC-46 | Pending SourceLanding Mutation |
| PC-47 | Pending SourceLanding Mutation |
| PC-48 | Pending SourceLanding Mutation |
| PC-49 | Pending SourceLanding Mutation |
| PC-50 | Pending SourceLanding Mutation |
| PC-51 | Pending SourceLanding Mutation |
| PC-52 | Pending SourceLanding Mutation |
| PC-53 | Pending SourceLanding Mutation |
| PC-54 | Pending SourceLanding Mutation |
| PC-55 | Pending SourceLanding Mutation |
| PC-56 | Pending SourceLanding Mutation |
| PC-57 | Pending SourceLanding Mutation |
| PC-58 | Pending SourceLanding Mutation |
| PC-59 | Pending SourceLanding Mutation |
| PC-60 | Pending SourceLanding Mutation |
| PC-61 | Pending SourceLanding Mutation |
| PC-62 | Pending SourceLanding Mutation |
| PC-63 | Pending SourceLanding Mutation |
| PC-64 | Pending SourceLanding Mutation |
| PC-65 | Pending SourceLanding Mutation |
| PC-66 | Pending SourceLanding Mutation |
| PC-67 | Pending SourceLanding Mutation |
| PC-68 | Pending SourceLanding Mutation |
| PC-69 | Pending SourceLanding Mutation |
| PC-70 | Pending SourceLanding Mutation |
| PC-71 | Pending SourceLanding Mutation |
| PC-72 | Pending SourceLanding Mutation |
| PC-73 | Pending SourceLanding Mutation |
| PC-74 | Pending SourceLanding Mutation |
| PC-75 | Pending SourceLanding Mutation |
| PC-76 | Pending SourceLanding Mutation |
| PC-77 | Pending SourceLanding Mutation |
| PC-78 | Pending SourceLanding Mutation |
| PC-79 | Pending SourceLanding Mutation |
| PC-80 | Pending SourceLanding Mutation |
| PC-81 | Pending SourceLanding Mutation |
| PC-82 | Pending SourceLanding Mutation |
| PC-83 | Pending SourceLanding Mutation |
| PC-84 | Pending SourceLanding Mutation |
| PC-85 | Pending SourceLanding Mutation |
| PC-86 | Pending SourceLanding Mutation |
| PC-87 | Pending SourceLanding Mutation |
| PC-88 | Pending SourceLanding Mutation |
| PC-89 | Pending SourceLanding Mutation |
| PC-90 | Pending SourceLanding Mutation |
| PC-91 | Pending SourceLanding Mutation |
| PC-92 | Pending SourceLanding Mutation |
| PC-93 | Pending SourceLanding Mutation |
| PC-94 | Pending SourceLanding Mutation |
| PC-95 | Pending SourceLanding Mutation |
| PC-96 | Pending SourceLanding Mutation |
| PC-97 | Pending SourceLanding Mutation |
| PC-98 | Pending SourceLanding Mutation |
| PC-99 | Pending SourceLanding Mutation |
| PC-100 | Pending SourceLanding Mutation |
| PC-1059-1 / roots | Pending SourceLanding Mutation |
| PC-1059-2 / traversal | Pending SourceLanding Mutation |
| PC-1059-3 / file and directory links | Pending SourceLanding Mutation |
| PC-1059-4 / pre-read budget | Pending SourceLanding Mutation |
| PC-1059-5 / Linux regular type | Pending SourceLanding Mutation |
| PC-1059-6 / Linux growing-file budget | Pending SourceLanding Mutation |
| PC-S4-1 / default off | Pending SourceLanding Mutation |
| PC-19 / S4 main and machine activation | Pending SourceLanding Mutation |
| PC-15 / S4 staged source | Pending SourceLanding Mutation |
| PC-22 / S4 silence | Pending SourceLanding Mutation |
| PC-23 / S4 origin | Pending SourceLanding Mutation |
| PC-S4-2 / catalog-less main | Pending SourceLanding Mutation |
| PC-S4-3 / catalog-less machine | Pending SourceLanding Mutation |
| PC-S4-4 / catalog-less trailing | Pending SourceLanding Mutation |
| PC-S5-1 / event closure | Pending SourceLanding Mutation |
| PC-S5-2 / transactional closure | Pending SourceLanding Mutation |
| PC-S5-3 / complete machine batch | Pending SourceLanding Mutation |
| PC-S5-4 / original context time | Pending SourceLanding Mutation |
| PC-S6-1 / silent main root | Pending SourceLanding Mutation |
| PC-S6-2 / root fair budget | Pending SourceLanding Mutation |
| PC-S6-3 / machine trailing policy | Pending SourceLanding Mutation |
| PC-S6-4 / trailing API withholding | Pending SourceLanding Mutation |
| PC-S6-5 / terminal ordering | Pending SourceLanding Mutation |
| PC-S7-1 / null accepted lease | Pending SourceLanding Mutation |
| PC-S8-1 / SentAt origin | Pending SourceLanding Mutation |
| PC-S8-2 / recording-only preparation repair | Pending SourceLanding Mutation |
| PC-S12-1 | Pending SourceLanding Mutation |
| PC-S12-2 | Pending SourceLanding Mutation |
| PC-S12-3 | Pending SourceLanding Mutation |
| PC-S12-4 | Pending SourceLanding Mutation |
| PC-S12-5 | Pending SourceLanding Mutation |

## Handoff

Next: Review of completed S12e evidence and the Markdown-only commit. Preserve original Code task 11c3a54a as landing owner; caller lands after Review and commissions SourceLanding Mutation. Continue S12f/S12g and caller-owned qualification independently at the same frozen production/test source. No restart requested.
