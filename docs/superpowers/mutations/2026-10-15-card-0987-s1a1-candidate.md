# CARD-0987 S1a1 Mutation candidate (unbound)

Code/landing owner: `dc2ddbc6-b317-420e-aacd-ca0d1e92750d`.
Scope: S1a1 and CARD-1302 item 1 only.
Frozen authority: [test design](../plans/2026-10-15-card-0987-test-design.md).
Evidence: [Code evidence](../evidence/2026-10-15-card-0987-s1a1-code.md).

All **31** S1a1 controls below are **PENDING**, not mutation-qualified.
Every PC-1..413 and every variant in the full design remains pending; this
candidate does not discharge or replace the other slices. SourceLanding Mutation
owns deliberate mutants, method-scoped red/restore/green and missing-control
discovery after ordinary Review and landing the original Code task.

All methods are in `tests/Antiphon.Tests/Infrastructure/HostCleanupCheckpointReceiptTests.cs`.
For each row the bound pack must use exactly
`/*/*/HostCleanupCheckpointReceiptTests/<ExactMethod>`, with no wildcard, class
selection or whole suite. Assertions use
`C987.<ExactMethod>.<variant>`; primitive entry observations and expected refusal
strings distinguish the intended guard from later vetoes. This is an unbound
candidate, not an executable bound pack or a claim of a completed PC cycle.

Production bindings:
- PC-313..315 and PC-413: `ReceiptFile.ReadBytes/ReadText` and `ReceiptJson.Read`
  in `tools/Antiphon.Checkpoints/Evidence/ReceiptFile.cs`; the reader policy remains real.
- PC-316..324 and PC-412: `ReceiptJson.Parse/Validate/Walk` in that same file.
- PC-325..335 and PC-341: context overload of `ReportWriter.RequiredReceiptRefusal`
  in `tools/Antiphon.Checkpoints/Report/ReportWriter.cs`.
- PC-336..339: context overload of `ReportWriter.WriteFiles`.
- PC-340: `RunStateStore.TryRead` in `tools/Antiphon.Checkpoints/State/RunStateStore.cs`.

| PC (all pending) | Frozen compiling defect | Exact detecting method and variant | Decisive oracle |
|---|---|---|---|
| PC-313 | Replace verified reader call with injected raw path-byte read. | `HostCleanupCheckpointReceiptTests.Receipt_types_are_checked_before_open` (`wrapper-route`) | verified-reader entry count one and raw-path-read count zero for receipt table. |
| PC-314 | Add a second raw path read in ReceiptJson.Read. | `HostCleanupCheckpointReceiptTests.Receipt_read_uses_verified_handle` (`parser-route`) | parsed RunId is verified-run after path swap, never replacement-run. |
| PC-315 | Pass int.MaxValue as receipt limit. | `HostCleanupCheckpointReceiptTests.Receipt_size_and_deadline_are_bounded` (`receipt-cap`) | 64-MiB-plus-one refused; exactly 64 MiB reader control completes. |
| PC-316 | Omit resolver property.IsRequired assignment. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`missing-reference`) | missing rows/builds/source fields produce receipt-shape refusal. |
| PC-317 | Set RespectNullableAnnotations false. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`serializer-null`) | explicit-null Parse throws JsonException with the field JSON Path; downstream Walk-only rejection has no such Path. |
| PC-318 | Remove NullabilityState.NotNull check in Walk. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`validator-null`) | Validate of an object with null required reference throws receipt-shape: null before report policy. |
| PC-319 | Skip null-element rejection in Walk. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`collection-null`) | null element yields receipt-shape; mutation-entry counter zero. |
| PC-320 | Skip whitespace-element rejection only. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`collection-blank`) | blank outputPaths item yields receipt-shape. |
| PC-321 | Skip Id whitespace check. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`empty-id`) | receipt-identity for empty/whitespace Id, with ordinary nonempty strings valid. |
| PC-322 | Remove required-string whitespace branch. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`required-string`) | each single-field blank variant yields receipt-shape; positive pair accepted. |
| PC-323 | Remove ReportModel OutputNames/OutputPaths cross-field check. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`output-pair`) | names-nonempty paths-empty gives receipt-shape. |
| PC-324 | Remove Walk depth bound. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`depth`) | directly validated depth-65 object gives receipt-shape: depth limit. |
| PC-325 | Skip state.ToolOwner comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`state-owner`) | receipt-run-directory-mismatching with foreign owner. |
| PC-326 | Skip state.RunId path-leaf comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`state-run`) | receipt-run-directory-mismatching for sibling run ID. |
| PC-327 | Bypass state.Source.Start null check and continue with empty observation. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`state-source`) | state-receipt-source-unreadable when source start missing. |
| PC-328 | Skip MarkdownSha256 comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`markdown-digest`) | receipt-mismatching: report.md after same-size markdown rewrite. |
| PC-329 | Substitute zero for null state.ExitCode. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`terminal-exit`) | receipt-unreadable-or-terminal-exit-missing for null exit. |
| PC-330 | Skip report.RunId comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`report-run`) | receipt-stale-or-source-mismatching with only report RunId changed. |
| PC-331 | Skip report.ExitCode comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`report-exit`) | receipt-stale-or-source-mismatching with only report exit changed. |
| PC-332 | Skip report.Commit comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`commit`) | receipt-stale-or-source-mismatching with report commit changed. |
| PC-333 | Skip serialized source-equality comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`source-object`) | receipt-stale-or-source-mismatching with same commit but changed fingerprint/state. |
| PC-334 | Skip binding.Commit comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`build-stamp-commit`) | receipt-source-mismatching for stamp wrong commit. |
| PC-335 | Skip binding.Fingerprint comparison. | `HostCleanupCheckpointReceiptTests.Terminal_receipt_requires_digest_source_and_exit` (`build-stamp-fingerprint`) | receipt-source-mismatching for stamp wrong fingerprint. |
| PC-336 | Ignore markdownRefusal. | `HostCleanupCheckpointReceiptTests.Report_writes_require_verified_readback` (`markdown-write`) | required report.md receipt-write-failed rather than successful return. |
| PC-337 | Ignore jsonRefusal. | `HostCleanupCheckpointReceiptTests.Report_writes_require_verified_readback` (`json-write`) | required report.json receipt-write-failed rather than successful return. |
| PC-338 | Skip markdown equality check. | `HostCleanupCheckpointReceiptTests.Report_writes_require_verified_readback` (`markdown-readback`) | required report receipt readback mismatching after injected markdown alteration. |
| PC-339 | Skip JSON equality check. | `HostCleanupCheckpointReceiptTests.Report_writes_require_verified_readback` (`json-readback`) | required report receipt readback mismatching after injected JSON alteration. |
| PC-340 | Return a synthetic done RunState from TryRead on IOException. | `HostCleanupCheckpointReceiptTests.Receipt_read_errors_do_not_write_or_adopt` (`swallowed-read`) | TryRead returns null plus state-receipt-unreadable; write/adopt count zero. |
| PC-341 | Return receipt-mismatching unconditionally. | `HostCleanupCheckpointReceiptTests.Valid_terminal_receipt_is_accepted` (`positive-terminal`) | RequiredReceiptRefusal null for valid exit 0 and 1 pairs. |
| PC-412 | Skip Validate ReportModel.MarkdownSha256 whitespace check. | `HostCleanupCheckpointReceiptTests.Receipt_shape_and_identity_are_required` (`markdown-required`) | Validate otherwise-valid report with blank digest throws receipt-shape: markdownSha256. |
| PC-413 | Skip Windows FileAttributes prefilter. | `HostCleanupCheckpointReceiptTests.Receipt_types_are_checked_before_open` (`windows-preopen`) | Windows directory/reparse variant has verified-open attempts zero and receipt-nonregular. |

Additional discovery candidates, with no invented frozen PC IDs:
`HostCleanupCheckpointPathTests.Containment_trims_the_candidate_before_comparison`
guards candidate trailing-separator normalization; `Invalid_paths_refuse_without_throwing`
guards invalid candidate and root refusal. Both have ordinary red-first evidence,
but that is not a SourceLanding Mutation result. Also inspect the newly threaded
Markdown output observation, state history write/append and cancellation forwarding
for missing controls. Do not weaken any assertion or timeout.
