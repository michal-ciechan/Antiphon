# Run e7f92f66 split-8 checkpoints

Debug task `ebb01ffc`. Base `6cf3a2680e08321509cf9fbc4d587474381a6fd4`.
Budget pin `af6d5b33a9b6374fe323f1d484ed38b63e0b81f7`. Land-graph refresh
`15503f6ca8a5b2ffbd3cfb5f5d485c6ba4e02ca5`. Why missed:
`docs/investigations/2026-11-20-why-missed-run-e7f92f66-split-8-and-land-graph.md`.

The class is 382 results. Summed cost on the historical rc row is about 3505s,
above one 570s session. Width is 4 (`DelegationRecoveryLoadLimit`). Shards below
keep each filter under that cap. CP-1 carries EstimatedMinutes 15 so its cold
build deadline is 45 minutes. Later rows reuse that build and stay under 15.
CP-6 and CP-7 stay in the pin After group: PlanTableImporter refuses a reused
build whose After differs from CP-1. Do not pass `--row-timeout`. Mutation is waived (operator 2026-11-12). Short
tests only (operator 2026-11-18).

## Verification design

V-1: the three recovery budgets equal 70, 65, and 65, and the other eleven
budget arguments stay at their previous pins. R-1: the other 368 rows of
`DelegationDispatchRecoveryBoundaryTests` still pass. R-2:
`TestClassificationPolicyTests` passes once, inside the guard union.
R-3: `DerivedImpactIndexTests` passes on the committed index, driver built
first; regenerate only if the index bytes change, as its own last commit, run
twice. R-4: the eleven-class guard union passes, and C1729 reports
`VIOLATIONS 0` and `LISTED 162`. R-5: `scripts/audit-land-verification-graph.ps1 -Check`
exits 0 against digest `EA552D85` count 80. R-6: `scripts/check-evidence-diff.ps1`
over `6cf3a2680e08321509cf9fbc4d587474381a6fd4..HEAD` reports no violations.
R-7: nightly `policyHash` recomputed from master inputs stays
`8c376b03c4e012e3700de15e8b99bdbe851187bce4d3c5defe2b6b6df1f782e2` unless a
guard fails after an input edit. This task does not edit those inputs.

No whole Unit lane. No product restart. Desktop filter for the caller after
land: `/*/*/DelegationDispatchRecoveryBoundaryTests/*`.

### Checkpoints

Rows are serial. CP-2..CP-7 reuse the CP-1 build. Command rows do not build.
One build and one exact filter per row.

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | pin | `tests/Antiphon.Tests -> bin-e7f92-split8/` | c1149 | `/*/*/DelegationDispatchRecoveryBoundaryTests/C1149*` | V-1 | 63 executed, 0 failed, 0 skipped | 63 | 15 | true |
| CP-2 | pin | CP-1 | brief-matrix | `/*/*/DelegationDispatchRecoveryBoundaryTests/(C1150_Brief_evidence_whitelist_flips_one_condition*)\|(C1150_Brief_envelope_corpus_matches_the_expected_matrix*)` | R-1 | 121 executed, 0 failed, 0 skipped | 121 | 6 | true |
| CP-3 | pin | CP-1 | brief-surface | `/*/*/DelegationDispatchRecoveryBoundaryTests/(C1150_Brief_decision_table_flips_one_condition*)\|(C1150_Inline_brief_mentioning_spill_paths_is_not_held*)\|(C1150_Outer_envelope_and_receipt_floor_through_the_queue*)\|(C1150_Local_spill_must_be_present_and_intact*)\|(C1150_Spill_pointer_forms_fail_closed*)\|(C1150_Late_ensure_after_remote_receipt_and_payload_release*)\|(C1150_Concurrent_producers_ensure_one_brief*)\|(C1150_Existing_brief_and_spill_are_byte_identical*)\|(C1150_Old_attempt_evidence_does_not_suppress_current_brief*)\|(C1150_Uncertain_evidence_cannot_create_a_replacement*)\|(C1150_Real_producers_race_to_one_brief_and_keep_the_followup*)\|(C1150_Working_probe_failure_keeps_the_committed_brief*)` | R-1 | 70 executed, 0 failed, 0 skipped | 70 | 6 | true |
| CP-4 | pin | CP-1 | recovery-path | `/*/*/DelegationDispatchRecoveryBoundaryTests/(C1150_Discovery_gates_withhold_recovery*)\|(C1150_Followup_survives_both_queue_orders*)\|(C1150_Insert_before_flush_cut_reuses_pending_row*)\|(C1150_Running_cut_is_discovered_and_delivered_once*)\|(C1150_Pre_input_refusal_keeps_the_resume_failure_path*)\|(C1150_Readiness_or_persistence_fault_preserves_custody*)\|(C1150_Recovery_exhaustion_is_visible_without_replacement*)\|(C1150_Resume_input_rule_classifies_every_delivery_result*)\|(C1150_Resumed_backfill_event_save_failure_keeps_the_working_recipient*)\|(C1150_Resumed_launch_post_input_bookkeeping_flips_one_step*)\|(C1150_Watchdog_cleanup_requires_failure_and_fresh_safe_evidence*)\|(C1150_Watchdog_first_recovers_before_failure*)` | R-1 | 61 executed, 0 failed, 0 skipped | 61 | 6 | true |
| CP-5 | pin | CP-1 | remainder | `/*/*/DelegationDispatchRecoveryBoundaryTests/(C1151*)\|(C1153*)\|(C1161*)\|(C1182*)\|(C1186*)\|(C1213*)\|(C1591*)` | R-1 | 67 executed, 0 failed, 0 skipped | 67 | 6 | true |
| CP-6 | pin | CP-1 | derived-index | `/*/*/(DerivedImpactIndexTests*)|(DerivedImpactIndexInvariantTests*)/*` | R-3 | 27 executed, 0 failed, 0 skipped | 27 | 4 | true |
| CP-7 | pin | CP-1 | guard-union | `/*/*/(TestClassificationPolicyTests*)\|(TestClassificationGuardTests*)\|(SlowTestRegistryMergeTests*)\|(SlowTestTripwireTests*)\|(NativeLinuxPolicyWitnessTests*)\|(DerivedImpactIndexTests*)\|(MechanicalPcContractTests*)\|(TaskPlatformGuidanceTests*)\|(C1729LayeringGuardTests*)\|(TestLaneCategoryGuardTests*)\|(WindowsUnitPlatformAdmissionTests*)\|(DerivedImpactIndexInvariantTests*)/*` | R-2, R-4 | 0 failed; Linux may skip C1210_Windows_linux_only_outcomes_are_exact; C1729 VIOLATIONS 0 LISTED 162 | 114 | 6 | true |
| CP-8 | graph | n/a | land-graph | `pwsh -NoProfile -File scripts/audit-land-verification-graph.ps1 -Check` | R-5 | exit 0; digest EA552D856F649EEDF024C84E76676F00D5547BEF4B1BC77573ABF5BF6B43C7CC count 80 | n/a | 3 | true |
| CP-9 | evidence | n/a | evidence-diff | `pwsh -NoProfile -File scripts/check-evidence-diff.ps1 -BaseRef 6cf3a2680e08321509cf9fbc4d587474381a6fd4 -HeadRef HEAD` | R-6 | violations 0 | n/a | 2 | true |

CP-1 through CP-5 are the whole class: 63+121+70+61+67 = 382. CP-4's
`C1150_Pre_input_refusal_keeps_the_resume_failure_path*` covers that method and
its `_for_a_local_command` and `_for_a_transport_refusal` siblings (6+8+18).
`TestClassificationPolicyTests` is not given a second row; CP-7 is its one run.
Build the derived-impact driver (`scripts/regenerate-derived-impact-index`)
before CP-6. CP-7 repeats that class inside the union because the union is closed.
Nightly pin recompute is R-7, not a TUnit row: read `policyHash` from master,
recompute with `Get-NightlyPolicyHash`, and re-pin only in its own commit if a
guard fails after an input edit.
