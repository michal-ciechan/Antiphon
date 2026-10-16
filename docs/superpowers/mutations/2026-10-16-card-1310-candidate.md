# CARD-1310 unbound Mutation candidate

Original Code owner: a5c09b23-f3a1-4956-a4bd-221d9bbf7628.
Plan: docs/superpowers/plans/2026-10-16-card-1310-coverage-mapping.md.
All controls below remain pending method-scoped SourceLanding Mutation. No deliberate
mutant was run by Code. Bind to the published source before execution.

| PC | Guard method | Mutation variants | Required red |
|---|---|---|---|
| PC-1 | CheckpointCoverageMappingTests.Build_append_is_sealed_and_green_output_is_removed | omit mapping seal; ignore final content | cleanup success assertion |
| PC-2 | CheckpointCoverageMappingTests.Mapping_authority_rejects_non_build_appends | omit prefix check; omit identity check; omit exit/window; allow reseal; ignore final bytes/stat | preservation/refusal for rewrite, replace, late, no-exit, after-seal, reseal |
| PC-3 | CheckpointCoverageMappingTests.Mapping_append_does_not_authorize_unrelated_entries | generalize mapping exception; trust disk inventory; bypass identity/hash, link, hardlink or Git guard | preservation/refusal for changed, added, replaced, replaced-identical, link, hardlink, tracked |
| PC-4 | CheckpointCoverageMappingNativeTests | omit Windows custody; omit owner-only root check; omit exit bound | exact Native_build_append_and_child_output_are_sealed_and_removed, Windows_root_with_other_writers_does_not_adopt_child_output, Native_child_write_after_build_exit_is_retained methods respectively |
| PC-5 | CheckpointCoverageMappingNativeTests.Windows_copied_old_timestamps_inside_creation_window_are_adopted | restore the Windows change-time lower bound | copied-output adoption/cleanup assertion |
| PC-6 | CheckpointCoverageMappingNativeTests.Windows_file_created_before_root_is_retained | omit the Windows creation-time lower bound | refusal/preservation assertion |
| PC-7 | CheckpointCoverageMappingNativeTests.Large_child_output_is_streamed_and_tail_changes_are_detected | restore 64 MiB output cap; materialize output bytes; omit final chunk from hash | adoption, allocation-bound or tail-change assertion respectively |
| PC-8 | CheckpointCoverageMappingTests.Output_above_one_gib_ceiling_is_refused_before_streaming | remove/raise opened-handle output ceiling | refusal and zero stream-open assertion |
| PC-9 | CheckpointConfinementLedgerTests.Large_ignored_output_is_hashed_and_tail_rewrite_is_refused | restore capped receipt reader in ledger; omit content hash/tail; skip strict admission or final rehash | unchanged-ledger or changed-tail assertions |

Mutation owns additional missing-control discovery, native Windows variants, and
red/restore/green. Do not widen deadlines or weaken assertions. Restart: none.
