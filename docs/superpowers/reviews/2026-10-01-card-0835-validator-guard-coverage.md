# CARD-0835 validator guard and plan-name coverage

Audit at the Final Code follow-up. Each row identifies the refusal reached by an independently changed fixture; the label is in the test's failure output. `Test-CheckpointSourceEvidence` and `ReportValidator.IsSourceEligible` return a Boolean, so their reason column is the enclosing validator's refusal code. Source locations are the pre-edit production line numbers at `03df6c6d` and remain unchanged on this test-only branch.

| Guard file:line | Refusal reason | Detecting test and assertion label |
|---|---|---|
| `scripts/validate-checkpoint-receipt.ps1:16` | `duplicate_json_property` | V-10 `duplicate-json-property` |
| `scripts/validate-checkpoint-receipt.ps1:29` | `duplicate_receipt_token` | V-10 `duplicate-receipt-token` |
| `scripts/validate-checkpoint-receipt.ps1:36` | `expected_sha_invalid` | V-10 `expected-sha-invalid` |
| `scripts/validate-checkpoint-receipt.ps1:43` | `legacy_report` | V-16 `script-tool-legacy-parity` |
| `scripts/validate-checkpoint-receipt.ps1:48,50` | `selected_rows_missing` | V-16 `script-selected-rows-missing`, `script-empty-rows-missing` |
| `scripts/validate-checkpoint-receipt.ps1:53-54` | `row_failed` | V-16 `script-tool-failed-verdict-parity`, `script-tunit-failed-counts` |
| `scripts/validate-checkpoint-receipt.ps1:55` | `row_source_ineligible` | V-16 `row-source-ineligible` |
| `scripts/validate-checkpoint-receipt.ps1:56-58` | `row_heading_disagreement` | V-16 `script-fingerprint-row-heading-parity`, `script-build-row-heading-parity` |
| `scripts/validate-checkpoint-receipt.ps1:60-62` | `receipt_disagreement` | V-16 `row-receipt-missing`, `row-receipt-source` |
| `scripts/validate-checkpoint-receipt.ps1:64-66` | `receipt_counts` | V-16 `script-receipt-count-disagreement` |
| `scripts/validate-checkpoint-receipt.ps1:69` | `source_ineligible` | V-16 `report-heading-source` |
| `scripts/validate-checkpoint-receipt.ps1:71` | `source_ineligible` | V-10 `dirty-source-clean-receipt-ineligible` |
| `scripts/validate-checkpoint-receipt.ps1:72` | `receipt_failed` | V-10 `selected-receipt-ineligible` |
| `scripts/validate-checkpoint-receipt.ps1:74` | `receipt_name` | V-10 `receipt-name` |
| `scripts/validate-checkpoint-receipt.ps1:75` | `receipt_disagreement` | V-10 `receipt-sha-disagreement` |
| `scripts/validate-checkpoint-receipt.ps1:76-78` | `receipt_counts` | V-10 `receipt-counts` |
| `scripts/lib/checkpoint-source.ps1:155` | Boolean false, then `source_ineligible` | V-10 direct helper `source-helper-expected-sha-shape`; the public script's earlier SHA guard would otherwise mask this check |
| `scripts/lib/checkpoint-source.ps1:156` | `source_ineligible` | V-10 `schema-version`, `missing-end`, `legacy-receipt-ineligible` |
| `scripts/lib/checkpoint-source.ps1:157` | `source_ineligible` | V-10 `start-capture-unknown`, `end-capture-unknown` |
| `scripts/lib/checkpoint-source.ps1:158` | `source_ineligible` | V-10 `wrong-sha`, `end-sha` |
| `scripts/lib/checkpoint-source.ps1:159` | `source_ineligible` | V-10 `dirty-source-clean-receipt-ineligible`, `end-dirty`, `state-dirty`; PC-11B red at the first label |
| `scripts/lib/checkpoint-source.ps1:160` | `source_ineligible` | V-10 `fingerprint-shape`, `fingerprint-disagreement` |
| `scripts/lib/checkpoint-source.ps1:161` | `source_ineligible` | V-10 `build-source-mismatch`; V-16 valid command control covers `notApplicable` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:12-13` | Boolean false, then `report_source_ineligible` or `row_source_disagreement` | V-16 `tool-invalid-expected-sha`, `tool-null-source`, `tool-source-version`, `tool-source-end-missing` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:16-21` | Boolean false, then `report_source_ineligible` or `row_source_disagreement` | V-16 `tool-source-state`, `tool-source-build-binding`, `tool-source-start-capture`, `tool-source-end-capture`, `tool-source-start-commit`, `tool-source-end-commit`, `tool-source-start-dirty`, `tool-source-end-dirty`, `tool-source-fingerprint-shape`, `tool-source-fingerprint-match` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:26-27` | `report_source_ineligible` | V-16 `legacy-report-ineligible`, `report-heading-source`, `tool-report-commit` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:31-32` | `selected_rows_missing` | V-16 `tool-selected-rows-missing`, `tool-empty-rows-missing` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:35-37` | `row_failed` | V-16 `tool-row-failed`, `tool-tunit-failed-counts` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:38-41` | `row_source_disagreement` | V-16 `row-source-ineligible`, `fingerprint-row-heading-disagreement`, `build-row-heading-disagreement` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:42-43` | `row_receipt_missing` | V-16 `row-receipt-missing` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:45-48` | `duplicate_receipt_token` | V-16 `tool-duplicate-receipt-token` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:49-54` | `receipt_source_disagreement` | V-16 `row-receipt-source` |
| `tools/Antiphon.Checkpoints/Report/ReportValidator.cs:55-57` | `receipt_count_disagreement` | V-16 `tool-receipt-count-disagreement` |

The two required single-line mutations were run separately against V-10 and restored with `git diff --exit-code`: removing `checkpoint-source.ps1:159` failed at `dirty-source-clean-receipt-ineligible`; removing `validate-checkpoint-receipt.ps1:72` failed at `selected-receipt-ineligible`. PC-27A and PC-27B each failed both hidden-index methods at their named tool or script assertion. These are focused Code proofs; the complete 37-variant SourceLanding Mutation inventory remains pending.

## Programmatic plan-name coverage

The check extracts V-matrix class/method names, backtick-quoted PC witness labels after each `V-n:` clause, and checkpoint filter class or method operands, then searches the selected test roots for each literal. Duplicated names are kept by plan row. It found 76 names and zero absent names. Locations below identify the matching declaration or literal.

| Plan entry | Name | Test file:line | Found |
|---|---|---|---|
| V-1 | `CheckpointSourceStateTests.clean_and_ignored_outputs_match_head` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:36` | yes |
| V-2 | `CheckpointSourceStateTests.index_and_worktree_edits_are_dirty` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:73` | yes |
| V-3 | `CheckpointSourceStateTests.untracked_contents_and_paths_affect_identity` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:128` | yes |
| V-4 | `CheckpointSourceStateTests.failed_or_unstable_capture_is_unknown` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:153` | yes |
| V-5 | `CheckpointSourceStateTests.script_and_tool_snapshots_agree` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:205` | yes |
| V-6 | `RunCheckpointSourceScriptTests.C835_DiagnosticReceipts` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:17` | yes |
| V-7 | `RunCheckpointSourceScriptTests.C835_StrictAdmission` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:46` | yes |
| V-8 | `RunCheckpointSourceScriptTests.C835_DriftAndReuse` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:93` | yes |
| V-9 | `RunCheckpointSourceScriptTests.C835_TerminalEvidence` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:175` | yes |
| V-10 | `RunCheckpointSourceScriptTests.C835_ReceiptValidation` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:205` | yes |
| V-11 | `CheckpointSourceExecutionTests.clean_and_dirty_runs_publish_bound_source` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:21` | yes |
| V-12 | `CheckpointSourceExecutionTests.changed_admission_and_driver_boundaries_refuse` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:79` | yes |
| V-13 | `CheckpointSourceExecutionTests.strict_cli_and_reuse_require_clean_binding` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:201` | yes |
| V-14 | `CheckpointSourceExecutionTests.terminal_paths_do_not_invent_clean_evidence` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:251` | yes |
| V-15 | `CheckpointSourceExecutionTests.merge_cannot_launder_source_identity` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:296` | yes |
| V-16 | `CheckpointSourceExecutionTests.validation_requires_complete_consistent_source` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:320` | yes |
| V-17 | `ReviewEvidenceParserTests.C835_SourceCleanGrammar` | `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs:227` | yes |
| V-18 | `ReviewEvidenceParserTests.C835_SourceStateCannotBeInferred` | `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs:249` | yes |
| V-19 | `CheckpointSourceApprovalTests.settlement_persists_source_assertion` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:25` | yes |
| V-20 | `CheckpointSourceApprovalTests.land_admission_requires_clean_review_source` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:51` | yes |
| V-21 | `CheckpointSourceApprovalTests.recovery_and_resume_recheck_source_assertion` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:87` | yes |
| V-22 | `CheckpointSourceApprovalTests.migration_and_override_preserve_unknown` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:347` | yes |
| V-23 | `DelegateScriptLandApprovalTests.C835_FindingSourceCleanIsExplicit` | `tests/Antiphon.Tests/Application/DelegateScriptLandApprovalTests.cs:76` | yes |
| PC-1A | `ignored-output-stability` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:66` | yes |
| PC-1B | `tracked-output-must-count` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:69` | yes |
| PC-2 | `index-only-content-change` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:106` | yes |
| PC-3 | `untracked-content-digest` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:140` | yes |
| PC-4 | `failed-git-is-unknown` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:156` | yes |
| PC-5 | `fixed-vector-parity` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:60` | yes |
| PC-6 | `dirty-receipt-agrees-with-source-json` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:32` | yes |
| PC-7A | `dirty-preflight-no-lease` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:67` | yes |
| PC-7B | `wrong-sha-no-lease` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:56` | yes |
| PC-7C | `slot-edit-no-driver` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:86` | yes |
| PC-8 | `driver-drift-is-changed` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:143` | yes |
| PC-9A | `invalid-stamp-no-tests` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:119` | yes |
| PC-9B | `failed-rebuild-invalidates-stamp` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:134` | yes |
| PC-10 | `interruption-has-no-observed-end` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:197` | yes |
| PC-11A | `legacy-receipt-ineligible` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:222` | yes |
| PC-11B | `dirty-source-clean-receipt-ineligible` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:234` | yes |
| PC-11C | `selected-receipt-ineligible` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:258` | yes |
| PC-12 | `dirty-row-preserved` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:34` | yes |
| PC-13 | `queued-source-not-readmitted` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:86` | yes |
| PC-14A | `driver-drift-stops-next-row` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:160` | yes |
| PC-14B | `rerun-cannot-certify-drift` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:194` | yes |
| PC-15A | `strict-cli-refuses-wrong-sha` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:239` | yes |
| PC-15B | `property-mismatch-no-tests` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:217` | yes |
| PC-16 | `executor-error-has-no-observed-end` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:259` | yes |
| PC-17 | `old-dirty-row-not-relabeled` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:304` | yes |
| PC-18A | `legacy-report-ineligible` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:331` | yes |
| PC-18B | `row-heading-disagreement` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:345` | yes |
| PC-19 | `duplicate-clean-is-unknown` | `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs:244` | yes |
| PC-20 | `full-scope-is-not-source-clean` | `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs:252` | yes |
| PC-21 | `settled-source-assertion-roundtrip` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:37` | yes |
| PC-22 | `unclean-evidence-no-request` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:72` | yes |
| PC-22 | `review_evidence_source_not_clean` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:72` | yes |
| PC-23 | `unlatched-resume-refuses-unclean` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:137` | yes |
| PC-24 | `legacy-source-clean-remains-null` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:378` | yes |
| PC-25 | `override-does-not-inherit-true` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:412` | yes |
| PC-26 | `finding-json-preserves-explicitness` | `tests/Antiphon.Tests/Application/DelegateScriptLandApprovalTests.cs:104` | yes |
| PC-27A | `assume-unchanged-tool-hidden` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:268` | yes |
| PC-27A | `skip-worktree-tool-hidden` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:268` | yes |
| PC-27B | `assume-unchanged-script-hidden` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:277` | yes |
| PC-27B | `skip-worktree-script-hidden` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:277` | yes |
| CP-1 | `CheckpointSourceStateTests` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceStateTests.cs:16` | yes |
| CP-2 | `RunCheckpointSourceScriptTests` | `tests/Antiphon.Tests/Scripts/RunCheckpointSourceScriptTests.cs:14` | yes |
| CP-3 | `RunCheckpointScriptTests` | `tests/Antiphon.Tests/Scripts/RunCheckpointScriptTests.cs:14` | yes |
| CP-4 | `CheckpointSourceExecutionTests` | `tests/Antiphon.Tests/Checkpoints/CheckpointSourceExecutionTests.cs:14` | yes |
| CP-5 | `CheckpointLineTests` | `tests/Antiphon.Tests/Checkpoints/CheckpointLineTests.cs:9` | yes |
| CP-6 | `ReviewEvidenceParserTests` | `tests/Antiphon.Tests/Application/ReviewEvidenceParserTests.cs:9` | yes |
| CP-7 | `CheckpointSourceApprovalTests` | `tests/Antiphon.Tests/Application/CheckpointSourceApprovalTests.cs:22` | yes |
| CP-8 | `AgentTaskReviewEvidenceTests` | `tests/Antiphon.Tests/Application/AgentTaskReviewEvidenceTests.cs:16` | yes |
| CP-9 | `AgentTaskLandApprovalRequestTests` | `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRequestTests.cs:20` | yes |
| CP-10 | `AgentTaskLandApprovalPersistenceTests` | `tests/Antiphon.Tests/Application/AgentTaskLandApprovalPersistenceTests.cs:21` | yes |
| CP-11 | `DelegateScriptLandApprovalTests` | `tests/Antiphon.Tests/Application/DelegateScriptLandApprovalTests.cs:10` | yes |
| CP-12 | `AgentTaskLandApprovalRecoveryTests` | `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRecoveryTests.cs:17` | yes |
| CP-12 | `C488_OriginalApprovalNeverAdoptsHead` | `tests/Antiphon.Tests/Application/AgentTaskLandApprovalRecoveryTests.cs:238` | yes |
