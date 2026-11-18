# CARD-1989 control window and timings

Base: e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4. Code owner: 62cec2de.

The coordinator and live proof assertion share a configurable wall-clock budget.
Use `ANTIPHON_RETIRE_CONTROL_WINDOW_MS`, default 8000, range 3000..8000 (floor raised from
1000 by the round-2 repair below).
The ceiling preserves the host's existing 10-second response read and 11-second
effect fence. A 15-second default would require widening those independent gates;
that is outside this repair. No proof, ref, nonce, renewal or effect gate changes.

Record proof read, bundle hash, expected parse, ls-remote, live parse, comparison
and cleanup durations, including failures, in the coordinator receipt. Fixed step
names and numeric times only; no URLs, advertisements or raw errors.

S1 commits offline red witnesses before production changes. S2 implements the
window and timings. No deliberate mutants in Code; SourceLanding Mutation owns
PC-1 (remove budget check), PC-2 (discard timings), PC-3 (ignore configuration),
PC-4 (omit failure receipt), all pending; operator has waived mutation execution.

## Performance budget

Script witnesses use one tiny temporary proof, no database, Docker or network.
V-1 deliberately waits 5.5 seconds, V-2 1.2 seconds, others under 2 seconds plus
PowerShell startup. Estimates are provisional for CARD-1989, collect one red and
one green sample. No repeats after green. The first row's 15-minute estimate is
the brief's cold bootstrap budget, not an execution timeout increase. Rows are
serial with a ten-minute command ceiling and a 570-second TUnit limit. Build the checkpoint driver in native bin/;
command rows need no build, avoiding prohibited custom output directories.

## Verification design

V-1: a live read over the old five seconds passes inside eight seconds.
V-2: a hash that exceeds a one-second configured budget refuses before Git, names
bundle-hash and reports its actual elapsed time.
V-3: all step durations are populated and Git receives only remaining budget; a
successful proof at the 3000 ms floor with the measured steady hash (1.4 s) and
ls-remote (1.15 s) maxima passes, and scratch cleanup is recorded but not charged.
R-1: withdrawn refs and bundle tampering still refuse.
V-4: absent, empty, valid and invalid configuration states.
V-5: coordinator persists success/failure timing receipts (offline protocol fixture).
R-2: affected class selections, bounded to short rows; no whole slow class.
R-3: pin recomputation, land graph, evidence diff, merge-tree, OS branch audit.
V-6: inspect whether renewals can share a bundle without changing proof semantics.
V-7: a proved branch that fast-forwards during the window (CARD-1986 graph path)
refuses closed at step moved-ref with control-deadline when the graph read cannot
finish in the window (CARD-1992), never as an admitted or withdrawn ref.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---:|---|
| CP-1 | S1-S2 | n/a | window | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case window` | V-1 | 1 PASS, zero failures | n/a | 15 | true |
| CP-2 | S1-S2 | n/a | deadline | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case deadline` | V-2 | 1 PASS, zero failures | n/a | 1 | true |
| CP-3 | S1-S2 | n/a | timings | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case timings` | V-3 | 1 PASS, zero failures | n/a | 1 | true |
| CP-4 | S1-S2 | n/a | guards | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case guards` | R-1 | 1 PASS, zero failures | n/a | 1 | true |
| CP-5 | S1-S2 | n/a | config | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case config` | V-4 | 1 PASS, zero failures | n/a | 1 | true |
| CP-6 | S1-S2 | n/a | receipt | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case receipt` | V-5 | 1 PASS, two protocol exchanges, zero failures | n/a | 1 | true |
| CP-7 | S1-S2 | n/a | host-proof | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Build -Class VolumeRetirementHostProofTests` | R-2 | 19 fresh TRX results, zero failures | n/a | 15 | true |
| CP-8 | S2 | n/a | adjacent-contracts | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Build -Class '(ClaudeTokenRefreshOptInTests*)\|(DockerStackContractTests*)\|(HostJqFixtureClosureTests*)'` | R-2 | every named class nonzero, zero failures | n/a | 15 | true |
| CP-9 | S2 | n/a | remote-bridge | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RemoteScriptContractTests -Method '(Custody_containment_is_routed*)\|(Temp_retire_requires*)\|(C849_Cache_cases_use_only*)'` | R-2 | 3 fresh TRX results, zero failures | n/a | 2 | true |
| CP-10 | S2 | n/a | host-jq | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class HostJqPrerequisiteScriptTests -Method '(C1058_Receipt_formatter*)\|(C1058_Proof_is_buffered*)\|(C1025_Check_qualifies*)'` | R-2 | each named method nonzero, zero failures | n/a | 2 | true |
| CP-11 | S2 | n/a | exchange-content | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementContentTests -Method Exchange_*` | R-2 | 7 fresh TRX results, zero failures | n/a | 3 | true |
| CP-12 | S2 | n/a | effect-fence | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementReceiptTests -Method '(One_repository_measured*)\|(Control_observation_older*)\|(Every_delete_rechecks*)'` | R-2 | 3 fresh TRX results, zero failures | n/a | 3 | true |
| CP-13 | S2 | n/a | publication-control | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementPublicationTests -Method '(Salvage_withdrawn_after_staging*)\|(Head_rolled_back_after_staging*)\|(Origin_unreachable_after_staging*)\|(Final_origin_binding_refresh*)'` | R-2 | 4 fresh TRX results, zero failures | n/a | 4 | true |
| CP-14 | S2 | n/a | apply-guards | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementApplyGateTests` | R-2 | every method, zero failures | n/a | 3 | true |
| CP-15 | S2 | n/a | wrappers | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class VolumeRetirementWrapperTests` | R-2 | 7 fresh TRX results, zero failures | n/a | 3 | true |
| CP-16 | S2 | n/a | retired-host | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RetiredTempContainerHostTests -Method C994_Each_removal_rechecks_live_proofs` | R-2 | 1 fresh TRX result, zero failures | n/a | 2 | true |
| CP-17 | S2 | n/a | ref-publication | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class C0824BranchDeletionRecycleAuditTests` | R-2 | 3 fresh TRX results, zero failures | n/a | 3 | true |
| CP-18 | S2 | n/a | bound-land-wait | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class C1364BoundLandWaitProofTests -Method C1364_Wrapper_*` | R-2 | 3 fresh TRX results, zero failures | n/a | 2 | true |
| CP-19 | S2 | n/a | layering | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class C1729LayeringGuardTests` | R-3 | zero violations, listed 162 | n/a | 2 | true |
| CP-20 | S2 | n/a | derived-index | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class DerivedImpactIndexTests -Method committed_index_matches_the_builder` | R-3 | 1 fresh TRX result, zero failures | n/a | 2 | true |
| CP-21 | S2 | n/a | declaration-category | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class TestLaneCategoryGuardTests` | R-3 | every method, zero failures | n/a | 2 | true |
| CP-22 | S2 | n/a | adjacent-22 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RetiredTempContainerScriptTests -Method '(C994_Bridge_transport_preserves_literal_context*)\|(C994_Cleanup_receipt_is_required_before_recycle*)\|(C994_Drain_uses_one_deadline*)\|(C994_Drain_waits_for_exit_and_removes_containers*)\|(C994_Host_bridge_transport_preserves_literal_context*)\|(C994_Host_resume_never_runs_container_cleanup*)'` | R-2 | 6 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-23 | S2 | n/a | adjacent-23 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RetiredTempContainerScriptTests -Method '(C994_Host_strict_volume_admission_remains_independent*)\|(C994_Preview_reports_pending_volume_proof*)\|(C994_Resume_never_runs_container_cleanup*)\|(C994_Retire_recovers_exited_and_absent_temp*)\|(C994_Separate_invocations_preserve_audit_identity*)\|(C994_Strict_volume_admission_remains_independent*)'` | R-2 | 6 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-24 | S2 | n/a | adjacent-24 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RetiredTempContainerScriptTests -Method '(C994_Verifier_manifest_is_strict*)\|(C994_Wrapper_main_admission_is_required*)\|(C994_Wrapper_retirement_proof_is_typed*)\|(C994_Wrapper_task_and_land_census_is_complete*)'` | R-2 | 4 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-25 | S2 | n/a | native-build-smoke | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Build -Class RollingVolumeRecycleScriptTests -Method C1008_Documentation_and_transport_pins_match` | R-2 | 1 named method, nonzero, zero failures | n/a | 15 | true |
| CP-26 | S2 | n/a | legacy-failing-shell | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method C1008_Legacy_rolling_and_jq_rosters_remain_failing_shell` | R-2 | 1 complete legacy mode, nonzero, zero failures | n/a | 7 | true |
| CP-27 | S2 | n/a | rolling-27 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1008_Refusal_receipts_do_not_leak_secrets*)\|(C1008_Retired_absent_null_is_accepted*)\|(C1008_Same_sha_and_partial_retries_are_safe*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-28 | S2 | n/a | rolling-28 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1008_Wrapper_retired_absent_null_is_accepted*)\|(C1087_Census_filters_open_work_and_pending_lands*)\|(C1087_Check_census_phase_is_read_only*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-29 | S2 | n/a | rolling-29 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1319_Census_tolerates_a_pass0_settled_task_that_leaves_before_pass1*)\|(C1324_Census_tolerates_a_no_land_task_that_appears_in_pass1*)\|(C1326_Census_refuses_a_land_timestamp_or_land_pending_appearance*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-30 | S2 | n/a | rolling-30 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1361_Wrapper_census_tolerates_open_status_changes_between_passes*)\|(C1371_Census_distinguishes_cross_read_count_drift_from_malformed_counts*)\|(C1371_Post_removal_count_drift_does_not_abort*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-31 | S2 | n/a | rolling-31 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1594_Disagreeing_cross_sha_images_refuse*)\|(C1594_Older_retirement_receipt_is_ignored*)'` | R-2 | 2 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-32 | S2 | n/a | rolling-32 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method C1008_Busy_routed_and_land_in_flight_refuse` | R-2 | 1 named method, nonzero, zero failures | n/a | 8 | true |
| CP-33 | S2 | n/a | rolling-33 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1008_Host_proof_file_omits_credentials*)\|(C1008_Legacy_rolling_and_jq_rosters_remain_absent*)'` | R-2 | 2 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-34 | S2 | n/a | rolling-34 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1008_Option_manifest_is_strict*)\|(C1008_Present_or_unknown_temp_keeps_null_refusal*)\|(C1008_Preview_helper_removal_is_owned*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-35 | S2 | n/a | rolling-35 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1008_Transport_scripts_are_ascii*)\|(C1008_Wrapper_option_manifest_is_strict*)\|(C1008_Wrapper_refusal_receipts_do_not_leak_secrets*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-36 | S2 | n/a | rolling-36 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1087_Project_resolves_by_repository_identity*)\|(C1087_Read_failures_name_their_cause*)\|(C1317_Census_tolerates_a_task_that_settles_without_a_land*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-37 | S2 | n/a | rolling-37 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1353_Host_census_passes_off_runner_lands_and_waits_for_bound_lands*)\|(C1353_Wrapper_waits_for_a_bound_land_then_proceeds*)\|(C1361_Wrapper_census_tolerates_open_status_changes_between_list_and_detail*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-38 | S2 | n/a | rolling-38 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1374_Wrapper_census_recovers_transient_reads*)\|(C1594_Antiphon_evidence_gitdir*)\|(C1594_Cross_sha_cleanup_image_binds_the_retired_temp*)'` | R-2 | 3 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-39 | S2 | n/a | rolling-39 | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1693_Retire_temp_census_ignores_standing_runner_churn_between_passes*)\|(C1697_Wrapper_standing_census_compares_only_rows_bound_to_server2*)'` | R-2 | 2 named methods, nonzero each, zero failures | n/a | 3 | true |
| CP-40 | S2 | n/a | legacy-missing-shell | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method C1008_Legacy_rolling_and_jq_rosters_remain_missing_shell` | R-2 | 1 complete legacy mode, nonzero, zero failures | n/a | 7 | true |
| CP-41 | S2 | n/a | legacy-present | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method C1008_Legacy_rolling_and_jq_rosters_remain_present` | R-2 | 1 complete legacy mode, nonzero, zero failures | n/a | 7 | true |
| CP-43 | S3 | n/a | moved | `pwsh -NoProfile -File scripts/test-c1989-control-window.ps1 -Case moved` | V-7 | 1 PASS, zero failures | n/a | 1 | true |
| CP-42 | S2 | n/a | preview-private-origin | `pwsh -NoProfile -File scripts/test-c1989-affected.ps1 -Class RollingVolumeRecycleScriptTests -Method '(C1008_Dry_run_preview_binds_a_dry_run_cleanup_receipt*)\|(C1008_Host_proof_admits_only_an_authenticated_private_origin*)'` | R-2 | 2 named methods, nonzero each, zero failures | n/a | 7 | true |

The native-output adapter builds each committed group at CP-7, CP-8 or CP-25, records committed source and DLL
SHA-256, and refuses reuse at a different SHA or after a DLL change. Each row
emits a fresh TRX with counts; checkpoint command-row buildSource is honestly
notApplicable, with the additional native build binding in `.antiphon/c1989-native`.
All 17 impacted classes are represented, including TestLaneCategoryGuardTests
selected by the changed test declaration. The large adjacent classes have
method-scoped selections under the operator's SHORT-only override; the named
invariants are origin publication, final control binding, receipt and effect
fences, wrapper admission, helper custody, and unchanged bridge routing. No whole
Slow class or Unit lane is selected. No classification, selector or pin inputs are
edited, so the operator's explicit override omits the broad guard union.

Native Windows execution is not claimed. Changed logic is portable PowerShell,
with no new OS branch or production factory. Production script remains ASCII and
uses PowerShell 5.1-compatible syntax. No live retire-temp preview/apply is allowed.
Restart: none.

## Renewal cost decision (V-6)

`c1609_publication_gate` renews at every gate. The coordinator requests required
object coverage on renew; `Get-C1008RetireProofExports` passes `-FreshGraph`, and
`Invoke-C1609HostOriginProofAttempt` disables bundle reuse for FreshGraph. The
existing `Fresh_graph_renewal_fetches_again_for_unchanged_advertisement` witness
requires a second real fetch. Sharing one downloaded bundle across the five
renewals would change that invariant, so this repair retains it. The Debug note
measured 541.8 seconds of network across the renewals; this repair makes control
cost attributable but does not claim to reduce renewal traffic.

## Red provenance

Production remained byte-identical to base for the red runs. CP-1..CP-4 failed at
77f3c71a3df13da162a0671b1c91a638f88b458a: 8000 rejected, over-bound hash admitted,
timings missing, and widened-bound ref guard rejected as origin before reaching
the intended ref comparison. CP-5's absent-key fixture was corrected, then failed
for the missing config function at 342e4a0909ddb4480894a72cf41ff181bd4c5e2c.
CP-6's cleanup fixture was corrected, then failed for the missing timing receipt
at 4a579f8b6c1b561a77618ef0cb05c2e76fd3f45f. CP-7 at 342e4a0909ddb4480894a72cf41ff181bd4c5e2c
executed 19 methods: 18 passed, only the updated 8000-ms acceptance failed.
An earlier CP-7 was stopped and joined before any test child launched; its native
argument adapter was repaired to use literal ProcessStartInfo arguments. Preserve
these fixture failures and stopped-run costs; they are not passing evidence.

At production commit 81ba831c0cf3cdc3961a662fc938affb4de875f6 CP-1, CP-2, CP-4
and CP-5 passed. CP-3 correctly refused when its cold fixture first imported the
file-management module during cleanup, exceeding its unchanged 1000-ms budget.
Fixture setup now uses New-Item, matching the real proof exchange's preparation;
the budget and all assertions remain unchanged. CP-6 was stopped and joined while
still polling host load, before its fixture began. Rerun only CP-3 and CP-6.

CP-8 at 0e1ea2a3b1db6accc20448f42eb9c668380d61e6 hit the unchanged 570-second execution cap (576.327 seconds including cleanup, 45-second slot wait) without a usable TRX. It has no passing credit. CP-9..CP-21 were stopped before execution. CP-8 now retains the three small classes; CP-22..CP-42 partition every public test method of the two larger classes into groups of at most six, with no method omitted. CP-8 builds the new committed group in native outputs; the existing CP-7 19/19 result remains at its actual tested SHA. No test assertion or timeout changed.

CP-25 at 71cf747671cef48a20f7f9c19f964bed7171f65e reached the unchanged 570-second test cap. The console reported five completed tests, but no fresh TRX was produced; the row has no passing credit. CP-26 then waited 165 seconds for a slot and another 90 seconds for load below 16. The run was stopped and joined as that command exceeded ten minutes; CP-26 has no TRX/pass credit, and CP-27..31 had not run. The remaining RollingVolumeRecycleScriptTests selections now contain at most three methods. CP-32 isolates the 44-scenario C1008_Busy_routed_and_land_in_flight_refuse method. CP-25 builds the new committed group. All 40 methods are preserved exactly once; no scenario, assertion or test timeout is removed or relaxed.

CP-11's expected count is corrected from eight to seven after source and fresh TRX inspection. Its Exchange_* filter is unchanged and every matching declared method passed.

Performance follow-up: serial run 20261118-072854-41f3 exceeded 30 minutes, triggering test-performance-standards R1. The caller must commission Debug with source 71cf747671cef48a20f7f9c19f964bed7171f65e, that run's exact resolved manifest, row logs, load samples and TRX durations. This Code delegate cannot sub-delegate. Preserve the initial CP-8 and CP-25 timeout costs and CP-26's command timeout (601.187 seconds including cleanup); none is credited as passing. The source and receipt inventory records slot waits separately from command execution and test duration.

At 222f576390547ee7c9e0821d08df92abab7b1dcd, run 20261118-084209-aab0 retained the ten-minute command limit. CP-25 built successfully in 3m28.36s but load waiting plus its test selection exhausted that command budget without a TRX. Its cold-build smoke now selects the existing documentation/transport contract alone; CP-42 retains the two behavioral methods. CP-26's three legacy modes also exhausted the command budget without a TRX: each mode runs a complete 62/66-child roster, so CP-26, CP-40 and CP-41 now each select one whole mode. No roster invocation, assertion or test deadline is removed or relaxed. Earlier green rows are not repeated.

That run finished at 3426.835 seconds (3156.782 execution, 270.053 admission),
with CP-27..CP-39 green and CP-25/26 timed out. Include it in the same required
performance Debug follow-up. Its manifest and original receipts retain the exact
source, filters and costs; parameterized methods are counted from fresh TRX.

## Round 2 repair (Code 05a3e971, Review c4d03262 blocker)

Review c4d03262 (`docs/superpowers/reviews/2026-11-18-card-1989-review.md` at
e1c263950b6ce4e2b950470b90385f246d57d679) found the 1000 ms floor could not finish a
successful proof: the finally block charged `Remove-Item -Recurse` of the empty
scratch (856 ms, 1073 ms) and the coordinator charged it again (step complete,
1068 ms over a 1000 ms window). Repair, all at the unchanged 8000 ms default and cap:

- Floor 3000 ms. One origin's measured steady controls are bundle hash 0.9-1.4 s and
  ls-remote 1.0-1.15 s, at most 2.55 s; proof read, parses and compare measured
  under 100 ms. 3000 ms leaves about 0.45 s. Several origins share one window, so a
  multi-origin control should keep the default.
- Cleanup: `Remove-C1609LiveScratch` uses `Directory.Delete` (2 ms) with the
  cmdlet as fallback for read-only Windows Git objects. It runs after the charged
  time is recorded, is reported as `cleanupMs` and the `cleanup` step, and never
  fails a successful proof; the coordinator subtracts per-origin `cleanupMs` from
  its window check and receipt. Every proof step still enforces the budget.
- Merge of master 9cb2550e74a5dd18cae82b4788abf02e50521435 (CARD-1986): the live
  moved-ref graph read is one budgeted step, `moved-ref`; a Git child killed at the
  remaining budget, or CARD-1986's remaining-time check, is reported as
  `control-deadline` at the running step. Withdrawn refs keep CARD-1986's
  `RecycleOriginMoved` diagnosis.
- CARD-1993: the stale five-second live-check comment is refreshed.

Red at e4712081b4e70f7ab68683d95ce19ec9a8d6c361 (production = merge 315d918f17):
CP-3 refused at step cleanup (elapsed 4997, limit 3000); CP-5 admitted 1000; CP-6
fast path `control-step=cleanup control-elapsed-ms=4036 control-limit-ms=3000`.
CP-43 passed already at the merge (behaviour confirmation, not a red witness).

Rows run this round (S3, one checkpoint run at the committed tip): CP-1..CP-6 and
CP-43 (all script cases share the merged function), CP-7 (VolumeRetirementHostProofTests,
CARD-1986 and CARD-1989 witnesses together, native build), and the guard rows
CP-19 (layering), CP-20 (derived index), CP-21 (declaration category). CP-8..CP-18 and
CP-22..CP-42 are not re-run (operator SHORT-only, 2026-11-18); their earlier results
stay at their recorded SHAs and are not credited to this tip.
