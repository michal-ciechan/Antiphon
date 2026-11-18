# CARD-1986 retirement publication refs

Base: e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4. Only local script fixtures; no rollout invocation.

The initial audit may see the whole origin. A proof with required objects retains deterministic covering heads/salvage roots and exact tags. Renewal compares those dependencies; unrelated branches are outside the proof. A changed depended head requires old-tip ancestry; deletion, rewind and tag movement refuse with the ref and old/new tips. The unchanged local inventory/audit checks continue to fence changes to the retiring volume's own branches. Existing v2 wire headers and bundle equality remain authoritative.

## Performance budget

Each method uses one disposable local Git origin, no Docker, database, desktop or live service. Fixture setup/teardown already records TestTiming phases; process wall time and TRX remain under ignored evidence. Expected test execution below 60 seconds per row (no measured p95 yet); 570-second runner ceiling is the caller's oversubscription allowance, not a measured timeout. First cold build budget 15 minutes. Rows serial, no whole classes or repeats. Every driver uses build-slot, compiler policy 0, and starts only after load1 <16. Native default outputs are required by the brief; this overrides checkpoint custom output directories, so execute the table with the gated native build and exact TUnit filters, retaining counts/TRX per row. No classification/selector/pin changes are planned; broad guard union is excluded by the brief's short-tests override.

## Verification design

S1 commits witnesses before production changes and executes CP-1 through CP-4 red against base implementation (the new-branch control may pass). S2 fixes dependencies/diagnosis and executes the table green. Mutation checks waived by operator. Native Windows is not used; transport maps the approved fixture URL to a local bare origin while the real proof writer and bounded Git runner execute.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---|---|---|
| CP-1 | S2 | tests/Antiphon.Tests (native default) | unrelated | `/*/*/VolumeRetirementHostProofTests/C1986_Unrelated_origin_changes_preserve_renewal_and_live_proof` | unrelated forward/rewrite/delete/new | all listed, 0 failed | 4 | 15 | true |
| CP-2 | S2 | CP-1 | forward | `/*/*/VolumeRetirementHostProofTests/C1986_Depended_branch_fast_forward_preserves_proof` | depended ancestry | all listed, 0 failed | 1 | 1 | true |
| CP-3 | S2 | CP-1 | withdrawn | `/*/*/VolumeRetirementHostProofTests/C1986_Withdrawn_dependency_reports_ref_and_tips` | dependency delete/rewind/tag | all listed, 0 failed | 3 | 1 | true |
| CP-4 | S2 | CP-1 | diagnosis | `/*/*/VolumeRetirementHostProofTests/C1986_Result_boundary_preserves_withdrawal_diagnosis` | caller-visible result | all listed, 0 failed | 1 | 1 | true |
| CP-5 | S2 | CP-1 | custody | `/*/*/VolumeRetirementHostProofTests/Final_live_check_runs_git_outside_a_long_desktop_proof_directory` | short scratch and cleanup | all listed, 0 failed | 1 | 1 | true |
| CP-6 | S2 | CP-1 | coverage | `/*/*/VolumeRetirementHostProofTests/Fresh_graph_renewal_fetches_again_for_unchanged_advertisement` | independent renewal fetch | all listed, 0 failed | 1 | 1 | true |
| CP-7 | S2 | CP-1 | salvage | `/*/*/VolumeRetirementPublicationTests/Salvage_withdrawn_after_staging_refuses_control` | real offline control refusal | all listed, 0 failed | 1 | 1 | true |
| CP-8 | S2 | CP-1 | rewind | `/*/*/VolumeRetirementPublicationTests/Head_rolled_back_after_staging_refuses_control` | real offline control rewind | all listed, 0 failed | 1 | 1 | true |
