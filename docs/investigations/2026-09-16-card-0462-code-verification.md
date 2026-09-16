# CARD-0462 Code verification — task 05a66230

S1–S5 are implemented and pushed. Ordinary verification is complete: 396 named integration cases pass; Unit has 2,456 passed, four failures reproduced verbatim at the original base, and one privilege-dependent skip. V-21 is an explicit native-fixture skip. Required isolated-output cleanup was rejected by automatic approval review and remains open; nothing was deleted.

- Original Code task / landing owner: `05a66230`.
- Branch: `feat/card-task-05a66230`.
- Exact worktree: `C:\Antiphon\worktrees\card-task-05a66230`.
- Original base and landed plan: `7a7dac4ac4f9c4d1477dfbc094861dd990bd87f9`.
- Final implementation/test-metadata source commit: `0f359477bfc2cfadd4db56d08982466c61b2565d`. The later evidence-only commit contains this report; use its full SHA from the completion report.
- Plan artifact: [2026-09-15-card-0462-follow-live-herdr-labels-plan.md](../superpowers/plans/2026-09-15-card-0462-follow-live-herdr-labels-plan.md). D-2/D-3/D-4/D-8 defaults retained.
- External evidence root: `C:\Antiphon\worktrees\card-task-05a66230\.antiphon\c462-05a66230`. Machine-readable audit: `verification-audit.json`; per-run TRX/log paths below; integrity inventory: `evidence-manifest.json`.
- Next: ordinary read-only Review. No landing, deployment, deliberate mutant, server restart or runner restart was performed. Activation after landing requires **server / runner**; original landing owner remains `05a66230`.

## Implemented behavior

- S1: versioned nullable launch intent, additive observation DTO, edit token and generation/sequence watermark migration. Manual placement writes and automatic follow use a common owner-before-seat row lock and reload; explicit reaffirmation/clear and placement-context changes invalidate the old generation’s intent.
- S2: strict protocol-20 tab/workspace getters and read-only, same-binding collector. It checks positive recorded child identity, IDs, counts, list/get stability, host tab comparer, ordinal workspace resolution, explicit untagged provenance, token preemption and representable labels. It never discovers a moved target or creates a pin.
- S3: shared nonqueueing admission for GET/baseline/independent runner timer, persisted attempt before I/O, hourly cooldown on every attempt, ten-second observation deadline and joined shutdown. Atomic sidecar publication and exact-binding last-pane repair preserve retirement/replacement ordering; cached GET requires fresh positive current binding evidence.
- S4: independent server sweep, real HTTP mapping, transactionally conditional labels plus watermark, expiry/sequence/owner/token/generation/live guards, and postcommit AgentChanged only for an actual label change. Lost response, restart and rollback replay the durable snapshot.
- S5: production launch/GET routes hosted on a random loopback fixture, real launch queue/client/adapter, isolated PostgreSQL stores, busy/idle and tab-only/workspace+tab relaunch receipts, and all named legacy regressions. Native canary requires an explicitly selected test-owned Herdr instance. Owner docs and measured Slow classifications are updated.

Key production files are `HerdrLabelFollowService.cs`, `HerdrPlacementLock.cs`, `HerdrLabelObserver.cs`, `HerdrPaneChild.LabelFollow.cs`, `HerdrSnapshotFile.cs`, `SessionReadLaunchRoutes.cs`, `HerdrLabelFollowContracts.cs` and migration `20260916074258_AddHerdrLabelFollow`. New test classes are O/T/F/A/C/W/E/L below. Migration Designer and snapshot were generated, not hand-trimmed.

## Ordinary commands, counts and provenance

Build output was producer-owned `bin-c462-05a66230/` (forward slash). Each source checkpoint was committed and pushed before its build/run; projects ran sequentially and source was frozen during runs. Each result directory was fresh. Tests used `dotnet run --no-build`, never `dotnet test`. Builds were repeated only after fixes/test extensions/metadata edits. There was no namespace or full-assembly run. No Antiphon.Agents.Pty.Tests run overlapped these runs.

Production implementation is unchanged since `a3ea596b1e0c242e678089a71e77f538caa26870`, the SN/SR/RR/Unit tested commit. Later changes expand runner test assertions/actor schedules and annotate measured test cost; RN was rerun at `c25a8e6460018751b2a7b2d6b61880d5b1368235`, and final metadata M at the source commit above. Earlier RN runs passed 115 then 126 cases; the final 128 replaces them in totals.

| Command | Project | Expanded actual result | Exit | Verified commit | Fresh TRX relative to evidence root |
|---|---|---|---:|---|---|
| U | Antiphon.Tests | 2456 passed, 4 failed, 1 notexecuted | 2 | `a3ea596b1e0c242e678089a71e77f538caa26870` | `final1-unit/run.trx` |
| RN | Antiphon.SessionRunner.Tests | 128 passed | 0 | `c25a8e6460018751b2a7b2d6b61880d5b1368235` | `final3-runner-new/run.trx` |
| RR | Antiphon.SessionRunner.Tests | 158 passed | 0 | `a3ea596b1e0c242e678089a71e77f538caa26870` | `final1-runner-regression/run.trx` |
| SN | Antiphon.Tests | 56 passed | 0 | `a3ea596b1e0c242e678089a71e77f538caa26870` | `final1-server-new/run.trx` |
| SR | Antiphon.Tests | 54 passed | 0 | `a3ea596b1e0c242e678089a71e77f538caa26870` | `final1-server-regression/run.trx` |
| L | Antiphon.SessionRunner.Tests | 1 notexecuted | 8 | `a3ea596b1e0c242e678089a71e77f538caa26870` | `final1-native/run.trx` |
| M | Antiphon.Tests | 25 passed, 3 failed | 2 | `0f359477bfc2cfadd4db56d08982466c61b2565d` | `final4-classification/run.trx` |

Exact executed filters and command form (replace results directory with a fresh path on rerun):

U:
```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462-05a66230/ -- --treenode-filter '/*/*/*/*[Category=Unit]' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-05a66230/final1-unit
```

RN:
```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c462-05a66230/ -- --treenode-filter '/*/*/(HerdrLabelObservationTests*)|(HerdrLabelFollowSchedulingTests*)|(HerdrLabelSnapshotTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-05a66230/final3-runner-new
```

RR:
```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c462-05a66230/ -- --treenode-filter '/*/*/(HerdrEventPumpTests*)|(HerdrRunnerSessionTests*)|(HerdrAdoptionSweepTests*)|(HerdrNamedTabResolverTests*)|(HerdrNamedTabPlacementTests*)|(HerdrPaneAllocatorTests*)|(HerdrClientTests*)|(HerdrClientSurfaceTests*)|(HerdrPaneSidecarTests*)|(HerdrAttachTests*)|(HerdrPaneDisposalConcurrencyTests*)|(HerdrPaneDisposalStopRegressionTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-05a66230/final1-runner-regression
```

SN:
```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462-05a66230/ -- --treenode-filter '/*/*/(HerdrLabelFollowTests*)|(HerdrLabelFollowConcurrencyTests*)|(HerdrLabelFollowWireTests*)|(HerdrLabelFollowFlowTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-05a66230/final1-server-new
```

SR:
```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462-05a66230/ -- --treenode-filter '/*/*/(HerdrPlacementSettingsTests*)|(HerdrLaunchContextResolverTests*)|(HerdrPlacementPreflightTests*)|(SessionRunnerHttpClientHerdrWireTests*)|(AgentAttachHerdrTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-05a66230/final1-server-regression
```

L:
```powershell
dotnet run --project tests/Antiphon.SessionRunner.Tests --no-build --property:OutputPath=bin-c462-05a66230/ -- --treenode-filter '/*/*/HerdrLabelFollowLiveTests/Owned_tab_and_workspace_getters_support_rename_follow_validation' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-05a66230/final1-native
```

M:
```powershell
dotnet run --project tests/Antiphon.Tests --no-build --property:OutputPath=bin-c462-05a66230/ -- --treenode-filter '/*/*/(TestClassificationGuardTests*)|(TestLaneCategoryGuardTests*)|(TestClassificationPolicyTests*)|(SlowTestTripwireTests*)/*' --report-trx --report-trx-filename run.trx --results-directory .antiphon/c462-05a66230/final4-classification
```

Before rerunning after approved cleanup, build each affected project once with `dotnet build tests/<Project> --property:OutputPath=bin-<fresh-owner>/ --nologo`, then use that same output suffix for its selections. Final build logs are `build-final-runner.log`, `build-final-server.log` and `build-classification-final.log`; all succeeded.

| Command | Actual class | Expanded cases |
|---|---|---:|
| RN | `Antiphon.SessionRunner.Tests.HerdrLabelFollowSchedulingTests` | 25 |
| RN | `Antiphon.SessionRunner.Tests.HerdrLabelObservationTests` | 69 |
| RN | `Antiphon.SessionRunner.Tests.HerdrLabelSnapshotTests` | 34 |
| RR | `Antiphon.SessionRunner.Tests.HerdrAdoptionSweepTests` | 22 |
| RR | `Antiphon.SessionRunner.Tests.HerdrAttachTests` | 23 |
| RR | `Antiphon.SessionRunner.Tests.HerdrClientSurfaceTests` | 3 |
| RR | `Antiphon.SessionRunner.Tests.HerdrClientTests` | 16 |
| RR | `Antiphon.SessionRunner.Tests.HerdrEventPumpTests` | 5 |
| RR | `Antiphon.SessionRunner.Tests.HerdrNamedTabPlacementTests` | 23 |
| RR | `Antiphon.SessionRunner.Tests.HerdrNamedTabResolverTests` | 7 |
| RR | `Antiphon.SessionRunner.Tests.HerdrPaneAllocatorTests` | 5 |
| RR | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalConcurrencyTests` | 27 |
| RR | `Antiphon.SessionRunner.Tests.HerdrPaneDisposalStopRegressionTests` | 13 |
| RR | `Antiphon.SessionRunner.Tests.HerdrPaneSidecarTests` | 7 |
| RR | `Antiphon.SessionRunner.Tests.HerdrRunnerSessionTests` | 7 |
| SN | `Antiphon.Tests.Application.HerdrLabelFollowConcurrencyTests` | 9 |
| SN | `Antiphon.Tests.Application.HerdrLabelFollowFlowTests` | 10 |
| SN | `Antiphon.Tests.Application.HerdrLabelFollowTests` | 33 |
| SN | `Antiphon.Tests.Application.HerdrLabelFollowWireTests` | 4 |
| SR | `Antiphon.Tests.Agents.SessionRunnerHttpClientHerdrWireTests` | 16 |
| SR | `Antiphon.Tests.Application.AgentAttachHerdrTests` | 13 |
| SR | `Antiphon.Tests.Application.HerdrLaunchContextResolverTests` | 9 |
| SR | `Antiphon.Tests.Application.HerdrPlacementPreflightTests` | 7 |
| SR | `Antiphon.Tests.Application.HerdrPlacementSettingsTests` | 9 |
| L | `Antiphon.SessionRunner.Tests.HerdrLabelFollowLiveTests` | 1 |
| M | `Antiphon.TestSupport.TestClassificationGuardTests` | 1 |
| M | `Antiphon.Tests.TestHelpers.SlowTestTripwireTests` | 2 |
| M | `Antiphon.Tests.TestHelpers.TestClassificationPolicyTests` | 24 |
| M | `Antiphon.Tests.TestHelpers.TestLaneCategoryGuardTests` | 1 |

The audit joins each result to its TRX TestMethod definition, checks every intended class/method is nonempty, and checks every `[Test]` body in all eight new classes (80 methods). Parameter variants and individual outcomes are preserved in `verification-audit.json`. L enumerated one test but executed zero: it is not native pass evidence.

## Every V/R outcome

Aliases: O = HerdrLabelObservationTests; T = HerdrLabelFollowSchedulingTests; F = HerdrLabelSnapshotTests; A = HerdrLabelFollowTests; C = HerdrLabelFollowConcurrencyTests; W = HerdrLabelFollowWireTests; E = HerdrLabelFollowFlowTests; L = HerdrLabelFollowLiveTests. Rows overlap, so their counts must not be summed as unique tests. Shared commands are the IDs above.

| ID | Shared commands | Actual outcome / expanded count | Exact method anchors (a class means all its methods) |
|---|---|---|---|
| V-1 | SN, SR | PASS / 6 | `HerdrPlacementSettingsTests.Manual_placement_edit_rotates_only_the_internal_token`; `HerdrLaunchContextResolverTests.Follow_intent_preserves_nullable_pins_and_physical_owner`; `W.Launch_and_get_round_trip_follow_metadata` |
| V-2 | RN, RR | PASS / 12 | `HerdrClientSurfaceTests.Tab_and_workspace_get_use_schema_envelopes`; `O.Malformed_or_failed_getter_never_yields_a_candidate` |
| V-3 | RN | PASS / 2 | `O.Rename_follows_only_the_same_binding`; `O.Final_read_change_invalidates_collection` |
| V-4 | RN | PASS / 2 | `O.Duplicate_tab_label_is_ambiguous`; `O.Case_only_rename_uses_the_host_comparer` |
| V-5 | RN | PASS / 5 | `O.Reported_pane_count_must_be_one`; `O.Enumerated_pane_count_must_be_one`; `O.Only_the_bound_pane_can_validate_the_tab` |
| V-6 | RN | PASS / 6 | `O.Workspace_follow_requires_original_untagged_selection`; `O.Workspace_follow_requires_current_untagged_state`; `O.Workspace_follow_requires_unique_next_launch_resolution` |
| V-7 | RN | PASS / 11 | `O.Unrepresentable_labels_preserve_pins` |
| V-8 | RN, SN | PASS / 6 | `O.Attached_or_unpinned_binding_never_becomes_named`; `A.Null_pins_are_never_created_even_with_claimed_intent` |
| V-9 | RN | PASS / 5 | `T.Concurrent_triggers_share_one_persisted_attempt`; `T.Boundary_and_failure_attempts_obey_the_same_hour` |
| V-10 | RN | PASS / 5 | `T.Idle_healthy_stream_checks_without_turns_or_gets`; `T.Disabled_or_stopped_pump_does_no_label_work`; `T.Stalled_getter_times_out_and_releases_the_pane_lease` |
| V-11 | RN | PASS / 2 | `T.Restart_preserves_cooldown_and_requires_fresh_validation`; `F.Failed_persistence_does_not_publish_or_advance_cached_labels` |
| V-12 | SN | PASS / 4 | `C.Manual_edit_before_follow_wins`; `C.Manual_edit_after_follow_wins`; `C.Away_and_back_or_clear_and_repin_does_not_rearm` |
| V-13 | SN | PASS / 12 | `A.Current_pointer_must_still_name_the_session`; `A.Physical_owner_must_match_both_launch_and_session`; `A.Only_live_standing_herdr_agents_are_eligible` |
| V-14 | SN | PASS / 11 | `A.Same_id_new_generation_rejects_old_observation`; `A.Older_or_duplicate_sequence_cannot_roll_back_labels`; `A.Expired_future_or_unverified_observation_is_ignored` |
| V-15 | SN | PASS / 1 | `A.Labels_and_watermark_commit_together_and_only_changes_notify` |
| V-16 | SN | PASS / 2 | `E.Missed_observation_is_recovered_after_server_restart`; `E.Db_failure_retries_the_same_current_snapshot` |
| V-17 | RN | PASS / 3 | `F.Retirement_and_refresh_cannot_resurrect_or_revert_sidecar` |
| V-18 | RN | PASS / 10 | `F.Last_pane_refresh_requires_exact_generation_and_binding`; `F.Last_pane_write_failure_is_repaired_without_new_observation` |
| V-19 | SN | PASS / 3 | `W.Old_peers_and_sidecars_remain_compatible_without_follow`; `A.Sweep_is_independent_of_corroboration_and_turn_state` |
| V-20 | SN | PASS / 6 | `E.Renamed_pin_is_used_by_the_next_named_launch`; `E.Move_keeps_the_pinned_destination_and_never_creates_during_follow`; `E.Observation_never_mutates_herdr_furniture` |
| V-21 | L | SKIPPED / 1 enumerated, 0 executed — native prerequisite absent | `L.Owned_tab_and_workspace_getters_support_rename_follow_validation` |
| V-22 | RN | PASS / 69 | `O` |
| V-23 | RN | PASS / 26 | `T`; `F.Crash_after_sidecar_commit_recovers_without_stale_publication` |
| V-24 | RN | PASS / 34 | `F` |
| V-25 | SN, SR | PASS / 51 | `A`; `C`; `HerdrPlacementSettingsTests` |
| V-26 | SN | PASS / 7 | `E.Renamed_pin_is_used_by_the_next_named_launch`; `E.Db_failure_retries_the_same_current_snapshot`; `E.Missed_observation_is_recovered_after_server_restart`; `E.Committed_pin_is_visible_after_notification_failure` |
| V-27 | RN, SN, SR | PASS / 10 | `W.Launch_and_get_round_trip_follow_metadata`; `W.Old_peers_and_sidecars_remain_compatible_without_follow`; `HerdrPlacementSettingsTests.Follow_migration_preserves_existing_labels`; `T.Invalid_settings_refuse_startup`; `A.Invalid_sweep_settings_refuse_startup` |
| V-28 | RN, SN | PASS / 14 | `E.Label_failure_does_not_change_session_lifecycle`; `E.Observation_never_mutates_herdr_furniture`; `F.Observer_coordinates_with_existing_pane_actors` |
| V-29 | SN | PASS / 1 | `W.Direct_and_http_get_refresh_and_map_the_same_follow_observation` |
| V-30 | SN | PASS / 2 | `A.Disabled_sweep_polls_nothing`; `A.Sweep_is_independent_of_corroboration_and_turn_state` |
| R-1 | RR | PASS / 34 | `HerdrEventPumpTests`; `HerdrRunnerSessionTests`; `HerdrAdoptionSweepTests` |
| R-2 | RR | PASS / 35 | `HerdrNamedTabResolverTests`; `HerdrNamedTabPlacementTests`; `HerdrPaneAllocatorTests` |
| R-3 | SR | PASS / 25 | `HerdrPlacementSettingsTests`; `HerdrLaunchContextResolverTests`; `HerdrPlacementPreflightTests` |
| R-4 | RR, SR | PASS / 42 | `SessionRunnerHttpClientHerdrWireTests`; `HerdrClientTests`; `HerdrClientSurfaceTests`; `HerdrPaneSidecarTests` |
| R-5 | RR, SR | PASS / 76 | `HerdrAttachTests`; `HerdrPaneDisposalConcurrencyTests`; `HerdrPaneDisposalStopRegressionTests`; `AgentAttachHerdrTests` |
| R-6 | RR | PASS / 2 | `HerdrRunnerSessionTests.Sticky_revision_plus_changed_read_text_advances_LastSequence_via_GetSnapshot_and_GetAsync`; `HerdrRunnerSessionTests.Sticky_revision_plus_identical_text_across_reads_does_not_advance` |
| R-7 | RR | PASS / 1 | `HerdrAdoptionSweepTests.R13_replayed_pane_closed_on_a_healthy_pane_does_nothing` |
| R-8 | RR | PASS / 2 | `HerdrNamedTabPlacementTests.Named_pin_outranks_a_valid_last_pane_and_an_allocator_slot`; `HerdrNamedTabPlacementTests.Named_same_id_restart_relaunches_into_the_same_labelled_pane` |
| R-9 | SR | PASS / 1 | `HerdrPlacementSettingsTests.Updating_labels_on_a_live_agent_launches_nothing` |
| R-10 | SR | PASS / 1 | `HerdrPlacementPreflightTests.Public_start_refused_by_preflight_is_409_before_any_row_or_queue_mutation` |
| R-11 | RR | PASS / 3 | `HerdrPaneSidecarTests.A_sidecar_without_the_field_loads_a_null_generation`; `HerdrPaneSidecarTests.Placement_labels_round_trip_and_old_files_load_with_null_labels`; `HerdrPaneSidecarTests.retire_of_an_attached_sidecar_writes_no_last_pane_record` |
| R-12 | RR | PASS / 3 | `HerdrPaneDisposalConcurrencyTests.C461_G072_Retirement_lease`; `HerdrPaneDisposalConcurrencyTests.C461_G073_Lock_order`; `HerdrPaneDisposalStopRegressionTests.C461_G075_Stop_completes_when_adoption_wins_the_pane_lease` |

V-28 includes 12 runner data rows: observer-first and actor-first for retirement, stop, detach, adoption, same-ID replacement and disposal. Disposal safely refuses an active bound pane; it does not close that pane to manufacture a race. The real PostgreSQL manual-before/manual-after tests use independently blocked connections, not only sequential calls. V-20 has four busy/idle × tab-only/both-label rename cases and proves the actual next launch reuses the original pane.

## Inherited failures, skips and duration diagnostics

Each Unit failure was rebuilt once in detached baseline `7a7dac4ac4f9c4d1477dfbc094861dd990bd87f9`, using isolated `bin-c462-base-05a66230/`, then executed alone with its exact method filter. Each fresh TRX has one failed test; assertion messages exactly match current Unit. Final metadata M has only the same three classification failures, also matched verbatim to the baseline. No timeout, assertion or retry was loosened.

| Exact base filter | Actual | Evidence directory | Cause |
|---|---|---|---|
| `/*/*/TestClassificationGuardTests/Registry_matches_compiled_metadata` | 1 failed, exit 2; identical assertion | `base-Registry_matches_compiled_metadata` | Existing HerdrPaneDisposalEndpointTests lacks Unit xor Integration category. |
| `/*/*/ScopedVerificationInstructionTests/C487_G142` | 1 failed, exit 2; identical assertion | `base-C487_G142` | Stale instruction expectation says next: mutation; bundle says ordinary Review. |
| `/*/*/TestLaneCategoryGuardTests/every_test_class_is_tagged_unit_xor_integration` | 1 failed, exit 2; identical assertion | `base-every_test_class_is_tagged_unit_xor_integration` | Existing HerdrPaneDisposalEndpointTests lacks Unit xor Integration category. |
| `/*/*/TestClassificationPolicyTests/C487_G068` | 1 failed, exit 2; identical assertion | `base-C487_G068` | Existing HerdrPaneDisposalEndpointTests lacks Unit xor Integration category. |

Unit skip: `AgentTuiSecretProtectorTests.Restored_key_file_symlink_is_rejected_without_mutating_target` requires unavailable symlink privilege. V-21 skip: `ANTIPHON_HEADED_TESTS=1` and `ANTIPHON_C462_HERDR_SESSION` naming an already test-owned instance were absent. No default-socket probe, historical pane or native model turn was used. `native-schema.json` is read-only schema evidence only, not execution proof.

Duration tripwire: final SN and SR each report **0 unlisted tests >=5s**; see `tripwire-final-server-new.log` and `tripwire-final-server-regression.log`. The six measured PostgreSQL/HTTP classes have matching Slow metadata and allowlist rows. The Unit tripwire reports **40 unlisted >=5s rows in unchanged existing test bodies** (`tripwire-unit.log`, exit 1). Their timing was not reproduced at base; no claim is made that the timing is inherited or caused by this branch. This diagnostic does not change their actual assertion outcomes. No new Unit test was added by this work.

## Mutation handoff — every active PC/variant remains pending

All **119** active G/PC pairs, G-28–G-146 / PC-28–PC-146, remain **PENDING SourceLanding Mutation**. No deliberate production defect was executed. Original G/PC-1–27 are retired by the landed addendum, not additional obligations. Ordinary passes below do not count as red/restore/green. Mutation owns deliberate red/restore/green and missing-control discovery after ordinary Review and caller landing of original Code task 05a66230.

For each row, all exact method parameter rows must be run with `--treenode-filter "/*/*/ClassName/ExactTestMethod"`. The recipe and decisive variant are preserved below; `verification-audit.json` also records every actual expanded test display name for each PC. Mutations sharing a file/method are sequential; no full class/suite mutant runs.

Noticed coverage gap: **PC-56 / G-56 may be masked**. The earlier bound workspace list/get agreement plus unique untagged match already implies selected workspace ID equality. The ordinary other-ID variant refuses before the final selected-ID check. SourceLanding must classify that possible redundant/surviving guard and discover a missing control if feasible; no mutant was run here. Strict getter-ID checks PC-34/36 and mandatory-field PC-37 now have direct literal-wire exception assertions in addition to the downstream observer checks, addressing their observed masking risk.

| PC / guard | Status | Exact method (ordinary expanded rows) | Pending defect / decisive variant |
|---|---|---|---|
| PC-28 / G-28 | PENDING | `HerdrLabelObservationTests.Attached_or_unpinned_binding_never_becomes_named` (4) | remove observer origin rejection; attached fixture with valid intent returns no candidate and preserves snapshot labels. |
| PC-29 / G-29 | PENDING | `HerdrLabelObservationTests.Missing_or_unknown_intent_is_ineligible` (3) | treat null/unknown follow-state version as version 1 using ordinary launch labels; candidate is null for absent intent and version 99. |
| PC-30 / G-30 | PENDING | `HerdrLabelObservationTests.Missing_generation_is_not_inferred` (1) | substitute LaunchedAtUtc for missing AcceptedStartedAt; candidate remains null despite a live pane and configured pins. |
| PC-31 / G-31 | PENDING | `HerdrLabelObservationTests.Binding_identity_components_must_match` (6) | omit only returned pane-id equality; PaneId mismatch case has no candidate. |
| PC-32 / G-32 | PENDING | `HerdrLabelObservationTests.Binding_identity_components_must_match` (6) | omit only pane.TabId comparison; moved-tab case has no candidate. |
| PC-33 / G-33 | PENDING | `HerdrLabelObservationTests.Binding_identity_components_must_match` (6) | omit only pane.WorkspaceId comparison; moved-workspace case has no candidate. |
| PC-34 / G-34 | PENDING | `HerdrLabelObservationTests.Binding_identity_components_must_match` (6) | omit only getter tab-id validation; wrong tab reply produces no candidate. |
| PC-35 / G-35 | PENDING | `HerdrLabelObservationTests.Binding_identity_components_must_match` (6) | omit only tab.WorkspaceId comparison; cross-workspace tab reply produces no candidate. |
| PC-36 / G-36 | PENDING | `HerdrLabelObservationTests.Binding_identity_components_must_match` (6) | omit only getter workspace-id validation; wrong workspace reply produces no candidate. |
| PC-37 / G-37 | PENDING | `HerdrLabelObservationTests.Malformed_or_failed_getter_never_yields_a_candidate` (11) | accept missing label/count payload fields using plausible default values; malformed literal-envelope case has no candidate, never a fabricated pin. |
| PC-38 / G-38 | PENDING | `HerdrLabelObservationTests.Malformed_or_failed_getter_never_yields_a_candidate` (11) | catch a getter/list failure and reuse the previous successful response; failed-RPC case has no candidate after an earlier success. |
| PC-39 / G-39 | PENDING | `HerdrLabelObservationTests.Duplicate_tab_label_is_ambiguous` (1) | replace duplicate refusal with first matching tab; duplicate case returns herdr_tab_ambiguous. |
| PC-40 / G-40 | PENDING | `HerdrLabelObservationTests.Missing_tab_match_is_not_a_rename` (1) | use tab.get result when resolver returns null; zero-list-match case has no candidate. |
| PC-41 / G-41 | PENDING | `HerdrLabelObservationTests.Reported_pane_count_must_be_one` (2) | remove only reported-count comparison; reported 0/2 with enumerated 1 returns herdr_tab_invalid. |
| PC-42 / G-42 | PENDING | `HerdrLabelObservationTests.Enumerated_pane_count_must_be_one` (2) | remove only enumeration-count comparison and select FirstOrDefault; reported 1/enumerated 2 returns herdr_tab_invalid. |
| PC-43 / G-43 | PENDING | `HerdrLabelObservationTests.Selected_tab_must_be_bound` (1) | omit selected TabId comparison; single wrong selected tab yields no candidate. |
| PC-44 / G-44 | PENDING | `HerdrLabelObservationTests.Only_the_bound_pane_can_validate_the_tab` (1) | omit selected PaneId comparison; one foreign selected pane yields no candidate. |
| PC-45 / G-45 | PENDING | `HerdrLabelObservationTests.Final_reads_must_match_initial_and_list_values` (9) | omit PaneId from final-read comparison; barrier changes only final PaneId; candidate is null. |
| PC-46 / G-46 | PENDING | `HerdrLabelObservationTests.Final_reads_must_match_initial_and_list_values` (9) | omit TabId from final-read comparison; barrier changes only final TabId; candidate is null. |
| PC-47 / G-47 | PENDING | `HerdrLabelObservationTests.Final_reads_must_match_initial_and_list_values` (9) | omit WorkspaceId from final-read comparison; barrier changes only final WorkspaceId; candidate is null. |
| PC-48 / G-48 | PENDING | `HerdrLabelObservationTests.Final_reads_must_match_initial_and_list_values` (9) | omit final tab-label equality; second tab rename before publication yields no candidate. |
| PC-49 / G-49 | PENDING | `HerdrLabelObservationTests.Final_reads_must_match_initial_and_list_values` (9) | omit final workspace-label equality; second workspace rename before publication yields no candidate. |
| PC-50 / G-50 | PENDING | `HerdrLabelObservationTests.Final_reads_must_match_initial_and_list_values` (9) | omit tab list/get agreement check; different tab label/count in list versus getter yields no candidate. |
| PC-51 / G-51 | PENDING | `HerdrLabelObservationTests.Final_reads_must_match_initial_and_list_values` (9) | omit workspace list/get agreement check; different workspace label/token in list versus getter yields no candidate. |
| PC-52 / G-52 | PENDING | `HerdrLabelObservationTests.Workspace_follow_requires_original_untagged_selection` (1) | infer untagged provenance from current tokens; managed-at-launch/token-expired case preserves workspace pin and snapshot. |
| PC-53 / G-53 | PENDING | `HerdrLabelObservationTests.Workspace_follow_requires_current_untagged_state` (2) | ignore non-whitespace antiphon-ws; new foreign or managed token preserves workspace pin and snapshot. |
| PC-54 / G-54 | PENDING | `HerdrLabelObservationTests.Workspace_follow_requires_unique_next_launch_resolution` (3) | choose first untagged label match; two untagged exact matches refuse the placement update. |
| PC-55 / G-55 | PENDING | `HerdrLabelObservationTests.Workspace_follow_requires_unique_next_launch_resolution` (3) | skip token-first preemption check; unique untagged label plus remote matching token yields no candidate. |
| PC-56 / G-56 | PENDING | `HerdrLabelObservationTests.Workspace_follow_requires_unique_next_launch_resolution` (3) | omit selected workspace-id equality; unique same-label different workspace yields no candidate. |
| PC-57 / G-57 | PENDING | `HerdrLabelObservationTests.Ambiguous_workspace_blocks_simultaneous_tab_rename` (1) | keep the tab candidate when workspace validation refuses; both old labels remain, including tab label. |
| PC-58 / G-58 | PENDING | `HerdrLabelObservationTests.Unrepresentable_labels_preserve_pins` (11) | remove shared nonblank rejection; tab and workspace blank cases have no candidate. |
| PC-59 / G-59 | PENDING | `HerdrLabelObservationTests.Unrepresentable_labels_preserve_pins` (11) | change shared maximum from 256 to 257; 257-unit case has no candidate; 256-unit companion succeeds. |
| PC-60 / G-60 | PENDING | `HerdrLabelObservationTests.Unrepresentable_labels_preserve_pins` (11) | remove shared char.IsControl rejection; embedded LF/NUL/DEL cases have no candidate. |
| PC-61 / G-61 | PENDING | `HerdrLabelObservationTests.Unrepresentable_labels_preserve_pins` (11) | trim raw candidate before validation; leading/trailing-space case has no candidate. |
| PC-62 / G-62 | PENDING | `HerdrLabelObservationTests.Case_only_rename_uses_the_host_comparer` (1) | force Ordinal for Windows observation selection; Orch/orch duplicate fixture refuses on explicit ignore-case path. |
| PC-63 / G-63 | PENDING | `HerdrLabelObservationTests.Workspace_resolution_remains_ordinal` (1) | replace workspace Ordinal with OrdinalIgnoreCase; case-distinct workspace does not create false ambiguity; exact bound workspace follows. |
| PC-64 / G-64 | PENDING | `HerdrLabelObservationTests.Attached_or_unpinned_binding_never_becomes_named` (4) | copy raw tab name into TabLabel for workspace-only intent; workspace-only successful follow leaves sidecar/cache TabLabel null. |
| PC-65 / G-65 | PENDING | `HerdrLabelObservationTests.Managed_workspace_keeps_snapshot_while_tab_follows` (2) | copy current workspace label whenever tab follow succeeds; managed/foreign fixture follows tab but preserves WorkspaceLabel. |
| PC-66 / G-66 | PENDING | `HerdrLabelFollowSchedulingTests.Concurrent_triggers_share_one_persisted_attempt` (1) | read due state before releasing/reacquiring claim serialization, without recheck; barrier-controlled baseline/GET/timer yield exactly one getter batch. |
| PC-67 / G-67 | PENDING | `HerdrLabelFollowSchedulingTests.Attempt_claim_is_durable_before_io` (1) | move attempt SaveAtomic after getter collection; crash at first getter leaves durable sequence 1 and due time T+60m. |
| PC-68 / G-68 | PENDING | `HerdrLabelFollowSchedulingTests.Boundary_and_failure_attempts_obey_the_same_hour` (4) | advance next-due only on changed-label success; failed/refused/equal cases have zero label batches at T+59m59s. |
| PC-69 / G-69 | PENDING | `HerdrLabelFollowSchedulingTests.Boundary_and_failure_attempts_obey_the_same_hour` (4) | change due comparison to admit one second early; T+59m59s has zero batches; T+60m has one. |
| PC-70 / G-70 | PENDING | `HerdrLabelFollowSchedulingTests.Invalid_settings_refuse_startup` (4) | remove only cooldown positive validator; cooldown 0/-1 causes OptionsValidationException before any RPC. |
| PC-71 / G-71 | PENDING | `HerdrLabelFollowSchedulingTests.Invalid_settings_refuse_startup` (4) | remove only observation-timeout positive validator; timeout 0/-1 causes OptionsValidationException before any RPC. |
| PC-72 / G-72 | PENDING | `HerdrLabelFollowTests.Invalid_sweep_settings_refuse_startup` (2) | remove only sweep-period positive validator; sweep period 0/-1 causes OptionsValidationException before polling. |
| PC-73 / G-73 | PENDING | `HerdrLabelFollowSchedulingTests.Stalled_getter_times_out_and_releases_the_pane_lease` (2) | use only host token instead of linked observation-deadline token; lease-wait/getter-wait variants finish after simulated 10s and next actor acquires lease within 2 real seconds. |
| PC-74 / G-74 | PENDING | `HerdrLabelFollowSchedulingTests.Inflight_triggers_return_without_waiting` (1) | replace nonwaiting observer admission with awaited semaphore admission; second GET finishes before first getter gate is released. |
| PC-75 / G-75 | PENDING | `HerdrLabelFollowSchedulingTests.Disabled_or_stopped_pump_does_no_label_work` (2) | give timer CancellationToken.None; Stop completes within 2 real seconds and advancing clock produces zero RPC delta. |
| PC-76 / G-76 | PENDING | `HerdrLabelFollowSchedulingTests.Stop_joins_stream_and_timer` (1) | return from shutdown after joining stream only; Stop is incomplete while timer finalizer barrier is held. |
| PC-77 / G-77 | PENDING | `HerdrLabelFollowSchedulingTests.Idle_healthy_stream_checks_without_turns_or_gets` (1) | remove label timer invocation while retaining stream baseline; new observation appears after due time with no GET or turn. |
| PC-78 / G-78 | PENDING | `HerdrLabelFollowSchedulingTests.New_attempt_clears_old_candidate_before_io` (1) | retain previous candidate in persisted attempt claim; blocked/failed new attempt exposes no old candidate. |
| PC-79 / G-79 | PENDING | `HerdrLabelFollowSchedulingTests.Restart_preserves_cooldown_and_requires_fresh_validation` (1) | reset restored next-due to now; restart at minute 30 performs zero label batches until minute 60. |
| PC-80 / G-80 | PENDING | `HerdrLabelFollowSchedulingTests.Restart_preserves_cooldown_and_requires_fresh_validation` (1) | expose restored completed observation immediately after adoption; GET after positive adoption but before due returns no actionable labels. |
| PC-81 / G-81 | PENDING | `HerdrLabelFollowSchedulingTests.Cached_candidate_requires_positive_current_get` (2) | treat VerifyHerdrLivenessAsync unreachable-true as confirmation; unreachable or missing recorded child suppresses cached actionable labels. |
| PC-82 / G-82 | PENDING | `HerdrLabelFollowSchedulingTests.Cached_candidate_is_suppressed_after_move` (3) | omit only current PaneId equality on cached GET; PaneId-only mismatch during cooldown suppresses candidate with zero new label getters. |
| PC-83 / G-83 | PENDING | `HerdrLabelFollowWireTests.Old_peers_and_sidecars_remain_compatible_without_follow` (2) | map null/unknown follow version to actionable default binding; old/unknown DTO keeps follow result null and DB pins unchanged. |
| PC-84 / G-84 | PENDING | `HerdrLabelFollowTests.Current_pointer_must_still_name_the_session` (1) | remove current-session pointer predicate; another current session leaves both pins unchanged. |
| PC-85 / G-85 | PENDING | `HerdrLabelFollowTests.Physical_owner_must_match_both_launch_and_session` (2) | remove only launch-owner equality; different launch StandingAgentId preserves pins. |
| PC-86 / G-86 | PENDING | `HerdrLabelFollowTests.Physical_owner_must_match_both_launch_and_session` (2) | remove only AgentSession.StandingAgentId equality; different persisted session owner preserves pins. |
| PC-87 / G-87 | PENDING | `HerdrLabelFollowTests.Same_id_new_generation_rejects_old_observation` (2) | compare SessionId without AcceptedStartedAt; old same-ID generation cannot change current pin. |
| PC-88 / G-88 | PENDING | `HerdrLabelFollowTests.Only_live_standing_herdr_agents_are_eligible` (9) | remove only attached-origin rejection; otherwise valid attached DTO preserves pins. |
| PC-89 / G-89 | PENDING | `HerdrLabelFollowTests.Only_live_standing_herdr_agents_are_eligible` (9) | remove only CardId eligibility predicate; card-owned session preserves pins. |
| PC-90 / G-90 | PENDING | `HerdrLabelFollowTests.Only_live_standing_herdr_agents_are_eligible` (9) | remove only IsPoolDelegate predicate; pool-agent fixture preserves pins. |
| PC-91 / G-91 | PENDING | `HerdrLabelFollowTests.Only_live_standing_herdr_agents_are_eligible` (9) | omit only agent backend predicate; PtyHost agent with Herdr session preserves pins. |
| PC-92 / G-92 | PENDING | `HerdrLabelFollowTests.Only_live_standing_herdr_agents_are_eligible` (9) | omit only session backend predicate; Herdr agent with PtyHost session preserves pins. |
| PC-93 / G-93 | PENDING | `HerdrLabelFollowTests.Only_live_standing_herdr_agents_are_eligible` (9) | omit only initial live-status predicate; Stopped/Failed/Ended session preserves pins. |
| PC-94 / G-94 | PENDING | `HerdrLabelFollowConcurrencyTests.Away_and_back_or_clear_and_repin_does_not_rearm` (2) | omit token equality in follow writer; ABA and clear/re-pin retain manual configuration after delayed follow. |
| PC-95 / G-95 | PENDING | `HerdrPlacementSettingsTests.Manual_placement_edit_rotates_only_the_internal_token` (1) | omit token rotation for explicit tab field; new token differs after tab reaffirmation/clear. |
| PC-96 / G-96 | PENDING | `HerdrPlacementSettingsTests.Manual_placement_edit_rotates_only_the_internal_token` (1) | omit token rotation for explicit workspace field; new token differs after workspace reaffirmation/clear. |
| PC-97 / G-97 | PENDING | `HerdrPlacementSettingsTests.Placement_context_changes_invalidate_follow_intent` (1) | omit backend-change token rotation; backend away-and-back has changed token and old observation is refused. |
| PC-98 / G-98 | PENDING | `HerdrPlacementSettingsTests.Placement_context_changes_invalidate_follow_intent` (1) | omit board-change token rotation; board away-and-back has changed token and old observation is refused. |
| PC-99 / G-99 | PENDING | `HerdrLabelFollowConcurrencyTests.Manual_edit_before_follow_wins` (1) | remove agent row lock from manual placement update; barrier-raced manual commit wins; old-token follow does not overwrite it. |
| PC-100 / G-100 | PENDING | `HerdrLabelFollowConcurrencyTests.Manual_edit_after_follow_wins` (1) | use pre-lock tracked label value to decide whether supplied label is modified; manual same-old-spelling after committed follow persists manual spelling. |
| PC-101 / G-101 | PENDING | `HerdrLabelFollowConcurrencyTests.Follow_reloads_after_runner_response` (2) | retain pre-network tracked Agent without reload; edit/pointer swap during HTTP barrier preserves replacement state. |
| PC-102 / G-102 | PENDING | `HerdrLabelFollowConcurrencyTests.Conditional_write_rechecks_session_generation` (1) | remove only generation condition from final SQL update; generation changed after validation affects zero rows; labels/watermark unchanged. |
| PC-103 / G-103 | PENDING | `HerdrLabelFollowConcurrencyTests.Conditional_write_rechecks_session_liveness` (1) | remove only live-session condition from final SQL update; session terminated after validation affects zero rows; labels/watermark unchanged. |
| PC-104 / G-104 | PENDING | `HerdrLabelFollowTests.Older_or_duplicate_sequence_cannot_roll_back_labels` (1) | allow sequence <= stored sequence; B then delayed A remains B and duplicate gives no event. |
| PC-105 / G-105 | PENDING | `HerdrLabelFollowTests.Expired_future_or_unverified_observation_is_ignored` (8) | omit only expiry predicate; at expiry and after expiry pins unchanged. |
| PC-106 / G-106 | PENDING | `HerdrLabelFollowTests.Expired_future_or_unverified_observation_is_ignored` (8) | omit only future-clock predicate; completion now+30s+1 tick preserves pins. |
| PC-107 / G-107 | PENDING | `HerdrLabelFollowTests.Expired_future_or_unverified_observation_is_ignored` (8) | allow refused/in-progress result with candidate fields; fabricated candidate on refusal/in-progress does not change pins. |
| PC-108 / G-108 | PENDING | `HerdrLabelFollowTests.Null_pins_are_never_created_even_with_claimed_intent` (2) | assign candidate TabLabel regardless of current null; null tab remains null even with otherwise valid nonnull launch tab intent. |
| PC-109 / G-109 | PENDING | `HerdrLabelFollowTests.Null_pins_are_never_created_even_with_claimed_intent` (2) | assign candidate WorkspaceLabel regardless of current null; null workspace remains null even with valid nonnull workspace intent. |
| PC-110 / G-110 | PENDING | `HerdrLabelFollowTests.Null_launch_intent_cannot_authorize_follow` (2) | ignore launch explicit-tab intent while current pin exists; malformed same-token DTO with null launch tab intent preserves tab pin. |
| PC-111 / G-111 | PENDING | `HerdrLabelFollowTests.Null_launch_intent_cannot_authorize_follow` (2) | ignore launch explicit-workspace intent while current pin exists; malformed same-token DTO with null launch workspace intent preserves workspace pin. |
| PC-112 / G-112 | PENDING | `HerdrLabelFollowTests.Labels_and_watermark_commit_together_and_only_changes_notify` (1) | commit watermark separately before label write; injected label-write rollback leaves old labels AND old watermark. |
| PC-113 / G-113 | PENDING | `HerdrLabelFollowTests.Labels_and_watermark_commit_together_and_only_changes_notify` (1) | publish event before commit; event callback reading a fresh DB scope sees new pins and watermark. |
| PC-114 / G-114 | PENDING | `HerdrLabelFollowTests.Labels_and_watermark_commit_together_and_only_changes_notify` (1) | unconditionally stamp and publish for any processed observation; equal/refusal/duplicate leaves UpdatedAt and event count unchanged. |
| PC-115 / G-115 | PENDING | `HerdrLabelSnapshotTests.Failed_persistence_does_not_publish_or_advance_cached_labels` (1) | publish result/cache before SaveAtomic; injected save failure preserves prior cache/file labels and has no success receipt. |
| PC-116 / G-116 | PENDING | `HerdrLabelSnapshotTests.Concurrent_snapshot_saves_use_distinct_temp_files` (1) | replace unique temp suffix with shared .tmp; two writers gated after temp creation have distinct temp paths and both complete without corruption. |
| PC-117 / G-117 | PENDING | `HerdrLabelSnapshotTests.Observer_coordinates_with_existing_pane_actors` (12) | skip observer pane lease; held-pane fixture has zero label writes until release; observer and actor critical sections never overlap. |
| PC-118 / G-118 | PENDING | `HerdrLabelSnapshotTests.Retirement_and_refresh_cannot_resurrect_or_revert_sidecar` (3) | save captured record without final current-object/generation comparison; replacement-generation/retired record is not overwritten or resurrected. |
| PC-119 / G-119 | PENDING | `HerdrLabelSnapshotTests.Retirement_and_refresh_cannot_resurrect_or_revert_sidecar` (3) | retire from record captured before successful follow; follow-first arm retires New labels, never Old. |
| PC-120 / G-120 | PENDING | `HerdrLabelSnapshotTests.Observer_preserves_lock_order` (1) | request pane lock before workspace lock; recorded acquisition order equals workspace-key, workspace-id, pane with no recursive pane acquisition. |
| PC-121 / G-121 | PENDING | `HerdrLabelSnapshotTests.Blocked_observation_does_not_block_unrelated_runtime_reads` (1) | hold runtime gate during awaited getter via synchronous wait; unrelated session List/Get completes while selected getter is gated. |
| PC-122 / G-122 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_requires_exact_generation_and_binding` (9) | synthesize last-pane when load returns null; absent last-pane remains absent after valid follow. |
| PC-123 / G-123 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_requires_exact_generation_and_binding` (9) | omit only last-pane SessionId comparison; mismatched-session JSON bytes remain identical. |
| PC-124 / G-124 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_requires_exact_generation_and_binding` (9) | omit only last-pane generation comparison; old/null-generation JSON bytes remain identical. |
| PC-125 / G-125 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_requires_exact_generation_and_binding` (9) | omit only last-pane WorkspaceId comparison; different-workspace JSON bytes remain identical. |
| PC-126 / G-126 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_requires_exact_generation_and_binding` (9) | omit only last-pane TabId comparison; different-tab JSON bytes remain identical. |
| PC-127 / G-127 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_requires_exact_generation_and_binding` (9) | omit only last-pane PaneId comparison; different-pane JSON bytes remain identical. |
| PC-128 / G-128 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_requires_exact_generation_and_binding` (9) | omit only last-pane origin check; attached-origin JSON bytes remain identical. |
| PC-129 / G-129 | PENDING | `HerdrLabelSnapshotTests.Last_pane_refresh_preserves_retirement_metadata` (1) | rebuild last-pane with FromSidecar instead of a label-only with-expression; ExitedAtUtc/ExitReason and all non-label fields remain identical. |
| PC-130 / G-130 | PENDING | `HerdrLabelSnapshotTests.Last_pane_write_failure_is_repaired_without_new_observation` (1) | save sidecar labels without the required matching-last-pane repair marker; crash after first-file commit reloads marker and repairs matching last-pane. |
| PC-131 / G-131 | PENDING | `HerdrLabelSnapshotTests.Last_pane_write_failure_is_repaired_without_new_observation` (1) | return for cooldown before attempting local repair; second wakeup repairs New labels with unchanged attempt sequence and zero getter delta. |
| PC-132 / G-132 | PENDING | `HerdrLabelSnapshotTests.Repair_marker_survives_failure_and_respects_replacement` (2) | clear repair marker before last-pane save; second injected failure retains durable marker; later successful retry clears it. |
| PC-133 / G-133 | PENDING | `HerdrLabelSnapshotTests.Repair_marker_survives_failure_and_respects_replacement` (2) | retry second-file write using captured record without exact-binding recheck; replacement last-pane bytes stay unchanged and obsolete marker clears. |
| PC-134 / G-134 | PENDING | `HerdrLabelFollowFlowTests.Db_failure_retries_the_same_current_snapshot` (1) | advance in-memory seen-sequence on failed GET/save and skip retry; recreated service commits exact current pins/watermark once and relaunch uses original tab. |
| PC-135 / G-135 | PENDING | `HerdrLabelFollowFlowTests.Missed_observation_is_recovered_after_server_restart` (1) | omit candidate when live labels already equal sidecar labels; server absent through initial follow and runner restart still reaches New DB pin after fresh due check. |
| PC-136 / G-136 | PENDING | `HerdrLabelFollowTests.Disabled_sweep_polls_nothing` (1) | ignore Enabled=false; runner GET count stays zero across two simulated ticks. |
| PC-137 / G-137 | PENDING | `HerdrLabelFollowTests.Sweep_is_independent_of_corroboration_and_turn_state` (1) | return from sweep on first session exception; second eligible agent reaches New pin after first GET fails. |
| PC-138 / G-138 | PENDING | `HerdrLabelFollowFlowTests.Observation_never_mutates_herdr_furniture` (1) | invoke existing TabRenameAsync with unchanged label during observation; observation request delta contains zero tab.rename and zero other mutating RPCs. |
| PC-139 / G-139 | PENDING | `HerdrLabelFollowFlowTests.Label_failure_does_not_change_session_lifecycle` (1) | mark session Exited on label collection timeout; same live session/generation/status remains and kill counter is zero. |
| PC-140 / G-140 | PENDING | `HerdrLabelFollowFlowTests.Renamed_pin_is_used_by_the_next_named_launch` (4) | drop candidate label in SessionRunnerHttpClient mapping while preserving transport success; DB/detail show New and next production named launch reuses original tab; no new tab. |
| PC-141 / G-141 | PENDING | `HerdrLabelObservationTests.Collection_requires_positive_recorded_child` (3) | accept unknown/missing recorded ChildPid or absence from process_info; otherwise eligible collection produces no candidate when child identity is unproven. |
| PC-142 / G-142 | PENDING | `HerdrLabelFollowSchedulingTests.Cached_candidate_is_suppressed_after_move` (3) | omit only current TabId equality on cached GET; coherent pane move during cooldown suppresses candidate without new label getters. |
| PC-143 / G-143 | PENDING | `HerdrLabelFollowSchedulingTests.Cached_candidate_is_suppressed_after_move` (3) | omit only current WorkspaceId equality on cached GET; WorkspaceId-only mismatch during cooldown suppresses candidate without new label getters. |
| PC-144 / G-144 | PENDING | `HerdrLabelFollowSchedulingTests.Disabled_or_stopped_pump_does_no_label_work` (2) | start label timer despite Herdr Enabled=false; two simulated timer ticks produce zero label RPCs. |
| PC-145 / G-145 | PENDING | `HerdrLabelFollowSchedulingTests.New_generation_is_immediately_eligible` (1) | inherit previous same-ID generation next-due time; new generation at old minute 30 completes first observation without advancing clock. |
| PC-146 / G-146 | PENDING | `HerdrLabelFollowConcurrencyTests.Specialist_lock_order_is_preserved` (1) | acquire specialist seat before owner in follow or manual writer; EF command interception records owner then seat for both writers; concurrent ordinary owner operation completes. |

Native V-21 remains open acceptance. No OS-power-loss/fsync, live provider prompt receipt, browser repaint or durable SignalR inbox claim is made; these are the plan’s stated exclusions/substitutes. No new OnTurnEnd hook was added.

## Retained outputs and settlement

Automatic approval review rejected the cleanup command for the producer-owned build outputs and disposable base worktree with the sole reason **“blocked by policy.”** The command did not start, so all 31 existing inventoried output directories and the base worktree remain. No alternative deletion was attempted to bypass the rejection.

- Base worktree retained: `C:\Antiphon\worktrees\card-task-05a66230\.antiphon\c462-base` (detached original base).
- Exact existing output paths and rejection: `C:\Antiphon\worktrees\card-task-05a66230\.antiphon\c462-05a66230\cleanup-blocked.json`.
- Before-build absence inventories: `C:\Antiphon\worktrees\card-task-05a66230\.antiphon\c462-05a66230\output-candidates.txt` and `C:\Antiphon\worktrees\card-task-05a66230\.antiphon\c462-05a66230\base-output-candidates.txt`.
- Original landing owner must arrange approved cleanup; retain the evidence root. All owned build/test processes were awaited and completed. No branch was landed and no stack was restarted.

--- next stage ---
next: review
handoff: Review CARD-0462 S1-S5 for original landing owner 05a66230. 396 named cases pass; four Unit failures match base; V-21 native skipped. PC-28–146 remain pending for post-land SourceLanding Mutation; inspect PC-56 masking. Restart: server / runner. Automatic approval blocked output/base cleanup; paths in evidence.
artifact: docs/investigations/2026-09-16-card-0462-code-verification.md
