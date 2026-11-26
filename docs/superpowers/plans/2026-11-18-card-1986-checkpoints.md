# CARD-1986 retirement publication refs

Base: e229fb4d3fb859ee16f82bc553b1d4a5cfdccce4. Only local script fixtures; no rollout invocation.

The initial audit may see the whole origin. A proof with required objects retains deterministic covering heads/salvage roots and exact tags. Renewal compares those dependencies; unrelated branches are outside the proof. A changed depended head requires old-tip ancestry; deletion, rewind and tag movement refuse with the ref and old/new tips. The unchanged local inventory/audit checks continue to fence changes to the retiring volume's own branches. Existing v2 wire headers and bundle equality remain authoritative.

## Performance budget

Each method uses one disposable local Git origin, no Docker, database, desktop or live service. Fixture setup/teardown already records TestTiming phases; process wall time and TRX remain under ignored evidence. Expected test execution below 60 seconds per row (no measured p95 yet); 570-second runner ceiling is the caller's oversubscription allowance, not a measured timeout. First cold build budget 15 minutes. Rows serial, no whole classes or repeats. Every driver uses build-slot, compiler policy 0, and starts only after load1 <16. Native default outputs are required by the brief; this overrides checkpoint custom output directories, so execute the table with the gated native build and exact TUnit filters, retaining counts/TRX per row. The generated selector adds the diagnostic witness class to one script entry; CP-10 therefore runs the caller-required guard union once. Pins remain unchanged.

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
| CP-9 | S2 | CP-1 | narrowing | `/*/*/VolumeRetirementHostProofTests/C1986_Renewal_narrows_the_initial_full_origin_graph` | initial broad graph to bound renewal | all listed, 0 failed | 1 | 1 | true |
| CP-10 | S3 | CP-1 | requested-guards | `/*/*/(TestClassificationPolicyTests*\|TestClassificationGuardTests*\|SlowTestRegistryMergeTests*\|SlowTestTripwireTests*\|NativeLinuxPolicyWitnessTests*\|DerivedImpactIndexTests*\|MechanicalPcContractTests*\|TaskPlatformGuidanceTests*\|C1729LayeringGuardTests*\|TestLaneCategoryGuardTests*\|WindowsUnitPlatformAdmissionTests*)\|(DerivedImpactIndexInvariantTests*)/*` | requested guard union after derived selector update, including C1729 census | all listed, 0 failed | 1 | 5 | true |
| CP-11 | S2 | CP-1 | object-types | `/*/*/VolumeRetirementHostProofTests/Host_proves_complete_origin_ancestry` | commit/tree/blob/annotated-tag preservation and wrong/missing objects | all listed, 0 failed | 1 | 1 | true |
| CP-12 | S2 | CP-1 | salvage-objects | `/*/*/VolumeRetirementPublicationTests/Host_origin_salvage_proves_exact_required_objects` | multiple required commits plus a tree through salvage | all listed, 0 failed | 1 | 1 | true |

Red evidence at 024b404585 (base production bytes): one short `C1986_*` selection combined CP-1/2/3/4/9 to avoid repeated discovery. Total 10, failed 9 on the intended behavior/diagnosis assertions, passed 1 new-branch control; 82 seconds. Build 221 seconds, zero errors, compiler policy disabled. This is the explicit reason for that additional pre-fix selection. Final qualification uses each exact table row. The first plan-only commit's subject mentioned witnesses before their addition; the next two commits contain the actual witnesses.

CP-11/12 cover object types after replacing per-object Git scans with one topological traversal.


CP-1 at 9c511f163f228a487e3c36a0585164433a7705cd passed 4/4 in 62 seconds. The runner was stopped and joined while waiting on load before CP-2; no test child was running. That source was superseded to batch covering-root discovery into one topological graph traversal, avoiding one process/scan per required commit. Final rows rerun against the final source. Stable master/main refs take precedence over ephemeral task branches when either covers an object.

S3: the rebuilt derived-index driver first printed changed (one class in the c590-command.ps1 entry), then unchanged. Commit that generated index alone as the last commit. Final verification builds and executes this table against that committed source. CP-10 subsumes the earlier standalone layering census row.
