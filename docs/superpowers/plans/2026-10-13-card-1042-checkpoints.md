# CARD-1042 Windows clean-receipt checkpoints

## Scope and implementation

S1 adds this checkpoint plan only, retaining the metadata-only implementation at
9e482f37de08b3bbf1f607aa27a5dba6e093535b (original Code task
5fa89e36-d4b5-4081-bc97-989439c0a023). Code task
d7a4f178-1a80-40ab-8730-f0be6b07eead becomes the landing owner.
No test, assertion, classifier, allowlist, tripwire, retry or timeout changes.

The explicit repair brief excludes the Unit lane and limits Final verification to
the classification guard, the four evidence classes minus five inherited Windows
failures, the fresh-TRX tripwire and source-receipt/manual acceptance below.
This bounded selection supersedes the generic Final profile. It does not certify
the excluded methods as green. CARD-1214 Linux partial-clone failures are outside
this Windows slice.

## Verification design

- V-1: TestClassificationGuardTests.Registry_matches_compiled_metadata passes once.
- V-2: CP-2 executes exactly the 36 Selected class/method pairs below, each Passed,
  with no extra, missing, failed or skipped result. The positive method OR operands
  use the pinned runner's required trailing wildcards. The class OR operands name
  only the four affected classes; each method prefix is checked against the full
  source roster and the fresh TRX, so the wildcards cannot conceal extra selection.
- R-1: The unchanged duration tripwire reads this run's fresh CP-2 TRX and reports
  expanded rows=36 and 0 unlisted tests at or above 5 seconds.
- R-2: Both scripts/validate-checkpoint-receipt.ps1 and the checkpoint tool validate
  all three rows against the exact committed SHA, exit 0, with clean source and
  verified TUnit build provenance (command row buildSource=notApplicable).
- Manual acceptance: join fresh TRX result testId to TestDefinitions/UnitTest/
  TestMethod className/name; compare exact sets and outcomes against this roster.
  Verify the five excluded methods are each present with [Test] in their named
  source class. Run check-evidence-diff.ps1 over original implementation base
  94216c6cfd5b60f17ec962e4bf4068198f983a3a..pushed HEAD. Fetch origin/master and
  run git merge-tree --write-tree HEAD origin/master (expected exit 0).

### Inherited exclusions

Final Review task cea60832, report
C:\Antiphon\review-evidence\cea60832\review-report.md, executed all 41 methods:
36 passed, 5 failed, 0 skipped. Each failure reproduced with the same assertion/
error at pre-change base 94216c6cfd5b60f17ec962e4bf4068198f983a3a.
CARD-1215 owns the following exact five exclusions; this task does not repair them:

| Class | Exact excluded method |
|---|---|
| EvidenceApprovedFixtureGuardTests | Rejects_fixture_path_near_misses |
| EvidenceDeletionGuardTests | Refuses_unverifiable_deletion_history |
| EvidenceDiffGuardTests | Refuses_unverifiable_ranges_and_objects |
| EvidenceDiffGuardTests | Handles_literal_paths_and_case_variants |
| EvidenceSupplementalDeletionGuardTests | Refuses_nonrecoverable_deleted_blob |

### Complete source roster

Each row corresponds to one parameterless [Test] method in
tests/Antiphon.Tests/Scripts/<Class>.cs. Totals: ApprovedFixture 7 minus 1 = 6;
Diff 18 minus 2 = 16; Deletion 6 minus 1 = 5; SupplementalDeletion 10 minus 1 = 9.
Total 41 minus exactly 5 = 36 selected, 0 other exclusions.

| Class | Exact method | Disposition |
|---|---|---|
| EvidenceApprovedFixtureGuardTests | Allows_approved_json_fixtures | Selected |
| EvidenceApprovedFixtureGuardTests | Rejects_fixture_path_near_misses | Excluded: CARD-1215 |
| EvidenceApprovedFixtureGuardTests | Rejects_run_output_in_fixture_directory | Selected |
| EvidenceApprovedFixtureGuardTests | Approved_fixtures_obey_checkpoint_exclusion | Selected |
| EvidenceApprovedFixtureGuardTests | Approved_fixtures_obey_regular_mode_constraint | Selected |
| EvidenceApprovedFixtureGuardTests | Approved_fixtures_obey_one_mib_limit | Selected |
| EvidenceApprovedFixtureGuardTests | Deletion_inventory_preserves_approved_fixtures | Selected |
| EvidenceDiffGuardTests | Rejects_checkpoint_paths | Selected |
| EvidenceDiffGuardTests | Rejects_non_markdown_outputs | Selected |
| EvidenceDiffGuardTests | Rejects_non_regular_modes | Selected |
| EvidenceDiffGuardTests | Enforces_one_mib_blob_limit | Selected |
| EvidenceDiffGuardTests | Allows_small_markdown_and_other_source | Selected |
| EvidenceDiffGuardTests | Grandfathers_unchanged_legacy_and_allows_deletion | Selected |
| EvidenceDiffGuardTests | Rechecks_modified_and_renamed_legacy_paths | Selected |
| EvidenceDiffGuardTests | Checks_intermediate_commits_even_when_tip_is_clean | Selected |
| EvidenceDiffGuardTests | Checks_merge_side_history | Selected |
| EvidenceDiffGuardTests | Reads_pinned_git_objects_not_index_or_worktree | Selected |
| EvidenceDiffGuardTests | Handles_literal_paths_and_case_variants | Excluded: CARD-1215 |
| EvidenceDiffGuardTests | Refuses_unverifiable_ranges_and_objects | Excluded: CARD-1215 |
| EvidenceDiffGuardTests | Refuses_failed_or_malformed_git_results | Selected |
| EvidenceDiffGuardTests | Resolves_refs_once_and_stays_read_only | Selected |
| EvidenceDiffGuardTests | Ci_selects_cumulative_feature_push_range | Selected |
| EvidenceDiffGuardTests | Ci_selects_complete_default_branch_push_range | Selected |
| EvidenceDiffGuardTests | Ci_requires_valid_manual_range | Selected |
| EvidenceDiffGuardTests | Ci_refuses_invalid_events_and_missing_history | Selected |
| EvidenceDeletionGuardTests | Verifies_exact_legacy_deletion | Selected |
| EvidenceDeletionGuardTests | Rejects_wrong_inventory | Selected |
| EvidenceDeletionGuardTests | Rejects_ambiguous_or_merge_deletion | Selected |
| EvidenceDeletionGuardTests | Rejects_missing_extra_or_non_deletions | Selected |
| EvidenceDeletionGuardTests | Rejects_changed_legacy_or_resurrected_paths | Selected |
| EvidenceDeletionGuardTests | Refuses_unverifiable_deletion_history | Excluded: CARD-1215 |
| EvidenceSupplementalDeletionGuardTests | Verifies_later_cleanup_without_original_anchor_entries | Selected |
| EvidenceSupplementalDeletionGuardTests | Rejects_extra_deletion | Selected |
| EvidenceSupplementalDeletionGuardTests | Rejects_missing_deletion | Selected |
| EvidenceSupplementalDeletionGuardTests | Rejects_changed_kept_markdown | Selected |
| EvidenceSupplementalDeletionGuardTests | Refuses_nonrecoverable_deleted_blob | Excluded: CARD-1215 |
| EvidenceSupplementalDeletionGuardTests | Rejects_wrong_inventory_or_cleanup_label | Selected |
| EvidenceSupplementalDeletionGuardTests | Rejects_wrong_ambiguous_or_merge_marker | Selected |
| EvidenceSupplementalDeletionGuardTests | Rejects_non_deletion_changes | Selected |
| EvidenceSupplementalDeletionGuardTests | Rejects_changed_inventory_entries | Selected |
| EvidenceSupplementalDeletionGuardTests | Rejects_resurrection_and_new_final_artifacts | Selected |

### Mutation obligations

No new executable guard or PC is introduced by this documentation repair.
All inherited PCs/variants remain pending for separately commissioned
method-scoped SourceLanding Mutation; ordinary green does not discharge them.
In particular CARD-1036 PC-1 through PC-7 remain pending, including PC-2 whose
ordinary method is excluded here under CARD-1215. Mutation owns any missing-control
discovery. Post-land Mutation was NOT run.

### Cost

One isolated test-project build is shared by CP-1 and CP-2. No full assembly run.
Ordinary floor: 4 + 19 + 1 = 24 minutes, based on the previous Windows Review's
approximately 19-minute four-class run; authoring/reporting fit the 30-minute brief.
One execution per row, no repeats after green. The separate slot-gated tool
bootstrap is infrastructure, explicitly disclosed as an unlisted build.
Use default Windows apphost behavior; do not set UseAppHost=false.
Do not widen existing assertion or execution deadlines.

### Checkpoints

| CP | After | Build | Group | Filter | Covers | Expect | Min | EstimatedMinutes | Serial |
|---|---|---|---|---|---|---|---:|---:|---|
| CP-1 | S1 | `tests/Antiphon.Tests -> bin-c1042-repair-d7a4f178/` | classification | `/*/*/TestClassificationGuardTests/*` | V-1 | exactly 1 passed, 0 failed/skipped | 1 | 4 | true |
| CP-2 | S1 | CP-1 | evidence-policy | `/*/*/(EvidenceApprovedFixtureGuardTests*)\|(EvidenceDiffGuardTests*)\|(EvidenceDeletionGuardTests*)\|(EvidenceSupplementalDeletionGuardTests*)/(Allows_approved_json_fixtures*)\|(Rejects_run_output_in_fixture_directory*)\|(Approved_fixtures_obey_checkpoint_exclusion*)\|(Approved_fixtures_obey_regular_mode_constraint*)\|(Approved_fixtures_obey_one_mib_limit*)\|(Deletion_inventory_preserves_approved_fixtures*)\|(Rejects_checkpoint_paths*)\|(Rejects_non_markdown_outputs*)\|(Rejects_non_regular_modes*)\|(Enforces_one_mib_blob_limit*)\|(Allows_small_markdown_and_other_source*)\|(Grandfathers_unchanged_legacy_and_allows_deletion*)\|(Rechecks_modified_and_renamed_legacy_paths*)\|(Checks_intermediate_commits_even_when_tip_is_clean*)\|(Checks_merge_side_history*)\|(Reads_pinned_git_objects_not_index_or_worktree*)\|(Refuses_failed_or_malformed_git_results*)\|(Resolves_refs_once_and_stays_read_only*)\|(Ci_selects_cumulative_feature_push_range*)\|(Ci_selects_complete_default_branch_push_range*)\|(Ci_requires_valid_manual_range*)\|(Ci_refuses_invalid_events_and_missing_history*)\|(Verifies_exact_legacy_deletion*)\|(Rejects_wrong_inventory*)\|(Rejects_ambiguous_or_merge_deletion*)\|(Rejects_missing_extra_or_non_deletions*)\|(Rejects_changed_legacy_or_resurrected_paths*)\|(Verifies_later_cleanup_without_original_anchor_entries*)\|(Rejects_extra_deletion*)\|(Rejects_missing_deletion*)\|(Rejects_changed_kept_markdown*)\|(Rejects_wrong_inventory_or_cleanup_label*)\|(Rejects_wrong_ambiguous_or_merge_marker*)\|(Rejects_non_deletion_changes*)\|(Rejects_changed_inventory_entries*)\|(Rejects_resurrection_and_new_final_artifacts*)` | V-2 | exactly the 36 Selected roster pairs, 0 failed/skipped | 36 | 19 | true |
| CP-3 | S1 | n/a | duration-tripwire | `for /f %r in (.antiphon\checkpoints\latest) do pwsh -NoProfile -File scripts\test-duration-tripwire.ps1 -Trx .antiphon\checkpoints\%r\rows\CP-2\run.trx` | R-1 | exit 0; expanded rows=36; 0 unlisted tests >= 5 s | n/a | 1 | true |

## Execution and handoff

Commit S1 before execution, pass that full HEAD as --expected-source-sha, and keep
source frozen until every row finishes. Run the table once through the checkpoint
tool with --serial; keep waiting while exit is 75. CP-3 resolves the fresh run ID
from the tool-owned latest pointer written by this invocation; do not start another
checkpoint run in this worktree concurrently. Inspect both TRX files and CP-3 log.
Validate both receipt implementations; record unedited CHECKPOINT lines and actual
tested SHA in the ignored stored task report after qualification. Delete only
producer-owned alternate outputs, keeping ignored TRX/receipts/logs as evidence.
Commit/push the plan; next is Windows Final Review asserting clean source.
Restart: none.